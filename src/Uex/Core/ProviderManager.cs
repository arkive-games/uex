using System.Collections.Concurrent;
using CUE4Parse.Compression;
using CUE4Parse.Encryption.Aes;
using CUE4Parse.FileProvider;
using CUE4Parse.MappingsProvider.Usmap;
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

        var provider = new DefaultFileProvider(p.PaksDir, SearchOption.AllDirectories,
            new VersionContainer(game), StringComparer.OrdinalIgnoreCase);
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
