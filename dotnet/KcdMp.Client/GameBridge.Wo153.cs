// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>
/// WO-153 -- a partner in a cutscene (docs/WO-153-findings.md). Three small things on top of what exists:
///
///   * The JOINER is told when the HOST is in a cutscene (the host's Rendered/Ingame edge already reaches
///     every peer, WO-98) and may press F11 to stand beside the host to watch, or F12 / nothing to keep
///     playing. The engine has no way to show one player another's camera (WO-149 6.1, option C): "watch"
///     means standing beside the host's staged scene and looking at it from your own camera.
///   * The HOST, when a scene window closes with the host somewhere else, or after a story scene with a
///     joiner left far away, brings every joiner along through the leash's own pull (WO-114/147: the
///     dismount, the placement beside the host, the busy refusals). Only with mp_scene_follow on.
///   * Nothing here moves anyone at a scene's START, nothing ends or skips a scene, and nothing here is a
///     second pull mechanism: the host asks the leash (Wo114NoteHostFastTravel), the joiner's "watch" is
///     the leash's placement under the leash's own refusals.
///
/// The rules are SceneFollowLogic (pure, Wo153Tests). Rollback: mp_scene_follow off / mp_scene_notice off.
/// </summary>
public partial class GameBridge
{
    // ---- the host's settings (mirrors of the mod's KCD2MP.w153; Lua emits w153_cfg) ----
    private volatile bool _w153Follow;                                   // mp_scene_follow, default OFF
    private float _w153RelocM = SceneFollowLogic.RelocDefaultM;          // mp_scene_follow_m
    private float _w153FarM = SceneFollowLogic.FarDefaultM;              // mp_scene_follow_far_m

    // ---- host: the scene window ----
    private readonly SceneFollowLogic.Window _w153Window = new();
    private int _w153Windows, _w153Pulls;

    // ---- joiner ----
    private DateTime _w153HostSceneSeenUtc = DateTime.MinValue;          // the host's story-scene edge, for the pull's wording
    private int _w153Notices, _w153Watches;

    private static long W153NowMs() => Environment.TickCount64;

    private bool W153IsHost => _combatRoleApplied && _isDamageAuthority;

    private byte W153HostId() => _leashHostId != 0xFF ? _leashHostId : _hostModeFrom;

    private void Wo153OnConnect()
    {
        lock (_w153Window) _w153Window.Reset();
        _w153HostSceneSeenUtc = DateTime.MinValue;
        _ = ExecLuaAsync("if KCD2MP_W153CfgEmit then KCD2MP_W153CfgEmit() end");
    }

    // ---------------------------------------------------------------- mod events

    /// <summary><c>w153_cfg follow=on|off relocm=25 farm=150</c> (the host's settings), <c>w153_watch</c> (this player's F11).</summary>
    private void Wo153OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w153_cfg":
                foreach (var kv in (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    int eq = kv.IndexOf('=');
                    if (eq <= 0) continue;
                    string k = kv[..eq], v = kv[(eq + 1)..];
                    switch (k)
                    {
                        case "follow": _w153Follow = v == "on"; break;
                        case "relocm": if (SceneFollowLogic.ParseMetres(v) is float r) _w153RelocM = r; break;
                        case "farm": if (SceneFollowLogic.ParseMetres(v) is float f) _w153FarM = f; break;
                    }
                }
                Console.WriteLine(FormattableString.Invariant(
                    $"MP-W153 cfg follow={(_w153Follow ? "on" : "off")} reloc_m={_w153RelocM:F0} far_m={_w153FarM:F0} role={(!_combatRoleApplied ? "unknown" : _isDamageAuthority ? "host (decides)" : "joiner (the host's value counts)")}"));
                return;
            case "w153_watch":
                _ = Wo153WatchAsync();
                return;
        }
    }

    // ---------------------------------------------------------------- host: the scene window

    /// <summary>
    /// Every scene edge on this machine, any kind (called from ApplyLocalCutsceneEdge, which the engine's
    /// release edge also reaches). Host only; a joiner's scenes bring nobody anywhere.
    /// </summary>
    private void Wo153OnLocalScene(bool active, string type, string name)
    {
        if (!W153IsHost || !_sharedWorld) return;
        long now = W153NowMs();
        if (active)
        {
            bool opened;
            lock (_w153Window) opened = _w153Window.Start(now, _lastX, _lastY, type, name);
            if (opened) { _w153Windows++; Console.WriteLine(FormattableString.Invariant($"MP-W153 host: a scene window opens ({type} '{name}') at ({_lastX:F0}, {_lastY:F0})")); }
            return;
        }
        (float StartX, float StartY, bool Story)? closed;
        lock (_w153Window) closed = _w153Window.End(now, name);
        if (closed is not { } c) return;
        _ = Wo153EvaluateAsync(c.StartX, c.StartY, c.Story, type, name);
    }

    private async Task Wo153EvaluateAsync(float startX, float startY, bool story, string lastType, string lastName)
    {
        try
        {
            await Task.Delay(SceneFollowLogic.SettleMs);
            if (!W153IsHost || !_sharedWorld || !_hasPushed) return;
            var excluded = LeashLogic.Hold.HostDowned | LeashLogic.Hold.HostLoading | LeashLogic.Hold.HostReloading | LeashLogic.Hold.HostTravelling;
            var hold = HostHold();
            if ((hold & excluded) != 0 || DateTime.UtcNow < _leashJumpQuietUntilUtc)
            {
                Console.WriteLine($"MP-W153 host: the scene window closed ({lastType} '{lastName}') while this game was loading, down or travelling -- nobody is brought along on a position that is not the scene's");
                return;
            }
            double moved = LeashLogic.Dist2D(startX, startY, _lastX, _lastY);
            double? farthest = null;
            foreach (var id in _leashJoinerState.Keys)
                if (Wo147LeashDistance(id, _lastX, _lastY) is double d && (farthest is null || d > farthest)) farthest = d;
            var verdict = SceneFollowLogic.Decide(story, moved, farthest, _w153RelocM, _w153FarM);
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W153 host: the scene window closed (last {lastType} '{lastName}', story={(story ? 1 : 0)}): the host moved {moved:F0} m, the farthest joiner is {(farthest is double f ? f.ToString("F0", CultureInfo.InvariantCulture) + " m away" : "unknown")} -> {verdict} (follow {(_w153Follow ? "on" : "off")}, reloc {_w153RelocM:F0} m, far {_w153FarM:F0} m)"));
            if (verdict == SceneFollowLogic.Verdict.None) return;
            if (!_w153Follow) { Console.WriteLine("MP-W153 host: mp_scene_follow is off -- nobody is brought along"); return; }
            _w153Pulls++;
            Wo114NoteHostFastTravel(FormattableString.Invariant(
                $"a scene ({lastType} '{lastName}') {(verdict == SceneFollowLogic.Verdict.Relocated ? $"moved the host {moved:F0} m" : $"ended with a joiner {farthest:F0} m from the story")}"));
        }
        catch (Exception ex) { Console.WriteLine($"MP-W153 host: evaluate failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- joiner: the notice and "watch"

    /// <summary>
    /// A peer's Rendered/Ingame scene edge arrived (called from the StoryBeat receive). Only the HOST's
    /// scenes matter, and only to a joiner in the host's world: the mod's Lua shows the notice.
    /// </summary>
    private void Wo153OnPeerScene(byte source, bool active, string type, string name)
    {
        if (!_joinedWorld || W153IsHost) return;
        byte hid = W153HostId();
        if (hid == 0xFF || source != hid) return;
        _w153HostSceneSeenUtc = DateTime.UtcNow;
        if (active) _w153Notices++;
        Console.WriteLine($"MP-W153 joiner: the host's {type} scene '{name}' {(active ? "started" : "ended")} (own copy {(_localCutsceneActive ? "playing" : "not playing")})");
        _ = ExecLuaAsync($"if KCD2MP_W153HostScene then KCD2MP_W153HostScene(\"{source}\", {(active ? "true" : "false")}, \"{EscapeLua(type)}\", \"{EscapeLua(name)}\", {(_localCutsceneActive ? "true" : "false")}) end");
    }

    /// <summary>The pull's wording: after the host's own story scene it was the scene, not a fast travel.</summary>
    private string Wo153PulledText(bool fastTravelReason) =>
        fastTravelReason && (DateTime.UtcNow - _w153HostSceneSeenUtc).TotalSeconds < SceneFollowLogic.SceneTextWindowS
            ? SceneFollowLogic.Text.JoinerPulledScene
            : fastTravelReason ? LeashLogic.Text.JoinerPulledFastTravel : LeashLogic.Text.JoinerPulledDistance;

    /// <summary>
    /// F11 on the notice: stand beside the host. The leash's own placement, under the leash's own refusals
    /// (a load, a dialogue, a cutscene of this player's, a down); never while mounted without a clean
    /// dismount. A result code goes back to the mod, which has the words.
    /// </summary>
    private async Task Wo153WatchAsync()
    {
        string code = "failed";
        bool locked = false;
        try
        {
            byte hid = W153HostId();
            if (!_joinedWorld || hid == 0xFF) { code = "notjoined"; Console.WriteLine("MP-W153 watch: not in a host's world -- nothing to stand beside"); return; }
            if (!_ghostLastPos.TryGetValue(hid, out var gp) || (DateTime.UtcNow - gp.AtUtc).TotalSeconds > LeashFreshS)
            {
                code = "nopos"; Console.WriteLine("MP-W153 watch: no fresh position of the host -- not moved"); return;
            }
            if (Interlocked.Exchange(ref _leashPulling, 1) == 1) { code = "busy"; Console.WriteLine("MP-W153 watch: a placement is already running -- ignored"); return; }
            locked = true;
            _w153Watches++;
            string busy = await AskModAsync("KCD2MP_Wo114BusyNow", 2000);
            bool dlg = busy.Contains("d=1"), mounted = busy.Contains("m=1") || (!busy.Contains("m=0") && _lastRiding);
            ushort f = JoinerLeashFlags();
            if (dlg) f |= Protocol.LeashFlagDialogue;
            var why = Wo147Rules.JoinerRefusesPull(LeashLogic.JoinerHold(f), false);
            if (why != LeashLogic.Hold.None)
            {
                code = "busy";
                Console.WriteLine($"MP-W153 watch: refused -- {LeashLogic.HoldText(why)}; never moved while busy");
                return;
            }
            if (mounted)
            {
                bool off = false;
                for (int i = 0; i < 4 && !off; i++)
                {
                    string r = await AskModAsync("KCD2MP_Wo114Dismount", 2000);
                    off = r.Contains("mounted=no");
                    Console.WriteLine($"MP-W153 watch: dismount try {i + 1}: {r}");
                    if (!off) await Task.Delay(500);
                }
                if (!off) { code = "mounted"; Console.WriteLine("MP-W153 watch: still mounted -- never moved on a horse"); return; }
                await Task.Delay(300);
            }
            var pr = await _combat.JoinPlaceAsync(gp.X, gp.Y, gp.Z, LeashPlaceDistM);
            if (pr is null) { code = "failed"; Console.WriteLine("MP-W153 watch: no answer from the plugin (not placed)"); return; }
            code = pr.Ok ? "placed" : "failed";
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W153 watch: {(pr.Ok ? "placed" : "NOT placed")} beside the host ({gp.X:F0}, {gp.Y:F0}) residual={pr.Residual:F2}"));
        }
        catch (Exception ex) { code = "failed"; Console.WriteLine($"MP-W153 watch failed: {ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            if (locked) Interlocked.Exchange(ref _leashPulling, 0);
            _ = ExecLuaAsync($"if KCD2MP_W153Result then KCD2MP_W153Result(\"{code}\") end");
        }
    }

    private string Wo153StatsLine() =>
        $"MP-W153 stats follow={(_w153Follow ? "on" : "off")} windows={_w153Windows} pulls_asked={_w153Pulls} notices={_w153Notices} watches={_w153Watches}";
}
