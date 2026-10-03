// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-155: join the host's story period, or stay in the open world (docs/WO-155-findings.md).</summary>
public class Wo155Tests
{
    // ---------------------------------------------------------------- periods

    [Fact]
    public void Periods_are_runs_of_consecutive_locked_sections()
    {
        var ids = StorySections.Periods.Select(p => p.Id).ToArray();
        Assert.Equal(new[] { "M01", "M05", "M09", "M12", "M30", "M32", "M37a", "M38", "M42", "M44a", "M44b", "M45", "M46", "M47" }, ids);
    }

    [Theory]
    [InlineData("M01", "M01")] [InlineData("M02", "M01")]
    [InlineData("M05", "M05")] [InlineData("M06", "M05")]           // the wedding runs into Trosky
    [InlineData("M09", "M09")] [InlineData("M10", "M09")] [InlineData("M11", "M09")]
    [InlineData("M12", "M12")]
    [InlineData("M30", "M30")] [InlineData("M31", "M30")]           // the move to Kuttenberg is two quests
    [InlineData("M37a", "M37a")] [InlineData("M37b", "M37a")]
    [InlineData("M47", "M47")] [InlineData("M48a", "M47")] [InlineData("M48b", "M47")] [InlineData("M48c", "M47")]
    [InlineData("M49", "M47")] [InlineData("M50", "M47")] [InlineData("M51", "M47")]   // the final set: six quests, one choice
    public void A_section_belongs_to_its_period(string code, string period) => Assert.Equal(period, StorySections.PeriodOf(code)!.Id);

    [Theory]
    [InlineData("M03")] [InlineData("M07")] [InlineData("M08")] [InlineData("M33")] [InlineData("M34")] [InlineData("M35")]
    [InlineData("M99")] [InlineData("")] [InlineData(null)]
    public void An_open_quest_or_unknown_code_has_no_period(string? code) => Assert.Null(StorySections.PeriodOf(code));

    [Fact]
    public void Every_locked_section_is_in_exactly_one_period_and_no_open_one_is()
    {
        foreach (var s in StorySections.All)
        {
            int n = StorySections.Periods.Count(p => p.Codes.Contains(s.Code));
            Assert.Equal(s.Locked ? 1 : 0, n);
        }
    }

    [Fact]
    public void A_period_says_what_it_is_in_the_hosts_words()
    {
        Assert.Equal("the wedding in Semine and Trosky castle", StorySections.PeriodOf("M06")!.Why);
        Assert.Equal("the final set", StorySections.PeriodOf("M51")!.Why);
        Assert.Equal("the move to Kuttenberg", StorySections.PeriodOf("M31")!.Why);
        Assert.Equal("the battle of Nebakov", StorySections.PeriodOf("M10")!.Why);
    }

    // ---------------------------------------------------------------- the wire text

    [Theory]
    [InlineData("M47 join", "M47", RailsChoice.Join)]
    [InlineData("M51 free", "M47", RailsChoice.Free)]
    [InlineData("M06 join", "M05", RailsChoice.Join)]
    public void An_answer_names_its_period(string text, string period, RailsChoice choice)
    {
        Assert.True(RailsRules.TryParseChoice(text, out var p, out var c));
        Assert.Equal(period, p.Id);
        Assert.Equal(choice, c);
    }

    [Theory]
    [InlineData("")] [InlineData("M47")] [InlineData("M47 pending")] [InlineData("M47 join extra")] [InlineData("M03 join")]
    [InlineData("zzz join")] [InlineData("M47 JOIN")] [InlineData("M47\njoin")]
    public void Anything_else_is_not_an_answer(string text) => Assert.False(RailsRules.TryParseChoice(text, out _, out _));

    [Fact]
    public void An_answer_round_trips() =>
        Assert.True(RailsRules.TryParseChoice(RailsRules.ChoiceText("M48b", RailsChoice.Free), out var p, out var c) && p.Id == "M47" && c == RailsChoice.Free);

    [Theory]
    [InlineData("ask", RailsPref.Ask)] [InlineData(" JOIN ", RailsPref.Join)] [InlineData("free", RailsPref.Free)]
    [InlineData("stay", RailsPref.Free)] [InlineData("open", RailsPref.Free)]
    public void A_standing_answer_parses(string s, RailsPref p) => Assert.Equal(p, RailsRules.ParsePref(s));

    [Theory] [InlineData("")] [InlineData("maybe")] [InlineData(null)] [InlineData("%line")]
    public void A_bad_standing_answer_does_not(string? s) => Assert.Null(RailsRules.ParsePref(s));

    // ---------------------------------------------------------------- the host's roster

    [Fact]
    public void Outside_a_period_nobody_is_exempt()
    {
        var r = new RailsRoster();
        Assert.False(r.Exempt(1));
        Assert.Equal(RailsChoice.Join, r.ChoiceOf(1));
    }

    [Fact]
    public void A_new_period_exempts_every_friend_until_they_answer()
    {
        var r = new RailsRoster();
        Assert.True(r.Start("M47"));
        r.SeedPending(new byte[] { 1, 2 }, 0);
        Assert.True(r.Exempt(1));
        Assert.True(r.Exempt(2));
        Assert.True(r.Exempt(9));   // unknown to the roster: not moved without its say
        Assert.Equal(RailsRoster.Outcome.Joined, r.Choose(1, "M47", RailsChoice.Join, 1000));
        Assert.False(r.Exempt(1));
        Assert.Equal(RailsRoster.Outcome.Freed, r.Choose(2, "M47", RailsChoice.Free, 1000));
        Assert.True(r.Exempt(2));
    }

    [Fact]
    public void The_next_section_of_the_same_period_keeps_the_answers()
    {
        var r = new RailsRoster();
        r.Start("M47");
        r.Choose(2, "M47", RailsChoice.Free, 0);
        Assert.False(r.Start("M47"));   // M48a after M47: same period
        Assert.True(r.Exempt(2));
    }

    [Fact]
    public void A_different_period_asks_everyone_again()
    {
        var r = new RailsRoster();
        r.Start("M45");
        r.Choose(2, "M45", RailsChoice.Free, 0);
        r.Choose(3, "M45", RailsChoice.Join, 0);
        Assert.True(r.Start("M46"));
        r.SeedPending(new byte[] { 2, 3 }, 10);
        Assert.True(r.Exempt(2));
        Assert.True(r.Exempt(3));   // joined the cardinal, not yet the Italian Job
    }

    [Fact]
    public void A_late_answer_for_a_period_that_is_over_is_ignored()
    {
        var r = new RailsRoster();
        r.Start("M45");
        r.Start("M46");
        Assert.Equal(RailsRoster.Outcome.Ignored, r.Choose(1, "M45", RailsChoice.Join, 0));
        r.End();
        Assert.Equal(RailsRoster.Outcome.NoPeriod, r.Choose(1, "M46", RailsChoice.Free, 0));   // a late repeat: not worth a log line
        Assert.False(r.Exempt(1));
    }

    [Fact]
    public void The_hosts_period_outlives_its_last_section_by_the_grace_so_a_short_gap_keeps_the_answers()
    {
        var r = new RailsRoster();
        r.Start("M05");
        r.Choose(2, "M05", RailsChoice.Free, 0);
        r.EndSoon(10_000);                                                  // M05 ended
        Assert.True(r.Ending);
        Assert.True(r.Exempt(2));                                           // still free: the friend keeps it for the same grace
        Assert.Empty(r.Tick(10_000 + RailsRules.PeriodEndGraceMs - 1, new byte[] { 2 }));
        Assert.True(r.Exempt(2));
        Assert.False(r.Start("M05"));                                       // M06 begins within the grace: the same period
        Assert.False(r.Ending);
        Assert.True(r.Exempt(2));                                           // the answer stood
        r.Tick(10_000 + RailsRules.PeriodEndGraceMs + 1000, new byte[] { 2 });
        Assert.True(r.Exempt(2));                                           // and the cancelled end does not fire later
    }

    [Fact]
    public void After_the_grace_the_period_is_over_and_everyone_is_released()
    {
        var r = new RailsRoster();
        r.Start("M05");
        r.Choose(2, "M05", RailsChoice.Free, 0);
        r.EndSoon(10_000);
        r.Tick(10_000 + RailsRules.PeriodEndGraceMs, new byte[] { 2 });
        Assert.Null(r.Period);
        Assert.False(r.Exempt(2));
        Assert.True(r.Start("M05"));                                        // a later start is a new period: asked again
        Assert.True(r.Exempt(2));
    }

    [Fact]
    public void A_different_period_straight_after_replaces_it_even_within_the_grace()
    {
        var r = new RailsRoster();
        r.Start("M45");
        r.Choose(2, "M45", RailsChoice.Free, 0);
        r.EndSoon(0);
        Assert.True(r.Start("M46"));
        Assert.False(r.Ending);
        Assert.Equal(RailsChoice.Pending, r.ChoiceOf(2));
    }

    [Fact]
    public void Ending_twice_does_not_extend_the_grace()
    {
        var r = new RailsRoster();
        r.Start("M05");
        r.EndSoon(0);
        r.EndSoon(5000);
        r.Tick(RailsRules.PeriodEndGraceMs, new byte[] { });
        Assert.Null(r.Period);
    }

    [Fact]
    public void The_hosts_wait_for_an_answer_outlasts_the_worst_case_of_asking()
    {
        // a friend who arrives mid-period hears the host's beat on its next 30 s repeat and then has the prompt's 30 s
        Assert.True(RailsRules.HostGraceMs >= 30_000 + RailsRules.PromptSeconds * 1000 + 10_000);
        Assert.True(RailsRules.HostGraceMs > RailsRules.AskBackstopMs);
    }

    [Fact]
    public void An_unchanged_answer_is_not_news()
    {
        var r = new RailsRoster();
        r.Start("M47");
        Assert.Equal(RailsRoster.Outcome.Freed, r.Choose(1, "M47", RailsChoice.Free, 0));
        Assert.Equal(RailsRoster.Outcome.Unchanged, r.Choose(1, "M47", RailsChoice.Free, 20_000));
    }

    [Fact]
    public void A_friend_can_change_their_mind_either_way()
    {
        var r = new RailsRoster();
        r.Start("M47");
        r.Choose(1, "M47", RailsChoice.Free, 0);
        Assert.Equal(RailsRoster.Outcome.Joined, r.Choose(1, "M47", RailsChoice.Join, 5000));
        Assert.False(r.Exempt(1));
        Assert.Equal(RailsRoster.Outcome.Freed, r.Choose(1, "M47", RailsChoice.Free, 6000));
        Assert.True(r.Exempt(1));
    }

    [Fact]
    public void Silence_is_taken_as_joined_after_the_grace_and_the_friend_is_owed_the_bring_along()
    {
        var r = new RailsRoster();
        r.Start("M47");
        r.SeedPending(new byte[] { 1, 2 }, 0);
        r.Choose(2, "M47", RailsChoice.Free, 100);
        Assert.Empty(r.Tick(RailsRules.HostGraceMs - 1, new byte[] { 1, 2 }));
        Assert.True(r.Exempt(1));
        var expired = r.Tick(RailsRules.HostGraceMs, new byte[] { 1, 2 });
        Assert.Equal(new byte[] { 1 }, expired);
        Assert.False(r.Exempt(1));
        Assert.True(r.Exempt(2));                                           // an answer of free never times out
        Assert.Empty(r.Tick(RailsRules.HostGraceMs + 1000, new byte[] { 1, 2 }));   // told once
    }

    [Fact]
    public void A_friend_who_arrives_mid_period_gets_their_own_grace()
    {
        var r = new RailsRoster();
        r.Start("M47");
        r.Tick(100_000, new byte[] { 1 });                                  // friend 1 first seen at 100 s
        Assert.True(r.Exempt(1));
        Assert.Empty(r.Tick(100_000 + RailsRules.HostGraceMs - 1, new byte[] { 1 }));
        Assert.Equal(new byte[] { 1 }, r.Tick(100_000 + RailsRules.HostGraceMs, new byte[] { 1 }));
    }

    [Fact]
    public void A_friend_who_left_is_forgotten_and_asked_again_if_they_come_back()
    {
        var r = new RailsRoster();
        r.Start("M47");
        r.Choose(1, "M47", RailsChoice.Free, 0);
        r.Tick(1000, new byte[] { });                                       // gone
        r.Tick(2000, new byte[] { 1 });                                     // back: pending again
        Assert.Equal(RailsChoice.Pending, r.ChoiceOf(1));
    }

    [Fact]
    public void End_releases_everyone()
    {
        var r = new RailsRoster();
        r.Start("M47");
        r.Choose(1, "M47", RailsChoice.Free, 0);
        r.End();
        Assert.False(r.Exempt(1));
        Assert.Null(r.Period);
    }

    [Fact]
    public void Counts_by_choice()
    {
        var r = new RailsRoster();
        r.Start("M47");
        r.SeedPending(new byte[] { 1, 2, 3 }, 0);
        r.Choose(1, "M47", RailsChoice.Join, 0);
        r.Choose(2, "M47", RailsChoice.Free, 0);
        Assert.Equal((1, 1, 1), (r.Count(RailsChoice.Join), r.Count(RailsChoice.Free), r.Count(RailsChoice.Pending)));
    }

    // ---------------------------------------------------------------- "keep playing" on a scene: not brought along after it

    [Fact]
    public void Staying_put_after_a_scene_is_used_once()
    {
        var b = new SceneStayBook();
        b.Note(1, 1000);
        Assert.True(b.Standing(1, 2000));
        Assert.True(b.Take(1, 2000));
        Assert.False(b.Take(1, 2000));
    }

    [Fact]
    public void Staying_put_lapses()
    {
        var b = new SceneStayBook();
        b.Note(1, 0);
        Assert.False(b.Take(1, RailsRules.SceneStayMs + 1));
        b.Note(2, 0);
        Assert.True(b.Take(2, RailsRules.SceneStayMs));
    }

    [Fact]
    public void Staying_put_is_per_friend_and_clearable()
    {
        var b = new SceneStayBook();
        b.Note(1, 0);
        Assert.False(b.Take(2, 0));
        b.Clear();
        Assert.False(b.Take(1, 0));
        Assert.Equal(0, b.Count);
    }

    // ---------------------------------------------------------------- the friend's side

    [Fact]
    public void Asking_is_the_default_for_a_new_period()
    {
        var j = new RailsJoiner();
        var s = j.OnEnter("M47", 0, RailsPref.Ask);
        Assert.Equal(RailsJoiner.Act.Ask, s.Act);
        Assert.Equal("M47", s.Period!.Id);
        Assert.True(j.Asking);
        Assert.False(j.IsFree);
    }

    [Fact]
    public void The_host_repeating_its_beat_does_not_ask_twice()
    {
        var j = new RailsJoiner();
        j.OnEnter("M47", 0, RailsPref.Ask);
        Assert.Equal(RailsJoiner.Act.None, j.OnEnter("M47", 30_000, RailsPref.Ask).Act);
        Assert.Equal(RailsJoiner.Act.None, j.OnEnter("M48a", 31_000, RailsPref.Ask).Act);   // the next section of the same period
    }

    [Theory]
    [InlineData(RailsPref.Join, RailsChoice.Join)]
    [InlineData(RailsPref.Free, RailsChoice.Free)]
    public void A_standing_answer_asks_nothing_and_decides_at_once(RailsPref pref, RailsChoice choice)
    {
        var j = new RailsJoiner();
        var s = j.OnEnter("M45", 0, pref);
        Assert.Equal(RailsJoiner.Act.Auto, s.Act);
        Assert.Equal(choice, s.Choice);
        Assert.False(j.Asking);
        Assert.Equal(choice == RailsChoice.Free, j.IsFree);
    }

    [Fact]
    public void An_answer_sticks_for_the_whole_period_and_a_new_period_asks_again()
    {
        var j = new RailsJoiner();
        j.OnEnter("M47", 0, RailsPref.Ask);
        Assert.True(j.Decide(RailsChoice.Free, 5000));
        Assert.True(j.IsFree);
        Assert.Equal(RailsJoiner.Act.None, j.OnEnter("M50", 60_000, RailsPref.Ask).Act);   // still free in the final set
        Assert.True(j.IsFree);
        j.OnLeave("M51", 70_000);
        Assert.Equal(RailsJoiner.Act.Over, j.Tick(70_000 + RailsRules.PeriodEndGraceMs).Act);
        Assert.False(j.Open);
        Assert.Equal(RailsJoiner.Act.Ask, j.OnEnter("M45", 200_000, RailsPref.Ask).Act);   // another period: asked again
    }

    [Fact]
    public void A_deciding_with_no_period_is_nothing()
    {
        var j = new RailsJoiner();
        Assert.False(j.Decide(RailsChoice.Free, 0));
        j.OnEnter("M47", 0, RailsPref.Ask);
        Assert.False(j.Decide(RailsChoice.Pending, 0));
    }

    [Fact]
    public void The_end_of_a_section_is_not_the_end_of_the_period_if_the_next_follows()
    {
        var j = new RailsJoiner();
        j.OnEnter("M47", 0, RailsPref.Ask);
        j.Decide(RailsChoice.Free, 1000);
        j.OnLeave("M47", 10_000);                                           // host: moved on to M48a
        Assert.Equal(RailsJoiner.Act.None, j.Tick(12_000).Act);
        Assert.Equal(RailsJoiner.Act.None, j.OnEnter("M48a", 12_500, RailsPref.Ask).Act);
        Assert.Equal(RailsJoiner.Act.None, j.Tick(10_000 + RailsRules.PeriodEndGraceMs + 1).Act);   // the grace was cancelled
        Assert.True(j.IsFree);
    }

    [Fact]
    public void A_leave_for_another_period_changes_nothing()
    {
        var j = new RailsJoiner();
        j.OnEnter("M47", 0, RailsPref.Ask);
        j.Decide(RailsChoice.Free, 0);
        j.OnLeave("M45", 1000);
        Assert.Equal(RailsJoiner.Act.None, j.Tick(1000 + RailsRules.PeriodEndGraceMs + 1).Act);
        Assert.True(j.IsFree);
    }

    [Fact]
    public void An_unanswered_question_is_given_up_as_join()
    {
        var j = new RailsJoiner();
        j.OnEnter("M47", 0, RailsPref.Ask);
        Assert.Equal(RailsJoiner.Act.None, j.Tick(RailsRules.AskBackstopMs - 1).Act);
        var s = j.Tick(RailsRules.AskBackstopMs);
        Assert.Equal(RailsJoiner.Act.Backstop, s.Act);
        Assert.Equal(RailsChoice.Join, s.Choice);
    }

    [Fact]
    public void An_answer_is_repeated_while_the_period_lasts()
    {
        var j = new RailsJoiner();
        j.OnEnter("M47", 0, RailsPref.Ask);
        j.Decide(RailsChoice.Free, 1000);
        Assert.Equal(RailsJoiner.Act.None, j.Tick(1000 + RailsRules.ResendMs - 1).Act);
        var s = j.Tick(1000 + RailsRules.ResendMs);
        Assert.Equal((RailsJoiner.Act.Resend, RailsChoice.Free), (s.Act, s.Choice));
        Assert.Equal(RailsJoiner.Act.None, j.Tick(1000 + RailsRules.ResendMs + 1).Act);   // not every tick
        Assert.Equal(RailsJoiner.Act.Resend, j.Tick(1000 + 2 * RailsRules.ResendMs).Act);
    }

    [Fact]
    public void A_standing_answer_is_also_repeated()
    {
        var j = new RailsJoiner();
        j.OnEnter("M45", 0, RailsPref.Free);
        Assert.Equal(RailsJoiner.Act.Resend, j.Tick(RailsRules.ResendMs).Act);
    }

    [Fact]
    public void Force_over_ends_it_without_grace_and_names_the_period()
    {
        var j = new RailsJoiner();
        j.OnEnter("M47", 0, RailsPref.Ask);
        j.Decide(RailsChoice.Free, 0);
        var p = j.ForceOver();
        Assert.Equal("M47", p!.Id);
        Assert.False(j.Open);
        Assert.False(j.IsFree);
        Assert.Null(j.ForceOver());
    }

    [Fact]
    public void An_unknown_section_is_nothing()
    {
        var j = new RailsJoiner();
        Assert.Equal(RailsJoiner.Act.None, j.OnEnter("M03", 0, RailsPref.Ask).Act);   // an open quest has no period
        Assert.Equal(RailsJoiner.Act.None, j.OnEnter("nonsense", 0, RailsPref.Ask).Act);
        Assert.False(j.Open);
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var j = new RailsJoiner();
        j.OnEnter("M47", 0, RailsPref.Ask);
        j.Decide(RailsChoice.Free, 0);
        j.Reset();
        Assert.False(j.Open);
        Assert.False(j.IsFree);
        Assert.Equal(RailsJoiner.Act.None, j.Tick(1_000_000).Act);
    }
}

/// <summary>WO-155: what the launcher says about the choice.</summary>
public class Wo155StatusTests
{
    [Theory]
    [InlineData("joiner", "asking", "choose in the game")]
    [InlineData("joiner", "join", "with your host for the final set")]
    [InlineData("joiner", "free", "staying in the open world")]
    public void A_friend_is_told_where_they_stand(string role, string choice, string contains) =>
        Assert.Contains(contains, CoopStatus.RailsText(role, "the final set", choice, 0, 0, 0));

    [Fact]
    public void A_friend_with_no_period_or_no_choice_gets_nothing()
    {
        Assert.Equal("", CoopStatus.RailsText("joiner", "", "free", 0, 0, 0));
        Assert.Equal("", CoopStatus.RailsText("joiner", "the final set", "", 0, 0, 0));
    }

    [Fact]
    public void The_host_sees_how_many_joined_stay_or_are_deciding()
    {
        Assert.Equal("the final set: 1 joined you, 2 staying in the open world, 0 deciding", CoopStatus.RailsText("host", "the final set", "", 1, 2, 0));
        Assert.Equal("", CoopStatus.RailsText("host", "the final set", "", 0, 0, 0));   // alone: nothing to say
    }

    [Fact]
    public void The_json_carries_the_choice_and_its_words()
    {
        using var d = System.Text.Json.JsonDocument.Parse(CoopStatus.Json("joiner", "in-sync", "Oratores", "the cardinal", true, null, null, "free", "You are staying \"free\""));
        Assert.Equal("free", d.RootElement.GetProperty("railsChoice").GetString());
        Assert.Equal("You are staying \"free\"", d.RootElement.GetProperty("railsText").GetString());
    }

    [Fact]
    public void The_json_without_a_choice_still_has_the_fields()
    {
        using var d = System.Text.Json.JsonDocument.Parse(CoopStatus.Json("none", "solo", "", "", false, null, null));
        Assert.Equal("", d.RootElement.GetProperty("railsChoice").GetString());
        Assert.Equal("", d.RootElement.GetProperty("railsText").GetString());
    }
}
