# Playing together — the guide for the host and for the friends

Kingdom Come: Together 0.44.0. Unofficial; not affiliated with or endorsed by Warhorse Studios or PLAION.

**Everyone — the host and every friend — needs the same build (0.44.0), the same game build (see §2), and the same two
Steam items.** The mod refuses to connect two different versions.

---

## Send this to your friends

> 1. In Steam, install **Kingdom Come: Deliverance II** and the free **Kingdom Come: Deliverance II Modding tools**
>    (a separate item in your library).
> 2. Run the modding tools' one-time setup (§3 below). Skip it and the game crashes with "114 tables are not loaded".
> 3. Download `KingdomComeTogether-Setup-0.44.0.exe` from the host (not from anywhere else), run it. Windows will warn
>    that it is unsigned: **More info → Run anyway**. If Defender removes a file, do §6.
> 4. Open **Kingdom Come: Together** (the desktop shortcut). The host gives you an **address and port**
>    (for example `100.64.12.3:7778`): **ADD SERVER**, paste it, **JOIN SERVER**.
> 5. When the launcher asks, load into the game, then press **CONNECT** in the launcher. The first time it asks
>    **Bring my character** or **Start fresh**.
> 6. Use the launcher to start the game. **Never start it from Steam's Play button** (that runs the setup tool).

---

## 1. What you get

You play the host's world together: you see each other, fight together, talk to the host's people, and share the quests,
the clock and the weather. New in 0.44.0:

* When the **host is in a cutscene** you are told, and you choose: **F11** to stand beside them and watch from your own
  camera, **F12** (or do nothing for 20 seconds) to keep playing. By default the game also plays *its own copy* of their
  cutscene on your screen; `mp_scene_mode play` (§5) skips that so you keep playing.
* In the story parts that **lock the host into one place** — the wedding in Semine, Trosky castle, the move to Kuttenberg,
  the devil's job, the burning of the Jewish quarter, the cardinal, the Italian Job, the final set — you are **brought to the
  host when it starts and kept within 120 m** of them until it ends. A message tells you so.
* When a cutscene **moves the host** somewhere else, or a story region changes, you are **brought along**. Nobody within
  50 m of the host is ever moved.
* The launcher shows **Story: IN SYNC / DRIFTING / CATCHING UP**, the locked part the host is in, and a warning if your
  game is not the build the mod was verified on.

What is not covered: side quests, DLC and the non-Henry stretches of the story; "locked" is a judgement from the quest
data, not something the game tells us. **Nothing in 0.44.0 has been played by two people yet**; please send the logs
(§7) if anything looks wrong.

## 2. The game build, and your saves — read this

The mod runs on the **Modding Tools** build of the game, which on 2026-10-02 is **1.5.5**. The retail game is already
**1.5.6**. Two consequences:

* **A save made by a newer game than the Modding Tools is silently refused**: the load command does nothing. Saves from
  1.5.6 (anything from mid-June 2026 on, if you played then) cannot be loaded; older saves load fine. Check a save's build
  in the launcher's folder: `%USERPROFILE%\Saved Games\kingdomcome2\saves\<playline>\` and the game's `kcd.log` line
  `ver: 10505` (= 1.5.5) or `ver: 10506` (= 1.5.6).
* The host should therefore **host from a 1.5.5-or-older save, or start a new game in the Modding Tools build**. The friends
  do not need a save: they join the host's world with their own Henry (**Bring my character** copies their own Henry from a
  save of the same build; **Start fresh** gives a new one).

The launcher warns when your game is not 1.5.5. When Warhorse ships a newer Modding Tools, update it in Steam on every
machine, and the saves of that build become usable.

## 3. One-time setup of the Modding Tools (everyone)

Steam's **Play** button on the Modding tools entry runs `Tools\ModdingWorkspaceSetup\WorkspaceSetup.exe`. It asks for
administrator rights, then copies — or symlinks — the retail game's data packs into the Modding Tools folder. If it flashes
and closes (it does when it cannot elevate), do either:

* **Run it as administrator**: right-click `WorkspaceSetup.exe` in
  `<steam library>\steamapps\common\KCD2Mod\Tools\ModdingWorkspaceSetup\` → *Run as administrator* → answer `S` (symlink,
  fast, no extra disk) or `C` (copy, 89 GB).
* **Or with no administrator rights** (both games on the same drive): from the repo, in PowerShell,
  `powershell -ExecutionPolicy Bypass -File tools\Link-GameData.ps1` (`-WhatIf` first to see what it would do). It makes the
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
| `mp_scene_mode play` / `watch` | friend | `watch` (default): your own copy of the host's cutscene plays too. `play`: it is skipped and you keep playing |
| `mp_scene_notice on` / `off` | friend | the "your host is in a cutscene" message and its keys (default on) |
| `mp_story_lock on` / `off` | host | the locked story parts bring the friends and hold them close (default on) |
| `mp_story_tether_m 120` | host | how far a friend may stray in a locked part, 60–5000 m |
| `mp_scene_follow on` / `off` | host | bring the friends along after a cutscene moved the host (default on) |
| `mp_scene_follow_m 25`, `mp_scene_follow_far_m 150` | host | how far counts as "moved" and as "far" |
| `mp_leash on` / `off` | host | the ordinary leash that keeps friends within 650 m |
| `mp_scene_status`, `mp_story_lock` (bare) | anyone | report the current settings |
| `mp_unstuck` | anyone | if you are stuck in a bed, a minigame or anything else |

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

* Not connecting: the host's launcher says HOST GAME is active? Same port? Same version (0.44.0)? Both on Tailscale and shown
  as online? Firewall allowed? See `docs/NETWORKING.md`.
* "Database system error — 114 tables are not loaded": §3 was not done.
* The game starts and nothing of the mod is there: you started it from Steam instead of the launcher, or the mod is not in
  `<KCD2Mod>\Mods\kdcmp\`.
* A load that does nothing: the save is from a newer game than the Modding Tools (§2).
* A black screen after a scene: wait up to 90 seconds; the mod helps it along. Tell the host.
* Send the logs: the launcher's **REPORT BUG** button collects them. Please remove your public IP from anything you post.
