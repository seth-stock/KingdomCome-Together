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
///     dismount, the placement beside the host, the busy refusals, the 50 m "already beside" rule). Only
///     with mp_scene_follow on.
///   * Nothing here moves anyone at a scene's START, nothing ends or skips a scene, and nothing here is a
///     second pull mechanism: the host asks the leash (LeashLogic.NoteHostFastTravel), the joiner's "watch"
///     is the leash's placement under the leash's own refusals, with the leash's own bookkeeping
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
    private int _w153Gen;                                                // bumped by a load / session start: a judgement waiting on a hold is abandoned
    private int _w153Windows, _w153Verdicts, _w153Pulls;

    // ---- joiner ----
    private DateTime _w153HostStoryEndUtc = DateTime.MinValue;           // the host's story scene ENDED: the pull's wording
    private int _w153Notices, _w153Watches;

    private static long W153NowMs() => Environment.TickCount64;

    private bool W153IsHost => _combatRoleApplied && _isDamageAuthority;

    private byte W153HostId() => _leashHostId != 0xFF ? _leashHostId : _hostModeFrom;

    private void Wo153OnConnect()
    {
        Interlocked.Increment(ref _w153Gen);
        lock (_w153Window) _w153Window.Reset();
        _w153HostStoryEndUtc = DateTime.MinValue;
        _ = ExecLuaAsync("if KCD2MP_W153Reset then KCD2MP_W153Reset(\"connect\") end");   // a notice or scene names left by an earlier session
        _ = ExecLuaAsync("if KCD2MP_W153CfgEmit then KCD2MP_W153CfgEmit() end");
    }

    /// <summary>The session ended: nothing of the notice survives it (the host's end edge can never arrive now).</summary>
    private void Wo153OnDisconnect()
    {
        Interlocked.Increment(ref _w153Gen);
        lock (_w153Window) _w153Window.Reset();
        _ = ExecLuaAsync("if KCD2MP_W153Reset then KCD2MP_W153Reset(\"disconnect\") end");
    }

    /// <summary>A peer left. If it was the host, its scenes can never end: the notice goes.</summary>
    private void Wo153OnPeerGone(byte ghostId)
    {
        if (W153IsHost) return;
        byte hid = W153HostId();
        if (hid == 0xFF || ghostId == hid) _ = ExecLuaAsync("if KCD2MP_W153Reset then KCD2MP_W153Reset(\"host-left\") end");
    }

    /// <summary>A load or the session's end (Wo151SceneReset): the engine interrupts every scene, so no window survives.</summary>
    private void Wo153OnSceneReset(string why)
    {
        Interlocked.Increment(ref _w153Gen);
        bool was;
        lock (_w153Window) { was = _w153Window.Open; _w153Window.Reset(); }
        _w153HostStoryEndUtc = DateTime.MinValue;
        if (was) Console.WriteLine($"MP-W153 host: a scene window was open at a {why} -- forgotten (no scene survives it)");
        _ = ExecLuaAsync($"if KCD2MP_W153Reset then KCD2MP_W153Reset(\"{why}\") end");
    }

    /// <summary>
    /// Every position sample of this game (the push site). While a window waits for its end position, the first sample
    /// 0.4 s after the last scene's end edge settles it: the poll loop's sample at the edge itself can predate the engine's
    /// end placement. One unlocked flag read otherwise.
    /// </summary>
    private void Wo153OnHostSample(float x, float y)
    {
        if (!_w153Window.AwaitingEndSample) return;
        lock (_w153Window) _w153Window.NoteSample(W153NowMs(), x, y);
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
                        case "farm": if (SceneFollowLogic.ParseMetres(v, SceneFollowLogic.MinFarM) is float f) _w153FarM = f; break;
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
        // The end edge is the engine's release (after its end placement): where the host stands NOW is where the scene put it,
        // before the player is free again. Not read at the close, 2 s later.
        bool quiet;
        lock (_w153Window) quiet = _w153Window.End(now, name, _lastX, _lastY);
        if (quiet) _ = Wo153CloseAsync(type, name);
    }

    /// <summary>The last scene ended: after the quiet period (a chained scene would have started) the window closes and is judged.</summary>
    private async Task Wo153CloseAsync(string lastType, string lastName)
    {
        try
        {
            int gen = Volatile.Read(ref _w153Gen);
            await Task.Delay(SceneFollowLogic.SettleMs);
            SceneFollowLogic.Closed? closed;
            lock (_w153Window) closed = _w153Window.TryClose(W153NowMs());
            if (closed is not { } c) return;
            // A loading screen can cover the very teleport this is for. The end position is already taken (at the end edge,
            // settled by the first sample after it), so wait for the host's own hold to clear -- up to 20 s -- instead of
            // dropping the verdict. A real LOAD resets the window and bumps the generation: that is abandoned.
            for (int i = 0; i < 20 && (HostHold() & HostPositionNotTheirs) != 0; i++)
            {
                await Task.Delay(1000);
                if (gen != Volatile.Read(ref _w153Gen) || !W153IsHost) { Console.WriteLine($"MP-W153 host: the scene window ({lastType} '{lastName}') was waiting for a load or a down to end when the session or the world changed -- not judged"); return; }
            }
            if (gen != Volatile.Read(ref _w153Gen)) return;
            Wo153Evaluate(c, lastType, lastName);
        }
        catch (Exception ex) { Console.WriteLine($"MP-W153 host: close failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private void Wo153Evaluate(SceneFollowLogic.Closed c, string lastType, string lastName)
    {
        if (!W153IsHost || !_sharedWorld || !_hasPushed) return;
        if ((HostHold() & HostPositionNotTheirs) != 0)
        {
            Console.WriteLine($"MP-W153 host: the scene window closed ({lastType} '{lastName}') and this game is still loading, down or travelling after 20 s -- not judged");
            return;
        }
        double moved = c.MovedM;
        // The joiners the leash itself would pull: in the host's world (not in its own, WO-140), with a fresh state and a current position.
        double? farthest = null;
        foreach (var (id, (st, at)) in _leashJoinerState)
        {
            if ((DateTime.UtcNow - at).TotalSeconds >= LeashFreshS) continue;
            if ((st.Flags & Protocol.LeashFlagInWorld) == 0 || (st.Flags & Protocol.LeashFlagSeparate) != 0) continue;
            if (Wo147LeashDistance(id, _lastX, _lastY) is double d && (farthest is null || d > farthest)) farthest = d;
        }
        var verdict = SceneFollowLogic.Decide(c.Story, moved, farthest, _w153RelocM, _w153FarM);
        Console.WriteLine(FormattableString.Invariant(
            $"MP-W153 host: the scene window closed (last {lastType} '{lastName}', story={(c.Story ? 1 : 0)}): the host moved {moved:F0} m ({c.StartX:F0},{c.StartY:F0} -> {c.EndX:F0},{c.EndY:F0}), the farthest joiner in this world is {(farthest is double f ? f.ToString("F0", CultureInfo.InvariantCulture) + " m away" : "unknown")} -> {verdict} (follow {(_w153Follow ? "on" : "off")}, reloc {_w153RelocM:F0} m, far {_w153FarM:F0} m)"));
        if (verdict == SceneFollowLogic.Verdict.None) return;
        _w153Verdicts++;
        if (!_w153Follow) { Console.WriteLine("MP-W153 host: mp_scene_follow is off -- nobody is brought along"); return; }
        if (Wo153NoteScene(FormattableString.Invariant(
                $"a scene ({lastType} '{lastName}') {(verdict == SceneFollowLogic.Verdict.Relocated ? $"moved the host {moved:F0} m" : $"ended with a joiner {farthest:F0} m from the story")}")))
            _w153Pulls++;
    }

    /// <summary>
    /// Ask the leash to bring every joiner along (the same call the engine's fast travel makes: LeashLogic.NoteHostFastTravel).
    /// No duplicate stamp, neither the leash's nor one of its own: the leash's exists because one fast travel is announced three
    /// ways (the engine's lines, a position jump, a clock jump), whereas this is one request per scene window, and the leash keeps
    /// what is owed (a joiner that was busy is still owed it for up to 120 s; asking again while it is pending changes nothing).
    /// A second relocating scene within 20 s of the first must bring the joiners along again. True = it asked the leash.
    /// </summary>
    private bool Wo153NoteScene(string why)
    {
        if (!_leashEnabled) { Console.WriteLine($"MP-W153 host: {why} -- mp_leash is off: nobody is brought along"); return false; }
        Console.WriteLine($"MP-W153 host: {why} -- {_leashJoinerState.Count} joiner(s) come along (the leash decides who: nobody within 50 m, nobody busy)");
        foreach (var id in _leashJoinerState.Keys) { var l = _leashByJoiner.GetOrAdd(id, _ => new LeashLogic()); lock (l) l.NoteHostFastTravel(); }
        return true;
    }

    // ---------------------------------------------------------------- joiner: the notice and "watch"

    /// <summary>
    /// A peer's Rendered/Ingame scene edge arrived (called from the StoryBeat receive). Only the HOST's
    /// scenes matter, and only to a joiner in the host's world (not one connected from its own save, WO-140:
    /// the host's coordinates mean nothing there): the mod's Lua shows the notice.
    /// </summary>
    private void Wo153OnPeerScene(byte source, bool active, string type, string name)
    {
        if (!_joinedWorld || W153IsHost || _w140Separate) return;
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
    /// dismount (the pull's six tries); the host's position read again right before the placement (the awaits
    /// take seconds); the leash's "no ground beside the host yet" fallback and settle; and its _leashPulledUtc,
    /// so the motion check (WO-147) does not take the teleport for a flight. A result code goes back to the
    /// mod, which has the words.
    ///
    /// The steps mirror Wo114PullAsync's on purpose (a watch is a pull the player asks for). They are copied,
    /// not shared: the pull is the leash's core and cannot be exercised here without the game. A later change
    /// to the pull's placement rules must be repeated here until the two are merged (docs/WO-153-findings.md 7).
    /// The pull lock (_leashPulling) is held only for the PLACEMENT, never across the waits before it: a host
    /// pull that arrives while this player is still being asked about their horse is not dropped (a dropped
    /// pull reports no result, and the host counts it as a failure; three disarm the leash).
    /// </summary>
    private async Task Wo153WatchAsync()
    {
        string code = "failed";
        bool locked = false;
        try
        {
            byte hid = W153HostId();
            if (!_joinedWorld || hid == 0xFF || _w140Separate) { code = "notjoined"; Console.WriteLine("MP-W153 watch: not in the host's world -- nothing to stand beside"); return; }
            if (Wo153HostTarget(hid) is null) { code = "nopos"; Console.WriteLine("MP-W153 watch: no fresh position of the host -- not moved"); return; }
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
                for (int i = 0; i < 6 && !off; i++)
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
            if (Interlocked.Exchange(ref _leashPulling, 1) == 1) { code = "pulling"; Console.WriteLine("MP-W153 watch: a pull is placing this player right now -- it puts them beside the host"); return; }
            locked = true;
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

    private string Wo153StatsLine()
    {
        int open, running;
        lock (_w153Window) { open = _w153Window.Open ? 1 : 0; running = _w153Window.Depth; }
        return $"MP-W153 stats follow={(_w153Follow ? "on" : "off")} windows={_w153Windows} window_open={open} scenes_running={running} verdicts={_w153Verdicts} pulls_asked={_w153Pulls} notices={_w153Notices} watches={_w153Watches}";
    }
}
