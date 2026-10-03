// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Globalization;

namespace KcdMp.Client;

/// <summary>
/// WO-153, the story half (docs/WO-153-findings.md): where the story is, told to the joiners, and the joiners kept there.
///
///   * LOCKED SECTIONS. The host's quest States say which main quest it is in (StorySections, StoryLock). While it is in a
///     quest the story stages in one place -- the wedding in Semine, Trosky, the move to Kuttenberg, the devil's job, the
///     burning of the Jewish quarter, the cardinal, the Italian Job, the final set -- every joiner is brought beside the host
///     when it enters, and held on a tight leash (mp_story_tether_m, default 120 m: the leash's own warning, countdown and
///     pull, with tighter numbers) until it leaves. Nothing here is a second pull mechanism.
///   * LEVEL TRANSITIONS. The host's game loaded another region: the joiners are told, and brought along once the host is up.
///   * DIALOGUE. The joiner is told when the host is in a conversation (the mod already reads it for the leash).
///   * KEEP PLAYING. A joiner who chose mp_scene_mode play has the native cutscene gate armed (wo153.cpp): their own copy of a
///     scene the host's mirrored step starts is not played.
/// </summary>
public partial class GameBridge
{
    // ---- the host's settings (mirrors of the mod's KCD2MP.w153; Lua emits w153_cfg) ----
    private volatile bool _w153StoryOn = true;                         // mp_story_lock
    private float _w153TetherM = 120f;                                 // mp_story_tether_m
    // ---- the joiner's choice ----
    private volatile int _w153SceneMode;                               // mp_scene_mode: 0 watch (own copy plays), 1 play (own copy skipped)

    private readonly StoryLock _w153Lock = new();
    private volatile bool _w153TetherOn;
    private string? _w153PrevLevel;
    private int _w153Enters, _w153Leaves, _w153Levels, _w153DialogueEdges;
    private byte _w153GateSent;                  // what the plugin last confirmed (0 = off)
    private bool _w153GateMaybeOn;               // the plugin may have the gate on (we turned it on, or a session just began): an off command is owed
    private int _w153GateOffFails;
    private DateTime _w153GateSentUtc = DateTime.MinValue, _w153DialogueSentUtc = DateTime.MinValue;
    private bool _w153LastHostDialogue;

    /// <summary>The leash's numbers while a locked section holds the joiners close: the tighter of the host's own and the tether's.</summary>
    private float W153WarnM => StoryLock.Tether(_leashWarnM, _leashPullM, _w153TetherOn, _w153TetherM).WarnM;
    private float W153PullM => StoryLock.Tether(_leashWarnM, _leashPullM, _w153TetherOn, _w153TetherM).PullM;

    private enum LevelSeed { Null, Seed, Keep }

    /// <param name="level">Seed = a new connection: the level this game is in now is where the next region change starts from.
    /// Keep = a load: its own level event came first (OnLocalLevel runs during the load, before GameplayStarted), so what it set stands.
    /// Null = a disconnect: the next level event only seeds it.</param>
    private void Wo153StoryReset(LevelSeed level = LevelSeed.Null)
    {
        lock (_w153Lock) _w153Lock.Reset();
        _w153TetherOn = false;
        if (level == LevelSeed.Seed) _w153PrevLevel = _localLevel; else if (level == LevelSeed.Null) _w153PrevLevel = null;
        _w153GateMaybeOn = true;   // the plugin may hold a gate from a session that ended without closing the pipe: one off command is owed
        _w153GateOffFails = 0;
        _w153LastHostDialogue = false;
        W153SetPeer("", "", "");
    }

    /// <summary>
    /// After a load the host is wherever its save put it, with no State changing yet: the section it is in is read from the quest its own
    /// checkpoint names (the same marker the story divergence uses), so the tether is back without waiting for the next step. No bring-along:
    /// the joiners rejoin a load on their own.
    /// </summary>
    private async Task Wo153SeedFromQuestAsync()
    {
        try
        {
            int gen = Volatile.Read(ref _w153Gen);
            await Task.Delay(6000);
            if (gen != Volatile.Read(ref _w153Gen) || !W153IsHost || !_w153StoryOn || _localQuest is not { } q || _localLevel is not { } lvl) return;
            var s = StorySections.ByQuest(q);
            if (s is null || !s.Locked || !string.Equals(s.Level, lvl, StringComparison.OrdinalIgnoreCase)) return;
            IReadOnlyList<StoryLock.Transition> ts;
            lock (_w153Lock) ts = _w153Lock.Note($"Barbora.{lvl}.{s.Quest}.loaded", W153NowMs());
            foreach (var t in ts.Where(x => x.Kind == StoryLock.Kind.Enter))
            {
                Interlocked.Increment(ref _w153Enters);
                _w153TetherOn = true;
                _w153SectionResendUtc = DateTime.UtcNow;
                Console.WriteLine($"MP-W153 story: after a load the host's own quest is {s.Code} '{s.Title}' ({s.Why}) -- the tether is back (nobody is pulled: the joiners rejoin a load)");
                _ = _sendStoryBeat?.Invoke(Protocol.StoryBeatKindSectionEnter, s.Code);
            }
        }
        catch (Exception ex) { Console.WriteLine($"MP-W153 story: reseed failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    private void Wo153SendGateOff()
    {
        if (!_w153GateMaybeOn) return;
        _ = Task.Run(async () =>
        {
            try { var r = await _combat.Wo151Async(9, [0, (byte)(GateWindowMs & 0xFF), (byte)(GateWindowMs >> 8)]); if (r is not null) { _w153GateSent = 0; _w153GateMaybeOn = false; } }
            catch { }
        });
    }

    /// <summary>mp_story_lock was switched off: a section the host is in is released now (the tether, the launcher line), not at the idle time.</summary>
    private void Wo153StoryOff()
    {
        StorySection? active;
        lock (_w153Lock) { active = _w153Lock.Active; _w153Lock.Reset(); }
        _w153TetherOn = false;
        if (active is not null)
        {
            Console.WriteLine($"MP-W153 story: mp_story_lock is off -- {active.Code} '{active.Title}' released, the leash is back to its own numbers");
            _ = _sendStoryBeat?.Invoke(Protocol.StoryBeatKindSectionLeave, $"{active.Code} switched_off");
            _ = ExecLuaAsync($"if KCD2MP_W153Story then KCD2MP_W153Story(\"host-leave\", \"{EscapeLua(active.Title)}\", \"switched off\") end");
        }
    }

    // ---------------------------------------------------------------- the host: where the story is

    /// <summary>Every quest State change of the host (Wo137OnLocalChangeAsync): which main quest it is in.</summary>
    private void Wo153NoteQuestChange(QuestChange c)
    {
        if (!_w153StoryOn || c.Old == c.New) return;
        IReadOnlyList<StoryLock.Transition> ts;
        lock (_w153Lock) ts = _w153Lock.Note(c.Path, W153NowMs());
        foreach (var t in ts) Wo153ApplyTransition(t);
    }

    /// <summary>About once a second on the host (the leash loop): a section the host has not touched for a long time is over.</summary>
    private DateTime _w153SectionResendUtc = DateTime.MinValue;

    private void Wo153StoryTick()
    {
        StoryLock.Transition? t;
        StorySection? active;
        lock (_w153Lock) { t = _w153Lock.Tick(W153NowMs()); active = _w153Lock.Active; }
        if (t is { } tr) Wo153ApplyTransition(tr);
        // A joiner who connects or reconnects while the host is in a locked section would never hear of it: the beat is repeated.
        if (active is not null && _w153StoryOn && (DateTime.UtcNow - _w153SectionResendUtc).TotalSeconds >= 30)
        {
            _w153SectionResendUtc = DateTime.UtcNow;
            _ = _sendStoryBeat?.Invoke(Protocol.StoryBeatKindSectionEnter, active.Code);
        }
    }

    private void Wo153ApplyTransition(StoryLock.Transition t)
    {
        if (t.Section is not { } s) return;
        if (t.Kind == StoryLock.Kind.Enter)
        {
            Interlocked.Increment(ref _w153Enters);
            _w153TetherOn = true;
            Console.WriteLine(FormattableString.Invariant(
                $"MP-W153 story: the host entered {s.Code} '{s.Title}' ({s.Why}) -- every joiner is brought beside it and held within {W153PullM:F0} m (warn {W153WarnM:F0} m) until it leaves"));
            _w153SectionResendUtc = DateTime.UtcNow;
            _ = _sendStoryBeat?.Invoke(Protocol.StoryBeatKindSectionEnter, s.Code);
            _ = ExecLuaAsync($"if KCD2MP_W153Story then KCD2MP_W153Story(\"host-enter\", \"{EscapeLua(s.Title)}\", \"{EscapeLua(s.Why)}\", {W153PullM:F0}) end");
            Wo153NoteStory($"the host entered a locked story section ({s.Code} {s.Title})");
        }
        else if (t.Kind == StoryLock.Kind.Leave)
        {
            Interlocked.Increment(ref _w153Leaves);
            bool still;
            lock (_w153Lock) still = _w153Lock.Active is not null;
            if (!still) _w153TetherOn = false;
            Console.WriteLine($"MP-W153 story: the host left {s.Code} '{s.Title}' ({t.Reason}) -- {(still ? "another locked section is open" : "the leash is back to its own numbers")}");
            _ = _sendStoryBeat?.Invoke(Protocol.StoryBeatKindSectionLeave, $"{s.Code} {t.Reason.Replace(' ', '_')}");
            _ = ExecLuaAsync($"if KCD2MP_W153Story then KCD2MP_W153Story(\"host-leave\", \"{EscapeLua(s.Title)}\", \"{EscapeLua(t.Reason)}\") end");
        }
    }

    // ---------------------------------------------------------------- the host: a region change

    /// <summary>The host's game loaded a level (OnLocalLevel). A different one than before, outside a save load, is a story transition.</summary>
    private void Wo153OnLocalLevel(string level)
    {
        string? prev = _w153PrevLevel;
        _w153PrevLevel = level;
        if (!W153IsHost || !_sharedWorld || !_w153StoryOn || prev is null || string.Equals(prev, level, StringComparison.Ordinal)) return;
        if (_hostLoadAnnounced) { Console.WriteLine($"MP-W153 story: the host's level is now {level} (a save load: the joiners rejoin, nobody is pulled)"); return; }
        Interlocked.Increment(ref _w153Levels);
        Console.WriteLine($"MP-W153 story: the host moved from level {prev} to {level} -- the joiners are told and brought along once the host's world is up");
        if (SceneFollowLogic.IsTrackableName(level)) _ = _sendStoryBeat?.Invoke(Protocol.StoryBeatKindLevel, level);
        _ = Wo153AfterLevelAsync(level);
    }

    /// <summary>
    /// A story reason to bring the joiners along (a locked section entered, a region changed). One transition can raise both (the move to
    /// Kuttenberg enters a section and loads a level), and each is asked. Honours mp_scene_follow like every bring-along.
    /// </summary>
    private void Wo153NoteStory(string why)
    {
        // No throttle: a section entered and a region loaded a moment apart (the move to Kuttenberg), or two sections back to back, each ask the
        // leash, which is idempotent while a pull is pending and never moves a joiner already within 50 m. A second ask costs nothing; a dropped one
        // would strand a joiner who strayed in between.
        Wo153NoteScene(why);
    }

    private async Task Wo153AfterLevelAsync(string level)
    {
        try
        {
            int gen = Volatile.Read(ref _w153Gen);
            for (int i = 0; i < 90; i++)   // the load itself: up to 90 s
            {
                await Task.Delay(1000);
                if (gen != Volatile.Read(ref _w153Gen) || !W153IsHost) return;
                if (_where == GameWhere.World && (HostHold() & HostPositionNotTheirs) == 0 && _hasPushed) break;
                if (i == 89) { Console.WriteLine("MP-W153 story: the host's world did not come up within 90 s after the level change -- nobody is pulled"); return; }
            }
            await Task.Delay(3000);   // the first positions of the new region
            if (gen != Volatile.Read(ref _w153Gen) || !W153IsHost) return;
            Wo153NoteStory($"the host's world changed region ({level})");
        }
        catch (Exception ex) { Console.WriteLine($"MP-W153 story: level follow failed: {ex.GetType().Name}: {ex.Message}"); }
    }

    // ---------------------------------------------------------------- the host: a conversation

    /// <summary>The mod's once-a-second dialogue read (wo114_busy d=): the host began or ended a conversation.</summary>
    private void Wo153NoteHostDialogue(bool dialogue)
    {
        if (W153IsHost) lock (_w153Lock) _w153Lock.Touch(W153NowMs());   // a conversation is the host still being in the section
        if (!W153IsHost || dialogue == _w153LastHostDialogue) return;
        _w153LastHostDialogue = dialogue;
        var now = DateTime.UtcNow;
        if (dialogue && (now - _w153DialogueSentUtc).TotalSeconds < 8) return;   // chatter is not a story
        if (dialogue) _w153DialogueSentUtc = now;
        Interlocked.Increment(ref _w153DialogueEdges);
        _ = _sendStoryBeat?.Invoke(Protocol.StoryBeatKindDialogue, dialogue ? "start" : "end");
    }

    // ---------------------------------------------------------------- the joiner: what the host's story told it

    /// <summary>A peer's story beat (kinds 7-10) arrived: only the HOST's, only to a joiner in the host's world, and only as words.</summary>
    private void Wo153OnPeerStory(byte source, byte kind, string text)
    {
        if (!_joinedWorld || W153IsHost || _w140Separate) return;
        byte hid = W153HostId();
        if (hid == 0xFF || source != hid) return;
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Length > 2 || !parts.All(IsPlainToken)) return;
        switch (kind)
        {
            case Protocol.StoryBeatKindSectionEnter:
            {
                var s = StorySections.ByCode(parts[0]);
                if (s is null || !s.Locked) return;
                bool changed = W153GetPeer().Code != s.Code;   // the host re-sends it every 30 s for a late joiner: told once
                W153SetPeer(s.Code, s.Title, s.Why);
                if (!changed) return;
                Console.WriteLine($"MP-W153 joiner: the host entered {s.Code} '{s.Title}' ({s.Why})");
                _ = ExecLuaAsync($"if KCD2MP_W153Story then KCD2MP_W153Story(\"enter\", \"{EscapeLua(s.Title)}\", \"{EscapeLua(s.Why)}\") end");
                break;
            }
            case Protocol.StoryBeatKindSectionLeave:
            {
                var s = StorySections.ByCode(parts[0]);
                if (s is null) return;
                var held = W153GetPeer().Code;
                if (held != s.Code) return;   // a joiner not holding that section (a late join, the 90 s expiry, a duplicate beat) is told nothing, and a beat for another one leaves the held one standing
                W153SetPeer("", "", "");
                Console.WriteLine($"MP-W153 joiner: the host left {s.Code} '{s.Title}' ({(parts.Length > 1 ? parts[1] : "-")})");
                _ = ExecLuaAsync($"if KCD2MP_W153Story then KCD2MP_W153Story(\"leave\", \"{EscapeLua(s.Title)}\", \"\") end");
                break;
            }
            case Protocol.StoryBeatKindLevel:
                Console.WriteLine($"MP-W153 joiner: the host's game loaded the region '{parts[0]}'");
                _ = ExecLuaAsync("if KCD2MP_W153Story then KCD2MP_W153Story(\"level\", \"\", \"\") end");
                break;
            case Protocol.StoryBeatKindDialogue:
                if (parts[0] == "start") _ = ExecLuaAsync("if KCD2MP_W153Story then KCD2MP_W153Story(\"dialogue\", \"\", \"\") end");
                break;
        }
    }

    // ---------------------------------------------------------------- the joiner: keep playing (the native gate)

    /// <summary>
    /// About once a second (the leash loop), and at once after a change: the native cutscene gate follows this player's
    /// choice. Armed only for a joiner in the host's world who chose mp_scene_mode play; off for everyone else, always.
    /// </summary>
    private int _w153GateBusy;
    private volatile bool _w153GateDirty;   // the choice changed: say so at the next tick
    private const int GateWindowMs = 1500;

    private void W153SetPeer(string code, string title, string why)
    {
        lock (_w153PeerLock) { _w153PeerSectionCode = code; _w153PeerSection = title; _w153PeerSectionWhy = why; _w153PeerSectionAtUtc = code.Length > 0 ? DateTime.UtcNow : DateTime.MinValue; }
    }

    private (string Code, string Title, string Why, DateTime At) W153GetPeer()
    {
        lock (_w153PeerLock) return (_w153PeerSectionCode, _w153PeerSection, _w153PeerSectionWhy, _w153PeerSectionAtUtc);
    }

    private void Wo153GateTick()
    {
        var pr = W153GetPeer();
        if (pr.Code.Length > 0 && (DateTime.UtcNow - pr.At).TotalSeconds > 90)
        {
            Console.WriteLine($"MP-W153 joiner: no word of '{pr.Title}' from the host for 90 s -- taken as over");
            string title = pr.Title;
            W153SetPeer("", "", "");
            _ = ExecLuaAsync($"if KCD2MP_W153Story then KCD2MP_W153Story(\"leave\", \"{EscapeLua(title)}\", \"\") end");
        }
        byte want = (_combatRoleApplied && !_isDamageAuthority && _joinedWorld && !_w140Separate && _w153SceneMode == 1) ? (byte)1 : (byte)0;
        if (want == 1) _w153GateMaybeOn = true;
        // Nothing to turn off that may not be on: no pipe traffic (the plugin starts, and returns after a closed pipe, with the gate off).
        if (want == 0 && !_w153GateMaybeOn) return;
        if (want == _w153GateSent && !_w153GateDirty && (DateTime.UtcNow - _w153GateSentUtc).TotalSeconds < 30) return;
        if ((DateTime.UtcNow - _w153GateSentUtc).TotalSeconds < 5) return;   // a failed ask waits 5 s: no plugin, a hung pipe
        if (Interlocked.Exchange(ref _w153GateBusy, 1) == 1) return;
        _w153GateSentUtc = DateTime.UtcNow;
        _ = Task.Run(async () =>   // its own task: a slow pipe never delays the leash tick that runs the pulls and the tether
        {
            try
            {
                var r = await _combat.Wo151Async(9, [want, (byte)(GateWindowMs & 0xFF), (byte)(GateWindowMs >> 8)]);
                if (r is null)   // no plugin yet: asked again in 5 s; an owed OFF is given up after 3 tries (there is no plugin to hold a gate)
                {
                    if (want == 0 && ++_w153GateOffFails >= 3) _w153GateMaybeOn = false;
                    return;
                }
                _w153GateOffFails = 0;
                _w153GateDirty = false;
                if (want != _w153GateSent)
                    Console.WriteLine($"MP-W153 gate: {(want == 1 ? "ON" : "off")} -- {(want == 1 ? "this game's own copy of a scene the host's step starts is not played" : "this game plays its own copies as before")}; plugin {(r.Value.Ok ? "armed" : "has no gate (not armed)")}");
                _w153GateSent = want;
                if (want == 0) _w153GateMaybeOn = false;   // confirmed off
            }
            catch (Exception ex) { Console.WriteLine($"MP-W153 gate: {ex.GetType().Name}: {ex.Message}"); }
            finally { Volatile.Write(ref _w153GateBusy, 0); }
        });
    }

    // ---------------------------------------------------------------- the launcher: GET /coop-status

    private string? _w153GameBuild;
    private readonly object _w153PeerLock = new();
    private string _w153PeerSection = "", _w153PeerSectionWhy = "", _w153PeerSectionCode = "";   // the HOST's locked section, as a joiner heard it (written and read together under _w153PeerLock)
    private DateTime _w153PeerSectionAtUtc = DateTime.MinValue;   // the host repeats it every 30 s; 90 s of silence = it is over (a lost Leave beat)

    /// <summary>The game's own build (wh_sys_GameReleaseVersion), read once the game's API answers; the launcher warns when it is not one the mod was verified on.</summary>
    private int _w153BuildReader;
    private int _w153BuildFails;

    private async Task Wo153ReadGameBuildAsync(string apiBase)
    {
        if (Interlocked.Exchange(ref _w153BuildReader, 1) == 1) return;   // one reader
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        try
        {
        for (int i = 0; ; i++)
        {
            try
            {
                string raw = await http.GetStringAsync($"{apiBase}/api/System/Console/GetCvarValue?name=wh_sys_GameReleaseVersion");
                if (CoopStatus.ParseBuild(raw) is { } b)
                {
                    _w153BuildFails = 0;
                    if (b != _w153GameBuild)   // first read, or a new game process on another build
                    {
                        _w153GameBuild = b;
                        string? warn = CoopStatus.BuildWarning(b);
                        Console.WriteLine($"MP-W153 game build {b} ({CoopStatus.Pretty(b)}){(warn is null ? " -- a build the mod was verified on" : " -- WARNING: " + warn)}");
                    }
                    await Task.Delay(60000);   // read again each minute: a game restarted on another build changes the warning
                    continue;
                }
            }
            catch { if (++_w153BuildFails >= 3 && _w153GameBuild is not null) { Console.WriteLine("MP-W153 game build: the game stopped answering -- the build is forgotten until it does"); _w153GameBuild = null; } }
            await Task.Delay(i < 24 ? 5000 : 30000);   // the game may start long after the agent: no cap, slower after two minutes (and an answered read waits a minute)
        }
        }
        finally { Volatile.Write(ref _w153BuildReader, 0); }
    }

    private string Wo153CoopStatusJson()
    {
        bool host = W153IsHost;
        bool inSession = _combatRoleApplied;
        int peers = Wo134Peers().Count();
        string sync = CoopStatus.SyncState(inSession, peers > 0, host, _w151CaughtUp, _storyDivergenceTold.Count);
        StorySection? active;
        lock (_w153Lock) active = _w153Lock.Active;
        var peer = W153GetPeer();
        string section = host ? (active?.Title ?? "") : peer.Title;
        string why = host ? (active?.Why ?? "") : peer.Why;
        string role = !inSession ? "none" : host ? "host" : "joiner";
        return CoopStatus.Json(role, sync, section, why, host ? _w153TetherOn : section.Length > 0, _w153GameBuild, CoopStatus.BuildWarning(_w153GameBuild));
    }

    private string W153ActiveCode() { lock (_w153Lock) return _w153Lock.Active?.Code ?? "-"; }

    private string Wo153StoryStatsLine() =>
        $"MP-W153 story lock={(_w153StoryOn ? "on" : "off")} tether_m={_w153TetherM:F0} tether_on={(_w153TetherOn ? 1 : 0)} active={W153ActiveCode()} enters={_w153Enters} leaves={_w153Leaves} levels={_w153Levels} dialogue_edges={_w153DialogueEdges} scene_mode={(_w153SceneMode == 1 ? "play" : "watch")} gate_sent={_w153GateSent}";
}
