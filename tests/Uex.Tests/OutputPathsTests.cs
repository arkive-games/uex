using Uex.Core;
using Xunit;

namespace Uex.Tests;

public class OutputPathsTests
{
    [Theory]
    [InlineData("Pal/Content/Pal/DataTable/DT_X.uasset", true)]
    [InlineData("Pal/Content/Pal/Map/World.umap", true)]
    [InlineData("Pal/Config/DefaultGame.ini", false)]
    [InlineData("Pal/Content/L10N/en/Game.locres", false)]
    public void IsPackage_by_extension(string vpath, bool expected) =>
        Assert.Equal(expected, OutputPaths.IsPackage(vpath));

    [Theory]
    [InlineData("Pal/Content/Pal/DataTable/DT_X.uexp")]
    [InlineData("Pal/Content/Pal/Texture/T_Icon.ubulk")]
    [InlineData("Pal/Content/Pal/Texture/T_Icon.uptnl")]
    public void IsPackagePart_true_for_companions(string vpath) =>
        Assert.True(OutputPaths.IsPackagePart(vpath));

    [Fact]
    public void IsPackagePart_false_for_packages_and_raw()
    {
        Assert.False(OutputPaths.IsPackagePart("A/B.uasset"));
        Assert.False(OutputPaths.IsPackagePart("A/B.ini"));
    }

    [Fact]
    public void PackageJson_swaps_extension()
    {
        Assert.Equal("Pal/Content/Pal/DataTable/DT_X.json",
            OutputPaths.ForPackageJson("Pal/Content/Pal/DataTable/DT_X.uasset"));
    }

    [Fact]
    public void TexturePng_first_texture_uses_package_name_extras_suffixed()
    {
        Assert.Equal("Pal/Content/T_Icon.png", OutputPaths.ForTexturePng("Pal/Content/T_Icon.uasset", "T_Icon", 0));
        Assert.Equal("Pal/Content/T_Icon.Second.png", OutputPaths.ForTexturePng("Pal/Content/T_Icon.uasset", "Second", 1));
    }

    [Fact]
    public void Raw_keeps_path_as_is() =>
        Assert.Equal("Pal/Config/DefaultGame.ini", OutputPaths.ForRaw("Pal/Config/DefaultGame.ini"));

    [Theory]
    [InlineData("Pal/Content/Pal/DataTable/DT_X.uasset", new[] { "Pal/Content/Pal" }, true)]
    [InlineData("Pal/Content/PalOther/DT_X.uasset", new[] { "Pal/Content/Pal" }, false)] // prefix must be segment-aligned
    [InlineData("Pal/Config/DefaultGame.ini", new[] { "Pal/Content/Pal", "Pal/Config" }, true)]
    [InlineData("Engine/Content/X.uasset", new[] { "Pal" }, false)]
    public void IsUnderAnyRoot_segment_aligned(string vpath, string[] roots, bool expected) =>
        Assert.Equal(expected, OutputPaths.IsUnderAnyRoot(vpath, roots));

    [Fact]
    public void IsUnderAnyRoot_is_case_insensitive_and_tolerates_slashes() =>
        Assert.True(OutputPaths.IsUnderAnyRoot("Pal/Content/Pal/DT.uasset", ["/pal/content/pal/"]));
}
