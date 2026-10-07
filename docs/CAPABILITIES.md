# Capabilities and what a room really is

Kingdom Come: Together development build (VERSION 0.45.0, protocol 12). Every claim here has a level, and the level is what the room handshake sends to the other computer.
Two computers agree on the **lower** of their two levels for each capability, so neither side can promise more than the other can do. The same contract, test vectors and wording
are used by the KCD1 sibling (Deliver Us).

| Level | Meaning |
|---|---|
| `absent` | Not implemented, or not possible on this install. |
| `candidate` | Code exists. The engine entry path or the durability is not proved. Never counted as working. |
| `engine-verified` | Seen working in a real game process (earlier live sessions are recorded in the `TEST-*.md` pages and `FEATURE-HISTORY.md`). |
| `integration-verified` | Exercised end to end between real relay and client processes by automated tests. |
| `human-accepted` | Two or more people played it and signed it off using [HUMAN-ACCEPTANCE-TESTS.md](HUMAN-ACCEPTANCE-TESTS.md). **Nothing is at this level from this branch's work.** |

## The table for this build

| Capability | Level | Evidence / limit |
|---|---|---|
| `presence.bodies` | engine-verified | Peer ghosts spawned and driven by the native plugin / Lua. |
| `presence.locomotion` | engine-verified | Same. |
| `presence.outfits` | engine-verified | Appearance replication seen in earlier live sessions. |
| `character.personal` (bring my character / start fresh) | engine-verified | Save splice and join flow; WO-125/153. Limits: [IMPLEMENTATION-STATUS.md](IMPLEMENTATION-STATUS.md). |
| `identity.participant` | integration-verified | Each install keeps a P-256 key; the relay binds the participant id to the first key that proves it, across restarts. Tested against hijack and replay. |
| `checkpoint.barrier` | candidate | The immutable checkpoint store and offline selection are tested. A **live** consistent capture barrier is not. |
| `authority.npc` | engine-verified **only with the native plugin (KCDMP.dll) present**, else candidate | Host-side NPC claims, scan and drive. `KCDMP_NativeTests.exe`: 403 passed. |
| `authority.combat` | engine-verified **only with the native plugin present**, else candidate | Damage authority exists; coordination with NPC claims and unique hit ids are incomplete (see gates). |
| `authority.quest` | engine-verified | Quest-state sync with host attribution. |
| `authority.loot` | candidate | Scoped in-memory body retry cache with host readback. **No durable operation journal, no pre-transfer escrow.** Do not call this duplication-proof. |

## Room modes, in plain words

| Mode | What you are told | When |
|---|---|---|
| **Refused** | The reason, in a sentence, and what to do. | Another game, another contract or wire version, a different or unverifiable mod payload (the `kdcmp.pak`), or no handshake at all. |
| **Presence** | "Room: presence only. You see each other, but shared NPC, combat, loot and quest authority is NOT active (…)." | No authority capability is engine-verified on both sides, **or** the installs have different DLC or other mods. |
| **Partial** ("partly shared") | "Room: partly shared (not every authority capability is verified: …)." | At least one authority capability is engine-verified on both sides, and not all of the required ones are integration-verified. **This is what a normal two-player room with the native plugin reports today**, and it names `authority.npc`, `authority.combat`, `authority.quest`, `authority.loot`, `checkpoint.barrier` and `character.personal` among the missing (each is engine-verified at best, not yet integration-verified). |
| **Shared simulation** | "Room: shared simulation." | Every capability required for a shared simulation is integration-verified on both sides. **This build cannot reach it** (loot durability and the live checkpoint barrier are open). |

### Different DLC or mods

Two installs with different DLC or other mods are admitted but capped at Presence. The room's list of what is missing gains "your game has other DLC or mods than the host's", and
`mp_join_request` is refused with a sentence on screen; no world is moved to the player whose content differs.

## What decides a payload match

Both computers must have the same `kdcmp.pak` (Lua), compared by SHA-256 in the handshake. The agent (`KCDMP.exe`/client), the native plugin (`KCDMP.dll`), the game engine (`WHGame.dll`)
and the content profile (DLC paks and other mod folders) are also sent and logged; only the Lua pak and the content profile are enforced. A missing pak is refused ("could not be verified").
`Contract:AllowUnverifiedPayload true` on the relay exists for development and payload smoke tests only.

## Not done, on purpose

Recorded rather than faked. Full list with reasons: [IMPLEMENTATION-STATUS.md](IMPLEMENTATION-STATUS.md).

* A durable loot and economy ledger: every ownership change in one journaled authority queue, pre-transfer control or escrow, recovery of ambiguous transfers. `OperationJournal` and `AuthorityGuard` exist with tests but are **not attached** to the loot paths.
* A live consistent world, character and ledger capture barrier, and promotion of a winning checkpoint.
* A global authority incarnation (epoch) carried by every gameplay message.
* Combat: one health and death authority coordinated with NPC claims and unique hit ids.
* Pause-menu Multiplayer tab in a live session (the main-menu tab is observed).
* Linux and Proton packages: not rebuilt from this branch; the published "Untested Linux support" release is unchanged.
* No Warhorse modding terms text for KCD2 was found, so the installer does not claim one.
* Two-computer, four-player and multi-hour soak testing.
