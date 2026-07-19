using System.Collections.Concurrent;
using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.Texture;
using CUE4Parse_Conversion.Textures;
using Uex.Config;

namespace Uex.Core;

public sealed record ExportSummary(int Packages, int Textures, int RawFiles, int DecodedData, List<string> Errors);

/// <summary>Batch export of everything under the profile's exportRoots (or an explicit subset) into the FModel-compatible tree.</summary>
public static class ExportRunner
{
    public static ExportSummary Run(DefaultFileProvider provider, GameProfile profile,
        IReadOnlyList<string>? only = null, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(profile.OutputDir))
            throw new UexException("Profile has no outputDir configured.");
        var roots = only is { Count: > 0 } ? only : profile.ExportRoots;
        if (roots.Count == 0)
            throw new UexException("Nothing to export: profile has no exportRoots and no --only paths given.");

        var targets = provider.Files.Keys
            .Where(k => !OutputPaths.IsPackagePart(k) && OutputPaths.IsUnderAnyRoot(k, roots))
            .ToList();
        if (targets.Count == 0)
            throw new UexException($"No files under roots [{string.Join(", ", roots)}].");

        int packages = 0, textures = 0, rawFiles = 0, decodedData = 0, done = 0;
        var errors = new ConcurrentBag<string>();
        var game = provider.Versions.Game;
        Parallel.ForEach(targets, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, vpath =>
        {
            try
            {
                if (OutputPaths.IsPackage(vpath))
                {
                    var package = provider.LoadPackage(vpath);
                    var exports = package.GetExports().ToArray();
                    WriteText(profile.OutputDir, OutputPaths.ForPackageJson(vpath),
                        Newtonsoft.Json.JsonConvert.SerializeObject(exports, Newtonsoft.Json.Formatting.Indented));
                    Interlocked.Increment(ref packages);
                    var textureIndex = 0;
                    foreach (var export in exports)
                    {
                        if (export is not UTexture texture) continue;
                        var decoded = texture.Decode();
                        if (decoded is null) continue;
                        var png = decoded.Encode(ETextureFormat.Png, false, out _);
                        WriteBytes(profile.OutputDir, OutputPaths.ForTexturePng(vpath, texture.Name, textureIndex++), png);
                        Interlocked.Increment(ref textures);
                    }
                }
                else if (Aion2Dat.Handles(game, vpath))
                {
                    try
                    {
                        WriteText(profile.OutputDir, OutputPaths.ForPackageJson(vpath),
                            Aion2Dat.ToJson(provider, vpath));
                        Interlocked.Increment(ref decodedData);
                    }
                    catch (Exception e)
                    {
                        // Decode failed: record it, then preserve the file as a raw copy so nothing is lost.
                        errors.Add($"{vpath}: decode failed ({e.Message}); wrote raw copy");
                        WriteBytes(profile.OutputDir, OutputPaths.ForRaw(vpath), provider.Files[vpath].Read());
                        Interlocked.Increment(ref rawFiles);
                    }
                }
                else
                {
                    WriteBytes(profile.OutputDir, OutputPaths.ForRaw(vpath), provider.Files[vpath].Read());
                    Interlocked.Increment(ref rawFiles);
                }
            }
            catch (Exception e)
            {
                errors.Add($"{vpath}: {e.Message}");
            }
            var current = Interlocked.Increment(ref done);
            if (current % 500 == 0) log?.Invoke($"{current}/{targets.Count} ...");
        });
        return new ExportSummary(packages, textures, rawFiles, decodedData, [.. errors.Order()]);
    }

    private static void WriteText(string outDir, string relPath, string content)
    {
        var path = Path.Combine(outDir, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private static void WriteBytes(string outDir, string relPath, byte[] content)
    {
        var path = Path.Combine(outDir, relPath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }
}
