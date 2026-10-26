using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The capture's wobble files part: what the install says of the engine's wobble - the object types of the wobble points and the
/// doodads they may sway, followed through what they extend; every path naming a wobble; the shader sources and the program's
/// strings naming one.
/// </summary>
/// <remarks>
/// WRITTEN FOR PORT'S SHIP, which sways in the game while its .ao is a FixedMesh with no rig and no animation (v0.1.289's capture).
/// The room lays a two-triangle doodad beside it, <c>BoatnautWobbleCore_small.ao</c>, of the entity type
/// <c>Metadata/Terrain/Gallows/Act4/4_6/Objects/BoatWobbleCore</c>, whose entity carries a WobblePoint component; and the room's
/// doodads are of the type <c>Doodad</c> or <c>DoodadNoWobble</c>. No reference names any of it (GameHelper2, dat-schema and
/// poe_data_tools were searched), so the game's own files are read: an object type's <c>.ot</c> and <c>.otc</c> say which
/// components it carries and with what numbers, and the memory half (<c>wobble.txt</c>, WobbleProbe) says what the component does
/// with them over time.
/// </remarks>
public static class CaptureWobble
{
    /// <summary>The word a path, a source or a string is found by.</summary>
    public static IReadOnlyList<string> Words { get; } = ["wobble"];

    /// <summary>The folders the install's walk keeps a path naming a wobble from - any extension.</summary>
    public static IReadOnlyList<string> Folders { get; } = ["Metadata/", "Data/", "Art/"];

    /// <summary>The object types read whatever the loaded list says: the doodad that may sway and the one that may not.</summary>
    public static IReadOnlyList<string> Types { get; } =
        ["Metadata/MiscellaneousObjects/Doodad", "Metadata/MiscellaneousObjects/DoodadNoWobble"];

    /// <summary>The files an object type is made of, tried for every type.</summary>
    private static readonly string[] Kinds = [".ot", ".otc", ".ao", ".aoc"];

    /// <summary>Most text files a walk found written whole.</summary>
    private const int MostWhole = 40;

    /// <summary>Largest a found file is written whole.</summary>
    private const int LargestWhole = 64 * 1024;

    /// <summary>Most sources written whole.</summary>
    private const int MostShaders = 8;

    /// <summary>Most strings written a program.</summary>
    private const int MostStrings = 400;

    private static readonly byte[][] WordBytes = [.. Words.Select(word => Encoding.ASCII.GetBytes(word.ToLowerInvariant()))];

    /// <summary>The part's text: a line saying what it holds, then the types, the paths, the sources and the strings. Never throws on a missing file.</summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="loaded">The files the area loaded - the wobble points' types are among them.</param>
    /// <param name="found">Every install path naming a wobble, as the walk found them.</param>
    /// <param name="shaders">Every shader source in the install, as its walk found them.</param>
    /// <param name="programs">The game's program files, read from disk.</param>
    public static string Write(
        Func<string, byte[]?> read, IReadOnlyList<string>? loaded, IReadOnlyList<string>? found, IReadOnlyList<string>? shaders, IReadOnlyList<string>? programs)
    {
        ArgumentNullException.ThrowIfNull(read);
        var body = new StringBuilder();

        // THE TYPES: every loaded path naming a wobble, and the two doodad types, each kind of file followed through what it extends.
        string[] types = [.. (loaded ?? []).Where(Wobbly).Select(Base).Concat(Types).Concat((found ?? []).Where(Typed).Select(Base))
            .Distinct(StringComparer.OrdinalIgnoreCase)];
        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the object types: {types.Length} - every loaded path naming a wobble, the two doodad types, and every object file the walk found naming one; each kind ({string.Join(", ", Kinds)}) followed through what it extends").AppendLine();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        foreach (string type in types)
        {
            foreach (string kind in Kinds)
            {
                files += Chain(read, type + kind, kind, written, body, depth: 0);
            }
        }

        // THE PATHS: every one the walk found, the text files among them whole.
        string[] paths = [.. (found ?? []).Order(StringComparer.OrdinalIgnoreCase)];
        body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== every install path naming a wobble under {string.Join(", ", Folders)}: {paths.Length}").AppendLine();
        foreach (string path in paths)
        {
            body.Append("  ").AppendLine(path);
        }

        int whole = 0;
        foreach (string path in paths.Where(path => !written.Contains(path) && Text(path)))
        {
            if (whole >= MostWhole)
            {
                body.Append(CultureInfo.InvariantCulture, $"(more text files not written whole - {MostWhole} were)").AppendLine();
                break;
            }

            if (read(path) is { Length: > 0 and <= LargestWhole } bytes)
            {
                Whole(path, bytes, body);
                written.Add(path);
                whole++;
            }
        }

        int played = Played(read, types, body);
        Swaying(read, loaded, body);

        int sources = CaptureSources.Naming(read, shaders, Words, MostShaders, body);
        string strings = Programs(programs, body);
        string head = string.Create(
            CultureInfo.InvariantCulture,
            $"wobble files: {types.Length} object types, {files} of their files read, {played} wobble cores played; {paths.Length} install paths name a wobble, {whole} written whole; {(sources < 0 ? "no shader sources" : $"{sources} shader sources name one")}; {strings}");
        return head + Environment.NewLine + body;
    }

    /// <summary>
    /// Every type with a WobblePoint played as the tool plays it (WobbleRig): what it read, and the bone's matrix over one loop as a
    /// table in the watch's own columns - so <c>wobble-replay.txt</c> holds the tool's sway beside the game's.
    /// </summary>
    private static int Played(Func<string, byte[]?> read, IReadOnlyList<string> types, StringBuilder body)
    {
        body.AppendLine().AppendLine("=== the wobble cores played as the tool plays them (WobbleRig): the WobblePoint's bone, its rig and default animation, and the bone's sway over one loop - inverse(bindModel) * world, the core's model frame, row vectors");
        int played = 0;
        foreach (string type in types)
        {
            if (read(type + ".ot") is not { Length: > 0 } bytes || !StatDescriptionFiles.Decode(bytes).Contains(WobbleRig.Block, StringComparison.Ordinal))
            {
                continue;
            }

            if (WobbleRig.Of(read, WobbleRig.Unpacking, type, out string why) is not { } rig)
            {
                body.Append(type).Append(": not played - ").AppendLine(why);
                continue;
            }

            played++;
            body.AppendLine(rig.Said());
            body.Append(CultureInfo.InvariantCulture, $"=== {WobbleRig.Block} of played:{type} over time: the tool's own, a row every {PlayedStep} s over {rig.Seconds:0.###} s").AppendLine();
            body.AppendLine("t\t" + string.Join('\t', Enumerable.Range(0, 16).Select(at => string.Create(CultureInfo.InvariantCulture, $"+0x{0x68 + (at * 4):X}"))));
            for (float at = 0; at <= rig.Seconds; at += PlayedStep)
            {
                Matrix4x4 sway = rig.At(at);
                body.Append(at.ToString("0.000", CultureInfo.InvariantCulture));
                for (var row = 0; row < 4; row++)
                {
                    for (var column = 0; column < 4; column++)
                    {
                        body.Append('\t').Append(sway[row, column].ToString("G7", CultureInfo.InvariantCulture));
                    }
                }

                body.AppendLine();
            }

            body.AppendLine();
        }

        if (played == 0)
        {
            body.AppendLine("(no loaded type carries a WobblePoint that plays)");
        }

        return played;
    }

    /// <summary>The step the played table is written at.</summary>
    private const float PlayedStep = 0.02f;

    /// <summary>Every loaded object type under MiscellaneousObjects, and whether it may be swayed (WobbleRig.Sways).</summary>
    private static void Swaying(Func<string, byte[]?> read, IReadOnlyList<string>? loaded, StringBuilder body)
    {
        body.AppendLine().AppendLine("=== the loaded object types and whether a wobble may sway them - false where the type or what it extends sets no_wobble");
        foreach (string type in (loaded ?? []).Where(path => path.StartsWith("Metadata/MiscellaneousObjects/", StringComparison.OrdinalIgnoreCase) && !path.Contains('.', StringComparison.Ordinal)).Order(StringComparer.OrdinalIgnoreCase))
        {
            body.Append("  ").Append(type).Append("  ").AppendLine(WobbleRig.Sways(read, type) ? "sways" : "no_wobble");
        }
    }

    /// <summary>Whether a path names a wobble.</summary>
    private static bool Wobbly(string path) => path.Contains("wobble", StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether a path is an object type's file.</summary>
    private static bool Typed(string path) => Kinds.Any(kind => path.EndsWith(kind, StringComparison.OrdinalIgnoreCase));

    /// <summary>Whether a path is a text file worth writing whole.</summary>
    private static bool Text(string path) =>
        Typed(path) || path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".fxgraph", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".ais", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".epk", StringComparison.OrdinalIgnoreCase)
        || path.EndsWith(".pet", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".trl", StringComparison.OrdinalIgnoreCase);

    /// <summary>A path without the object type's extension it may carry.</summary>
    private static string Base(string path)
    {
        string clean = path.Replace('\\', '/').Trim();
        foreach (string kind in Kinds)
        {
            if (clean.EndsWith(kind, StringComparison.OrdinalIgnoreCase))
            {
                return clean[..^kind.Length];
            }
        }

        return clean;
    }

    /// <summary>A file whole, then each file it extends of the same kind. Returns how many were read.</summary>
    private static int Chain(Func<string, byte[]?> read, string path, string kind, HashSet<string> written, StringBuilder body, int depth)
    {
        if (depth > 12 || !written.Add(path) || read(path) is not { Length: > 0 } bytes)
        {
            return 0;
        }

        string text = Whole(path, bytes, body);
        int count = 1;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("extends", StringComparison.Ordinal))
            {
                continue;
            }

            int open = trimmed.IndexOf('"', StringComparison.Ordinal);
            int close = open < 0 ? -1 : trimmed.IndexOf('"', open + 1);
            string parent = close > open ? trimmed[(open + 1)..close] : string.Empty;
            if (parent.Length > 0 && !parent.Equals("nothing", StringComparison.OrdinalIgnoreCase))
            {
                count += Chain(read, parent + kind, kind, written, body, depth + 1);
            }
        }

        return count;
    }

    /// <summary>Writes a file whole under its path, and hands back its text.</summary>
    private static string Whole(string path, byte[] bytes, StringBuilder body)
    {
        string text = StatDescriptionFiles.Decode(bytes);
        body.AppendLine().Append("##### ").AppendLine(path);
        foreach (string line in text.Split('\n'))
        {
            body.Append("  | ").AppendLine(line.TrimEnd('\r'));
        }

        return text;
    }

    /// <summary>The strings each program holds naming a wobble.</summary>
    private static string Programs(IReadOnlyList<string>? programs, StringBuilder body)
    {
        body.AppendLine().AppendLine("=== the game's program: its strings naming a wobble - a component's name and its fields are spelled here if anywhere");
        if (programs is not { Count: > 0 })
        {
            body.AppendLine("(no program: the install's folder was not found, or holds none of the game's programs)");
            return "no program to search";
        }

        var total = 0;
        foreach (string program in programs)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(program);
            }
            catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                body.Append("- ").Append(program).Append(": not read, ").AppendLine(failed.Message);
                continue;
            }

            SortedDictionary<string, int> strings = CaptureWaterTextures.Strings(bytes, WordBytes);
            total += strings.Count;
            body.Append(CultureInfo.InvariantCulture, $"- {program}: {bytes.Length:N0} bytes, {strings.Count} strings").AppendLine();
            foreach ((string text, int times) in strings.Take(MostStrings))
            {
                body.Append("  ").Append(text);
                if (times > 1)
                {
                    body.Append(CultureInfo.InvariantCulture, $"  (x{times})");
                }

                body.AppendLine();
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{total} strings in the program name one");
    }
}
