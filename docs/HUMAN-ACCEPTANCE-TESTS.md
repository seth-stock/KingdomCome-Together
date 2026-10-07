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
| H-07 | Install a build with a different `kdcmp.pak` (the maintainer sends one) on a friend; join. | "The host's room refused this install: … different mod payloads …", plus "install the same download". Nothing else breaks. |
| H-08 | Install an older release (0.44.x) on a friend; join. | The release or protocol mismatch message, as before. |
| H-09 | Host a room limited to two players (`ServerInfo:MaxPlayers` 2 on a relay you run); a third tries. | "The host's game is full". |
| H-10 | A third machine joins the host's room while the host and friend are in it, then the friend leaves. | The third is admitted; the room line recalculates for who remains. |
| H-11 | A friend with an extra DLC or mod folder in `Mods\` joins. | **Admitted.** Both launchers/rooms say the room is **presence only** and that this game has other DLC or mods. |
| H-12 | The friend from H-11 triggers the world join (`mp_join_request`, or the menu's join). | Refused with: "Your game has other DLC or mods than your host's…". The host is not paused. |

## C. The room says what it is (10 minutes)

| # | Do | Expect | Failure looks like |
|---|---|---|---|
| H-13 | Read the launcher's room line with host and one friend connected. | "Room: partly shared (not every authority capability is verified: …)". | The word "shared simulation" anywhere. |
| H-14 | Optional, if you have a host install without the native plugin (`KCDMP.dll` not loaded). | The room drops to "presence only … NOT active". | Still "partly shared". |
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
