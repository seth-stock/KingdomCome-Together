// SPDX-License-Identifier: GPL-3.0-only
#pragma once
#include <array>
#include <cstdint>
#include <mutex>
#include <cmath>
#include <algorithm>

namespace kcdmp::combatwatch {
struct Target {
    uint32_t eid = 0;
    void* soul = nullptr;
    uint64_t generation = 0;
    explicit operator bool() const { return eid && soul && generation; }
};

// Hit hooks can run off the main thread. Publish/read the whole identity
// atomically, including its lifetime. Two independent atomic fields are not
// a coherent entity/soul snapshot, and numeric IDs and pointers can be reused.
class Targets {
    mutable std::mutex mutex_;
    std::array<Target, 16> slots_{};
    uint64_t next_ = 0;
public:
    Target find(uint32_t eid) const {
        if (!eid) return {};
        std::lock_guard<std::mutex> lock(mutex_);
        for (const auto& t : slots_) if (t.eid == eid) return t;
        return {};
    }
    void note(uint32_t eid, void* soul, bool on) {
        if (!eid) return;
        std::lock_guard<std::mutex> lock(mutex_);
        for (auto& t : slots_) if (t.eid == eid) {
            if (!on || !soul) t = {};
            else if (t.soul != soul) t = {eid, soul, ++next_};
            return;
        }
        if (on && soul) for (auto& t : slots_) if (!t.eid) {
            t = {eid, soul, ++next_};return;
        }
    }
    bool live(const Target& expected) const {
        const auto current = find(expected.eid);
        return expected && current.soul == expected.soul && current.generation == expected.generation;
    }
    int list(uint32_t* eids, void** souls, int max) const {
        std::lock_guard<std::mutex> lock(mutex_);
        int n = 0;
        for (const auto& t : slots_) if (t && n < max) {
            eids[n] = t.eid;souls[n] = t.soul;++n;
        }
        return n;
    }
};

// A successful reflected invocation does not prove that SetState changed
// anything. Forward only the part of a measured drop actually put back.
inline float restored_amount(float baseline, float before, float after, bool writeOk, bool readOk) {
    if (!writeOk || !readOk || !std::isfinite(baseline) || !std::isfinite(before) || !std::isfinite(after)) return 0;
    return (std::max)(0.0f, (std::min)(baseline - before, after - before));
}
inline bool fully_restored(float baseline, float after, float tolerance, bool writeOk, bool readOk) {
    return writeOk && readOk && std::isfinite(baseline) && std::isfinite(after) && std::abs(baseline - after) <= tolerance;
}
}
