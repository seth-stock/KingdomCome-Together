// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
namespace KcdMp.Client;

/// <summary>
/// WO-155 (docs/WO-155-findings.md): when the host is on rails, the FRIEND chooses -- join the host, or stay in the open world.
///
///   * The host is "on rails" in a locked story period (StorySections.Periods: a wedding, a castle, the move to Kuttenberg,
///     the final set, ...). When one begins, every friend is asked (a question on their screen: F11 join / F12 stay, 30 s;
///     nobody answering is a yes), or answers by their standing setting (mp_story_join ask|join|free).
///   * JOIN is what 0.44.0 always did: brought beside the host, kept within the tether, the host's quest steps applied.
///   * STAY (free) leaves the friend alone for as long as the period lasts: the host does not leash, tether or bring them
///     (RailsRoster, asked at the leash tick, the fast-travel come-along and every scene bring-along), their own game refuses a
///     pull, and the host's quest steps are HELD (the mirror is paused like during a load), so the engine cannot start the
///     host's scenes or locks on them by itself. The friend's own quest steps are not shared meanwhile.
///   * When the period ends the mirror comes back the way it does after any pause (a checkpoint compare, like a late join),
///     the ordinary leash applies again, and they are told. F11 (or mp_story_come) joins at any time.
///   * The answer is sent to the host as a StoryBeat (kind 11) and repeated every 20 s, so a lost message or a host that
///     reloaded heals. A friend that has not answered 50 s after the period began is taken as joined.
///   * A friend who says F12 ("keep playing") to the host's CUTSCENE is not brought along after it (kind 12).
///
/// The rules are RailsChoice.cs (pure, Wo155Tests). Rollback: mp_story_join join (the 0.44.0 behaviour for that friend).
/// </summary>
public partial class GameBridge
{
    private readonly object _w155Lock = new();
    private readonly RailsRoster _w155Roster = new();          // host
    private readonly SceneStayBook _w155SceneStay = new();     // host
    private readonly RailsJoiner _w155Me = new();              // friend
    private volatile bool _w155Free;                           // friend: staying in the open world (read by the mirror hold, the pull refusal, the leash flags)
    private volatile int _w155Pref = (int)RailsPref.Ask;       // friend: mp_story_join
    private DateTime _w155JoinedUtc = DateTime.MinValue;       // friend: the pull right after a join says "you joined your host" (once, within a minute)
    private int _w155JoinedPull;
    private DateTime _w155FreeSinceUtc = DateTime.MinValue;
    private int _w155Asks, _w155Joins, _w155Frees, _w155Backstops, _w155HeldChanges, _w155Overs;
    private int _w155HostJoins, _w155HostFrees, _w155HostTimeouts, _w155HostSceneStays, _w155HostSkips;

    private RailsPref W155Pref => (RailsPref)_w155Pref;

    // ---------------------------------------------------------------- host: who is exempt

    private byte[] W155Joiners() =>
        _leashJoinerState.Where(kv => (kv.Value.S.Flags & Protocol.LeashFlagSeparate) == 0).Select(kv => kv.Key).ToArray();

    /// <summary>HOST: nothing may move this friend now (no leash, no bring-along): it stays free, or has not answered yet.</summary>
    private bool Wo155Exempt(byte joiner)
    {
        lock (_w155Lock) return _w155Roster.Exempt(joiner);
    }

    /// <summary>
    /// HOST: keep the roster in step with the section the host is in (called on every entry and exit, a reset, mp_story_lock off).
    /// A new period asks every friend again; the next section of the same period keeps their answers; leaving releases everyone.
    /// </summary>
    private void Wo155HostSync(bool immediate = false)
    {
        if (!W153IsHost) return;
        StorySection? a;
        lock (_w153Lock) a = _w153Lock.Active;
        var p = a is null || !_w153StoryOn ? null : StorySections.PeriodOf(a.Code);
        bool isNew = false, ended = false, ending = false;
        byte[] ids = Array.Empty<byte>();
        lock (_w155Lock)
        {
            if (p is null)
            {
                // The friends keep a period for a few seconds after its last section (the next quest of it usually follows at once); so does the host.
                if (immediate) { ended = _w155Roster.Period is not null; _w155Roster.End(); }
                else if (_w155Roster.Period is not null && !_w155Roster.Ending) { _w155Roster.EndSoon(W153NowMs()); ending = true; }
            }
            else if (_w155Roster.Start(p.Id)) { isNew = true; ids = W155Joiners(); _w155Roster.SeedPending(ids, W153NowMs()); }
        }
        if (isNew) Console.WriteLine($"MP-W155 host: the story period {p!.Id} '{p.Why}' began -- {ids.Length} friend(s) are asked: join you (kept within the tether) or stay in the open world; nobody is moved until they answer ({RailsRules.HostGraceMs / 1000} s at most)");
        else if (ended) Console.WriteLine("MP-W155 host: the story period is over -- every friend is released; the ordinary leash applies again");
        else if (ending) Console.WriteLine($"MP-W155 host: the story period's last section ended -- the friends' answers stand for {RailsRules.PeriodEndGraceMs / 1000} s more, in case the next quest of it follows");
    }

    /// <summary>A friend is gone: nothing of theirs is kept.</summary>
    private void Wo155Forget(byte id)
    {
        lock (_w155Lock) { _w155Roster.Forget(id); _w155SceneStay.Forget(id); }
    }

    /// <summary>HOST, once a second: a friend that has not answered in time is taken as joined, and brought along.</summary>
    private void Wo155HostTick()
    {
        List<byte> expired;
        bool had, has;
        lock (_w155Lock) { had = _w155Roster.Period is not null; expired = _w155Roster.Tick(W153NowMs(), W155Joiners()); has = _w155Roster.Period is not null; }
        if (had && !has) Console.WriteLine("MP-W155 host: the story period is over -- every friend is released; the ordinary leash applies again");
        foreach (byte id in expired)
        {
            Interlocked.Increment(ref _w155HostTimeouts);
            Console.WriteLine($"MP-W155 host: joiner {id} did not answer in {RailsRules.HostGraceMs / 1000} s -- taken as joined");
            Wo155Bring(id, "taken as joined");
        }
    }

    /// <summary>HOST: bring one friend beside the host through the leash's own pull (the 50 m, busy and dismount rules all apply).</summary>
    private void Wo155Bring(byte id, string why)
    {
        if (!_w153Follow) { Console.WriteLine($"MP-W155 host: joiner {id} {why} -- mp_scene_follow is off: not brought along"); return; }
        if (!_leashEnabled) { Console.WriteLine($"MP-W155 host: joiner {id} {why} -- mp_leash is off: not brought along"); return; }
        var l = _leashByJoiner.GetOrAdd(id, _ => new LeashLogic());
        lock (l) l.NoteHostFastTravel();
        Console.WriteLine($"MP-W155 host: joiner {id} {why} -- brought beside the host (the leash decides: nobody within 50 m, nobody busy)");
    }

    /// <summary>HOST: stop everything the leash has running for this friend; a countdown on their screen is cancelled.</summary>
    private void Wo155Release(byte id)
    {
        if (!_leashByJoiner.TryGetValue(id, out var l)) return;
        bool had;
        lock (l) had = l.Suspend();
        if (had)
        {
            Console.WriteLine($"MP-W155 host: joiner {id} is not leashed -- its running countdown is cancelled");
            _ = Wo114SendAsync(id, new LeashCommand(Protocol.LeashKindCancel, 0, 0, _lastX, _lastY, _lastZ, 0));
        }
    }

    /// <summary>A friend's answer (kind 11) or its "keep playing" to a scene (kind 12). Only the host reads them.</summary>
    private void Wo155OnFriendBeat(byte source, byte kind, string text)
    {
        if (!W153IsHost || !IsLivePeer(source)) return;
        if (kind == Protocol.StoryBeatKindSceneStay)
        {
            if (text.Trim() != "stay") return;
            lock (_w155Lock) _w155SceneStay.Note(source, W153NowMs());
            Interlocked.Increment(ref _w155HostSceneStays);
            Console.WriteLine($"MP-W155 host: joiner {source} keeps playing through the host's scene -- not brought along after it");
            return;
        }
        if (kind != Protocol.StoryBeatKindChoice) return;
        if (!RailsRules.TryParseChoice(text, out var period, out var choice))
        {
            Console.WriteLine($"MP-W155 host: joiner {source} sent an answer that is not one ('{(text.Length > 24 ? text[..24] : text)}'); dropped");
            return;
        }
        RailsRoster.Outcome o;
        lock (_w155Lock) o = _w155Roster.Choose(source, period.Id, choice, W153NowMs());
        string who = LeashPartnerName(source);
        switch (o)
        {
            case RailsRoster.Outcome.Joined:
                Interlocked.Increment(ref _w155HostJoins);
                Console.WriteLine($"MP-W155 host: joiner {source} ({who}) JOINS you for {period.Why}");
                _ = ExecLuaAsync($"if KCD2MP_W155Host then KCD2MP_W155Host(\"join\", \"{EscapeLua(who)}\", \"{EscapeLua(period.Why)}\") end");
                Wo155Bring(source, "chose to join");
                break;
            case RailsRoster.Outcome.Freed:
                Interlocked.Increment(ref _w155HostFrees);
                Console.WriteLine($"MP-W155 host: joiner {source} ({who}) STAYS in the open world for {period.Why} -- not leashed, tethered or brought along until it ends");
                _ = ExecLuaAsync($"if KCD2MP_W155Host then KCD2MP_W155Host(\"free\", \"{EscapeLua(who)}\", \"{EscapeLua(period.Why)}\") end");
                Wo155Release(source);
                break;
            case RailsRoster.Outcome.Ignored:
                Console.WriteLine($"MP-W155 host: joiner {source}'s answer for {period.Id} does not match the host's period now -- ignored");
                break;
            // NoPeriod: a late repeat after the period ended; Unchanged: the 20 s repeat. Neither is news.
        }
    }

    // ---------------------------------------------------------------- friend: the question, the answer, the end

    /// <summary>The host's "entered section" beat reached this friend (the host repeats it every 30 s). True = a new period began (asked, or answered by the standing setting).</summary>
    private bool Wo155OnSectionBeat(StorySection s)
    {
        RailsJoiner.Step st;
        lock (_w155Lock) st = _w155Me.OnEnter(s.Code, W153NowMs(), W155Pref);
        switch (st.Act)
        {
            case RailsJoiner.Act.Ask:
                Interlocked.Increment(ref _w155Asks);
                Console.WriteLine($"MP-W155 friend: the host entered {st.Period!.Id} '{st.Period.Why}' -- asked: join the host, or stay in the open world ({RailsRules.PromptSeconds} s, then join)");
                _ = ExecLuaAsync($"if KCD2MP_W155Ask then KCD2MP_W155Ask(\"{EscapeLua(st.Period.Why)}\", {RailsRules.PromptSeconds}) end");
                break;
            case RailsJoiner.Act.Auto:
                Console.WriteLine($"MP-W155 friend: the host entered {st.Period!.Id} '{st.Period.Why}' -- {RailsRules.ChoiceName(st.Choice)} ({st.Why}; mp_story_join {RailsRules.PrefName(W155Pref)})");
                Wo155Decide(st.Choice, st.Why, announce: true);
                break;
        }
        return st.Act is RailsJoiner.Act.Ask or RailsJoiner.Act.Auto;
    }

    /// <summary>
    /// This friend's answer, from the question (F11/F12/the time running out), a command or a standing setting.
    /// Join from free resumes the quest mirror; free holds it.
    /// </summary>
    private void Wo155Decide(RailsChoice choice, string how, bool announce)
    {
        StoryPeriod? p;
        bool ok, wasFree = _w155Free;
        lock (_w155Lock)
        {
            ok = _w155Me.Decide(choice, W153NowMs());
            p = ok ? StorySections.Periods.FirstOrDefault(x => x.Id == _w155Me.Period) : null;
            if (ok) _w155Free = _w155Me.IsFree;
        }
        if (!ok || p is null) return;
        _ = _sendStoryBeat?.Invoke(Protocol.StoryBeatKindChoice, RailsRules.ChoiceText(p.Id, choice));
        string why = EscapeLua(p.Why);
        if (choice == RailsChoice.Free)
        {
            Interlocked.Increment(ref _w155Frees);
            if (!wasFree) _w155FreeSinceUtc = DateTime.UtcNow;
            Console.WriteLine($"MP-W155 friend: STAYS in the open world for {p.Why} ({how}) -- the host does not bring or tether this player; the host's quest steps are held until it ends; F11 / mp_story_come joins");
            _ = ExecLuaAsync($"if KCD2MP_W155Decided then KCD2MP_W155Decided(\"free\", \"{why}\", {(announce ? "true" : "false")}) end");
        }
        else
        {
            Interlocked.Increment(ref _w155Joins);
            _w155JoinedUtc = DateTime.UtcNow;
            Volatile.Write(ref _w155JoinedPull, 1);
            Console.WriteLine($"MP-W155 friend: JOINS the host for {p.Why} ({how}){(wasFree ? " -- the quest mirror comes back" : "")}");
            if (wasFree) Wo155ResumeMirror("this player joined");
            _ = ExecLuaAsync($"if KCD2MP_W155Decided then KCD2MP_W155Decided(\"join\", \"{why}\", {(announce ? "true" : "false")}) end");
        }
    }

    /// <summary>
    /// The mirror comes back after a free stretch: what the host changed meanwhile was not kept (it would replay a whole story part's
    /// scenes at the wrong place and time), so the host's own checkpoint is compared, as for a late joiner (the mirror's "runs again" path).
    /// </summary>
    private void Wo155ResumeMirror(string why)
    {
        int n = _w137Queue.Reset();
        Console.WriteLine($"MP-W155 the quest mirror is back ({why}): {_w155HeldChanges} host change(s) were held{(n > 0 ? $", {n} queued ones dropped" : "")} -- the host's checkpoint is compared next (a late join's way)");
        Interlocked.Exchange(ref _w155HeldChanges, 0);
    }

    /// <summary>FRIEND, once a second: repeat the answer, give up an unanswered question, end the period.</summary>
    private void Wo155Tick()
    {
        RailsJoiner.Step st;
        lock (_w155Lock) st = _w155Me.Tick(W153NowMs());
        switch (st.Act)
        {
            case RailsJoiner.Act.Resend:
                if (st.Period is not null) _ = _sendStoryBeat?.Invoke(Protocol.StoryBeatKindChoice, RailsRules.ChoiceText(st.Period.Id, st.Choice));
                break;
            case RailsJoiner.Act.Backstop:
                Interlocked.Increment(ref _w155Backstops);
                Console.WriteLine("MP-W155 friend: the question was never answered or never shown -- taken as join");
                _ = ExecLuaAsync("if KCD2MP_W155Cancel then KCD2MP_W155Cancel() end");
                Wo155Decide(RailsChoice.Join, "no answer", announce: true);
                break;
            case RailsJoiner.Act.Over:
                Wo155Over(st.Period, st.Choice);
                break;
        }
    }

    /// <summary>The host's period is over for this friend (its last section ended, the host went quiet, a reset).</summary>
    private void Wo155Over(StoryPeriod? p, RailsChoice was)
    {
        bool wasFree = _w155Free;
        _w155Free = false;
        Interlocked.Increment(ref _w155Overs);
        string why = p?.Why ?? "the story part";
        Console.WriteLine($"MP-W155 friend: {why} is over ({(was == RailsChoice.Free ? "was staying in the open world" : was == RailsChoice.Join ? "had joined" : "never answered")}) -- the ordinary leash applies again");
        if (wasFree) Wo155ResumeMirror("the host's story part ended");
        _ = ExecLuaAsync($"if KCD2MP_W155Over then KCD2MP_W155Over(\"{EscapeLua(why)}\", \"{RailsRules.ChoiceName(was)}\") end");
    }

    /// <summary>The host went quiet for 90 s or left: no grace, the period is over now.</summary>
    private void Wo155ForceOver()
    {
        StoryPeriod? p;
        RailsChoice was;
        lock (_w155Lock) { was = _w155Me.Choice; p = _w155Me.ForceOver(); }
        if (p is not null) Wo155Over(p, was);
    }

    /// <summary>A section of the host's period ended (kind 8): the period is over only if no section of it follows within seconds.</summary>
    private void Wo155OnLeaveBeat(string code)
    {
        lock (_w155Lock) _w155Me.OnLeave(code, W153NowMs());
    }

    /// <summary>A session started or ended, a load: nothing of the question or the stay survives it.</summary>
    private void Wo155Reset()
    {
        lock (_w155Lock) { _w155Me.Reset(); _w155Roster.End(); _w155SceneStay.Clear(); }
        bool wasFree = _w155Free;
        _w155Free = false;
        if (wasFree) Wo155ResumeMirror("reset");
        _ = ExecLuaAsync("if KCD2MP_W155Reset then KCD2MP_W155Reset() end");
    }

    // ---------------------------------------------------------------- mod events

    /// <summary><c>w155_choice join|free [how]</c> (F11 / F12 / mp_story_come / mp_story_stay / the question timing out), <c>w155_scene_stay</c>.</summary>
    private void Wo155OnEvent(string name, string? arg)
    {
        switch (name)
        {
            case "w155_choice":
            {
                var parts = (arg ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0 || RailsRules.ParseChoiceWord(parts[0]) is not { } c) return;
                string how = parts.Length > 1 ? parts[1] : "key";
                if (W153IsHost || !_joinedWorld || _w140Separate)
                {
                    Console.WriteLine("MP-W155 friend: an answer with no host's world to answer for -- ignored");
                    _ = ExecLuaAsync("if KCD2MP_W155Decided then KCD2MP_W155Decided(\"none\", \"\", true) end");
                    return;
                }
                bool open, asking;
                lock (_w155Lock) { open = _w155Me.Open; asking = _w155Me.Asking; }
                if (!open)
                {
                    Console.WriteLine($"MP-W155 friend: '{parts[0]}' but the host is not in a locked story part -- nothing to answer");
                    _ = ExecLuaAsync("if KCD2MP_W155Decided then KCD2MP_W155Decided(\"none\", \"\", true) end");
                    return;
                }
                if (how == "timeout" && !asking) return;   // the question had been answered meanwhile
                Wo155Decide(c, how, announce: how != "timeout");
                return;
            }
            case "w155_scene_stay":
                if (W153IsHost || !_joinedWorld || _w140Separate || W153HostId() == 0xFF) return;
                Console.WriteLine("MP-W155 friend: keeps playing through the host's scene -- the host is told not to bring this player along after it");
                _ = _sendStoryBeat?.Invoke(Protocol.StoryBeatKindSceneStay, "stay");
                return;
        }
    }

    private string Wo155StatsLine() =>
        $"MP-W155 stats pref={RailsRules.PrefName(W155Pref)} free={(_w155Free ? 1 : 0)} asks={_w155Asks} joins={_w155Joins} frees={_w155Frees} backstops={_w155Backstops} overs={_w155Overs} held_changes={_w155HeldChanges} | host: joined={_w155HostJoins} free={_w155HostFrees} timeouts={_w155HostTimeouts} scene_stays={_w155HostSceneStays} skipped_moves={_w155HostSkips}";

    /// <summary>The words the launcher shows a friend: where this player stands in the host's story period ("" = no period).</summary>
    /// <summary>The pull that follows this player's own "join" says so, once (a later, unrelated fast travel keeps its own words).</summary>
    private bool W155TakeJoinedWording() =>
        (DateTime.UtcNow - _w155JoinedUtc).TotalSeconds < 60 && Interlocked.Exchange(ref _w155JoinedPull, 0) == 1;

    private string W155ChoiceName()
    {
        lock (_w155Lock) return !_w155Me.Open ? "" : _w155Me.Asking ? "asking" : RailsRules.ChoiceName(_w155Me.Choice);
    }

    /// <summary>Host: how many friends joined / stay / have not answered, for the launcher.</summary>
    private (int Joined, int Free, int Asking) W155Counts()
    {
        lock (_w155Lock) return (_w155Roster.Count(RailsChoice.Join), _w155Roster.Count(RailsChoice.Free), _w155Roster.Count(RailsChoice.Pending));
    }
}
