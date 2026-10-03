# WO-154 — keep playing, the locked story parts, the story line, and the first run in the real game

Kingdom Come: Together. Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial;
not affiliated with or endorsed by Warhorse Studios or PLAION.

Written 2026-10-03 on `feature/wo-154-story-lock-keep-playing` (on top of WO-153, `docs/WO-153-findings.md`). The
KCD2 Modding Tools (build 1.5.5) are installed on this machine now, so for the first time in this work some things were
run in the real game, solo, on a throwaway copy of an old save of the user's (the real saves were backed up first and
are byte-identical afterwards).

Marks: **(observed)** seen in the running game · **(unit)** / **(synthetic)** tests without the game ·
**(code-verified)** · **(not observed)** built, not seen working, with the reason.

## 0. The answer

| Asked for | Built | Seen working |
|---|---|---|
| "Keep playing" must beat the joiner's own copy of a cutscene | A native gate at `C_CutscenePlayer::EnqueueCutscene` (the one function every scene goes through). A joiner who chose `mp_scene_mode play` has the copy of a scene *the host's mirrored quest step started* skipped; their own scenes are not touched | The gate **arms** in the real engine at `GUIModule+0x1456E0`, takes its commands over the real pipe and reports its counters **(observed)**. That it **refuses a real scene** was **not** seen: no scene could be started in the test save by any lever tried (§3) |
| "Watch" is not a camera | Said plainly: the engine cannot show one player another's camera, so *watch* = F11 stands you beside the host (WO-153). *Own copy* (the game's way) is the other way to see the scene | — |
| Locked story parts: bring the joiner to the host and into that area | 32 main quests in `StorySections.cs`, the user's sections locked: Semine wedding (M05), Trosky (M06, M12), the move to Kuttenberg (M30, M31), the devil's job (M32, M38, M44b — **a guess, see §2**), the pogrom (M42), the cardinal (M45), the Italian Job (M46), the final set (M47–M51), Sigismund's camp (M44a). The host's quest States say where it is; on entry every joiner is brought beside it, and kept within 120 m until it ends | tests (unit, synthetic) only **(not observed)** |
| A region change (the Kuttenberg transition) | told to the joiners; they are brought along once the host's world is up | not observed |
| Dialogue notice | the host's conversations are told to the partner | not observed |
| Launcher "in sync / drifting" label; build-mismatch warning | `GET /coop-status` and two launcher banners | the agent's and launcher's code builds and is tested; the banners were not looked at **(not observed)** |
| Native build, the installer | native build run (389 native tests); the installer built, see §5 | the plugin ran in the game **(observed)** |

## 1. How it works

* **The gate.** `GUIModule` exports `C_CutscenePlayer::EnqueueCutscene(shared_ptr<C_CutsceneConfiguration>&)`. An entry hook
  (the repo's `install_gate`, the way the avatar dialogue gate works) skips it, and the argument is the caller's own
  reference, so skipping leaks nothing. The first 16 bytes of the function are matched exactly before anything is patched, and
  decoded for an instruction boundary: another build = not armed, logged. `wo153_rules.h` decides: refuse only while the quest
  sync's apply is running, or for 1.5 s after it ended (the apply counter in `wo137.cpp`, which already wraps the host's
  `Set<Value>` pulse). Off by default, per joiner; the pipe ops are WO-151's 9 and 10.
* **The sections.** `StoryLock` reads the quest root out of each host State path. A change in a locked quest enters; a *later*
  main quest, the quest's end, or 40 minutes of nothing (no State, no conversation, no scene) leaves; an earlier quest's
  background States and a finished quest's leftovers are ignored; a load or a disconnect forgets it, and after a load the
  section is re-derived from the quest the host's own checkpoint names.
* **The tether** is the leash's own numbers, tightened while a section is open: pull = `mp_story_tether_m` (120), warning 30 m
  inside it (never under the leash's 50 m floor), never looser than the host's own. The leash's countdown, holds, busy
  refusals and 60 s cap all apply; there is no second pull mechanism.
* **Messages** are `StoryBeat` kinds 7–10 (section entered / left, region, conversation): additive, an older build drops them,
  shape-checked, looked up in the receiver's own table, shown in the receiver's own words. The host repeats the section every
  30 s so a late joiner learns it; 90 s of silence ends it on the joiner.

## 2. The sections: what is a guess

> **Updated by WO-155 (`docs/WO-155-findings.md` §2):** the opening (M01, M02), the battle of Nebakov (M09–M11) and the meeting at Rattay (M37a, M37b)
> are locked too, and the locked sections are grouped into *periods* that a friend answers once (join, or stay in the open world).

The mapping from your names to quests comes from the repo's registry (`docs/WO-94-mainquest-registry.csv`, titles are
Warhorse's):

| You said | Quest(s) |
|---|---|
| the wedding in Semine | M05 *Wedding Crashers* |
| Trosky castle and the scenes around it | M06 *For Whom the Bell Tolls*, M12 *Storm* |
| the transition to Kuttenberg | M30 *Last Rites*, M31 *The Sword and the Quill* |
| the cardinal | M45 *Oratores* (the papal legate) |
| the Italian Job | M46 *The Italian Job* |
| the burning of the Jewish quarter | M42 *Exodus* |
| the final mission set | M47 *Civitas Pragensis*, M48a–c the siege, M49 *Reckoning*, M50, M51 *Judgement Day* |
| **the job with the dry devil** | **a guess**: the three "devil" quests M32 *Speak of the Devil*, M38 *The Devil's Pack*, M44b *Dancing with the Devil*. If you mean another quest, say which and it is one line in `StorySections.cs` |

Everything else (the riding and exploring quests) is not locked, so friends can wander in them. M44a (Sigismund's camp) is locked
as well. `mp_story_lock off` turns the whole thing off.

## 3. What was run in the real game, and what it found

(Solo, one machine, the Modding Tools 1.5.5 build, a throwaway playline holding a copy of one old save; `kcd.log`,
`kcdmp-native.log` and the pipe were read.)

* **Steam's *Play* button on the Modding tools entry runs `WorkspaceSetup.exe`, not the game** (Steam's own log). It needs
  administrator rights and exits after ~3 s when it cannot get them. That is why the user's three launches "failed". The
  game is started directly (`KCD2Mod\Bin\Win64ReleaseSteamLTO_DLL\KingdomCome.exe`, working folder the install root — what the
  mod's launcher does). The setup's job — the retail packs into the Modding Tools folder — was done without administrator
  rights with NTFS hard links (94 files, 89 GB, no extra disk), and `tools/Link-GameData.ps1` now does that for anyone.
* **The mod loads in the real engine**: `=== MOD INIT ===`, `kdcmp.lua` runs, the commands answer, **0 Lua errors**, the
  new commands and the 50 m floor answer. The native plugin injects, finds every anchor, and `WO153-GATE armed` — including the
  16-byte prologue — and the gate answers `wo153 scene_gate=armed mode=0 ... seen=0` over the pipe.
* **A save from a newer game than the engine is silently refused.** The user's recent saves are 1.5.6 (`ver: 10506`); the
  Modding Tools are 1.5.5. `wh_sys_LoadGame` on a 1.5.6 save did nothing and logged nothing; a 1.5.2 save loaded at once. This
  is why the build warning exists, and why the host must host from a 1.5.5-or-older save (`docs/PLAYING-TOGETHER.md` §2).
* **Windows Defender removed the freshly built `KCDMP_LauncherInjector.exe`** right after its first use (an unsigned program that
  injects a DLL). The user's friends will meet this too; the guide has the fix. A folder exclusion for the repo was the answer here.
* **No lever tried started a cutscene in the test save**: `wh_ui_PlayCutscene` plays media without the enqueue function;
  `wh_pl_FastTravelTo` needed navmesh points; the `svatba.startCutsceneCeremony` Haste node does not exist by that name. So the
  gate's *refusal* is the one thing not seen. The first two-player run (or any run that reaches a quest scene) settles it: the
  native log line `WO153-GATE enqueue #n ... REFUSED` or `... enqueued`, with the shape of the configuration object logged for the
  first 24 enqueues of a run.
* **The plugin must be injected once the world has loaded.** Injected while the level was still loading, it saw one frame in
  22 s and never started (the README's known "soul walk after injection" gap, met live). The launcher waits for you to say you
  are in the game, which is the same rule.

## 4. Defects found by review and fixed (three rounds on this WO)

Each finding of three independent high-effort code reviews was checked against the code before it was acted on.
Real and fixed (selected; all are in the commit messages): the story lock re-entering a finished or passed section from leftover
States; the tether warning landing at or above the pull for a tight host leash; a gate left **on** after a mode switch or a
reconnect; `mp_story_lock off` mid-section leaving the tether and the launcher line; the first region change after connecting or
after a load being swallowed; a lost "left the section" beat leaving a joiner's line forever; a load wiping the tether until the
next quest step; the gate's off command never sent at disconnect; a throttle that would have dropped a second bring-along; the
gate's 3 s window catching the joiner's own sleep (now 1.5 s); a stale "default OFF" for `mp_scene_follow` in the help text and
docs; the game-build reader racing the connect; and several smaller ones.
Declined with reasons: none that were real were left open.

## 5. Verification

| What | Result |
|---|---|
| `dotnet build KCD2-MP.sln` | 0 errors |
| `KcdMp.Client.Tests` / `KcdMp.Relay.Tests` / `KcdMp.Farkle.Tests` | 1007 / 62 / 59, 0 failed |
| Native tests | 389 / 389 (21 new: the gate's rules and the prologue's instruction boundary) |
| `Test-WO153Synthetic.ps1` | 123 / 123 |
| All 43 offline Lua suites | 0 failed |
| The three static checks | exit 0 |
| The plugin and the gate in the real game | armed, answers, no Lua errors **(observed)** |
| The frame-rate soak (the installer's gate) | PASS, with the mod 55.7 → 58.7 FPS, without 55.3 → 59.7, stat stack 0, no FAULT, 50 rows each (`tools/perf/soak-record.json`) **(observed)** |
| A real scene refused by the gate; the story sections; the launcher banners; two people | **not observed** |

## 6. What a live run with two people must show (the checklist items 110–116)

1. A locked section's entry brings the friend beside the host and says so; leaving it says so. Check the agent's
   `MP-W153 story:` lines and the host's `WO153-STORY`.
2. In `mp_scene_mode play`, a host cutscene is *not* played on the friend's screen, and the native log says `REFUSED`; the friend's
   quest still moves because the host's steps reach them. If it ever stalls, `mp_scene_mode watch`.
3. The wedding, Trosky, the move to Kuttenberg: the friend ends where the host is, each time. Write down any time they do not.
4. The launcher line follows reality; the build warning shows on a game that is not 1.5.5.
