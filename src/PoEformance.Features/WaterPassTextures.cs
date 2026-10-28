using System.Globalization;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The two pass textures the engine's water reads and no file names - <c>caustics_tex_sampler</c> and <c>distortion_sampler</c> - looked
/// for among the install's textures: the candidates, each read and measured as the water's shaders read it.
/// </summary>
/// <remarks>
/// WRITTEN BEFORE THE REFRACTION'S CAUSTICS ARE BUILT (asked for from the live client, v0.1.331, with the rocky shore and the wet sand).
/// water.ffx's GetCausticsBias reads both: the distortion's red and green as a vector, <c>(rg * 2 - 1) * 0.4</c>, at the place times
/// 0.001 drifting with the clock, and the caustics' green, <c>pow(rgb, 1 / 4.2)</c> then <c>* 2 - 1</c>, at the place times 0.002 moved by
/// it - both through a wrapping sampler, so both must tile. The rocky shore (GetClashingWaterPlanarState) and the beach foam's leading
/// edge read the same distortion. The engine binds them itself, so which files they are is read off the files, as the swell's profile
/// was (WaterLookups): the game's program names <c>water_caustics_2.dds</c> under a waterfall's particles - and the source's own comment
/// over the caustics reads "for water_caustics_2.dds" - and <c>muddle_double.dds</c> among the particles' distortions, beside the
/// install's other caustics and flow textures. CHOSEN: the candidates (<see cref="Caustics"/>, <see cref="Distortions"/>, and the
/// program's names of the same words); what a distortion or a caustics texture should measure is said beside the numbers, not decided.
/// </remarks>
public static class WaterPassTextures
{
    /// <summary>The caustics' candidates: the program's own name first, the install's other caustics after.</summary>
    public static IReadOnlyList<string> Caustics { get; } =
    [
        "Art/particles/environment_effects/waterfalls/twilight_strand/water_caustics_2.dds",
        "Art/Textures/General/Water/water_caustics_2_bc1.dds",
        "Art/Textures/General/Water/water_caustics_2_dxt5.dds",
        "Art/particles/masters/ninjacop/ink_ripple/water_caustics_2.dds",
        "Art/particles/FXLib/tileables/noise_caustic_010_BC7.dds",
        "Art/particles/distortion/caustics.dds",
        "Art/particles/distortion/caustics_uncompressed.dds",
        "Art/particles/distortion/caustics_weak.dds",
    ];

    /// <summary>The distortion's candidates: the program's own name first, the install's other flow and noise textures after.</summary>
    public static IReadOnlyList<string> Distortions { get; } =
    [
        "Art/particles/distortion/muddle_double.dds",
        "Art/particles/distortion/flowmap_04.dds",
        "Art/particles/FXLib/tileables/noise_cell_painted_01_Uncompressed.dds",
        "Art/2DArt/Lookup/noise_curl3.dds",
        "Art/2DArt/Lookup/influence_noise.dds",
    ];

    /// <summary>The words a program's texture name is taken as a candidate by, beside the lists.</summary>
    private static readonly string[] Words = ["caustic", "distort", "muddle", "flowmap"];

    /// <summary>The largest candidate written whole beside the part.</summary>
    private const int LargestFile = 4 * 1024 * 1024;

    /// <summary>What a candidate is written whole as: <c>water-pass-N-name.dds</c>.</summary>
    public static string FileOf(int number, string path) =>
        string.Create(CultureInfo.InvariantCulture, $"water-pass-{number}-{Path.GetFileName(path).ToLowerInvariant()}");

    /// <summary>
    /// The part's section: every candidate, its header, and its measures as the caustics or the distortion; the files written whole
    /// added to <paramref name="files"/>. Returns the summary for the part's first line.
    /// </summary>
    internal static string Write(Func<string, byte[]?> read, IReadOnlyList<string> named, StringBuilder said, List<WaterLookupFile> files)
    {
        said.AppendLine().AppendLine("=== the pass textures the water reads and no file names: caustics_tex_sampler and distortion_sampler - the candidates, read as water.ffx reads them")
            .AppendLine("    (each channel lowest, tenth, middle, ninetieth, highest · mean, over the top level; the seam is the wrap's step across the edges over a step")
            .AppendLine("    between neighbours inside - near one where it tiles, as the wrapping sampler needs; each written whole beside this file as water-pass-N-name)");
        var candidates = new List<(string Path, bool Distortion)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Caustics)
        {
            if (seen.Add(path))
            {
                candidates.Add((path, false));
            }
        }

        foreach (string path in Distortions)
        {
            if (seen.Add(path))
            {
                candidates.Add((path, true));
            }
        }

        // THE PROGRAM'S OWN NAMES OF THE SAME WORDS - a texture the engine binds is named there if anywhere.
        foreach (string name in named)
        {
            string path = name.Trim();
            if (path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)
                && Words.Any(word => path.Contains(word, StringComparison.OrdinalIgnoreCase)) && seen.Add(path))
            {
                candidates.Add((path, !path.Contains("caustic", StringComparison.OrdinalIgnoreCase)));
            }
        }

        int found = 0, measured = 0;
        for (var at = 0; at < candidates.Count; at++)
        {
            (string path, bool distortion) = candidates[at];
            said.AppendLine().Append(distortion ? "distortion? " : "caustics? ").Append(path).Append(": ");
            byte[]? raw = GameArt.ReadRaw(read, path);
            if (raw is null)
            {
                said.AppendLine("not in the install");
                continue;
            }

            found++;
            said.AppendLine(ModelDump.Header(raw));
            if (GameArt.Decode(raw) is { } picture)
            {
                measured++;
                foreach (string line in Lines(picture, distortion))
                {
                    said.AppendLine(line);
                }
            }
            else
            {
                said.AppendLine("  not decoded");
            }

            // A LOOKUP IS WRITTEN WHOLE BY THE LOOKUPS' OWN SECTION, under its own name.
            if (raw.Length <= LargestFile && !path.Contains(CaptureWaterTextures.LookupFolder, StringComparison.OrdinalIgnoreCase))
            {
                files.Add(new WaterLookupFile(path, raw, FileOf(at + 1, path)));
                said.Append("  written whole: ").AppendLine(files[^1].File);
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{candidates.Count} pass-texture candidates, {found} in the install, {measured} measured");
    }

    /// <summary>
    /// A candidate's measures: each channel's spread and seam, the red and green's likeness, and what the water would read of it - as the
    /// distortion, <c>rg * 2 - 1</c>; as the caustics, the green <c>pow(g, 1 / 4.2) * 2 - 1</c>.
    /// </summary>
    public static IReadOnlyList<string> Lines(GamePicture picture, bool distortion)
    {
        int width = picture.Width, height = picture.Height;
        byte[] rgba = picture.Rgba;
        var lines = new List<string>(8) { Say($"  read as {width} x {height}") };
        if (width < 2 || height < 2 || rgba.Length < width * height * 4)
        {
            lines.Add("  too small to measure");
            return lines;
        }

        // ONE PASS FOR THE HISTOGRAMS AND THE SUMS, the percentiles off the histograms: a million texels cost a million steps.
        var histograms = new int[4 * 256];
        double sumR = 0, sumG = 0, sumRR = 0, sumGG = 0, sumRG = 0, vector = 0, vectorX = 0, vectorY = 0, bias = 0;
        Span<double> inside = stackalloc double[4];
        Span<double> seam = stackalloc double[4];
        var caustic = new double[256];
        for (var level = 0; level < 256; level++)
        {
            caustic[level] = (Math.Pow(level / 255.0, 1.0 / 4.2) * 2.0) - 1.0;
        }

        for (var y = 0; y < height; y++)
        {
            int row = y * width * 4;
            for (var x = 0; x < width; x++)
            {
                int at = row + (x * 4);
                for (var channel = 0; channel < 4; channel++)
                {
                    byte value = rgba[at + channel];
                    histograms[(channel * 256) + value]++;
                    int right = x + 1 < width ? at + 4 : row;
                    int down = y + 1 < height ? at + (width * 4) : x * 4;
                    double step = Math.Abs(value - rgba[right + channel]) + Math.Abs(value - rgba[down + channel]);
                    if (x + 1 < width && y + 1 < height)
                    {
                        inside[channel] += step;
                    }
                    else
                    {
                        seam[channel] += (x + 1 < width ? 0 : Math.Abs(value - rgba[right + channel])) + (y + 1 < height ? 0 : Math.Abs(value - rgba[down + channel]));
                    }
                }

                double r = rgba[at] / 255.0, g = rgba[at + 1] / 255.0;
                sumR += r;
                sumG += g;
                sumRR += r * r;
                sumGG += g * g;
                sumRG += r * g;
                double vx = (r * 2.0) - 1.0, vy = (g * 2.0) - 1.0;
                vectorX += vx;
                vectorY += vy;
                vector += Math.Sqrt((vx * vx) + (vy * vy));
                bias += caustic[rgba[at + 1]];
            }
        }

        double count = (double)width * height;
        double insideSteps = 2.0 * (width - 1) * (height - 1), seamSteps = width + height;
        for (var channel = 0; channel < 4; channel++)
        {
            ReadOnlySpan<int> histogram = histograms.AsSpan(channel * 256, 256);
            double mean = 0;
            for (var level = 0; level < 256; level++)
            {
                mean += level * (double)histogram[level];
            }

            double ratio = inside[channel] > 0 ? (seam[channel] / seamSteps) / (inside[channel] / insideSteps) : double.NaN;
            lines.Add(Say($"  {"rgba"[channel]} {Percentile(histogram, count, 0)}, {Percentile(histogram, count, 0.1)}, {Percentile(histogram, count, 0.5)}, {Percentile(histogram, count, 0.9)}, {Percentile(histogram, count, 1)} · mean {mean / count / 255.0:F3} · seam {ratio:F2}"));
        }

        double varianceR = (sumRR / count) - Math.Pow(sumR / count, 2), varianceG = (sumGG / count) - Math.Pow(sumG / count, 2);
        double covariance = (sumRG / count) - (sumR / count * (sumG / count));
        double correlation = varianceR > 1e-12 && varianceG > 1e-12 ? covariance / Math.Sqrt(varianceR * varianceG) : double.NaN;
        lines.Add(Say($"  red and green alike: correlation {correlation:F3} (one a single picture in both, as a greyscale texture)"));
        lines.Add(distortion
            ? Say($"  as the distortion (rg * 2 - 1): the mean vector ({vectorX / count:F3}, {vectorY / count:F3}), its mean length {vector / count:F3} - a vector field centred on nought, not one picture twice, tiling")
            : Say($"  as the caustics (pow(g, 1 / 4.2) * 2 - 1): the bias's mean {bias / count:F3} - bright lines over a darker floor, tiling"));
        return lines;
    }

    /// <summary>The level at a fraction of the texels, off a channel's histogram.</summary>
    private static int Percentile(ReadOnlySpan<int> histogram, double count, double fraction)
    {
        double wanted = Math.Max(1.0, fraction * count);
        double sum = 0;
        for (var level = 0; level < 256; level++)
        {
            sum += histogram[level];
            if (sum >= wanted)
            {
                return level;
            }
        }

        return 255;
    }

    private static string Say(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);
}
