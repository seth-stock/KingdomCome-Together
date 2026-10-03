// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.Json;
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>WO-154: the launcher's story-sync line and the game-build warning.</summary>
public class Wo154StatusTests
{
    [Theory]
    [InlineData(false, true, false, true, 0, "solo")]      // no session
    [InlineData(true, false, true, true, 0, "solo")]       // a session, nobody else in it
    [InlineData(true, true, true, true, 0, "in-sync")]
    [InlineData(true, true, false, true, 0, "in-sync")]
    [InlineData(true, true, false, false, 0, "catching-up")]   // a joiner whose steps wait for the host's checkpoint
    [InlineData(true, true, true, false, 0, "in-sync")]        // the host never waits for anyone
    [InlineData(true, true, false, true, 2, "drifting")]
    [InlineData(true, true, true, true, 1, "drifting")]
    [InlineData(true, true, false, false, 3, "catching-up")]   // catching up outranks drifting: the drift is what it is catching up on
    public void The_sync_state_follows_the_session(bool inSession, bool peer, bool host, bool caughtUp, int div, string want) =>
        Assert.Equal(want, CoopStatus.SyncState(inSession, peer, host, caughtUp, div));

    [Fact]
    public void Every_state_has_words_except_solo()
    {
        foreach (var s in new[] { "in-sync", "drifting", "catching-up" }) Assert.NotEmpty(CoopStatus.SyncText(s));
        Assert.Equal("", CoopStatus.SyncText("solo"));
        Assert.Equal("", CoopStatus.SyncText("nonsense"));
    }

    [Theory]
    [InlineData("ver_01_05_05", "ver_01_05_05")]
    [InlineData("<Value>ver_01_05_06</Value>", "ver_01_05_06")]
    [InlineData("wh_sys_GameReleaseVersion = ver_01_05_06 []", "ver_01_05_06")]
    [InlineData("nothing here", null)]
    [InlineData("", null)]
    [InlineData(null, null)]
    public void The_build_is_read_out_of_the_cvar_reply(string? raw, string? want) => Assert.Equal(want, CoopStatus.ParseBuild(raw));

    [Fact]
    public void Builds_read_as_versions() => Assert.Equal("1.5.6", CoopStatus.Pretty("ver_01_05_06"));

    [Fact]
    public void The_verified_build_has_no_warning_and_an_unread_one_makes_no_claim()
    {
        Assert.Null(CoopStatus.BuildWarning("ver_01_05_05"));
        Assert.Null(CoopStatus.BuildWarning(null));
    }

    [Theory]
    [InlineData("ver_01_05_06")]
    [InlineData("ver_01_05_04")]
    [InlineData("ver_01_06_00")]
    public void Any_other_build_is_warned_about(string build)
    {
        var w = CoopStatus.BuildWarning(build)!;
        Assert.Contains(CoopStatus.Pretty(build), w);
        Assert.Contains("1.5.5", w);
        Assert.Contains("same build", w);
    }

    [Fact]
    public void The_json_parses_and_carries_every_field()
    {
        string j = CoopStatus.Json("joiner", "in-sync", "Wedding \"Crashers\"", "the wedding in Semine", true, "ver_01_05_05", null);
        using var d = JsonDocument.Parse(j);
        var r = d.RootElement;
        Assert.Equal("joiner", r.GetProperty("role").GetString());
        Assert.Equal("in-sync", r.GetProperty("sync").GetString());
        Assert.Equal("Story: IN SYNC", r.GetProperty("syncText").GetString());
        Assert.Equal("Wedding \"Crashers\"", r.GetProperty("section").GetString());   // quotes survive
        Assert.True(r.GetProperty("tether").GetBoolean());
        Assert.Equal("ver_01_05_05", r.GetProperty("gameBuild").GetString());
        Assert.Equal(JsonValueKind.Null, r.GetProperty("buildWarning").ValueKind);
    }

    [Fact]
    public void The_json_with_nothing_known_is_still_valid()
    {
        using var d = JsonDocument.Parse(CoopStatus.Json("none", "solo", "", "", false, null, null));
        Assert.Equal(JsonValueKind.Null, d.RootElement.GetProperty("gameBuild").ValueKind);
        Assert.False(d.RootElement.GetProperty("tether").GetBoolean());
    }
}
