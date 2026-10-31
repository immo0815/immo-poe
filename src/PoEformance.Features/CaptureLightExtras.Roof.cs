using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace PoEformance.Features;

/// <summary>
/// The light extras' tenth section: the install's files naming the roof fade's own words - where an object, a tile or a room sets how
/// its roof is faded round the player.
/// </summary>
/// <remarks>
/// WRITTEN FOR THE ROOF FADE (v0.1.381). The shader side is in the files and settled: a material whose .mat says
/// <c>"roofsection": true</c> wears the engine's <c>roof_fade.fxgraph</c> beside its own graphs, an AlphaTest of the object uniform
/// <c>roof_fade</c> (internalonly.ffx's RoofFade) against a threshold of world-space noise (Noise31 of the place times 0.2, fitted to 0.01
/// to 0.99) - so a section dissolves through a speckle as its value falls. WHAT SETS THE VALUE is the processor's: the program names
/// <c>TileMeshInstance::UpdateHaveRoofFade</c>, <c>ClientTileMap::CalculateRoofFadeRanges</c> and <c>CalculateDisabledRoofFade</c>,
/// <c>RoofFadeMode</c> ("Unknown RoofFadeMode: "), <c>RoofFadeBlend</c>, <c>RoofFadeDistanceUpscreen</c> and <c>Downscreen</c>, and the
/// words <c>roof_fade_bounding_box</c>, <c>roof_fade_circle_up</c>, <c>_dn</c> and <c>_lr</c>, <c>roof_fade_cylinder</c>,
/// <c>roof_fade_with_parent_tile</c>, <c>no_roof_fade</c>, <c>enable_roof_fade</c>, <c>disable_automatic_roof_fade</c>,
/// <c>set_roof_fade_dynamic_parameter</c> and <c>HideRoof</c> (light-extras.txt section 4, v0.1.374) - the keys some file sets. Fifteen
/// environments set <c>area.roof_fade_circle_dn</c> or <c>_up</c>; which objects, tiles or rooms set the rest, and how, is what this
/// section reads - before the roof fade is given a rule.
/// </remarks>
public static partial class CaptureLightExtras
{
    /// <summary>The roof fade's words a file is found by, any case, ASCII or UTF-16 - the program's own spellings, a word each covering several.</summary>
    /// <remarks>
    /// NOT "rooffade" BARE: the roof sections' own textures are named RoofFade_JungleTreeBranches01_colour_BC1.dds and the like, so every file
    /// binding one would read as setting a roof fade - the program's three names that begin so are looked for whole instead.
    /// </remarks>
    public static IReadOnlyList<string> RoofFadeWords { get; } = ["roof_fade", "rooffademode", "rooffadeblend", "rooffadedistance", "hideroof", "hide_roof", "no_roof"];

    /// <summary>The most files whose lines the section lists.</summary>
    private const int MostRoofFiles = 600;

    /// <summary>The characters the files found may take written whole, and the largest one.</summary>
    private const int RoofWordChars = 1024 * 1024;

    private const int LargestRoofWord = 64 * 1024;

    /// <summary>
    /// Section 10: every file of the walks (the object scripts, ground and edge types, environments, masters' lists, effects, objects, tiles,
    /// rooms and masters) naming a roof fade word, each with its lines - the strings round the word in a binary file - by kind, the text ones
    /// whole while the section lasts. Returns how many were found and searched.
    /// </summary>
    private static (int Found, int Searched) RoofWords(Func<string, byte[]?> read, IReadOnlyList<string>? files, StringBuilder said)
    {
        string[] paths = [.. (files ?? []).Distinct(StringComparer.OrdinalIgnoreCase)];
        var clock = Stopwatch.StartNew();
        (IReadOnlyList<(string Path, IReadOnlyList<string> Lines, string? Text)> found, long bytes, int unread) =
            paths.Length == 0 ? ([], 0L, 0) : CaptureWaterShore.Find(read, paths, RoofFadeWords);
        clock.Stop();

        said.AppendLine().Append(CultureInfo.InvariantCulture,
            $"=== 10. the install's files naming the roof fade's words ({string.Join(", ", RoofFadeWords)}), any case, ASCII or UTF-16 - where an object, a tile or a room sets how its roof fades round the player (the program's own words: RoofFadeMode, roof_fade_bounding_box, roof_fade_circle_up/dn/lr, roof_fade_cylinder, roof_fade_with_parent_tile, no_roof_fade, enable_roof_fade, disable_automatic_roof_fade, set_roof_fade_dynamic_parameter, HideRoof): {found.Count} of {paths.Length} searched ({bytes:N0} bytes, {unread} not read) in {clock.Elapsed.TotalSeconds:0.0} s - each file's lines naming one, by kind, the text ones whole below while the section lasts")
            .AppendLine();
        if (paths.Length == 0)
        {
            said.AppendLine("(no files: the install's walk found none, or it had not run when the key went down)");
            return (0, 0);
        }

        foreach (IGrouping<string, (string Path, IReadOnlyList<string> Lines, string? Text)> kind in found
            .GroupBy(one => Path.GetExtension(one.Path).ToLowerInvariant())
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal))
        {
            said.Append(CultureInfo.InvariantCulture, $"  {(kind.Key.Length > 0 ? kind.Key : "(no extension)")}: {kind.Count()} files").AppendLine();
        }

        foreach ((string path, IReadOnlyList<string> lines, _) in found.Take(MostRoofFiles))
        {
            said.Append("  rooffile\t").Append(path).Append('\t').Append(lines.Count.ToString(CultureInfo.InvariantCulture)).Append('\t')
                .AppendLine(string.Join(" | ", lines.Select(line => Shorten(Compact(line), 240))));
        }

        if (found.Count > MostRoofFiles)
        {
            said.Append(CultureInfo.InvariantCulture, $"  ({found.Count - MostRoofFiles} more files not listed)").AppendLine();
        }

        Wholes([.. found.Where(one => one.Text is not null).Select(one => (one.Path, one.Text!, $"names {string.Join(", ", RoofFadeWords.Where(word => one.Text!.Contains(word, StringComparison.OrdinalIgnoreCase)))}"))],
            RoofWordChars, LargestRoofWord, "files", said);
        return (found.Count, paths.Length);
    }
}
