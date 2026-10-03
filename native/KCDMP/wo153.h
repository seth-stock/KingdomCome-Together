// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// WO-153: "keep playing" -- the joiner's own copy of the host's cutscene is not played
// (docs/WO-153-findings.md; the rules are wo153_rules.h).
//
// GUIModule exports C_CutscenePlayer::EnqueueCutscene(shared_ptr<C_CutsceneConfiguration>&), the one function
// every cutscene is enqueued through. The gate is an entry hook that skips it (the function returns void; the
// shared_ptr is the caller's own reference and is left untouched) while the mode says to. Anchored by the
// module's own export, and the exact 16 prologue bytes (hook_prologues.h); any miss = not armed (logged).
//
// Pipe: wo151 ops 9 (SceneGate [mode:1][windowMs:2] -> [armed:1][mode:1]) and 10 (SceneGateStatus -> text).

#include <cstdint>

namespace kcdmp::wo153 {

// Call once from a thread that is not the game's main thread (the patch suspends every other thread).
void install();
bool armed();

// mode: 0 off, 1 follow the host, 2 all (live check only). Resets to off when the pipe closes.
void set_mode(uint8_t mode, uint16_t windowMs);
void on_disconnect();

// "wo153 scene_gate=armed|off mode=<n> window_ms=<n> refused=<n> seen=<n> ..."
int status_text(char* out, int n);

} // namespace kcdmp::wo153
