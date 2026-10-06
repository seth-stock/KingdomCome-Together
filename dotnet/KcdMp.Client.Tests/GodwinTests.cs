// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using KcdMp.Wire;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-157: the prologue and the other stretches of the story where the player is Godwin (player_bohuta). (synthetic) -- the splice, the
/// check and the stored block for a Godwin character, and the guards that keep Henry and Godwin apart. What this cannot prove is that the
/// ENGINE loads a spliced Godwin world and plays it with two players (docs/WO-157-findings.md says what was and was not run).
/// </summary>
public class GodwinTests
{
    private static (byte[] Host, byte[] Joiner) Godwins()
    {
        var (host, join) = WhsSaveTests.Pair();
        host.GodwinRich = true; host.Seed = 0xAAAA; host.World = "prologue-host"; host.Story = 0;
        join.GodwinRich = true; join.Seed = 0xBBBB; join.World = "prologue-guest";
        return (WhsSaveTests.File(host), WhsSaveTests.File(join));
    }

    [Fact]
    public void A_godwin_save_is_recognised_as_godwin_not_henry()
    {
        var (host, _) = Godwins();
        var who = WhsSave.PlayerOf(WhsSave.Inflate(host).Raw);
        Assert.True(who.IsGodwin);
        Assert.False(who.IsHenry);
        Assert.True(who.IsKnown);
        Assert.Equal(WhsSave.BohutaSoul, who.Soul);
        Assert.Equal("player_bohuta", who.Player);
    }

    [Fact]
    public void A_henry_save_is_still_henry_and_a_thin_godwin_save_is_still_known()
    {
        var (h, _) = WhsSaveTests.Pair();
        var henry = WhsSave.PlayerOf(WhsSave.Inflate(WhsSaveTests.File(h)).Raw);
        Assert.True(henry.IsHenry); Assert.False(henry.IsGodwin); Assert.Equal(WhsSave.HenrySoul, henry.Soul);
        var thin = new WhsSaveTests.Spec { Bohuta = true };   // the old fixture: Godwin bound, only a name on his record
        var t = WhsSave.PlayerOf(WhsSave.Inflate(WhsSaveTests.File(thin)).Raw);
        Assert.True(t.IsGodwin); Assert.False(t.IsHenry);
    }

    [Fact]
    public void The_joiners_godwin_goes_into_the_hosts_prologue_and_everything_else_stays_the_hosts()
    {
        var (host, joiner) = Godwins();
        var parts = WhsSave.PartsFromFile(joiner, WhsSave.HenryParts.OriginSave);
        Assert.Equal(WhsSave.BohutaSoul, parts.Soul);

        var res = WhsSave.SpliceParts(host, parts, WhsSaveTests.Quest, WhsSave.QuestItemMode.Strip);
        var fails = WhsSave.CheckParts(host, parts, res.File, WhsSaveTests.Quest, WhsSave.QuestItemMode.Strip);
        Assert.Empty(fails);
        Assert.True(WhsSave.Verify(res.File).Ok);

        // the player is still Godwin, now the joiner's Godwin
        var outRaw = WhsSave.Inflate(res.File).Raw;
        Assert.True(WhsSave.PlayerOf(outRaw).IsGodwin);
        var godwin = WhsSave.DecodePlayerSoul(outRaw, WhsSave.FindSoul(outRaw, WhsSave.BohutaSoul)!.Value);
        var joinRaw = WhsSave.Inflate(joiner).Raw;
        var joinGodwin = WhsSave.DecodePlayerSoul(joinRaw, WhsSave.FindSoul(joinRaw, WhsSave.BohutaSoul)!.Value);
        Assert.Equal(joinGodwin.Inventory.Where(i => !WhsSaveTests.Quest.ContainsKey(i.Class)).ToList(), godwin.Inventory);   // minus quest items
        // Henry (an ordinary soul in this world) is exactly the host's
        var hostRaw = WhsSave.Inflate(host).Raw;
        Assert.True(WhsSave.NodeBytes(hostRaw, WhsSave.FindSoul(hostRaw, WhsSave.HenrySoul)!.Value)
            .AsSpan().SequenceEqual(WhsSave.NodeBytes(outRaw, WhsSave.FindSoul(outRaw, WhsSave.HenrySoul)!.Value)));
        Assert.Equal(WhsSave.ReadSeed(hostRaw), WhsSave.ReadSeed(outRaw));   // still the host's world
    }

    [Fact]
    public void The_stored_block_of_a_godwin_remembers_whose_record_it_is()
    {
        var (_, joiner) = Godwins();
        var parts = WhsSave.PartsFromFile(joiner, WhsSave.HenryParts.OriginSnapshot);
        var back = WhsSave.ParseBlock(WhsSave.SerializeBlock(parts));
        Assert.Equal(WhsSave.BohutaSoul, back.Soul);
        Assert.Empty(WhsSave.DiffBlocks(parts, back));
    }

    [Fact]
    public void A_henry_is_never_spliced_into_a_godwin_world()
    {
        var (host, _) = Godwins();
        var (h, _) = WhsSaveTests.Pair();
        h.Seed = 0xCCCC;
        var henryParts = WhsSave.PartsFromFile(WhsSaveTests.File(h), WhsSave.HenryParts.OriginSave);
        var ex = Assert.Throws<InvalidDataException>(() => WhsSave.SpliceParts(host, henryParts, WhsSaveTests.Quest, WhsSave.QuestItemMode.Strip));
        Assert.Contains("player_bohuta", ex.Message);
    }

    [Fact]
    public void A_godwin_is_never_spliced_into_a_henry_world()
    {
        var (_, godwinJoiner) = Godwins();
        var (h, _) = WhsSaveTests.Pair();
        var ex = Assert.Throws<InvalidDataException>(() => WhsSave.SpliceParts(WhsSaveTests.File(h), WhsSave.PartsFromFile(godwinJoiner, WhsSave.HenryParts.OriginSave), WhsSaveTests.Quest, WhsSave.QuestItemMode.Strip));
        Assert.Contains("player_henry", ex.Message);
    }

    [Fact]
    public void Sending_a_godwin_home_into_a_henry_world_is_refused_with_a_reason()
    {
        var (_, godwinJoiner) = Godwins();
        var (h, _) = WhsSaveTests.Pair();
        var parts = WhsSave.PartsFromFile(godwinJoiner, WhsSave.HenryParts.OriginSnapshot);
        var ex = Assert.Throws<InvalidDataException>(() => HenryHome.Build(WhsSaveTests.File(h), parts, WhsSaveTests.Quest, 5, DateTimeOffset.UtcNow));
        Assert.Contains("not the character being sent home", ex.Message);
    }

    [Fact]
    public void The_wire_flags_are_distinct_bits_so_an_older_peer_reads_a_godwin_world_as_not_henry()
    {
        Assert.Equal(0, Protocol.SessionSeedKnown & Protocol.SessionGodwinWorld);
        Assert.Equal(0, Protocol.SessionHenryWorld & Protocol.SessionGodwinWorld);
        ushort flags = (ushort)(Protocol.SessionSeedKnown | Protocol.SessionGodwinWorld);
        // what a WO-125 peer computes: only its two known bits
        Assert.True((flags & Protocol.SessionSeedKnown) != 0);
        Assert.False((flags & Protocol.SessionHenryWorld) != 0);
    }
}
