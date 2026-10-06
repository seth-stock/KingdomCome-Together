// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using Microsoft.Win32;

namespace KcdMp.Client;

/// <summary>
/// Where Steam, the game and its saves are on THIS machine (docs/LINUX.md).
///
/// On Windows that is the registry and the Saved Games known folder, as before. On Linux the game runs under Proton, so:
///   * Steam is ~/.local/share/Steam (or ~/.steam/steam, or the Flatpak's copy);
///   * the game's Windows view of the world is a prefix: &lt;library&gt;/steamapps/compatdata/&lt;appid&gt;/pfx, where "C:\users\steamuser\Saved Games"
///     is &lt;prefix&gt;/drive_c/users/steamuser/Saved Games, and kcd.log's "User folder is 'C:\...'" has to be mapped into it;
///   * names are case sensitive on the host while the game (Windows) is not, so every segment is matched case-insensitively.
/// STEAM_COMPAT_DATA_PATH / WINEPREFIX (what the launch script exports) name the prefix outright and win.
/// </summary>
public static class GameHost
{
    /// <summary>The Steam app ids the game runs as: the Modding Tools build (what the mod needs) and retail.</summary>
    public static readonly uint[] AppIds = [2429020, 1771300];

    /// <summary>A registry string, or null (always null off Windows).</summary>
    public static string? RegistryString(string key, string value)
    {
        if (!OperatingSystem.IsWindows()) return null;
        try { return Registry.GetValue(key, value, null) as string; }
        catch { return null; }
    }

    /// <summary>Steam's install directory, or null.</summary>
    public static string? SteamInstallDir()
    {
        if (OperatingSystem.IsWindows())
            return RegistryString(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath")
                ?? RegistryString(@"HKEY_LOCAL_MACHINE\SOFTWARE\Valve\Steam", "InstallPath")
                ?? RegistryString(@"HKEY_CURRENT_USER\SOFTWARE\Valve\Steam", "SteamPath");
        string? env = Environment.GetEnvironmentVariable("STEAM_COMPAT_CLIENT_INSTALL_PATH");
        if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env)) return Real(env);
        foreach (var c in LinuxSteamCandidates(Environment.GetEnvironmentVariable("HOME") ?? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)))
            if (Directory.Exists(Path.Combine(c, "steamapps")) || File.Exists(Path.Combine(c, "config", "libraryfolders.vdf"))) return Real(c);
        return null;
    }

    /// <summary>Where Steam lives on Linux, most likely first: the usual symlink, the XDG data dir, Flatpak, then the old layout.</summary>
    public static IEnumerable<string> LinuxSteamCandidates(string home)
    {
        yield return Path.Combine(home, ".steam", "steam");
        yield return Path.Combine(home, ".local", "share", "Steam");
        yield return Path.Combine(home, ".var", "app", "com.valvesoftware.Steam", ".local", "share", "Steam");
        yield return Path.Combine(home, ".steam", "root");
        yield return Path.Combine(home, ".steam");
    }

    private static string Real(string dir)
    {
        try { return new DirectoryInfo(dir).ResolveLinkTarget(true)?.FullName ?? Path.GetFullPath(dir); }
        catch { return dir; }
    }

    /// <summary>Every Proton prefix (…/pfx) the game's app ids have in the given Steam libraries, plus the one the environment names.</summary>
    public static IEnumerable<string> ProtonPrefixes(IEnumerable<string> libraryRoots)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var env in new[] { Environment.GetEnvironmentVariable("WINEPREFIX"), PfxOf(Environment.GetEnvironmentVariable("STEAM_COMPAT_DATA_PATH")) })
            if (!string.IsNullOrWhiteSpace(env) && Directory.Exists(env) && seen.Add(env)) yield return env;
        foreach (var lib in libraryRoots)
            foreach (var id in AppIds)
            {
                string pfx = Path.Combine(lib, "steamapps", "compatdata", id.ToString(), "pfx");
                if (Directory.Exists(pfx) && seen.Add(pfx)) yield return pfx;
            }
    }

    private static string? PfxOf(string? compatData) =>
        string.IsNullOrWhiteSpace(compatData) ? null : Path.Combine(compatData, "pfx");

    /// <summary>
    /// A path as the game's Windows side writes it ("C:\users\steamuser\Saved Games\KingdomCome2") as a path this process can open.
    /// Unchanged on Windows. On Linux "C:" is the prefix's drive_c, "Z:" is the host's root, and each segment is matched
    /// case-insensitively; the first prefix in which the result exists wins, else the first prefix's reading.
    /// </summary>
    public static string MapWinePath(string winPath, IEnumerable<string> prefixes)
    {
        if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(winPath)) return winPath;
        if (winPath.Length < 2 || winPath[1] != ':' || !char.IsLetter(winPath[0])) return winPath;   // already a host path
        char drive = char.ToLowerInvariant(winPath[0]);
        var parts = winPath[2..].Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (drive == 'z') return ResolveCase("/", parts);
        string? first = null;
        foreach (var pfx in prefixes)
        {
            string mapped = ResolveCase(Path.Combine(pfx, drive == 'c' ? "drive_c" : "dosdevices/" + drive + ":"), parts);
            first ??= mapped;
            if (Directory.Exists(mapped) || File.Exists(mapped)) return mapped;
        }
        return first ?? winPath;
    }

    /// <summary>Joins <paramref name="parts"/> under <paramref name="root"/>, taking each name's real spelling when one differs only in case.</summary>
    public static string ResolveCase(string root, IEnumerable<string> parts)
    {
        string cur = root;
        bool exact = true;   // once a segment is missing, the rest cannot exist: append verbatim
        foreach (var part in parts)
        {
            string next = Path.Combine(cur, part);
            if (exact && !Directory.Exists(next) && !File.Exists(next))
            {
                string? hit = null;
                try
                {
                    foreach (var e in Directory.EnumerateFileSystemEntries(cur))
                        if (string.Equals(Path.GetFileName(e), part, StringComparison.OrdinalIgnoreCase)) { hit = e; break; }
                }
                catch { /* unreadable: keep the spelling we were given */ }
                if (hit is null) exact = false; else next = hit;
            }
            cur = next;
        }
        return cur;
    }

    /// <summary>Every Steam library's root (Steam's own folder plus those in libraryfolders.vdf).</summary>
    public static IEnumerable<string> LibraryRoots() => KcdLogLocator.SteamLibraryRoots();

    /// <summary>
    /// The engine's user folder (where saves/ lives): Windows' Saved Games\KingdomCome2, or on Linux the one inside the Proton prefix
    /// that has saves, newest first. Null when nothing is found.
    /// </summary>
    public static string? FindUserFolder()
    {
        if (OperatingSystem.IsWindows()) return null;   // Windows callers keep the shell-folder lookup they already had
        string? best = null; DateTime bestT = DateTime.MinValue;
        foreach (var pfx in ProtonPrefixes(LibraryRoots()))
        {
            string uf = ResolveCase(pfx, ["drive_c", "users", "steamuser", "Saved Games", "KingdomCome2"]);
            string saves = Path.Combine(uf, "saves");
            if (!Directory.Exists(saves)) continue;
            DateTime t = Directory.GetLastWriteTimeUtc(saves);
            if (t >= bestT) { best = uf; bestT = t; }
        }
        return best;
    }
}
