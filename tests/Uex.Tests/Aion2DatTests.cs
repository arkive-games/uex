using CUE4Parse.UE4.Versions;
using Uex.Core;
using Xunit;

namespace Uex.Tests;

public class Aion2DatTests
{
    [Theory]
    // Known Data dirs under GAME_Aion2 → handled.
    [InlineData("AION2/Content/Data/Table/AbnormalVolume.dat", true)]
    [InlineData("AION2/Content/Data/WorldMap/World_L_A.dat", true)]
    [InlineData("AION2/Content/Data/MapDataHierarchy/MapDataHierarchy.dat", true)]
    [InlineData("AION2/Content/Data/Map/Arena/Battlefield_A_1/MapData.dat", true)]
    [InlineData("AION2/Content/Data/MapEvent/Arkanis_G_01_MapEvent.dat", true)]
    // Wrong extension, or not under Data → not handled.
    [InlineData("AION2/Content/Data/Table/AbnormalVolume.uasset", false)]
    [InlineData("AION2/Content/System/Data/Something.dat", false)]
    [InlineData("AION2/Content/Misc/Readme.dat", false)]
    // key_manifest is the key source, never decoded.
    [InlineData("AION2/Content/Data/key_manifest.dat", false)]
    [InlineData("AION2/Content/Data/Table/key_manifest.dat", false)]
    public void Handles_aion2_dat_paths(string vpath, bool expected) =>
        Assert.Equal(expected, Aion2Dat.Handles(EGame.GAME_Aion2, vpath));

    [Theory]
    [InlineData(EGame.GAME_UE4_27)]
    [InlineData(EGame.GAME_UE5_1)]
    public void Handles_false_for_other_games(EGame game) =>
        Assert.False(Aion2Dat.Handles(game, "AION2/Content/Data/Table/AbnormalVolume.dat"));

    [Fact]
    public void Handles_is_case_insensitive() =>
        Assert.True(Aion2Dat.Handles(EGame.GAME_Aion2, "aion2/content/data/worldmap/world_l_a.DAT"));
}
