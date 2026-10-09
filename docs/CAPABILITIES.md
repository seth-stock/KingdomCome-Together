# Capabilities and what a room really is

## Session 6 status (2026-10-09)

Full multiplayer remains unfinished. KCD1 native input on ordinary NPCs is a confirmed no-op; a direct animation bypass faulted and was removed. A safe native diagnostic reader and verified health/death application remain. KCD2 hit watches now check avatar lifetimes and native health/stamina restoration. These reliability changes do not establish complete combat/economy/quests/personal state. See [SESSION6-RESULTS.md](SESSION6-RESULTS.md).

## Session 5 status (2026-10-09)

Full campaign multiplayer and parity remain unfinished. KCD1 now has sequenced, incarnation/term-checked NPC movement, stale-handle protection, pre-mutation reward baselines and verified item grants. Weather presets are Candidate, default off (`kcdus_weather on` on both clients), with no current-profile or scripted-weather preservation proof. KCD2 whole-item put compensation preserves the observed native instance on definitive refusal; unknown outcomes remain unresolved and cannot trigger a speculative refund. KCD2 source wire is now **15**. Neither game has complete native economy interception, universal quest/XP/effect authority or arbitrary complete character restoration. See [SESSION5-RESULTS.md](SESSION5-RESULTS.md).

## Session 4 current status (2026-10-08)

VERSION stays 0.45.0; **wire protocol is now 14** so older agents/relays are refused. All participants must install this same tagged build. This remains partly shared, with no human acceptance or shared-simulation claim.

Live container/reward parity is **Candidate: synthetic Lua and agent tests only, no fresh KCD2 engine proof**. `kdcmp_containers.lua` extends WO-134 with named horse saddlebags, shop-linked stashes and live chest stock. It uses existing scoped durable host decisions, native inventory readback, bounded multipart stock generations and duplicate/restart quarantine. Guest takes require pack gain and puts require pack loss. Stock is applied only after pending transfers settle; stale generations are ignored. Failed rollback blocks supported character/checkpoint capture.

The host defaults to live shared stock. `mp_shared_containers off` restores the existing personal per-world chest-ledger behavior; the setting is session-local and must be reapplied after restarting the agent. `mp_loot_chests off` disables the new container watcher too. Bodies, loose pickups, drops, NPC/combat/quests, shared pause, DLC rules and checkpoint Candidate mode remain in place.

Native inventory screens move items before polling: this is **not pre-transfer escrow**. Containers use class/count/condition, not exact per-instance metadata. Anonymous moving horses are refused; stable names or position identities must match. Simultaneous host-native UI transfers, streaming, restocks, barter/theft, recipient persistence and interruption conservation need engine/two-computer proof. A refused purchase refunds only the measured personal debit once; wallets are not shared. A rejected/unknown put removes the optimistic local stock without a speculative Henry refund, so items can be lost. Consume/equip before rollback can prevent settlement; the supported save path then refuses rather than declaring success. Crashing/reloading outside that path is not a proved receipt protocol.

`kdcmp_rewards.lua` observes a three-second host inventory window around verified mirrored quest steps. It excludes observed loot and subtracts native guest payment; same-quest cascades coalesce, early messages wait for verified application, and uncertain native creation is not retried. First observations/checkpoints are not rewarded retroactively. The window is heuristic, may confuse unrelated unobserved gains, is not durable across loads/restarts, has no XP or offline backfill, and cannot establish all quest side effects.

See [SESSION4-RESULTS.md](SESSION4-RESULTS.md), [SESSION4-PARITY-DESIGN.md](SESSION4-PARITY-DESIGN.md), and H-43 through H-50 in [HUMAN-ACCEPTANCE-TESTS.md](HUMAN-ACCEPTANCE-TESTS.md). The live checkpoint barrier remains Candidate, default off.

Kingdom Come: Together development build (VERSION 0.45.0, protocol 14). Every claim here has a level, and the level is what the room handshake sends to the other computer.
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
| `authority.npc` | engine-verified **only with the native plugin (KCDMP.dll) present**, else candidate | Host-side NPC claims, scan and drive. `KCDMP_NativeTests.exe`: 413 passed. |
| `authority.combat` | engine-verified **only with the native plugin present**, else candidate | Damage authority exists; coordination with NPC claims and unique hit ids are incomplete (see gates). |
| `authority.quest` | engine-verified | Quest-state sync with host attribution. |
| `authority.loot` | candidate | Scoped durable host body/loose decisions and native readback; recipient receipt and complete pre-transfer escrow/economy interception remain unproved. Do not call this duplication-proof. |

## Room modes, in plain words

| Mode | What you are told | When |
|---|---|---|
| **Refused** | The reason, in a sentence, and what to do. | Another game, another contract or wire version, a different or unverifiable mod payload (the `kdcmp.pak`), or no handshake at all. |
| **Presence** | "Room: presence only. You see each other, but shared NPC, combat, loot and quest authority is NOT active (â€¦)." | No authority capability is engine-verified on both sides, **or** the installs have different other mods. |
| **Partial** ("partly shared") | "Room: partly shared (not every authority capability is verified: â€¦)." | At least one authority capability is engine-verified on both sides, and not all of the required ones are integration-verified. **This is what a normal two-player room with the native plugin reports today**, and it names `authority.npc`, `authority.combat`, `authority.quest`, `authority.loot`, `checkpoint.barrier` and `character.personal` among the missing (each is engine-verified at best, not yet integration-verified). |
| **Shared simulation** | "Room: shared simulation." | Every capability required for a shared simulation is integration-verified on both sides. **This build cannot reach it** (loot durability and the live checkpoint barrier are open). |

### Different DLC or mods

Different other mods cap Presence and block world moves. Richer-DLC guests may join a lower-DLC host; extra DLC quest mirroring is vetoed and the guest is told to disable extras in Steam and restart.
A poorer-DLC guest cannot receive an existing richer host save. Choose a lower-content host world; the mod cannot convert a DLC-dependent save or deactivate Steam entitlements.

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

## Session 2 continuation: 2026-10-07

This remains a playtest build. No capability has human acceptance, and the two games do **not** yet have equivalent shared simulation. VERSION remains KCD1 0.1.0 / KCD2 0.45.0; distinguish downloads by release tag, commit and SHA-256, not VERSION alone.

* KCD2 protocol 14 refuses older agents/relays. Durable host body take/put and loose-item decisions are journaled before the Lua mutation and before the reply. Interrupted unknown mutations are quarantined, never retried automatically. `HostDecisionComplete` means the host decision is durable, not that the recipient received/kept an item. Loose-item replies now carry the same connection/load scope as body replies. Unknown loose items are refused; successful removal requires entity readback.
* KCD2 checkpoint barrier is **Candidate, default off**. Host console: `mp_checkpoint_mode candidate` (off reverses it for this agent session). It uses a fixed relay-verified participant roster, authority incarnation and checkpoint UUID, acknowledged native holds, loot settlement, a verified host save, bounded/acknowledged guest character uploads, matching save MD5s, chest ledgers, cross-participant item-instance checks, durable prepared artifacts, commit acknowledgements, and manifest publication before release. Disconnect, roster/load change, missing data or the 90-second deadline aborts. The native hold expires 20 seconds after the agent's last refresh. A guest keeps its paired personal artifacts and receipt; it does not yet receive a full host checkpoint archive for offline reconciliation.
* Checkpoints still need proof that native saves work while held and that **all** inventory/quest/combat mutations are excluded. The Lua pickup gate is not complete native inventory interception. Ordinary autosaves still follow the existing path; interrupted capture is not automatically promoted. Offline checkpoint selection/preparation remains separate from live reconnect promotion.
* KCD1 quest mirror is **Candidate, default off**. Console `kcdus_quest_mode candidate` on both test clients enables one-way host snapshots; `off` disables it. Identifiers come from the installed quest tables. Only open, base-game main/side/activity quests are eligible. DLC, rails/mixed scenes, local system/random events and unknown objectives are vetoed. It reads native state before applying and after mutation, replays completed objectives without repeating the call, refuses inactive objectives and unverified quest completion, and never treats cancel/deactivate as success. Objective reward/spawn side effects and full quest/variable coverage remain unproved.
* Private KCD1 engine evidence: `q_revenge/findVonAulitz` changed from started/not-completed to completed through the real adapter; repeated application read back completed. This proves one objective binding, not full quest authority. `t_scale=0` stopped observed calendar progression; it was restored to 1. Production shared pause remains **Absent**: there is no proved crash-safe engine lease or menu-pause interception. A Lua timer cannot safely release a freeze that stops its timers.
* KCD1 enemies/combat decision: **do not enable shared combat/NPC drive**. Existing damage probes did not prove the ordinary hit path; presence NPCs are not host-authoritative combatants. Damage interception, AI suppression/drive, targeting all players, one death/reward revision and a shared inventory ledger remain implementation gates. Do not replace this with cosmetic attacks or health setters.
* KCD2 shared pause and its off option remain Candidate until the two-computer tests pass. DLC uses the host's lower-content world: richer guests are allowed and extra DLC quest mirroring is gated; poorer guests cannot receive an existing richer host save. Disable extra DLC in Steam and restart as instructed. The mod does not rewrite a DLC save or deactivate Steam entitlements. Different other mods still cap the room at presence and block world/character moves.
* Linux packages are experimental. A successful WSL build/fake Steam test is not proof of Proton gameplay. KCD1's Windows startup adapter paths are not proved under the native Linux agent. No Linux engine feature is upgraded by rebuilding a tarball.

Human acceptance is pending. KCD1 ESC-menu Multiplayer entry and its Status/Host/Join/Leave/Game world/Story/Keys/Settings/Back page were visually observed in the disposable loaded game. That does not prove buttons in a connected session. KCD2 pause-menu behavior still needs verification. Run the new test cases on disposable copies, record release/commit hashes and logs, and leave failed capabilities Candidate. No real saves or firewall rules were changed by this continuation's guarded probes.

## Session 3 (2026-10-08): KCD1 notes above are superseded

The KCD1 lines in the Session 2 paragraphs above ("Production shared pause remains Absent", "do not enable shared combat/NPC drive", "existing damage probes did not prove the ordinary hit path") no longer hold. The KCD1 damage probe had passed health 0 (`DealDamage(3, 0, ...)` is stamina 3, health 0); with health damage the ordinary path lowers health and kills. KCD1 now has shared OUTCOMES of fights (health and death of the NPCs near each player), host-decided loot of corpses and stashes, and a crash-safe shared pause lease, each proved in a private engine with a synthetic second player. See the KCD1 repository's `docs/CAPABILITIES.md` (sections "Shared fights and loot" and "Pausing"). This KCD2 repository is unchanged by that work; KCD2 keeps its own native combat, loot journal, shared pause and candidate checkpoint as described above.
