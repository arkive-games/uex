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

    [McpServerTool(Name = "preview_asset"), Description("Serialize a pak asset to FModel-style JSON (truncated beyond maxBytes). Handles UE packages and AION2 .dat data files.")]
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
        return $"exported {summary.Packages} packages, {summary.Textures} textures, {summary.RawFiles} raw files, {summary.DecodedData} decoded data files -> {gameProfile.OutputDir}{errors}";
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
