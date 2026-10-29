using CUE4Parse.FileProvider;
using CUE4Parse.UE4.Localization;

namespace Uex.Core;

/// <summary>
/// Localization string tables (<c>.locres</c>) decode to FModel's JSON shape,
/// <c>{ "namespace": { "key": "string" } }</c>, written beside the file as <c>.json</c>.
/// A game's DataTables only carry FText namespace/key pairs, so this is where every
/// display string actually lives; a raw copy is unreadable without a parser of its own.
/// </summary>
public static class Locres
{
    public static bool Handles(string vpath) =>
        vpath.EndsWith(".locres", StringComparison.OrdinalIgnoreCase);

    public static string ToJson(DefaultFileProvider provider, string vpath)
    {
        if (!provider.Files[vpath].TryCreateReader(out var archive))
            throw new UexException($"Could not open {vpath}");
        using (archive)
            return Newtonsoft.Json.JsonConvert.SerializeObject(
                new FTextLocalizationResource(archive), Newtonsoft.Json.Formatting.Indented);
    }
}
