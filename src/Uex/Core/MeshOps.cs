using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Assets.Exports.SkeletalMesh;
using CUE4Parse.UE4.Assets.Exports.StaticMesh;
using CUE4Parse_Conversion;
using CUE4Parse_Conversion.Options;

namespace Uex.Core;

public sealed record MeshExportSummary(int Packages, int Meshes, List<string> Files, List<string> Errors);

/// <summary>
/// Mesh export, which the JSON/PNG tree of <see cref="ExportRunner"/> deliberately does not cover:
/// a mesh is not consumable as serialized properties, and the arkive pipeline reads that tree, so
/// meshes go to their own output directory rather than being folded into it.
/// </summary>
public static class MeshOps
{
    /// <summary>Export every skeletal/static mesh under <paramref name="roots"/> into <paramref name="outDir"/>.</summary>
    public static MeshExportSummary Export(DefaultFileProvider provider, IReadOnlyList<string> roots,
        string outDir, EMeshFormat format, bool exportMaterials, Action<string>? log = null)
    {
        if (roots.Count == 0)
            throw new UexException("Nothing to export: no paths given.");

        var targets = provider.Files.Keys
            .Where(k => OutputPaths.IsPackage(k) && OutputPaths.IsUnderAnyRoot(k, roots))
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (targets.Count == 0)
            throw new UexException($"No packages under paths [{string.Join(", ", roots)}].");

        var session = new ExportSession();
        var errors = new List<string>();
        int packages = 0, meshes = 0;
        foreach (var vpath in targets)
        {
            try
            {
                var found = false;
                foreach (var export in provider.LoadPackage(vpath).GetExports())
                {
                    // Only the mesh types; a package also holds materials and textures, which the
                    // session pulls in itself when ExportMaterials is on. Queueing them separately
                    // would fight the exporter over the same output file.
                    if (export is not (USkeletalMesh or UStaticMesh)) continue;
                    session.Add(export);
                    meshes++;
                    found = true;
                }
                if (found) packages++;
            }
            catch (Exception e)
            {
                errors.Add($"{vpath}: {e.Message}");
            }
        }
        if (meshes == 0)
            throw new UexException($"No skeletal or static mesh exports found in {targets.Count} package(s) under [{string.Join(", ", roots)}].");

        log?.Invoke($"queued {meshes} mesh(es) from {packages} package(s); writing to {outDir} ...");
        Directory.CreateDirectory(outDir);
        var options = new ExportOptions(meshFormat: format, exportMaterials: exportMaterials);
        var results = session.RunAsync(outDir, options).GetAwaiter().GetResult();

        var files = new List<string>();
        foreach (var result in results)
        {
            if (result.Success)
                files.AddRange(result.DiskFilePaths ?? []);
            else
                errors.Add($"{result.ObjectPath}: {Describe(result.Error)}");
        }
        files.Sort(StringComparer.OrdinalIgnoreCase);
        return new MeshExportSummary(packages, meshes, files, errors);
    }

    /// <summary>
    /// Exception type and the deepest CUE4Parse frame, not just the message.
    /// A bare "Object reference not set to an instance of an object." names neither the
    /// asset's problem nor the code that tripped over it, which is useless for deciding
    /// whether a failure is our bug, the library's, or a genuinely malformed asset.
    /// </summary>
    private static string Describe(Exception? error)
    {
        if (error is null) return "export failed";
        var inner = error;
        while (inner.InnerException is not null) inner = inner.InnerException;
        var frame = (inner.StackTrace ?? "").Split('\n')
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.StartsWith("at CUE4Parse", StringComparison.Ordinal));
        return frame is null
            ? $"{inner.GetType().Name}: {inner.Message}"
            : $"{inner.GetType().Name}: {inner.Message} [{frame}]";
    }
}
