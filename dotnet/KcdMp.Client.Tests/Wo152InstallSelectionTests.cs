// Copyright (C) 2026 the Kingdom Come: Together contributors (AUTHORS). SPDX-License-Identifier: GPL-3.0-only
// GPLv3 section 7 additional terms: NOTICE. This project's own code only; Kingdom Come: Deliverance II and its
// content belong to Warhorse Studios and PLAION. Unofficial, free, not affiliated with or endorsed by them.
using KcdMp.Client;
using Xunit;

namespace KcdMp.Client.Tests;

/// <summary>
/// WO-152: which Steam folders count as "the game". The first game also writes a kcd.log and ships a
/// Data\Tables.pak; on a machine that has both, it was picked as soon as it was the newer log, and
/// two Wo121Tests that compare against the install then failed on KCD1's tables.
/// </summary>
public class Wo152InstallSelectionTests
{
    [Theory]
    [InlineData("KingdomComeDeliverance2")]   // retail
    [InlineData("KCD2Mod")]                    // the Modding Tools
    [InlineData("kingdomcomedeliverance2")]
    [InlineData("Kingdom Come Deliverance II")]
    [InlineData("KCD")]                        // unknown KCD-ish name: kept, as before
    public void The_sequels_folders_are_candidates(string folder) =>
        Assert.True(KcdLogLocator.IsKcd2FolderName(folder));

    [Theory]
    [InlineData("KingdomComeDeliverance")]     // the first game
    [InlineData("kingdomcomedeliverance")]
    [InlineData("Cyberpunk 2077")]
    [InlineData("Red Dead Redemption 2")]      // has a 2, but is not KCD-ish
    [InlineData("Sid Meier's Civilization VII")]
    public void The_first_game_and_other_games_are_not(string folder) =>
        Assert.False(KcdLogLocator.IsKcd2FolderName(folder));
}
