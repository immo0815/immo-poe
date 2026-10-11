using System.Globalization;
using System.Text;
using System.Text.Json;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The capture's water part: the keys every environment's water sections carry, with how many files and what values, and the
/// install's shader sources that draw the water, whole.
/// </summary>
/// <remarks>
/// WRITTEN FOR PORT'S WATER, white in the Monster Book's stage (v0.1.204): its material is no ordinary one. It says
/// <c>"shadertype":"Ocean"</c> and <c>"splashywater":true</c>, and its graph, nvidian_Ocean.fxgraph, overrides the blend to
/// BackgroundGBufferSurfaces and writes as albedo nothing but a constant, (0.53, 0, 0, 0.608) off the minimap - a mark in
/// the frame's buffers, not a colour. The water's look is the engine's water pass (common.ffx: PASS_TYPE_WATER and its
/// water_color_terrain, water_color_open, water_clarity ... pass uniforms; WaterComputeSurface, WaterComputeRefraction),
/// while Port.env's water section names only flow_intensity, flow_turbulence, flow_resolution, flow_wait_init and
/// height_offset. So two things are read here before anything is drawn: which water keys any environment writes at all,
/// and the sources the water pass is made of, the functions its fragments call (InitWaterProps, BuildWaterRefraction,
/// GetTotalWaveSurfaceNumerical) included.
/// </remarks>
public static class CaptureWater
{
    /// <summary>The words a water source is found by: the pass, its properties, the functions its fragments call, its colour uniforms.</summary>
    public static IReadOnlyList<string> Words { get; } =
    [
        "PASS_TYPE_WATER", "WaterProps", "InitWaterProps", "BuildWaterRefraction", "BuildWaterWetness",
        "GetTotalWaveSurfaceNumerical", "GetFlowingSurface", "water_color_open", "water_color_terrain", "water_clarity",
    ];

    /// <summary>Most sources written whole.</summary>
    private const int MostShaders = 24;

    /// <summary>Distinct example values kept per key.</summary>
    private const int MostExamples = 6;

    /// <summary>Longest an example is printed.</summary>
    private const int MostExampleChars = 96;

    /// <summary>Files a key's examples name, at most.</summary>
    private const int MostNamed = 3;

    private static readonly JsonDocumentOptions Lenient = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>The part's text: a line saying what it holds, the water keys of every environment, then the sources. Never throws.</summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="environments">Every .env in the install, as its walk found them.</param>
    /// <param name="shaders">Every shader source in the install, as its walk found them.</param>
    public static string Write(Func<string, byte[]?> read, IReadOnlyList<string>? environments, IReadOnlyList<string>? shaders)
    {
        ArgumentNullException.ThrowIfNull(read);
        var body = new StringBuilder();
        string keys = Keys(read, environments, body);
        int found = CaptureSources.Naming(read, shaders, Words, MostShaders, body);
        string sources = found < 0 ? "no shader sources to search" : string.Create(CultureInfo.InvariantCulture, $"{found} of {shaders!.Count} shader sources draw the water");
        return keys + "; " + sources + Environment.NewLine + body;
    }

    /// <summary>Every key of every section whose name says water, over every environment: how many files, and a few values with a file each.</summary>
    private static string Keys(Func<string, byte[]?> read, IReadOnlyList<string>? environments, StringBuilder said)
    {
        said.AppendLine().AppendLine("=== the water sections of the install's environments: each key, how many files carry it, and its values");
        if (environments is not { Count: > 0 })
        {
            said.AppendLine("(no environments: the install walk found none, or it had not run when the key went down)");
            return "no environments to read";
        }

        var keys = new SortedDictionary<string, Key>(StringComparer.Ordinal);
        int carrying = 0, failed = 0;
        foreach (string path in environments)
        {
            if (read(path.Replace('\\', '/').Trim()) is not { Length: > 0 } bytes)
            {
                continue;
            }

            try
            {
                using var document = JsonDocument.Parse(StatDescriptionFiles.Decode(bytes), Lenient);
                if (document.RootElement.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var any = false;
                foreach (JsonProperty section in document.RootElement.EnumerateObject())
                {
                    if (section.Value.ValueKind != JsonValueKind.Object || !section.Name.Contains("water", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    any = true;
                    foreach (JsonProperty entry in section.Value.EnumerateObject())
                    {
                        string name = section.Name + "." + entry.Name;
                        if (!keys.TryGetValue(name, out Key? key))
                        {
                            key = new Key();
                            keys[name] = key;
                        }

                        key.Add(entry.Value.GetRawText(), path);
                    }
                }

                carrying += any ? 1 : 0;
            }
            catch (JsonException)
            {
                failed++;
            }
        }

        said.Append(CultureInfo.InvariantCulture, $"{carrying} of {environments.Count} environments carry a water section, {keys.Count} keys{(failed > 0 ? $", {failed} did not read as JSON" : string.Empty)}").AppendLine();
        foreach ((string name, Key key) in keys)
        {
            said.Append(CultureInfo.InvariantCulture, $"  {name}  in {key.Files} files: ").AppendLine(string.Join("  |  ", key.Examples));
        }

        return string.Create(CultureInfo.InvariantCulture, $"{carrying} of {environments.Count} environments carry a water section, {keys.Count} keys");
    }

    /// <summary>One key: in how many files, and its first distinct values, each with the first file that wrote it.</summary>
    private sealed class Key
    {
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);

        public int Files { get; private set; }

        public List<string> Examples { get; } = [];

        public void Add(string value, string path)
        {
            Files++;
            string compact = string.Join(' ', value.Split((char[])['\n', '\r', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
            if (compact.Length > MostExampleChars)
            {
                compact = compact[..MostExampleChars] + "...";
            }

            if (Examples.Count < MostExamples && _seen.Add(compact))
            {
                Examples.Add(Examples.Count < MostNamed ? $"{compact} ({path})" : compact);
            }
        }
    }
}
