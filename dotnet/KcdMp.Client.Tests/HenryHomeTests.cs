// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-157: sending a Henry home (HenryHome.cs). (synthetic) -- saves built in WhsSaveTests; no game and no real save. What this cannot
/// prove is that the ENGINE loads the result as an ordinary manual save; that is the live check in docs/WO-157-findings.md.
/// </summary>
public class HenryHomeTests
{
    private static readonly DateTimeOffset When = new(2026, 10, 6, 14, 30, 0, TimeSpan.Zero);

    // The shared world's Henry (the "joiner" spec) goes into the player's own world (the "host" spec, here a different playthrough).
    private static (byte[] Home, byte[] Shared, WhsSave.HenryParts Parts) Fixture()
    {
        var (home, shared) = WhsSaveTests.Pair();
        home.World = "my-own-world"; home.Seed = 0xAAAA; home.SaveTime = 1778303843;   // real headers always carry a SaveTime
        shared.World = "friends-world"; shared.Seed = 0xBBBB;
        var homeFile = WhsSaveTests.File(home);
        var sharedFile = WhsSaveTests.File(shared);
        return (homeFile, sharedFile, WhsSave.PartsFromFile(sharedFile, WhsSave.HenryParts.OriginSnapshot));
    }

    private static WhsSave.PlayerSoul HenryOf(byte[] file)
    {
        var raw = WhsSave.Inflate(file).Raw;
        return WhsSave.DecodePlayerSoul(raw, WhsSave.FindSoul(raw, WhsSave.HenrySoul)!.Value);
    }

    [Fact]
    public void The_home_world_gets_the_progressed_henry_and_keeps_its_own_story()
    {
        var (home, shared, parts) = Fixture();
        var b = HenryHome.Build(home, parts, WhsSaveTests.Quest, 60, When, TimeZoneInfo.Utc);
        Assert.Empty(b.Check);
        Assert.True(WhsSave.Verify(b.File).Ok);

        var before = HenryOf(home);
        var after = HenryOf(b.File);
        var sharedHenry = HenryOf(shared);

        // the Henry of the shared world: his stats and skills, his money and goods
        Assert.NotEqual(before.StatXp.Where(kv => kv.Key != "storyProgress").OrderBy(kv => kv.Key).Select(kv => kv.Value),
                        after.StatXp.Where(kv => kv.Key != "storyProgress").OrderBy(kv => kv.Key).Select(kv => kv.Value));
        Assert.Equal(sharedHenry.StatXp.Where(kv => kv.Key != "storyProgress").OrderBy(kv => kv.Key).Select(kv => kv.Value),
                     after.StatXp.Where(kv => kv.Key != "storyProgress").OrderBy(kv => kv.Key).Select(kv => kv.Value));

        // the HOME world's story progress (never the shared world's)
        Assert.Equal(before.StatXp["storyProgress"], after.StatXp["storyProgress"]);
        Assert.NotEqual(sharedHenry.StatXp["storyProgress"], after.StatXp["storyProgress"]);
    }

    [Fact]
    public void Shared_world_quest_items_stay_there_and_the_home_henrys_own_stay_home()
    {
        var (home, shared, parts) = Fixture();
        var b = HenryHome.Build(home, parts, WhsSaveTests.Quest, 60, When, TimeZoneInfo.Utc);
        var after = HenryOf(b.File);
        var homeBefore = HenryOf(home);
        var sharedHenry = HenryOf(shared);

        var qClasses = WhsSaveTests.Quest.Keys.ToHashSet();
        var sharedQuestInstances = sharedHenry.Inventory.Where(i => qClasses.Contains(i.Class)).Select(i => i.Instance).ToHashSet();
        var homeQuestItems = homeBefore.Inventory.Where(i => qClasses.Contains(i.Class)).ToList();

        Assert.NotEmpty(sharedQuestInstances);   // the fixture does carry one
        Assert.NotEmpty(homeQuestItems);
        Assert.DoesNotContain(after.Inventory, i => sharedQuestInstances.Contains(i.Instance));   // the shared world's quest item did not come
        foreach (var q in homeQuestItems) Assert.Contains(q, after.Inventory);                    // the home world's did not leave
        // the ordinary goods (an apple stack, money) did come
        Assert.Contains(after.Inventory, i => i.Instance == "20000000-0000-0000-0000-000000000001");
        Assert.Contains(after.Inventory, i => i.Instance == "20000000-0000-0000-0000-000000000003");
    }

    [Fact]
    public void Every_block_of_the_home_world_other_than_henrys_is_untouched()
    {
        var (home, _, parts) = Fixture();
        var b = HenryHome.Build(home, parts, WhsSaveTests.Quest, 60, When, TimeZoneInfo.Utc);
        // CheckParts compares every leaf of the stream outside the spliced blocks byte for byte; Build returns its failures
        Assert.Empty(b.Check);
        var h = WhsSave.Inflate(home); var o = WhsSave.Inflate(b.File);
        Assert.Equal(WhsSave.ReadSeed(h.Raw), WhsSave.ReadSeed(o.Raw));   // still the home playthrough, not the shared one
        Assert.Equal(h.Raw.Length > 0, o.Raw.Length > 0);
    }

    [Fact]
    public void The_header_reads_as_a_new_manual_save_of_the_same_world()
    {
        var (home, _, parts) = Fixture();
        var b = HenryHome.Build(home, parts, WhsSaveTests.Quest, 60, When, TimeZoneInfo.Utc);
        string desc = WhsSave.Inflate(b.File).Desc;
        Assert.Contains("SaveType=\"ManualSave\"", desc);
        Assert.Contains("SaveId=\"60\"", desc);
        Assert.Contains($"SaveTime=\"{When.ToUnixTimeSeconds()}\"", desc);
        Assert.Contains("LevelName=\"trosecko\"", desc);   // unchanged
        Assert.Equal("save060", b.Name);
    }

    [Fact]
    public void A_real_shaped_header_is_rewritten_field_by_field()
    {
        const string hdr = "<C_SaveGameDescription FormatVersion=\"0\" SaveType=\"PermanentSave\" SaveId=\"526\" SaveTime=\"1778303843\" LevelName=\"kutnohorsko\" PlayerId=\"0\" " +
            "UIDescription=\"0|526|@qname_x|@obj_y|location_kutnaHora|1778303843|08/05/2026 23:17|64.443764|\" QuestNameOverride=\"@qname_x|@obj_y\" BuildInfo=\"1.5.5-15315-release_1_5\" GameMode=\"normal\">\n</C_SaveGameDescription>";
        var local = TimeZoneInfo.CreateCustomTimeZone("t", TimeSpan.FromHours(-6), "t", "t");
        string o = Encoding.UTF8.GetString(HenryHome.RewriteDescription(Encoding.UTF8.GetBytes(hdr), 600, When, local));
        Assert.Contains("SaveType=\"ManualSave\"", o);
        Assert.Contains("SaveId=\"600\"", o);
        Assert.Contains($"SaveTime=\"{When.ToUnixTimeSeconds()}\"", o);
        Assert.Contains($"UIDescription=\"2|600|@qname_x|@obj_y|location_kutnaHora|{When.ToUnixTimeSeconds()}|06/10/2026 08:30|64.443764|\"", o);   // the local clock, the home world's play time and quest
        Assert.Contains("QuestNameOverride=\"@qname_x|@obj_y\"", o);   // the quest label stays
        Assert.Contains("BuildInfo=\"1.5.5-15315-release_1_5\"", o);
    }

    [Fact]
    public void A_header_of_a_shape_it_does_not_know_is_left_alone_not_mangled()
    {
        string o = Encoding.UTF8.GetString(HenryHome.RewriteDescription(Encoding.UTF8.GetBytes("<S SaveType=\"QuickSave\" SaveId=\"1\" UIDescription=\"a|b\" X=\"y\"/>"), 9, When, TimeZoneInfo.Utc));
        Assert.Contains("UIDescription=\"a|b\"", o);
        Assert.Contains("SaveId=\"9\"", o);
    }

    [Theory]
    [InlineData(new[] { "autosave527.whs", "permanent526.whs", "save543.whs" }, new int[0], 544)]
    [InlineData(new[] { "autosave527.whs", "exit.whs" }, new[] { 582 }, 583)]       // exit.whs has no number; its header does
    [InlineData(new string[0], new int[0], 1)]
    [InlineData(new[] { "quicksave099.whs", "SAVE100.WHS" }, new int[0], 101)]       // case does not matter; quicksaves share the counter
    public void The_next_id_follows_the_engines_single_counter(string[] names, int[] headerIds, int expect) =>
        Assert.Equal(expect, HenryHome.NextSaveId(names, headerIds));

    [Fact]
    public void Names_are_the_engines_three_digit_form() { Assert.Equal("save059", HenryHome.ManualName(59)); Assert.Equal("save1000", HenryHome.ManualName(1000)); }

    [Fact]
    public void A_home_save_that_is_not_a_henry_world_is_refused_with_a_reason()
    {
        var (home, shared) = WhsSaveTests.Pair();
        home.Bohuta = true; home.Seed = 0xAAAA; shared.Seed = 0xBBBB;
        var parts = WhsSave.PartsFromFile(WhsSaveTests.File(shared), WhsSave.HenryParts.OriginSnapshot);
        var ex = Assert.Throws<InvalidDataException>(() => HenryHome.Build(WhsSaveTests.File(home), parts, WhsSaveTests.Quest, 1, When));
        Assert.Contains("not a Henry world", ex.Message);
    }

    [Fact]
    public void A_damaged_home_save_is_refused_and_nothing_is_built()
    {
        var (home, _, parts) = Fixture();
        home[home.Length / 2] ^= 0xFF;
        var ex = Assert.Throws<InvalidDataException>(() => HenryHome.Build(home, parts, WhsSaveTests.Quest, 1, When));
        Assert.Contains("does not verify", ex.Message);
    }

    [Fact]
    public void Saves_of_different_game_builds_are_refused()
    {
        var (h, s) = WhsSaveTests.Pair();
        h.Build = "1.5.5-release_1_5"; s.Build = "1.5.6-release_1_5";
        var parts = WhsSave.PartsFromFile(WhsSaveTests.File(s), WhsSave.HenryParts.OriginSnapshot);
        var ex = Assert.Throws<InvalidDataException>(() => HenryHome.Build(WhsSaveTests.File(h), parts, WhsSaveTests.Quest, 1, When));
        Assert.Contains("builds differ", ex.Message);
    }

    [Fact]
    public void The_home_file_itself_is_never_modified()
    {
        var (home, _, parts) = Fixture();
        var copy = (byte[])home.Clone();
        HenryHome.Build(home, parts, WhsSaveTests.Quest, 60, When, TimeZoneInfo.Utc);
        Assert.Equal(copy, home);
    }

    [Fact]
    public void The_home_world_of_a_shared_world_is_remembered_by_tag_and_survives_a_restart()
    {
        string root = Path.Combine(Path.GetTempPath(), "kcdmp-home-" + Guid.NewGuid().ToString("N")[..8]);
        try
        {
            var store = new HenryStore(root, log: _ => { });
            Assert.Equal((null, null), store.HomeOf("0123456789"));
            store.SetHome("0123456789", "abcdef0123", "playline1/save021");
            var again = new HenryStore(root, log: _ => { });            // a new agent run
            Assert.Equal(("abcdef0123", "playline1/save021"), again.HomeOf("0123456789"));
            Assert.Equal("0123456789", again.MostRecentWorld());
            store.SetHome("0123456789", "ffffffffff", "playline2/save007");   // a later bring replaces it
            Assert.Equal("ffffffffff", again.HomeOf("0123456789").HomeTag);
            Assert.Throws<ArgumentException>(() => again.HomeOf("not-a-tag"));
        }
        finally { try { Directory.Delete(root, true); } catch { } }
    }

    [Fact]
    public void A_world_copy_is_the_same_world_with_a_new_manual_save_header()
    {
        // what mp_world_copy writes: the shared world's own save, only its header turned into a manual save's
        var (_, shared, _) = Fixture();
        var c = WhsSave.Inflate(shared);
        byte[] copy = WhsSave.Deflate(HenryHome.RewriteDescription(c.DescBytes, 1, When, TimeZoneInfo.Utc), c.Raw, c.FooterTail);
        Assert.True(WhsSave.Verify(copy).Ok);
        var o = WhsSave.Inflate(copy);
        Assert.Equal(c.Raw, o.Raw);   // the whole world, byte for byte
        Assert.Contains("SaveType=\"ManualSave\"", o.Desc);
        Assert.Contains("SaveId=\"1\"", o.Desc);
        Assert.Equal("save001", HenryHome.ManualName(1));
        Assert.Equal(WhsSave.ReadSeed(c.Raw), WhsSave.ReadSeed(o.Raw));   // still the host's playthrough (it is a copy of THAT world)
    }
}
