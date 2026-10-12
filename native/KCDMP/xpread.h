// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
#pragma once
// Session 10: the native XP observer (causal quest XP, the KCD2 half).
//
// A quest's reward node pays experience through the same RPGModule effects as every other XP source:
// C_StatXPEffect and C_SkillXPEffect (both C_SoulValueEffect<..., fixed_t<uint,25>>), whose Apply is vftable
// slot 3. Apply's own fields are the measurement: +0x28 the soul id, +0x30 the stat/skill index, +0x34 the
// requested amount, +0x38 the amount actually applied (after the soul's own multipliers), +0x3C the XP before.
//
// This module wraps those two slots (verified by RTTI and by the slot's own "::Apply" trace string, never by an
// address), lets the engine's Apply run exactly as before and then records what it did, with a short call stack so
// a reader can tell a quest's reward from a script's AddStatXP or a skill used. Nothing is changed and nothing is
// applied here: the agent reads the ring (pipe op 9) and decides.
//
// Pipe op 9 (WO137 request): [cursor:4]  -> [now:4][lost:4][count:1]{row}*
//   row = [seq:4][kind:1][soul:8][index:4][requested:4][applied:4][before:4][ageMs:4][cause:1][root:4][tag:4]   (42 bytes)
//   kind 0 stat, 1 skill; cause 0 other (script, activity, skill use ...), 1 a quest's stat reward, 2 a quest's skill reward
//   (the call stack runs through C_RPGQuest::OnStatXPReward / OnSkillXPReward, located by their own trace strings);
//   root = the outermost quest State change whose consumers were running (the plugin's change seq; 0 none);
//   tag = the Apply pulse running (a joiner applying the host's change: the host's seq; 0 none).
//   Rows are those with seq >= cursor, oldest first (at most 12 per reply); `lost` is the number of rows the ring
//   dropped before the cursor could read them.

#include <cstddef>
#include <cstdint>

namespace kcdmp::xpread {

constexpr uint8_t kOpXpRead = 9;

// Off the main thread: resolve and wrap. False (and logged) when an anchor does not match: nothing is patched then.
bool install();
bool armed();

// The pipe (main thread). Returns a wo137 reason code.
uint8_t handle(const uint8_t* req, size_t n, uint8_t* out, size_t cap, size_t* outN);

// One line for the status text.
void status(char* out, size_t n);

} // namespace kcdmp::xpread
