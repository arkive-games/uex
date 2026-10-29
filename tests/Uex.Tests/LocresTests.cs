using Uex.Core;
using Xunit;

namespace Uex.Tests;

public class LocresTests
{
    [Theory]
    [InlineData("ProjectPBZ/Content/Localization/Game/zh-Hans/Game.locres", true)]
    [InlineData("Game/Content/Localization/Game/en/Game.LOCRES", true)]
    [InlineData("ProjectPBZ/Content/Localization/Game/Game.locmeta", false)]
    [InlineData("Pal/Content/L10N/en/Pal/DataTable/Text/DT_ItemNameText.uasset", false)]
    public void Handles_only_locres_files(string vpath, bool expected) =>
        Assert.Equal(expected, Locres.Handles(vpath));
}
