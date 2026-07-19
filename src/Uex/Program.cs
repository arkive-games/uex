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
    catch (Exception e) { Console.Error.WriteLine($"unexpected error: {e.GetType().Name}: {e.Message}"); return 1; }
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
    Console.WriteLine($"exported: {summary.Packages} packages, {summary.Textures} textures, {summary.DecodedData} decoded data, {summary.RawFiles} raw files -> {profile.OutputDir}");
    if (summary.Errors.Count > 0)
    {
        Console.Error.WriteLine($"{summary.Errors.Count} assets failed:");
        foreach (var error in summary.Errors.Take(20)) Console.Error.WriteLine($"  {error}");
        if (summary.Errors.Count > 20) Console.Error.WriteLine($"  ... and {summary.Errors.Count - 20} more");
    }
    return 0; // partial parse failures are normal for a full-tree export; doctor is the health gate
}));
root.Subcommands.Add(exportCommand);

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

// ---- serve -----------------------------------------------------------------
var serveCommand = new Command("serve", "JSON-lines request/response server on stdin/stdout (all profiles, lazy mounts)");
serveCommand.SetAction(parse => Run(() =>
    new Uex.Serve.ServeLoop(ProfilesConfig.LoadDefault(parse.GetValue(configOption)))
        .Run(Console.In, Console.Out)));
root.Subcommands.Add(serveCommand);

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

return root.Parse(args).Invoke();
