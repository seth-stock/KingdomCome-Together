# Kingdom Come: Together 0.45.0 — when the host is on rails, you choose

**Official repository: https://github.com/DeepFriedDepp/KingdomCome-Together**
— only builds made from it are this project's releases; other repositories may
carry the same name.

Unofficial and free. Not affiliated with or endorsed by Warhorse Studios or
PLAION. Kingdom Come: Deliverance II, its assets and its content belong to
Warhorse Studios and PLAION; this project's copyright covers only its own code.

Everything on `main` up to WO-156 (2026-10-03). **Both machines and the relay must
run the same build**: 0.45.0 refuses every other version at the handshake. The guide
for the host and for the friends is `docs/PLAYING-TOGETHER.md`; the evidence is
`docs/WO-155-findings.md` and `docs/WO-156-quest-gating.md`.

**Verified:** by tests (the agent, the relay, the native plugin, every offline Lua
suite, and a pass over all 201 quests of the game) and a frame-rate soak in the real
game. **Not yet seen with two people.** What needs two is
`docs/TWO-PLAYER-CHECKLIST.md` (items 104 on).

---

## New

- **When the host is on rails, the friend chooses: join, or stay in the open world.**
  A question appears on the friend's screen when the host's story goes on rails:
  **F11 join your host** or **F12 stay in the open world** (30 seconds; no answer counts
  as joining). *Joining* brings the friend beside the host (and, in a quest staged
  throughout, keeps them within 120 m). *Staying* leaves them alone: nothing brings,
  tethers or pulls them back, and the host's story steps are held, so the game cannot
  start the host's scenes on them. They are told when it ends, and **F11 joins at any
  time**. `mp_story_join ask|join|free` makes it a standing answer; `mp_story_come` and
  `mp_story_stay` answer by command.
- **One question for a whole stretch**: the final act is nine quests and asks once; the
  opening is three; the Nebakov campaign is four. A different stretch asks again.
- **F12 on the host's cutscene** also means "do not bring me along after it".
- **Every quest of the game is catalogued** (201: 32 main, 59 side, 52 tasks, 57
  activities, 1 event; the game's own data plus the published guides). The gating works
  on all of them: the prologue (played as Godwin), the opening, the wedding, the Nebakov
  campaign, **both Dry Devil jobs** (Speak of the Devil and Dancing with the Devil), the
  council at Raborsch, the burning of the Jewish quarter, Sigismund's camp, **the final
  act from the Ruthard Palace courtyard to the end of the game** (when the open world is
  locked), and staged side quests and activities: the Kuttenberg Tournament, the fight
  clubs, heists, hunts. `docs/WO-156-quest-gating-table.md` lists each with its tier.
- **Quests with free stretches** (the wedding, most Kuttenberg quests) do not tether:
  only the ordinary leash applies, so a joined friend is not held within 120 m while the
  host roams. Quests staged throughout (sieges, the finale) tether joined friends.
- **The launcher says where each player stands** ("You are staying in the open world
  while your host is in …", and for the host "N joined you, N staying, N deciding").

## Fixed

- **The story order was wrong in 0.44.0**: it ran the main quests by code, which put the
  prologue ("Last Rites", played as Godwin, the first quest of the game) after Storm. A
  new game would have confused every lock after the prologue. Now the game's own order.
- **The Devil's Pack was treated as locked and is an open hub** (recruiting the gang in
  any order). It asks nothing now.
- **A task nested in a fight club's folder** ("A Moment of Fame") was read as the fight
  club. The deepest named quest on a path wins.
- **Oratores** locks only from the Ruthard Palace courtyard (the game's second point of
  no return), not from its first conversation.

## Known limits

- **Staying across a long stretch is untested in the engine**: how the game behaves with
  a friend whose host-quest steps are held, and how well the checkpoint compare brings
  them back, is for the first two-player run. If a friend's story looks wrong after a
  stay, quit to the main menu and rejoin the host's world.
- "On rails" is a judgement from the game's data and the guides: the engine has no flag
  for it. A mixed quest is one question for the whole quest, not one per scripted stretch.
- Keep playing cannot make a scene that a quest step starts later (a timer) skip.
- Windows Defender may remove `KCDMP_LauncherInjector.exe` (it injects a DLL into the
  game, which scanners dislike). See the guide for the one-line fix.
- Nothing here has been played by two people yet.
