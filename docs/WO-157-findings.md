# WO-157 — characters, saves and DLC

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse Studios or PLAION.
Written 2026-10-06 on `feature/linux-support`. Version string unchanged (the user picks versions).

Marks: **(unit)** tests without the game · **(real saves)** run offline on the real saves on this machine, in memory, nothing written ·
**(wiring)** built and called, not seen · **(live)** seen in the real Modding Tools game on this machine · **(not run)** not run at all.
**Nothing here has been seen with two players.** A live pass on 2026-10-06 (§6) ran the real game, the real agent and plugin and a real relay against a *synthetic* host (a copy of a real world, served by `tools/wo118/synthpeer`); what it proved and what it did not is in §6.

## 0. The ask, and the answer

| # | Asked | What exists now |
|---|---|---|
| 1 | Linux support | Done in the previous WO ([LINUX.md](LINUX.md)); not tested under a real Proton |
| 2 | Multiplayer in the prologue and the scenes played as someone other than Henry | **Built, not seen.** The only other character in KCD2 is **Godwin** (the census's 22 player switches: M30 the prologue, M05, M10, M37a/b, M46, M48c, M50). A host in a Godwin world can now be joined; the joiner's Godwin comes from the joiner's own saves (§2) |
| 3 | New Henrys allowed; bring a Henry from another world; put him back in his own world | New Henrys (*Start fresh*) and bringing a Henry (*Bring my character*) already existed (WO-125). **New: send him home** with what he gained (§1) |
| 4 | Every kind of quest, DLC included | All 201 quests were already catalogued and gated (WO-156). **New: DLC quest changes are now shared** like every other quest's; before, they were kept out on purpose (§3) |
| 5 | The host saves for host and client; and each can save separately, at other times and places | The host's saves already carry both (the joiner's Henry pairs with each host save, WO-125). **New: two ways to save separately** (§4) |

## 1. Send Henry home (item 3)

The joiner who brought a character into a host's shared world can put him back:

* `mp_henry_home` — now, at any time: the Henry **as he is right now** (a QuickSave of the joiner's copy; if the game will not save, the last host save's copy) is
  spliced into **the world he came from** and written as a **new manual save** (`saveNNN.whs`, in the playline of that world).
* `mp_henry_home playlineN/file` — into a named own save instead.
* `mp_henry_home_on_leave on|off` — do it whenever this game leaves the host's world (default off: leaving changes nothing at home unless asked).
* After the player has already left, `mp_henry_home` uses the last stored copy of that world's character.

What the result is (`HenryHome.cs`; the splice is the WO-124 one, run the other way round):
* The **own world's save is the "world" of the splice**: its story progress, renown, soul list, quests, map and every other block are the player's own, byte for byte (the check proves
  it block by block). The Henry arrives with skills, stats, perks, money, equipment and items.
* Quest items of the **shared world's** quests are left behind (they mean nothing at home); the home Henry's own quest items **stay** (a home quest is not broken by the trip).
* **A new file, never over one.** The old save is untouched; the header is the home save's turned into a manual save's (type, id, time), so it shows in the menu as an ordinary save.
* Which world is "home": remembered at the first join that brought a character (the **tag** of the seed, never the seed). A character that *started fresh* has no home: name a save.
* Refused with a sentence, nothing written: a home save that does not verify, is a different game build, or whose player is not that character; a playline with no free save number.
* The saves watcher knows the file (it would otherwise call a new save in a playline a "leak" and move it out).

**(unit)** 17 tests: the builder, quest items both ways, "every other block is the home world's", the header rewrite, the id counter (one counter for autosave/quicksave/permanent/manual,
exit.whs carries one in its header), refusals, "the home file is never modified". **(real saves)** the two real playthroughs on this machine, both directions, newest save of each:
**4 of 4 built, every one passed the full check and the footer verify** (250–630 ms; e.g. 1,174,015 B home + a 246-item Henry -> 969,033 B with his 98 items). **(wiring)** the command, the
snapshot, placement, rescan, the leave route. **(live)** the engine lists it as a manual save, loads it, and the live Henry matches the file exactly; the whole flow worked in a real session (§6). **(not run)** Continue ordering with several playlines.

## 2. Godwin: the prologue and the other stretches (item 2)

What the engine does: in a Godwin scene the player entity is bound to the soul `player_bohuta` instead of `player_henry`. The two records have **the same fields** (name, main block, renown,
inventory, 1303), so the whole machinery generalises by making "whose record" a parameter that defaults to Henry:

* `WhsSave`: the splice, its check, the stored block and the player test take the soul (`HenryParts.Soul` = the record's own GUID). Henry behaves exactly as before (all 1,225 earlier client tests pass unchanged).
* **A Henry is never spliced into a Godwin world and a Godwin never into a Henry world** (a guard in the splice itself).
* The wire: a new session flag `SessionGodwinWorld = 4`, sent **instead of** `SessionHenryWorld`. An older peer does not know the bit, reads "not Henry" and refuses, as it always did: no protocol bump.
* The join: the host's world says whose character it needs; *Bring* = the joiner's newest own save with that character; *Start fresh* = the earliest one (the prologue's own start save, which
  every new game has). Quest items are stripped as for Henry.
* Mid-session: if the host's story switches character (Henry ⇄ Godwin) the joiner's pairing snapshots **pause** with a message until the joiner's own game has switched too (the snapshot
  must be of the character the world was joined as). Godwin has no "home" to be sent to; `mp_henry_home` says so.

**(unit)** 8 tests on a synthetic Godwin world (recognition, the splice and its check, Henry's record untouched, the stored block's soul, both guards, the flag bits).
**(real saves)** the one real Godwin save on this machine (a 1.4.2 prologue save): verifies; recognised as Godwin; parts soul = Godwin; **self-splice passes the full check in both quest-item modes**; the stored block round-trips.
**(live)** the engine loads a Godwin world made through the generalised splice (the real prologue save, identity splice): "Gameplay started", the live player is Godwin (`char_BOHUTA_uiName`) with his own money and items, the mod boots, 0 mod errors. **(not run)** a Godwin JOIN (a Godwin host, a second player); the post-load character check against a Godwin's live money/items (it compares what the file says with what the live player says; Godwin's
inventory is small, so the "file lists only money / no list" warnings may apply); two players in the prologue; the switch mid-session. **Honest limit:** this machine has one Godwin save and it is from an older build, so the
Godwin splice has never met a same-build pair of real saves.

## 3. DLC quests (item 4)

* The quest catalogue, the tiers and the join-or-stay machine already covered **all 201 quests, 74 of them DLC** (WO-156); that is unchanged.
* The quest-state **mirror** (WO-137: the host's quest changes go to the joiner, the joiner's ask the host) deliberately excluded DLC (three `RequiredDLC` roots and any `dlc*` segment). It no longer does:
  `Wo137Rules.DlcShared` is **on by default**. Reason: a joiner runs the host's own world save, which names the DLCs it needs, and the engine does not load a save whose DLC is not active, so both games have the
  DLC's quest graphs. **`mp_quest_dlc off`** brings the old rule back both ways (the switch if a DLC questline misbehaves). Every other veto (per-machine, silent, not-a-quest, bad path) still holds with it on (**unit**).
* **(not run)** any DLC questline with two players; the DLC levels (the Sedlec monastery is a separate level with its own level switches): what a joiner does when the host changes level is not something this WO touches.
* Nothing checks that the joiner OWNS a DLC the host's world uses beyond the engine's own refusal to load the save.

## 4. Saving separately (item 5)

* **The host's saves cover both** (WO-125, unchanged): every host save pairs with a copy of the joiner's character; a host reload rewinds the joiner to the matching copy.
* `mp_henry_home` (§1) is a **separate save of the joiner's progress** into his own world, at any moment and place of the joiner's choosing, without leaving and without touching the host.
* `mp_world_copy [N]` — **a copy of the shared world, with the joiner's character in it, as the joiner's OWN save** (playline N, default the first playline with no saves), to carry on from alone later,
  whatever the host does next. A new file; the host and the others are not told. It is the engine's own QuickSave of the joiner's copy with the header turned into a manual save's: the world is byte for byte (**unit**).
  **(live)** the file loads in the engine (the host's world at the host's spot, with the joiner's Henry and his gain);  and note that while joined to that same host the copy has the host's playthrough seed, so the mod's own rule ("a save with the host's seed is
  never the joiner's own") skips it as a Bring source until the player leaves that host.
* Everyone playing in their own world with the others as presence (the mod's original mode, `mp_shared_world off`) already saves separately: each has their own saves and time.

## 5. Commands added

| Command | Does |
|---|---|
| `mp_henry_home [playlineN/file]` | send the character home as a new manual save |
| `mp_henry_home_on_leave on\|off` | do that whenever this game leaves a host's world |
| `mp_world_copy [N]` | save a copy of the shared world, with the character, as an own save |
| `mp_quest_dlc on\|off` | share DLC quests (default on) |

Static Lua checks pass with them; `kdcmp.pak` rebuilt from the source.

## 6. The live pass (2026-10-06): what was proven, and what was not

Setup: the Modding Tools game 1.5.5 at the main menu, the plugin injected by process name (`--process/--module/--wait`), a local relay, the **real agent as the joiner**, and
`synthpeer --join-host125` as the host serving a **copy** of one real playthrough ("world B"); the joiner's own world is a copy of another real playthrough ("world A") with a fresh time,
placed in a throwaway `playline4` so every file the agent writes lands there. Your real saves were hashed before and after: **138 files, 0 differences**; the throwaway playlines are deleted.

| # | Check | Result (live) |
|---|---|---|
| 1 | The four console commands exist, log `WO157-CMD`, reject bad arguments | yes (10 commands tried, 7 events emitted); the mod boots, 0 errors from the mod |
| 2 | A Henry sent home **loads in the engine** | the engine lists it as `ManualSave` (`2|1|@qname...`), loads it ("LoadGame ... done"); the live Henry matches the file exactly: money 1964 = 1964, **78 item classes (161) with 0 differing**, the join's own `JudgeHenry` says MATCH. The 19 "Soul ... created during load" warnings are the same 19 as a load of the unmodified home world |
| 3 | A Godwin world through the generalised splice **loads** | yes: "Gameplay started" after 115 s, the live player is `char_BOHUTA_uiName`, money 48.70, 28 items, 0 mod errors |
| 4 | The whole joiner flow, **after all the Godwin/soul changes** (a regression check of the join) | first join (*Bring*), splice (check PASS), place, load, Ready after ~74 s, in the shared world |
| 5 | `mp_henry_home` **in a live session** | `MP-HOME DONE`: the live snapshot (a QuickSave, moved out as designed), built into the home world, written as `saveNNN`, read back, listed by the engine, **not** treated as a leak, 2.4 s |
| 6 | The Henry **comes home with what he gained** | I gave him 7 bandages in the shared world. After the host left, the leave route loaded the new save: compared with the Henry before the trip, **exactly one class total differs: bandages 153 -> 160**; money, stats and every other class are identical; the home world's seed is unchanged; the in-game count (160) matches the file |
| 7 | `mp_henry_home_on_leave on` | `MP-HOME DONE (leave: host-left)` built the save, the leave route **loaded it**, bandages 160 -> 165 (+5 given in the session) |
| 8 | `mp_world_copy 3` | `playline3/save001` written (verify ok, listed); the engine **loads it**: the host's world at the host's spot with the joiner's Henry and his gain; its temporary QuickSaves moved out |
| 9 | The real saves | byte-identical (138 files); no `mpworld*`/`mpexit*`/`.part` left anywhere |

**Not proven:** two real players; a Godwin **join** (a Godwin host with a second player) and the mid-session Henry/Godwin switch; DLC quest sharing in a session (`mp_quest_dlc`; no DLC questline was played); the Sedlec level
switches; Continue ordering across several playlines; Linux/Proton (separate WO).

### What the live pass found

* **A home save must carry the same build string as the snapshot.** The engine's own QuickSave stamps `1.5.5-release_1_5`; saves written by another executable of the same version can carry a build number
  (`1.5.5-15315-release_1_5`). The existing WO-135 rule is strict equality, so such a home save is refused ("No save of the world this character came from (of the same game version) was found"). Nothing was written.
  In normal use the player's own saves are written by the same Modding Tools build, so they match; this showed up only because my test copies were not. (Header text only was changed in the test copies.)
* **The join's character check warns on big inventories** (`WARNING ... item classes file=170 live=65`): the live inventory travels in one log line that the engine cuts at about 3.9 KB, so a Henry with ~230 items is compared
  from a truncated read. Money matches, the item **count** matches the file exactly (231 = 231), and the check only warns by design (WO-144 1.4). Existing behaviour, not caused by WO-157; the home save is built from the
  engine's own save, not from this read.
* The "Lua error" lines in `kcd.log` during the pass were the engine's own door script (`animdoor.lua:579`) and my own one-liners run while the player did not exist; none came from the mod.
* An interrupted run (the machine was switched off mid-test) left two game-written autosaves in my throwaway playline only; nothing outside it was touched.

## 7. Files

`dotnet/KcdMp.Client/HenryHome.cs` (the pure builder), `GameBridge.Wo157.cs` (the commands and the flow), `HenryStore.cs` (the home world), `WhsSave.cs` / `WhsSave.Wo125.cs` (the soul parameter, the guard),
`GameBridge.Wo125.cs` / `Wo124.cs` / `Wo122.cs` (the Godwin identity, the post-load character, the watcher), `Wo137.cs` / `GameBridge.Wo137.cs` (the DLC switch), `KcdMp.Protocol/ProtocolWo123.cs` (the flag),
`kdcmp/Data/Scripts/Startup/kdcmp.lua` (four commands). Tests: `HenryHomeTests.cs`, `GodwinTests.cs`, `Wo137Tests.cs`, `WhsSaveTests.cs` (a Godwin fixture).
