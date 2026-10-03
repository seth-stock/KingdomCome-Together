// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text;
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// WO-154: what the launcher shows about the co-op story: "in sync / drifting / catching up", the locked section the
/// host is in, and a warning when this game is not the build the mod was verified on (docs/WO-153-findings.md).
/// Pure; Wo154StatusTests pins it. GameBridge publishes it at GET /coop-status.
/// </summary>
public static partial class CoopStatus
{
    /// <summary>The game builds (wh_sys_GameReleaseVersion) this mod was verified on, with live runs. Nothing else is claimed.</summary>
    public static readonly string[] VerifiedBuilds = ["ver_01_05_05"];
    public const string VerifiedBuildsText = "1.5.5";

    /// <summary>
    /// solo = nobody to be in sync with; catching-up = a joiner whose own steps wait for the host's checkpoint (WO-151);
    /// drifting = a standing difference between the two players' stories (WO-96); in-sync otherwise.
    /// </summary>
    public static string SyncState(bool inSession, bool anyPeer, bool isHost, bool caughtUp, int divergences)
    {
        if (!inSession || !anyPeer) return "solo";
        if (!isHost && !caughtUp) return "catching-up";
        if (divergences > 0) return "drifting";
        return "in-sync";
    }

    /// <summary>The words the launcher puts on each state (this project's own).</summary>
    public static string SyncText(string state) => state switch
    {
        "in-sync" => "Story: IN SYNC",
        "drifting" => "Story: DRIFTING (you and your partner are at different points)",
        "catching-up" => "Story: CATCHING UP with your host",
        _ => "",
    };

    [GeneratedRegex(@"ver_(\d+)_(\d+)_(\d+)")]
    private static partial Regex ReleaseRx();

    /// <summary>"ver_01_05_05" out of whatever the cvar read returned (a bare value or the API's XML around it); null when none.</summary>
    public static string? ParseBuild(string? raw)
    {
        if (string.IsNullOrEmpty(raw)) return null;
        var m = ReleaseRx().Match(raw);
        return m.Success ? m.Value : null;
    }

    /// <summary>"ver_01_05_05" -> "1.5.5".</summary>
    public static string Pretty(string build)
    {
        var m = ReleaseRx().Match(build);
        return m.Success ? $"{int.Parse(m.Groups[1].Value)}.{int.Parse(m.Groups[2].Value)}.{int.Parse(m.Groups[3].Value)}" : build;
    }

    /// <summary>
    /// A warning when the game is not a build this mod was verified on; null when it is (or when the build could not be read:
    /// no claim either way). Saves from a NEWER game build are silently refused by an older engine (observed, 2026-10-02: a
    /// 1.5.6 save in the 1.5.5 Modding Tools does nothing); older saves load.
    /// </summary>
    public static string? BuildWarning(string? build)
    {
        if (build is null) return null;
        if (Array.IndexOf(VerifiedBuilds, build) >= 0) return null;
        return $"This game is build {Pretty(build)}; the mod was verified on {VerifiedBuildsText}. Everyone must run the same build, " +
               "and a save from a newer game than this one will not load.";
    }

    public static string Json(string role, string sync, string section, string sectionWhy, bool tether, string? build, string? warning)
    {
        var sb = new StringBuilder(260);
        sb.Append('{').Append(Js.Str("role", role)).Append(',').Append(Js.Str("sync", sync)).Append(',').Append(Js.Str("syncText", SyncText(sync)))
          .Append(',').Append(Js.Str("section", section)).Append(',').Append(Js.Str("sectionWhy", sectionWhy))
          .Append(",\"tether\":").Append(tether ? "true" : "false")
          .Append(',').Append(Js.Str("gameBuild", build)).Append(',').Append(Js.Str("buildWarning", warning)).Append('}');
        return sb.ToString();
    }
}
