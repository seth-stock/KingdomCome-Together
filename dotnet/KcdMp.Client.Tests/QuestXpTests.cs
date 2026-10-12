// SPDX-License-Identifier: GPL-3.0-only
// Session 10: causal quest XP (QuestXp.cs, native xpread.h), protocol 17.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

public class QuestXpTests
{
    private static uint _seq;
    private static XpRow Row(bool skill, uint index, uint requested, byte cause = 1, uint root = 10, uint tag = 0, uint age = 0) =>
        new(++_seq, skill, 0x0500000000000251, index, requested, requested, 5, age, cause, root, tag);
    private static XpKey S(uint i) => new(false, i);
    private static XpKey K(uint i) => new(true, i);

    [Fact]
    public void The_wire_text_round_trips_and_is_ordered()
    {
        var text = QuestXpRules.Text(42, new Dictionary<XpKey, uint> { [K(17)] = 384, [S(0)] = 1024, [S(3)] = 128 });
        Assert.Equal("42 s0:1024,s3:128,k17:384", text);
        Assert.True(QuestXpRules.TryParse(text, out uint seq, out var totals));
        Assert.Equal(42u, seq);
        Assert.Equal(384u, totals[K(17)]);
        Assert.Equal(3, totals.Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("0 s0:128")]                        // no change 0
    [InlineData("5 s8:128")]                        // storyProgress is world-owned: never on the wire
    [InlineData("5 s0:0")]                          // nothing
    [InlineData("5 s0:128001")]                     // more than 1000 XP
    [InlineData("5 s0:128,s0:128")]                 // a slot twice
    [InlineData("5 x0:128")]
    [InlineData("5 k5:128")]                        // no such skill
    [InlineData("5 s0:-1")]
    [InlineData("5 s0:128 extra")]
    [InlineData("5 s0")]
    public void A_malformed_or_foreign_text_is_refused(string text) => Assert.False(QuestXpRules.TryParse(text, out _, out _));

    [Fact]
    public void Too_many_slots_are_refused()
    {
        var ids = new uint[] { 0, 1, 2, 3, 9 };
        var many = string.Join(',', ids.Select(i => $"s{i}:128").Concat(new[] { 0, 1, 2, 3, 4, 6, 7, 8 }.Select(i => $"k{i}:128")));
        Assert.False(QuestXpRules.TryParse("5 " + many, out _, out _));          // 13 entries
    }

    [Fact]
    public void Only_a_quests_own_known_rewards_are_shared()
    {
        Assert.True(QuestXpRules.Shares(Row(false, 0, 1024)));
        Assert.True(QuestXpRules.Shares(Row(true, 17, 384, cause: 2)));
        Assert.False(QuestXpRules.Shares(Row(false, 0, 1024, cause: 0)));       // a script, a fight, a conversation
        Assert.False(QuestXpRules.Shares(Row(false, 8, 7360)));                 // storyProgress
        Assert.False(QuestXpRules.Shares(Row(true, 5, 100)));                   // no such skill
        Assert.False(QuestXpRules.Shares(Row(false, 0, 0)));
    }

    [Fact]
    public void The_difference_is_only_what_this_copy_lacks()
    {
        var host = new Dictionary<XpKey, uint> { [S(0)] = 1024, [K(17)] = 384, [S(1)] = 128 };
        var native = new Dictionary<XpKey, uint> { [S(0)] = 1024, [K(17)] = 128, [S(2)] = 5000 };
        var give = QuestXpRules.Difference(host, native);
        Assert.Equal(2, give.Count);
        Assert.Equal(256u, give[K(17)]);
        Assert.Equal(128u, give[S(1)]);
        Assert.Equal("{{false,1,128},{true,17,256}}", QuestXpRules.Lua(give));
    }

    [Fact]
    public void The_host_groups_rows_by_the_change_they_ran_under_and_sends_a_finished_group_once()
    {
        var host = new QuestXpHost();
        host.Feed(new[] { Row(false, 0, 1024, root: 10), Row(true, 17, 256, cause: 2, root: 10), Row(false, 0, 128, root: 10), Row(false, 3, 128, root: 11) }, 1000);
        Assert.Empty(host.Take(1500, 1500, _ => true));                          // still warm
        host.Feed(new[] { Row(true, 17, 128, cause: 2, root: 10) }, 2000);
        var done = host.Take(3600, 1500, seq => seq == 10);
        var only = Assert.Single(done);                                           // 11 was never shared: dropped, not sent
        Assert.Equal(10u, only.Seq);
        Assert.Equal(1152u, only.Totals[S(0)]);
        Assert.Equal(384u, only.Totals[K(17)]);
        Assert.Equal(1, host.Unshared);
        Assert.Empty(host.Take(9000, 1500, _ => true));                          // once
        Assert.Equal(1, host.Sent);
    }

    [Fact]
    public void The_host_never_sends_what_no_change_caused_or_what_is_not_a_quests()
    {
        var host = new QuestXpHost();
        host.Feed(new[] { Row(false, 0, 1024, root: 0), Row(false, 0, 1024, cause: 0, root: 10), Row(false, 8, 7360, root: 10) }, 1000);
        Assert.Empty(host.Take(9000, 1500, _ => true));
        Assert.Equal(1, host.Unattributed);
    }

    [Fact]
    public void A_joiner_whose_engine_paid_everything_is_owed_nothing()
    {
        var g = new QuestXpGuest();
        g.NoteNative(new[] { Row(false, 0, 1024, tag: 10), Row(true, 17, 384, cause: 2, tag: 10) }, 5000);
        g.HostAmounts(10, new() { [S(0)] = 1024, [K(17)] = 384 }, 4000);
        g.Applied(10, 4800, 4900);
        Assert.Empty(g.Settle(6000));                                             // the window is still open
        var r = Assert.Single(g.Settle(9000));
        Assert.Empty(r.Give);
    }

    [Fact]
    public void A_joiner_whose_graph_did_not_pay_is_given_the_whole_reward_once()
    {
        var g = new QuestXpGuest();
        g.HostAmounts(10, new() { [S(0)] = 1024, [K(17)] = 384 }, 4000);
        g.Applied(10, 4800, 4900);
        var r = Assert.Single(g.Settle(9000));
        Assert.Equal(1024u, r.Give[S(0)]);
        Assert.Equal(384u, r.Give[K(17)]);
        Assert.Empty(g.Settle(20_000));                                           // once
    }

    [Fact]
    public void A_joiner_that_paid_part_is_given_the_rest()
    {
        var g = new QuestXpGuest();
        g.NoteNative(new[] { Row(false, 0, 1024, tag: 10) }, 5000);
        g.HostAmounts(10, new() { [S(0)] = 1024, [S(1)] = 128 }, 4000);
        g.Applied(10, 4800, 4900);
        var r = Assert.Single(g.Settle(9000));
        Assert.Equal(new[] { S(1) }, r.Give.Keys);
    }

    [Fact]
    public void A_delayed_native_payment_inside_the_window_counts_even_with_no_tag()
    {
        var g = new QuestXpGuest();
        g.HostAmounts(10, new() { [S(0)] = 1024 }, 4000);
        g.Applied(10, 4800, 4900);
        g.NoteNative(new[] { Row(false, 0, 1024, tag: 0, age: 1200) }, 6000);      // happened at 4800: inside [start-300, end+3000]
        var r = Assert.Single(g.Settle(9000));
        Assert.Empty(r.Give);
    }

    [Fact]
    public void A_payment_far_outside_the_window_is_not_this_changes()
    {
        var g = new QuestXpGuest();
        g.HostAmounts(10, new() { [S(0)] = 1024 }, 4000);
        g.Applied(10, 4800, 4900);
        g.NoteNative(new[] { Row(false, 0, 1024, tag: 0, age: 0) }, 30_000);       // 25 s later: another quest's
        var r = Assert.Single(g.Settle(40_000));
        Assert.Equal(1024u, r.Give[S(0)]);
    }

    [Fact]
    public void A_change_this_copy_already_had_owes_nothing_whichever_message_came_first()
    {
        var g = new QuestXpGuest();
        g.HostAmounts(10, new() { [S(0)] = 1024 }, 4000);
        g.AlreadyThere(10, 4100);
        Assert.Empty(g.Settle(60_000));
        g.AlreadyThere(11, 4100);
        g.HostAmounts(11, new() { [S(0)] = 1024 }, 4200);                           // the message after the apply
        g.Applied(11, 1, 2);                                                       // (even if something later claimed it moved)
        Assert.Empty(g.Settle(60_000).Where(x => x.Seq == 11));
    }

    [Fact]
    public void Amounts_for_a_change_that_was_never_applied_here_expire_and_are_never_given()
    {
        var g = new QuestXpGuest();
        g.HostAmounts(10, new() { [S(0)] = 1024 }, 4000);
        Assert.Empty(g.Settle(60_000));
        Assert.Empty(g.Settle(200_000));
        Assert.Equal(1, g.Expired);
    }

    [Fact]
    public void Protocol_17_names_the_new_kind_and_the_loot_text_fits()
    {
        Assert.Equal(17, Protocol.Version);
        Assert.Equal("quest-xp", Protocol.LootHostName(Protocol.LootHostQuestXp));
        var all = new Dictionary<XpKey, uint>();
        foreach (var i in new uint[] { 0, 1, 2, 3, 9 }) all[S(i)] = QuestXpRules.MaxAmount;
        foreach (var i in new uint[] { 0, 1, 2, 3, 4, 6, 7 }) all[K(i)] = QuestXpRules.MaxAmount;
        Assert.True(QuestXpRules.Text(4_000_000_000, all).Length < 400);
        var scoped = LootMsg.ScopedText("0123456789abcdef0123456789abcdef", QuestXpRules.Text(7, new Dictionary<XpKey, uint> { [S(0)] = 128 }));
        var wire = new LootMsg(Protocol.LootHostQuestXp, 0, scoped).BuildUp(Protocol.LootHostUp, 3);
        Assert.NotEmpty(wire);
    }
}
