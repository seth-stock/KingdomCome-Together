// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Collections.Concurrent;
using System.Reflection;
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-155: the WIRING, not just the rules (Wo155Tests): a real GameBridge, its game transport a recording stand-in, driven through the
/// same private methods the relay's frames and the mod's events reach. It proves who is exempt from every way the host moves a friend,
/// that a friend's answer reaches the host and brings them, that staying holds the quest mirror and refuses a pull, and that the end
/// gives it all back. What it cannot prove -- the engine's reaction -- is docs/WO-155-findings.md section 6.
/// </summary>
public class Wo155WiringTests
{
    // ---- a recording stand-in for the game: every Task returns done, every Lua call is kept ----
    public class Recorder : DispatchProxy
    {
        public ConcurrentQueue<string> Lua { get; } = new();

        protected override object? Invoke(MethodInfo? m, object?[]? args)
        {
            if (m is null) return null;
            if (m.Name == "ExecuteAsync" && args is { Length: > 0 } && args[0] is string s) Lua.Enqueue(s);
            var rt = m.ReturnType;
            if (rt == typeof(Task)) return Task.CompletedTask;
            if (rt == typeof(ValueTask)) return default(ValueTask);
            if (rt.IsGenericType && rt.GetGenericTypeDefinition() == typeof(Task<>))
            {
                var t = rt.GetGenericArguments()[0];
                object? def = t.IsValueType ? Activator.CreateInstance(t) : null;
                if (t == typeof(bool)) def = true;
                return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(t).Invoke(null, new[] { def });
            }
            return rt.IsValueType ? Activator.CreateInstance(rt) : null;
        }
    }

    private const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private sealed class Rig
    {
        public readonly GameBridge B = new(new ClientConfig());
        public readonly Recorder Game;
        public readonly List<(byte Kind, string Text)> Beats = new();

        public Rig()
        {
            Game = (Recorder)(object)DispatchProxy.Create<IGameTransport, Recorder>();
            Set("_transport", Game);
            Set("_sendStoryBeat", (Func<byte, string, Task>)((k, t) => { lock (Beats) Beats.Add((k, t)); return Task.CompletedTask; }));
        }

        public T Get<T>(string field) => (T)typeof(GameBridge).GetField(field, Any)!.GetValue(B)!;
        public T Prop<T>(string prop) => (T)typeof(GameBridge).GetProperty(prop, Any)!.GetValue(B)!;
        public void Set(string field, object? v) => typeof(GameBridge).GetField(field, Any)!.SetValue(B, v);
        public object? Call(string method, params object?[] args) => typeof(GameBridge).GetMethod(method, Any)!.Invoke(B, args);
        public T Call<T>(string method, params object?[] args) => (T)Call(method, args)!;

        public string LuaAll() => string.Join("\n", Game.Lua);
        public bool LuaHas(string needle) => Game.Lua.Any(l => l.Contains(needle, StringComparison.Ordinal));

        public void Host(params byte[] friends)
        {
            Set("_combatRoleApplied", true); Set("_isDamageAuthority", true); Set("_sharedWorld", true);
            Set("_leashEnabled", true); Set("_w153Follow", true);
            var peers = Get<PeerSet>("_peers");
            foreach (byte f in friends)
            {
                peers.Connected(f, "Friend" + f);
                Get<ConcurrentDictionary<byte, string>>("_ghostNames")[f] = "Friend" + f;
                var states = Get<ConcurrentDictionary<byte, (LeashState S, DateTime AtUtc)>>("_leashJoinerState");
                states[f] = (new LeashState(Protocol.LeashFlagInWorld, 0, 0, 0, 0, 0), DateTime.UtcNow);
            }
        }

        public void Friend()
        {
            Set("_combatRoleApplied", true); Set("_isDamageAuthority", false); Set("_joinedWorld", true);
            Set("_hostAuthority", true); Set("_hostModeKnown", true); Set("_hostSharedWorld", true);
            Set("_leashHostId", (byte)0);
        }

        public void HostEnters(string questPath)
        {
            var lockObj = Get<StoryLock>("_w153Lock");
            IReadOnlyList<StoryLock.Transition> ts;
            lock (lockObj) ts = lockObj.Note(questPath, Environment.TickCount64);
            foreach (var t in ts) Call("Wo153ApplyTransition", t);
        }

        public bool Pending(byte friend) =>
            Get<ConcurrentDictionary<byte, LeashLogic>>("_leashByJoiner").TryGetValue(friend, out var l) && l.FastTravelPending;

        public void ClearPending(byte friend)
        {
            if (Get<ConcurrentDictionary<byte, LeashLogic>>("_leashByJoiner").TryGetValue(friend, out var l)) lock (l) l.Suspend();
        }

        public bool Exempt(byte friend) => Call<bool>("Wo155Exempt", friend);
    }

    private const string FinalSetPath = "Barbora.kutnohorsko.erik.start";
    private const string FinalSetNext = "Barbora.kutnohorsko.oblehaniSuchdole.start";
    private const string FinalSetEnd = "Barbora.kutnohorsko.finale.endQuest";

    // ================================================================ the host

    [Fact]
    public void A_new_story_period_exempts_every_friend_and_moves_nobody_until_they_answer()
    {
        var r = new Rig();
        r.Host(1, 2);
        r.HostEnters(FinalSetPath);
        Assert.True(r.Exempt(1) && r.Exempt(2));
        Assert.False(r.Pending(1));
        Assert.False(r.Pending(2));
        Assert.True(r.LuaHas("KCD2MP_W153Story(\"host-enter\""));
        Assert.Contains(r.Beats, b => b.Kind == Protocol.StoryBeatKindSectionEnter && b.Text == "M47");
    }

    [Fact]
    public void A_friend_who_joins_is_brought_beside_the_host_and_one_who_stays_is_not()
    {
        var r = new Rig();
        r.Host(1, 2);
        r.HostEnters(FinalSetPath);
        r.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindChoice, "M47 join");
        r.Call("Wo155OnFriendBeat", (byte)2, Protocol.StoryBeatKindChoice, "M47 free");
        Assert.False(r.Exempt(1));
        Assert.True(r.Pending(1));            // the leash will place them (50 m and busy rules apply there)
        Assert.True(r.Exempt(2));
        Assert.False(r.Pending(2));
        Assert.True(r.LuaHas("KCD2MP_W155Host(\"join\", \"Friend1\""));
        Assert.True(r.LuaHas("KCD2MP_W155Host(\"free\", \"Friend2\""));
    }

    [Fact]
    public void A_friend_who_stays_does_not_come_along_when_the_host_fast_travels_but_one_who_joined_does()
    {
        var r = new Rig();
        r.Host(1, 2);
        r.HostEnters(FinalSetPath);
        r.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindChoice, "M47 join");
        r.Call("Wo155OnFriendBeat", (byte)2, Protocol.StoryBeatKindChoice, "M47 free");
        r.ClearPending(1);
        r.Call("Wo114NoteHostFastTravel", "test");
        Assert.True(r.Pending(1));
        Assert.False(r.Pending(2));
    }

    [Fact]
    public void A_friend_who_has_not_answered_does_not_come_along_either()
    {
        var r = new Rig();
        r.Host(1);
        r.HostEnters(FinalSetPath);
        r.Call("Wo114NoteHostFastTravel", "test");
        Assert.False(r.Pending(1));
    }

    [Fact]
    public void Outside_a_story_period_everybody_comes_along_as_before()
    {
        var r = new Rig();
        r.Host(1, 2);
        r.Call("Wo114NoteHostFastTravel", "test");
        Assert.True(r.Pending(1) && r.Pending(2));
    }

    [Fact]
    public void A_scene_brings_along_those_who_joined_and_skips_those_who_stay_or_said_keep_playing()
    {
        var r = new Rig();
        r.Host(1, 2, 3);
        r.HostEnters(FinalSetPath);
        r.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindChoice, "M47 join");
        r.Call("Wo155OnFriendBeat", (byte)2, Protocol.StoryBeatKindChoice, "M47 free");
        r.Call("Wo155OnFriendBeat", (byte)3, Protocol.StoryBeatKindChoice, "M47 join");
        r.ClearPending(1); r.ClearPending(3);
        r.Call("Wo155OnFriendBeat", (byte)3, Protocol.StoryBeatKindSceneStay, "stay");      // 3 said F12 to this scene
        Assert.True(r.Call<bool>("Wo153NoteScene", "a scene moved the host", true));
        Assert.True(r.Pending(1));
        Assert.False(r.Pending(2));     // staying
        Assert.False(r.Pending(3));     // keeps playing through this scene
        // the next scene is a new one: 3's "keep playing" was used up
        r.ClearPending(1);
        Assert.True(r.Call<bool>("Wo153NoteScene", "another scene", true));
        Assert.True(r.Pending(1));
        Assert.True(r.Pending(3));
        Assert.False(r.Pending(2));
    }

    [Fact]
    public void Keep_playing_on_a_scene_is_not_a_story_choice()
    {
        var r = new Rig();
        r.Host(1);                                         // no period at all
        r.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindSceneStay, "stay");
        Assert.False(r.Call<bool>("Wo153NoteScene", "a scene", true));
        Assert.False(r.Pending(1));
        Assert.True(r.Call<bool>("Wo153NoteScene", "a scene", true));      // once
        Assert.True(r.Pending(1));
    }

    [Fact]
    public void The_next_section_of_the_period_keeps_the_answers_and_the_end_releases_everyone()
    {
        var r = new Rig();
        r.Host(1, 2);
        r.HostEnters(FinalSetPath);
        r.Call("Wo155OnFriendBeat", (byte)2, Protocol.StoryBeatKindChoice, "M47 free");
        r.HostEnters(FinalSetNext);                                         // M48a: the same period
        Assert.True(r.Exempt(2));
        Assert.True(r.Exempt(1));                                           // still has not answered
        r.HostEnters(FinalSetEnd);                                          // M51's end: nothing active
        var roster = r.Get<RailsRoster>("_w155Roster");
        Assert.True(roster.Ending);                                         // the friends keep a period a few seconds after its last section: so does the host
        Assert.True(r.Exempt(2));
        roster.Tick(Environment.TickCount64 + RailsRules.PeriodEndGraceMs + 1, new byte[] { 1, 2 });   // the grace runs out
        Assert.False(r.Exempt(2));
        Assert.False(r.Exempt(1));
    }

    [Fact]
    public void A_short_gap_between_two_quests_of_one_period_keeps_the_answers()
    {
        var r = new Rig();
        r.Host(1, 2);
        r.HostEnters("Barbora.trosecko.svatba.start");                      // M05
        r.Call("Wo155OnFriendBeat", (byte)2, Protocol.StoryBeatKindChoice, "M05 free");
        r.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindChoice, "M05 join");
        r.HostEnters("Barbora.trosecko.svatba.endQuest");                   // M05 ends: nothing active for a moment
        r.HostEnters("Barbora.trosecko.naTroskach.start");                  // M06 begins: the same period (the wedding runs into Trosky)
        Assert.True(r.Exempt(2));
        Assert.False(r.Exempt(1));                                          // answers stood; nobody was asked again
    }

    [Fact]
    public void The_story_lock_going_off_releases_at_once_with_no_grace()
    {
        var r = new Rig();
        r.Host(1);
        r.HostEnters(FinalSetPath);
        r.Set("_w153StoryOn", false);
        r.Call("Wo153StoryOff");
        Assert.Null(r.Get<RailsRoster>("_w155Roster").Period);
        Assert.False(r.Exempt(1));
    }

    [Fact]
    public void A_friend_who_is_gone_is_forgotten()
    {
        var r = new Rig();
        r.Host(1);
        r.HostEnters(FinalSetPath);
        r.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindChoice, "M47 free");
        r.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindSceneStay, "stay");
        r.Call("Wo155Forget", (byte)1);
        Assert.Equal(0, r.Get<RailsRoster>("_w155Roster").Count(RailsChoice.Free));
        Assert.Equal(0, r.Get<SceneStayBook>("_w155SceneStay").Count);
    }

    [Fact]
    public void An_answer_for_another_period_changes_nothing()
    {
        var r = new Rig();
        r.Host(1);
        r.HostEnters(FinalSetPath);
        r.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindChoice, "M45 free");
        Assert.True(r.Exempt(1));                                           // still pending for the final set, not free for the cardinal
        Assert.Equal(RailsChoice.Pending, r.Get<RailsRoster>("_w155Roster").ChoiceOf(1));
    }

    [Theory]
    [InlineData("")] [InlineData("M47")] [InlineData("M47 maybe")] [InlineData("M03 join")] [InlineData("nonsense words here")]
    public void A_malformed_answer_is_dropped(string text)
    {
        var r = new Rig();
        r.Host(1);
        r.HostEnters(FinalSetPath);
        r.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindChoice, text);
        Assert.Equal(RailsChoice.Pending, r.Get<RailsRoster>("_w155Roster").ChoiceOf(1));
    }

    [Fact]
    public void An_answer_from_a_stranger_is_ignored()
    {
        var r = new Rig();
        r.Host(1);
        r.HostEnters(FinalSetPath);
        r.Call("Wo155OnFriendBeat", (byte)9, Protocol.StoryBeatKindChoice, "M47 free");   // id 9 is not a connected peer
        Assert.False(r.Get<RailsRoster>("_w155Roster").Exempt(9) == false);                // unknown stays pending (exempt)
        Assert.Equal(0, r.Get<RailsRoster>("_w155Roster").Count(RailsChoice.Free));
    }

    [Fact]
    public void Only_the_host_reads_a_friends_answer()
    {
        var r = new Rig();
        r.Friend();
        r.Get<PeerSet>("_peers").Connected(1, "Friend1");
        r.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindChoice, "M47 free");
        Assert.Equal(0, r.Get<RailsRoster>("_w155Roster").Count(RailsChoice.Free));
    }

    [Fact]
    public void A_friend_who_stays_has_a_running_countdown_cancelled()
    {
        var r = new Rig();
        r.Host(1);
        // a leash countdown was running for friend 1
        var logic = r.Get<ConcurrentDictionary<byte, LeashLogic>>("_leashByJoiner").GetOrAdd(1, _ => new LeashLogic());
        lock (logic) { logic.Config = new LeashLogic.Settings(true, 600, 650); logic.Tick(0, 700, LeashLogic.Hold.None); }
        Assert.True(logic.CountdownActive);
        r.HostEnters(FinalSetPath);
        r.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindChoice, "M47 free");
        Assert.False(logic.CountdownActive);
    }

    [Fact]
    public void Switching_the_story_lock_off_releases_everyone()
    {
        var r = new Rig();
        r.Host(1);
        r.HostEnters(FinalSetPath);
        Assert.True(r.Exempt(1));
        r.Set("_w153StoryOn", false);
        r.Call("Wo153StoryOff");
        Assert.False(r.Exempt(1));
    }

    // ================================================================ the friend

    private static (Rig R, byte Host) FriendRig()
    {
        var r = new Rig();
        r.Friend();
        return (r, 0);
    }

    [Fact]
    public void The_hosts_new_period_asks_the_friend_once()
    {
        var (r, host) = FriendRig();
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M47");
        Assert.True(r.LuaHas("KCD2MP_W155Ask(\"the final set\", 30)"));
        int asks = r.Game.Lua.Count(l => l.Contains("KCD2MP_W155Ask"));
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M47");      // the host repeats it every 30 s
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M48a");     // the next section of the same period
        Assert.Equal(asks, r.Game.Lua.Count(l => l.Contains("KCD2MP_W155Ask")));
        Assert.False(r.Get<bool>("_w155Free"));
        Assert.Equal("asking", r.Call<string>("W155ChoiceName"));
    }

    [Fact]
    public void Staying_tells_the_host_holds_the_quest_mirror_and_refuses_a_pull()
    {
        var (r, host) = FriendRig();
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M47");
        Assert.False(r.Prop<bool>("W151MirrorHolding"));
        r.Call("Wo155OnEvent", "w155_choice", "free key");
        Assert.True(r.Get<bool>("_w155Free"));
        Assert.Contains(r.Beats, b => b.Kind == Protocol.StoryBeatKindChoice && b.Text == "M47 free");
        Assert.True(r.Prop<bool>("W151MirrorHolding"));                                     // the host's quest steps are held
        Assert.NotEqual(0, r.Call<ushort>("JoinerLeashFlags") & Protocol.LeashFlagFreeRoam);
        Assert.True(r.LuaHas("KCD2MP_W155Decided(\"free\", \"the final set\""));
        Assert.Equal("free", r.Call<string>("W155ChoiceName"));
        // a pull is refused
        r.Call("Wo114PullAsync", new LeashCommand(Protocol.LeashKindPull, 7, Protocol.LeashReasonDistance, 1, 2, 3, 700));
        Assert.Equal(Protocol.LeashResultBusy, r.Get<byte>("_leashLastResult"));
    }

    [Fact]
    public void Joining_after_staying_brings_the_mirror_back_and_words_the_pull()
    {
        var (r, host) = FriendRig();
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M47");
        r.Call("Wo155OnEvent", "w155_choice", "free key");
        r.Call("Wo155OnEvent", "w155_choice", "join key");
        Assert.False(r.Get<bool>("_w155Free"));
        Assert.False(r.Prop<bool>("W151MirrorHolding"));
        Assert.Equal(0, r.Call<ushort>("JoinerLeashFlags") & Protocol.LeashFlagFreeRoam);
        Assert.Contains(r.Beats, b => b.Kind == Protocol.StoryBeatKindChoice && b.Text == "M47 join");
        Assert.Equal(RailsRules.JoinedText, r.Call<string>("Wo153PulledText", true));
        Assert.NotEqual(RailsRules.JoinedText, r.Call<string>("Wo153PulledText", true));   // once: a later, unrelated fast travel keeps its own words
    }

    [Fact]
    public void A_pull_that_is_not_a_fast_travel_never_takes_the_joined_words()
    {
        var (r, host) = FriendRig();
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M47");
        r.Call("Wo155OnEvent", "w155_choice", "join key");
        Assert.NotEqual(RailsRules.JoinedText, r.Call<string>("Wo153PulledText", false));   // a distance pull
        Assert.Equal(RailsRules.JoinedText, r.Call<string>("Wo153PulledText", true));       // the join's own pull still can
    }

    [Fact]
    public void The_end_of_a_scene_that_began_before_the_friend_chose_to_stay_is_not_left_in_the_tables()
    {
        var (r, host) = FriendRig();
        r.Call("Wo153OnPeerScene", host, true, "Ingame", "ride");           // the notice's scene starts first
        Assert.True(r.Prop<bool>("W153HostSceneRunning"));
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M47");
        r.Call("Wo155OnEvent", "w155_choice", "free key");
        r.Call("Wo153OnPeerScene", host, false, "Ingame", "ride");          // ...and ends while the friend stays
        Assert.False(r.Prop<bool>("W153HostSceneRunning"));
        // a scene that starts while they stay is not tracked at all
        r.Call("Wo153OnPeerScene", host, true, "Ingame", "another");
        Assert.False(r.Prop<bool>("W153HostSceneRunning"));
    }

    [Fact]
    public void A_standing_answer_asks_nothing()
    {
        var (r, host) = FriendRig();
        r.Set("_w155Pref", (int)RailsPref.Free);
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M45");
        Assert.False(r.LuaHas("KCD2MP_W155Ask"));
        Assert.True(r.Get<bool>("_w155Free"));
        Assert.Contains(r.Beats, b => b.Kind == Protocol.StoryBeatKindChoice && b.Text == "M45 free");
        var (r2, _) = FriendRig();
        r2.Set("_w155Pref", (int)RailsPref.Join);
        r2.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M45");
        Assert.False(r2.LuaHas("KCD2MP_W155Ask"));
        Assert.False(r2.Get<bool>("_w155Free"));
        Assert.Contains(r2.Beats, b => b.Kind == Protocol.StoryBeatKindChoice && b.Text == "M45 join");
    }

    [Fact]
    public void A_friend_who_stays_gets_no_cutscene_notice_and_no_region_notice()
    {
        var (r, host) = FriendRig();
        r.Set("_w155Pref", (int)RailsPref.Free);
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M30");
        while (r.Game.Lua.TryDequeue(out _)) { }
        r.Call("Wo153OnPeerScene", host, true, "Ingame", "ride");
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindLevel, "kutnohorsko");
        Assert.False(r.LuaHas("KCD2MP_W153HostScene"));
        Assert.False(r.LuaHas("KCD2MP_W153Story(\"level\""));
    }

    [Fact]
    public void The_hosts_period_ending_gives_a_friend_who_stayed_the_mirror_back_and_says_so()
    {
        var (r, host) = FriendRig();
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M47");
        r.Call("Wo155OnEvent", "w155_choice", "free key");
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionLeave, "M51 completed");   // grace first: a next section may follow
        Assert.True(r.Get<bool>("_w155Free"));
        r.Call("Wo155ForceOver");                                                                    // the grace ran out / the host went quiet
        Assert.False(r.Get<bool>("_w155Free"));
        Assert.False(r.Prop<bool>("W151MirrorHolding"));
        Assert.True(r.LuaHas("KCD2MP_W155Over(\"the final set\", \"free\")"));
        Assert.Equal("", r.Call<string>("W155ChoiceName"));
    }

    [Fact]
    public void A_leave_then_the_next_section_of_the_same_period_does_not_end_the_stay()
    {
        var (r, host) = FriendRig();
        r.Set("_w155Pref", (int)RailsPref.Free);
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M47");
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionLeave, "M47 moved_on");
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M48a");
        Assert.True(r.Get<bool>("_w155Free"));
        Assert.False(r.LuaHas("KCD2MP_W155Ask"));
        Assert.False(r.LuaHas("KCD2MP_W155Over"));
    }

    [Fact]
    public void The_host_leaving_ends_the_stay()
    {
        var (r, host) = FriendRig();
        r.Set("_w155Pref", (int)RailsPref.Free);
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M47");
        r.Get<PeerSet>("_peers").Connected(host, "Host");
        r.Call("Wo153OnPeerGone", host);
        Assert.False(r.Get<bool>("_w155Free"));
    }

    [Fact]
    public void A_session_reset_forgets_the_question_and_the_stay()
    {
        var (r, host) = FriendRig();
        r.Set("_w155Pref", (int)RailsPref.Free);
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M47");
        Assert.True(r.Get<bool>("_w155Free"));
        r.Call("Wo153OnDisconnect");
        Assert.False(r.Get<bool>("_w155Free"));
        Assert.Equal("", r.Call<string>("W155ChoiceName"));
    }

    [Fact]
    public void An_answer_with_no_story_period_open_is_nothing_to_answer()
    {
        var (r, _) = FriendRig();
        r.Call("Wo155OnEvent", "w155_choice", "free cmd");
        Assert.False(r.Get<bool>("_w155Free"));
        Assert.True(r.LuaHas("KCD2MP_W155Decided(\"none\""));
        Assert.DoesNotContain(r.Beats, b => b.Kind == Protocol.StoryBeatKindChoice);
    }

    [Fact]
    public void The_host_never_answers_for_itself()
    {
        var r = new Rig();
        r.Host(1);
        r.Call("Wo155OnEvent", "w155_choice", "free key");
        Assert.False(r.Get<bool>("_w155Free"));
        Assert.DoesNotContain(r.Beats, b => b.Kind == Protocol.StoryBeatKindChoice);
    }

    [Fact]
    public void Keep_playing_on_a_scene_tells_the_host_only_from_a_friend_in_the_hosts_world()
    {
        var (r, _) = FriendRig();
        r.Call("Wo155OnEvent", "w155_scene_stay", "");
        Assert.Contains(r.Beats, b => b.Kind == Protocol.StoryBeatKindSceneStay && b.Text == "stay");
        var h = new Rig();
        h.Host(1);
        h.Call("Wo155OnEvent", "w155_scene_stay", "");
        Assert.DoesNotContain(h.Beats, b => b.Kind == Protocol.StoryBeatKindSceneStay);
    }

    [Fact]
    public void The_launcher_line_says_where_each_player_stands()
    {
        var (r, host) = FriendRig();
        r.Call("Wo153OnPeerStory", host, Protocol.StoryBeatKindSectionEnter, "M47");
        r.Call("Wo155OnEvent", "w155_choice", "free key");
        string j = r.Call<string>("Wo153CoopStatusJson");
        Assert.Contains("\"railsChoice\":\"free\"", j);
        Assert.Contains("staying in the open world while your host is in the final set", j);
        var h = new Rig();
        h.Host(1, 2);
        h.HostEnters(FinalSetPath);
        h.Call("Wo155OnFriendBeat", (byte)1, Protocol.StoryBeatKindChoice, "M47 join");
        string hj = h.Call<string>("Wo153CoopStatusJson");
        Assert.Contains("the final set: 1 joined you, 0 staying in the open world, 1 deciding", hj);
    }
}
