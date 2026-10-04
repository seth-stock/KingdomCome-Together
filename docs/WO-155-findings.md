# WO-155 — when the host is on rails, the friend chooses: join, or stay in the open world

Kingdom Come: Together. Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial;
not affiliated with or endorsed by Warhorse Studios or PLAION.

Written 2026-10-03 on `feature/wo-155-join-or-stay` (on top of WO-154, `docs/WO-154-findings.md`).

Marks: **(unit)** / **(wiring)** / **(synthetic)** tests without the game · **(code-verified)** · **(data)** read in the game's data ·
**(not observed)** built, not seen working, with the reason. Nothing in this work has been seen with two players, and nothing
was run in the real game this round.

## 0. The ask, and the answer

> Find all instances where the client will need to be brought in with the host. In any instance where the host will be locked in
> for a prolonged time, let the client select whether they join the host in the "locked in" period (sometimes one quest,
> sometimes spanning multiple), or persist in the truly open world. Any time the host is put "on rails", let the client select.

| Asked | Built |
|---|---|
| Find every way a friend is brought to the host | §1: twelve paths, from the code and the game's data. Each is now either the friend's choice, or exempt while they stay in the open world, or (two cases) unavoidable and said so |
| A choice for a prolonged lock, one quest or several | **Periods** (§2): the story's locked stretches, one choice per stretch. The final set is six quests and one question |
| Join, or persist in the truly open world | **F11 join / F12 stay** on a 30 s question, or a standing answer, `mp_story_join ask|join|free`. Staying = left alone: not brought, not tethered, not leashed, and the host's quest steps held so the engine cannot start the host's scenes on you (§3) |
| Any time the host is on rails | The story periods, plus the host's cutscenes: **F12 on the cutscene notice now also means "do not bring me along after it"** |

## 1. Every way a friend is moved to the host (the inventory)

Found by reading every place the agent moves a player, every message that tells one where the host's story is, and the game's own
data (`docs/WO-149-cutscene-census.csv`: about 6,450 rows over the 32 main quests and the rest of the game).

| # | What brings the friend | Where | Before | Now |
|---|---|---|---|---|
| 1 | A locked story period begins | `GameBridge.Wo153Story` → `Wo155Bring` | brought beside the host at once | **asked**; brought only if they join |
| 2 | The tether in a period (120 m, a tighter leash) | `W153WarnM/PullM`, the leash tick | held within 120 m | applies to those who joined; **not** to those who stay |
| 3 | A region change (the move to Kuttenberg) | `Wo153AfterLevelAsync` | brought along when the host's world was up | inside a period: the period's choice. Outside one: automatic, unless they stay |
| 4 | A host cutscene that moves the host | `Wo153Evaluate` → `Wo153NoteScene` | brought along after it | **F12 on the notice = not brought along after that scene**; those who stay in a period are exempt |
| 5 | F11 "watch" on the cutscene notice | `Wo153WatchAsync` | the friend's own request | unchanged (it is the friend's choice already) |
| 6 | The host fast-travels | `Wo114NoteHostFastTravel` | every friend comes along | unchanged for those who joined or are outside a period; **exempt** while they stay or have not answered |
| 7 | The ordinary distance leash (650 m, countdown, pull) | `Wo114HostTickAsync` | pulled back | unchanged normally; **exempt** while they stay (it would defeat the choice) |
| 8 | The host loads a save | the join machinery (WO-112/124/125) | friends reload the host's world | **unavoidable**: the world is the host's. Not a rails event, not changed |
| 9 | A friend connects, or reconnects, mid-period | the host repeats its beat every 30 s | learned late | asked from their first sight, with their own 50 s |
| 10 | The engine's own copy of the host's scenes, locks and teleports, started by the mirrored quest steps | the quest mirror (WO-137) | applied to everyone | applied to those who joined; **held** for those who stay |
| 11 | Shared sleep | the vote (WO-140) | a yes/no already | unchanged |
| 12 | The host plays as someone else (a "player switch") | the non-Henry hold | nobody can be placed beside a different character: no pull | unchanged: nobody is moved; the friend is in the open world already |

**Item 10 is the one that matters for "truly open".** A pull is one way the host's story reaches a friend; the other is the
engine itself, which plays its own copy of every scene the mirrored quest steps start (WO-149: 337 scene handlers in the main
quests alone; game-wide 260 teleports and 21 player switches). Stopping the pulls alone would leave a friend free to walk and yet put on
rails by the next quest step. So staying also holds the host's quest steps (§3).

## 2. The periods: what a friend chooses about

> **Superseded by WO-156 (`docs/WO-156-quest-gating.md` §3):** the table below was the first reading, from the user's list and the census. WO-156 read the
> game's own data for all 201 quests and the published guides: the periods, their names, the story's order (M30 "Last Rites" is the prologue, not the
> move to Kuttenberg), and which quests are open (the Devil's Pack is an open hub) are corrected there, and side quests, tasks and activities are covered.

A **period** is a run of consecutive locked sections that read as one stretch of story. An open quest between two locked ones ends
a period. From `StorySections.cs` (the repo's registry, `docs/WO-94-mainquest-registry.csv`; titles are Warhorse's):

| Period | Quests | The host's words | Census evidence (rows over the quests) |
|---|---|---|---|
| M01 | M01 Easy Riders, M02 Fortuna | the opening of the story | 11 input locks, 5 videos, escort/follow ×4, fights ×3 |
| M05 | M05 Wedding Crashers, M06 For Whom the Bell Tolls | the wedding in Semine and Trosky castle | 24 input locks, 2 player switches, 129 + 28 lock rows |
| M09 | M09 For Victory!, M10 Divine Messenger, M11 The Finger of God | the battle of Nebakov | fights ×18, teleports ×14, 3 player switches, 17 input locks |
| M12 | M12 Storm | Trosky castle | 6 input locks, teleports ×4 |
| M30 | M30 Last Rites, M31 The Sword and the Quill | the move to Kuttenberg | 10 input locks, 2 player switches, 4 videos |
| M32 | M32 Speak of the Devil | the devil's job | 5 input locks, teleports ×5, fights ×5 |
| M37a | M37a The King's Gambit, M37b The Feast | the meeting at Rattay | 7 input locks, 4 player switches, teleports ×11 |
| M38 | M38 The Devil's Pack | the devil's job | 7 input locks, fights ×7 |
| M42 | M42 Exodus | the burning of the Jewish quarter | fights ×12, escort/follow ×11 |
| M44a | M44a The Lion's Den | Sigismund's camp | 8 input locks, 71 lock rows |
| M44b | M44b Dancing with the Devil | the devil's job | 7 input locks, a video |
| M45 | M45 Oratores | the cardinal | fights ×13, teleports ×7 |
| M46 | M46 The Italian Job | the Italian Job | 6 player switches, fights ×13 |
| M47 | M47, M48a, M48b, M48c, M49, M50, M51 | the final set | 22 input locks, 5 player switches, 5 videos |

**Changes from 0.44.0's table**, from reading the census for every main quest rather than only the user's list: M01, M02, M09, M10,
M11, M37a and M37b are now locked. They were open before on a judgement ("riding and exploring"). The data says otherwise:
the opening is escorted and filmed, the Nebakov quests are a battle (18 fights, 14 teleports) with the host playing as someone else,
and the Rattay quests have four player switches. This is still a judgement, not something the game reports; a false "locked" costs
the friends one question, a false "open" costs them being left behind. **M03, M07, M08, M33, M34, M35 stay open** (exploring, a
mine, a rescue). Any of it is one line in `StorySections.cs` (`Locked` and `Why`; `JoinsPrevious` joins two quests into one period).

**Still a guess, asked again: "the job with the dry devil."** I took it to be the three "devil" quests: M32 Speak of the Devil,
M38 The Devil's Pack, M44b Dancing with the Devil. If you meant another, say which.

## 3. What the choice does

**The question.** When the host enters a period, each friend sees *"Your host entered the final set. Join them, or stay in the open
world?"* for 30 s, with **F11 join your host** and **F12 stay in the open world**. Nobody answering is a **join**: the friends stay
together unless one opts out. A friend can also answer by command (`mp_story_come`, `mp_story_stay`) at any time, and **F11 joins
while they are staying**. The standing answer `mp_story_join ask|join|free` skips the question (`join` is exactly 0.44.0's behaviour).

**Join** — brought beside the host (the leash's own pull: dismount first, nobody within 50 m is moved, nobody busy), held within the
tether (120 m), the host's quest steps applied. Said in words: "You joined your host for the final set."

**Stay (free)**, for as long as the period lasts:

* the host does not leash, tether or bring them: the leash tick skips them, a running countdown is cancelled on their screen, the
  fast-travel come-along and every scene bring-along skip them (`RailsRoster.Exempt`, asked at every one of those places);
* their own game refuses a pull if one ever arrives (the second lock), and the host's roster shows them as free;
* **the host's quest steps are held**: the quest mirror is paused for them the way it is during a load (`W151MirrorHolding`), so the
  engine cannot start the host's scenes, locks or teleports on them. Their own quest steps are not shared meanwhile;
* no cutscene notice and no region notice (they chose not to follow); a quiet label says *"Open world: your host is in the final set.
  F11 / mp_story_come: join them"*;
* the launcher says where they stand, and the host sees "the final set: 1 joined you, 1 staying in the open world, 0 deciding" and a
  message each time a friend answers.

**The end.** When the host's last section of the period ends (or the host goes quiet for 90 s, or leaves), the friend is told
*"the final set is over. You are back with your host: the leash applies again."* The quest mirror comes back the way it does after any
pause: the held steps are dropped and **the host's own checkpoint is compared**, as for a late joiner. The ordinary 650 m leash
applies again (a warning and a 10 s countdown first, so a friend who strayed far is not snapped back without notice).

**Between sections of one period** (the final set's six quests) nothing is asked again and nothing ends: the answer stands for the
period. A different period asks again, even straight after.

**Through the wire.** The answer is a StoryBeat (kind 11, `"M47 join"` / `"M47 free"`, any code of the period names it), repeated every
20 s so a lost message or a host that reloaded heals. A friend that has not answered 50 s after the period began is taken as joined
and brought. "Keep playing" on a cutscene is kind 12 (`"stay"`), used once and lapsing. Both are additive: the relay passes beats on
untouched, and only the host reads them. The leash state gained a `free-roam` flag (informational: the host's log shows it).

## 4. What is not covered, and why

* **The engine's reaction is unobserved.** Holding the mirror for a friend is a state the mod has used for loads, not for half an hour
  of play. What the game does with the friend's own world meanwhile, and how well the checkpoint compare brings it back after a long
  period, is for the first two-player run to say (§6). If a friend's story looks wrong after a stay, the reliable repair is the
  old one: quit to the main menu and rejoin the host's world.
* **A friend who stays across the move to Kuttenberg** stays in the old region (the host's level change is not applied to them). On
  return their game has to follow into the new one; whether the compare carries it is unobserved. Rejoin from the main menu if not.
* **Their own quest steps while they stay are not shared**, and are put back to the host's values at the compare. Roam, fight and
  loot as you like; do not expect a side quest finished in the open world to be kept by the host's world.
* **Side quests, DLC, and any stretch the table does not name** are not "locked", so no question. The host's world still moves a
  friend on a fast travel or a scene they did not opt out of (rows 4 and 6).
* **The host reloading** (row 8) cannot be a choice: the world is the host's.
* **"Locked" is a judgement from the data.** No read exists in the engine; the table is the maintainers' reading.

## 5. Verification

| What | Result |
|---|---|
| `Wo155Tests`: periods (which quests form which), the wire text, the host's roster, the friend's state machine, the scene-stay book, the launcher words | 93 tests **(unit)** |
| `Wo155WiringTests`: a real `GameBridge` against a recording stand-in for the game: a new period exempts everyone and moves nobody; joining brings, staying does not; fast travel and scenes skip those who stay; "keep playing" is used once; the next section keeps the answers and the end releases everyone; staying holds the mirror, sets the flag and refuses a pull; joining gives them back; a standing answer asks nothing; no scene or region notice for one who stays; the host leaving, a reset and a switched-off story lock end it; the launcher says where each stands; a short gap between two quests of one period; the end of a scene that began before the choice | 36 tests **(wiring)** |
| `Test-WO153Synthetic.ps1` (the real kdcmp.lua under MoonSharp): the question, its countdown and timeout, F11/F12 and that the generic accept never answers, F11 joining while staying, the question and a cutscene notice answered by one press, the commands, the words | 164 / 164 **(synthetic)**, 41 of them new |
| Agent tests | 1136 / 1136 (the whole `KcdMp.Client.Tests`); relay 62, Farkle 59 |
| All 43 offline Lua suites, the three static checks, `dotnet build KCD2-MP.sln`, pak == source | 0 failed, 0 errors |
| The engine's reaction; two players; the launcher banner | **not observed** |

## 5b. Defects found by review and fixed

An independent high-effort code review of this work found seven things; each was checked against the code before acting.

* **The host's wait for an answer (50 s) was shorter than the worst case of asking** (a friend who arrives mid-period hears the host's
  30 s repeat, then has 30 s): they could be pulled with the question still on screen. Now 75 s.
* **A friend who stayed dropped the host's scene-END edges**, leaving a scene that began before their choice in the tables (a later
  notice silenced, a pull worded wrongly). Only the start edges are skipped now.
* **The host let go of a period the instant its last section ended, the friend after 8 s**: a short gap between two quests of one
  period reset the host's view of every answer. The host keeps a period the same few seconds, and a restart in that time is the same period.
* **"You joined your host" stuck for 90 s** and could label an unrelated fast travel. It is said once, for the pull that follows the
  join, within a minute.
* Smaller: a late repeat of an answer after the period was logged as a mismatch (now silent); a friend who is gone is forgotten (their
  answer, their "keep playing"); an unused import and a duplicated parse.
* **Declined, with the reason:** a friend on a build that does not know this message never answers, so the host leaves them alone for
  the wait at each period start. The mod refuses to connect two release versions, so this cannot happen once the version is bumped
  for this change. **The version must be bumped before this ships** (a friend and a host on the same version string with different
  code would not be refused).

## 6. What a live run with two people must show (checklist items 117–124)

1. A period begins: the friend sees the question; **nobody is moved before they answer**. F11 brings them beside the host; F12 leaves
   them where they are.
2. A friend who stays can walk 1 km away: no warning, no countdown, no pull; the host's log says `MP-W155 host: joiner N STAYS`.
3. The host fast-travels while a friend stays: the friend is **not** brought. (They are, outside a period.)
4. A friend who stays does not see the host's cutscene, and the game does not start it on them. **This is the one that may fail**:
   write down anything that locks the friend's input, moves them, or plays a scene while they stay.
5. The period ends: the friend is told, the leash is back, and their quests agree with the host's (the launcher says IN SYNC; if it
   says DRIFTING, rejoin from the main menu and report what differed).
6. The final set (six quests): one question, not six.
7. F12 on a host cutscene: the friend is not brought along after it; F11 still stands them beside the host.
8. `mp_story_join join` is 0.44.0's behaviour; `free` never asks and always stays.

Logs: `MP-W155` (agent), `WO155-` (kcd.log), the host's `MP-W155 host:`.
