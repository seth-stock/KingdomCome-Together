# WO-157 — characters, saves and DLC

Kingdom Come: Together. Unofficial; not affiliated with or endorsed by Warhorse Studios or PLAION.
Written 2026-10-06 on `feature/linux-support`. Version string unchanged (the user picks versions).

Marks: **(unit)** tests without the game · **(real saves)** run offline on the real saves on this machine, in memory, nothing written ·
**(wiring)** built and called, not seen · **(not run)** not run at all. **Nothing here has been seen with two players, and nothing in this WO has been run in the
real engine yet** (the maintainer's machine was in use for another game when this was written): the first live pass is §6.

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
snapshot, placement, rescan, the leave route. **(not run)** the engine loading the result as an ordinary manual save, the menu showing it, Continue ordering.

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
**(not run)** a Godwin join in the engine; whether the engine accepts a spliced Godwin world; the post-load character check against a Godwin's live money/items (it compares what the file says with what the live player says; Godwin's
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
  **(not run)** that the engine loads a manual save placed in a fresh playline; and note that while joined to that same host the copy has the host's playthrough seed, so the mod's own rule ("a save with the host's seed is
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

## 6. What the first live pass must show (not done)

1. The four commands exist in the console and log their `WO157-CMD` lines; the mod boots with 0 Lua errors.
2. `mp_henry_home` after a join: `MP-HOME DONE ...`; the file is listed by the engine's rescan; it **loads from the menu**; Henry has what he gained; the home world is as it was; the original save is untouched.
3. `mp_world_copy`: the file loads, as its own playline.
4. A Godwin world (a prologue save as the host, a synthetic or real second player): the join is offered, the splice passes, the world loads, the character check does not abort; then the mid-session switch.
5. A DLC quest change on the host reaches the joiner, and `mp_quest_dlc off` stops it.
6. The real saves are byte-identical afterwards (hash the saves folder before and after, as in WO-124/125).

## 7. Files

`dotnet/KcdMp.Client/HenryHome.cs` (the pure builder), `GameBridge.Wo157.cs` (the commands and the flow), `HenryStore.cs` (the home world), `WhsSave.cs` / `WhsSave.Wo125.cs` (the soul parameter, the guard),
`GameBridge.Wo125.cs` / `Wo124.cs` / `Wo122.cs` (the Godwin identity, the post-load character, the watcher), `Wo137.cs` / `GameBridge.Wo137.cs` (the DLC switch), `KcdMp.Protocol/ProtocolWo123.cs` (the flag),
`kdcmp/Data/Scripts/Startup/kdcmp.lua` (four commands). Tests: `HenryHomeTests.cs`, `GodwinTests.cs`, `Wo137Tests.cs`, `WhsSaveTests.cs` (a Godwin fixture).
