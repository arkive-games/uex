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

return root.Parse(args).Invoke();
