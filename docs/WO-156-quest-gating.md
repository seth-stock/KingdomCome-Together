# WO-156 — the gating on every quest of the game

Kingdom Come: Together. Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial;
not affiliated with or endorsed by Warhorse Studios or PLAION.

Written 2026-10-03 on `feature/wo-155-join-or-stay` (WO-155's choice, now fed by the whole game's quests).

Marks: **(online)** read in a published guide (§7) · **(data)** read in the game's own quest data or the cutscene census ·
**(unit)** / **(wiring)** tests without the game · **(not observed)** built, not seen working. Nothing here has been seen with two players.

## 0. The ask, and the answer

> Search online for the main quests, side quests and activities, so the gating logic works on all missions: the friend joins the
> host (or not) for missions that lock you in — for one mission, several, a period or an area of the game.

| Asked | Done |
|---|---|
| Know every mission | The game's **own data** lists 201 quest roots (32 main, 59 side, 52 task, 57 activity, 1 event; 74 of them DLC), with the English titles. The published guides say, for each, what kind of content it is. `tools/Build-QuestCatalog.py` joins them (§1) |
| The gating works on all of them | Every quest has a **tier**: *open*, *mixed* (scripted stretches alternate with free ones) or *rails* (staged throughout). 81 of the 201 put the host on rails or mixed; the rest are open-world. The state machine handles main quests, side quests, tasks, activities and DLC, in the story's real order (§2) |
| One mission, several, a period, an area | A **period** is the set of quests that share one question: the opening (3 quests), the wedding (2), the Nebakov campaign (4), the final act (9), the Dry Devil's two jobs (one each), a fight club bout (one) (§3) |
| A large verification pass | 201 quests × the path reader, the state machine and the plan; a scripted playthrough of the whole story; three defects found and fixed (§5) |

## 1. How the whole game is known

* **The game's data** (`Quests/Final` in `Scripts.pak`, the Modding Tools' build 1.5.5): every `<Quest … ProductionCode="…">` root, with its
  name, level, folder, type, and English title (`text_ui_quest.xml`). Codes: M main, S side, U task ("micro"), A activity, E event. **(data)**
* **The cutscene census** (`docs/WO-149-cutscene-census.csv`): per quest, how many input locks, player switches (the host plays as someone
  else), fights, escort/follow nodes, teleports and videos. **(data)**
* **The guides** (§7): per main quest, which phases are scripted and which free; per side quest and task, staged or open; the two points of no
  return; that The Devil's Pack is an open hub. **(online)**
* **The plan** (`docs/WO-156-quest-gating-plan.csv`): the reviewed answer for every quest, with its source and a note. The tool refuses to
  build if a quest has no plan row, a plan row has no quest, a period is not named by its own first quest, or two members of a period disagree
  about its name. The table of all 201 is `docs/WO-156-quest-gating-table.md` (generated).

To change an answer: edit the plan CSV, run `python tools\Build-QuestCatalog.py`, run the tests. (`--check` fails when the files drift from the game.)

## 2. The tiers, and what each does

| Tier | Means | Entered by | Left by | Tether |
|---|---|---|---|---|
| **rails** | staged for its whole run: a siege, a castle, a heist, a tournament, the locked world of the finale | a main quest: its first State; a side quest, task or activity: two distinct States | its end, a later main quest, 40 min without a NEW State (main) / 15 min (other) | yes, a main rails quest only: 120 m, for those who joined |
| **mixed** | scripted stretches alternate with free ones (the wedding, the Dry Devil's rescue, most Kuttenberg quests) | a burst: 3 DISTINCT States of the quest within 90 s (a lone background tick, or one timer node flipping, enters nothing) | its end, a later main quest, 12 min without a new State (main) / 8 min (other) | no: the ordinary 650 m leash, because a quest with free stretches must not hold a friend at 120 m while the host roams |
| **open** | open-world content: you go where you like | never | — | — (a scripted scene inside it is the cutscene notice's business, WO-153) |

Rules that apply to all kinds (the machine is `StoryLock`, pure and tested):

* **The story's own order decides "later".** The main quests are numbered in the game's order, not by code: **M30 "Last Rites" is the
  prologue (played as Godwin) and the first quest of the game**; M31 "The Sword and the Quill" opens Kuttenberg. A later main quest ends an
  earlier section **once it shows evidence** (its first State if it is on rails, a burst otherwise): one stray flag of the next quest does not
  end the section the host is still in. An earlier quest's background States are ignored and never re-enter one.
* **A side quest, task or activity** enters its own section, unless the host is in a main **rails** section (a locked world has no room for a
  side quest's timers). It replaces another section only when the host has been quiet in that one for 30 s (an unrelated quest's tick never
  takes a section the host is busy in), and a section that is replaced is not cooled down. The host's current main quest takes a side section
  back the same way, when its staged part begins after a quiet 30 s.
* **A side quest that ended** can be entered again after 90 s (a fight club's next bout); one that merely **idled out** only after 10 minutes (its
  background timers keep ticking). A section is kept alive only by NEW States: a timer cycling through the same few nodes does not.
* **A quest nested in another quest's module is the deeper one.** The task "A Moment of Fame" lives inside a fight club's folder, so its
  States carry the fight club's name; it resolves to the task (open), not the fight club.
* **A quest with markers** (Oratores) is entered only by a State inside its staged modules (§4).
* **After a load** the host's own checkpoint names the quest the story is at, not where the host stands, so only a quest staged throughout
  (rails, no markers) is seeded from it; a mixed quest or Oratores waits for the host's own States, which a load restarts.
* The friend's choice (WO-155) is per **period**; the tether is the host's, only in a rails section; everything the friend's "stay" does is
  unchanged (`docs/WO-155-findings.md` §3).

## 3. The periods: the stretches a friend answers once

Main story, in the game's order (open quests M07, M31 and M38 are between them and ask nothing):

| Period | Quests | Tier | What the guides say |
|---|---|---|---|
| the prologue, played as Godwin | M30 Last Rites | rails | Godwin on the castle walls: crossbow, courtyard, ladder; ~20 min, no roaming or fast travel |
| the opening | M01 Easy Riders, M02 Fortuna, M03 Laboratores | mixed | a locked ride and escape, a free camp; a hut, a meadow, an alchemy bench; a tavern you cannot leave |
| the wedding in Semine and Trosky castle | M05 Wedding Crashers, M06 For Whom the Bell Tolls | mixed | free entry quests, locked at the wedding, a locked brawl; a cell, free chores, the chamberlain |
| the raid on Semine | M08 Necessary Evil | rails | every phase locked: torture, a forced ride, an estate battle, a tower |
| the Nebakov campaign | M09 For Victory!, M10 Divine Messenger, M11 The Finger of God, M12 Storm | mixed, M12 rails | the area lock: four quests before Trosky is open again; M10 is played as Godwin; M12 is the escape from Trosky |
| **the Dry Devil's rescue** | M32 Speak of the Devil | mixed | **the first Dry Devil job**: follow Kubyenka, dig for the weapons, the ambush on the soldiers carrying him |
| Into the Underworld | M33 | mixed | open taverns, a locked ambush, a locked infiltration timed to 5 AM |
| Via Argentum | M34 | mixed | a locked palace scene, an open-hub investigation, a locked mint; 2–3 hours |
| Taking French Leave | M35 | mixed | a locked palace, an open excavation, a locked tunnel, stealth, a locked rescue |
| the council at Raborsch | M37a The King's Gambit, M37b The Feast | mixed, M37b rails | a 10–15 minute council-serving sequence; a courtyard battle fought as Godwin |
| the burning of the Jewish quarter | M42 Exodus | rails | locked from the tunnel entrance: inn, escort, synagogue |
| Sigismund's camp | M44a The Lion's Den | mixed | a locked enlistment, free investigation, a locked trial and cannon battle |
| **the Dry Devil's assault on Malesov** | M44b Dancing with the Devil | rails | **the second Dry Devil job**: mission-locked village assault, castle infiltration, the duel with the Dry Devil |
| **the final act** | M45 Oratores (from the Ruthard courtyard), M46 The Italian Job, M47 Civitas Pragensis, M48a So it begins…, M48b Besieged, M48c Hunger and Despair, M49 Reckoning, M50 Last Rites, M51 Judgement Day | rails | **the second point of no return**: from the Ruthard courtyard the open world is locked to the end of the game |

**The Devil's Pack (M38) is not a Dry Devil job** — it is an open hub (recruit the gang in any order across the Kuttenberg region; 49 side
quests branch from it): open. The earlier guess in WO-154 that it was locked was wrong. **(online)**

Everything that is not a main quest and not open is its own period (a tournament, a fight club bout, a heist, a hunt):

* **rails (arenas, series, training)**: Kuttenberg Tournament; the seven fight clubs (Melee at the Mill, Wine Women and Blood, The Best for Last,
  Enough!, Fight Dirty, Teeth in the Bag, Nail in the Coffin); the combat trainings (I, II, II); DLC: the forge guild's duels and tournament.
* **mixed (staged sequences)**: Mice, Frogs, the Battle of the Frogs and Mice, Invaders, Yackers 'n' Fash, Striped Tonies, The Spark, Ars
  Dimicatoria, Lost Honour, Dragon's Lair, The Thieves' Code, Demons of Trosky, Hunting the Werewolf, the Popinjay Shoot, the White Roebuck,
  Skeleton in the Closet, Like Old Times, mounted archery, the horse races, Horses for Courses; The Jaunt and Ransom (the census shows heavy
  scripted escort/teleport content although the guide calls them open); DLC: Portrait in Red, God Disposes, Unveiling, Seek and you Shall
  Find, The Time has Come, The Master's Game, Martin's Dream, The Last Step, The Executioner's Pride, thieving contracts, archery contests.
* **open (everything else, 120 quests)**: dialogue, fetch, hunt, rescue, investigation. The scenes inside them are the cutscene notice's.

## 4. Where a quest has an open part and a staged part

A guide says Oratores has a point of no return: **entering the Ruthard Palace courtyard** locks the open world until the end of the game.
The quest's own data has the matching modules (`dobyvani_ruthardky` "taking Ruthardka", `ruthardka_daycycle`), so Oratores is entered only by
a State inside them; before that the host is free (planning, side content) and nobody is asked. This is the one quest with a marker. Other
mixed quests could be given markers the same way (the data shows the staged modules: the wedding's `hibernovana_cast`, the invisible-wall and
"Henry cannot leave" modules of the opening quests); that would make their periods follow the scripted stretches exactly instead of the quest
as a whole. It was not done: a marker is a claim about a module's meaning that only a run can confirm, and a mixed quest costs the friend at
worst one question. **(not observed)**

## 5. The verification pass: what it found

201 quests × (the path reader, the state machine, the plan) and a scripted playthrough of all 32 main quests in the game's order (the
roster must ask once per stretch and never for an open quest). It found, and fixed:

1. **The story order was wrong.** The earlier table numbered the main quests by code, which put M30 "Last Rites" (the prologue and the first
   quest of the game) after M12. A host starting a new game would have entered M30, then M01…M12 would have been taken for "earlier quests'
   background States" and ignored: no lock after the prologue, every later section confused. Now the game's own order.
2. **The Devil's Pack was locked and is an open hub** (above).
3. **A quest nested in another quest's folder was read as its parent**: the task "A Moment of Fame" (open) sits inside the fight club "Fight
   Dirty"; its States would have entered and refreshed the fight club's section. The deepest named quest on the path wins now.
4. **The first Kuttenberg and the Nebakov quests were mislabelled** in WO-154's table ("the move to Kuttenberg" was M30/M31; "the battle of
   Nebakov"). The names now come from the plan and say what the guides say.
5. **Whole-quest locks were too broad for hub quests** (the wedding is an hour of mostly free play around a few staged scenes). Quests with
   free stretches are *mixed*: no tether, a shorter idle time, a burst to enter.

6. **The second review** (high effort, of this work) found seven more, all checked and fixed: an unrelated side quest's tick could replace a
   section the host was busy in and cool down the real one for ten minutes; a module that shared a name with a quest could be read as the
   quest (matching is now by the quest's whole address: level, folders, name); the post-load seed bypassed the burst rule for mixed quests;
   one stray State of the next main quest closed the current section for good; a finished fight club bout blocked the next for ten minutes;
   a repeating timer node could keep a section alive indefinitely or satisfy "a burst"; the joiner's status claimed a tether for mixed quests.
   Not fixed, said plainly: a load inside the staged part of Oratores is not seeded (the checkpoint cannot tell which part), so that lock
   starts at the next State inside a marker module.

Totals: 38 new tests over the whole catalog (`Wo156Tests`, `Wo156NestingTests`); the story-machine tests (`Wo153StoryTests`, 94) were rewritten for the new model and the WO-155 tests (`Wo155Tests` 93 + 7, `Wo155WiringTests` 37) moved onto it; the whole suite is in §6.

## 6. Verification

| What | Result |
|---|---|
| The catalog against the game's data (`Build-QuestCatalog.py --check`) | 201 quest roots, plan complete, files match **(data)** |
| `Wo156Tests` (36) + nesting (2): count and uniqueness, titles, every locking quest found by its own State path (including the ones kept in folders), wrong level refused, enter/leave/idle by tier for every locking quest, the named missions lock, every player-switch quest locks, no open side quest hides heavy scripted content, periods are runs of the story's order, the whole playthrough | all pass **(unit)** |
| `Wo153StoryTests` (rewritten, 94) + `Wo155Tests` (93 + 7) + `Wo155WiringTests` (37) | all pass; tether only in a rails section **(wiring)** |
| Agent tests (`KcdMp.Client.Tests`) | 1204 / 1204 |
| The engine's reaction; two players; the launcher banner | **not observed** |

## 7. Sources

* Quest data: the Modding Tools' `Scripts.pak` and `English_xml.pak` (build 1.5.5), read by `tools/Build-QuestCatalog.py`.
* Main-story order and structure: [GameRant — all main story quests](https://gamerant.com/kingdom-come-deliverance-2-kcd2-all-main-story-missions-quests-list/),
  [PowerPyx — full walkthrough](https://www.powerpyx.com/kingdom-come-deliverance-2-full-walkthrough-all-main-quests/),
  [Fextralife — quests](https://kingdomcomedeliverance2.wiki.fextralife.com/Quests), and the per-quest walkthroughs at
  [GamerGuides](https://www.gamerguides.com/kingdom-come-deliverance-ii/guide/main-quests) (all 32).
* Side quests and tasks, staged or open: [PowerPyx — all side quests and tasks](https://www.powerpyx.com/kingdom-come-deliverance-2-all-side-quests-tasks-guide/).
* Points of no return: [The Nerd Stash](https://thenerdstash.com/all-points-of-no-return-in-kingdom-come-deliverance-2/),
  [TrueTrophies](https://www.truetrophies.com/news/kingdom-come-deliverence-2-points-of-no-return), [GamePressure](https://www.gamepressure.com/kingdom-come-deliverance-2/is-there-a-point-of-no-return/z4119b7).
* The Dry Devil: [TheGamer — Speak of the Devil](https://www.thegamer.com/kingdom-come-deliverance-2-speak-of-the-devil-quest-dry-devil-walkthrough-guide/),
  [The Devil's Pack](https://www.thegamer.com/kingdom-come-deliverance-2-the-devils-pack-quest-walkthrough-guide/),
  [Dancing with the Devil](https://www.thegamer.com/kingdom-come-deliverance-2-dancing-with-the-devil-walkthrough-save-maleshov/).

The guides are third-party and were read once, by a summarising reader, on 2026-10-03; where a guide and the game's data disagreed the data won
(§5, item 1). A guide can be wrong about a quest's length or where a lock begins: the plan's per-quest note says what it was based on.

## 8. What is not covered

* **The engine's reaction is unobserved** (as in WO-155 §4): what the game does with a friend whose quest steps are held, and how the
  checkpoint compare brings them back, is for the first two-player run.
* **Mixed quests are one period each**, not one per scripted stretch (§4). A friend who stays free across a whole mixed quest is held out of
  its free stretches too; they can join at any moment (F11) and leave again.
* **A quest that the guides call open but that has staged moments** (a duel, a short scene) is open: those are the cutscene notice's.
* **DLC**: classified from the guide (staged or not) and the same machine, but the agent does not mirror three DLC roots at all
  (`Wo137Rules.DlcQuests`); a locked section there can never be entered because its States are never sent.
* **"Locked" is still a judgement.** The engine has no flag for "on rails" (searched: no fast-travel, input or open-world switch in the quest
  data); the plan is the maintainers' reading of the game's data and the guides.
