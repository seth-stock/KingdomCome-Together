// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Diagnostics;
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
///     the leash's placement under the leash's own refusals, with the leash's own bookkeeping
///     (_leashPulledUtc, so the joiner's motion check does not read the teleport as flight).
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
    private int _w153Windows, _w153Verdicts, _w153Pulls;

    // ---- joiner ----
    private DateTime _w153HostStoryEndUtc = DateTime.MinValue;           // the host's story scene ENDED: the pull's wording
    private int _w153Notices, _w153Watches;

    private static long W153NowMs() => Environment.TickCount64;

    private bool W153IsHost => _combatRoleApplied && _isDamageAuthority;

    private byte W153HostId() => _leashHostId != 0xFF ? _leashHostId : _hostModeFrom;

    private void Wo153OnConnect()
    {
        lock (_w153Window) _w153Window.Reset();
        _w153HostStoryEndUtc = DateTime.MinValue;
        _ = ExecLuaAsync("if KCD2MP_W153CfgEmit then KCD2MP_W153CfgEmit() end");
    }

    /// <summary>A load or the session's end (Wo151SceneReset): the engine interrupts every scene, so no window survives.</summary>
    private void Wo153OnSceneReset(string why)
    {
        bool was;
        lock (_w153Window) { was = _w153Window.Open; _w153Window.Reset(); }
        _w153HostStoryEndUtc = DateTime.MinValue;
        if (was) Console.WriteLine($"MP-W153 host: a scene window was open at a {why} -- forgotten (no scene survives it)");
    }

    // ---------------------------------------------------------------- mod events

    /// <summary><c>w153_cfg follow=on|off relocm=25 farm=150</c> (the host's settings), <c>w153_watch</c> (this player's F11), <c>w153_status</c>.</summary>
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
            case "w153_status":
                Console.WriteLine(Wo153StatsLine());
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
            // No position sample yet (a scene during the very first seconds): the window's start would be (0, 0) and
            // every real position would read as a huge relocation. No window; the scene is simply not measured.
            if (!_hasPushed) { Console.WriteLine($"MP-W153 host: a {type} scene '{name}' started before this game's first position -- not measured"); return; }
            bool opened;
            lock (_w153Window) opened = _w153Window.Start(now, _lastX, _lastY, type, name);
            if (opened) { _w153Windows++; Console.WriteLine(FormattableString.Invariant($"MP-W153 host: a scene window opens ({type} '{name}') at ({_lastX:F0}, {_lastY:F0})")); }
            return;
        }
        bool quiet;
        lock (_w153Window) quiet = _w153Window.End(now, name);
        if (quiet) _ = Wo153CloseAsync(type, name);
    }

    /// <summary>The last scene ended: after the quiet period (a chained scene would have started) the window closes and is measured.</summary>
    private async Task Wo153CloseAsync(string lastType, string lastName)
    {
        try
        {
            await Task.Delay(SceneFollowLogic.SettleMs);
            (float StartX, float StartY, bool Story)? closed;
            lock (_w153Window) closed = _w153Window.TryClose(W153NowMs());
            if (closed is { } c) Wo153Evaluate(c.StartX, c.StartY, c.Story, lastType, lastName);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W153 host: close failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private void Wo153Evaluate(float startX, float startY, bool story, string lastType, string lastName)
    {
        if (!W153IsHost || !_sharedWorld || !_hasPushed) return;
        var excluded = LeashLogic.Hold.HostDowned | LeashLogic.Hold.HostLoading | LeashLogic.Hold.HostReloading | LeashLogic.Hold.HostTravelling;
        var hold = HostHold();
        if ((hold & excluded) != 0 || DateTime.UtcNow < _leashJumpQuietUntilUtc)
        {
            Console.WriteLine($"MP-W153 host: the scene window closed ({lastType} '{lastName}') while this game was loading, down or travelling -- nobody is brought along on a position that is not the scene's");
            return;
        }
        double moved = LeashLogic.Dist2D(startX, startY, _lastX, _lastY);
        // The joiners the leash itself would pull: in the host's world (not in its own, WO-140), with a fresh state and a current position.
        double? farthest = null;
        foreach (var (id, (st, at)) in _leashJoinerState)
        {
            if ((DateTime.UtcNow - at).TotalSeconds >= LeashFreshS) continue;
            if ((st.Flags & Protocol.LeashFlagInWorld) == 0 || (st.Flags & Protocol.LeashFlagSeparate) != 0) continue;
            if (Wo147LeashDistance(id, _lastX, _lastY) is double d && (farthest is null || d > farthest)) farthest = d;
        }
        var verdict = SceneFollowLogic.Decide(story, moved, farthest, _w153RelocM, _w153FarM);
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W153 host: the scene window closed (last {lastType} '{lastName}', story={(story ? 1 : 0)}): the host moved {moved:F0} m, the farthest joiner in this world is {(farthest is double f ? f.ToString("F0", CultureInfo.InvariantCulture) + " m away" : "unknown")} -> {verdict} (follow {(_w153Follow ? "on" : "off")}, reloc {_w153RelocM:F0} m, far {_w153FarM:F0} m)"));
        if (verdict == SceneFollowLogic.Verdict.None) return;
        _w153Verdicts++;
        if (!_w153Follow) { Console.WriteLine("MP-W153 host: mp_scene_follow is off -- nobody is brought along"); return; }
        bool asked = Wo114NoteHostFastTravel(FormattableString.Invariant(
            $"a scene ({lastType} '{lastName}') {(verdict == SceneFollowLogic.Verdict.Relocated ? $"moved the host {moved:F0} m" : $"ended with a joiner {farthest:F0} m from the story")}"));
        if (asked) _w153Pulls++;
        else Console.WriteLine("MP-W153 host: the leash did not take it (a fast travel was noted in the last 20 s, or mp_leash is off)");
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
        if (!active) _w153HostStoryEndUtc = DateTime.UtcNow;   // the wording of a pull right after: only an END counts
        else _w153Notices++;
        Console.WriteLine($"MP-W153 joiner: the host's {type} scene '{name}' {(active ? "started" : "ended")} (own copy {(_localCutsceneActive ? "playing" : "not playing")})");
        _ = ExecLuaAsync($"if KCD2MP_W153HostScene then KCD2MP_W153HostScene(\"{source}\", {(active ? "true" : "false")}, \"{EscapeLua(type)}\", \"{EscapeLua(name)}\", {(_localCutsceneActive ? "true" : "false")}) end");
    }

    /// <summary>The pull's wording: just after the host's own story scene ended it was the scene, not a fast travel.</summary>
    private string Wo153PulledText(bool fastTravelReason) =>
        fastTravelReason && SceneFollowLogic.UseSceneWording(DateTime.UtcNow, _w153HostStoryEndUtc)
            ? SceneFollowLogic.Text.JoinerPulledScene
            : fastTravelReason ? LeashLogic.Text.JoinerPulledFastTravel : LeashLogic.Text.JoinerPulledDistance;

    /// <summary>
    /// The host's position as a placement should use it, read NOW: the newest sample of the host's trail when it
    /// is under 2 s old (what a pull uses), else the last position the host's ghost reported, when fresh.
    /// </summary>
    private (float X, float Y, float Z)? Wo153HostTarget(byte hid)
    {
        if (_w147Trail.TryGetValue(hid, out var trail))
        {
            lock (trail)
            {
                if (trail.Count > 0 && W147StampMs(Stopwatch.GetTimestamp()) - trail[^1].AtMs < 2000)
                    return (trail[^1].X, trail[^1].Y, trail[^1].Z);
            }
        }
        if (_ghostLastPos.TryGetValue(hid, out var gp) && (DateTime.UtcNow - gp.AtUtc).TotalSeconds <= LeashFreshS)
            return (gp.X, gp.Y, gp.Z);
        return null;
    }

    /// <summary>
    /// F11 on the notice: stand beside the host. The leash's own placement under the leash's own refusals
    /// (a load, a dialogue, a cutscene of this player's, a down); never while mounted without a clean
    /// dismount; the host's position read again right before the placement (the awaits take seconds);
    /// the leash's "no ground beside the host yet" fallback and settle; and its _leashPulledUtc, so the
    /// motion check (WO-147) does not take the teleport for a flight. A result code goes back to the mod,
    /// which has the words.
    /// </summary>
    private async Task Wo153WatchAsync()
    {
        string code = "failed";
        bool locked = false;
        try
        {
            byte hid = W153HostId();
            if (!_joinedWorld || hid == 0xFF) { code = "notjoined"; Console.WriteLine("MP-W153 watch: not in a host's world -- nothing to stand beside"); return; }
            if (Wo153HostTarget(hid) is null) { code = "nopos"; Console.WriteLine("MP-W153 watch: no fresh position of the host -- not moved"); return; }
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
            if (Wo153HostTarget(hid) is not { } t) { code = "nopos"; Console.WriteLine("MP-W153 watch: the host's position went stale while this was checked -- not moved"); return; }
            var pr = await _combat.JoinPlaceAsync(t.X, t.Y, t.Z, LeashPlaceDistM);
            if (pr is null) { code = "failed"; Console.WriteLine("MP-W153 watch: no answer from the plugin (not placed)"); return; }
            bool fallback = false;
            if (!pr.Ok && !pr.Snapped)
            {
                // The area around the host is not loaded here yet (the usual case after a scene that moved the host): a spot
                // the host just stood on, placed exactly; then the ground beside the host once it has loaded (the pull's way).
                var spot = Wo147PullFallbackSpot(t.X, t.Y, t.Z);
                var fb = await _combat.JoinPlaceAsync(spot.X, spot.Y, spot.Z, 0f);
                Console.WriteLine(FormattableString.Invariant(
                    $"MP-W153 watch: no ground beside the host here yet -- placed on a spot the host stood on ({spot.X:F1}, {spot.Y:F1}, {spot.Z:F1}): {(fb is { Ok: true } ? "placed" : "NOT placed")}"));
                if (fb is not null) { pr = fb; fallback = true; }
            }
            code = pr.Ok ? "placed" : "failed";
            if (pr.Ok) _leashPulledUtc = DateTime.UtcNow;   // the motion check holds off 5 s: a placement is not a flight
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W153 watch: {(pr.Ok ? "placed" : "NOT placed")} beside the host ({t.X:F0}, {t.Y:F0}){(fallback ? " (on the host's own spot)" : "")} residual={pr.Residual:F2}"));
            if (pr.Ok && fallback) _ = Wo147SettleBesideHostAsync(0, "watch");
        }
        catch (Exception ex) { code = "failed"; Console.WriteLine($"MP-W153 watch failed: {ex.GetType().Name}: {ex.Message}"); }
        finally
        {
            if (locked) Interlocked.Exchange(ref _leashPulling, 0);
            _ = ExecLuaAsync($"if KCD2MP_W153Result then KCD2MP_W153Result(\"{code}\") end");
        }
    }

    private string Wo153StatsLine() =>
        $"MP-W153 stats follow={(_w153Follow ? "on" : "off")} windows={_w153Windows} verdicts={_w153Verdicts} pulls_asked={_w153Pulls} notices={_w153Notices} watches={_w153Watches}";
}
