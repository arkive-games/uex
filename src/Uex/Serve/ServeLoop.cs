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
        output.Flush();
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
            ["decodedData"] = summary.DecodedData,
            ["errors"] = new JsonArray([.. summary.Errors.Take(50).Select(e => (JsonNode)JsonValue.Create(e))]),
            ["errorCount"] = summary.Errors.Count,
        };
    }

    private static string? Str(JsonNode? args, string key) => args?[key]?.GetValue<string>();
    private static int? Int(JsonNode? args, string key) => args?[key]?.GetValue<int>();
}
