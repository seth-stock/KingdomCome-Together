# Kingdom Come: Together on Linux (Steam + Proton)

**Status: experimental, new, and not yet run under a real Proton by the people who wrote it.** Read
[What is and is not tested](#what-is-and-is-not-tested) before you spend an evening on it.

## Can it work on Linux?

Yes, by design, with one dependency nobody here could check: the mod needs the **"Kingdom Come: Deliverance II Modding
tools"** build of the game (the retail game has no console/REST API), and that build has to run under Proton.

The mod has four parts. What each one needs on Linux:

| Part | On Linux |
|---|---|
| **Lua mod** (`kdcmp.pak`) | Unchanged. It is game data, loaded by the game. |
| **Relay** (`KcdMpServer`) | Native Linux program (.NET 8, self-contained). Same protocol as on Windows. |
| **Agent** (`KcdMpClient`) | Native Linux program. Talks to the game over `127.0.0.1`. |
| **Plugin** (`KCDMP.dll`) + **injector** | Windows programs. They run **inside the game's Proton prefix**, like the game itself. |

The launcher window (Photino/Blazor/WPF) and the Inno Setup installer are Windows-only and are replaced by the
`kcdmp` shell script.

### How the agent reaches the game from Linux

* **The game's REST API** (`localhost:1403`): a Wine socket is a real host socket, so the native agent just connects.
* **The plugin**: on Windows it serves a named pipe, `\\.\pipe\kcdmp`. A named pipe lives inside Wine's own server and a
  native Linux process cannot open it. So the plugin detects Wine (`wine_get_version` in `ntdll`) and serves the **same
  frames over TCP on `127.0.0.1:14070`** instead (loopback only: nothing off your machine can reach it). The agent
  connects there when it is not running on Windows. The rules are one small, unit-tested header on each side
  (`native/KCDMP/transport.h`, `dotnet/KcdMp.Client/PluginTransport.cs`).
  Override with `KCDMP_TRANSPORT=tcp|pipe` and `KCDMP_TCP_PORT=<1024-65535>` (set it for both the game and the agent).
* **The injector** needs the game's Windows process, which a Linux script cannot name by pid. It was extended to find the
  game by name and wait for the engine: `--process KingdomCome.exe --module WHGame.dll --wait <s>` (the old `--pid`
  form, which the Windows launcher uses, is unchanged). `kcdmp inject` runs it through `proton run` in the game's prefix.
* **Saves**: the game's own `kcd.log` says `User folder is 'C:\users\steamuser\Saved Games\KingdomCome2'`. The agent maps
  that Windows path into the Proton prefix (`<library>/steamapps/compatdata/2429020/pfx/drive_c/...`) and matches each
  folder name case-insensitively, since Linux file names are case-sensitive and Windows' are not.

### Playing with Windows players

The wire protocol did not change. A Linux player and a Windows player can be in the same session, as long as both run
the same mod version.

## Requirements

* Steam for Linux, with **Kingdom Come: Deliverance II** (retail) *and* **Kingdom Come: Deliverance II Modding tools**
  (Steam → Library → Tools) installed. Run the Modding tools entry once from Steam so Proton creates its prefix.
* A Proton that runs the game. (Which Proton works for the *Modding tools* build is exactly the thing that is not verified.)
* `curl`. Nothing else: the package carries its own .NET.

## Install

Download `KingdomComeTogether-Linux-<version>.tar.gz`, then:

```bash
tar xzf KingdomComeTogether-Linux-*.tar.gz && cd KingdomComeTogether-Linux-*
./kcdmp doctor        # what is missing, in plain words
./kcdmp link-data     # once, and after retail game updates: mirror retail Data/ into the Modding tools
./kcdmp install       # with the game closed: copies the mod into KCD2Mod/Mods/kdcmp
```

`link-data` does on Linux what Steam's `WorkspaceSetup.exe` does on Windows: every file of the retail game's `Data/` and
`Localization/` appears under the Modding tools folder, as a hard link when both are on one filesystem (no extra disk
space), else a symlink (`./kcdmp link-data copy` copies instead, ~90 GB).

## Play

Start **Steam first** (the game refuses to run without it), then:

```bash
./kcdmp play          # starts the Modding tools' KingdomCome.exe under Proton and waits for its API
# load a save in the game, wait until you are standing in the world, then:
./kcdmp inject        # injects the plugin inside the same Proton prefix
./kcdmp host Henry    # you host: starts the relay here and connects your agent to it
./kcdmp join 203.0.113.9:7778 Hans   # or: you join a friend's relay
./kcdmp stop          # stops the relay
```

Hosting: friends need TCP port 7778 (override with `KCDMP_RELAY_PORT`) reachable, exactly as on Windows
([NETWORKING.md](NETWORKING.md)). From there, [PLAYING-TOGETHER.md](PLAYING-TOGETHER.md) applies; the in-game parts
(F-keys, `mp_*` console commands) are the same.

If you start the game some other way (Steam's own Play button, a launch option), skip `kcdmp play`; `kcdmp inject`
works on any running Modding-tools game.

Overrides: `STEAM_ROOT`, `KCD2MOD_DIR`, `KCD2_DIR`, `PROTON` (path to the `proton` script),
`STEAM_COMPAT_DATA_PATH`, `KCDMP_TCP_PORT`, `KCDMP_RELAY_PORT`.

## Off on Linux, on purpose

| Feature | Why |
|---|---|
| Steam P2P connection | It binds the game's `steam_api64.dll`, a Windows library a native process cannot load. The agent says so and you connect to a relay by address. |
| Voice chat | NAudio's microphone capture is WinMM, Windows-only. The agent turns it off with one log line. |
| Discord presence | Off by default in `kcdmp` (`--no-discord`): it probes IPC sockets slowly and uses nothing the game needs. |
| The launcher window, the installer | Windows-only; replaced by `kcdmp`. |

## What is and is not tested

Tested, on this repository's authors' machines (a Windows 11 PC and an Ubuntu 24.04 under WSL2):

| What | How | Result |
|---|---|---|
| The whole .NET suite on real Linux | Ubuntu 24.04, .NET 8: client 1225, relay 62, Farkle 59 | all pass (4 tests that assumed Windows were fixed; see below) |
| Agent ↔ plugin over **TCP** | unit tests against a fake plugin on a real socket (ping, reconnect after the game dies, nothing listening) | pass, on Windows and Linux |
| The **plugin's TCP listener inside the real game** | the real Modding-tools game on Windows, started with `KCDMP_TRANSPORT=tcp`, plugin injected by the new `--process/--module/--wait`: a raw client got Pong, then dropped, reconnected and got Pong again | works. This is Winsock in a real game process; it is **not** Wine |
| `kcdmp` + the package, on Linux | a fake Steam tree and fake Proton: `doctor`, `link-data`, `install`, `play`, `inject` (the exact command line is checked), `host`; the real linux-x64 relay and agent, the agent logs `connected to KCDMP.dll over tcp 127.0.0.1:14070` | works, against stand-ins |
| Native plugin unit tests | 403 pass, 0 fail (14 new: the transport rules) | pass |

**Not tested, because nobody involved had it:**

* **Anything under real Proton/Wine.** The Modding-tools game starting under Proton; the plugin loading in it; Wine
  reporting itself as expected (`wine_get_version`); the plugin's TCP socket being reachable from a native process (this
  is how Wine sockets are documented to work, not something seen here); `proton run` for the injector joining the game's
  wineserver; the engine hooks working under Wine (they patch machine code in the game: if Wine's `KingdomCome.exe`
  differs in layout, the plugin's own checks refuse to hook, and the mod degrades, as on a game update).
* Two players, in any combination (the same gap as the Windows 0.45.0 build).
* Steam Deck, Flatpak Steam, non-default library folders other than via `libraryfolders.vdf`.
* An observation, not yet explained: in two runs of the TCP transport on Windows the plugin's quest hook
  (`wo137`) logged a burst of guarded-read faults (`FAULT wo137::set_send_callback ... counted, never switched off`,
  which that code is built to survive) while two runs on the named pipe did not. Four runs are not enough to call it a
  difference or to find a cause, and the machine was shared with another game during the runs; no crash and no Lua
  error followed. Worth a look from anyone who has a Proton setup.

The four tests fixed for Linux were test assumptions, not product bugs: backslash path literals, a zlib
size that differs between platforms, and two that called the Windows registry (the product code now has a Linux Steam finder).

## Troubleshooting

* `./kcdmp doctor` first. It names the missing piece and the command that fixes it.
* **Game API never comes up**: look at `~/.local/state/kcdmp/game.log` (Proton's output). Confirm the *Modding tools* build
  starts under Proton at all by running it once from Steam's own Play button.
* **`inject` says the plugin is not listening on 14070**: the plugin writes `kcdmp-native.log` next to `KCDMP.dll`
  (in `plugin/`). Its `PIPE:` lines say which transport it chose and why; `transport tcp 127.0.0.1:14070 (running under Wine/Proton...)` is the good one. If it says `Windows: the named pipe`, Wine was not detected: run the game with `KCDMP_TRANSPORT=tcp`.
* **Wrong Proton**: `PROTON=/path/to/proton ./kcdmp inject`. It must be the Proton the game runs under (same prefix, same wineserver).
* **Antivirus-style removal of the injector**: not a Linux concern; on Linux nothing scans it.

## For developers

* Build the package on Linux/WSL: `linux/Build-LinuxPackage.sh` (needs the .NET 8 SDK; takes the plugin and injector
  from `native/build`, which only Windows can build: `native\Build-Native.ps1`). Output: `release/KingdomComeTogether-Linux-<version>.tar.gz` + `.sha256`.
* New code: `native/KCDMP/transport.h` + `pipe_server.cpp` (TCP listener; the serve loop is shared with the pipe),
  `native/KCDMP_LauncherInjector/main.cpp` (`--process/--module/--wait`), `dotnet/KcdMp.Client/PluginTransport.cs`,
  `CombatPipe.cs` (TCP client), `GameHost.cs` (Steam/Proton/saves discovery), `linux/kcdmp`.
* Tests: `native/tests/transport_rules_tests.cpp`, `dotnet/KcdMp.Client.Tests/LinuxTransportTests.cs`, `GameHostTests.cs`.
* The version string is the repository's `VERSION` and was not changed for this.
