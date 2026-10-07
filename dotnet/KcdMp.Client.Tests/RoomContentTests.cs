// SPDX-License-Identifier: GPL-3.0-only
// Content rules of the room: other mods, and the "least DLC wins" cap on what is shared.
namespace KcdMp.Client.Tests;

public class RoomContentTests
{
    [Fact]
    public void The_content_tokens_in_an_ack_are_parsed_and_stripped()
    {
        string missing = "authority.npc,other-mods,dlc-host-extra=A+B,authority.loot,dlc-peer-extra=C";
        var f = RoomContract.ParseFlags(missing);
        Assert.True(f.ModsDiffer);
        Assert.Equal(new[] { "A", "B" }, f.DlcHostExtra);
        Assert.Equal(new[] { "C" }, f.DlcPeerExtra);
        Assert.Equal("authority.npc,authority.loot", RoomContract.StripFlags(missing));
        Assert.Equal(RoomContract.Flags.None, RoomContract.ParseFlags("authority.npc"));
    }

    [Fact]
    public void The_content_sentence_names_what_stays_out_and_how_to_switch_it_off()
    {
        var f = RoomContract.ParseFlags("dlc-peer-extra=MysteriaEcclesiae");
        string s = RoomContract.ContentSentence(f);
        Assert.Contains("MysteriaEcclesiae", s);
        Assert.Contains("Steam", s);
        Assert.Equal("", RoomContract.ContentSentence(RoomContract.Flags.None));
        Assert.Contains("cannot be moved to you", RoomContract.ContentSentence(RoomContract.ParseFlags("dlc-host-extra=X")));
    }

    [Fact]
    public void A_capped_game_keeps_dlc_quests_out_whatever_mp_quest_dlc_says()
    {
        const string dlcQuest = "Barbora.kutnohorsko.navstevaLekare";
        bool shared = Wo137Rules.DlcShared, capped = Wo137Rules.DlcCapped;
        try
        {
            Wo137Rules.DlcShared = true; Wo137Rules.DlcCapped = false;
            Assert.False(Wo137Rules.DlcBlocked(dlcQuest));
            Wo137Rules.DlcCapped = true;                                   // the room says this game has DLC the host lacks
            Assert.True(Wo137Rules.DlcBlocked(dlcQuest));
            Assert.False(Wo137Rules.DlcBlocked("Barbora.kutnohorsko.someBaseQuest"));    // base-game quests are untouched
        }
        finally { Wo137Rules.DlcShared = shared; Wo137Rules.DlcCapped = capped; }
    }
}

public class SharedPauseTests
{
    private static readonly Func<byte, long> Seen = _ => 10_000;

    [Fact]
    public void Another_players_open_menu_holds_this_world_and_closing_it_releases()
    {
        var t = new SharedPauseTracker();
        Assert.False(t.ShouldHold(10_000, Seen));
        t.Note(3, Wo138Codec.ReasonMenu, 9_000);
        Assert.True(t.ShouldHold(10_000, Seen));
        t.Note(3, 0, 11_000);
        Assert.False(t.ShouldHold(11_000, Seen));
    }

    [Fact]
    public void Only_the_menu_counts_never_the_inventory_a_dialogue_or_a_cutscene()
    {
        var t = new SharedPauseTracker();
        foreach (var r in new[] { Wo138Codec.ReasonInventory, Wo138Codec.ReasonDialogue, Wo138Codec.ReasonCutscene, Wo138Codec.ReasonLoad, Wo138Codec.ReasonSkipTime, Wo138Codec.ReasonFrozen, Wo138Codec.ReasonClock })
            t.Note(5, r, 9_000);
        Assert.False(t.ShouldHold(10_000, Seen));
        t.Note(5, (byte)(Wo138Codec.ReasonInventory | Wo138Codec.ReasonMenu), 9_000);       // the menu bit among others still counts
        Assert.True(t.ShouldHold(10_000, Seen));
    }

    [Fact]
    public void A_silent_or_departed_player_cannot_freeze_the_others()
    {
        var t = new SharedPauseTracker();
        t.Note(2, Wo138Codec.ReasonMenu, 1_000);
        Assert.False(t.ShouldHold(20_000, _ => 10_000));          // nothing heard from them for 10 s: their link is gone
        Assert.False(t.ShouldHold(10_000, _ => 0));               // never heard
        Assert.True(t.ShouldHold(10_000, _ => 9_000));
        t.Left(2);
        Assert.False(t.ShouldHold(10_000, _ => 9_000));           // leaving ends the hold at once
    }

    [Fact]
    public void A_menu_left_open_for_a_quarter_of_an_hour_stops_holding()
    {
        var t = new SharedPauseTracker();
        t.Note(2, Wo138Codec.ReasonMenu, 0);
        Assert.True(t.Holding(14 * 60 * 1000, _ => 14 * 60 * 1000).Count == 1);
        Assert.Empty(t.Holding(16 * 60 * 1000, _ => 16 * 60 * 1000));
    }

    [Fact]
    public void Two_players_in_menus_hold_until_both_close_theirs()
    {
        var t = new SharedPauseTracker();
        t.Note(1, Wo138Codec.ReasonMenu, 9_000); t.Note(2, Wo138Codec.ReasonMenu, 9_500);
        t.Note(1, 0, 10_000);
        Assert.True(t.ShouldHold(10_000, Seen));
        t.Note(2, 0, 10_500);
        Assert.False(t.ShouldHold(10_500, Seen));
    }

    [Fact]
    public void The_shared_hold_body_and_the_new_config_flag()
    {
        Assert.Equal(new byte[] { 1, 20, 0 }, Wo138Codec.SharedHoldBody(true, 20));
        Assert.Equal(new byte[] { 0, 0, 0 }, Wo138Codec.SharedHoldBody(false, 0));
        var c = new ClientConfig();
        Assert.True(c.SharedPause);                                  // shared pause is the default
        c.ApplyCommandLine(new[] { "--no-shared-pause" });
        Assert.False(c.SharedPause);
        c.ApplyCommandLine(new[] { "--shared-pause" });
        Assert.True(c.SharedPause);
    }
}
