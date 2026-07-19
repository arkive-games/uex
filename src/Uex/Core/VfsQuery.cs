using System.Text.RegularExpressions;

namespace Uex.Core;

/// <summary>Directory listing and search over the flat virtual-path set of a mounted provider.</summary>
public static class VfsQuery
{
    public readonly record struct Entry(string Name, bool IsDirectory);
    public sealed record SearchResult(List<string> Matches, int Total);

    /// <summary>Children of a virtual directory: subdirectories first, then files, each name-sorted. Package parts (.uexp/.ubulk/.uptnl) are hidden.</summary>
    public static List<Entry> List(IEnumerable<string> allPaths, string dirPath)
    {
        var dir = OutputPaths.Normalize(dirPath);
        var prefix = dir.Length == 0 ? "" : dir + "/";
        var dirs = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = dir.Length == 0;
        foreach (var raw in allPaths)
        {
            var path = OutputPaths.Normalize(raw);
            if (prefix.Length > 0 && !path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            found = true;
            var rest = path[prefix.Length..];
            var slash = rest.IndexOf('/');
            if (slash >= 0) dirs.Add(rest[..slash]);
            else if (!OutputPaths.IsPackagePart(rest)) files.Add(rest);
        }
        if (!found)
            throw new UexException($"No such directory in paks: {dir}");
        return
        [
            .. dirs.Select(d => new Entry(d, true)),
            .. files.Select(f => new Entry(f, false)),
        ];
    }

    /// <summary>Substring (default) or regex match over full virtual paths, case-insensitive. Package parts are hidden.</summary>
    public static SearchResult Search(IEnumerable<string> allPaths, string pattern, bool regex, int limit)
    {
        Func<string, bool> matches;
        if (regex)
        {
            Regex compiled;
            try { compiled = new Regex(pattern, RegexOptions.IgnoreCase); }
            catch (ArgumentException e) { throw new UexException($"Invalid regex '{pattern}': {e.Message}"); }
            matches = compiled.IsMatch;
        }
        else
        {
            matches = p => p.Contains(pattern, StringComparison.OrdinalIgnoreCase);
        }

        var hits = new List<string>();
        var total = 0;
        foreach (var raw in allPaths.Select(OutputPaths.Normalize).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (OutputPaths.IsPackagePart(raw) || !matches(raw)) continue;
            total++;
            if (hits.Count < limit) hits.Add(raw);
        }
        return new SearchResult(hits, total);
    }
}
