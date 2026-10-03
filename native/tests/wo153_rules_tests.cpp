// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
// WO-153: engine-free checks of the cutscene gate's rules (native/KCDMP/wo153_rules.h).
// Linked into KCDMP_NativeTests; wo153_rules_tests() returns the number of failures.
#include <cstdio>

#include "wo153_rules.h"

using namespace kcdmp::wo153rules;

namespace {
int g_fail = 0, g_pass = 0;
#define KCHECK(cond, ...) do { if (cond) ++g_pass; else { ++g_fail; std::printf("FAIL  %s:%d  ", __FILE__, __LINE__); std::printf(__VA_ARGS__); std::printf("\n"); } } while (0)
} // namespace

int wo153_rules_tests(int* passed) {
    // the mode byte: only 1 and 2 mean anything; everything else is off (an unknown future value never refuses a scene)
    KCHECK(mode_from(0) == Mode::Off && mode_from(1) == Mode::FollowHost && mode_from(2) == Mode::All, "the three modes");
    KCHECK(mode_from(3) == Mode::Off && mode_from(255) == Mode::Off, "an unknown mode is off");

    // off refuses nothing, whatever is going on
    KCHECK(!refuse(Mode::Off, true, 0, 3000, false), "off, applying");
    KCHECK(!refuse(Mode::Off, false, 10, 3000, false), "off, just after an apply");

    // follow-the-host: a scene made by the host's mirrored step is refused...
    KCHECK(refuse(Mode::FollowHost, true, kNever, 3000, false), "inside the apply, no earlier apply");
    KCHECK(refuse(Mode::FollowHost, true, 99999, 3000, false), "inside the apply, a long-ago earlier one");
    // ...and one a moment after it (a short deferral), up to and including the window
    KCHECK(refuse(Mode::FollowHost, false, 0, 3000, false), "0 ms after the apply");
    KCHECK(refuse(Mode::FollowHost, false, 2999, 3000, false), "2999 ms after");
    KCHECK(refuse(Mode::FollowHost, false, 3000, 3000, false), "exactly the window");
    KCHECK(!refuse(Mode::FollowHost, false, 3001, 3000, false), "past the window: the player's own scene plays");
    KCHECK(!refuse(Mode::FollowHost, false, 600000, 3000, false), "ten minutes later");
    // a scene this player started themselves, with no apply ever: never refused
    KCHECK(!refuse(Mode::FollowHost, false, kNever, 3000, false), "no apply has ever ended: nothing is the host's doing");
    // a window of 0 refuses only inside the apply itself (and the very ms it ended)
    KCHECK(refuse(Mode::FollowHost, true, kNever, 0, false), "window 0, inside");
    KCHECK(!refuse(Mode::FollowHost, false, 1, 0, false), "window 0, 1 ms after");

    // all: the live check refuses everything
    KCHECK(refuse(Mode::All, false, kNever, 3000, false), "all, no apply");
    KCHECK(refuse(Mode::All, false, 99999, 0, false), "all, long after");

    // our own re-entry is never refused, in any mode
    KCHECK(!refuse(Mode::All, true, 0, 3000, true) && !refuse(Mode::FollowHost, true, 0, 3000, true), "bypass");

    // the window is clamped to what the agent may ask
    KCHECK(clamp_window(0) == 0 && clamp_window(3000) == 3000 && clamp_window(10000) == 10000, "in range");
    KCHECK(clamp_window(10001) == kMaxWindowMs && clamp_window(65535) == kMaxWindowMs && clamp_window(1000000) == kMaxWindowMs, "clamped");
    KCHECK(kDefaultWindowMs == 1500, "default window: short, so a scene the player starts is not caught");

    *passed = g_pass;
    return g_fail;
}
