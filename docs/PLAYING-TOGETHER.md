# Playing together — the guide for the host and for the friends

Kingdom Come: Together 0.45.0. Unofficial; not affiliated with or endorsed by Warhorse Studios or PLAION.

**Everyone — the host and every friend — needs the same build (0.45.0), the same game build (see §2), and the same two
Steam items.** The mod refuses to connect two different versions.

---

## The host, in short

1. Install **Kingdom Come: Deliverance II** and the free **Kingdom Come: Deliverance II Modding tools** in Steam, and do the one-time
   data setup (§3). Run `KingdomComeTogether-Setup-0.45.0.exe` (§6 if Windows complains).
2. Open **Kingdom Come: Together** (desktop shortcut). Click **HOST GAME**. It shows the addresses your friends can use (§4).
3. Send each friend the **installer** and **one address with the port** (for example `100.64.12.3:7778`).
4. Click through to start the game **from the launcher** (never Steam's Play button), load a save made on build 1.5.5 or older, or start a
   new game (§2). When you are standing in the world, click **CONNECT** in the launcher.
5. Tell your friends when you are in the world. They join (below). **Only you save.**
   (You are the only one who loads a save. Friends must *not*: they wait at the main menu.)

## Send this to your friends

> 1. In Steam, install **Kingdom Come: Deliverance II** and the free **Kingdom Come: Deliverance II Modding tools**
>    (a separate item in your library).
> 2. Run the modding tools' one-time data setup: in Explorer open
>    `<your Steam library>\steamapps\common\KCD2Mod\Tools\ModdingWorkspaceSetup\`, right-click **WorkspaceSetup.exe** →
>    **Run as administrator**, answer `S`. (More in §3; the host can also send you a script that does it without administrator rights.)
>    Skip it and the game crashes with "114 tables are not loaded".
> 3. Download `KingdomComeTogether-Setup-0.45.0.exe` from the host (not from anywhere else), run it. Windows will warn
>    that it is unsigned: **More info → Run anyway**. If Defender removes a file, do §6.
> 4. Open **Kingdom Come: Together** (the desktop shortcut). The host gives you an **address and port**
>    (for example `100.64.12.3:7778`): click **ADD SERVER**, enter it, then **JOIN SERVER**.
>    (If you are not on the host's network, you both need Tailscale first: §4.)
> 5. **You need one save of your own, made on the Modding Tools build (1.5.5) or older**, on your PC before you join:
>    either a character you already have (the game then offers **Bring my character**) or the first save of a new game, which the
>    game writes right after the short prologue (then **Start fresh** works). If you have none, or only saves from the newer retail game,
>    start a new game once in the Modding Tools build, play through the prologue until it saves, and quit to the main menu.
> 6. Use the launcher to start the game (**never Steam's Play button**: that runs the setup tool). **Stay at the main menu. Do not load a
>    save.** Click **CONNECT** in the launcher; the first time it asks **Bring my character** or **Start fresh**, and the game then loads
>    the host's world with your Henry (a loading screen, up to a minute). If you loaded a save by mistake, quit the game and start it again.
> 7. When the host's story goes "on rails" you are asked: **F11** join them, **F12** stay in the open world.

---

## 1. What you get

You play the host's world together: you see each other, fight together, talk to the host's people, and share the quests,
the clock and the weather. New in 0.44.0 and 0.45.0:

* When the **host is in a cutscene** you are told, and you choose: **F11** to stand beside them and watch from your own
  camera, **F12** (or do nothing for 20 seconds) to keep playing. By default the game also plays *its own copy* of their
  cutscene on your screen; `mp_scene_mode play` (§5) skips that so you keep playing.
* Whenever the **host goes on rails** — the story's scripted stretches (the prologue, the opening, the wedding in Semine, the Nebakov
  campaign, the Dry Devil's two jobs, the council at Raborsch, the burning of the Jewish quarter, Sigismund's camp, and **the final act**, from the
  Ruthard Palace courtyard to the end of the game, when the open world is locked), and also a tournament, a fight club bout, a heist or another
  staged side quest or activity — **you choose**. Every quest of the game is catalogued (`docs/WO-156-quest-gating.md`). A question appears
  on your screen: **F11 join your host** or **F12 stay in the open world**
  (30 seconds; no answer counts as joining). Joining brings you beside the host and keeps you within 120 m of them until the part
  ends. Staying leaves you alone for as long as that part lasts: nothing brings you, tethers you or pulls you back, and the host's
  story steps are held so the game cannot start their scenes on you. You are told when it ends, and **F11 joins at any time**.
  One question covers a whole stretch (the final act is nine quests; you are asked once). In quests that alternate scripted and free parts
  you are not tethered (only the ordinary leash applies); in quests staged throughout, joining also keeps you within 120 m.
  `mp_story_join` (§5) makes it a standing answer.
* When a cutscene **moves the host** somewhere else, or a story region changes, you are **brought along**. Nobody within
  50 m of the host is ever moved.
* The launcher shows **Story: IN SYNC / DRIFTING / CATCHING UP**, the locked part the host is in, and a warning if your
  game is not the build the mod was verified on.

What is not covered: side quests, DLC and the stretches where the host plays as someone else (nobody can be placed beside a
different character, so nobody is moved); "locked" is a judgement from the quest data, not something the game tells us
(`docs/WO-156-quest-gating-table.md` lists every quest and why). **Nothing new in 0.44.0 or 0.45.0 has been played by two people yet**; please send the logs
(§7) if anything looks wrong.

## 2. The game build, and your saves — read this

The mod runs on the **Modding Tools** build of the game, which on 2026-10-02 is **1.5.5**. The retail game is already
**1.5.6**. Two consequences:

* **A save made by a newer game than the Modding Tools is silently refused**: the load command does nothing. Saves from
  1.5.6 (anything from mid-June 2026 on, if you played then) cannot be loaded; older saves load fine. Check a save's build
  in the launcher's folder: `%USERPROFILE%\Saved Games\kingdomcome2\saves\<playline>\` and the game's `kcd.log` line
  `ver: 10505` (= 1.5.5) or `ver: 10506` (= 1.5.6).
* The host should therefore **host from a 1.5.5-or-older save, or start a new game in the Modding Tools build**. The friends
  do not need the host's save, but each needs **a save of their own on the Modding Tools build or older**: they join the host's world with
  their own Henry (**Bring my character** copies their Henry from such a save; **Start fresh** uses the first save of a new game, written
  right after the prologue). They never load it: they wait at the main menu and the world comes to them.

The launcher warns when your game is not 1.5.5. When Warhorse ships a newer Modding Tools, update it in Steam on every
machine, and the saves of that build become usable.

## 3. One-time setup of the Modding Tools (everyone)

Steam's **Play** button on the Modding tools entry runs `Tools\ModdingWorkspaceSetup\WorkspaceSetup.exe`. It asks for
administrator rights, then copies — or symlinks — the retail game's data packs into the Modding Tools folder. If it flashes
and closes (it does when it cannot elevate), do either:

* **Run it as administrator**: right-click `WorkspaceSetup.exe` in
  `<steam library>\steamapps\common\KCD2Mod\Tools\ModdingWorkspaceSetup\` → *Run as administrator* → answer `S` (symlink,
  fast, no extra disk) or `C` (copy, 89 GB).
* **Or with no administrator rights** (both games on the same drive): the script `tools\Link-GameData.ps1` in the repo (it is **not**
  inside the installer: the host sends the one file). In PowerShell:
  `powershell -ExecutionPolicy Bypass -File Link-GameData.ps1` (`-WhatIf` first to see what it would do). It makes the
  same result with NTFS hard links: nothing is copied, no extra disk is used.

You have it right when `<KCD2Mod>\Data` contains `Tables.pak`, `Scripts.pak`, `Characters.pak` and the rest.

## 4. The host: start a game others can join

1. Install the mod (the Setup exe) and open the launcher.
2. **HOST GAME.** The launcher starts the relay (it listens on **TCP port 7778**, on every network interface of your PC) and
   shows every address your PC can be reached at.
3. Give your friends **one address and the port**:
   * **Same house or Wi-Fi:** your LAN address (`192.168.x.x:7778`).
   * **Different houses (recommended):** install **Tailscale** (free) on both PCs, sign in with the same account or share
     the machine, and give your **Tailscale address** (`100.x.x.x:7778`). No router settings, nothing exposed to the internet.
   * **No VPN:** forward TCP 7778 on your router to your PC and give your **public IP** (`docs/NETWORKING.md`).
   * `localhost` / `127.0.0.1` only ever means *this* PC; it is what your own game uses to reach your own relay and is
     never what a friend types.
4. Windows Firewall asks the first time the relay starts: **allow it on Private networks**.
5. Start the game from the launcher, load a **1.5.5-or-older** save (§2), then press **CONNECT**.

The host is the narrative authority: the world, the quests, the clock and the weather are the host's. **Only the host
saves.** If the host reloads, the friends are brought back in on their own.

## 5. The settings (the game's console, the `~` key)

You normally type nothing. Everything below is optional.

| Command | Who | What it does |
|---|---|---|
| `mp_story_join ask` / `join` / `free` | friend | what to do when the host's story goes on rails: `ask` (default) = the question; `join` = always go with the host; `free` = always stay in the open world |
| `mp_story_come` / `mp_story_stay` | friend | answer now, with or without the question (same as F11 / F12) |
| `mp_scene_mode play` / `watch` | friend | `watch` (default): your own copy of the host's cutscene plays too. `play`: it is skipped and you keep playing |
| `mp_scene_notice on` / `off` | friend | the "your host is in a cutscene" message and its keys (default on) |
| `mp_story_lock on` / `off` | host | the locked story parts ask each friend to join (those who join are brought and held close; those who stay are left alone) (default on) |
| `mp_story_tether_m 120` | host | how far a friend may stray in a locked part, 60–5000 m |
| `mp_scene_follow on` / `off` | host | bring the friends along after a cutscene moved the host (default on) |
| `mp_scene_follow_m 25`, `mp_scene_follow_far_m 150` | host | how far counts as "moved" and as "far" |
| `mp_leash on` / `off` | host | the ordinary leash that keeps friends within 650 m |
| `mp_scene_status`, `mp_story_lock` (bare) | anyone | report the current settings |
| `mp_unstuck` | anyone | if you are stuck in a bed, a minigame or anything else |
| `mp_henry_home` / `mp_henry_home playlineN/file` | friend | **send your character home**: your Henry, as he is now, goes back into the world he came from as a **new manual save** (your old save is never touched). He comes back with what he gained; his quest items from the host's world stay behind |
| `mp_henry_home_on_leave on` / `off` | friend | do that automatically whenever you leave your host's world (default off) |
| `mp_world_copy` / `mp_world_copy N` | friend | save a **copy of the shared world, with your character in it, as your own save** (playline N, default the first empty one), to carry on from alone, later, whatever the host does |
| `mp_quest_dlc on` / `off` | anyone | share DLC quests like every other quest (default on); `off` keeps DLC out of the session |

These four are new in WO-157 and **not yet seen in the real game**: `docs/WO-157-findings.md` says what was tested and what was not. The prologue and the other
scenes played as **Godwin** can now be joined too (your own Godwin save is the character you bring); that is untested in the game as well.

`mp_scene_mode play` needs the native plugin (the launcher injects it) and is **off by default** because it is the least
tested part: a skipped cutscene means that scene's own steps never run on your game, and you rely on the host's steps
reaching you. If a quest ever stops moving for you, switch it back (`mp_scene_mode watch`).

## 6. Windows Defender and SmartScreen

* **SmartScreen** says the Setup exe is unsigned: *More info → Run anyway*. That is expected.
* **Defender** may remove `KCDMP_LauncherInjector.exe` — the small tool that loads the mod's plugin into the game — because
  it injects a DLL into another program, which is what malware does too. It is this project's own code and its source is in
  `native/KCDMP_LauncherInjector/`. If it vanishes: *Windows Security → Virus & threat protection → Protection history →
  Restore*, then *Manage settings → Exclusions → Add → Folder* for the install folder
  (`%LocalAppData%\KCDMP`). Only do this for a build you got from the host or built yourself.

## 7. If something goes wrong

* Not connecting: the host's launcher says HOST GAME is active? Same port? Same version (0.45.0)? Both on Tailscale and shown
  as online? Firewall allowed? See `docs/NETWORKING.md`.
* "Database system error — 114 tables are not loaded": §3 was not done.
* The game starts and nothing of the mod is there: you started it from Steam instead of the launcher, or the mod is not in
  `<KCD2Mod>\Mods\kdcmp\`.
* A load that does nothing: the save is from a newer game than the Modding Tools (§2).
* A black screen after a scene: wait up to 90 seconds; the mod helps it along. Tell the host.
* Send the logs: the launcher's **REPORT BUG** button collects them. Please remove your public IP from anything you post.
