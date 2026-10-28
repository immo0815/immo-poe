using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The engine's beach waves in the water textures' part: the graphs the game's program names for them, each whole, and every graph
/// and texture those name - followed through - each whole or by its header.
/// </summary>
/// <remarks>
/// WRITTEN WHEN PORT'S ROCKS STOOD DRY (asked for from the live client, v0.1.238): in the game a thick sheet of white foam runs in
/// over the rocks by the pier in surges and floods them up to the sand, while the Monster Book's water ends at the planes' line. That
/// is not the ocean planes' pass: water.ffx draws three kinds of wave that move their own vertices over the shore -
/// BreakingWavesVertex and FloodWaveVertex, both <c>engineonly</c>, and CrashingWavesVertex - over meshes the engine makes itself,
/// reading textures it binds itself (<c>wave_shape_precomp_tex</c>, <c>wave_coord_noise_tex</c>, <c>foam_color_tex</c> and more).
/// No file read so far says which: Port's planes name none, and its material only sets <c>"splashywater":true</c>. The program names
/// the graphs that draw them, BreakingOceanWaves.fxgraph and BreakingOceanWavesWetness.fxgraph, and a debug graph for the flood
/// waves' markup - so they are written whole, with everything they name, before the waves are built from them.
/// </remarks>
internal static partial class CaptureWaves
{
    /// <summary>The graphs the program was found naming (v0.1.236's water-textures.txt, Port), taken whether or not this program names them again.</summary>
    /// <remarks>
    /// AND THE WATER'S OWN, from v0.1.336: the graphs the engine draws a water and casts its wetness with (water_default.fxgraph,
    /// water_default_wetness.fxgraph and their flowing kin) - named by the program (v0.1.335's water-textures.txt), but by no word the
    /// filter below takes, and past the debug part's 256 written; the wetness pass casts the water's by WaterComputeWetness, the only
    /// fragment working a water's wetness out, until the graph says how.
    /// </remarks>
    public static IReadOnlyList<string> Known { get; } =
    [
        "Metadata/Effects/Graphs/Environment/BreakingOceanWaves.fxgraph",
        "Metadata/Effects/Graphs/Environment/BreakingOceanWavesWetness.fxgraph",
        "Metadata/EngineGraphs/debug_flood_wave_markup.fxgraph",
        "Metadata/EngineGraphs/water_default.fxgraph",
        "Metadata/EngineGraphs/water_default_wetness.fxgraph",
        "Metadata/EngineGraphs/water_flowing.fxgraph",
        "Metadata/EngineGraphs/water_flowing_wetness.fxgraph",
    ];

    /// <summary>The words a graph the program names is taken by, besides those above.</summary>
    private static readonly string[] Wavy = ["wave", "flood", "beach", "shore", "breaking", "splash", "foam", "ocean"];

    /// <summary>The folders' words a texture of the walk is taken by: the beach waves' own folders.</summary>
    private static readonly string[] Folders = ["breakingwave", "floodwave", "beachwave", "crashingwave"];

    /// <summary>Most graphs written whole, those named and those they name.</summary>
    private const int MostGraphs = 48;

    /// <summary>Most textures read for their header.</summary>
    private const int MostTextures = 96;

    /// <summary>The section: every graph whole under a <c>##### path  (why)</c> heading - the heading CaptureReplayTests reads files back by - then the textures. Returns a line saying what it found.</summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="textures">Every texture whose path names the water, as the install's walk found them.</param>
    /// <param name="named">The strings the game's program holds that name the water.</param>
    /// <param name="said">Where it is written.</param>
    public static string Write(Func<string, byte[]?> read, IReadOnlyList<string>? textures, IReadOnlyList<string> named, StringBuilder said)
    {
        said.AppendLine().AppendLine("=== the engine's beach waves: the graphs the program names for them (BreakingOceanWaves and its kin), each whole, then every graph they name")
            .AppendLine("    (water.ffx's BreakingWaves, CrashingWaves and FloodWave move their own vertices over the shore, on meshes and textures the engine binds itself)");
        var queue = new Queue<(string Path, string Why)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Offer(string path, string why)
        {
            string clean = Clean(path);
            if (clean.Length > 0 && seen.Add(clean))
            {
                queue.Enqueue((clean, why));
            }
        }

        foreach (string one in Known)
        {
            Offer(one, "named by the game's program");
        }

        foreach (string one in named)
        {
            string trimmed = one.Trim();
            if (trimmed.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase) && Wavy.Any(word => trimmed.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                Offer(trimmed, "named by the game's program");
            }
        }

        var pictures = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var others = new SortedDictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        int graphs = 0, found = 0;
        while (queue.Count > 0 && graphs < MostGraphs)
        {
            (string path, string why) = queue.Dequeue();
            graphs++;
            said.AppendLine().Append("##### ").Append(path).Append("  (").Append(why).AppendLine(")");
            if (read(path) is not { Length: > 0 } bytes)
            {
                said.AppendLine("(not in the install)");
                continue;
            }

            found++;
            string text = StatDescriptionFiles.Decode(bytes).TrimEnd();
            said.AppendLine(text);
            foreach (string file in Files(text))
            {
                if (file.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase))
                {
                    Offer(file, "named by " + path);
                }
                else if (Picture(file))
                {
                    pictures.TryAdd(file, path);
                }
                else
                {
                    others.TryAdd(file, path);
                }
            }
        }

        if (queue.Count > 0)
        {
            said.AppendLine().Append(CultureInfo.InvariantCulture, $"({queue.Count} more graphs named, past the {MostGraphs} written: ")
                .Append(string.Join(", ", queue.Select(one => one.Path))).AppendLine(")");
        }

        foreach (string one in textures ?? [])
        {
            if (Folders.Any(word => one.Contains(word, StringComparison.OrdinalIgnoreCase)))
            {
                pictures.TryAdd(Clean(one), "the install's walk");
            }
        }

        said.AppendLine().AppendLine("=== the textures those graphs name, and the walk's in a beach-wave folder - each one's header, and who names it");
        foreach ((string path, string by) in pictures.Take(MostTextures))
        {
            said.Append("  ").Append(path).Append("  (").Append(by).Append("): ").AppendLine(ModelDump.Header(GameArt.ReadRaw(read, path)));
        }

        if (pictures.Count > MostTextures)
        {
            said.Append(CultureInfo.InvariantCulture, $"  ... and {pictures.Count - MostTextures} more").AppendLine();
        }

        if (others.Count > 0)
        {
            said.AppendLine().AppendLine("=== the other files those graphs name - whether the install holds each");
            foreach ((string path, string by) in others)
            {
                said.Append("  ").Append(path).Append("  (").Append(by).Append("): ")
                    .AppendLine(read(path) is { Length: > 0 } held ? string.Create(CultureInfo.InvariantCulture, $"{held.Length:N0} bytes") : "not in the install");
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"beach waves: {found} of {graphs} graphs read, {pictures.Count} textures named");
    }

    /// <summary>Every path a graph's text names, by its extension, once each in the order met.</summary>
    internal static IEnumerable<string> Files(string text)
    {
        var met = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in FilePath().Matches(text))
        {
            string path = Clean(match.Value);
            if (met.Add(path))
            {
                yield return path;
            }
        }
    }

    private static bool Picture(string path) =>
        path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".tga", StringComparison.OrdinalIgnoreCase);

    private static string Clean(string path) => path.Replace('\\', '/').Trim();

    /// <summary>A path ending in one of the extensions a graph names files by.</summary>
    [GeneratedRegex(@"[A-Za-z0-9_\-/\\.]+\.(?:fxgraph|dds|png|tga|mat|ffx|aoc|ao|fx)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex FilePath();
}
