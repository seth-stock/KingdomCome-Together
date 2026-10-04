# tools/perf — the frame-rate soak and the stat-stack reader (WO-151)

Why: 0.42.5–0.42.7 lost the frame rate in every fight and crowd (75 → 4.5 FPS within
minutes) and nothing measured it; the cause was a reflection argument that outlived its
value, faulting inside the game's stat getter thousands of times behind a silent guard
(docs/WO-148-findings.md §7). These tools make a decline visible before an installer.

Everything here is read-only on the game except the soak's scene (spawned, never saved,
removed at the end), and runs only on a **throwaway save** (the soak reads which save is
loaded from `kcd.log` first and refuses any other playline).

## The soak (required before every installer)

```
python tools/perf/soak.py run --label mod1 --throwaway playline4            # the game WITH the mod (pak + DLL)
python tools/perf/soak.py run --label van1 --throwaway playline4 --vanilla  # the same save, the game WITHOUT the mod
python tools/perf/soak.py verdict --mod tools/perf/runs/mod1.json --vanilla tools/perf/runs/van1.json --version <x.y.z>
```

* 10 minutes: three commoners 3 m around the player (no AI, never saved: two or more
  souls within 15 m, the 0.42.5 leak's trigger), from minute 3 a fight beside them (two AI
  commoners, the game's own attack interrupt re-sent every 15 s). No input, no focus.
* Every 10 s: the frame rate over those 10 s, the depth of the game's stat stack on its
  main thread, the `FAULT` lines in `kcdmp-native.mirror.log` since the start.
* PASS: the mod's last 2 minutes within 10 % of its first 2, both within 10 % of the same
  windows without the mod, the stack 0 in every row, no `FAULT` line, the code committed.
* `verdict` writes `tools/perf/soak-record.json` (commit it). It carries the git trees of
  the code the soak ran (`native/KCDMP`, `kdcmp/Data/Scripts`, `kdcmp/Data/Libs`,
  `kdcmp/mod.manifest`, the agent, the relay, the protocol); `tools/Build-Installer.ps1`
  runs `soak.py check` first and stops when the record is missing, says FAIL, or names
  other trees than the ones being built.
* The game without the mod: close the game, move `Mods\kdcmp` out of the Modding Tools
  folder (and do not inject the DLL), run, put it back.
* **The window in the same state for both runs** (the 0.43.0 soak: focused all the way).
  A window that loses focus mid-run caps the game's frame rate: an early 0.43.0 run fell
  from 68 to 25 FPS after the game was alt-tabbed out of, and stayed there after the scene
  was removed, while the DLL's own cost (`FRAME ... ours_us_mean`) stayed at 0.7 ms; the
  focused rerun held 71 FPS to the end.
* A dropped console connection is retried (the first 0.43.0 attempt died at 72 s on one).
* **Warm the game up for 3 minutes in the world before the soak, the same for both runs** (0.45.0). The game's own frame rate rises about
  10 % over its first five minutes in this scene, with or without the mod (0.45.0 trial runs: mod 57.1 -> 62.6 and 57.0 -> 63.3, vanilla
  56.7 -> 62.4 and 59.5 -> 63.4): started cold, the "last 2 minutes within 10 % of the first 2" rule sits on its own boundary and fails on noise
  (an 11 % RISE failed it). Warmed up, the pair passed: mod 59.9 -> 65.0, vanilla 59.5 -> 63.4.
* **Steam must be running before the game starts**, or the game stops at a "License not verified / No SteamApps" dialog.
* **A single stat-stack reading above 0 is not by itself a leak.** The reader samples the main thread from outside, so it can catch the
  game mid-call: one 0.45.0 mod run read 1 and 3 in two rows (back to 0 at once, 0 at the end), and a run of the game WITHOUT the mod read 3
  once. A leak grows and stays; the rule still requires 0 in every row, and a flagged run is repeated, not argued with.

## The stat-stack reader

```
python tools/perf/statstack.py            # the running game, its main thread: depth=N
```

The stack must be 0 between frames. A depth that grows is a leak (WO-148 §7.3). The
offsets are the Modding Tools 1.5.5 RPGModule's; the reader checks that module's PE
identity and refuses any other build.
