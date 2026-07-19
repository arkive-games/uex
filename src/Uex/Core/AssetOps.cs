using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
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

    /// <summary>Decode the first texture export of a package to a PNG file; returns the written path.</summary>
    public static string SavePng(DefaultFileProvider provider, string vpath, string outPath)
    {
        var package = provider.LoadPackage(vpath);
        foreach (var export in package.GetExports())
        {
            if (export is not UTexture texture) continue;
            var decoded = texture.Decode()
                ?? throw new UexException($"Texture failed to decode: {vpath}");
            var png = decoded.Encode(ETextureFormat.Png, false, out _);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outPath))!);
            File.WriteAllBytes(outPath, png);
            return Path.GetFullPath(outPath);
        }
        throw new UexException($"No texture export in package: {vpath}");
    }

    /// <summary>Serialize with a byte cap for agent previews; truncated output is marked and not valid JSON.</summary>
    public static string Preview(DefaultFileProvider provider, string vpath, int maxBytes)
    {
        string content;
        if (Aion2Dat.Handles(provider.Versions.Game, vpath))
            content = Aion2Dat.ToJson(provider, vpath);
        else if (OutputPaths.IsPackage(vpath))
            content = SerializePackage(provider, vpath);
        else
            throw new UexException($"Not a previewable asset (UE package or known data file): {vpath}");

        if (content.Length <= maxBytes) return content;
        return content[..maxBytes] + $"\n... [truncated {content.Length - maxBytes} of {content.Length} chars - use --max-bytes to raise]";
    }
}
