// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-153: the locked story sections (the table of main quests and the state machine that reads the host's States).</summary>
public class Wo153StoryTests
{
    private const string Wed = "Barbora.trosecko.svatba.hibernovana_cast.x";

    // ---- the table ----

    [Fact]
    public void The_table_is_the_registrys_32_main_quests_in_story_order()
    {
        Assert.Equal(32, StorySections.All.Count);
        Assert.Equal("M01", StorySections.All[0].Code);
        Assert.Equal("M51", StorySections.All[^1].Code);
        Assert.Equal(StorySections.All.Count, StorySections.All.Select(s => s.Code).Distinct().Count());
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
    [InlineData("M05", "the wedding in Semine")]
    [InlineData("M06", "Trosky castle")]
    [InlineData("M12", "Trosky castle")]
    [InlineData("M30", "the move to Kuttenberg")]
    [InlineData("M31", "arrival in Kuttenberg")]
    [InlineData("M42", "the burning of the Jewish quarter")]
    [InlineData("M45", "the cardinal")]
    [InlineData("M46", "the Italian Job")]
    [InlineData("M47", "the final set")]
    [InlineData("M48a", "the final set")]
    [InlineData("M48b", "the final set")]
    [InlineData("M48c", "the final set")]
    [InlineData("M49", "the final set")]
    [InlineData("M50", "the final set")]
    [InlineData("M51", "the final set")]
    public void The_users_sections_are_locked(string code, string why)
    {
        var s = StorySections.ByCode(code)!;
        Assert.True(s.Locked);
        Assert.Equal(why, s.Why);
    }

    [Theory]
    [InlineData("M01")] [InlineData("M02")] [InlineData("M03")] [InlineData("M07")] [InlineData("M08")]
    [InlineData("M09")] [InlineData("M10")] [InlineData("M11")] [InlineData("M33")] [InlineData("M34")]
    [InlineData("M35")] [InlineData("M37a")] [InlineData("M37b")]
    public void Open_world_quests_are_not_locked(string code) => Assert.False(StorySections.ByCode(code)!.Locked);

    [Fact]
    public void Every_locked_section_says_why_and_no_open_one_does()
    {
        foreach (var s in StorySections.All) Assert.Equal(s.Locked, s.Why.Length > 0);
    }

    // ---- reading a path ----

    [Theory]
    [InlineData("Barbora.trosecko.svatba.hibernovana_cast.x", "M05")]
    [InlineData("Barbora.trosecko.svatba", "M05")]
    [InlineData("Barbora.kutnohorsko.pogrom.a.b.c", "M42")]
    [InlineData("Barbora.kutnohorsko.PAPEZSKYLEGAT.x", "M45")]       // case-insensitive quest name
    public void A_quest_path_names_its_main_quest(string path, string code) => Assert.Equal(code, StorySections.FromPath(path)!.Code);

    [Theory]
    [InlineData("Barbora.kutnohorsko.svatba.x")]                       // right name, wrong level
    [InlineData("Barbora.trosecko.hledaniPsa.x")]                      // a side quest
    [InlineData("Barbora.kutnohorsko.kovarske_mikroquesty.katuvSleh")] // a DLC root
    [InlineData("Haste.trosecko.svatba.x")]                            // not under Barbora
    [InlineData("Barbora.trosecko")]
    [InlineData("")]
    [InlineData(null)]
    public void Anything_else_is_not_a_main_quest(string? path) => Assert.Null(StorySections.FromPath(path));

    [Fact]
    public void An_end_path_is_recognised() =>
        Assert.True(StorySections.IsEndPath("Barbora.trosecko.svatba.endQuest")
                    && StorySections.IsEndPath("Barbora.trosecko.svatba.x.ENDQUEST.y") && !StorySections.IsEndPath(Wed));

    // ---- the state machine ----

    [Fact]
    public void A_change_in_a_locked_quest_enters_its_section_once()
    {
        var l = new StoryLock();
        var t = l.Note(Wed, 1000);
        Assert.Single(t);
        Assert.Equal(StoryLock.Kind.Enter, t[0].Kind);
        Assert.Equal("M05", t[0].Section!.Code);
        Assert.Empty(l.Note(Wed, 2000));          // more changes in it: nothing new
        Assert.Equal("M05", l.Active!.Code);
    }

    [Fact]
    public void A_change_in_an_open_quest_enters_nothing()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note("Barbora.trosecko.nebakovPruzkum.x", 1));
        Assert.Null(l.Active);
    }

    [Fact]
    public void A_side_quest_or_a_dlc_root_changes_nothing()
    {
        var l = new StoryLock();
        l.Note(Wed, 1);
        Assert.Empty(l.Note("Barbora.trosecko.hledaniPsa.x", 2));
        Assert.Empty(l.Note("Barbora.kutnohorsko.kovarske_mikroquesty.katuvSleh", 3));
        Assert.Equal("M05", l.Active!.Code);
    }

    [Fact]
    public void The_quests_end_leaves_the_section()
    {
        var l = new StoryLock();
        l.Note(Wed, 1);
        var t = l.Note("Barbora.trosecko.svatba.endQuest", 5000);
        Assert.Single(t);
        Assert.Equal(StoryLock.Kind.Leave, t[0].Kind);
        Assert.Equal("completed", t[0].Reason);
        Assert.Null(l.Active);
    }

    [Fact]
    public void An_end_with_no_section_open_enters_nothing()
    {
        var l = new StoryLock();
        Assert.Empty(l.Note("Barbora.trosecko.svatba.endQuest", 1));
        Assert.Null(l.Active);
    }

    [Fact]
    public void A_later_main_quest_ends_the_earlier_section_and_a_later_locked_one_enters()
    {
        var l = new StoryLock();
        l.Note(Wed, 1);
        var t = l.Note("Barbora.trosecko.naTroskach.x", 100);        // M06, locked, later than M05
        Assert.Equal(2, t.Count);
        Assert.Equal((StoryLock.Kind.Leave, "M05", "moved on"), (t[0].Kind, t[0].Section!.Code, t[0].Reason));
        Assert.Equal((StoryLock.Kind.Enter, "M06"), (t[1].Kind, t[1].Section!.Code));
        Assert.Equal("M06", l.Active!.Code);
    }

    [Fact]
    public void A_later_open_quest_just_ends_the_section()
    {
        var l = new StoryLock();
        l.Note("Barbora.trosecko.naTroskach.x", 1);                   // M06
        var t = l.Note("Barbora.trosecko.nebakovPruzkum.x", 10);      // M07: open
        Assert.Single(t);
        Assert.Equal(StoryLock.Kind.Leave, t[0].Kind);
        Assert.Null(l.Active);
    }

    [Fact]
    public void An_earlier_quests_background_states_never_end_a_later_section()
    {
        var l = new StoryLock();
        l.Note("Barbora.trosecko.naTroskach.x", 1);                   // M06
        Assert.Empty(l.Note(Wed, 2));                                  // M05 ticking in the background: ignored
        Assert.Empty(l.Note("Barbora.trosecko.zachrana.x", 3));       // M02: ignored
        Assert.Equal("M06", l.Active!.Code);
    }

    [Fact]
    public void An_untouched_section_is_over_after_the_idle_time()
    {
        var l = new StoryLock();
        l.Note(Wed, 1000);
        Assert.Null(l.Tick(1000 + StoryLock.IdleMs));                  // exactly the limit: still in
        var t = l.Tick(1000 + StoryLock.IdleMs + 1);
        Assert.Equal((StoryLock.Kind.Leave, "idle"), (t!.Value.Kind, t.Value.Reason));
        Assert.Null(l.Active);
        Assert.Null(l.Tick(1000 + 2L * StoryLock.IdleMs));             // and only once
    }

    [Fact]
    public void A_change_in_the_section_refreshes_the_idle_clock()
    {
        var l = new StoryLock();
        l.Note(Wed, 0);
        l.Note(Wed, StoryLock.IdleMs - 10);
        Assert.Null(l.Tick(StoryLock.IdleMs + 1000));
        Assert.NotNull(l.Active);
    }

    [Fact]
    public void Reset_forgets_the_section()
    {
        var l = new StoryLock();
        l.Note(Wed, 1);
        l.Reset();
        Assert.Null(l.Active);
        Assert.Null(l.Tick(long.MaxValue / 2));
        Assert.Single(l.Note(Wed, 5));                                  // and the next change enters it again
    }

    [Fact]
    public void Every_locked_section_can_be_entered_by_a_state_of_its_own_quest()
    {
        foreach (var s in StorySections.All.Where(x => x.Locked))
        {
            var l = new StoryLock();
            var t = l.Note($"Barbora.{s.Level}.{s.Quest}.node", 1);
            Assert.Single(t);
            Assert.Equal(s.Code, t[0].Section!.Code);
        }
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
        l.Note(Wed, 1);
        l.Note("Barbora.trosecko.svatba.endQuest", 2);                  // completed
        Assert.Null(l.Active);
        Assert.Empty(l.Note("Barbora.trosecko.svatba.cleanup.x", 3));   // a cleanup node after the end
        Assert.Empty(l.Note(Wed, 4));
        Assert.Null(l.Active);
    }

    [Fact]
    public void An_earlier_quest_never_re_enters_after_the_host_moved_on()
    {
        var l = new StoryLock();
        l.Note(Wed, 1);                                                  // M05
        l.Note("Barbora.trosecko.vezniNaTroskach.x", 2);                 // M12: moved on, entered
        l.Note("Barbora.trosecko.vezniNaTroskach.endQuest", 3);          // M12 completed: nothing active
        Assert.Null(l.Active);
        Assert.Empty(l.Note(Wed, 4));                                    // M05's background State
        Assert.Empty(l.Note("Barbora.trosecko.naTroskach.x", 5));        // M06, earlier than M12
        Assert.Null(l.Active);
    }

    [Fact]
    public void A_later_quest_still_enters_after_an_earlier_one_completed()
    {
        var l = new StoryLock();
        l.Note(Wed, 1);
        l.Note("Barbora.trosecko.svatba.endQuest", 2);
        var t = l.Note("Barbora.trosecko.naTroskach.x", 3);
        Assert.Single(t);
        Assert.Equal("M06", t[0].Section!.Code);
    }

    [Fact]
    public void After_an_idle_leave_new_activity_in_the_same_quest_re_enters()
    {
        var l = new StoryLock();
        l.Note(Wed, 0);
        Assert.NotNull(l.Tick(StoryLock.IdleMs + 1));
        Assert.Single(l.Note(Wed, StoryLock.IdleMs + 5));                // the host came back to it
    }

    [Fact]
    public void Reset_forgets_what_was_finished_and_how_far_the_host_got()
    {
        var l = new StoryLock();
        l.Note("Barbora.trosecko.naTroskach.x", 1);
        l.Note("Barbora.trosecko.naTroskach.endQuest", 2);
        l.Note("Barbora.trosecko.vezniNaTroskach.x", 3);
        l.Reset();                                                       // the host loaded an earlier save
        Assert.Single(l.Note(Wed, 4));                                   // M05 enters again
        Assert.Single(l.Note("Barbora.trosecko.naTroskach.y", 5).Where(x => x.Kind == StoryLock.Kind.Enter));
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
