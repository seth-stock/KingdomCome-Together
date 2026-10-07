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
