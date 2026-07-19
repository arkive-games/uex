using Uex;
using Uex.Config;
using Xunit;

namespace Uex.Tests;

public class ProfilesConfigTests
{
    private static string WriteTemp(string json)
    {
        var path = Path.Combine(Path.GetTempPath(), $"uex-test-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, json);
        return path;
    }

    private const string Valid = """
    {
      "profiles": {
        "palworld": {
          "game": "GAME_Palworld",
          "paksDir": "E:/Games/Palworld/Pal/Content/Paks",
          "usmap": "E:/Games/Palworld.usmap",
          "outputDir": "E:/Games/Palworld/Exports",
          "exportRoots": ["Pal/Content/Pal", "Pal/Config"]
        },
        "aion2": {
          "game": "GAME_Aion2",
          "paksDir": "G:/NCSoft/AION2_TW/Aion2/Content/Paks",
          "aesKey": "0xABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABAB",
          "outputDir": "G:/NCSoft/Export/Exports",
          "exportRoots": ["AION2/Content"]
        }
      }
    }
    """;

    [Fact]
    public void Load_parses_profiles_with_fields()
    {
        var config = ProfilesConfig.Load(WriteTemp(Valid));
        Assert.Equal(2, config.Profiles.Count);
        var pal = config.GetProfile("palworld");
        Assert.Equal("GAME_Palworld", pal.Game);
        Assert.Equal("E:/Games/Palworld.usmap", pal.Usmap);
        Assert.Null(pal.AesKey);
        Assert.Equal(["Pal/Content/Pal", "Pal/Config"], pal.ExportRoots);
        Assert.NotNull(config.GetProfile("aion2").AesKey);
    }

    [Fact]
    public void GetProfile_is_case_insensitive()
    {
        var config = ProfilesConfig.Load(WriteTemp(Valid));
        Assert.Equal("GAME_Aion2", config.GetProfile("AION2").Game);
    }

    [Fact]
    public void GetProfile_unknown_lists_available_names()
    {
        var config = ProfilesConfig.Load(WriteTemp(Valid));
        var ex = Assert.Throws<UexException>(() => config.GetProfile("fortnite"));
        Assert.Contains("palworld", ex.Message);
        Assert.Contains("aion2", ex.Message);
    }

    [Fact]
    public void Load_missing_file_throws_with_path()
    {
        var ex = Assert.Throws<UexException>(() => ProfilesConfig.Load(@"Z:\nope\profiles.json"));
        Assert.Contains("profiles.json", ex.Message);
    }

    [Fact]
    public void Load_missing_required_field_names_profile_and_field()
    {
        var bad = """{ "profiles": { "p1": { "paksDir": "X:/paks" } } }""";
        var ex = Assert.Throws<UexException>(() => ProfilesConfig.Load(WriteTemp(bad)));
        Assert.Contains("p1", ex.Message);
        Assert.Contains("game", ex.Message);
    }

    [Fact]
    public void Resolve_prefers_explicit_then_env_then_cwd()
    {
        var explicitPath = WriteTemp(Valid);
        Assert.Equal(explicitPath, ProfilesConfig.ResolvePath(explicitPath, env: null, cwd: Path.GetTempPath()));

        var envPath = WriteTemp(Valid);
        Assert.Equal(envPath, ProfilesConfig.ResolvePath(null, env: envPath, cwd: Path.GetTempPath()));

        var dir = Directory.CreateTempSubdirectory("uex-cwd").FullName;
        var cwdPath = Path.Combine(dir, "profiles.json");
        File.WriteAllText(cwdPath, Valid);
        Assert.Equal(cwdPath, ProfilesConfig.ResolvePath(null, env: null, cwd: dir));
    }
}
