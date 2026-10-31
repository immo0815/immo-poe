using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The light extras' last three sections: the object the game attaches to the player for the area's weather, the fragments the
/// screen-space fog's graph reads that the sources above do not hold, and the engine's noise texture's candidates whole.
/// </summary>
/// <remarks>
/// WRITTEN FOR THE SCREEN-SPACE FOG AND THE CLOUDS (v0.1.380). The fog is a material - <c>screenspacefogenv.fxgraph</c>, the env's
/// <c>ssf_*</c> handed in by SSFColor and its kin, ScreenspaceFog marching layers over the terrain to the depth behind each pixel - and
/// nothing captured says who wears it: no room, tile or doodad of The Red Vale names it, the program names no such file, and the area's
/// .env names <c>area.player_environment_ao</c> (Red_Vale.ao), a weather attachment the game puts on the player, whose files no capture held.
/// The clouds (shadows.ffx's GetCloudsIntensity) and the fog's flow read <c>noise_sampler</c>, a pass texture the engine binds and no file
/// names; the program names four noises in Art/2DArt/Lookup/, written whole here so light-against-game's clouds can be held against
/// each (see <see cref="CloudNoise"/>).
/// </remarks>
public static partial class CaptureLightExtras
{
    /// <summary>
    /// The engine's noise texture's candidates: the four noises with an alpha the program names in Art/2DArt/Lookup/, and the two the clouds
    /// went best with offline (v0.1.381's capture of The Red Vale) - the one drawn, <see cref="AreaClouds.NoisePath"/> (CHOSEN), and the next.
    /// </summary>
    /// <remarks>
    /// influence_noise.dds is named beside them but is R8G8: its alpha reads one, and the clouds read the noise's alpha - so it is left out.
    /// THE NOISE DRAWN IS MEASURED AGAIN in every capture, against the four the program names, so the choice is held against each new area.
    /// </remarks>
    public static IReadOnlyList<string> NoiseCandidates { get; } =
    [
        "Art/2DArt/Lookup/perlin_noise.dds", "Art/2DArt/Lookup/noise_perlin_worley_high.dds", "Art/2DArt/Lookup/noise_curl3.dds",
        "Art/2DArt/Lookup/noise_worley_low.dds", AreaClouds.NoisePath, "Art/particles/distortion/flowmap_04.dds",
    ];

    /// <summary>The fog's layer texture as screenspacefogenv.fxgraph names it - its custom parameter "1.Fog_Texture" may be set otherwise by a material.</summary>
    public const string FogTexture = "Art/Textures/Environment/Leagues/Affliction/fog/BakedFog_BC1.dds";

    /// <summary>The fragments and functions the fog's graph and its march read that the sources above did not hold whole in The Red Vale's v0.1.373 capture.</summary>
    public static IReadOnlyList<string> FogFragments { get; } = ["WindVector", "PlayerTrailingGhost", "GetBurnSample", "WindParams", "UnprojectPixelDepth"];

    /// <summary>The most files the attachment's walk reads, and the most hops it goes from the attachment.</summary>
    private const int MostAttachmentFiles = 120;

    private const int MostHops = 4;

    /// <summary>The characters the attachment's files may take written whole, and the largest one.</summary>
    private const int AttachmentChars = 1024 * 1024;

    private const int LargestAttachment = 256 * 1024;

    /// <summary>The most lines one fragment or function is written with.</summary>
    private const int MostFragmentLines = 120;

    /// <summary>The file a texture written whole beside the part goes to.</summary>
    public static string WholeFile(string path) => "light-extras-" + Path.GetFileName(path).ToLowerInvariant();

    /// <summary>
    /// Section 7: the area's .env's <c>area.player_environment_ao</c>, its files hop by hop - what each names, the text ones whole - and the
    /// model the tool builds of it: its box and each shape's material. Returns how many files were read.
    /// </summary>
    private static int Attachment(Func<string, byte[]?> read, string? area, StringBuilder said)
    {
        said.AppendLine().AppendLine("=== 7. the player's environment attachment: the object the area's .env names as area.player_environment_ao, which the game attaches to the player - its files hop by hop from it, each with what named it, the text ones whole below; and the model the tool builds of it");
        string? start = area is null || read(Clean(area)) is not { Length: > 0 } env ? null : AttachmentOf(env);
        if (start is null)
        {
            said.AppendLine(area is null ? "(no area environment)" : "(the area's .env names no player_environment_ao: " + area + ")");
            return 0;
        }

        said.Append("  attachment\t").Append(start).Append('\t').AppendLine(area);
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Hop, string By)>();
        void Offer(string path, int hop, string by)
        {
            if (seen.Count < MostAttachmentFiles * 4 && seen.Add(path))
            {
                queue.Enqueue((path, hop, by));
            }
        }

        Offer(start, 0, area!);
        if (start.EndsWith(".ao", StringComparison.OrdinalIgnoreCase))
        {
            Offer(start + "c", 0, start);
        }

        var texts = new List<(string Path, string Text, string Why)>();
        int taken = 0;
        while (queue.Count > 0 && taken < MostAttachmentFiles)
        {
            (string path, int hop, string by) = queue.Dequeue();
            byte[]? bytes = read(path);
            taken++;
            if (bytes is not { Length: > 0 })
            {
                said.Append(CultureInfo.InvariantCulture, $"  attach\t{path}\tnot in the install\t{hop}\t{by}").AppendLine();
                continue;
            }

            bool text = TextKinds.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
            string kind = text ? "text" : path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? ModelDump.Header(bytes) : "binary";
            said.Append(CultureInfo.InvariantCulture, $"  attach\t{path}\t{bytes.Length}\t{hop}\t{by}\t{kind}").AppendLine();
            if (!text)
            {
                continue;
            }

            string decoded = StatDescriptionFiles.Decode(bytes);
            texts.Add((path, decoded, $"hop {hop}, named by {by}"));
            if (hop >= MostHops)
            {
                continue;
            }

            foreach (string named in NamedIn(decoded))
            {
                Offer(named, hop + 1, path);
                if (named.EndsWith(".ao", StringComparison.OrdinalIgnoreCase))
                {
                    Offer(named + "c", hop + 1, path);
                }
            }
        }

        if (queue.Count > 0)
        {
            said.Append(CultureInfo.InvariantCulture, $"  (cut: {queue.Count} more files named past the {MostAttachmentFiles} the walk reads)").AppendLine();
        }

        // THE MODEL AS THE TOOL BUILDS A DOODAD: its box says how far round the player the fog's mesh reaches, its shapes what they wear.
        try
        {
            MonsterModel model = MonsterModels.OfFiles(read, [start]);
            if (!model.Ready)
            {
                said.Append("  model\tnot built\t").AppendLine(model.Why);
            }
            else
            {
                System.Numerics.Vector3 least = model.Mesh.Least, most = model.Mesh.Most;
                said.Append(CultureInfo.InvariantCulture, $"  model\tbuilt\t{model.Mesh.Shapes.Count} shapes, {model.Mesh.Triangles} triangles, box ({least.X:0.#}, {least.Y:0.#}, {least.Z:0.#}) to ({most.X:0.#}, {most.Y:0.#}, {most.Z:0.#})").AppendLine();
                for (var shape = 0; shape < model.Mesh.Shapes.Count; shape++)
                {
                    string material = shape < model.ShapeMaterials.Count ? model.ShapeMaterials[shape] : string.Empty;
                    string mode = shape < model.Modes.Count ? model.Modes[shape] : string.Empty;
                    said.Append(CultureInfo.InvariantCulture, $"  shape\t{shape}\t{model.Mesh.Shapes[shape].Name}\t{model.Mesh.Shapes[shape].Count / 3} triangles\t{material}\t{mode}").AppendLine();
                }

                // THE SHAPES' OWN MATERIALS WHOLE, with what they name (v0.1.383): the mesh is drawn with them as well as with its render pass,
                // whose type is "Append" - The Red Vale's screenSpaceGeoShape wears SSTexturec.mat, which the walk above never reaches, the
                // .fmt naming it in its bytes. The fog is drawn from the pass alone until this says what the mesh's own material is.
                Shapes(read, model.ShapeMaterials, seen, texts, said);
            }
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or StackOverflowException))
        {
            said.Append("  model\tfailed\t").AppendLine(exception.Message);
        }

        _ = Wholes(texts, AttachmentChars, LargestAttachment, "attachment files", said);
        return taken;
    }

    /// <summary>
    /// The model's shape materials not walked yet, each read and written whole with the graphs and materials it names (a textures' header
    /// each) - two hops at most.
    /// </summary>
    private static void Shapes(
        Func<string, byte[]?> read, IReadOnlyList<string> materials, HashSet<string> seen, List<(string Path, string Text, string Why)> texts, StringBuilder said)
    {
        var queue = new Queue<(string Path, int Hop, string By)>();
        foreach (string material in materials.Where(one => one.Length > 0).Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (seen.Add(material))
            {
                queue.Enqueue((material, 0, "a shape of the model"));
            }
        }

        while (queue.Count > 0)
        {
            (string path, int hop, string by) = queue.Dequeue();
            byte[]? bytes = read(path);
            if (bytes is not { Length: > 0 })
            {
                said.Append(CultureInfo.InvariantCulture, $"  shapefile\t{path}\tnot in the install\t{hop}\t{by}").AppendLine();
                continue;
            }

            bool text = TextKinds.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
            string kind = text ? "text" : path.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) ? ModelDump.Header(bytes) : "binary";
            said.Append(CultureInfo.InvariantCulture, $"  shapefile\t{path}\t{bytes.Length}\t{hop}\t{by}\t{kind}").AppendLine();
            if (!text)
            {
                continue;
            }

            string decoded = StatDescriptionFiles.Decode(bytes);
            texts.Add((path, decoded, $"a shape's material of the model, hop {hop}, named by {by}"));
            if (hop >= 2)
            {
                continue;
            }

            foreach (string named in NamedIn(decoded).Where(one => one.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase)
                || one.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || one.EndsWith(".dds", StringComparison.OrdinalIgnoreCase)))
            {
                if (seen.Add(named))
                {
                    queue.Enqueue((named, hop + 1, path));
                }
            }
        }
    }

    /// <summary>The text kinds of the attachment's walk: written whole and read for what they name.</summary>
    private static readonly string[] TextKinds = [".ao", ".aoc", ".ot", ".otc", ".pet", ".epk", ".trl", ".mat", ".fxgraph", ".sm", ".atlas", ".amd"];

    /// <summary>The attachment an environment's JSON names, any section - or null.</summary>
    internal static string? AttachmentOf(byte[] env)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(StatDescriptionFiles.Decode(env), Lenient);
            return Find(document.RootElement);
        }
        catch (JsonException)
        {
            return null;
        }

        static string? Find(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    if (property.Name.Equals("player_environment_ao", StringComparison.OrdinalIgnoreCase) && property.Value.ValueKind == JsonValueKind.String
                        && property.Value.GetString() is { Length: > 0 } path)
                    {
                        return Clean(path);
                    }

                    if (Find(property.Value) is { } inner)
                    {
                        return inner;
                    }
                }
            }

            return null;
        }
    }

    /// <summary>The files a text names - by extension, and an <c>extends</c> line's path, which names no extension, as an .ao.</summary>
    internal static IEnumerable<string> NamedIn(string text)
    {
        var met = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match match in AttachedPath().Matches(text))
        {
            string path = Clean(match.Value);
            if (met.Add(path))
            {
                yield return path;
            }
        }

        foreach (Match match in Extends().Matches(text))
        {
            string path = Clean(match.Groups[1].Value);
            if (path.Length > 0 && !Path.HasExtension(path) && met.Add(path + ".ao"))
            {
                yield return path + ".ao";
            }
        }
    }

    /// <summary>
    /// Section 8: each fragment or function of <see cref="FogFragments"/> as the install's sources define it - the first source defining it,
    /// its lines to the fragment's end - or that none does. Returns how many were found.
    /// </summary>
    private static int Fragments(Func<string, byte[]?> read, IReadOnlyList<string>? shaders, StringBuilder said)
    {
        said.AppendLine().AppendLine("=== 8. the fragments the fog's graph and its march read: each of " + string.Join(", ", FogFragments) + " as the first source defining it writes it, a FRAGMENT to its closing }} and a function to its closing brace");
        if (shaders is not { Count: > 0 })
        {
            said.AppendLine("(no shader sources)");
            return 0;
        }

        var found = new Dictionary<string, (string Path, string Block)>(StringComparer.Ordinal);
        foreach (string path in shaders.Select(Clean).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase))
        {
            if (found.Count == FogFragments.Count || read(path) is not { Length: > 0 } bytes)
            {
                continue;
            }

            string[] lines = StatDescriptionFiles.Decode(bytes).Split('\n');
            foreach (string word in FogFragments)
            {
                if (!found.ContainsKey(word) && Block(lines, word) is { } block)
                {
                    found[word] = (path, block);
                }
            }
        }

        var texts = new List<(string Path, string Text, string Why)>();
        foreach (string word in FogFragments)
        {
            if (found.TryGetValue(word, out (string Path, string Block) one))
            {
                said.Append("  fragment\t").Append(word).Append('\t').AppendLine(one.Path);
                texts.Add((one.Path + "#" + word, one.Block, "defines " + word));
            }
            else
            {
                said.Append(CultureInfo.InvariantCulture, $"  fragment\t{word}\t-\tdefined in none of {shaders.Count} sources").AppendLine();
            }
        }

        _ = Wholes(texts, AttachmentChars, LargestAttachment, "fragments", said);
        return found.Count;
    }

    /// <summary>A word's FRAGMENT to its closing <c>}}</c>, or a function of that name to its closing brace - or null where neither stands in the lines.</summary>
    internal static string? Block(string[] lines, string word)
    {
        for (var at = 0; at < lines.Length; at++)
        {
            // THE WORD FIRST, which nearly every line fails - the pattern only where it stands.
            if (!lines[at].Contains(word, StringComparison.Ordinal))
            {
                continue;
            }

            string line = lines[at].Trim();
            bool fragment = line.Equals("FRAGMENT " + word, StringComparison.Ordinal);
            bool function = !fragment && FunctionOf(word).IsMatch(line);
            if (!fragment && !function)
            {
                continue;
            }

            var block = new StringBuilder();
            int depth = 0;
            bool opened = false;
            for (int from = at; from < lines.Length && from - at < MostFragmentLines; from++)
            {
                string text = lines[from].TrimEnd('\r');
                block.AppendLine(text);
                if (fragment)
                {
                    if (text.Trim() == "}}")
                    {
                        return block.ToString();
                    }

                    continue;
                }

                foreach (char c in text)
                {
                    depth += c == '{' ? 1 : c == '}' ? -1 : 0;
                    opened |= c == '{';
                }

                if (opened && depth <= 0)
                {
                    return block.ToString();
                }
            }

            return block.ToString();
        }

        return null;
    }

    /// <summary>
    /// Section 9: the noise candidates and the fog's texture - each one's header, and its bytes handed back to be written whole beside the
    /// part. Returns how many were in the install.
    /// </summary>
    private static int Noises(Func<string, byte[]?> read, StringBuilder said, List<(string File, byte[] Bytes)> files)
    {
        said.AppendLine().AppendLine("=== 9. the engine's noise texture's candidates (noise_sampler: a pass texture no file names - the clouds read its alpha, the fog its flow) and the fog's own texture, each written whole beside the part");
        int found = 0;
        foreach (string path in NoiseCandidates.Append(FogTexture))
        {
            byte[]? bytes = read(path);
            if (bytes is not { Length: > 0 })
            {
                said.Append("  noise\t").Append(path).AppendLine("\tnot in the install");
                continue;
            }

            found++;
            files.Add((WholeFile(path), bytes));
            said.Append(CultureInfo.InvariantCulture, $"  noise\t{path}\t{bytes.Length}\t{ModelDump.Header(bytes)}\t{WholeFile(path)}").AppendLine();
        }

        return found;
    }

    /// <summary>A path ending in one of the extensions the attachment's files name each other by.</summary>
    [GeneratedRegex(@"[A-Za-z0-9_\-/\\.]+\.(?:aoc|ao|otc|ot|pet|epk|trl|mat|fxgraph|smd|sm|fmt|dds|png|ast|amd|atlas)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AttachedPath();

    /// <summary>An <c>extends "path"</c> line.</summary>
    [GeneratedRegex(@"\bextends\s+""([^""]+)""", RegexOptions.CultureInvariant)]
    private static partial Regex Extends();

    /// <summary>A function definition of the word: a return type, the name and its parenthesis - one pattern a word, made once.</summary>
    private static Regex FunctionOf(string word)
        => Functions.GetOrAdd(word, one => new Regex(
            @"^(?:inline\s+)?(?:float[234]?(?:x[234])?|void|bool|int[234]?|uint[234]?|half[234]?)\s+" + Regex.Escape(one) + @"\s*\(", RegexOptions.CultureInvariant));

    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, Regex> Functions = new(StringComparer.Ordinal);
}
