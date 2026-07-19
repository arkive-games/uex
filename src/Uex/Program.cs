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

return root.Parse(args).Invoke();
