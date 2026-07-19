using System.Text;
using CUE4Parse.FileProvider;
using CUE4Parse.GameTypes.Aion2.Encryption.Aes;
using CUE4Parse.GameTypes.Aion2.Objects;
using CUE4Parse.UE4.Versions;
using Newtonsoft.Json;

namespace Uex.Core;

/// <summary>
/// AION2 stores game data as obfuscated loose .dat files; CUE4Parse ships dedicated readers.
/// Mirrors the FModel output: &lt;name&gt;.json with the decoded object.
/// </summary>
public static class Aion2Dat
{
    // Case-insensitive substring matches against a leading-slash normalized virtual path.
    // Order matters: the more specific dirs (MapDataHierarchy, MapEvent) must be tested
    // before the generic Map prefix, because they also contain "/data/map".
    private const string TableDir = "/data/table/";
    private const string HierarchyDir = "/data/mapdatahierarchy/";
    private const string WorldMapDir = "/data/worldmap/";
    private const string MapEventDir = "/data/mapevent/";
    private const string MapDir = "/data/map"; // Data/Map, Data/MapData... (and, as a superset, MapEvent — filtered above)

    /// <summary>True when this profile/game + path combination should be decoded
    /// (game is GAME_Aion2, extension .dat, under a known Data dir).</summary>
    public static bool Handles(EGame game, string vpath)
    {
        if (game != EGame.GAME_Aion2) return false;
        if (!vpath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)) return false;

        var lower = ("/" + OutputPaths.Normalize(vpath)).ToLowerInvariant();

        // key_manifest.dat is the key source, not data — never decode it.
        if (lower.EndsWith("/key_manifest.dat")) return false;

        return lower.Contains(TableDir)
            || lower.Contains(HierarchyDir)
            || lower.Contains(WorldMapDir)
            || lower.Contains(MapEventDir)
            || lower.Contains(MapDir);
    }

    /// <summary>Populate the AION2 AES key cache single-threaded before parallel decoding —
    /// CUE4Parse's Aion2DatFileAes.Initialize has a check-then-act race on static state.</summary>
    public static void Warmup(DefaultFileProvider provider, EGame game)
    {
        if (game != EGame.GAME_Aion2) return;
        try { Aion2DatFileAes.Initialize(provider); }
        catch { /* no key manifest mounted — per-file decodes will fail and fall back to raw copies */ }
    }

    /// <summary>Decode to FModel-compatible JSON. Throws on failure (caller decides fallback).</summary>
    public static string ToJson(DefaultFileProvider provider, string vpath)
    {
        var file = provider.Files[vpath];
        var lower = ("/" + OutputPaths.Normalize(vpath)).ToLowerInvariant();

        // MapEvent .dat files are obfuscated JSON text (not a CUE4Parse object): FModel
        // decrypts the keystream and writes the payload verbatim. Everything else is an
        // object serialized via the reader's JsonConverter.
        if (lower.Contains(MapEventDir))
            return DecryptToText(file);

        object obj;
        if (lower.Contains(TableDir))
            obj = new FAion2DataTableFile(file, provider);
        else if (lower.Contains(HierarchyDir))
            obj = new FAion2MapHierarchyFile(file);
        else if (lower.Contains(WorldMapDir))
            obj = new FAion2MapDataFile(file, provider);
        else // under /Data/Map*
        {
            // Only actual MapData.dat payloads carry the AION map struct. Sibling files under
            // Data/Map (e.g. NavSignature.dat) are a different, unparsed format: FModel emits
            // the base-class default for them, and the map-data reader would otherwise read a
            // garbage FString length and blow the stack. Match FModel: empty defaults.
            if (!file.NameWithoutExtension.Equals("MapData", StringComparison.OrdinalIgnoreCase))
                return JsonConvert.SerializeObject(
                    new { Version = 0, Ids = Array.Empty<string>() }, Formatting.Indented);
            obj = new FAion2MapDataFile(file, provider);
        }

        return JsonConvert.SerializeObject(obj, Formatting.Indented);
    }

    /// <summary>Read + keystream-decrypt a .dat whose decrypted payload is UTF-8 JSON text.</summary>
    private static string DecryptToText(CUE4Parse.FileProvider.Objects.GameFile file)
    {
        var data = file.SafeRead() ?? throw new InvalidOperationException("empty .dat");
        FAion2DatFileArchive.DecryptData(data);
        // Payload is UTF-8 (no BOM in the FModel reference). Strip a trailing NUL pad if present.
        var len = data.Length;
        while (len > 0 && data[len - 1] == 0) len--;
        return Encoding.UTF8.GetString(data, 0, len);
    }
}
