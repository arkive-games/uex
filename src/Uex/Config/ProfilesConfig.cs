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
