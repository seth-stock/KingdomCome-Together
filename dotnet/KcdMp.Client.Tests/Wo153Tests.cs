// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-153: the rules of "the joiner comes along after the host's scene" (the pure half).</summary>
public class Wo153Tests
{
    private const float Reloc = SceneFollowLogic.RelocDefaultM, Far = SceneFollowLogic.FarDefaultM;
    private static SceneFollowLogic.Verdict D(bool story, double moved, double? joiner) =>
        SceneFollowLogic.Decide(story, moved, joiner, Reloc, Far);

    [Theory]
    [InlineData(false, 25.0, null, SceneFollowLogic.Verdict.Relocated)]     // exactly the threshold counts
    [InlineData(false, 24.9, null, SceneFollowLogic.Verdict.None)]
    [InlineData(false, 800.0, 3.0, SceneFollowLogic.Verdict.Relocated)]       // moved far; the joiner being near does not undo it (the leash skips a near joiner)
    [InlineData(true, 0.0, 150.0, SceneFollowLogic.Verdict.Far)]               // a story scene, the host stayed, the joiner is far
    [InlineData(true, 0, 149.9, SceneFollowLogic.Verdict.None)]
    [InlineData(false, 0.0, 900.0, SceneFollowLogic.Verdict.None)]             // a fader/text scene never pulls by distance alone
    [InlineData(true, 0.0, null, SceneFollowLogic.Verdict.None)]             // no joiner position known: nothing to measure
    [InlineData(true, 10.0, 40.0, SceneFollowLogic.Verdict.None)]              // nothing moved, joiner near: nobody needs bringing
    public void The_window_closes_with_a_verdict(bool story, double moved, double? joiner, SceneFollowLogic.Verdict want) =>
        Assert.Equal(want, D(story, moved, joiner));

    [Theory]
    [InlineData(double.NaN)]
    [InlineData(-1.0)]
    public void A_bad_displacement_is_never_a_verdict(double moved) =>
        Assert.Equal(SceneFollowLogic.Verdict.None, D(true, moved, 9999));

    [Fact]
    public void A_nan_joiner_distance_is_never_far() =>
        Assert.Equal(SceneFollowLogic.Verdict.None, D(true, 0, double.NaN));

    [Theory]
    [InlineData("25", 25f)]
    [InlineData("5", 5f)]
    [InlineData("5000", 5000f)]
    public void Thresholds_in_range_parse(string text, float want) => Assert.Equal(want, SceneFollowLogic.ParseMetres(text));

    [Theory]
    [InlineData("4")]
    [InlineData("5001")]
    [InlineData("-30")]
    [InlineData("12.5")]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("abc")]
    [InlineData("1e3")]
    public void Thresholds_out_of_range_or_malformed_are_refused(string? text) => Assert.Null(SceneFollowLogic.ParseMetres(text));

    [Theory]
    [InlineData("Rendered", true)]
    [InlineData("Ingame", true)]
    [InlineData("Fader", false)]
    [InlineData("Text", false)]
    [InlineData("SkipTime", false)]
    [InlineData("", false)]
    public void Only_rendered_and_ingame_scenes_are_story_scenes(string type, bool story) =>
        Assert.Equal(story, SceneFollowLogic.IsStoryScene(type));




    // ---- the far threshold has a floor: the leash never pulls a joiner within 50 m ----

    [Theory]
    [InlineData("50", 50f)]
    [InlineData("150", 150f)]
    [InlineData("5000", 5000f)]
    public void The_far_threshold_accepts_from_the_leashs_own_50_m(string text, float want) =>
        Assert.Equal(want, SceneFollowLogic.ParseMetres(text, SceneFollowLogic.MinFarM));

    [Theory]
    [InlineData("49")]
    [InlineData("5")]
    [InlineData("5001")]
    [InlineData("")]
    public void The_far_threshold_refuses_below_50_and_above_5000(string text) =>
        Assert.Null(SceneFollowLogic.ParseMetres(text, SceneFollowLogic.MinFarM));

    [Fact]
    public void The_far_floor_is_the_leashs_own_rule_not_a_second_number()
    {
        Assert.Equal(LeashLogic.FastTravelMinM, (float)SceneFollowLogic.MinFarM);
        Assert.True(SceneFollowLogic.RelocDefaultM < SceneFollowLogic.FarDefaultM);
    }

    // ---- the window ----

    private const long Settle = SceneFollowLogic.SettleMs;

    [Fact]
    public void One_scene_opens_a_window_and_closes_after_the_quiet_period_with_where_it_started_and_ended()
    {
        var w = new SceneFollowLogic.Window();
        Assert.True(w.Start(1000, 100, 200, "Ingame", "a"));
        Assert.True(w.End(9000, "a", 130, 240));                // nothing runs: the caller starts its timer
        Assert.True(w.Open);                                     // ...the window is still open until the quiet period passes
        Assert.Null(w.TryClose(9000 + Settle - 1000));           // too early
        var closed = w.TryClose(9000 + Settle)!.Value;
        Assert.Equal(new SceneFollowLogic.Closed(100, 200, 130, 240, true), closed);
        Assert.Equal(50.0, closed.MovedM, 6);                    // a 3-4-5 triangle: 30 and 40 -> 50
        Assert.False(w.Open);
        Assert.Null(w.TryClose(9000 + Settle + 5));              // a second timer for the same window finds it closed
    }

    [Fact]
    public void The_end_position_is_where_the_last_scene_ended_not_where_the_window_closed()
    {
        // review finding: the position was read 2 s after the end, with the player free again (a gallop covers ~30 m).
        var w = new SceneFollowLogic.Window();
        w.Start(0, 0, 0, "Fader", "a");
        w.End(500, "a", 10, 0);                                  // the scene put the host 10 m away
        var closed = w.TryClose(500 + Settle)!.Value;
        Assert.Equal(10.0, closed.MovedM, 6);
    }

    [Fact]
    public void A_timer_that_fires_a_little_early_still_closes_the_window()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a"); w.End(100, "a", 1, 1);
        Assert.NotNull(w.TryClose(100 + Settle - SceneFollowLogic.SettleSlackMs));
    }

    [Fact]
    public void Nested_scenes_close_only_at_the_last_end_and_keep_the_first_start_and_the_last_end_position()
    {
        var w = new SceneFollowLogic.Window();
        Assert.True(w.Start(0, 10, 10, "Ingame", "outer"));
        Assert.False(w.Start(500, 90, 90, "Fader", "inner"));   // a fader inside: not a new window
        Assert.Equal(2, w.Depth);
        Assert.False(w.End(800, "inner", 91, 91));               // others still run: no timer
        Assert.Null(w.TryClose(800 + Settle));                   // and a stray timer finds a running scene
        Assert.True(w.End(5000, "outer", 200, 200));
        Assert.Equal(new SceneFollowLogic.Closed(10, 10, 200, 200, true), w.TryClose(5000 + Settle)!.Value);
    }

    [Fact]
    public void The_outer_scene_may_end_before_the_inner_one()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Fader", "f");
        w.Start(10, 2, 2, "Ingame", "i");
        Assert.False(w.End(20, "f", 5, 5));
        Assert.True(w.End(30, "i", 6, 6));
        Assert.Equal(new SceneFollowLogic.Closed(1, 1, 6, 6, true), w.TryClose(30 + Settle)!.Value);
    }

    [Fact]
    public void A_window_of_only_faders_is_not_a_story_window()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 2, "Fader", "f"); w.End(100, "f", 1, 2);
        Assert.False(w.TryClose(100 + Settle)!.Value.Story);
    }

    [Fact]
    public void Chained_scenes_are_one_window_so_a_relocation_between_them_is_not_lost()
    {
        // a scene closes and another begins within the quiet period: the window runs from the FIRST start to the LAST end.
        var w = new SceneFollowLogic.Window();
        Assert.True(w.Start(0, 10, 10, "Fader", "a"));
        Assert.True(w.End(1000, "a", 300, 300));                 // a timer is started for t = 3000
        Assert.False(w.Start(1500, 300, 300, "Ingame", "b"));    // the next scene begins inside the quiet period: the SAME window
        Assert.Null(w.TryClose(1000 + Settle));                  // the first timer fires while b runs: nothing closes
        Assert.True(w.End(4000, "b", 305, 300));
        Assert.Equal(new SceneFollowLogic.Closed(10, 10, 305, 300, true), w.TryClose(4000 + Settle)!.Value);
    }

    [Fact]
    public void A_start_after_the_window_closed_opens_a_new_one()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a"); w.End(10, "a", 1, 1); Assert.NotNull(w.TryClose(10 + Settle));
        Assert.True(w.Start(20_000, 7, 8, "Fader", "b"));
        w.End(20_100, "b", 9, 9);
        Assert.Equal(new SceneFollowLogic.Closed(7, 8, 9, 9, false), w.TryClose(20_100 + Settle)!.Value);
    }

    [Fact]
    public void A_start_long_after_a_window_whose_timer_never_closed_it_opens_a_new_one()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a"); w.End(10, "a", 500, 500);   // closed in effect, but no timer ever called TryClose
        Assert.True(w.Start(60_000, 7, 8, "Fader", "b"));            // not a continuation of a window that ended a minute ago
        w.End(60_100, "b", 9, 9);
        Assert.Equal(new SceneFollowLogic.Closed(7, 8, 9, 9, false), w.TryClose(60_100 + Settle)!.Value);
    }

    [Fact]
    public void An_end_with_no_start_changes_nothing()
    {
        var w = new SceneFollowLogic.Window();
        Assert.False(w.End(100, "a", 1, 1));      // a load, a duplicate release
        Assert.False(w.Open);
        Assert.Null(w.TryClose(100 + Settle));
    }

    [Fact]
    public void A_duplicate_end_after_the_scene_ended_is_ignored_and_keeps_the_first_end_position()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 0, 0, "Rendered", "a");
        Assert.True(w.End(10, "a", 3, 4));
        Assert.False(w.End(20, "a", 999, 999));
        Assert.Equal(5.0, w.TryClose(10 + Settle)!.Value.MovedM, 6);
    }

    [Fact]
    public void A_duplicate_end_never_closes_an_outer_scene_early()
    {
        // The guard gives up on the inner scene (end edge sent), then the engine really releases it: two ends, one name.
        var w = new SceneFollowLogic.Window();
        w.Start(0, 10, 10, "Ingame", "outer");
        w.Start(5, 20, 20, "Fader", "inner");
        Assert.False(w.End(100, "inner", 1, 1));
        Assert.False(w.End(200, "inner", 1, 1));   // the late real release: no running scene has this name any more
        Assert.True(w.Open);
        Assert.Equal(1, w.Depth);
        Assert.True(w.End(300, "outer", 2, 2));
    }

    [Fact]
    public void An_end_naming_a_scene_that_never_started_leaves_the_window_open()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a");
        Assert.False(w.End(10, "other", 1, 1));
        Assert.True(w.Open);
        Assert.Equal(1, w.Depth);
    }

    [Fact]
    public void A_start_repeated_for_the_same_scene_counts_once_and_keeps_the_first_position()
    {
        var w = new SceneFollowLogic.Window();
        Assert.True(w.Start(0, 1, 1, "Ingame", "a"));
        Assert.False(w.Start(5, 9, 9, "Ingame", "a"));
        Assert.Equal(1, w.Depth);
        w.End(10, "a", 1, 1);
        Assert.Equal(1f, w.TryClose(10 + Settle)!.Value.StartX);
    }

    [Fact]
    public void A_long_scene_is_evaluated_at_its_real_end_however_long_it_ran()
    {
        // a scene's real end is never refused for its age (the orphan rule only forgets the OTHER scenes that never ended)
        var w = new SceneFollowLogic.Window();
        w.Start(0, 5, 5, "Ingame", "long");
        long hours = 3L * 60 * 60 * 1000;
        Assert.True(w.End(hours, "long", 80, 5));
        Assert.Equal(new SceneFollowLogic.Closed(5, 5, 80, 5, true), w.TryClose(hours + Settle)!.Value);
    }

    [Fact]
    public void A_scene_whose_end_never_came_is_forgotten_at_the_next_edge_and_cannot_hold_a_window_open()
    {
        // review finding: an end the log parser refuses (an odd scene name) never removes its start.
        var w = new SceneFollowLogic.Window();
        w.Start(0, 500, 500, "Ingame", "orphan");
        long later = SceneFollowLogic.OrphanSceneMs + 1;
        Assert.True(w.Start(later, 7, 8, "Fader", "new"));        // the orphan is purged, nothing ever ended in that window: a fresh one
        Assert.Equal(1, w.Depth);
        w.End(later + 50, "new", 9, 9);
        Assert.Equal(new SceneFollowLogic.Closed(7, 8, 9, 9, false), w.TryClose(later + 50 + Settle)!.Value);
    }

    [Fact]
    public void An_orphan_inside_a_window_that_did_have_an_end_is_purged_and_the_window_still_closes()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "outer");
        w.Start(10, 2, 2, "Fader", "orphan");
        Assert.False(w.End(100, "outer", 30, 40));                // the orphan still "runs"
        long later = SceneFollowLogic.OrphanSceneMs + 1000;
        Assert.False(w.End(later, "ghost", 0, 0));                // any edge purges it; the window is still open with nothing running
        Assert.Equal(0, w.Depth);
        Assert.Equal(new SceneFollowLogic.Closed(1, 1, 30, 40, true), w.TryClose(later + Settle)!.Value);
    }

    [Fact]
    public void A_nested_start_does_not_wipe_a_running_scene_on_its_own()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 5, 5, "Ingame", "outer");
        w.Start(SceneFollowLogic.OrphanSceneMs - 1, 9, 9, "Fader", "inner");
        Assert.Equal(2, w.Depth);
    }

    [Fact]
    public void Reset_forgets_everything_a_load_or_a_disconnect_leaves_no_window_behind()
    {
        // a load never reset the window: it stayed open with scene names that could no longer end.
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a"); w.Start(1, 1, 1, "Fader", "b");
        w.Reset();
        Assert.False(w.Open);
        Assert.Equal(0, w.Depth);
        Assert.Null(w.TryClose(10_000));
        Assert.False(w.End(2, "a", 0, 0));
        // and the first scene after it opens a clean window
        Assert.True(w.Start(5000, 42, 43, "Fader", "c"));
        w.End(5100, "c", 44, 43);
        Assert.Equal(new SceneFollowLogic.Closed(42, 43, 44, 43, false), w.TryClose(5100 + Settle)!.Value);
    }

    // ---- the joiner's wording of a pull ----

    private static readonly DateTime T0 = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(9.9, true)]
    [InlineData(10, false)]
    [InlineData(25, false)]     // a real fast travel 25 s after a scene is a fast travel
    [InlineData(600, false)]
    public void The_scene_wording_is_for_a_pull_just_after_the_hosts_story_scene_ended(double secondsAfter, bool scene) =>
        Assert.Equal(scene, SceneFollowLogic.UseSceneWording(T0.AddSeconds(secondsAfter), T0));

    [Fact]
    public void No_scene_seen_means_the_ordinary_wording() =>
        Assert.False(SceneFollowLogic.UseSceneWording(T0, DateTime.MinValue));

    [Fact]
    public void A_scene_end_in_the_future_is_never_the_scene_wording() =>
        Assert.False(SceneFollowLogic.UseSceneWording(T0, T0.AddSeconds(5)));   // a clock step back

    [Fact]
    public void The_scene_wording_is_the_projects_own_and_names_no_game_text() =>
        Assert.Equal("Your host's scene took them elsewhere; you were brought along.", SceneFollowLogic.Text.JoinerPulledScene);

    // ---- the end position: the first sample 0.4 s after the end edge (review: the sample at the edge can predate the engine's placement) ----

    private const long Delay = SceneFollowLogic.EndSampleDelayMs;

    [Fact]
    public void The_first_sample_after_the_delay_settles_the_end_position()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 0, 0, "Fader", "a");
        Assert.True(w.End(1000, "a", 0, 0));                     // the poll loop's last sample: still the pre-teleport one
        Assert.True(w.AwaitingEndSample);
        Assert.False(w.NoteSample(1000 + Delay - 1, 5, 5));      // too soon: the engine's placement may not be in the sample yet
        Assert.True(w.NoteSample(1000 + Delay, 300, 400));       // the first sample after the delay: where the scene put the host
        Assert.False(w.AwaitingEndSample);
        Assert.False(w.NoteSample(1000 + Delay + 500, 9, 9));    // only the first counts: the player is free now
        Assert.Equal(500.0, w.TryClose(1000 + Settle)!.Value.MovedM, 6);
    }

    [Fact]
    public void With_no_sample_after_the_end_the_provisional_position_stands()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 0, 0, "Fader", "a");
        w.End(1000, "a", 30, 40);
        Assert.Equal(50.0, w.TryClose(1000 + Settle)!.Value.MovedM, 6);
    }

    [Fact]
    public void A_scene_starting_again_stops_waiting_and_its_own_end_waits_for_its_own_sample()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 0, 0, "Fader", "a");
        w.End(1000, "a", 1, 1);
        Assert.True(w.AwaitingEndSample);
        w.Start(1200, 5, 5, "Ingame", "b");                      // chained
        Assert.False(w.AwaitingEndSample);
        Assert.False(w.NoteSample(1200 + Delay + 10, 77, 77));   // a scene runs: this sample is not an end position
        w.End(3000, "b", 2, 2);
        Assert.True(w.AwaitingEndSample);
        Assert.True(w.NoteSample(3000 + Delay, 60, 80));
        Assert.Equal(100.0, w.TryClose(3000 + Settle)!.Value.MovedM, 6);
    }

    [Fact]
    public void Closing_or_resetting_the_window_stops_it_waiting_for_a_sample()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 0, 0, "Fader", "a"); w.End(10, "a", 0, 0);
        Assert.NotNull(w.TryClose(10 + Settle));
        Assert.False(w.AwaitingEndSample);
        Assert.False(w.NoteSample(10 + Settle + Delay, 1, 1));
        w.Start(5000, 0, 0, "Fader", "b"); w.End(5010, "b", 0, 0);
        w.Reset();
        Assert.False(w.AwaitingEndSample);
    }

    [Fact]
    public void A_sample_with_no_window_is_ignored()
    {
        var w = new SceneFollowLogic.Window();
        Assert.False(w.NoteSample(10_000, 1, 1));
        Assert.False(w.AwaitingEndSample);
    }

    [Fact]
    public void The_sample_delay_is_short_enough_that_free_movement_cannot_reach_the_threshold()
    {
        // a gallop is ~15 m/s: the free movement the delay lets in must stay well under the relocation threshold.
        double gallopMetres = 15.0 * SceneFollowLogic.EndSampleDelayMs / 1000.0;
        Assert.True(gallopMetres < SceneFollowLogic.RelocDefaultM / 2);
    }

    // ---- which scenes the window can follow ----

    [Theory]
    [InlineData("m03_intro", true)]
    [InlineData("A1", true)]
    [InlineData("zachrana_zastav_krvaceni", true)]
    [InlineData("scene-with-dash", false)]      // the end-edge parser refuses it: its end would never arrive
    [InlineData("scene.with.dot", false)]
    [InlineData("scene name", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_names_the_end_edge_parser_accepts_are_tracked(string? name, bool tracked) =>
        Assert.Equal(tracked, SceneFollowLogic.IsTrackableName(name));
}
