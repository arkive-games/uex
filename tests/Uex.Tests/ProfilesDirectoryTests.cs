using Uex;
using Uex.Config;
using Xunit;

namespace Uex.Tests;

public class ProfilesDirectoryTests
{
    private static string NewRoot() => Directory.CreateTempSubdirectory("uex-profiles").FullName;

    private static string Game(string root, string dir, string profileJson, string? localJson = null)
    {
        var path = Path.Combine(root, dir);
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, ProfilesConfig.SharedFileName), profileJson);
        if (localJson is not null)
            File.WriteAllText(Path.Combine(path, ProfilesConfig.LocalFileName), localJson);
        return path;
    }

    [Fact]
    public void Directory_profile_is_named_after_its_directory_and_merges_local_paths()
    {
        var root = NewRoot();
        var dir = Game(root, "palworld",
            """{ "game": "GAME_UE5_1", "usmap": "Palworld.usmap", "aesKey": null, "exportRoots": ["Pal/Config"] }""",
            """{ "palworld": { "paksDir": "E:/Pal/Paks", "outputDir": "E:/Pal/Out" } }""");

        var pal = ProfilesConfig.Load(root).GetProfile("palworld");
        Assert.Equal("GAME_UE5_1", pal.Game);
        Assert.Equal(Path.Combine(dir, "Palworld.usmap"), pal.Usmap); // resolved against profile.json
        Assert.Equal("E:/Pal/Paks", pal.PaksDir);
        Assert.Equal("E:/Pal/Out", pal.OutputDir);
        Assert.Equal(["Pal/Config"], pal.ExportRoots);
        Assert.Null(pal.AesKey);
    }

    [Fact]
    public void Variants_share_key_and_usmap_but_take_their_own_local_paths()
    {
        var root = NewRoot();
        Game(root, "gmzz",
            """{ "game": "GAME_LordOfMysteries", "usmap": "lom.usmap", "aesKey": "0x01", "variants": ["gmzz", "gmzz-base"] }""",
            """{ "gmzz": { "paksDir": "D:/patched" }, "gmzz-base": { "paksDir": "D:/install" } }""");

        var config = ProfilesConfig.Load(root);
        Assert.Equal(["gmzz", "gmzz-base"], config.Profiles.Keys);
        var patched = config.GetProfile("gmzz");
        var base_ = config.GetProfile("GMZZ-BASE");
        Assert.Equal("D:/patched", patched.PaksDir);
        Assert.Equal("D:/install", base_.PaksDir);
        Assert.Equal(patched.Usmap, base_.Usmap);
        Assert.Equal("0x01", base_.AesKey);
    }

    [Fact]
    public void Game_without_local_json_still_loads_and_points_at_the_file_to_create()
    {
        var root = NewRoot();
        var dir = Game(root, "aion2", """{ "game": "GAME_Aion2" }""");
        Game(root, "palworld", """{ "game": "GAME_UE5_1" }""",
            """{ "palworld": { "paksDir": "E:/Pal/Paks" } }""");

        var config = ProfilesConfig.Load(root);
        var aion2 = config.GetProfile("aion2");
        Assert.Equal("", aion2.PaksDir);
        Assert.Null(aion2.Usmap);
        Assert.Contains(Path.Combine(dir, ProfilesConfig.LocalFileName), aion2.LocalHint("paksDir"));
        Assert.Equal("E:/Pal/Paks", config.GetProfile("palworld").PaksDir);
    }

    [Fact]
    public void Local_json_entry_for_an_unknown_profile_is_rejected()
    {
        var root = NewRoot();
        Game(root, "gmzz", """{ "game": "GAME_LordOfMysteries" }""",
            """{ "gmzz-typo": { "paksDir": "D:/x" } }""");
        var ex = Assert.Throws<UexException>(() => ProfilesConfig.Load(root));
        Assert.Contains("gmzz-typo", ex.Message);
        Assert.Contains("gmzz", ex.Message);
    }

    [Fact]
    public void Directories_without_profile_json_are_ignored_and_an_empty_root_throws()
    {
        var root = NewRoot();
        Directory.CreateDirectory(Path.Combine(root, ".git"));
        var ex = Assert.Throws<UexException>(() => ProfilesConfig.Load(root));
        Assert.Contains(ProfilesConfig.SharedFileName, ex.Message);
    }

    [Fact]
    public void Missing_game_field_names_the_profile()
    {
        var root = NewRoot();
        Game(root, "sts", """{ "usmap": "x.usmap" }""");
        var ex = Assert.Throws<UexException>(() => ProfilesConfig.Load(root));
        Assert.Contains("sts", ex.Message);
        Assert.Contains("game", ex.Message);
    }

    [Fact]
    public void Same_profile_name_in_two_game_directories_is_rejected()
    {
        var root = NewRoot();
        Game(root, "a", """{ "game": "GAME_UE5_1", "variants": ["shared"] }""");
        Game(root, "b", """{ "game": "GAME_UE5_1", "variants": ["shared"] }""");
        var ex = Assert.Throws<UexException>(() => ProfilesConfig.Load(root));
        Assert.Contains("shared", ex.Message);
    }

    [Fact]
    public void Single_file_profiles_carry_no_local_hint()
    {
        var path = Path.Combine(NewRoot(), "profiles.json");
        File.WriteAllText(path, """{ "profiles": { "p": { "game": "GAME_UE5_1", "paksDir": "X:/p" } } }""");
        Assert.Equal("", ProfilesConfig.Load(path).GetProfile("p").LocalHint("paksDir"));
    }
}
