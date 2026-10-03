# Kingdom Come: Together 0.44.0 — cutscenes, locked story parts, and the story line

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

Everything on `main` up to WO-154 (2026-10-03). **Both machines and the relay must
run the same build**: 0.44.0 refuses every other version at the handshake. The guide
for the host and for the friends is `docs/PLAYING-TOGETHER.md`; the evidence is
`docs/WO-153-findings.md` and `docs/WO-154-findings.md`.

**Verified:** by tests (the agent, the relay, the native plugin, every offline Lua
suite) and, for the plugin, in the real game: it loads, the new cutscene gate arms
and answers over the pipe, the new console commands answer. **Not yet seen with two
people.** What needs two is `docs/TWO-PLAYER-CHECKLIST.md` (items 104 on).

---

## New

- **The partner is told when the host is in a cutscene**, and chooses: **F11** stands
  them beside the host to watch from their own camera, **F12** (or nothing for 20
  seconds) keeps them playing. (The game cannot show one player another's camera;
  "watch" is standing there.)
- **Keep playing** (`mp_scene_mode play`, the partner's choice; the default `watch` is
  the game's way: your own copy of the host's cutscene plays too). With `play`, your
  own copy of a scene the host's quest step started is not played. Needs the plugin.
- **The partner is brought along** when a cutscene moves the host, or a story scene
  ends with the partner far away (`mp_scene_follow`, on). Nobody within 50 m is moved.
- **Locked story parts.** In the quests where the story stages one place and holds
  you there (the wedding in Semine, Trosky castle, the move to Kuttenberg, the devil's
  job, the burning of the Jewish quarter, the cardinal, the Italian Job, the final set)
  the partner is brought to the host when it begins and kept within 120 m until it
  ends (`mp_story_lock`, on; `mp_story_tether_m`).
- **A region change** (a story move to another map) is told, and the partner is brought
  along once the host's world is up. **The host's conversations** are told too.
- **The launcher shows "Story: IN SYNC / DRIFTING / CATCHING UP"**, the locked part the
  host is in, and a **warning when the game is not build 1.5.5**, the one the mod was
  verified on. (A save from a newer game than the engine silently refuses to load.)

## Known limits

- Keep playing cannot make a scene that a quest step starts later (a timer) skip.
- "Locked" is a judgement from the quest data; the game gives no read of its own locks.
- Windows Defender may remove `KCDMP_LauncherInjector.exe` (it injects a DLL into the
  game, which scanners dislike). See the guide for the one-line fix.
- Nothing here has been played by two people yet.
