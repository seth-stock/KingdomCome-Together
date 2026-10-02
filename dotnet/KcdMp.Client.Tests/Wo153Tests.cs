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



    // ---- the window ----

    private const long Settle = SceneFollowLogic.SettleMs;

    [Fact]
    public void One_scene_opens_a_window_and_closes_after_the_quiet_period_at_its_start_position()
    {
        var w = new SceneFollowLogic.Window();
        Assert.True(w.Start(1000, 100, 200, "Ingame", "a"));
        Assert.True(w.End(9000, "a"));                          // nothing runs: the caller starts its timer
        Assert.True(w.Open);                                    // ...the window is still open until the quiet period passes
        Assert.Null(w.TryClose(9000 + Settle - 1000));          // too early
        var closed = w.TryClose(9000 + Settle);
        Assert.Equal((100f, 200f, true), closed!.Value);
        Assert.False(w.Open);
        Assert.Null(w.TryClose(9000 + Settle + 5));             // a second timer for the same window finds it closed
    }

    [Fact]
    public void A_timer_that_fires_a_little_early_still_closes_the_window()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a"); w.End(100, "a");
        Assert.NotNull(w.TryClose(100 + Settle - SceneFollowLogic.SettleSlackMs));
    }

    [Fact]
    public void Nested_scenes_close_only_at_the_last_end_and_keep_the_first_start()
    {
        var w = new SceneFollowLogic.Window();
        Assert.True(w.Start(0, 10, 10, "Ingame", "outer"));
        Assert.False(w.Start(500, 90, 90, "Fader", "inner"));   // a fader inside: not a new window
        Assert.Equal(2, w.Depth);
        Assert.False(w.End(800, "inner"));                       // others still run: no timer
        Assert.Null(w.TryClose(800 + Settle));                   // and a stray timer finds a running scene
        Assert.True(w.End(5000, "outer"));
        Assert.Equal((10f, 10f, true), w.TryClose(5000 + Settle)!.Value);
    }

    [Fact]
    public void The_outer_scene_may_end_before_the_inner_one()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Fader", "f");
        w.Start(10, 2, 2, "Ingame", "i");
        Assert.False(w.End(20, "f"));
        Assert.True(w.End(30, "i"));
        Assert.Equal((1f, 1f, true), w.TryClose(30 + Settle)!.Value);
    }

    [Fact]
    public void A_window_of_only_faders_is_not_a_story_window()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 2, "Fader", "f"); w.End(100, "f");
        Assert.False(w.TryClose(100 + Settle)!.Value.Story);
    }

    [Fact]
    public void Chained_scenes_are_one_window_so_a_relocation_between_them_is_not_lost()
    {
        // review finding: a scene closes and another begins within the quiet period. Each half measured alone would
        // lose the move between them; the window must run from the FIRST start to the LAST end.
        var w = new SceneFollowLogic.Window();
        Assert.True(w.Start(0, 10, 10, "Fader", "a"));
        Assert.True(w.End(1000, "a"));                           // a timer is started for t = 3000
        Assert.False(w.Start(1500, 300, 300, "Ingame", "b"));    // the next scene begins inside the quiet period: the SAME window
        Assert.Null(w.TryClose(1000 + Settle));                  // the first timer fires while b runs: nothing closes
        Assert.True(w.End(4000, "b"));
        var closed = w.TryClose(4000 + Settle)!.Value;
        Assert.Equal((10f, 10f, true), closed);                  // the first position, the story flag of b
    }

    [Fact]
    public void A_start_after_the_window_closed_opens_a_new_one()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a"); w.End(10, "a"); Assert.NotNull(w.TryClose(10 + Settle));
        Assert.True(w.Start(20_000, 7, 8, "Fader", "b"));
        w.End(20_100, "b");
        Assert.Equal((7f, 8f, false), w.TryClose(20_100 + Settle)!.Value);
    }

    [Fact]
    public void An_end_with_no_start_changes_nothing()
    {
        var w = new SceneFollowLogic.Window();
        Assert.False(w.End(100, "a"));      // a load, a duplicate release
        Assert.False(w.Open);
        Assert.Null(w.TryClose(100 + Settle));
    }

    [Fact]
    public void A_duplicate_end_after_the_scene_ended_is_ignored()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 0, 0, "Rendered", "a");
        Assert.True(w.End(10, "a"));
        Assert.False(w.End(20, "a"));
        Assert.NotNull(w.TryClose(10 + Settle));
    }

    [Fact]
    public void A_duplicate_end_never_closes_an_outer_scene_early()
    {
        // The guard gives up on the inner scene (end edge sent), then the engine really releases it: two ends, one name.
        var w = new SceneFollowLogic.Window();
        w.Start(0, 10, 10, "Ingame", "outer");
        w.Start(5, 20, 20, "Fader", "inner");
        Assert.False(w.End(100, "inner"));
        Assert.False(w.End(200, "inner"));   // the late real release: no running scene has this name any more
        Assert.True(w.Open);
        Assert.Equal(1, w.Depth);
        Assert.True(w.End(300, "outer"));
    }

    [Fact]
    public void An_end_naming_a_scene_that_never_started_leaves_the_window_open()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a");
        Assert.False(w.End(10, "other"));
        Assert.True(w.Open);
        Assert.Equal(1, w.Depth);
    }

    [Fact]
    public void A_start_repeated_for_the_same_scene_counts_once()
    {
        var w = new SceneFollowLogic.Window();
        Assert.True(w.Start(0, 1, 1, "Ingame", "a"));
        Assert.False(w.Start(5, 9, 9, "Ingame", "a"));
        Assert.Equal(1, w.Depth);
        w.End(10, "a");
        Assert.Equal((1f, 1f, true), w.TryClose(10 + Settle)!.Value);   // and the start stays the first one
    }

    [Fact]
    public void A_long_scene_is_evaluated_at_its_real_end_however_long_it_ran()
    {
        // review finding: an end was refused for the window's age, so the longest scenes brought nobody along.
        var w = new SceneFollowLogic.Window();
        w.Start(0, 5, 5, "Ingame", "long");
        long hours = 3L * 60 * 60 * 1000;
        Assert.True(w.End(hours, "long"));
        Assert.Equal((5f, 5f, true), w.TryClose(hours + Settle)!.Value);
    }

    [Fact]
    public void A_nested_scene_starting_long_into_a_running_one_does_not_wipe_it()
    {
        // ...nor does a start refuse to nest: staleness is measured from the last EDGE, and the outer scene's own start was an edge.
        var w = new SceneFollowLogic.Window();
        w.Start(0, 5, 5, "Ingame", "outer");
        w.Start(SceneFollowLogic.StaleWindowMs - 1, 9, 9, "Fader", "inner");
        Assert.Equal(2, w.Depth);
    }

    [Fact]
    public void An_orphan_window_with_no_edge_for_half_an_hour_is_dropped_by_the_next_start()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 500, 500, "Ingame", "old");     // its end edge was never logged
        long later = SceneFollowLogic.StaleWindowMs + 1;
        Assert.True(w.Start(later, 7, 8, "Fader", "new"));      // a fresh window at the NEW position
        w.End(later + 50, "new");
        Assert.Equal((7f, 8f, false), w.TryClose(later + 50 + Settle)!.Value);
    }

    [Fact]
    public void Reset_forgets_everything_a_load_or_a_disconnect_leaves_no_window_behind()
    {
        // review finding: a load never reset the window, so it stayed open with scene names that could no longer end.
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a"); w.Start(1, 1, 1, "Fader", "b");
        w.Reset();
        Assert.False(w.Open);
        Assert.Equal(0, w.Depth);
        Assert.Null(w.TryClose(10_000));
        Assert.False(w.End(2, "a"));
        // and the first scene after it opens a clean window
        Assert.True(w.Start(5000, 42, 43, "Fader", "c"));
        w.End(5100, "c");
        Assert.Equal((42f, 43f, false), w.TryClose(5100 + Settle)!.Value);
    }

    // ---- the joiner's wording of a pull ----

    private static readonly DateTime T0 = new(2026, 10, 2, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(0, true)]
    [InlineData(3, true)]
    [InlineData(9.9, true)]
    [InlineData(10, false)]
    [InlineData(25, false)]     // review finding: a real fast travel 25 s after a scene is a fast travel
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
}
