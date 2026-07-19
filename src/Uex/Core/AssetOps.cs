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
