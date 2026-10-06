// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// Linux support (docs/LINUX.md): finding Steam, the Proton prefix and the saves from a native Linux agent.
/// (synthetic) -- a fake Steam library built in a temp folder. What this cannot prove is that a real Proton prefix has this layout
/// on every distro; the layout is Proton's documented one (compatdata/&lt;appid&gt;/pfx/drive_c/users/steamuser).
/// </summary>
public class GameHostTests : IDisposable
{
    private readonly string _lib = Path.Combine(Path.GetTempPath(), "kcdmp-gamehost-" + Guid.NewGuid().ToString("N")[..8]);

    public GameHostTests() => Directory.CreateDirectory(_lib);
    public void Dispose() { try { Directory.Delete(_lib, true); } catch { } }

    // <lib>/steamapps/compatdata/2429020/pfx/drive_c/users/steamuser/<savedGames>/<kcd2>/saves/playline1
    private string MakePrefix(string savedGames = "Saved Games", string kcd2 = "KingdomCome2", uint appId = 2429020)
    {
        string pfx = Path.Combine(_lib, "steamapps", "compatdata", appId.ToString(), "pfx");
        Directory.CreateDirectory(Path.Combine(pfx, "drive_c", "users", "steamuser", savedGames, kcd2, "saves", "playline1"));
        return pfx;
    }

    [Fact]
    public void ResolveCase_takes_the_real_spelling_of_each_segment()
    {
        if (OperatingSystem.IsWindows()) return;   // a case-insensitive file system: nothing to resolve
        string pfx = MakePrefix(savedGames: "saved games", kcd2: "kingdomcome2");
        string got = GameHost.ResolveCase(pfx, ["drive_c", "users", "steamuser", "Saved Games", "KingdomCome2", "saves"]);
        Assert.Equal(Path.Combine(pfx, "drive_c", "users", "steamuser", "saved games", "kingdomcome2", "saves"), got);
        Assert.True(Directory.Exists(got));
    }

    [Fact]
    public void ResolveCase_keeps_the_rest_verbatim_after_a_missing_segment()
    {
        string pfx = MakePrefix();
        string got = GameHost.ResolveCase(pfx, ["drive_c", "nope", "A", "b"]);
        Assert.Equal(Path.Combine(pfx, "drive_c", "nope", "A", "b"), got);
    }

    [Fact]
    public void A_wine_path_maps_into_the_prefix()
    {
        string pfx = MakePrefix(savedGames: "saved games", kcd2: "KingdomCome2");
        string win = @"C:\users\steamuser\Saved Games\KingdomCome2";
        string got = GameHost.MapWinePath(win, [pfx]);
        if (OperatingSystem.IsWindows()) { Assert.Equal(win, got); return; }   // on Windows the path is already real
        Assert.Equal(Path.Combine(pfx, "drive_c", "users", "steamuser", "saved games", "KingdomCome2"), got);
        Assert.True(Directory.Exists(got));
    }

    [Fact]
    public void Z_is_the_hosts_root_and_a_host_path_is_left_alone()
    {
        if (OperatingSystem.IsWindows()) return;
        Assert.Equal("/tmp", GameHost.MapWinePath(@"Z:\tmp", []));
        Assert.Equal("/home/x/y", GameHost.MapWinePath("/home/x/y", []));
        Assert.Equal("", GameHost.MapWinePath("", []));
    }

    [Fact]
    public void The_prefix_that_has_the_file_wins_over_the_first_one()
    {
        if (OperatingSystem.IsWindows()) return;
        string retail = MakePrefix(appId: 1771300);   // retail has saves too
        Directory.CreateDirectory(Path.Combine(retail, "drive_c", "users", "steamuser", "Saved Games", "KingdomCome2", "only-retail"));
        string tools = MakePrefix(appId: 2429020);
        string got = GameHost.MapWinePath(@"C:\users\steamuser\Saved Games\KingdomCome2\only-retail", [tools, retail]);
        Assert.StartsWith(retail, got);
    }

    [Fact]
    public void Proton_prefixes_are_found_for_both_app_ids_and_only_where_they_exist()
    {
        MakePrefix(appId: 2429020);
        MakePrefix(appId: 1771300);
        var found = GameHost.ProtonPrefixes([_lib, Path.Combine(_lib, "no-such-library")]).Where(p => p.StartsWith(_lib)).ToArray();
        Assert.Equal(2, found.Length);
        Assert.All(found, p => Assert.EndsWith("pfx", p));
    }

    [Fact]
    public void Linux_steam_candidates_name_the_usual_places()
    {
        var c = GameHost.LinuxSteamCandidates("/home/me").Select(p => p.Replace('\\', '/')).ToArray();
        Assert.Contains("/home/me/.steam/steam", c);
        Assert.Contains("/home/me/.local/share/Steam", c);
        Assert.Contains("/home/me/.var/app/com.valvesoftware.Steam/.local/share/Steam", c);
    }

    [Fact]
    public void Steam_p2p_declines_cleanly_off_windows()
    {
        if (OperatingSystem.IsWindows()) return;
        var s = KcdMp.Steam.SteamSession.TryStart(480, out var failure, out var detail);
        Assert.Null(s);
        Assert.Equal(KcdMp.Steam.SteamStartFailure.NoSteamDll, failure);
        Assert.Contains("Linux", detail);
    }
}
