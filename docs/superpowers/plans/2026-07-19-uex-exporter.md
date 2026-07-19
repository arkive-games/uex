# uex — UE Pak Export & Exploration Tool Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A standalone .NET console tool (`uex`) built on CUE4Parse that replaces manual FModel exports — batch-exporting game pak data to an FModel-compatible tree (JSON + PNG), and letting agents explore paks via CLI, a persistent serve mode, and an MCP server, across multiple games at once.

**Architecture:** One console binary with a shared core: `ProfilesConfig` (named per-game profiles), `ProviderManager` (lazily mounts one CUE4Parse `DefaultFileProvider` per profile, cached — this is how multiple games work simultaneously: every command/request/tool carries a `profile` parameter, and one long-lived serve/MCP process serves all configured games), pure helpers (`OutputPaths`, `VfsQuery`) that are unit-testable without paks, and three frontends (System.CommandLine CLI, JSON-lines serve loop, MCP stdio server) over the same operations. Real-pak validation happens via `uex doctor` against the locally installed AION2 (`G:\NCSoft\AION2_TW`), since Palworld is not installed on this machine.

**Tech Stack:** .NET 10 (`net10.0`), CUE4Parse 1.2.2.202607 + CUE4Parse-Conversion 1.2.2.202607 (NuGet, net10.0-only — this is why .NET 10 is required; installed SDK is 9.0.308 so Task 0 installs the 10 SDK), System.CommandLine 2.0.10 (stable GA API: `SetAction`/`ParseResult.GetValue`), ModelContextProtocol 1.4.1 + Microsoft.Extensions.Hosting (MCP stdio server), Newtonsoft.Json (transitive via CUE4Parse — its converters produce the FModel-compatible JSON), xunit 2.9 tests.

---

## Verified facts (do not re-derive)

- **FModel compatibility target:** FModel's "save properties" is `JsonConvert.SerializeObject(package.GetExports(), Formatting.Indented)` written to `<OutputDir>/Exports/<virtual-path-with-.json>`. The arkive palworld pipeline reads `PALWORLD_RAW=…/Exports/Pal/Content/Pal`, plus `…/Pal/Content/L10N` (locales) and `…/Pal/Config/DefaultGame.ini` (version stamp — `version.py` accepts either `DefaultGame.ini` or `DefaultGame.json`, so raw-copying the `.ini` is correct).
- **EGame values (CUE4Parse master):** `GAME_Palworld` (= UE5_1+8), `GAME_Aion2` (= UE5_3+5 = 84082693, which matches the user's FModel `UeVersion` for AION2 exactly).
- **AION2 profile values (from `C:\Users\liuyh\AppData\Roaming\FModel\AppSettings.json`):** paks `G:\NCSoft\AION2_TW\Aion2\Content\Paks` (pak+utoc/ucas IoStore), usmap `G:\NCSoft\FModel\AION2-5.3.2.0-1.0.34.0.usmap`, AES main key `0xABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABAB`.
- **Palworld:** not installed on this machine; profile ships as a placeholder in `profiles.example.json` (paks `E:/SteamLibrary/steamapps/common/Palworld/Pal/Content/Paks`, no AES key, usmap path to be filled on the machine that has it).
- **CUE4Parse API (verified against master, July 2026):**
  - `new DefaultFileProvider(string dir, SearchOption, bool isCaseInsensitive, VersionContainer?)`
  - Mount flow: `provider.Initialize();` then `provider.SubmitKey(new FGuid(), new FAesKey(hex))` for encrypted archives, then `provider.Mount()` (mounts remaining unencrypted readers; already-mounted are skipped), then `provider.PostMount()`.
  - Mappings: `provider.MappingsContainer = new FileUsmapTypeMappingsProvider(usmapPath);`
  - **Native decompression is mandatory for UE5 paks:** `CUE4Parse.Compression.OodleHelper.Initialize(path)` and `ZlibHelper.Initialize(path)` — with a target path they download the DLL on first use (`oodle-data-shared.dll`, `zlib-ng2.dll`). Must be called before `Initialize()`.
  - Packages: `provider.LoadPackage(vpath)` → `package.GetExports()` (IEnumerable<UObject>); serialize with `JsonConvert.SerializeObject(exports, Formatting.Indented)`.
  - Textures: `using CUE4Parse_Conversion.Textures;` → `utexture.Decode()` returns `CTexture?`; `ctexture.Encode(ETextureFormat.Png, false, out var ext)` returns PNG bytes.
  - Raw file bytes: `gameFile.Read()` (byte[]) for non-package entries.
  - `provider.Files` is `IReadOnlyDictionary<string, GameFile>` keyed by virtual path like `Pal/Content/Pal/DataTable/DT_X.uasset`.
- **Package part extensions to hide/skip everywhere:** `.uexp`, `.ubulk`, `.uptnl` (companions merged into `.uasset`/`.umap` by CUE4Parse).
- **MapStructTypes:** deliberately NOT supported (YAGNI). The FModel AION2 entry contains only the settings-UI placeholder (`MapName/KeyType/ValueType`). If `doctor` later shows map-struct parse failures, add it then.

## File structure

```
uex/                                  (new standalone git repo, E:\arkive-games\uex)
├── .gitignore
├── README.md
├── CLAUDE.md
├── uex.sln
├── profiles.example.json             (committed; real profiles.json is gitignored)
├── docs/superpowers/plans/2026-07-19-uex-exporter.md   (this file)
├── src/Uex/
│   ├── Uex.csproj
│   ├── Program.cs                    (System.CommandLine wiring only)
│   ├── UexException.cs              (user-facing errors → exit 1, no stack trace)
│   ├── Config/ProfilesConfig.cs      (config model + loader; pure, tested)
│   ├── Core/OutputPaths.cs           (virtual path → output path rules; pure, tested)
│   ├── Core/VfsQuery.cs              (list/search over path strings; pure, tested)
│   ├── Core/ProviderManager.cs       (per-profile mount cache; CUE4Parse boundary)
│   ├── Core/AssetOps.cs              (preview / preview-texture / resolve helpers)
│   ├── Core/ExportRunner.cs          (batch export)
│   ├── Serve/RequestHandler.cs       (JSON-lines envelope; pure dispatch, tested)
│   ├── Serve/ServeLoop.cs            (stdin/stdout loop)
│   └── Mcp/UexMcpTools.cs            (MCP tool methods + host bootstrap)
└── tests/Uex.Tests/
    ├── Uex.Tests.csproj
    ├── ProfilesConfigTests.cs
    ├── OutputPathsTests.cs
    ├── VfsQueryTests.cs
    └── RequestHandlerTests.cs
```

Multi-game contract (applies to every task): **no global "current game" anywhere** — `profile` is a required parameter on every operation; `ProviderManager` maps profile name → mounted provider via `ConcurrentDictionary<string, Lazy<…>>` so first use mounts, later uses are instant, and concurrent requests for different games never interfere. Per-profile `outputDir` keeps exports collision-free.

---

### Task 0: Repo scaffold + .NET 10 SDK

**Files:**
- Create: `.gitignore`, `uex.sln`, `src/Uex/Uex.csproj`, `src/Uex/Program.cs`, `tests/Uex.Tests/Uex.Tests.csproj`, `README.md` (stub)

- [ ] **Step 1: Install .NET 10 SDK (required by CUE4Parse net10.0)**

Run: `winget install Microsoft.DotNet.SDK.10 --accept-source-agreements --accept-package-agreements`
Then in a NEW shell: `dotnet --list-sdks`
Expected: a `10.0.x` entry alongside `9.0.308`. (If winget fails, download from https://dotnet.microsoft.com/download/dotnet/10.0 and re-verify.)

- [ ] **Step 2: git init + .gitignore**

```bash
cd /e/arkive-games/uex && git init -b main
```

Create `.gitignore`:

```gitignore
bin/
obj/
*.user
.vs/
# real game profiles contain AES keys and machine paths — keep local
profiles.json
# native decompression DLLs downloaded at runtime
.uex-cache/
```

- [ ] **Step 3: Projects + solution**

Create `src/Uex/Uex.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <AssemblyName>uex</AssemblyName>
    <RootNamespace>Uex</RootNamespace>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="CUE4Parse" Version="1.2.2.202607" />
    <PackageReference Include="CUE4Parse-Conversion" Version="1.2.2.202607" />
    <PackageReference Include="System.CommandLine" Version="2.0.10" />
    <PackageReference Include="ModelContextProtocol" Version="1.4.1" />
    <PackageReference Include="Microsoft.Extensions.Hosting" Version="10.0.0" />
  </ItemGroup>
</Project>
```

Create `src/Uex/Program.cs` (placeholder, replaced in Task 5):

```csharp
Console.WriteLine("uex 0.1.0-dev");
return 0;
```

Create `tests/Uex.Tests/Uex.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.12.0" />
    <PackageReference Include="xunit" Version="2.9.2" />
    <PackageReference Include="xunit.runner.visualstudio" Version="2.8.2" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\..\src\Uex\Uex.csproj" />
  </ItemGroup>
</Project>
```

```bash
cd /e/arkive-games/uex
dotnet new sln -n uex
dotnet sln add src/Uex/Uex.csproj tests/Uex.Tests/Uex.Tests.csproj
```

Create `README.md` stub: `# uex\n\nUE pak export & exploration tool on CUE4Parse. WIP — see docs/superpowers/plans/.`

- [ ] **Step 4: Verify build + test run**

Run: `cd /e/arkive-games/uex && dotnet build && dotnet test`
Expected: build succeeds (restores CUE4Parse from NuGet), test run reports 0 tests, exit 0. If NuGet can't find `Microsoft.Extensions.Hosting` 10.0.0, use the newest 10.0.x listed by `dotnet package search Microsoft.Extensions.Hosting`.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "chore: scaffold uex solution (net10.0, CUE4Parse, System.CommandLine, MCP)"
```

---

### Task 1: ProfilesConfig (TDD)

Named per-game profiles — the foundation of multi-game support. Config path resolution: explicit `--config` > `UEX_PROFILES` env var > `./profiles.json` > `<exe dir>/profiles.json`.

**Files:**
- Create: `src/Uex/UexException.cs`, `src/Uex/Config/ProfilesConfig.cs`
- Test: `tests/Uex.Tests/ProfilesConfigTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Uex.Tests/ProfilesConfigTests.cs`:

```csharp
using Uex;
using Uex.Config;

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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter ProfilesConfigTests`
Expected: FAIL — compile error, `Uex.Config.ProfilesConfig` does not exist.

- [ ] **Step 3: Implement**

`src/Uex/UexException.cs`:

```csharp
namespace Uex;

/// <summary>User-facing error: printed as a plain message (no stack trace), exit code 1.</summary>
public sealed class UexException(string message) : Exception(message);
```

`src/Uex/Config/ProfilesConfig.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Uex.Config;

public sealed class GameProfile
{
    [JsonPropertyName("game")] public string Game { get; set; } = "";
    [JsonPropertyName("paksDir")] public string PaksDir { get; set; } = "";
    [JsonPropertyName("usmap")] public string? Usmap { get; set; }
    [JsonPropertyName("aesKey")] public string? AesKey { get; set; }
    [JsonPropertyName("outputDir")] public string OutputDir { get; set; } = "";
    [JsonPropertyName("exportRoots")] public List<string> ExportRoots { get; set; } = [];
}

public sealed class ProfilesConfig
{
    [JsonPropertyName("profiles")]
    public Dictionary<string, GameProfile> Profiles { get; set; } = new();

    public GameProfile GetProfile(string name)
    {
        foreach (var (key, value) in Profiles)
            if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase))
                return value;
        throw new UexException(
            $"Unknown profile '{name}'. Available profiles: {string.Join(", ", Profiles.Keys)}");
    }

    public static ProfilesConfig Load(string path)
    {
        if (!File.Exists(path))
            throw new UexException(
                $"Profiles config not found: {path}. Copy profiles.example.json to profiles.json and fill in your games.");
        ProfilesConfig? config;
        try
        {
            config = JsonSerializer.Deserialize<ProfilesConfig>(File.ReadAllText(path));
        }
        catch (JsonException e)
        {
            throw new UexException($"Invalid JSON in {path}: {e.Message}");
        }
        if (config is null || config.Profiles.Count == 0)
            throw new UexException($"{path} contains no profiles.");
        foreach (var (name, p) in config.Profiles)
        {
            if (string.IsNullOrWhiteSpace(p.Game))
                throw new UexException($"Profile '{name}': missing required field 'game' (an EGame name, e.g. GAME_Palworld).");
            if (string.IsNullOrWhiteSpace(p.PaksDir))
                throw new UexException($"Profile '{name}': missing required field 'paksDir'.");
        }
        return config;
    }

    /// <summary>--config > UEX_PROFILES env > cwd/profiles.json > exe-dir/profiles.json.</summary>
    public static string ResolvePath(string? explicitPath, string? env, string cwd)
    {
        if (!string.IsNullOrEmpty(explicitPath)) return explicitPath;
        if (!string.IsNullOrEmpty(env)) return env;
        var cwdPath = Path.Combine(cwd, "profiles.json");
        if (File.Exists(cwdPath)) return cwdPath;
        return Path.Combine(AppContext.BaseDirectory, "profiles.json");
    }

    public static ProfilesConfig LoadDefault(string? explicitPath) =>
        Load(ResolvePath(explicitPath, Environment.GetEnvironmentVariable("UEX_PROFILES"), Environment.CurrentDirectory));
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter ProfilesConfigTests`
Expected: 6 passed.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: profiles config with per-game entries and path resolution"
```

---

### Task 2: OutputPaths (TDD)

Pure rules for the FModel-compatible on-disk layout.

**Files:**
- Create: `src/Uex/Core/OutputPaths.cs`
- Test: `tests/Uex.Tests/OutputPathsTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Uex.Tests/OutputPathsTests.cs`:

```csharp
using Uex.Core;

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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter OutputPathsTests`
Expected: FAIL — `Uex.Core.OutputPaths` does not exist.

- [ ] **Step 3: Implement**

`src/Uex/Core/OutputPaths.cs`:

```csharp
namespace Uex.Core;

/// <summary>Pure virtual-path rules for the FModel-compatible export layout.</summary>
public static class OutputPaths
{
    private static readonly string[] PackageExts = [".uasset", ".umap"];
    private static readonly string[] PartExts = [".uexp", ".ubulk", ".uptnl"];

    public static string Normalize(string vpath) => vpath.Replace('\\', '/').Trim('/');

    public static bool IsPackage(string vpath) =>
        PackageExts.Any(e => vpath.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static bool IsPackagePart(string vpath) =>
        PartExts.Any(e => vpath.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static string ForPackageJson(string vpath) => SwapExtension(vpath, ".json");

    /// <summary>First texture export in a package keeps the package name; extras get ".{exportName}.png".</summary>
    public static string ForTexturePng(string vpath, string exportName, int index) =>
        index == 0 ? SwapExtension(vpath, ".png") : SwapExtension(vpath, $".{exportName}.png");

    public static string ForRaw(string vpath) => vpath;

    public static bool IsUnderAnyRoot(string vpath, IEnumerable<string> roots)
    {
        var path = Normalize(vpath);
        foreach (var root in roots)
        {
            var r = Normalize(root);
            if (path.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string SwapExtension(string vpath, string newExt)
    {
        var dot = vpath.LastIndexOf('.');
        return (dot < 0 ? vpath : vpath[..dot]) + newExt;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter OutputPathsTests`
Expected: all passed.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: output path rules for FModel-compatible layout"
```

---

### Task 3: VfsQuery — list & search (TDD)

Directory listing and pattern search over the flat `provider.Files` key set. Pure: operates on `IEnumerable<string>`.

**Files:**
- Create: `src/Uex/Core/VfsQuery.cs`
- Test: `tests/Uex.Tests/VfsQueryTests.cs`

- [ ] **Step 1: Write the failing tests**

`tests/Uex.Tests/VfsQueryTests.cs`:

```csharp
using Uex;
using Uex.Core;

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
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter VfsQueryTests`
Expected: FAIL — `Uex.Core.VfsQuery` does not exist.

- [ ] **Step 3: Implement**

`src/Uex/Core/VfsQuery.cs`:

```csharp
using System.Text.RegularExpressions;

namespace Uex.Core;

/// <summary>Directory listing and search over the flat virtual-path set of a mounted provider.</summary>
public static class VfsQuery
{
    public readonly record struct Entry(string Name, bool IsDirectory);
    public sealed record SearchResult(List<string> Matches, int Total);

    /// <summary>Children of a virtual directory: subdirectories first, then files, each name-sorted. Package parts (.uexp/.ubulk/.uptnl) are hidden.</summary>
    public static List<Entry> List(IEnumerable<string> allPaths, string dirPath)
    {
        var dir = OutputPaths.Normalize(dirPath);
        var prefix = dir.Length == 0 ? "" : dir + "/";
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = dir.Length == 0;
        foreach (var raw in allPaths)
        {
            var path = OutputPaths.Normalize(raw);
            if (prefix.Length > 0 && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            found = true;
            var rest = path[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash >= 0) dirs.Add(rest[..slash]);
            else if (!OutputPaths.IsPackagePart(rest)) files.Add(rest);
        }
        if (!found)
            throw new UexException($"No such directory in paks: {dir}");
        return
        [
            .. dirs.Select(d => new Entry(d, true)),
            .. files.Select(f => new Entry(f, false)),
        ];
    }

    /// <summary>Substring (default) or regex match over full virtual paths, case-insensitive. Package parts are hidden.</summary>
    public static SearchResult Search(IEnumerable<string> allPaths, string pattern, bool regex, int limit)
    {
        Func<string, bool> matches;
        if (regex)
        {
            Regex compiled;
            try { compiled = new Regex(pattern, RegexOptions.IgnoreCase); }
            catch (ArgumentException e) { throw new UexException($"Invalid regex '{pattern}': {e.Message}"); }
            matches = compiled.IsMatch;
        }
        else
        {
            matches = p => p.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        var hits = new List<string>();
        var total = 0;
        foreach (var raw in allPaths.Select(OutputPaths.Normalize).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (OutputPaths.IsPackagePart(raw) || !matches(raw)) continue;
            total++;
            if (hits.Count < limit) hits.Add(raw);
        }
        return new SearchResult(hits, total);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter VfsQueryTests`
Expected: all passed.

- [ ] **Step 5: Commit**

```bash
git add -A && git commit -m "feat: vfs list/search over virtual paths"
```

---

### Task 4: ProviderManager — multi-game mount cache

The CUE4Parse boundary. No unit tests (needs real paks); `doctor` in Task 5 is its integration test.

**Files:**
- Create: `src/Uex/Core/ProviderManager.cs`

- [ ] **Step 1: Implement**

`src/Uex/Core/ProviderManager.cs`:

```csharp
using System.Collections.Concurrent;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider;
using CUE4Parse.UE4.Objects.Core.Misc;
using CUE4Parse.UE4.Versions;
using Uex.Config;

namespace Uex.Core;

/// <summary>
/// Lazily mounts and caches one DefaultFileProvider per profile. This is the heart of
/// multi-game support: every operation names its profile, first use mounts (several
/// seconds), later uses hit the cache, and different games coexist in one process.
/// </summary>
public sealed class ProviderManager(ProfilesConfig config) : IDisposable
{
    private readonly ConcurrentDictionary<string, Lazy<DefaultFileProvider>> _providers =
        new(StringComparer.OrdinalIgnoreCase);
    private static bool _nativesReady;
    private static readonly object _nativesLock = new();

    public DefaultFileProvider Get(string profileName)
    {
        var profile = config.GetProfile(profileName); // throws UexException for unknown names
        return _providers.GetOrAdd(profileName,
            _ => new Lazy<DefaultFileProvider>(() => Mount(profileName, profile))).Value;
    }

    private static DefaultFileProvider Mount(string name, GameProfile p)
    {
        EnsureNatives();
        if (!Enum.TryParse<EGame>(p.Game, ignoreCase: true, out var game))
            throw new UexException(
                $"Profile '{name}': unknown game '{p.Game}'. Use a CUE4Parse EGame name, e.g. GAME_Palworld, GAME_Aion2, GAME_UE5_1.");
        if (!Directory.Exists(p.PaksDir))
            throw new UexException($"Profile '{name}': paks directory not found: {p.PaksDir}");
        if (p.Usmap is not null && !File.Exists(p.Usmap))
            throw new UexException($"Profile '{name}': usmap file not found: {p.Usmap}");

        var provider = new DefaultFileProvider(p.PaksDir, SearchOption.AllDirectories, true, new VersionContainer(game));
        if (p.Usmap is not null)
            provider.MappingsContainer = new FileUsmapTypeMappingsProvider(p.Usmap);
        provider.Initialize();
        if (!string.IsNullOrEmpty(p.AesKey))
            provider.SubmitKey(new FGuid(), new FAesKey(p.AesKey));
        provider.Mount();     // mounts remaining unencrypted archives; already-mounted are skipped
        provider.PostMount();
        if (provider.Files.Count == 0)
            throw new UexException(
                $"Profile '{name}': mounted 0 files from {p.PaksDir}. Check the AES key and game version.");
        return provider;
    }

    /// <summary>Oodle/zlib native decompression, required for UE5 paks. DLLs are downloaded once into .uex-cache next to the exe.</summary>
    private static void EnsureNatives()
    {
        lock (_nativesLock)
        {
            if (_nativesReady) return;
            var cache = Path.Combine(AppContext.BaseDirectory, ".uex-cache");
            Directory.CreateDirectory(cache);
            OodleHelper.Initialize(Path.Combine(cache, OodleHelper.OodleFileName));
            ZlibHelper.Initialize(Path.Combine(cache, ZlibHelper.DllName));
            _nativesReady = true;
        }
    }

    public void Dispose()
    {
        foreach (var lazy in _providers.Values)
            if (lazy.IsValueCreated)
                lazy.Value.Dispose();
        _providers.Clear();
    }
}
```

Note: if `OodleHelper.Initialize(path)` does not auto-download the DLL in the pinned package version (a runtime error on first mount will make it obvious), switch to the explicit pair `OodleHelper.DownloadOodleDll(path); OodleHelper.Initialize(path);` (and `ZlibHelper.DownloadDll(path)` likewise) — both exist in the package.

- [ ] **Step 2: Verify it compiles**

Run: `dotnet build`
Expected: build succeeds. If a `using` namespace is wrong for the pinned package version, the compiler error names the type — search the package source on GitHub (FabianFG/CUE4Parse) for the correct namespace and fix.

- [ ] **Step 3: Commit**

```bash
git add -A && git commit -m "feat: per-profile provider mount cache with native decompression bootstrap"
```

---

### Task 5: CLI skeleton + doctor (first real-pak contact)

System.CommandLine 2.0 GA wiring, with `doctor` implemented; other handlers come in Tasks 6-8. Global option `--config <path>`; per-command option `--profile <name>` (required).

**Files:**
- Create: `src/Uex/Core/AssetOps.cs`
- Modify: `src/Uex/Program.cs` (replace placeholder)

- [ ] **Step 1: Implement AssetOps**

`src/Uex/Core/AssetOps.cs`:

```csharp
using CUE4Parse.FileProvider;
using Newtonsoft.Json;

namespace Uex.Core;

/// <summary>Single-asset operations shared by CLI, serve mode and MCP.</summary>
public static class AssetOps
{
    /// <summary>Resolve a user path to an exact Files key: exact, then +.uasset/.umap; on failure suggest near matches by file name.</summary>
    public static string ResolvePackagePath(DefaultFileProvider provider, string input)
    {
        var path = OutputPaths.Normalize(input);
        foreach (var candidate in new[] { path, path + ".uasset", path + ".umap" })
            if (provider.Files.ContainsKey(candidate))
                return candidate;
        var name = path[(path.LastIndexOf('/') + 1)..];
        var suggestions = VfsQuery.Search(provider.Files.Keys, name, regex: false, limit: 5);
        var hint = suggestions.Matches.Count > 0
            ? $" Did you mean:\n  {string.Join("\n  ", suggestions.Matches)}"
            : "";
        throw new UexException($"Asset not found: {input}.{hint}");
    }

    /// <summary>FModel-compatible package JSON: the serialized array of exports.</summary>
    public static string SerializePackage(DefaultFileProvider provider, string vpath)
    {
        var package = provider.LoadPackage(vpath);
        return JsonConvert.SerializeObject(package.GetExports(), Formatting.Indented);
    }

    /// <summary>Serialize with a byte cap for agent previews; truncated output is marked and not valid JSON.</summary>
    public static string Preview(DefaultFileProvider provider, string vpath, int maxBytes)
    {
        var json = SerializePackage(provider, vpath);
        if (json.Length <= maxBytes) return json;
        return json[..maxBytes] + $"\n... [truncated {json.Length - maxBytes} of {json.Length} chars - use --max-bytes to raise]";
    }
}
```

- [ ] **Step 2: Implement Program.cs with doctor**

Replace `src/Uex/Program.cs`:

```csharp
using System.CommandLine;
using Uex;
using Uex.Config;
using Uex.Core;

var configOption = new Option<string?>("--config") { Description = "Path to profiles.json (default: UEX_PROFILES env, ./profiles.json, exe dir)", Recursive = true };
var profileOption = new Option<string>("--profile") { Description = "Game profile name from profiles.json", Required = true };

var root = new RootCommand("uex - UE pak export & exploration on CUE4Parse");
root.Options.Add(configOption);

int Run(Func<int> action)
{
    try { return action(); }
    catch (UexException e) { Console.Error.WriteLine($"error: {e.Message}"); return 1; }
}

// ---- doctor ----------------------------------------------------------------
var doctorCommand = new Command("doctor", "Mount a profile and parse a few assets to verify the setup");
doctorCommand.Options.Add(profileOption);
doctorCommand.SetAction(parse => Run(() =>
{
    var config = ProfilesConfig.LoadDefault(parse.GetValue(configOption));
    var profileName = parse.GetValue(profileOption)!;
    var profile = config.GetProfile(profileName);
    using var providers = new ProviderManager(config);

    Console.WriteLine($"profile:  {profileName} ({profile.Game})");
    Console.WriteLine($"paks:     {profile.PaksDir}");
    var provider = providers.Get(profileName);
    Console.WriteLine($"mounted:  {provider.MountedVfs.Count} archives, {provider.Files.Count} files");
    Console.WriteLine($"usmap:    {profile.Usmap ?? "(none)"}");

    var probes = provider.Files.Keys
        .Where(k => OutputPaths.IsPackage(k) && OutputPaths.IsUnderAnyRoot(k, profile.ExportRoots))
        .Order(StringComparer.OrdinalIgnoreCase)
        .Take(3)
        .ToList();
    if (probes.Count == 0)
        throw new UexException($"No packages found under exportRoots [{string.Join(", ", profile.ExportRoots)}].");
    var failures = 0;
    foreach (var probe in probes)
    {
        try
        {
            var json = AssetOps.SerializePackage(provider, probe);
            Console.WriteLine($"parse ok: {probe} ({json.Length:N0} chars)");
        }
        catch (Exception e)
        {
            failures++;
            Console.WriteLine($"parse FAILED: {probe}: {e.Message}");
        }
    }
    Console.WriteLine(failures == 0 ? "doctor: OK" : $"doctor: {failures}/{probes.Count} probes failed");
    return failures == 0 ? 0 : 1;
}));
root.Subcommands.Add(doctorCommand);

return root.Parse(args).Invoke();
```

Note on System.CommandLine 2.0 GA: options are added via `command.Options.Add(...)`, handlers via `command.SetAction(parseResult => ...)`, values read via `parseResult.GetValue(option)`. If `Recursive` doesn't exist on `Option<T>` in 2.0.10, drop that property and add `configOption` to each subcommand's `Options` instead.

- [ ] **Step 3: Create the real profiles.json (gitignored) and run doctor against AION2**

Create `E:\arkive-games\uex\profiles.json`:

```json
{
  "profiles": {
    "aion2": {
      "game": "GAME_Aion2",
      "paksDir": "G:/NCSoft/AION2_TW/Aion2/Content/Paks",
      "usmap": "G:/NCSoft/FModel/AION2-5.3.2.0-1.0.34.0.usmap",
      "aesKey": "0xABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABABAB",
      "outputDir": "E:/arkive-games/uex-out/aion2",
      "exportRoots": ["AION2/Content/Data", "AION2/Config"]
    }
  }
}
```

Run: `dotnet run --project src/Uex -- doctor --profile aion2`
Expected: first run downloads the Oodle/zlib DLLs into `.uex-cache`, mounts (this can take a while for hundreds of archives), prints non-zero archive/file counts, three `parse ok:` lines, `doctor: OK`, exit 0.
Debugging notes:
- `mounted: 0 archives` -> AES key or EGame mismatch (values above are copied from the user's working FModel setup, so suspect code first).
- probe parse failures about unknown/failed properties -> usmap not applied.
- `No packages found under exportRoots` -> the virtual root name differs from `AION2`; print a sample with a temporary `Console.WriteLine(provider.Files.Keys.First())` and adjust profiles.json. (The existing FModel export tree `G:\NCSoft\Export\Exports\AION2\Content\Data` implies `AION2/Content/Data` is correct.)

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: CLI skeleton with doctor command (verified against AION2 paks)"
```

---

### Task 6: export command (batch, FModel-compatible)

**Files:**
- Create: `src/Uex/Core/ExportRunner.cs`
- Modify: `src/Uex/Program.cs` (add export command)

- [ ] **Step 1: Implement ExportRunner**

`src/Uex/Core/ExportRunner.cs`:

```csharp
using System.Collections.Concurrent;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using Uex.Config;

namespace Uex.Core;

public sealed record ExportSummary(int Packages, int Textures, int RawFiles, List<string> Errors);

/// <summary>Batch export of everything under the profile's exportRoots (or an explicit subset) into the FModel-compatible tree.</summary>
public static class ExportRunner
{
    public static ExportSummary Run(DefaultFileProvider provider, GameProfile profile,
        IReadOnlyList<string>? only = null, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(profile.OutputDir))
            throw new UexException("Profile has no outputDir configured.");
        var roots = only is { Count: > 0 } ? only : profile.ExportRoots;
        if (roots.Count == 0)
            throw new UexException("Nothing to export: profile has no exportRoots and no --only paths given.");

        var targets = provider.Files.Keys
            .Where(k => !OutputPaths.IsPackagePart(k) && OutputPaths.IsUnderAnyRoot(k, roots))
            .ToList();
        if (targets.Count == 0)
            throw new UexException($"No files under roots [{string.Join(", ", roots)}].");

        int packages = 0, textures = 0, rawFiles = 0, done = 0;
        var errors = new ConcurrentBag<string>();
        Parallel.ForEach(targets, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, vpath =>
        {
            try
            {
                if (OutputPaths.IsPackage(vpath))
                {
                    var package = provider.LoadPackage(vpath);
                    var exports = package.GetExports().ToArray();
                    WriteText(profile.OutputDir, OutputPaths.ForPackageJson(vpath),
                        Newtonsoft.Json.JsonConvert.SerializeObject(exports, Newtonsoft.Json.Formatting.Indented));
                    Interlocked.Increment(ref packages);
                    var textureIndex = 0;
                    foreach (var export in exports)
                    {
                        if (export is not UTexture texture) continue;
                        var decoded = texture.Decode();
                        if (decoded is null) continue;
                        var png = decoded.Encode(ETextureFormat.Png, false, out _);
                        WriteBytes(profile.OutputDir, OutputPaths.ForTexturePng(vpath, texture.Name, textureIndex++), png);
                        Interlocked.Increment(ref textures);
                    }
                }
                else
                {
                    WriteBytes(profile.OutputDir, OutputPaths.ForRaw(vpath), provider.Files[vpath].Read());
                    Interlocked.Increment(ref rawFiles);
                }
            }
            catch (Exception e)
            {
                errors.Add($"{vpath}: {e.Message}");
            }
            var current = Interlocked.Increment(ref done);
            if (current % 500 == 0) log?.Invoke($"{current}/{targets.Count} ...");
        });
        return new ExportSummary(packages, textures, rawFiles, [.. errors.Order()]);
    }

    private static void WriteText(string outDir, string relPath, string content)
    {
        var path = Path.Combine(outDir, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteBytes(string outDir, string relPath, byte[] content)
    {
        var path = Path.Combine(outDir, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }
}
```

Note: `ETextureFormat` lives in the `CUE4Parse_Conversion` namespace in some versions and `CUE4Parse_Conversion.Textures` in others — the `using CUE4Parse_Conversion.Textures;` plus compiler feedback settles it; add `using CUE4Parse_Conversion;` if needed.

- [ ] **Step 2: Wire the export command**

Add to `src/Uex/Program.cs` before `return root.Parse(args).Invoke();`:

```csharp
// ---- export ----------------------------------------------------------------
var onlyOption = new Option<string[]>("--only") { Description = "Export only these virtual paths/prefixes (default: profile exportRoots)", AllowMultipleArgumentsPerToken = true };
var exportCommand = new Command("export", "Batch export to the profile's outputDir (FModel-compatible JSON/PNG tree)");
exportCommand.Options.Add(profileOption);
exportCommand.Options.Add(onlyOption);
exportCommand.SetAction(parse => Run(() =>
{
    var config = ProfilesConfig.LoadDefault(parse.GetValue(configOption));
    var profileName = parse.GetValue(profileOption)!;
    var profile = config.GetProfile(profileName);
    using var providers = new ProviderManager(config);
    var summary = ExportRunner.Run(providers.Get(profileName), profile,
        parse.GetValue(onlyOption), msg => Console.Error.WriteLine(msg));
    Console.WriteLine($"exported: {summary.Packages} packages, {summary.Textures} textures, {summary.RawFiles} raw files -> {profile.OutputDir}");
    if (summary.Errors.Count > 0)
    {
        Console.Error.WriteLine($"{summary.Errors.Count} assets failed:");
        foreach (var error in summary.Errors.Take(20)) Console.Error.WriteLine($"  {error}");
        if (summary.Errors.Count > 20) Console.Error.WriteLine($"  ... and {summary.Errors.Count - 20} more");
    }
    return 0; // partial parse failures are normal for a full-tree export; doctor is the health gate
}));
root.Subcommands.Add(exportCommand);
```

- [ ] **Step 3: Integration check — export a small AION2 subtree and diff against the FModel export**

```bash
dotnet run --project src/Uex -- export --profile aion2 --only AION2/Content/Data/WorldMap
diff <(python -c "import json,sys;print(json.dumps(json.load(open(sys.argv[1],encoding='utf-8-sig')),sort_keys=True,indent=1))" /e/arkive-games/uex-out/aion2/AION2/Content/Data/WorldMap/World_L_A.json) \
     <(python -c "import json,sys;print(json.dumps(json.load(open(sys.argv[1],encoding='utf-8-sig')),sort_keys=True,indent=1))" /g/NCSoft/Export/Exports/AION2/Content/Data/WorldMap/World_L_A.json)
```

Expected: export summary with >0 packages; the semantic JSON diff is empty. Byte-identity is NOT required — FModel version drift can rename/reorder minor fields; **semantic equality is the acceptance bar**. If specific fields differ, list them and check whether the arkive pipeline reads them before treating it as a failure. Pick another file under `Data/WorldMap` if `World_L_A.json` is absent from the old export.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: batch export command with FModel-compatible output tree"
```

---

### Task 7: Exploration commands — list, search, preview, preview-texture

**Files:**
- Modify: `src/Uex/Program.cs` (four commands)
- Modify: `src/Uex/Core/AssetOps.cs` (add SavePng)

- [ ] **Step 1: Add texture decode helper to AssetOps**

Append to `src/Uex/Core/AssetOps.cs` (inside the class):

```csharp
    /// <summary>Decode the first texture export of a package to a PNG file; returns the written path.</summary>
    public static string SavePng(DefaultFileProvider provider, string vpath, string outPath)
    {
        var package = provider.LoadPackage(vpath);
        foreach (var export in package.GetExports())
        {
            if (export is not CUE4Parse.UE4.Assets.Exports.Texture.UTexture texture) continue;
            var decoded = texture.Decode()
                ?? throw new UexException($"Texture failed to decode: {vpath}");
            var png = CUE4Parse_Conversion.Textures.TextureEncoder.Encode(
                decoded, CUE4Parse_Conversion.Textures.ETextureFormat.Png, false, out _);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllBytes(outPath, png);
            return Path.GetFullPath(outPath);
        }
        throw new UexException($"No texture export in package: {vpath}");
    }
```

(Adjust the `ETextureFormat` namespace as discovered in Task 6.)

- [ ] **Step 2: Wire the four commands**

Add to `src/Uex/Program.cs` before `return root.Parse(args).Invoke();`:

```csharp
// ---- list ------------------------------------------------------------------
var listPathArg = new Argument<string>("path") { Description = "Virtual directory ('' = root)", DefaultValueFactory = _ => "" };
var listCommand = new Command("list", "List children of a virtual directory (like FModel's tree)");
listCommand.Options.Add(profileOption);
listCommand.Arguments.Add(listPathArg);
listCommand.SetAction(parse => Run(() =>
{
    var config = ProfilesConfig.LoadDefault(parse.GetValue(configOption));
    using var providers = new ProviderManager(config);
    var provider = providers.Get(parse.GetValue(profileOption)!);
    foreach (var entry in VfsQuery.List(provider.Files.Keys, parse.GetValue(listPathArg)!))
        Console.WriteLine(entry.IsDirectory ? entry.Name + "/" : entry.Name);
    return 0;
}));
root.Subcommands.Add(listCommand);

// ---- search ----------------------------------------------------------------
var patternArg = new Argument<string>("pattern") { Description = "Substring (default) or regex with --regex" };
var regexOption = new Option<bool>("--regex") { Description = "Treat pattern as a regex" };
var limitOption = new Option<int>("--limit") { Description = "Max results to print", DefaultValueFactory = _ => 200 };
var searchCommand = new Command("search", "Search all virtual paths");
searchCommand.Options.Add(profileOption);
searchCommand.Options.Add(regexOption);
searchCommand.Options.Add(limitOption);
searchCommand.Arguments.Add(patternArg);
searchCommand.SetAction(parse => Run(() =>
{
    var config = ProfilesConfig.LoadDefault(parse.GetValue(configOption));
    using var providers = new ProviderManager(config);
    var provider = providers.Get(parse.GetValue(profileOption)!);
    var result = VfsQuery.Search(provider.Files.Keys, parse.GetValue(patternArg)!,
        parse.GetValue(regexOption), parse.GetValue(limitOption));
    foreach (var match in result.Matches) Console.WriteLine(match);
    if (result.Total > result.Matches.Count)
        Console.Error.WriteLine($"({result.Matches.Count} of {result.Total} matches shown - raise --limit)");
    return 0;
}));
root.Subcommands.Add(searchCommand);

// ---- preview ---------------------------------------------------------------
var assetArg = new Argument<string>("asset") { Description = "Virtual asset path (.uasset/.umap extension optional)" };
var maxBytesOption = new Option<int>("--max-bytes") { Description = "Truncate JSON beyond this size", DefaultValueFactory = _ => 200_000 };
var previewCommand = new Command("preview", "Serialize an asset to JSON on stdout");
previewCommand.Options.Add(profileOption);
previewCommand.Options.Add(maxBytesOption);
previewCommand.Arguments.Add(assetArg);
previewCommand.SetAction(parse => Run(() =>
{
    var config = ProfilesConfig.LoadDefault(parse.GetValue(configOption));
    using var providers = new ProviderManager(config);
    var provider = providers.Get(parse.GetValue(profileOption)!);
    var vpath = AssetOps.ResolvePackagePath(provider, parse.GetValue(assetArg)!);
    Console.WriteLine(AssetOps.Preview(provider, vpath, parse.GetValue(maxBytesOption)));
    return 0;
}));
root.Subcommands.Add(previewCommand);

// ---- preview-texture ---------------------------------------------------------
var texAssetArg = new Argument<string>("asset") { Description = "Virtual asset path of a texture package" };
var outOption = new Option<string>("--out") { Description = "PNG output file path", Required = true };
var previewTextureCommand = new Command("preview-texture", "Decode a texture asset to a PNG file");
previewTextureCommand.Options.Add(profileOption);
previewTextureCommand.Options.Add(outOption);
previewTextureCommand.Arguments.Add(texAssetArg);
previewTextureCommand.SetAction(parse => Run(() =>
{
    var config = ProfilesConfig.LoadDefault(parse.GetValue(configOption));
    using var providers = new ProviderManager(config);
    var provider = providers.Get(parse.GetValue(profileOption)!);
    var vpath = AssetOps.ResolvePackagePath(provider, parse.GetValue(texAssetArg)!);
    Console.WriteLine(AssetOps.SavePng(provider, vpath, parse.GetValue(outOption)!));
    return 0;
}));
root.Subcommands.Add(previewTextureCommand);
```

- [ ] **Step 3: Integration check against AION2**

```bash
dotnet run --project src/Uex -- list --profile aion2
dotnet run --project src/Uex -- list --profile aion2 AION2/Content/Data
dotnet run --project src/Uex -- search --profile aion2 WorldMap --limit 10
dotnet run --project src/Uex -- preview --profile aion2 AION2/Content/Data/WorldMap/World_L_A --max-bytes 2000
dotnet run --project src/Uex -- search --profile aion2 "T_.*[Mm]ap" --regex --limit 5
# pick a texture path from that output:
dotnet run --project src/Uex -- preview-texture --profile aion2 <texture-path> --out /tmp/uex-tex.png
```

Expected: root listing shows top-level dirs (`AION2/`, `Engine/`, ...); Data listing shows subdirs incl. `WorldMap/`; preview prints truncated JSON starting with `[`; preview-texture writes a PNG (verify by opening/Reading the image).

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: list/search/preview/preview-texture exploration commands"
```

---

### Task 8: Serve mode (JSON-lines, multi-game per request)

Long-lived process for agent exploration sessions: one JSON request per stdin line, one JSON response per stdout line. `profile` is a field on every request, so a single serve process handles all games — mounts happen lazily per profile via `ProviderManager`.

Protocol:
- Request: `{"id": 1, "cmd": "list", "profile": "aion2", "args": {"path": "AION2/Content"}}`
- Success: `{"id": 1, "ok": true, "result": ...}` — Error: `{"id": 1, "ok": false, "error": "message"}`
- Commands: `profiles` (no profile needed), `list {path}`, `search {pattern, regex?, limit?}`, `preview {asset, maxBytes?}`, `preview-texture {asset, out}`, `export {only?}`, `shutdown`.

**Files:**
- Create: `src/Uex/Serve/RequestHandler.cs`, `src/Uex/Serve/ServeLoop.cs`
- Modify: `src/Uex/Program.cs` (serve command)
- Test: `tests/Uex.Tests/RequestHandlerTests.cs`

- [ ] **Step 1: Write the failing tests (envelope logic with a fake executor)**

`tests/Uex.Tests/RequestHandlerTests.cs`:

```csharp
using System.Text.Json.Nodes;
using Uex;
using Uex.Serve;

namespace Uex.Tests;

public class RequestHandlerTests
{
    private static readonly RequestHandler Handler = new((cmd, profile, args) => cmd switch
    {
        "echo" => JsonValue.Create($"{profile}:{args?["x"]?.GetValue<string>()}"),
        "boom" => throw new UexException("kaboom"),
        _ => throw new UexException($"Unknown command '{cmd}'"),
    });

    [Fact]
    public void Dispatches_and_wraps_result()
    {
        var response = Handler.Handle("""{"id":7,"cmd":"echo","profile":"aion2","args":{"x":"hi"}}""");
        var node = JsonNode.Parse(response)!;
        Assert.Equal(7, node["id"]!.GetValue<int>());
        Assert.True(node["ok"]!.GetValue<bool>());
        Assert.Equal("aion2:hi", node["result"]!.GetValue<string>());
    }

    [Fact]
    public void UexException_becomes_error_response_with_id()
    {
        var node = JsonNode.Parse(Handler.Handle("""{"id":8,"cmd":"boom","profile":"p"}"""))!;
        Assert.Equal(8, node["id"]!.GetValue<int>());
        Assert.False(node["ok"]!.GetValue<bool>());
        Assert.Equal("kaboom", node["error"]!.GetValue<string>());
    }

    [Fact]
    public void Malformed_json_is_error_with_null_id()
    {
        var node = JsonNode.Parse(Handler.Handle("not json"))!;
        Assert.False(node["ok"]!.GetValue<bool>());
        Assert.Null(node["id"]);
        Assert.Contains("JSON", node["error"]!.GetValue<string>());
    }

    [Fact]
    public void Missing_cmd_is_error()
    {
        var node = JsonNode.Parse(Handler.Handle("""{"id":9}"""))!;
        Assert.False(node["ok"]!.GetValue<bool>());
        Assert.Contains("cmd", node["error"]!.GetValue<string>());
    }

    [Fact]
    public void Response_is_single_line()
    {
        var response = Handler.Handle("""{"id":1,"cmd":"echo","profile":"p","args":{"x":"a\nb"}}""");
        Assert.DoesNotContain('\n', response);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --filter RequestHandlerTests`
Expected: FAIL — `Uex.Serve.RequestHandler` does not exist.

- [ ] **Step 3: Implement RequestHandler**

`src/Uex/Serve/RequestHandler.cs`:

```csharp
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Uex.Serve;

/// <summary>JSON-lines envelope: parse request, dispatch to the executor, wrap result/error. Never throws.</summary>
public sealed class RequestHandler(Func<string, string?, JsonNode?, JsonNode?> execute)
{
    private static readonly JsonSerializerOptions Compact = new() { WriteIndented = false };

    public string Handle(string requestLine)
    {
        JsonNode? id = null;
        try
        {
            JsonNode? request;
            try { request = JsonNode.Parse(requestLine); }
            catch (JsonException e) { throw new UexException($"Invalid JSON request: {e.Message}"); }
            id = request?["id"]?.DeepClone();
            var cmd = request?["cmd"]?.GetValue<string>()
                ?? throw new UexException("Request is missing 'cmd'.");
            var profile = request?["profile"]?.GetValue<string>();
            var result = execute(cmd, profile, request?["args"]);
            return new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result }
                .ToJsonString(Compact);
        }
        catch (Exception e)
        {
            var message = e is UexException ? e.Message : e.ToString().ReplaceLineEndings(" | ");
            return new JsonObject { ["id"] = id?.DeepClone(), ["ok"] = false, ["error"] = message }
                .ToJsonString(Compact);
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --filter RequestHandlerTests`
Expected: 5 passed.

- [ ] **Step 5: Implement ServeLoop (real executor) and the serve command**

`src/Uex/Serve/ServeLoop.cs`:

```csharp
using System.Text.Json.Nodes;
using CUE4Parse.FileProvider;
using Uex.Config;
using Uex.Core;

namespace Uex.Serve;

/// <summary>stdin/stdout JSON-lines server over all configured profiles.</summary>
public sealed class ServeLoop(ProfilesConfig config)
{
    private readonly ProviderManager _providers = new(config);
    private bool _shutdown;

    public int Run(TextReader input, TextWriter output)
    {
        var handler = new RequestHandler(Execute);
        output.WriteLine("""{"ok":true,"result":"uex serve ready - one JSON request per line"}""");
        while (!_shutdown && input.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            output.WriteLine(handler.Handle(line));
            output.Flush();
        }
        _providers.Dispose();
        return 0;
    }

    private JsonNode? Execute(string cmd, string? profileName, JsonNode? args)
    {
        if (cmd == "shutdown") { _shutdown = true; return JsonValue.Create("bye"); }
        if (cmd == "profiles")
            return new JsonArray([.. config.Profiles.Keys.Select(k => (JsonNode)JsonValue.Create(k))]);

        if (profileName is null) throw new UexException($"Command '{cmd}' requires 'profile'.");
        var provider = _providers.Get(profileName);
        return cmd switch
        {
            "list" => new JsonArray([.. VfsQuery.List(provider.Files.Keys, Str(args, "path") ?? "")
                .Select(e => (JsonNode)new JsonObject { ["name"] = e.Name, ["dir"] = e.IsDirectory })]),
            "search" => SearchNode(provider, args),
            "preview" => JsonValue.Create(AssetOps.Preview(provider,
                AssetOps.ResolvePackagePath(provider, Str(args, "asset") ?? throw new UexException("'asset' required")),
                Int(args, "maxBytes") ?? 200_000)),
            "preview-texture" => JsonValue.Create(AssetOps.SavePng(provider,
                AssetOps.ResolvePackagePath(provider, Str(args, "asset") ?? throw new UexException("'asset' required")),
                Str(args, "out") ?? throw new UexException("'out' required"))),
            "export" => ExportNode(provider, config.GetProfile(profileName), args),
            _ => throw new UexException($"Unknown command '{cmd}'. Commands: profiles, list, search, preview, preview-texture, export, shutdown."),
        };
    }

    private static JsonNode SearchNode(DefaultFileProvider provider, JsonNode? args)
    {
        var result = VfsQuery.Search(provider.Files.Keys,
            Str(args, "pattern") ?? throw new UexException("'pattern' required"),
            args?["regex"]?.GetValue<bool>() ?? false,
            Int(args, "limit") ?? 200);
        return new JsonObject
        {
            ["total"] = result.Total,
            ["matches"] = new JsonArray([.. result.Matches.Select(m => (JsonNode)JsonValue.Create(m))]),
        };
    }

    private static JsonNode ExportNode(DefaultFileProvider provider, GameProfile profile, JsonNode? args)
    {
        var only = args?["only"] is JsonArray arr
            ? arr.Select(n => n!.GetValue<string>()).ToList()
            : null;
        var summary = ExportRunner.Run(provider, profile, only);
        return new JsonObject
        {
            ["packages"] = summary.Packages,
            ["textures"] = summary.Textures,
            ["rawFiles"] = summary.RawFiles,
            ["errors"] = new JsonArray([.. summary.Errors.Take(50).Select(e => (JsonNode)JsonValue.Create(e))]),
            ["errorCount"] = summary.Errors.Count,
        };
    }

    private static string? Str(JsonNode? args, string key) => args?[key]?.GetValue<string>();
    private static int? Int(JsonNode? args, string key) => args?[key]?.GetValue<int>();
}
```

Add to `src/Uex/Program.cs` before the final return:

```csharp
// ---- serve -----------------------------------------------------------------
var serveCommand = new Command("serve", "JSON-lines request/response server on stdin/stdout (all profiles, lazy mounts)");
serveCommand.SetAction(parse => Run(() =>
    new Uex.Serve.ServeLoop(ProfilesConfig.LoadDefault(parse.GetValue(configOption)))
        .Run(Console.In, Console.Out)));
root.Subcommands.Add(serveCommand);
```

- [ ] **Step 6: Full test suite + smoke test**

Run: `dotnet test`
Expected: all tests pass.

```bash
printf '%s\n%s\n%s\n' \
  '{"id":1,"cmd":"profiles"}' \
  '{"id":2,"cmd":"list","profile":"aion2","args":{"path":"AION2/Content/Data"}}' \
  '{"id":3,"cmd":"shutdown"}' \
  | dotnet run --project src/Uex -- serve
```

Expected: ready banner, `{"id":1,"ok":true,"result":["aion2"]}`, a directory listing for id 2, `bye` for id 3, clean exit.

- [ ] **Step 7: Commit**

```bash
git add -A && git commit -m "feat: serve mode - JSON-lines server over all profiles"
```

---

### Task 9: MCP server

Stdio MCP server exposing the same operations as native tools. `profile` is a required string parameter on every tool (except `profiles`), so one registered MCP server covers every game — this is the "multiple games at the same time" story for agents.

**Files:**
- Create: `src/Uex/Mcp/UexMcpTools.cs`
- Modify: `src/Uex/Program.cs` (mcp command)

- [ ] **Step 1: Implement tools + host**

`src/Uex/Mcp/UexMcpTools.cs`:

```csharp
using System.ComponentModel;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Uex.Config;
using Uex.Core;

namespace Uex.Mcp;

[McpServerToolType]
public sealed class UexMcpTools(ProfilesConfig config, ProviderManager providers)
{
    [McpServerTool(Name = "profiles"), Description("List configured game profiles (use these names as the 'profile' argument of every other tool).")]
    public string Profiles() =>
        new JsonArray([.. config.Profiles.Select(p => (JsonNode)new JsonObject
        {
            ["name"] = p.Key, ["game"] = p.Value.Game, ["paksDir"] = p.Value.PaksDir,
        })]).ToJsonString();

    [McpServerTool(Name = "list_dir"), Description("List children of a virtual pak directory, like FModel's tree view. Directories end with '/'.")]
    public string ListDir(
        [Description("Game profile name")] string profile,
        [Description("Virtual directory path; empty string for the root")] string path = "")
    {
        var provider = providers.Get(profile);
        return string.Join("\n",
            VfsQuery.List(provider.Files.Keys, path).Select(e => e.IsDirectory ? e.Name + "/" : e.Name));
    }

    [McpServerTool(Name = "search_paths"), Description("Search all virtual paths of a game (case-insensitive substring, or regex). Returns matches plus total count.")]
    public string SearchPaths(
        [Description("Game profile name")] string profile,
        [Description("Substring or regex pattern")] string pattern,
        [Description("Interpret pattern as regex")] bool regex = false,
        [Description("Max matches returned")] int limit = 200)
    {
        var provider = providers.Get(profile);
        var result = VfsQuery.Search(provider.Files.Keys, pattern, regex, limit);
        return $"total: {result.Total}\n" + string.Join("\n", result.Matches);
    }

    [McpServerTool(Name = "preview_asset"), Description("Serialize a pak asset to FModel-style JSON (truncated beyond maxBytes).")]
    public string PreviewAsset(
        [Description("Game profile name")] string profile,
        [Description("Virtual asset path; .uasset/.umap extension optional")] string asset,
        [Description("Truncate JSON beyond this many characters")] int maxBytes = 100_000)
    {
        var provider = providers.Get(profile);
        return AssetOps.Preview(provider, AssetOps.ResolvePackagePath(provider, asset), maxBytes);
    }

    [McpServerTool(Name = "preview_texture"), Description("Decode a texture asset to a PNG file on disk and return the file path (read the file to view it).")]
    public string PreviewTexture(
        [Description("Game profile name")] string profile,
        [Description("Virtual asset path of the texture package")] string asset,
        [Description("PNG output file path; default: a temp file")] string? outPath = null)
    {
        var provider = providers.Get(profile);
        outPath ??= Path.Combine(Path.GetTempPath(), "uex",
            Path.GetFileNameWithoutExtension(asset) + ".png");
        return AssetOps.SavePng(provider, AssetOps.ResolvePackagePath(provider, asset), outPath);
    }

    [McpServerTool(Name = "export_assets"), Description("Batch export to the profile's outputDir (FModel-compatible JSON/PNG tree). With no 'only', exports the profile's configured exportRoots.")]
    public string ExportAssets(
        [Description("Game profile name")] string profile,
        [Description("Optional list of virtual path prefixes to restrict the export")] string[]? only = null)
    {
        var gameProfile = config.GetProfile(profile);
        var summary = ExportRunner.Run(providers.Get(profile), gameProfile, only);
        var errors = summary.Errors.Count == 0 ? "" :
            $"\nerrors ({summary.Errors.Count}):\n" + string.Join("\n", summary.Errors.Take(20));
        return $"exported {summary.Packages} packages, {summary.Textures} textures, {summary.RawFiles} raw files -> {gameProfile.OutputDir}{errors}";
    }
}

public static class McpHost
{
    public static async Task<int> RunAsync(ProfilesConfig config)
    {
        var builder = Host.CreateApplicationBuilder();
        // stdout carries the MCP protocol - all logging must go to stderr
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton<ProviderManager>();
        builder.Services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools<UexMcpTools>();
        await builder.Build().RunAsync();
        return 0;
    }
}
```

Add to `src/Uex/Program.cs` before the final return:

```csharp
// ---- mcp -------------------------------------------------------------------
var mcpCommand = new Command("mcp", "MCP stdio server exposing list/search/preview/export tools for all profiles");
mcpCommand.SetAction((parse, cancellationToken) =>
{
    try
    {
        return Uex.Mcp.McpHost.RunAsync(ProfilesConfig.LoadDefault(parse.GetValue(configOption)));
    }
    catch (UexException e)
    {
        Console.Error.WriteLine($"error: {e.Message}");
        return Task.FromResult(1);
    }
});
root.Subcommands.Add(mcpCommand);
```

(If `SetAction` with the async signature differs in 2.0.10, the sync overload wrapping `.GetAwaiter().GetResult()` works too.)

- [ ] **Step 2: Build + handshake smoke test**

Run: `dotnet build`
Then:

```bash
printf '%s\n' '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"smoke","version":"0"}}}' \
  | UEX_PROFILES=/e/arkive-games/uex/profiles.json dotnet run --project src/Uex -- mcp | head -1
```

Expected: a single JSON-RPC `initialize` response on stdout listing serverInfo (tool listing requires the full handshake; a real check happens in Step 3).

- [ ] **Step 3: Register with Claude Code and verify tools**

```bash
dotnet publish src/Uex -c Release -o /e/arkive-games/uex/publish
claude mcp add uex --scope user -e UEX_PROFILES=E:/arkive-games/uex/profiles.json -- E:/arkive-games/uex/publish/uex.exe mcp
```

Then in a fresh Claude Code session: the `uex` MCP server should list tools `profiles`, `list_dir`, `search_paths`, `preview_asset`, `preview_texture`, `export_assets`; calling `profiles` returns aion2; `list_dir(profile="aion2")` returns the root listing.

- [ ] **Step 4: Commit**

```bash
git add -A && git commit -m "feat: MCP stdio server with per-profile pak tools"
```

---

### Task 10: profiles.example.json, README, CLAUDE.md

**Files:**
- Create: `profiles.example.json`, `CLAUDE.md`
- Modify: `README.md`

- [ ] **Step 1: profiles.example.json**

```json
{
  "profiles": {
    "palworld": {
      "game": "GAME_Palworld",
      "paksDir": "E:/SteamLibrary/steamapps/common/Palworld/Pal/Content/Paks",
      "usmap": "FILL_ME/Palworld.usmap",
      "aesKey": null,
      "outputDir": "E:/SteamLibrary/steamapps/common/Palworld/Exports",
      "exportRoots": ["Pal/Content/Pal", "Pal/Content/L10N", "Pal/Config"]
    },
    "aion2": {
      "game": "GAME_Aion2",
      "paksDir": "G:/NCSoft/AION2_TW/Aion2/Content/Paks",
      "usmap": "G:/NCSoft/FModel/AION2-5.3.2.0-1.0.34.0.usmap",
      "aesKey": "0xFILL_ME",
      "outputDir": "G:/NCSoft/Export/Exports",
      "exportRoots": ["AION2/Content/Data", "AION2/Config"]
    }
  }
}
```

(Palworld exportRoots cover exactly what the arkive pipeline reads: `PALWORLD_RAW` = `…/Exports/Pal/Content/Pal`, locales from `…/L10N`, version stamp from `…/Pal/Config/DefaultGame.ini`. The real AES key stays out of the committed example.)

- [ ] **Step 2: README.md**

Rewrite with: what uex is (headless FModel-compatible exporter + explorer on CUE4Parse); requirements (.NET 10 SDK, game paks, per-game usmap/AES); setup (`cp profiles.example.json profiles.json`, fill values from your FModel settings); command reference (`doctor`, `export [--only]`, `list`, `search [--regex] [--limit]`, `preview [--max-bytes]`, `preview-texture --out`, `serve` with the JSON-lines protocol example, `mcp` with the `claude mcp add` line); multi-game section (profiles = games; every command/request/tool names its profile; one serve/MCP process handles all games, mounting lazily per profile); note that `profiles.json` is gitignored because it holds AES keys.

- [ ] **Step 3: CLAUDE.md**

```markdown
# uex — UE pak export & exploration tool

Standalone .NET 10 console tool on CUE4Parse. Replaces manual FModel exports for the
arkive pipeline (E:\arkive-games\arkive) and gives agents pak exploration via CLI,
`serve` (JSON-lines), and `mcp` (stdio MCP server).

## Commands
- Build: `dotnet build` — Test: `dotnet test` — Run: `dotnet run --project src/Uex -- <cmd>`
- Publish: `dotnet publish src/Uex -c Release -o publish`
- Health check: `uex doctor --profile <name>` (mounts paks, parses 3 probe assets)

## Architecture
- `Config/ProfilesConfig` — named per-game profiles (profiles.json, gitignored: AES keys).
- `Core/ProviderManager` — lazily mounts one CUE4Parse provider per profile, cached;
  every operation takes a `profile` parameter → one process serves many games.
- `Core/OutputPaths`, `Core/VfsQuery` — pure, unit-tested (no paks needed).
- `Core/ExportRunner` — batch export, FModel-compatible tree: packages → `.json`
  (serialized exports array), textures → `.png`, other files raw-copied.
- `Serve/` — JSON-lines stdin/stdout server. `Mcp/` — MCP stdio server, same ops.

## Conventions
- Tests must not require game paks; real-pak verification is `doctor` (AION2 is
  installed locally at G:\NCSoft\AION2_TW; Palworld is not on this machine).
- Output layout compatibility with FModel is a hard contract — the arkive `tools/`
  pipeline consumes it (`PALWORLD_RAW` etc.). Semantic JSON equality is the bar.
- CUE4Parse pins: net10.0-only package; Oodle/zlib DLLs auto-download to `.uex-cache/`.
```

- [ ] **Step 4: Full verification + commit**

Run: `dotnet test && dotnet run --project src/Uex -- doctor --profile aion2`
Expected: all unit tests pass; doctor OK.

```bash
git add -A && git commit -m "docs: profiles example, README, CLAUDE.md"
```

---

## Follow-ups (separate work, NOT in this plan)

1. **arkive monorepo integration:** add the published `uex.exe` path to `tools/.env`, create `uv run python -m palworld.refresh` (runs `uex export --profile palworld` then the twelve stages in documented order), and register the uex MCP server in `arkive/.mcp.json`. Do this in the arkive repo after uex proves itself on a real Palworld patch day.
2. **Palworld profile validation:** on the machine with Palworld installed, fill the usmap path, run `uex doctor --profile palworld`, then a full export and diff against the last FModel export tree before swapping `PALWORLD_RAW`.
3. **AION2 pipeline cutover:** AION2's raw export currently comes from FModel bulk export at `G:\NCSoft\Export\Exports`; once uex output diffs clean, patch-day AION2 exports also become one command (and Perforce-sourced paks later just change `paksDir`).
4. **MapStructTypes support** in profiles — only if AION2 doctor probes reveal map-struct parse failures.
5. **GitHub remote:** create a repo and push (all other repo origins are SSH `git@github.com:...`).

