// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-156: the whole game, not a handful of quests (docs/WO-156-quest-gating.md). Every one of the 201 quest roots the game ships --
/// main, side, task, activity, event, base game and DLC -- is checked against the gating: classified, found by its own State paths
/// (including the quests the game keeps in folders), and entered and left by the state machine the way its tier says.
/// </summary>
public class Wo156Tests
{
    private static readonly QuestRoot[] Cat = QuestCatalog.All;

    private static string RepoFile(string rel)
    {
        string? dir = AppContext.BaseDirectory;
        for (int i = 0; i < 12 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
            if (File.Exists(Path.Combine(dir, rel))) return Path.Combine(dir, rel);
        return "";
    }

    /// <summary>The State path a quest's own nodes have: Barbora.&lt;level&gt;.&lt;folders&gt;.&lt;quest&gt;.&lt;node&gt;.</summary>
    private static string PathOf(QuestRoot q, string node = "node") =>
        "Barbora." + q.Level + "." + (q.Folders.Length > 0 ? q.Folders.Replace('/', '.') + "." : "") + q.Name + "." + node;

    private static List<StoryLock.Transition> Burst(StoryLock l, string path, long at, int n = StoryLock.MixedChanges)
    {
        var all = new List<StoryLock.Transition>();
        for (int i = 0; i < n; i++) all.AddRange(l.Note(path + i, at + i));
        return all;
    }

    /// <summary>How many distinct States a quest needs before the host counts as being in it.</summary>
    private static int Need(QuestRoot q) => q.Kind == "main" && q.Tier == "rails" ? 1 : q.Kind != "main" && q.Tier == "rails" ? StoryLock.SideRailsChanges : StoryLock.MixedChanges;

    private static List<StoryLock.Transition> EnterBy(StoryLock l, QuestRoot q, long at) => Burst(l, PathOf(q, "node"), at, Need(q));

    // ================================================================ the catalog

    [Fact]
    public void The_catalog_is_every_quest_root_of_the_game()
    {
        Assert.Equal(201, Cat.Length);
        Assert.Equal(32, Cat.Count(q => q.Kind == "main"));
        Assert.Equal(59, Cat.Count(q => q.Kind == "side"));
        Assert.Equal(52, Cat.Count(q => q.Kind == "task"));
        Assert.Equal(57, Cat.Count(q => q.Kind == "activity"));
        Assert.Equal(1, Cat.Count(q => q.Kind == "event"));
        Assert.Equal(Cat.Length, Cat.Select(q => q.Code).Distinct().Count());
    }

    [Fact]
    public void Quest_names_are_unique_so_a_State_path_names_one_quest()
    {
        var dup = Cat.GroupBy(q => q.Name, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
        Assert.True(dup.Count == 0, "duplicate quest names: " + string.Join(", ", dup));
    }

    [Fact]
    public void Every_quest_has_a_title_a_known_level_and_a_known_tier()
    {
        foreach (var q in Cat)
        {
            Assert.False(string.IsNullOrWhiteSpace(q.Title), q.Code + " has no title");
            Assert.DoesNotContain('�', q.Title);
            Assert.Contains(q.Level, new[] { "trosecko", "kutnohorsko", "klaster" });
            Assert.Contains(q.Tier, new[] { "open", "mixed", "rails" });
            Assert.Contains(q.Kind, new[] { "main", "side", "task", "activity", "event" });
        }
    }

    [Fact]
    public void The_plan_and_the_catalog_csv_in_the_repo_agree_with_the_generated_table()
    {
        string plan = RepoFile(Path.Combine("docs", "WO-156-quest-gating-plan.csv"));
        string cat = RepoFile(Path.Combine("docs", "WO-156-quest-catalog.csv"));
        if (plan.Length == 0 || cat.Length == 0) return;   // not run from a checkout
        var planRows = File.ReadLines(plan).Skip(1).Select(SplitCsv).Where(c => c.Length >= 5).ToDictionary(c => c[0], c => (Tier: c[2], Period: c[3], Why: c[4]));
        Assert.Equal(Cat.Length, planRows.Count);
        foreach (var q in Cat)
        {
            Assert.True(planRows.TryGetValue(q.Code, out var p), q.Code + " is not in the plan");
            Assert.Equal(p.Tier, q.Tier);
            Assert.Equal(p.Period, q.Period);
            Assert.Equal(p.Why, q.Why);
        }
        var catRows = File.ReadLines(cat).Skip(1).Select(SplitCsv).Where(c => c.Length > 9).ToDictionary(c => c[0], c => c);
        Assert.Equal(Cat.Length, catRows.Count);
        foreach (var q in Cat) Assert.Equal(q.Tier, catRows[q.Code][9]);
    }

    /// <summary>A CSV line with quoted cells (the plan's notes have commas).</summary>
    private static string[] SplitCsv(string line)
    {
        var cells = new List<string>();
        var sb = new System.Text.StringBuilder();
        bool q = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (q) { if (c == '"') { if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; } else q = false; } else sb.Append(c); }
            else if (c == '"') q = true;
            else if (c == ',') { cells.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        cells.Add(sb.ToString());
        return cells.ToArray();
    }

    // ================================================================ every quest, found by its own paths

    [Fact]
    public void Every_quest_that_locks_is_found_by_the_State_paths_of_its_own_nodes()
    {
        foreach (var q in Cat.Where(x => x.Tier != "open"))
        {
            var s = StorySections.FromPath(PathOf(q));
            Assert.True(s is not null, q.Code + " " + q.Title + " was not found from " + PathOf(q));
            Assert.Equal(q.Code, s!.Code);
        }
    }

    [Fact]
    public void An_open_quest_is_never_taken_for_a_section()
    {
        foreach (var q in Cat.Where(x => x.Tier == "open"))
        {
            var s = StorySections.FromPath(PathOf(q));
            if (q.Kind == "main") { Assert.Equal(q.Code, s!.Code); Assert.False(s.Locked); }   // an open main quest is known (it orders the story), never locked
            else Assert.Null(s);
        }
    }

    [Fact]
    public void Every_section_that_locks_is_found_whatever_the_case_of_the_name()
    {
        foreach (var q in Cat.Where(x => x.Tier != "open"))
            Assert.Equal(q.Code, StorySections.FromPath(PathOf(q).ToUpperInvariant().Replace("BARBORA.", "Barbora."))?.Code ?? "-" + q.Code);
    }

    [Fact]
    public void A_quest_in_the_wrong_level_is_not_found()
    {
        foreach (var q in Cat.Where(x => x.Tier != "open"))
        {
            string wrong = q.Level == "trosecko" ? "kutnohorsko" : "trosecko";
            Assert.Null(StorySections.FromPath(PathOf(q with { Level = wrong })));
        }
    }

    // ================================================================ every tier does what it says

    [Fact]
    public void A_rails_quest_is_entered_by_its_first_State_and_a_mixed_one_by_a_burst_and_each_ends_with_its_endQuest()
    {
        foreach (var q in Cat.Where(x => x.Tier != "open" && x.Markers.Length == 0))
        {
            var l = new StoryLock();
            var enter = EnterBy(l, q, 1000);
            Assert.True(enter.Count == 1 && enter[0].Kind == StoryLock.Kind.Enter && enter[0].Section!.Code == q.Code, q.Code + " " + q.Tier + " did not enter");
            var end = l.Note(PathOf(q, "endQuest"), 5000);
            Assert.True(end.Count == 1 && end[0].Kind == StoryLock.Kind.Leave && end[0].Reason == "completed", q.Code + " did not leave on its end");
            Assert.Null(l.Active);
        }
    }

    [Fact]
    public void Only_a_main_rails_quest_is_entered_by_one_State_the_rest_need_evidence()
    {
        foreach (var q in Cat.Where(x => x.Tier != "open" && x.Markers.Length == 0))
        {
            var l = new StoryLock();
            var one = l.Note(PathOf(q), 1);
            Assert.Equal(Need(q) == 1 ? 1 : 0, one.Count);
            if (Need(q) > 1) Assert.Empty(l.Note(PathOf(q), 2));       // the SAME State again is not more evidence
        }
    }

    [Fact]
    public void Every_section_is_left_by_its_idle_time_and_only_after_it()
    {
        foreach (var q in Cat.Where(x => x.Tier != "open" && x.Markers.Length == 0))
        {
            var l = new StoryLock();
            EnterBy(l, q, 0);
            int idle = l.Active!.IdleMs;
            Assert.True(idle >= 8 * 60 * 1000);
            Assert.Null(l.Tick(idle));
            Assert.NotNull(l.Tick(idle + 3000));
            Assert.Null(l.Active);
        }
    }

    // ================================================================ the plan against what the sources say

    [Theory]
    [InlineData("M05")] [InlineData("M06")]                                            // the wedding in Semine, the castle at Trosky
    [InlineData("M12")]                                                                 // Storm
    [InlineData("M32")] [InlineData("M44b")]                                            // the two Dry Devil jobs
    [InlineData("M42")]                                                                 // the burning of the Jewish quarter
    [InlineData("M45")] [InlineData("M46")] [InlineData("M47")] [InlineData("M48a")]    // the cardinal, the Italian Job, the final set
    [InlineData("M48b")] [InlineData("M48c")] [InlineData("M49")] [InlineData("M50")] [InlineData("M51")]
    public void The_missions_the_user_named_lock(string code) => Assert.True(StorySections.ByCode(code)!.Locked);

    [Fact]
    public void The_Devils_Pack_hub_and_the_free_Trosky_and_arrival_quests_are_open()
    {
        foreach (var c in new[] { "M07", "M31", "M38" }) Assert.False(StorySections.ByCode(c)!.Locked, c);
    }

    [Fact]
    public void Every_quest_where_the_player_is_someone_else_locks()
    {
        // the census counts "player switch" nodes (the controlled character changes: Godwin)
        foreach (var q in Cat.Where(x => x.Switch > 0)) Assert.NotEqual("open", q.Tier);
    }

    [Fact]
    public void No_open_side_quest_hides_heavy_scripted_content()
    {
        // escorts and teleports are the census's marks of a scripted journey; an open quest with a lot of them is a mistake in the plan
        var bad = Cat.Where(q => q.Kind != "main" && q.Tier == "open" && (q.Escort >= 4 || q.Teleport >= 10 || q.Switch > 0)).Select(q => q.Code + " " + q.Title).ToList();
        Assert.True(bad.Count == 0, "open but scripted: " + string.Join("; ", bad));
    }

    [Fact]
    public void Periods_of_the_main_story_are_runs_of_the_games_own_order()
    {
        var order = StorySections.All.Select(s => s.Code).ToList();
        foreach (var p in StorySections.Periods.Where(x => order.Contains(x.Codes[0])))
        {
            var idx = p.Codes.Select(c => order.IndexOf(c)).ToList();
            Assert.Equal(idx.OrderBy(i => i).ToList(), idx);
            for (int i = 1; i < idx.Count; i++) Assert.Equal(idx[i - 1] + 1, idx[i]);
        }
    }

    [Fact]
    public void The_final_act_is_one_period_from_Oratores_to_the_last_quest()
    {
        var p = StorySections.PeriodOf("M45")!;
        Assert.Equal(new[] { "M45", "M46", "M47", "M48a", "M48b", "M48c", "M49", "M50", "M51" }, p.Codes);
        Assert.All(p.Codes, c => Assert.Equal(StoryTier.Rails, StorySections.ByCode(c)!.Tier));
        Assert.Contains("dobyvani_ruthardky", StorySections.ByCode("M45")!.Markers);
    }

    [Fact]
    public void The_Nebakov_campaign_is_one_period_of_four_quests()
    {
        var p = StorySections.PeriodOf("M10")!;
        Assert.Equal(new[] { "M09", "M10", "M11", "M12" }, p.Codes);
    }

    [Fact]
    public void Every_period_has_one_name_and_starts_at_its_own_first_section()
    {
        foreach (var p in StorySections.Periods)
        {
            Assert.Equal(p.Id, p.Codes[0]);
            Assert.Single(p.Codes.Select(c => StorySections.ByCode(c)!.Why).Distinct());
            Assert.NotEmpty(p.Why);
        }
    }

    // ================================================================ the whole story, in order

    private static readonly string[] MainQuestsInStoryOrder =
        { "M30", "M01", "M02", "M03", "M05", "M06", "M07", "M08", "M09", "M10", "M11", "M12", "M31", "M32", "M38", "M33", "M34", "M35",
          "M37a", "M37b", "M42", "M44a", "M44b", "M45", "M46", "M47", "M48a", "M48b", "M48c", "M49", "M50", "M51" };

    [Fact]
    public void A_whole_playthrough_asks_once_per_stretch_and_never_for_the_open_quests()
    {
        var l = new StoryLock();
        var roster = new RailsRoster();
        var asked = new List<string>();
        long now = 1000;
        var visited = new List<string>();
        foreach (var code in MainQuestsInStoryOrder)
        {
            var s = StorySections.ByCode(code)!;
            string path = s.Markers.Count > 0 ? $"Barbora.{s.Level}.{s.Quest}.{s.Markers[0]}.node" : $"Barbora.{s.Level}.{s.Quest}.node";
            var ts = s.Tier == StoryTier.Mixed ? Burst(l, path, now) : l.Note(path, now).ToList();   // an open quest gets one State: the story has not moved on for it
            now += 1000;
            // the later quests' background States would also tick; an earlier quest's must change nothing
            foreach (var t in ts)
            {
                if (t.Kind != StoryLock.Kind.Enter) continue;
                var period = StorySections.PeriodOf(t.Section!.Code)!;
                if (roster.Start(period.Id)) asked.Add(period.Id);
                visited.Add(t.Section.Code);
            }
            // a quest that ends: its end node leaves it (the roster keeps the period through the grace, so the next quest of it is not asked again)
            var end = l.Note($"Barbora.{s.Level}.{s.Quest}.endQuest", now);
            now += 500;
            if (end.Count > 0) roster.EndSoon(now);
        }
        // every locked main quest was entered, in the game's order
        Assert.Equal(MainQuestsInStoryOrder.Where(c => StorySections.ByCode(c)!.Locked).ToArray(), visited.ToArray());
        // one question per stretch: the main periods, once each, in order; nothing for the open quests
        Assert.Equal(new[] { "M30", "M01", "M05", "M08", "M09", "M32", "M33", "M34", "M35", "M37a", "M42", "M44a", "M44b", "M45" }, asked.ToArray());
        Assert.DoesNotContain("M07", visited);
        Assert.DoesNotContain("M31", visited);
        Assert.DoesNotContain("M38", visited);
    }

    [Fact]
    public void The_quests_names_are_the_games_own_titles_for_the_ones_the_user_listed()
    {
        Assert.Equal("Wedding Crashers", StorySections.ByCode("M05")!.Title);
        Assert.Equal("Speak of the Devil", StorySections.ByCode("M32")!.Title);
        Assert.Equal("Dancing with the Devil", StorySections.ByCode("M44b")!.Title);
        Assert.Equal("The Devil's Pack", StorySections.ByCode("M38")!.Title);
        Assert.Equal("Oratores", StorySections.ByCode("M45")!.Title);
        Assert.Equal("The Italian Job", StorySections.ByCode("M46")!.Title);
        Assert.Equal("Judgement Day", StorySections.ByCode("M51")!.Title);
        Assert.Equal("Exodus", StorySections.ByCode("M42")!.Title);
        Assert.Equal("Kuttenberg Tournament", StorySections.ByCode("A30")!.Title);
    }

    [Fact]
    public void The_beat_for_every_section_and_period_is_a_plain_token_the_wire_accepts()
    {
        foreach (var s in StorySections.Everything.Where(x => x.Locked))
        {
            Assert.Matches("^[A-Za-z0-9]+$", s.Code);
            Assert.True(RailsRules.TryParseChoice(RailsRules.ChoiceText(s.Code, RailsChoice.Free), out var p, out _) && p.Id == s.Period, s.Code);
        }
    }
}

/// <summary>WO-156: a quest nested inside another quest's module (found by the whole-game pass).</summary>
public class Wo156NestingTests
{
    [Fact]
    public void A_task_nested_in_a_fight_clubs_folder_is_the_task_not_the_fight_club()
    {
        Assert.Equal("A36", StorySections.FromPath("Barbora.kutnohorsko.fight_clubs.fightClubHorany.fightClubHorany.x")!.Code);
        Assert.Equal("A36", StorySections.FromPath("Barbora.kutnohorsko.fight_clubs.fightClubHorany.x")!.Code);
        Assert.Null(StorySections.FromPath("Barbora.kutnohorsko.fight_clubs.fightClubHorany.sanceProBerusku.x"));   // U56 "A Moment of Fame": open
    }

    [Fact]
    public void A_nested_task_never_enters_or_refreshes_its_parents_section()
    {
        var l = new StoryLock();
        l.Note("Barbora.kutnohorsko.fight_clubs.fightClubHorany.a", 0);
        Assert.Single(l.Note("Barbora.kutnohorsko.fight_clubs.fightClubHorany.b", 1000));                    // the fight club bout (two distinct States)
        Assert.Equal(15 * 60 * 1000, l.Active!.IdleMs);
        l.Note("Barbora.kutnohorsko.fight_clubs.fightClubHorany.sanceProBerusku.x", 14 * 60 * 1000);       // the open task: no refresh
        Assert.NotNull(l.Tick(15 * 60 * 1000 + 1001));
    }
}
