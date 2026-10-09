# Human acceptance tests: Kingdom Come: Together (KCD2)

For two to four people, each on their own computer, each with the **same installer** from the same download. This page is the **protocol 12 / contract** layer: do it first. The older,
longer, feature-by-feature list is [TWO-PLAYER-CHECKLIST.md](TWO-PLAYER-CHECKLIST.md); the current tester page is [TEST-0.43.0.md](TEST-0.43.0.md). A pass here moves a capability
to `human-accepted` in [CAPABILITIES.md](CAPABILITIES.md); a failure is a bug report, not a reason to skip the step.

**Rules for every test**

* Use a throwaway copy of the host's save, and a throwaway save on every friend's machine (crimes, deaths, sleeping and loot are real).
* Write down what you *saw*, with the time, not "it worked". If the result is not in the "Expect" column it is a failure, even if the game did not crash.
* Console markers (`~`): type `mark_<name>` as the item starts, e.g. `mark_contract`; `mark_odd` for anything strange. The line lands in the game and agent logs.
* A normal room with the native plugin is **partly shared**, not shared: loot is not duplication-proof and there is no live checkpoint barrier. Do not report those as new bugs. Do report anything that *claims* more.

**Evidence to send after any failure, and after H-30**

* **Report a bug** in the launcher on every machine (it also collects the previous six launches' logs).
* The relay's log if you host (`logs\` next to the relay).
* Screenshot of the launcher's room line. Remove public IP addresses.

## A. Install and identity (each machine, 15 minutes)

| # | Do | Expect | Failure looks like |
|---|---|---|---|
| H-01 | Check the installer's SHA-256 against `SHA256.txt`; run it. | Equal. Installer states the game and Modding-tools requirements. | Different hash: do not install. |
| H-02 | Open the launcher; read the title bar and the version line. | 0.45.0 on every machine, and the same payload build line. | Different numbers between machines. |
| H-03 | Host: **HOST GAME**. Note the addresses. | Listening on 7778, addresses listed. | No address. |
| H-04 | Friend: **ADD SERVER**, **JOIN SERVER**, then start the game from the launcher and stay at the main menu; **CONNECT**. | Friend joins the host's world (loading screen, up to a minute). The launcher shows the room line. | Any refusal is a result: write the sentence down. |
| H-05 | Friend closes the launcher and game, starts everything again, reconnects with the same name. | Same participant; the host shows one entry for that friend. | Two entries, or "refused as someone else". |
| H-06 | Copy the friend's `%LocalAppData%\KCDMP\participant.key` to a third computer; join under the same name while the friend is *not* connected, then again while the friend *is*. Delete the copy afterwards. | First works (same key, same participant); the second is refused as already present. | Both admitted at once. |

## B. Refusals say why (15 minutes)

| # | Do | Expect |
|---|---|---|
| H-07 | Install a build with a different `kdcmp.pak` (the maintainer sends one) on a friend; join. | "The host's room refused this install: â€¦ different mod payloads â€¦", plus "install the same download". Nothing else breaks. |
| H-08 | Install an older release (0.44.x) on a friend; join. | The release or protocol mismatch message, as before. |
| H-09 | Host a room limited to two players (`ServerInfo:MaxPlayers` 2 on a relay you run); a third tries. | "The host's game is full". |
| H-10 | A third machine joins the host's room while the host and friend are in it, then the friend leaves. | The third is admitted; the room line recalculates for who remains. |
| H-11 | A friend with an extra DLC or mod folder in `Mods\` joins. | **Admitted.** Both launchers/rooms say the room is **presence only** and that this game has other DLC or mods. |
| H-12 | The friend from H-11 triggers the world join (`mp_join_request`, or the menu's join). | Refused with: "Your game has other DLC or mods than your host'sâ€¦". The host is not paused. |

## C. The room says what it is (10 minutes)

| # | Do | Expect | Failure looks like |
|---|---|---|---|
| H-13 | Read the launcher's room line with host and one friend connected. | "Room: partly shared (not every authority capability is verified: â€¦)". | The word "shared simulation" anywhere. |
| H-14 | Optional, if you have a host install without the native plugin (`KCDMP.dll` not loaded). | The room drops to "presence only â€¦ NOT active". | Still "partly shared". |
| H-15 | A friend disconnects. | The remaining room line recalculates within seconds. | A stale line. |

## D. Gameplay still works (two machines, 45 minutes)

Run the "Most wanted first" part of [TEST-0.43.0.md](TEST-0.43.0.md) (both fight the same enemies, a knockout, host down, guard fight, horses, talking) and record each result. These
must still behave as before this branch: **no feature was removed.**

| # | Do | Expect |
|---|---|---|
| H-16 | Both fight the same bandits. | Enemies attack both; the friend's hits count on both screens. |
| H-17 | The friend rides the host's horse. | "Mount", not a theft. |
| H-18 | Talk, trade, sleep, wait. | Time and weather agree within the documented tolerance. |
| H-19 | Join-or-stay: the host enters a rails quest. | The friend is asked; F11 brings them, F12 leaves them. |
| H-20 | Chat in both directions. | Messages arrive. |

## E. Loot (the open gate: read this before trusting it)

The loot path is **not** duplication-proof. This test is meant to find the failures, not to prove the absence of them.

| # | Do | Expect |
|---|---|---|
| H-21 | Both take everything from the same corpse at once (count the items first). | Each item ends with exactly one owner. Note any item that exists twice or vanishes. |
| H-22 | The friend moves items between their inventory and a chest; the host does the same at the same time. | Counts are conserved. |
| H-23 | Kill the friend's agent in the middle of a transfer; reconnect. | No duplicated or lost item, or a duplicate you can *name*. Send the logs either way. |

## F. World, reconnect and saves (two machines, 30 minutes)

| # | Do | Expect |
|---|---|---|
| H-24 | The host saves, quits, loads; the friend stays connected, then reconnects. | The friend joins the host's loaded world. |
| H-25 | The friend quits to the menu and joins again. | Same character, same inventory; "Bring my character" is offered only once. |
| H-26 | The host kills their agent and starts it again. | Friends reconnect on their own. |
| H-27 | Check `Saved Games` before and after the session. | No save of yours was changed except the ones the mod says it writes. |

## G. Soak and four players (60+ minutes)

| # | Do | Expect |
|---|---|---|
| H-28 | Four machines in one room for an hour: walk, fight, ride, enter buildings, save/load on the host. | No crashes; the room line unchanged; logs grow modestly. |
| H-29 | Uninstall from "Apps" on one machine. | The game's own files and saves are untouched. |
| H-30 | Install the same installer over a working install. | Settings kept. |

## Result sheet

For each test: ID, pass/fail, what you saw, time, the launcher's version line, and the installer's SHA-256. Send it with the evidence above.
Anything failed stays `candidate` or `engine-verified` in [CAPABILITIES.md](CAPABILITIES.md) until fixed and repeated.

## Session 2 tests: pending human acceptance

All cases below are PENDING. Record both machines' release tags, installer hashes, VERSION, engine builds, active DLC, settings and logs. Use disposable save copies. Do not mark overall shared simulation accepted from any individual passing case.

| ID | Test | Required result |
|---|---|---|
| H-31 | Host with no save-affecting DLC, guest with all; restart after disabling extras as instructed. | Guest is admitted; extra DLC is identified and excluded from mirroring; lower-DLC world loads. No automatic Steam entitlement removal is claimed. |
| H-32 | Host with all DLC, guest with none; try moving that host world. Then reverse the host roles. | First transfer is refused before installation. Lower-content host world may be joined. Original saves remain preserved. |
| H-33 | Different other mods or different Lua build. | Other mods cap presence and block world moves; differing Lua payload is refused. |
| H-34 | Host opens ESC, then guest opens ESC; leave it open for 15 minutes. | KCD2: both worlds stop and resume together with shared pause enabled. KCD1: shared pause is unavailable; document independent worlds instead of reporting a pass. |
| H-35 | Set pause off on both KCD2 clients; open ESC on each. | Menu works while both worlds continue. KCD1: unavailable until a native interception path is proved. |
| H-36 | Disconnect/kill an agent during shared pause. | KCD2 hold releases within 20 seconds of the last refresh. Normal offline menu pause remains available. |
| H-37 | Open Multiplayer in the pause menu; use status, Back and settings, then resume. | Correct live-session pages and navigation; main-menu-only evidence is insufficient. |
| H-38 | Two guests take the same body/loose item at once; retry/delay replies; restart host between intent/mutation/result. | One host mutation; retries replay; uncertain mutations quarantine. No stale-scope reply grants an item. Record any lost item separately; no duplication claim from unit tests. |
| H-39 | KCD2: `mp_checkpoint_mode candidate`, save with all clients joined; disconnect/load or withhold one capture. | All participants' native holds, matching world MD5, distinct characters and ledgers, final manifest after ACKs; otherwise abort and release. No partial checkpoint is selectable. Save while held and all native inventory exclusion must be separately demonstrated. |
| H-40 | Reconnect from divergent checkpoints and choose one; stage personalized characters. | Both alternatives preserved, no independent-world merge, no unrelated character substitution. Guest archive transfer/live promotion are still unresolved; do not treat offline preparation as native acceptance. |
| H-41 | KCD1: on disposable matching worlds, enable `kcdus_quest_mode candidate` on both; complete an open base objective, repeat, then try rails/DLC/local events. | Native readback agrees; replay does not repeat mutation; vetoed paths stay untouched. Compare rewards, XP, NPC spawns and save/reload. Full quest completion without native proof must report unverified. |
| H-42 | Extract Linux archive, validate SHA256SUMS, run doctor/install on fake Steam, then real Proton on two computers. | Fake fixtures and real gameplay reported separately; verify Windows/Linux feature negotiation. Missing KCD1 native adapter paths stay unavailable. |

## Session 4 acceptance: all PENDING (two computers, disposable worlds only)

Record release tag/commit, installer/Lua/native hashes, engine build, content and both logs. Use protocol-14 agents and relay. Compare total quantities across both Henrys and source inventories; unexpected loss also fails. Tests must use the game's actual inventory/trade/quest screens, not console stand-ins.

| ID | Procedure | Required outcome / remaining gate |
|---|---|---|
| H-43 | Default live mode: open same chest on both PCs, race for its last item; put/split/withdraw stacks. | Matching stock; exactly one confirmed take/put; no duplicate, lost or recreated item after save/reload. |
| H-44 | Named horse saddlebags: same races, move horse away and return, rejoin/load. Try anonymous horse. | Named identity matches; anonymous moving identity refused; no wrong-horse mutation, duplicate or disappearance. |
| H-45 | Shop stock beyond 9 m: simultaneous purchase of last item, multi-item barter, refused and delayed replies. | Goods settle once, measured refused debit refunded once; no invented refunds. Shared wallets/barter/crime are not promised. |
| H-46 | Fresh restock occurs without pack transfer; stock multipart updates arrive out of order while take is pending. | No false put/take; pending transfer not overwritten; stale generation cannot replace newer stock. |
| H-47 | Host mp_shared_containers off, rejoin; test old personal chest ledger; mp_loot_chests off/on. | Personal-ledger behavior preserved; off disables live watcher. Session-local mode must be reapplied after agent restart. |
| H-48 | Mirrored supported quest pays items/currency natively; retry/cascade; complete alongside known loot and unrelated gains. | Native-paid amount subtracted, early reward waits for verified state, same-load retry not paid twice. Record heuristic attribution errors, XP and load/restart gaps as unresolved. |
| H-49 | Interrupt guest/host agent between journal intent/native mutation/result; withhold reply for >20 s, then attempt supported snapshot. | Durable unknown host mutation not retried; take rolled back. Inert native removal blocks snapshot. Rejected put loss, recipient persistence and crash/reload conservation must be recorded, not waived. |
| H-50 | Repeat existing combat, body/loose/drop, pause, DLC, quest and Candidate checkpoint tests on the packaged build, then real Proton. | No feature regression; platform absence honestly negotiated. Checkpoint remains Candidate until complete native exclusion and held-save proof. |

## Session 5 cases: all PENDING

| ID | Test | Required evidence |
|---|---|---|
| H-51 | Put a whole item with condition, theft/provenance and extensions into a chest/horse/shop. Receive a definitive refusal. | Same observed native item returns to Henry with metadata preserved; no class-only substitute. Split/merge/change the item and verify ambiguous compensation refuses. |
| H-52 | Restart host between put intent and outcome, lose a result, retry, then attempt capture and mode off. | Quarantined `uncertain` differs from `gone`; no speculative refund. Pending evidence survives mode refusal and new watcher asks suspend. Native-screen locks and recipient persistence remain unresolved. |
| H-53 | Native return/remove/add is inert or throws after a partial change; retry and reconnect/load. | No unverified success or repeated compensation. Supported capture refuses; record the unresolved recipient crash/load and partial-move recovery path. |
