namespace Uex.Core;

/// <summary>Pure virtual-path rules for the FModel-compatible export layout.</summary>
public static class OutputPaths
{
    private static readonly string[] PackageExts = [".uasset", ".umap"];
    private static readonly string[] PartExts = [".uexp", ".ubulk", ".uptnl"];

    public static string Normalize(string vpath) => vpath.Replace('\\', '/').Trim('/');

    public static bool IsPackage(string vpath) =>
        PackageExts.Any(e => vpath.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static bool IsPackagePart(string vpath) =>
        PartExts.Any(e => vpath.EndsWith(e, StringComparison.OrdinalIgnoreCase));

    public static string ForPackageJson(string vpath) => SwapExtension(vpath, ".json");

    /// <summary>First texture export in a package keeps the package name; extras get ".{exportName}.png".</summary>
    public static string ForTexturePng(string vpath, string exportName, int index) =>
        index == 0 ? SwapExtension(vpath, ".png") : SwapExtension(vpath, $".{exportName}.png");

    public static string ForRaw(string vpath) => vpath;

    public static bool IsUnderAnyRoot(string vpath, IEnumerable<string> roots)
    {
        var path = Normalize(vpath);
        foreach (var root in roots)
        {
            var r = Normalize(root);
            if (path.Equals(r, StringComparison.OrdinalIgnoreCase) ||
                path.StartsWith(r + "/", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string SwapExtension(string vpath, string newExt)
    {
        var dot = vpath.LastIndexOf('.');
        return (dot < 0 ? vpath : vpath[..dot]) + newExt;
    }
}
