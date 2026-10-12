// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// Session 10 -- see xpread.h.
#include "xpread.h"
#include "anchors.h"
#include "fault_guard.h"
#include "log.h"
#include "wo137.h"

#include <windows.h>
#include <atomic>
#include <cstdio>
#include <cstring>
#include <mutex>

namespace kcdmp::xpread {

namespace {

constexpr const char* kModule   = "RPGModule.dll";
constexpr const char* kRttiStat = ".?AVC_StatXPEffect@rpgmodule@wh@@";
constexpr const char* kRttiSkill = ".?AVC_SkillXPEffect@rpgmodule@wh@@";
constexpr const char* kStatApplyName  = "wh::rpgmodule::C_SoulValueEffect<enum wh::rpgmodule::E_SoulStat,struct fixed_t<unsigned int,25> >::Apply";
constexpr const char* kSkillApplyName = "wh::rpgmodule::C_SoulValueEffect<enum wh::rpgmodule::E_SoulSkill,struct fixed_t<unsigned int,25> >::Apply";
constexpr size_t kApplySlot = 3;

constexpr size_t kOffSoul = 0x28, kOffIndex = 0x30, kOffRequested = 0x34, kOffApplied = 0x38, kOffBefore = 0x3C;
constexpr int kFrames = 8, kRing = 256, kPerReply = 12;
constexpr const char* kQuestStatName  = "wh::rpgmodule::C_RPGQuest::OnStatXPReward";
constexpr const char* kQuestSkillName = "wh::rpgmodule::C_RPGQuest::OnSkillXPReward";

struct Row {
    uint32_t seq = 0;
    uint8_t kind = 0;
    uint64_t soul = 0;
    uint32_t index = 0, requested = 0, applied = 0, before = 0;
    uint64_t tick = 0;
    uint8_t cause = 0;                 // 0 other, 1 a quest's stat reward, 2 a quest's skill reward
    uint32_t root = 0, tag = 0;        // wo137::current_root_seq / current_apply_tag at the moment of the Apply
};

using ApplyFn = uint64_t (*)(void*);

std::mutex g_m;
Row g_ring[kRing];
uint32_t g_next = 1;                       // the seq the next row takes
std::atomic<bool> g_armed{false};
std::atomic<uint32_t> c_rows{0}, c_faults{0}, c_logged{0};
ApplyFn g_origStat = nullptr, g_origSkill = nullptr;
void* const* g_vftStat = nullptr;
void* const* g_vftSkill = nullptr;
HMODULE g_mod = nullptr;
// C_RPGQuest::OnStatXPReward / OnSkillXPReward, found by their trace strings. The compiler splits these functions into chained
// unwind fragments (the stat handler's primary range is 212 of its 466 bytes), so a frame is matched by the PRIMARY entry of the
// function that holds it, not by a byte range.
const uint8_t* g_questFn[2] = {nullptr, nullptr};
anchor::Range g_text;
bool g_questKnown[2] = {false, false};
std::atomic<uint32_t> c_quest{0};

void module_name(HMODULE m, char* out, size_t n) {
    char path[MAX_PATH]{};
    if (!m || !GetModuleFileNameA(m, path, sizeof path)) { std::snprintf(out, n, "?"); return; }
    const char* base = std::strrchr(path, '\\');
    std::snprintf(out, n, "%s", base ? base + 1 : path);
}

void record(void* self, uint8_t kind) {
    Row r{};
    KCDMP_FAULT_READ(site, "xpread::record");
    const bool ok = fault::guarded(site, [&] {
        const char* b = static_cast<const char*>(self);
        std::memcpy(&r.soul, b + kOffSoul, 8);
        std::memcpy(&r.index, b + kOffIndex, 4);
        std::memcpy(&r.requested, b + kOffRequested, 4);
        std::memcpy(&r.applied, b + kOffApplied, 4);
        std::memcpy(&r.before, b + kOffBefore, 4);
    });
    if (!ok) { c_faults.fetch_add(1); return; }
    r.kind = kind;
    r.tick = GetTickCount64();
    void* frames[kFrames]{};
    const USHORT got = RtlCaptureStackBackTrace(2, kFrames, frames, nullptr);   // skip record() and the hook
    for (USHORT i = 0; i < got && !r.cause; ++i) {
        if (!g_text.contains(frames[i])) continue;
        anchor::Range fr{};
        if (!anchor::function_range(g_mod, frames[i], &fr)) continue;
        if (g_questKnown[0] && fr.begin == g_questFn[0]) r.cause = 1;
        else if (g_questKnown[1] && fr.begin == g_questFn[1]) r.cause = 2;
    }
    r.root = wo137::current_root_seq();
    r.tag = wo137::current_apply_tag();
    {
        std::lock_guard<std::mutex> lk(g_m);
        r.seq = g_next++;
        g_ring[r.seq % kRing] = r;
    }
    c_rows.fetch_add(1);
    if (r.cause) c_quest.fetch_add(1);
    if (r.cause || c_logged.fetch_add(1) < 60) {
        char fr[400]{}; size_t at = 0;
        for (USHORT i = 0; i < got && at + 60 < sizeof fr; ++i) {
            HMODULE m = nullptr; char nm[64];
            if (GetModuleHandleExA(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS | GET_MODULE_HANDLE_EX_FLAG_UNCHANGED_REFCOUNT,
                                   static_cast<LPCSTR>(frames[i]), &m) && m) {
                module_name(m, nm, sizeof nm);
                at += std::snprintf(fr + at, sizeof fr - at, " %s+0x%llX", nm,
                                    static_cast<unsigned long long>(static_cast<const char*>(frames[i]) - reinterpret_cast<const char*>(m)));
            }
        }
        logf("XPREAD #%u %s soul=%016llX idx=%u req=%u applied=%u before=%u cause=%u root=%u tag=%u |%s", r.seq, kind ? "skill" : "stat",
             static_cast<unsigned long long>(r.soul), r.index, r.requested, r.applied, r.before, r.cause, r.root, r.tag, fr);
    }
}

uint64_t hooked_stat(void* self) {
    const uint64_t rv = g_origStat(self);
    record(self, 0);
    return rv;
}
uint64_t hooked_skill(void* self) {
    const uint64_t rv = g_origSkill(self);
    record(self, 1);
    return rv;
}

bool swap_slot(void* const* vft, size_t slot, void* repl, void* expect) {
    void** at = const_cast<void**>(&vft[slot]);
    DWORD oldProt = 0, ignored = 0;
    if (!VirtualProtect(at, sizeof(void*), PAGE_READWRITE, &oldProt)) return false;
    void* prev = InterlockedExchangePointer(at, repl);
    VirtualProtect(at, sizeof(void*), oldProt, &ignored);
    if (prev != expect) {
        if (VirtualProtect(at, sizeof(void*), PAGE_READWRITE, &oldProt)) {
            InterlockedExchangePointer(at, prev);
            VirtualProtect(at, sizeof(void*), oldProt, &ignored);
        }
        return false;
    }
    return true;
}

} // namespace

bool armed() { return g_armed.load(); }

bool install() {
    if (g_armed.load()) return true;
    g_mod = GetModuleHandleA(kModule);
    if (!g_mod) { logf("XPREAD-BUILD RPGModule missing -- XP observer NOT armed"); return false; }
    g_vftStat  = anchor::find_vftable(g_mod, kRttiStat, 0);
    g_vftSkill = anchor::find_vftable(g_mod, kRttiSkill, 0);
    if (!g_vftStat || !g_vftSkill) {
        logf("XPREAD-BUILD ANCHOR FAILED -- XP effect vftables not unique (stat=%p skill=%p); NOT armed",
             static_cast<const void*>(g_vftStat), static_cast<const void*>(g_vftSkill));
        return false;
    }
    void* const sStat = g_vftStat[kApplySlot];
    void* const sSkill = g_vftSkill[kApplySlot];
    const char* nStat = anchor::find_cstring(g_mod, kStatApplyName);
    const char* nSkill = anchor::find_cstring(g_mod, kSkillApplyName);
    if (!nStat || !nSkill || !anchor::function_refs(g_mod, sStat, nStat) || !anchor::function_refs(g_mod, sSkill, nSkill)) {
        logf("XPREAD-BUILD ANCHOR FAILED -- vftable slot %zu is not C_SoulValueEffect::Apply for both XP effects; NOT armed", kApplySlot);
        return false;
    }
    // The call sites that make a quest's reward: C_RPGQuest::OnStatXPReward / OnSkillXPReward, each the one function that
    // references its own trace string. Without them XP rows still record, only none is classed as a quest's.
    for (int k = 0; k < 2; ++k) {
        int cnt = 0;
        const uint8_t* fn = anchor::function_by_string(g_mod, k ? kQuestSkillName : kQuestStatName, &cnt);
        anchor::Range rg{};
        g_questKnown[k] = fn && anchor::function_range(g_mod, fn, &rg) && rg.begin == fn;
        g_questFn[k] = fn;
        if (!g_questKnown[k]) logf("XPREAD-BUILD quest %s handler not found (%d functions reference it) -- its XP is not classed as a quest's", k ? "skill" : "stat", cnt);
        else logf("XPREAD-BUILD quest %s handler: fn RPGModule+0x%llX range +0x%llX..+0x%llX (%zu bytes)", k ? "skill" : "stat",
                  static_cast<unsigned long long>(fn - reinterpret_cast<const uint8_t*>(g_mod)),
                  static_cast<unsigned long long>(rg.begin - reinterpret_cast<const uint8_t*>(g_mod)),
                  static_cast<unsigned long long>(rg.end - reinterpret_cast<const uint8_t*>(g_mod)), rg.size());
    }
    if (!anchor::section(g_mod, ".text", &g_text)) { logf("XPREAD-BUILD RPGModule .text not found -- NOT armed"); return false; }
    g_origStat = reinterpret_cast<ApplyFn>(sStat);
    g_origSkill = reinterpret_cast<ApplyFn>(sSkill);
    if (!swap_slot(g_vftStat, kApplySlot, reinterpret_cast<void*>(&hooked_stat), sStat)) {
        logf("XPREAD-BUILD stat slot changed under us -- NOT armed");
        return false;
    }
    if (!swap_slot(g_vftSkill, kApplySlot, reinterpret_cast<void*>(&hooked_skill), sSkill)) {
        swap_slot(g_vftStat, kApplySlot, sStat, reinterpret_cast<void*>(&hooked_stat));
        logf("XPREAD-BUILD skill slot changed under us -- NOT armed");
        return false;
    }
    char d[200]{};
    anchor::describe(sStat, d, sizeof d);
    logf("XPREAD-BUILD armed: C_StatXPEffect/C_SkillXPEffect Apply wrapped (stat Apply %s)", d);
    g_armed.store(true);
    return true;
}

uint8_t handle(const uint8_t* req, size_t n, uint8_t* out, size_t cap, size_t* outN) {
    *outN = 0;
    if (n < 5) return wo137::kRBadRequest;
    if (!g_armed.load()) return wo137::kRUnarmed;
    if (cap < 9) return wo137::kRFailed;
    uint32_t cursor; std::memcpy(&cursor, req + 1, 4);
    std::lock_guard<std::mutex> lk(g_m);
    const uint32_t now = g_next;                       // one past the newest seq
    const uint32_t oldest = now > static_cast<uint32_t>(kRing) ? now - kRing : 1;
    uint32_t lost = 0;
    if (cursor == 0) cursor = 1;
    if (cursor < oldest) { lost = oldest - cursor; cursor = oldest; }
    size_t at = 9;
    uint8_t count = 0;
    const uint64_t t = GetTickCount64();
    for (uint32_t s = cursor; s < now && count < kPerReply; ++s) {
        const Row& r = g_ring[s % kRing];
        constexpr size_t kRowBytes = 42;
        if (r.seq != s || at + kRowBytes > cap) break;
        std::memcpy(out + at, &r.seq, 4); at += 4;
        out[at++] = r.kind;
        std::memcpy(out + at, &r.soul, 8); at += 8;
        std::memcpy(out + at, &r.index, 4); at += 4;
        std::memcpy(out + at, &r.requested, 4); at += 4;
        std::memcpy(out + at, &r.applied, 4); at += 4;
        std::memcpy(out + at, &r.before, 4); at += 4;
        const uint64_t ageFull = t >= r.tick ? t - r.tick : 0;
        const uint32_t age = static_cast<uint32_t>(ageFull > 0xFFFFFFFFull ? 0xFFFFFFFFull : ageFull);
        std::memcpy(out + at, &age, 4); at += 4;
        out[at++] = r.cause;
        std::memcpy(out + at, &r.root, 4); at += 4;
        std::memcpy(out + at, &r.tag, 4); at += 4;
        ++count;
    }
    std::memcpy(out, &now, 4);
    std::memcpy(out + 4, &lost, 4);
    out[8] = count;
    *outN = at;
    return wo137::kROk;
}

void status(char* out, size_t n) {
    std::snprintf(out, n, " xp armed=%d quest=%d/%d rows=%u quest_rows=%u faults=%u", g_armed.load() ? 1 : 0,
                  g_questKnown[0] ? 1 : 0, g_questKnown[1] ? 1 : 0, c_rows.load(), c_quest.load(), c_faults.load());
}

} // namespace kcdmp::xpread
