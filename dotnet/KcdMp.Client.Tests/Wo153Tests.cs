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

    [Fact]
    public void One_scene_opens_and_closes_a_window_at_its_first_start_position()
    {
        var w = new SceneFollowLogic.Window();
        Assert.True(w.Start(1000, 100, 200, "Ingame", "a"));
        var closed = w.End(9000, "a");
        Assert.NotNull(closed);
        Assert.Equal((100f, 200f, true), closed.Value);
        Assert.False(w.Open);
    }

    [Fact]
    public void Nested_scenes_close_only_at_the_last_end_and_keep_the_first_start()
    {
        var w = new SceneFollowLogic.Window();
        Assert.True(w.Start(0, 10, 10, "Ingame", "outer"));
        Assert.False(w.Start(500, 90, 90, "Fader", "inner"));   // a fader inside: not a new window
        Assert.Equal(2, w.Depth);
        Assert.Null(w.End(800, "inner"));                         // the fader ends: the window stays open
        var closed = w.End(5000, "outer");
        Assert.Equal((10f, 10f, true), closed!.Value);            // where the FIRST scene began; story by the ingame scene
    }

    [Fact]
    public void The_outer_scene_may_end_before_the_inner_one()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Fader", "f");
        w.Start(10, 2, 2, "Ingame", "i");
        Assert.Null(w.End(20, "f"));
        Assert.Equal((1f, 1f, true), w.End(30, "i")!.Value);
    }

    [Fact]
    public void A_window_of_only_faders_is_not_a_story_window()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 2, "Fader", "f");
        Assert.False(w.End(100, "f")!.Value.Story);
    }

    [Fact]
    public void An_end_with_no_start_closes_nothing()
    {
        var w = new SceneFollowLogic.Window();
        Assert.Null(w.End(100, "a"));       // a load, a duplicate release
        Assert.False(w.Open);
    }

    [Fact]
    public void A_duplicate_end_after_the_window_closed_closes_nothing_again()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 0, 0, "Rendered", "a");
        Assert.NotNull(w.End(10, "a"));
        Assert.Null(w.End(20, "a"));
    }

    [Fact]
    public void A_duplicate_end_never_closes_an_outer_scene_early()
    {
        // The guard gives up on the inner scene (end edge sent), then the engine really releases it: two ends, one name.
        var w = new SceneFollowLogic.Window();
        w.Start(0, 10, 10, "Ingame", "outer");
        w.Start(5, 20, 20, "Fader", "inner");
        Assert.Null(w.End(100, "inner"));
        Assert.Null(w.End(200, "inner"));    // the late real release: no running scene has this name any more
        Assert.True(w.Open);
        Assert.NotNull(w.End(300, "outer"));
    }

    [Fact]
    public void An_end_naming_a_scene_that_never_started_leaves_the_window_open()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a");
        Assert.Null(w.End(10, "other"));
        Assert.True(w.Open);
    }

    [Fact]
    public void A_start_repeated_for_the_same_scene_counts_once()
    {
        var w = new SceneFollowLogic.Window();
        Assert.True(w.Start(0, 1, 1, "Ingame", "a"));
        Assert.False(w.Start(5, 9, 9, "Ingame", "a"));
        Assert.Equal(1, w.Depth);
        Assert.Equal((1f, 1f, true), w.End(10, "a")!.Value);   // and the start stays the first one
    }

    [Fact]
    public void A_window_that_never_closed_is_dropped_not_evaluated_against_an_old_position()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 500, 500, "Ingame", "old");     // its end edge was swallowed by a load
        long later = SceneFollowLogic.StaleWindowMs + 1;
        Assert.True(w.Start(later, 7, 8, "Fader", "new"));   // a fresh window at the NEW position
        Assert.Equal((7f, 8f, false), w.End(later + 50, "new")!.Value);
    }

    [Fact]
    public void A_stale_window_ending_is_dropped()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a");
        Assert.Null(w.End(SceneFollowLogic.StaleWindowMs + 1, "a"));
        Assert.False(w.Open);
    }

    [Fact]
    public void Reset_forgets_everything()
    {
        var w = new SceneFollowLogic.Window();
        w.Start(0, 1, 1, "Ingame", "a"); w.Start(1, 1, 1, "Fader", "b");
        w.Reset();
        Assert.False(w.Open);
        Assert.Null(w.End(2, "a"));
    }

    [Fact]
    public void The_scene_wording_is_the_projects_own_and_names_no_game_text() =>
        Assert.Equal("Your host's scene took them elsewhere; you were brought along.", SceneFollowLogic.Text.JoinerPulledScene);
}
