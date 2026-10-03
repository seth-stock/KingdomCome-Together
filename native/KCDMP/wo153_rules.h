// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-153: the rules of the cutscene gate, engine-free (native/tests/wo153_rules_tests.cpp pins them).
//
// The gate sits at C_CutscenePlayer::EnqueueCutscene, the one function every cutscene (rendered, ingame,
// fader, text, skip-time, fast-travel) is enqueued through. On a joiner who chose to keep playing, a scene
// the HOST's quest step started on this machine -- the copy of the host's scene that this game's own quest
// graph would play 0.1-1.5 s after the host's -- is not enqueued. A scene this player started themselves is.
//
// "The host's step started it" is read from the one place that knows: the quest sync's apply (wo137.cpp),
// which pulses the State's own Set<Value> port with a counter held up around the call. The graph is
// single-threaded and runs the consumers inside that call, so an enqueue made by it happens while the
// counter is up. A scene a node schedules for later (a delay, a timer) happens outside it; the window after
// the apply (default 3 s) covers a short deferral and is the only place this can refuse something that was not
// the host's doing.

#include <cstdint>

namespace kcdmp::wo153rules {

enum class Mode : uint8_t {
    Off = 0,        // nothing is refused
    FollowHost = 1, // refuse an enqueue made by the host's mirrored quest step (and, for the window, just after it)
    All = 2,        // refuse every enqueue: the live check only (the agent never sets it in a session)
};

constexpr uint16_t kDefaultWindowMs = 3000, kMinWindowMs = 0, kMaxWindowMs = 10000;
constexpr uint64_t kNever = ~0ull;   // "no apply has ever ended"

inline Mode mode_from(uint8_t v) { return v == 1 ? Mode::FollowHost : v == 2 ? Mode::All : Mode::Off; }

inline uint16_t clamp_window(uint32_t ms) { return static_cast<uint16_t>(ms > kMaxWindowMs ? kMaxWindowMs : ms); }

// True = refuse this enqueue. `bypass` is our own re-entry (never set by this feature's refusal path; kept so a
// future "release a held scene" cannot be refused by its own gate).
inline bool refuse(Mode m, bool applyActive, uint64_t msSinceApplyEnd, uint16_t windowMs, bool bypass) {
    if (bypass) return false;
    switch (m) {
        case Mode::All: return true;
        case Mode::FollowHost: return applyActive || (msSinceApplyEnd != kNever && msSinceApplyEnd <= windowMs);
        default: return false;
    }
}

} // namespace kcdmp::wo153rules
