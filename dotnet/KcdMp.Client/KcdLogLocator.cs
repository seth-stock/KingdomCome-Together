// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using System.Text.RegularExpressions;

namespace KcdMp.Client;

/// <summary>
/// Finds the game's kcd.log.
///
/// The Modding Tools entry installs separately (steamapps\common\KCD2Mod) and
/// writes its own kcd.log, and it is the build that actually gets launched for
/// modding -- so looking only under KingdomComeDeliverance2 finds nothing, or
/// worse, finds a stale log from the base game. Every KCD-ish folder in every
/// Steam library is scanned and the most recently written match wins.
/// </summary>
public static partial class KcdLogLocator
{
    /// <summary>Full path to the most recently written kcd.log, or null.</summary>
    public static string? Find()
    {
        try
        {
            var logs = new List<FileInfo>();
            foreach (var dir in CandidateDirectories())
            {
                try
                {
                    logs.AddRange(new DirectoryInfo(dir)
                        .EnumerateFiles("kcd.log", SearchOption.TopDirectoryOnly));
                }
                catch { /* unreadable directory */ }
            }

            return logs.Count == 0
                ? null
                : logs.OrderByDescending(f => f.LastWriteTimeUtc).First().FullName;
        }
        catch { return null; }
    }

    /// <summary>Game install directories worth searching, across all Steam libraries.</summary>
    public static IEnumerable<string> CandidateDirectories()
    {
        foreach (var root in SteamLibraryRoots())
        {
            string common = Path.Combine(root, "steamapps", "common");
            if (!Directory.Exists(common)) continue;

            IEnumerable<string> subdirs;
            try { subdirs = Directory.EnumerateDirectories(common); }
            catch { continue; }

            foreach (var d in subdirs)
            {
                if (IsKcd2FolderName(Path.GetFileName(d))) yield return d;
            }
        }
    }

    /// <summary>
    /// True for a Steam folder name that can hold this mod's game: the retail
    /// KingdomComeDeliverance2 or the Modding Tools' KCD2Mod, or any other
    /// KCD-ish name. False for the first game's KingdomComeDeliverance, which
    /// also has a kcd.log and a Data\Tables.pak: a player who ran it more
    /// recently than KCD2 had its log tailed and its tables read as KCD2's
    /// (the walk is alphabetical, and "...Deliverance" sorts before
    /// "...Deliverance2").
    /// </summary>
    public static bool IsKcd2FolderName(string name)
    {
        bool kcdish = name.Contains("Kingdom", StringComparison.OrdinalIgnoreCase) ||
                      name.Contains("KCD", StringComparison.OrdinalIgnoreCase);
        if (!kcdish) return false;
        // The first game's folder is the only KCD-ish name with no "2" and no "II" in it.
        bool sequel = name.Contains('2') || name.Contains("II", StringComparison.Ordinal);
        return sequel || !name.Contains("Kingdom", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Steam install dir plus every library root in libraryfolders.vdf.</summary>
    public static IEnumerable<string> SteamLibraryRoots()
    {
        // The registry on Windows; ~/.local/share/Steam and friends on Linux (GameHost).
        string? steam = GameHost.SteamInstallDir();

        if (steam is null) yield break;
        yield return steam;

        string vdf = Path.Combine(steam, "config", "libraryfolders.vdf");
        if (!File.Exists(vdf)) vdf = Path.Combine(steam, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdf)) yield break;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { steam };
        IEnumerable<string> lines;
        try { lines = File.ReadLines(vdf); }
        catch { yield break; }

        foreach (var line in lines)
        {
            var m = LibraryPathRegex().Match(line.Trim());
            if (!m.Success) continue;
            string p = m.Groups[1].Value.Replace(@"\\", @"\");
            if (seen.Add(p)) yield return p;
        }
    }

    [GeneratedRegex(@"""path""\s+""([^""]+)""", RegexOptions.IgnoreCase)]
    private static partial Regex LibraryPathRegex();
}
