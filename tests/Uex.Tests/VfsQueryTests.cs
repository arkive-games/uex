using Uex;
using Uex.Core;
using Xunit;

namespace Uex.Tests;

public class VfsQueryTests
{
    private static readonly string[] Paths =
    [
        "Pal/Content/Pal/DataTable/DT_ItemData.uasset",
        "Pal/Content/Pal/DataTable/DT_ItemData.uexp",
        "Pal/Content/Pal/DataTable/Text/DT_PalNameText_Common.uasset",
        "Pal/Content/Pal/Texture/T_Icon.uasset",
        "Pal/Content/Pal/Texture/T_Icon.ubulk",
        "Pal/Config/DefaultGame.ini",
        "Engine/Content/Foo.uasset",
    ];

    [Fact]
    public void List_root_returns_top_level_directories()
    {
        var entries = VfsQuery.List(Paths, "");
        Assert.Equal(
            [new VfsQuery.Entry("Engine", true), new VfsQuery.Entry("Pal", true)],
            entries);
    }

    [Fact]
    public void List_directory_returns_subdirs_and_files_hiding_package_parts()
    {
        var entries = VfsQuery.List(Paths, "Pal/Content/Pal/DataTable");
        Assert.Equal(
            [new VfsQuery.Entry("Text", true), new VfsQuery.Entry("DT_ItemData.uasset", false)],
            entries);
    }

    [Fact]
    public void List_is_case_insensitive_and_tolerates_slashes()
    {
        var entries = VfsQuery.List(Paths, "/pal/config/");
        Assert.Equal([new VfsQuery.Entry("DefaultGame.ini", false)], entries);
    }

    [Fact]
    public void List_unknown_directory_throws()
    {
        var ex = Assert.Throws<UexException>(() => VfsQuery.List(Paths, "Pal/Nope"));
        Assert.Contains("Pal/Nope", ex.Message);
    }

    [Fact]
    public void Search_substring_case_insensitive_hides_package_parts()
    {
        var result = VfsQuery.Search(Paths, "t_icon", regex: false, limit: 10);
        Assert.Equal(["Pal/Content/Pal/Texture/T_Icon.uasset"], result.Matches);
        Assert.Equal(1, result.Total);
    }

    [Fact]
    public void Search_regex_mode()
    {
        var result = VfsQuery.Search(Paths, @"DT_.*Text", regex: true, limit: 10);
        Assert.Equal(["Pal/Content/Pal/DataTable/Text/DT_PalNameText_Common.uasset"], result.Matches);
    }

    [Fact]
    public void Search_respects_limit_but_reports_total()
    {
        var result = VfsQuery.Search(Paths, "uasset", regex: false, limit: 2);
        Assert.Equal(2, result.Matches.Count);
        Assert.Equal(4, result.Total);
    }

    [Fact]
    public void Search_invalid_regex_throws_uex_exception()
    {
        Assert.Throws<UexException>(() => VfsQuery.Search(Paths, "[", regex: true, limit: 10));
    }
}
