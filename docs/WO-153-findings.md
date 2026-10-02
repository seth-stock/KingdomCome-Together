# WO-153 — a partner in a cutscene: the notice, "watch", and being brought along

Kingdom Come: Together. Official repository:
https://github.com/DeepFriedDepp/KingdomCome-Together. Unofficial; not affiliated with
or endorsed by Warhorse Studios or PLAION.

Written 2026-10-02 on branch `feature/wo-153-scene-notice-follow` (off `main` at `11bc971`,
0.43.0). **No game was run.** The KCD2 Modding Tools are not installed on the machine this was
written on, so everything below is **(unit)** or **(synthetic)**, never **(observed)**.

Marks: **(unit)** an xUnit test of the agent's pure rules · **(synthetic)** the real `kdcmp.lua`
under MoonSharp with the engine stubbed (`tools\Test-WO153Synthetic.ps1`) · **(code-verified)** read
in the code · **(not verified)** needs the game · **(not built)** with the reason.

## 0. The answer

Asked for: *the second player is told when the host is in a cutscene and can watch it or keep
playing; for a cutscene that takes the host somewhere else or locks the host into a gameplay section,
the second player is brought along.* Built, offline-verified, **not yet seen in the game**:

| Piece | What it does | Switch | Default |
|---|---|---|---|
| The notice | When the **host's** Rendered or Ingame scene starts, the joiner sees "Your host is in a cutscene." with a 20 s countdown and two keys | `mp_scene_notice on\|off` | **on** (words only) |
| Watch | **F11** (or `mp_scene_watch`) stands the joiner beside the host, through the leash's own placement and its own refusals | — | the joiner's choice |
| Keep playing | **F12**, any decline key, or no answer for 20 s: nothing happens | `mp_scene_play` | — |
| Bring-along | When the host's scene window closes with the host **25 m or more** from where it opened, or after a **story scene** with a joiner **150 m or more** away, the host's agent asks the leash to pull every joiner beside the host | `mp_scene_follow on\|off` (HOST) | **off** |
| Thresholds | `mp_scene_follow_m` (25), `mp_scene_follow_far_m` (150); whole metres 5..5000 | | |
| Status | `mp_scene_status` writes `WO153-STATUS`; the agent writes `MP-W153` lines | | |

No wire message was added or changed; `Protocol.Version` and `VERSION` are untouched.

### 0.1 Three things this does NOT do, plainly

1. **"Keep playing" cannot stop the joiner's own copy of a scene.** WO-149 §0.2 measured that when a
   quest step is mirrored, the joiner's own copy of the scene starts 0.1–1.5 s after the host's. That
   is the "both watch" design and it is unchanged: the notice is **withdrawn** the moment this game's
   own scene starts (nothing is left to offer, and F11 would be refused). The choice is real when the
   joiner's copy does *not* start: the joiner is far away, the scene is not mirrored, or it started on
   one machine only (WO-149 §4.4, field cases 5 and 6). Making "keep playing" win over a copy that
   *would* start needs WO-149's **start gate** on the joiner's `EnqueueCutscene` handler port. That is a
   native hook on an engine class, and a wrong layout there crashes the game; it cannot be written or
   checked without the Modding Tools build. **(not built)** It is the first thing to build once a live
   run exists, and it changes none of this WO's interfaces.
2. **"Watch" is not a camera.** The engine has no way to show one player another's camera
   (WO-149 §6.1, option C). Watch means standing beside the host at the staged scene and looking at it
   from your own camera. The notice and the key say exactly that.
3. **"Locks the host into a gameplay section" is approximated, not detected.** The engine gives no
   read of that lock. The observable stand-ins are (a) the host ended the scene elsewhere (25 m), and
   (b) a story scene ended with a joiner 150 m away: the story continues where the host stands and the
   joiner is not there. A scene that locks the host in place with the joiner already near brings
   nobody along, because nobody needs bringing. A lock-in that happens with no scene at all is not
   covered. **(inferred)**

## 1. The rules (SceneFollowLogic, pure)

* A **scene window** opens at the host's first scene start (any kind: Rendered, Ingame, Fader, Text,
  SkipTime) and closes at the last matching end. Scenes are tracked **by name**: a repeated start counts
  once, and an end that matches no running scene (a duplicate release, the guard giving up and the engine
  releasing later, a load) closes nothing and can never close an outer scene early. A window older than
  10 minutes is dropped, not evaluated. **(unit: 12 window tests)**
* 2 s after the close (the end placement has run) the agent measures: how far the host is from where the
  window opened, and how far the **farthest** joiner with a fresh position is.
  * moved ≥ `mp_scene_follow_m` → **Relocated**
  * else a story scene (Rendered/Ingame) and farthest joiner ≥ `mp_scene_follow_far_m` → **Far**
  * else nothing. **(unit: decision table, NaN and negative displacements, exact thresholds)**
* Not evaluated at all if, at that moment, this game is loading, reloading, travelling or downed, or in the
  leash's post-load quiet window: a position read during a load is not the scene's.
* A verdict with `mp_scene_follow on` calls the leash's **existing** fast-travel path
  (`Wo114NoteHostFastTravel`): every joiner is pulled beside the host as soon as nothing holds it, **unless
  already within 50 m**, with the leash's own dismount, placement, fall-damage hold, busy refusals, 60 s
  hold cap and failure back-off. This WO adds **no** second pull mechanism and changes **none** of the
  leash's numbers (WO-114's are the maintainer's).
* Nothing moves anyone at a scene's **start**; nothing ends, skips or suppresses a scene.

## 2. The joiner's side

* The host's edge already reaches every peer (`StoryBeat` kind 6, WO-98). The agent calls the mod only for
  the **host's** scene, on a joiner **in the host's world**; Lua shows the words natively
  (`hud.ShowInfoText`, like every prompt) plus a two-row overlay with the seconds left.
* **Keys.** Only the explicit F11 action (`kcd2mp_dice_bank`) means watch. The generic accept actions
  (`confirm`, `ui_accept`, `dialog_answer1`), which the sleep prompt accepts, are **never** a yes here,
  because a yes moves the player. Any decline action (F12, `cancel`, `ui_cancel`) is "keep playing". Only a
  press answers. The sleep prompt (WO-140) is answered first when both are up, so one press never answers
  two prompts, and the dice board's earlier branch still wins. **(synthetic)**
* **Watch** runs under the leash's refusals: not while this player is loading, in a dialogue, in a cutscene
  of their own, or downed; mounted → dismount first (up to 4 tries), never placed on a horse. A result code
  (`placed | busy | nopos | mounted | failed | notjoined`) comes back and the mod says it in its own words.
  It reuses the leash's pull lock, so it can never run beside a real pull. It does **not** touch the
  leash's pull sequence number or its state report to the host.
* After the host's own **story** scene, a pull is worded "Your host's scene took them elsewhere; you were
  brought along." instead of "Your host fast-travelled." A scene made of faders only (no edge reaches the
  joiner for those) still reads "fast-travelled". **(known wording nuance, cosmetic)**

## 3. Review findings fixed before the first live run

| # | Found | Cause | Fix | Evidence |
|---|---|---|---|---|
| 1 | The notice could stay up over the joiner's **own** copy of the scene | the copy starts 0.1–1.5 s after the host's, after the notice | withdrawn in `KCD2MP_SetCutscene` when this game's own scene starts | synthetic (6 checks) |
| 2 | A script reload left the agent's `mp_scene_follow` **on** while the Lua came back **off** | the Lua is reborn with defaults; the agent kept its copy | the agent asks the mod to re-send its settings when it sees the mod reborn (`OnModInitDetected`) | code-verified |
| 3 | The notice lingered up to 1 s past its 20 s | `math.ceil(-0.5)` is `-0`, which is not `< 0` | compare on the clock, not the rounded countdown | synthetic |
| 4 | Lua accepted `1e3` and `0x10` as metres; the agent's parser refuses them | `tonumber` is permissive | digits only on both sides | synthetic |
| 5 | A duplicate end could close an outer scene's window early | a counter, not a set | scenes tracked by name | unit |

Also found on the way (separate branch `fix/kcd2-install-selection`, `b73c204`): the agent could take
Kingdom Come: Deliverance **1**'s `kcd.log` and `Tables.pak` as the game's. See `docs/WO-152-orientation.md` §2.1.

## 4. Verification status

| What | Result |
|---|---|
| `dotnet build KCD2-MP.sln` | 0 errors |
| `KcdMp.Client.Tests` | 875 / 875 (824 before; +41 for this WO, +10 for the KCD1 fix) |
| `KcdMp.Relay.Tests` / `KcdMp.Farkle.Tests` | 62 / 62, 59 / 59 |
| `Test-WO153Synthetic.ps1` | 63 / 63 (defaults, 7 commands, settings line, metres parser, notice, countdown, race, timeout, keys, precedence, results, status) |
| Every other offline Lua suite (42) | exit 0, 0 failed (the real `kdcmp.lua` is loaded by all of them) |
| The three static checks | 7/7, 6/6, 7/7 |
| `kdcmp.pak` | rebuilt with `tools\Build-And-Install-Mod.ps1 -NoInstall`; its Lua is byte-identical to the source |
| **In the game** | **not verified** (no Modding Tools on this machine) |

## 5. What a live run must show (two players; the checklist items are 104–109)

1. **The notice** appears on the joiner within about a second of the host's Rendered/Ingame scene starting,
   and never when the joiner's own copy is already playing. `WO153-NOTICE` in `kcd.log`, `MP-W153 joiner:` in `agent.log`.
2. **F11 watch** places the joiner beside the host without a fall hurt, dismounting first if riding;
   refused (with the words) in a dialogue, a cutscene of their own, a load.
3. **The race:** when the joiner's own copy starts, the notice goes (`WO153-NOTICE withdrawn`).
4. **Bring-along** with `mp_scene_follow on` on the host: a quest scene that teleports the host (a
   fader teleport under 200 m is the interesting case: the leash's own jump rule does not catch it);
   the joiner arrives beside the host after it, once. `MP-W153 host: the scene window closed ... -> Relocated`.
5. **No false pull:** a scene that does not move the host with the joiner near; a scene ended by a **load**
   (the quiet-window skip line); the host's fast travel (one pull, not two: the leash dedupes 20 s).
6. **Rollback:** `mp_scene_follow off` on the host, `mp_scene_notice off` on the joiner, each takes effect at once.

What would change the design: if the joiner's copy of the scene *never* starts in the cases the notice is
for, then "watch" is the only way to see it and the start gate is not needed; if it always starts, the
choice is empty until the start gate exists (§0.1, item 1). The first live run answers this.

## 6. Rollback and files

`mp_scene_follow off` (host) and `mp_scene_notice off` (joiner) remove every behaviour; with both off the
mod behaves as 0.43.0. New: `dotnet/KcdMp.Client/SceneFollowLogic.cs`, `GameBridge.Wo153.cs`,
`dotnet/KcdMp.Client.Tests/Wo153Tests.cs`, `tools/Test-WO153Synthetic.{ps1,lua}`. Changed: `GameBridge.cs`
(four one-line hooks), `GameBridge.Wo114.cs` (the pull's wording, one line), `kdcmp.lua` (one block, three
one-line hooks), `kdcmp.pak` (rebuilt).

## 7. Not built, and what each needs

* **The start gate** (§0.1): a native hook on the joiner's cutscene handler; needs the Modding Tools build.
* **A dialogue notice** ("your host is talking to ..."): the local player's `IsInDialog()` is read in Lua
  but never sent; needs a wire message and one live check of the call (WO-80).
* **A launcher "in sync / drifting" label**: the checkpoint compare exists (WO-151 §3.1); the label needs the launcher UI.
* **A game-build mismatch warning**: not designed here.
