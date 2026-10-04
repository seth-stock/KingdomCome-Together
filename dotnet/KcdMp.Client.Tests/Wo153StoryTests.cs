// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-153/156: the story sections (the table, built from the game's own quest catalog and the reviewed plan) and the state machine that
/// reads the host's States. Wo156Tests covers the catalog as a whole (all 201 quests); this is the machine, on representative quests.
/// </summary>
public class Wo153StoryTests
{
    // M05 svatba is MIXED (a burst of States is needed to enter); M08 mucirna, M12 vezniNaTroskach and M44b utokNaMalesov are RAILS
    // (the first State enters); M07, M31 and M38 are OPEN.
    private const string Wed = "Barbora.trosecko.svatba.hibernovana_cast.x";
    private const string Raid = "Barbora.trosecko.mucirna.x";                       // M08, rails
    private const string Storm = "Barbora.trosecko.vezniNaTroskach.x";              // M12, rails
    private const string Assault = "Barbora.kutnohorsko.utokNaMalesov.x";           // M44b, rails

    /// <summary>Every transition a burst of DISTINCT States of one quest produced, in order.</summary>
    private static List<StoryLock.Transition> Burst(StoryLock l, string path, long at)
    {
        var all = new List<StoryLock.Transition>();
        for (int i = 0; i < StoryLock.MixedChanges; i++) all.AddRange(l.Note(path + i, at + i));
        return all;
    }

    // ---- the table ----

    [Fact]
    public void The_table_is_the_32_main_quests_in_the_games_own_order()
    {
        Assert.Equal(32, StorySections.All.Count);
        Assert.Equal(new[] { "M30", "M01", "M02", "M03", "M05", "M06", "M07", "M08", "M09", "M10", "M11", "M12", "M31", "M32", "M38", "M33", "M34", "M35",
                             "M37a", "M37b", "M42", "M44a", "M44b", "M45", "M46", "M47", "M48a", "M48b", "M48c", "M49", "M50", "M51" },
                     StorySections.All.Select(s => s.Code).ToArray());
        Assert.Equal(StorySections.All.Count, StorySections.All.Select(s => s.Quest).Distinct(StringComparer.OrdinalIgnoreCase).Count());
        for (int i = 0; i < StorySections.All.Count; i++) Assert.Equal(i, StorySections.All[i].Order);
    }

    [Fact]
    public void The_table_matches_the_registry_csv_in_the_repo_when_it_is_there()
    {
        // The registry (docs/WO-94-mainquest-registry.csv) is the source of the codes and quest names.
        string? dir = AppContext.BaseDirectory;
        string? csv = null;
        for (int i = 0; i < 10 && dir is not null; i++, dir = Path.GetDirectoryName(dir))
            if (File.Exists(Path.Combine(dir, "docs", "WO-94-mainquest-registry.csv"))) { csv = Path.Combine(dir, "docs", "WO-94-mainquest-registry.csv"); break; }
        if (csv is null) return;   // not run from a checkout
        var pairs = File.ReadLines(csv).Skip(1).Select(l => l.Split(',')).Where(c => c.Length > 2)
            .Select(c => (Code: c[0].Trim('"', '﻿'), Quest: c[1].Trim('"'), Level: c[2].Trim('"'))).Distinct().ToList();
        Assert.True(pairs.Count >= 30, "registry rows: " + pairs.Count);
        foreach (var (code, quest, level) in pairs)
        {
            var s = StorySections.ByCode(code);
            Assert.NotNull(s);
            Assert.Equal(quest, s!.Quest);
            Assert.Equal(level, s.Level);
        }
    }

    [Theory]
    [InlineData("M30", StoryTier.Rails, "the prologue, played as Godwin")]
    [InlineData("M01", StoryTier.Mixed, "the opening: Easy Riders, Fortuna and Laboratores")]
    [InlineData("M02", StoryTier.Mixed, "the opening: Easy Riders, Fortuna and Laboratores")]
    [InlineData("M03", StoryTier.Mixed, "the opening: Easy Riders, Fortuna and Laboratores")]
    [InlineData("M05", StoryTier.Mixed, "the wedding in Semine and Trosky castle")]
    [InlineData("M06", StoryTier.Mixed, "the wedding in Semine and Trosky castle")]
    [InlineData("M08", StoryTier.Rails, "the raid on Semine")]
    [InlineData("M09", StoryTier.Mixed, "the Nebakov campaign: For Victory!, Divine Messenger, The Finger of God and Storm")]
    [InlineData("M12", StoryTier.Rails, "the Nebakov campaign: For Victory!, Divine Messenger, The Finger of God and Storm")]
    [InlineData("M32", StoryTier.Mixed, "the Dry Devil's rescue: Speak of the Devil")]   // the first Dry Devil job
    [InlineData("M44b", StoryTier.Rails, "the Dry Devil's assault on Malesov: Dancing with the Devil")]   // the second
    [InlineData("M42", StoryTier.Rails, "the burning of the Jewish quarter")]
    [InlineData("M45", StoryTier.Rails, "the final act: from the cardinal's ambush to the end of the game")]
    [InlineData("M46", StoryTier.Rails, "the final act: from the cardinal's ambush to the end of the game")]
    [InlineData("M51", StoryTier.Rails, "the final act: from the cardinal's ambush to the end of the game")]
    public void The_main_quests_that_lock_say_so_in_the_plans_words(string code, StoryTier tier, string why)
    {
        var s = StorySections.ByCode(code)!;
        Assert.True(s.Locked);
        Assert.Equal(tier, s.Tier);
        Assert.Equal(why, s.Why);
    }

    [Theory]
    [InlineData("M07")] [InlineData("M31")] [InlineData("M38")]
    public void The_open_main_quests_are_not_locked(string code)
    {
        var s = StorySections.ByCode(code)!;
        Assert.False(s.Locked);
        Assert.Equal(StoryTier.Open, s.Tier);
        Assert.Equal("", s.Why);
        Assert.Equal("", s.Period);
    }

    [Fact]
    public void The_Devils_Pack_is_an_open_hub_not_a_dry_devil_job() => Assert.False(StorySections.ByCode("M38")!.Locked);

    [Fact]
    public void Every_locked_section_says_why_and_has_a_period_and_no_open_one_does()
    {
        foreach (var s in StorySections.Everything)
        {
            Assert.Equal(s.Locked, s.Why.Length > 0);
            Assert.Equal(s.Locked, s.Period.Length > 0);
        }
    }

    // ---- reading a path ----

    [Theory]
    [InlineData("Barbora.trosecko.svatba.hibernovana_cast.x", "M05")]
    [InlineData("Barbora.trosecko.svatba", "M05")]
    [InlineData("Barbora.kutnohorsko.pogrom.a.b.c", "M42")]
    [InlineData("Barbora.kutnohorsko.PAPEZSKYLEGAT.x", "M45")]       // case-insensitive quest name
    [InlineData("Barbora.kutnohorsko.nebakovPruzkum.x", null)]       // right name, wrong level
    public void A_quest_path_names_its_quest(string path, string? code) => Assert.Equal(code, StorySections.FromPath(path)?.Code);

    [Theory]
    [InlineData("Barbora.trosecko.fight_clubs.podseminsky_fight_club.fightClubPodsemin.x", "A08")]     // a quest kept in folders
    [InlineData("Barbora.kutnohorsko.fight_clubs.fightClubKutnaHora.fightClubKutnaHora.x", "A37")]
    [InlineData("Barbora.kutnohorsko.kutnohorskyTurnaj.turnajove_souboje.x", "A30")]
    [InlineData("Barbora.trosecko.zaby.x", "S22")]
    [InlineData("Barbora.kutnohorsko.kovarske_mikroquesty.katuvSleh.x", "U40")]
    public void A_side_quest_task_or_activity_that_locks_is_found_too(string path, string code) => Assert.Equal(code, StorySections.FromPath(path)?.Code);

    [Theory]
    [InlineData("Barbora.kutnohorsko.svatba.x")]                       // right name, wrong level
    [InlineData("Barbora.trosecko.hledaniPsa.x")]                      // an open side quest
    [InlineData("Barbora.trosecko.kovar.x")]                           // an open side quest
    [InlineData("Barbora.open_world.crime_reaction_barks.x")]          // not a quest of the table
    [InlineData("Haste.trosecko.svatba.x")]                            // not under Barbora
    [InlineData("Barbora.trosecko")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_a_section(string? path) => Assert.Null(StorySections.FromPath(path));

    [Fact]
    public void A_main_quests_own_modules_never_read_as_another_quest()
    {
        // svatba has a module "vyhodnoceni_questu_zaby"; a module that merely shares a name with a side quest must not win over the main root
        Assert.Equal("M05", StorySections.FromPath("Barbora.trosecko.svatba.zaby.x")!.Code);
        Assert.Equal("M05", StorySections.FromPath("Barbora.trosecko.svatba.fight_clubs.fightClubPodsemin.x")!.Code);
    }

    [Fact]
    public void An_end_path_is_recognised() =>
        Assert.True(StorySections.IsEndPath("Barbora.trosecko.svatba.endQuest")
                    && StorySections.IsEndPath("Barbora.trosecko.svatba.x.ENDQUEST.y") && !StorySections.IsEndPath(Wed));

    // ---- the state machine: rails, mixed, open ----

    [Fact]
    public void The_first_change_in_a_rails_quest_enters_its_section_once()
    {
        var l = new StoryLock();
        var t = l.Note(Raid, 1000);
        Assert.Single(t);
        Assert.Equal(StoryLock.Kind.Enter, t[0].Kind);
        Assert.Equal("M08", t[0].Section!.Code);
        Assert.Empty(l.Note(Raid, 2000));          // more changes in it: nothing new
        Assert.Equal("M08", l.Active!.Code);
    }

    [Fact]
    public void A_mixed_quest_is_entered_only_by_a_burst_of_states_not_a_lone_tick()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note(Wed + "1", 1000));
        Assert.Empty(l.Note(Wed + "2", 2000));
        Assert.Null(l.Active);
        var t = l.Note(Wed + "3", 3000);            // the third distinct State within 90 s
        Assert.Single(t);
        Assert.Equal("M05", t[0].Section!.Code);
    }

    [Fact]
    public void A_State_that_repeats_is_not_a_burst()
    {
        var l = new StoryLock();
        for (int i = 0; i < 20; i++) Assert.Empty(l.Note(Wed, 1000 + i * 1000));        // one timer node flipping every second
        Assert.Null(l.Active);
    }

    [Fact]
    public void Slow_background_ticks_of_a_mixed_quest_never_enter_it()
    {
        var l = new StoryLock();
        for (int i = 0; i < 10; i++) Assert.Empty(l.Note(Wed + i, i * (StoryLock.SustainedWindowMs + 1000L)));
        Assert.Null(l.Active);
    }

    [Fact]
    public void A_change_in_an_open_quest_enters_nothing()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note("Barbora.trosecko.nebakovPruzkum.x", 1));
        Assert.Null(l.Active);
    }

    [Fact]
    public void An_open_side_quest_or_a_dlc_root_that_is_not_ours_changes_nothing()
    {
        var l = new StoryLock();
        l.Note(Raid, 1);
        Assert.Empty(l.Note("Barbora.trosecko.hledaniPsa.x", 2));
        Assert.Equal("M08", l.Active!.Code);
    }

    [Fact]
    public void The_quests_end_leaves_the_section()
    {
        var l = new StoryLock();
        l.Note(Raid, 1);
        var t = l.Note("Barbora.trosecko.mucirna.endQuest", 5000);
        Assert.Single(t);
        Assert.Equal(StoryLock.Kind.Leave, t[0].Kind);
        Assert.Equal("completed", t[0].Reason);
        Assert.Null(l.Active);
    }

    [Fact]
    public void An_end_with_no_section_open_enters_nothing()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note("Barbora.trosecko.mucirna.endQuest", 1));
        Assert.Null(l.Active);
    }

    [Fact]
    public void A_later_main_quest_ends_the_earlier_section_and_a_later_rails_one_enters()
    {
        var l = new StoryLock();
        l.Note(Raid, 1);                                              // M08
        var t = l.Note(Storm, 100);                                   // M12, rails, later
        Assert.Equal(2, t.Count);
        Assert.Equal((StoryLock.Kind.Leave, "M08", "moved on"), (t[0].Kind, t[0].Section!.Code, t[0].Reason));
        Assert.Equal((StoryLock.Kind.Enter, "M12"), (t[1].Kind, t[1].Section!.Code));
        Assert.Equal("M12", l.Active!.Code);
    }

    [Fact]
    public void A_later_open_quest_just_ends_the_section()
    {
        var l = new StoryLock();
        l.Note(Raid, 1);                                              // M08
        var t = Burst(l, "Barbora.trosecko.utokNaNebakov.x", 10);     // M09: later, mixed: a burst shows the story moved on; it enters
        Assert.Equal(StoryLock.Kind.Leave, t[0].Kind);
        Assert.Equal("M08", t[0].Section!.Code);
        Assert.Equal("M09", l.Active!.Code);
        var l2 = new StoryLock();
        l2.Note(Storm, 1);                                            // M12
        var t2 = Burst(l2, "Barbora.kutnohorsko.prijezdNaSuchdol.x", 10);   // M31: open, later: the burst ends M12, nothing enters
        Assert.Single(t2);
        Assert.Equal(StoryLock.Kind.Leave, t2[0].Kind);
        Assert.Null(l2.Active);
    }

    [Fact]
    public void One_stray_State_of_a_later_main_quest_does_not_end_the_section_the_host_is_in()
    {
        var l = new StoryLock();
        Burst(l, Wed, 1);                                              // M05, the wedding
        Assert.Empty(l.Note("Barbora.trosecko.naTroskach.activate", 100));    // a lone State of M06 (a flag): the host is still at the wedding
        Assert.Empty(l.Note("Barbora.trosecko.nebakovPruzkum.flag", 200));    // and of M07
        Assert.Equal("M05", l.Active!.Code);
        Assert.Empty(Burst(l, Wed + "again", 300).Where(t => t.Kind == StoryLock.Kind.Leave));   // M05 is not "done": its later States still count
    }

    [Fact]
    public void An_earlier_quests_background_states_never_end_a_later_section()
    {
        var l = new StoryLock();
        l.Note(Storm, 1);                                              // M12
        Assert.Empty(l.Note(Raid, 2));                                 // M08 ticking in the background: ignored
        Assert.Empty(Burst(l, "Barbora.trosecko.zachrana.x", 3));      // M02: ignored
        Assert.Equal("M12", l.Active!.Code);
    }

    [Fact]
    public void The_story_order_is_the_games_not_the_codes_the_prologue_comes_first()
    {
        // M30 "Last Rites" (the prologue, played as Godwin) has the highest code of the first twelve and is the FIRST quest of the game
        var l = new StoryLock();
        var t = l.Note("Barbora.kutnohorsko.posledniPomazani.x", 1);
        Assert.Equal("M30", t[0].Section!.Code);
        var t2 = l.Note(Raid, 2);                                       // the game moves on to Necessary Evil: the prologue is over
        Assert.Equal((StoryLock.Kind.Leave, "M30"), (t2[0].Kind, t2[0].Section!.Code));
        Assert.Equal("M08", l.Active!.Code);
        Assert.Empty(l.Note("Barbora.kutnohorsko.posledniPomazani.y", 3));   // the prologue never re-enters
    }

    [Fact]
    public void An_untouched_section_is_over_after_its_idle_time()
    {
        var l = new StoryLock();
        l.Note(Raid, 1000);
        int idle = l.Active!.IdleMs;
        Assert.Equal(40 * 60 * 1000, idle);                            // a main rails section: 40 minutes
        Assert.Null(l.Tick(1000 + idle));                              // exactly the limit: still in
        var t = l.Tick(1000 + idle + 1);
        Assert.Equal((StoryLock.Kind.Leave, "idle"), (t!.Value.Kind, t.Value.Reason));
        Assert.Null(l.Active);
        Assert.Null(l.Tick(1000 + 2L * idle));                         // and only once
    }

    [Theory]
    [InlineData("M08", 40 * 60 * 1000)]    // main, rails
    [InlineData("M05", 12 * 60 * 1000)]    // main, mixed
    [InlineData("A30", 15 * 60 * 1000)]    // other, rails
    [InlineData("S49", 8 * 60 * 1000)]     // other, mixed
    public void The_idle_time_follows_the_kind_and_the_tier(string code, int ms) => Assert.Equal(ms, StorySections.ByCode(code)!.IdleMs);

    [Fact]
    public void A_change_in_the_section_refreshes_the_idle_clock()
    {
        int idle = 40 * 60 * 1000;
        var l = new StoryLock();
        l.Note(Raid, 0);
        l.Note(Raid + "2", idle - 10);                                  // a NEW State of the quest
        Assert.Null(l.Tick(idle + 1000));
        Assert.NotNull(l.Active);
    }

    [Fact]
    public void A_timer_cycling_through_the_same_States_does_not_keep_a_section_alive()
    {
        var l = new StoryLock();
        l.Note(Raid, 0);
        for (long t = 60_000; t < 41 * 60 * 1000; t += 60_000) l.Note(Raid, t);      // the same State every minute for 41 minutes
        Assert.NotNull(l.Tick(41 * 60 * 1000));
        Assert.Null(l.Active);
    }

    [Fact]
    public void Reset_forgets_the_section()
    {
        var l = new StoryLock();
        l.Note(Raid, 1);
        l.Reset();
        Assert.Null(l.Active);
        Assert.Null(l.Tick(long.MaxValue / 2));
        Assert.Single(l.Note(Raid, 5));                                 // and the next change enters it again
    }

    [Fact]
    public void Every_locked_main_section_can_be_entered_by_states_of_its_own_quest()
    {
        foreach (var s in StorySections.All.Where(x => x.Locked && x.Markers.Count == 0))
        {
            var l = new StoryLock();
            var t = Burst(l, $"Barbora.{s.Level}.{s.Quest}.node", 1);
            Assert.Single(t);
            Assert.Equal(s.Code, t[0].Section!.Code);
        }
    }

    // ---- the quest with a marker: the final act begins at the Ruthard courtyard ----

    [Fact]
    public void Oratores_is_entered_only_inside_the_staged_part_of_it()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note("Barbora.kutnohorsko.papezskyLegat.suchdol.x", 1));        // the open part: planning, side content
        Assert.Empty(l.Note("Barbora.kutnohorsko.papezskyLegat.pruzkum_sklepeni.x", 2));
        Assert.Null(l.Active);
        var t = l.Note("Barbora.kutnohorsko.papezskyLegat.dobyvani_ruthardky.x", 3);     // the Ruthard courtyard
        Assert.Single(t);
        Assert.Equal("M45", t[0].Section!.Code);
        Assert.Equal(StoryTier.Rails, t[0].Section!.Tier);
        Assert.Empty(l.Note("Barbora.kutnohorsko.papezskyLegat.suchdol.y", 4));          // refreshes, enters nothing new
        Assert.Equal("M45", l.Active!.Code);
    }

    [Fact]
    public void A_marked_quest_still_ends_the_section_before_it_and_orders_later_ones()
    {
        var l = new StoryLock();
        l.Note(Assault, 1);                                                                // M44b
        var t = l.Note("Barbora.kutnohorsko.papezskyLegat.suchdol.x", 2);                  // M45's open part: M44b is over, M45 is not entered
        Assert.Single(t);
        Assert.Equal((StoryLock.Kind.Leave, "M44b"), (t[0].Kind, t[0].Section!.Code));
        Assert.Null(l.Active);
        Assert.Empty(l.Note(Assault, 3));                                                  // and M44b never re-enters
    }

    [Fact]
    public void The_final_act_is_one_unbroken_stretch_from_the_courtyard_to_the_end()
    {
        var l = new StoryLock();
        l.Note("Barbora.kutnohorsko.papezskyLegat.dobyvani_ruthardky.x", 1);
        foreach (var q in new[] { "prepadeniVlasskehoDvora", "erik", "oblehaniSuchdole", "rutinaAVypad", "hladAZmar", "stealthMiseZaJindru", "zoufalaObranaZaBohutu", "finale" })
        {
            var t = l.Note($"Barbora.kutnohorsko.{q}.x", 10);
            Assert.Equal(2, t.Count);
            Assert.Equal("M45", StorySections.PeriodOf(t[0].Section!.Code)!.Id);
            Assert.Equal("M45", StorySections.PeriodOf(t[1].Section!.Code)!.Id);
        }
        Assert.Equal("M51", l.Active!.Code);
    }

    // ---- side quests, tasks, activities ----

    [Fact]
    public void A_tournament_is_entered_by_its_second_State_and_left_when_it_ends()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.round1", 1));
        var t = l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.round2", 2);
        Assert.Equal("A30", t[0].Section!.Code);
        Assert.Equal(StoryTier.Rails, t[0].Section!.Tier);
        var e = l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.endQuest", 2);
        Assert.Equal((StoryLock.Kind.Leave, "completed"), (e[0].Kind, e[0].Reason));
        Assert.Null(l.Active);
    }

    [Fact]
    public void A_staged_side_quest_needs_a_burst_and_is_left_after_its_idle_time_with_a_cooldown()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note("Barbora.kutnohorsko.sesivaniTonici.x", 1));                  // S50, mixed: one tick enters nothing
        var t = Burst(l, "Barbora.kutnohorsko.sesivaniTonici.y", 100);
        Assert.Equal("S50", t[0].Section!.Code);
        Assert.Single(t);
        Assert.Equal(8 * 60 * 1000, l.Active!.IdleMs);
        var gone = l.Tick(100 + 8 * 60 * 1000 + 1000);
        Assert.Equal("idle", gone!.Value.Reason);
        // the quest's background timers keep ticking: no re-entry for the cooldown
        Assert.Empty(Burst(l, "Barbora.kutnohorsko.sesivaniTonici.z", 100 + 8 * 60 * 1000 + 2000));
        Assert.Null(l.Active);
        // after the cooldown a new burst enters again
        long later = 100 + 8 * 60 * 1000 + 1000 + StoryLock.IdleCooldownMs + 5000;
        Assert.Single(Burst(l, "Barbora.kutnohorsko.sesivaniTonici.w", later));
    }

    [Fact]
    public void A_repeatable_bout_can_be_entered_again_soon_after_it_ended()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note("Barbora.trosecko.fight_clubs.fightClubZelejov.a", 0));
        l.Note("Barbora.trosecko.fight_clubs.fightClubZelejov.b", 1000);                 // A09 rails: the second distinct State enters
        Assert.Equal("A09", l.Active!.Code);
        l.Note("Barbora.trosecko.fight_clubs.fightClubZelejov.endQuest", 200_000);       // the bout ended
        Assert.Null(l.Active);
        Assert.Empty(l.Note("Barbora.trosecko.fight_clubs.fightClubZelejov.cleanup", 210_000));   // cleanup ticking right after
        long next = 200_000 + StoryLock.CompletedCooldownMs + 5000;
        l.Note("Barbora.trosecko.fight_clubs.fightClubZelejov.c", next);
        var t2 = l.Note("Barbora.trosecko.fight_clubs.fightClubZelejov.d", next + 1000);   // the next bout, a few minutes later
        Assert.Equal("A09", t2.Single().Section!.Code);
    }

    [Fact]
    public void A_staged_side_quest_needs_two_States_a_tournament_too()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.round1", 1));
        Assert.Equal("A30", l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.round2", 2).Single().Section!.Code);
    }

    [Fact]
    public void A_side_section_is_not_replaced_while_the_host_is_busy_in_it()
    {
        var l = new StoryLock();
        l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.a", 1000);
        l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.b", 2000);                          // A30 entered
        Assert.Empty(l.Note("Barbora.trosecko.combat_tutorial_pro_pokrocile.a", 5000));
        Assert.Empty(l.Note("Barbora.trosecko.combat_tutorial_pro_pokrocile.b", 6000));   // another rails side quest's States: the host is busy in the tournament
        Assert.Equal("A30", l.Active!.Code);
        // a new State of the tournament keeps it busy; when it goes quiet for 30 s another quest may take over (and the tournament is not cooled down)
        l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.c", 7000);
        var t = l.Note("Barbora.trosecko.combat_tutorial_pro_pokrocile.c", 7000 + StoryLock.ReplaceQuietMs);
        Assert.Equal((StoryLock.Kind.Leave, "A30"), (t[0].Kind, t[0].Section!.Code));
        Assert.Equal("S27", l.Active!.Code);
        long later = 200_000;
        l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.d", later);
        Assert.Single(l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.e", later + 1000).Where(x => x.Kind == StoryLock.Kind.Enter));   // replaced is not cooled down
    }

    [Fact]
    public void The_hosts_current_main_quest_can_take_a_quiet_side_section_back_when_its_staged_part_begins()
    {
        var l = new StoryLock();
        l.Note(Raid, 1);                                                                   // M08
        l.Note("Barbora.trosecko.mucirna.endQuest", 2);
        Burst(l, Wed, 100);                                                                // M05 is EARLIER than M08: ignored
        Assert.Null(l.Active);
        var l2 = new StoryLock();
        Burst(l2, Wed, 1);                                                                 // M05 (mixed) entered
        l2.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.a", 40_000);
        l2.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.b", 41_000);                        // A30 replaced it (the host had been quiet in the wedding)
        Assert.Equal("A30", l2.Active!.Code);
        Assert.Empty(Burst(l2, Wed + "x", 45_000));                                        // M05 ticking while the host is busy in the tournament
        Assert.Equal("A30", l2.Active!.Code);
        var back = Burst(l2, Wed + "y", 120_000);                                          // a burst after the tournament went quiet: the wedding's staged part
        Assert.Equal("M05", back.Last().Section!.Code);
    }

    [Fact]
    public void A_side_quest_never_replaces_a_main_rails_section()
    {
        var l = new StoryLock();
        l.Note(Storm, 1);                                                                   // M12, rails
        Assert.Empty(Burst(l, "Barbora.kutnohorsko.kutnohorskyTurnaj.x", 10));              // a tournament's timers: ignored in a locked world
        Assert.Equal("M12", l.Active!.Code);
    }

    [Fact]
    public void A_side_quest_replaces_a_mixed_main_section_and_the_main_one_comes_back_with_a_new_burst()
    {
        var l = new StoryLock();
        Burst(l, Wed, 1);                                                                   // M05, mixed
        Assert.Equal("M05", l.Active!.Code);
        l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.round0", 40_000);                     // the host has been quiet in the wedding for 30 s: a tournament's first State
        var t = l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.round1", 41_000);             // and its second: A30, rails, replaces it
        Assert.Equal(2, t.Count);
        Assert.Equal((StoryLock.Kind.Leave, "M05", "moved on"), (t[0].Kind, t[0].Section!.Code, t[0].Reason));
        Assert.Equal("A30", l.Active!.Code);
        // the wedding's own background States while the host is in the tournament do not take the section back
        Assert.Empty(Burst(l, Wed + "bg", 42_000));
        Assert.Equal("A30", l.Active!.Code);
        // when the tournament ends the wedding's next burst enters again (a main quest is not "done" by being replaced)
        l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.endQuest", 50_000);
        var back = Burst(l, Wed + "fresh", 600_000);
        Assert.Equal("M05", back.Last().Section!.Code);
    }

    [Fact]
    public void A_main_quest_replaces_a_side_section_when_it_is_later_than_where_the_host_has_been()
    {
        var l = new StoryLock();
        l.Note(Raid, 1);                                                                    // M08
        Burst(l, "Barbora.trosecko.utokNaNebakov.x", 10);                                   // M09: later, mixed: a burst, M08 is over, M09 entered
        Assert.Equal("M09", l.Active!.Code);
        l.Note("Barbora.trosecko.fight_clubs.fightClubZelejov.a", 100_000);                 // the host has gone quiet in M09: a fight club bout
        var swap = l.Note("Barbora.trosecko.fight_clubs.fightClubZelejov.b", 101_000);      // A09 enters, replacing the section the host was quiet in
        Assert.Equal(new[] { (StoryLock.Kind.Leave, "M09"), (StoryLock.Kind.Enter, "A09") }, swap.Select(x => (x.Kind, x.Section!.Code)).ToArray());
        Assert.Equal("A09", l.Active!.Code);
        var t = Burst(l, "Barbora.trosecko.bohutovaVlozka.x", 110_000);                     // M10: later than M09: the story moved on
        Assert.Equal((StoryLock.Kind.Leave, "A09", "moved on"), (t[0].Kind, t[0].Section!.Code, t[0].Reason));
        Assert.Equal((StoryLock.Kind.Enter, "M10"), (t[^1].Kind, t[^1].Section!.Code));
        Assert.Equal("M10", l.Active!.Code);
    }

    [Fact]
    public void An_earlier_main_quests_state_does_not_replace_a_side_section()
    {
        var l = new StoryLock();
        l.Note(Storm, 1);                                                                    // M12
        Burst(l, "Barbora.kutnohorsko.prijezdNaSuchdol.x", 2);                               // M31 open, later: a burst: M12 over, the host is at M31
        Assert.Null(l.Active);
        l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.a", 3);
        Assert.Single(l.Note("Barbora.kutnohorsko.kutnohorskyTurnaj.b", 4));                 // A30
        Assert.Empty(Burst(l, Wed, 100));                                                    // M05 is earlier than where the host has got
        Assert.Equal("A30", l.Active!.Code);
    }

    // ---- the tether ----

    [Theory]
    [InlineData(600f, 650f, false, 120f, 600f, 650f)]    // off: the leash's own
    [InlineData(600f, 650f, true, 120f, 90f, 120f)]      // on: pull = the tether, warn 30 inside it
    [InlineData(600f, 650f, true, 60f, 50f, 60f)]        // the tightest: warn at the leash's 50 m floor
    [InlineData(600f, 650f, true, 5000f, 600f, 650f)]    // a loose tether never loosens the host's own numbers
    [InlineData(100f, 110f, true, 120f, 90f, 110f)]      // the host's own pull is already tighter
    [InlineData(70f, 80f, true, 70f, 50f, 70f)]
    public void The_tether_is_the_tighter_of_the_leash_and_the_tether(float warn, float pull, bool on, float tether, float wantWarn, float wantPull)
    {
        var (w, p) = StoryLock.Tether(warn, pull, on, tether);
        Assert.Equal(wantWarn, w);
        Assert.Equal(wantPull, p);
        Assert.True(w < p || !on, "the warning stays under the pull");
    }

    [Fact]
    public void The_tether_never_puts_the_warning_at_or_over_the_pull()
    {
        foreach (float tether in new[] { 60f, 61f, 70f, 80f, 120f, 5000f })
            foreach (float pull in new[] { 55f, 60f, 90f, 650f })
            {
                var (w, p) = StoryLock.Tether(Math.Min(600f, pull - 5f), pull, true, tether);
                Assert.True(w < p, $"tether {tether} pull {pull}: warn {w} pull {p}");
            }
    }

    // ---- review: a section the host left must not be re-entered by leftovers ----

    [Fact]
    public void A_finished_quests_leftover_states_do_not_re_enter_its_section()
    {
        var l = new StoryLock();
        l.Note(Raid, 1);
        l.Note("Barbora.trosecko.mucirna.endQuest", 2);                  // completed
        Assert.Null(l.Active);
        Assert.Empty(l.Note("Barbora.trosecko.mucirna.cleanup.x", 3));   // a cleanup node after the end
        Assert.Empty(l.Note(Raid, 4));
        Assert.Null(l.Active);
    }

    [Fact]
    public void An_earlier_quest_never_re_enters_after_the_host_moved_on()
    {
        var l = new StoryLock();
        l.Note(Raid, 1);                                                 // M08
        l.Note(Storm, 2);                                                // M12: moved on, entered
        l.Note("Barbora.trosecko.vezniNaTroskach.endQuest", 3);          // M12 completed: nothing active
        Assert.Null(l.Active);
        Assert.Empty(l.Note(Raid, 4));                                   // M08's background State
        Assert.Empty(l.Note("Barbora.trosecko.mucirna.y", 5));
        Assert.Null(l.Active);
    }

    [Fact]
    public void A_later_quest_still_enters_after_an_earlier_one_completed()
    {
        var l = new StoryLock();
        l.Note(Raid, 1);
        l.Note("Barbora.trosecko.mucirna.endQuest", 2);
        var t = l.Note(Storm, 3);
        Assert.Single(t);
        Assert.Equal("M12", t[0].Section!.Code);
    }

    [Fact]
    public void After_an_idle_leave_new_activity_in_the_same_main_quest_re_enters()
    {
        var l = new StoryLock();
        l.Note(Raid, 0);
        Assert.NotNull(l.Tick(40 * 60 * 1000 + 1));
        Assert.Single(l.Note(Raid, 40 * 60 * 1000 + 5));                 // the host came back to it
    }

    [Fact]
    public void Reset_forgets_what_was_finished_and_how_far_the_host_got()
    {
        var l = new StoryLock();
        l.Note(Raid, 1);
        l.Note("Barbora.trosecko.mucirna.endQuest", 2);
        l.Note(Storm, 3);
        l.Reset();                                                       // the host loaded an earlier save
        Assert.Single(l.Note(Raid, 4));                                  // M08 enters again
        Assert.Single(l.Note(Storm, 5).Where(x => x.Kind == StoryLock.Kind.Enter));
    }

    [Theory]
    [InlineData(600f, 40f, true, 120f)]    // the host's own pull at or under 50 m
    [InlineData(100f, 50f, true, 60f)]
    [InlineData(45f, 50f, true, 5000f)]
    [InlineData(30f, 30f, true, 120f)]
    public void The_warning_stays_under_the_pull_even_for_a_very_tight_host_leash(float warn, float pull, bool on, float tether)
    {
        var (w, p) = StoryLock.Tether(warn, pull, on, tether);
        Assert.True(w < p, $"warn {w} pull {p}");
        Assert.True(w >= 1f);
    }

    [Fact]
    public void A_host_pull_of_40_gives_a_warning_of_39()
    {
        var (w, p) = StoryLock.Tether(600f, 40f, true, 120f);
        Assert.Equal((39f, 40f), (w, p));
    }
}
