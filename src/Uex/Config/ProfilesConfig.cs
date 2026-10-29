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

    /// <summary>The local.json that supplies this profile's machine paths (profiles-directory layout only).</summary>
    [JsonIgnore] public string? LocalFile { get; set; }

    /// <summary>Error-message suffix naming the local.json to fix; empty for single-file profiles.</summary>
    public string LocalHint(string field) =>
        LocalFile is null ? "" : $" (set \"{field}\" for this profile in {LocalFile}; see local.example.json)";
}

/// <summary>A game directory's profile.json: everything that is the same on every machine.</summary>
internal sealed class SharedGameProfile
{
    [JsonPropertyName("game")] public string Game { get; set; } = "";
    [JsonPropertyName("usmap")] public string? Usmap { get; set; }
    [JsonPropertyName("aesKey")] public string? AesKey { get; set; }
    [JsonPropertyName("exportRoots")] public List<string> ExportRoots { get; set; } = [];
    /// <summary>Profile names this game provides; defaults to the directory name.</summary>
    [JsonPropertyName("variants")] public List<string>? Variants { get; set; }
}

/// <summary>One entry of a game directory's local.json: where the game lives on this machine.</summary>
internal sealed class LocalGameProfile
{
    [JsonPropertyName("paksDir")] public string? PaksDir { get; set; }
    [JsonPropertyName("outputDir")] public string? OutputDir { get; set; }
}

public sealed class ProfilesConfig
{
    public const string SharedFileName = "profile.json";
    public const string LocalFileName = "local.json";

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

    /// <summary>Loads a single profiles.json, or a profiles directory (a uex-profiles checkout).</summary>
    public static ProfilesConfig Load(string path) =>
        Directory.Exists(path) ? LoadDirectory(path) : LoadFile(path);

    private static ProfilesConfig LoadFile(string path)
    {
        if (!File.Exists(path))
            throw new UexException(
                $"Profiles config not found: {path}. Point UEX_PROFILES at a uex-profiles checkout, " +
                "or copy profiles.example.json to profiles.json and fill in your games.");
        var config = ReadJson<ProfilesConfig>(path);
        if (config is null || config.Profiles.Count == 0)
            throw new UexException($"{path} contains no profiles.");
        foreach (var (name, p) in config.Profiles)
        {
            RequireGame(name, p.Game);
            if (string.IsNullOrWhiteSpace(p.PaksDir))
                throw new UexException($"Profile '{name}': missing required field 'paksDir'.");
        }
        return config;
    }

    /// <summary>
    /// One subdirectory per game: <c>&lt;game&gt;/profile.json</c> (shared, committed) beside its usmap,
    /// plus an optional gitignored <c>&lt;game&gt;/local.json</c> keyed by profile name with the machine
    /// paths. A missing paksDir is not an error here, only for the profile actually used, so a game
    /// that isn't installed on this machine doesn't break the others.
    /// </summary>
    private static ProfilesConfig LoadDirectory(string root)
    {
        var config = new ProfilesConfig
        {
            Profiles = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase),
        };
        foreach (var dir in Directory.GetDirectories(root).Order(StringComparer.OrdinalIgnoreCase))
        {
            var sharedPath = Path.Combine(dir, SharedFileName);
            if (!File.Exists(sharedPath)) continue;
            var shared = ReadJson<SharedGameProfile>(sharedPath)
                ?? throw new UexException($"{sharedPath} is empty.");
            var names = shared.Variants is { Count: > 0 } ? shared.Variants : [Path.GetFileName(dir)];
            RequireGame(names[0], shared.Game);

            var localPath = Path.Combine(dir, LocalFileName);
            var local = new Dictionary<string, LocalGameProfile>(StringComparer.OrdinalIgnoreCase);
            if (File.Exists(localPath))
                foreach (var (key, value) in ReadJson<Dictionary<string, LocalGameProfile>>(localPath) ?? [])
                {
                    if (!names.Contains(key, StringComparer.OrdinalIgnoreCase))
                        throw new UexException(
                            $"{localPath}: unknown profile '{key}'. This game's profiles: {string.Join(", ", names)}");
                    local[key] = value;
                }

            // usmap is relative to profile.json, so the checkout works wherever it is cloned.
            var usmap = string.IsNullOrWhiteSpace(shared.Usmap) ? null : Path.GetFullPath(Path.Combine(dir, shared.Usmap));
            foreach (var name in names)
            {
                var machine = local.GetValueOrDefault(name);
                var profile = new GameProfile
                {
                    Game = shared.Game,
                    Usmap = usmap,
                    AesKey = shared.AesKey,
                    ExportRoots = [.. shared.ExportRoots],
                    PaksDir = machine?.PaksDir ?? "",
                    OutputDir = machine?.OutputDir ?? "",
                    LocalFile = localPath,
                };
                if (!config.Profiles.TryAdd(name, profile))
                    throw new UexException($"{sharedPath}: profile '{name}' is already defined by another game directory.");
            }
        }
        if (config.Profiles.Count == 0)
            throw new UexException($"{root} contains no <game>/{SharedFileName} files.");
        return config;
    }

    private static void RequireGame(string name, string game)
    {
        if (string.IsNullOrWhiteSpace(game))
            throw new UexException($"Profile '{name}': missing required field 'game' (an EGame name, e.g. GAME_Palworld).");
    }

    private static T? ReadJson<T>(string path)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(File.ReadAllText(path));
        }
        catch (JsonException e)
        {
            throw new UexException($"Invalid JSON in {path}: {e.Message}");
        }
    }

    /// <summary>--config > UEX_PROFILES env > cwd/profiles.json > exe-dir/profiles.json. Any of them may name a profiles directory.</summary>
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
