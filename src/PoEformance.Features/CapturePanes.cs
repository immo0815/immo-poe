using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// One model book's pane as it stood when the capture key went down: its lines under the picture and the model it shows.
/// </summary>
/// <param name="Book">Which book's pane - "monster", "tile", ...</param>
/// <param name="Lines">The lines under the picture, as the pane's info fold shows them.</param>
/// <param name="Model">The model shown.</param>
public sealed record CapturePane(string Book, IReadOnlyList<string> Lines, MonsterModel Model)
{
    /// <summary>The shapes under the last probe click, nearest first - empty where the probe is off or was not clicked on this model.</summary>
    public IReadOnlyList<int> Probed { get; init; } = [];

    /// <summary>The programs the pane draws each shape with - the flat or the glossy list - or null where it draws none.</summary>
    public IReadOnlyList<ShadeProgram?>? Shades { get; init; }

    /// <summary>Where the time of building the model and drawing its first picture went - see ModelProgress.Report.</summary>
    public IReadOnlyList<string> Built { get; init; } = [];
}

/// <summary>
/// The capture's model-panes part: every model pane's lines under its picture, every doodad file of the room it shows with what
/// became of its lines, and each file that did not draw written whole with what it extends.
/// </summary>
/// <remarks>
/// WRITTEN FOR PORT'S WATER. The harbour tile brings 31 lines of WaterPlane3x3.ao, the Monster Book's stage drew none of
/// them, and the probe over the water found only the ocean's black floor - so the plane was not in the picture at all. Why
/// was said nowhere the capture could reach: the stage dropped the room's own lines, and those said only how many doodads in
/// all were left out and which ONE did not draw first. A file not drawn, a cap that ran out before the tiles' doodads, and a
/// plane laid somewhere else are three causes the picture cannot tell apart; the counts per file can.
///
/// PRINT BOTH HALVES: each file's line carries where its first line puts it and the box its mesh spans, so a doodad laid
/// where nobody looks can be told from one never laid; and a file that did not draw is written whole, with every file it
/// extends, so what it is made of is read rather than guessed.
/// </remarks>
public static class CapturePanes
{
    /// <summary>Most files written whole - the doodad files that did not draw, the first named first.</summary>
    private const int MostWhole = 32;

    /// <summary>Most files one extends chain is followed through.</summary>
    private const int MostDeep = 16;

    /// <summary>
    /// The part's text: a line saying what it holds, then each pane, then the files that did not draw, whole. Never throws.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path - or null, and the files are only named.</param>
    /// <param name="panes">The panes showing a model.</param>
    public static string Write(Func<string, byte[]?>? read, IReadOnlyList<CapturePane> panes)
    {
        ArgumentNullException.ThrowIfNull(panes);
        var body = new StringBuilder();
        var unlaid = new List<DoodadFile>();
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int files = 0;
        foreach (CapturePane pane in panes)
        {
            body.AppendLine().Append("=== the ").Append(pane.Book).AppendLine(" book's pane");
            MonsterModel model = pane.Model;
            body.Append("  shows\t").AppendLine(model.Ready ? model.Mesh_ : "nothing - " + model.Why);
            if (model.Stage.Length > 0)
            {
                body.Append("  stage\t").AppendLine(model.Stage);
            }

            foreach (string line in pane.Lines)
            {
                body.Append("  info\t").AppendLine(line);
            }

            foreach (string line in pane.Built)
            {
                body.Append("  time\t").AppendLine(line);
            }

            if (model.StageMove.Length > 0)
            {
                body.Append("  stage's own lines\t").AppendLine(model.StageMove);
            }

            Shore(body, model, panes.Count > 1 ? $"shore-{pane.Book}.png" : ShoreFile);

            if (model.DoodadFiles.Count == 0)
            {
                continue;
            }

            files += model.DoodadFiles.Count;
            Files(body, model.DoodadFiles);
            foreach (DoodadFile file in model.DoodadFiles)
            {
                if (file.Missing > 0 && named.Add(file.Ao))
                {
                    unlaid.Add(file);
                }
            }
        }

        Materials(body, read, panes);

        if (unlaid.Count > 0)
        {
            body.AppendLine().Append(CultureInfo.InvariantCulture, $"=== the doodad files that did not draw, whole, with what each extends ({unlaid.Count})").AppendLine();
            foreach (DoodadFile file in unlaid.Take(MostWhole))
            {
                Chain(body, read, file.Ao);
            }

            if (unlaid.Count > MostWhole)
            {
                body.AppendLine().Append(CultureInfo.InvariantCulture, $"{unlaid.Count - MostWhole} more not written - named in the lines above").AppendLine();
            }
        }

        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture,
            $"{panes.Count} model panes showing a model, {files} doodad files of their rooms with what became of each one's lines, {unlaid.Count} that did not draw written whole")
            .AppendLine();
        said.Append(body);
        return said.ToString();
    }

    /// <summary>The picture of a pane's shore beside the part - see <see cref="ShorePictures"/>.</summary>
    public const string ShoreFile = "shore.png";

    /// <summary>
    /// The shore the room's swell rolls in to, as made (ShoreField, CHOSEN): what it was made from and what came of it - and where its picture lies.
    /// </summary>
    private static void Shore(StringBuilder body, MonsterModel model, string picture)
    {
        if (model.Shore is not { } shore)
        {
            if (model.Kind == ModelKind.Room || model.Stage.Length > 0)
            {
                body.AppendLine("  shore\tnone - no water, or no ground standing over it: the swell is not drawn and the ripples run whole");
            }

            return;
        }

        float least = float.MaxValue, most = float.MinValue;
        ReadOnlySpan<float> texels = shore.Texels;
        for (var at = 0; at < texels.Length; at += 2)
        {
            least = MathF.Min(least, texels[at]);
            most = MathF.Max(most, texels[at]);
        }

        body.Append(CultureInfo.InvariantCulture,
            $"  shore\tmade from {shore.WaterTriangles} water and {shore.GroundTriangles} ground triangles, and {shore.StandingTriangles} of what stands in the sea (CHOSEN: the shapes whose material sprays, {shore.StandingCells} cells land by them alone), against a water level of {shore.Level:0.#}: ")
            .Append(CultureInfo.InvariantCulture,
                $"{shore.Width} x {shore.Height} cells of {shore.CellSize:0.#} from ({shore.Left:0}, {shore.Top:0}), {shore.LandCells} land; ")
            .Append(CultureInfo.InvariantCulture,
                $"the shore's distance {least:0.###} to {most:0.###} colour units, one being {ShoreField.Reach:0} - CHOSEN, the engine makes its own; picture {picture}, ")
            .AppendLine("row nought the least y, land brown by how far in, water blue by how far out, the shoreline white");
        Beach(body, model);
    }

    /// <summary>
    /// The beach waves' sheet: what it was laid over and how much of it there is, what its program came to, and what it reads - every
    /// number CHOSEN or taken said beside it.
    /// </summary>
    private static void Beach(StringBuilder body, MonsterModel model)
    {
        int shape = -1;
        for (var one = 0; one < model.ShapeSources.Count && one < model.Mesh.Shapes.Count; one++)
        {
            if (model.ShapeSources[one] == RoomModels.BeachSource)
            {
                shape = one;
                break;
            }
        }

        if (shape < 0)
        {
            body.Append(CultureInfo.InvariantCulture,
                $"  beach	none - no ground of the room lies from {ShoreField.BeachDeep:0} under the water to {ShoreField.BeachHigh:0} above it").AppendLine();
            return;
        }

        MeshShape sheet = model.Mesh.Shapes[shape];
        var least = new Vector3(float.MaxValue);
        var most = new Vector3(float.MinValue);
        for (int at = sheet.From; at < sheet.From + sheet.Count && at < model.Mesh.Indices.Length; at++)
        {
            Vector3 place = model.Mesh.Positions[model.Mesh.Indices[at]];
            least = Vector3.Min(least, place);
            most = Vector3.Max(most, place);
        }

        ShadeProgram? program = shape < model.Shades.Count ? model.Shades[shape] : null;
        body.Append(CultureInfo.InvariantCulture,
                $"  beach	{sheet.Count / 3} triangles over the ground from ({least.X:0}, {least.Y:0}, {least.Z:0}) to ({most.X:0}, {most.Y:0}, {most.Z:0}), ")
            .Append(CultureInfo.InvariantCulture,
                $"laid {ShoreField.BeachRaise:0} over the ground where it lies from {ShoreField.BeachDeep:0} under the water to {ShoreField.BeachHigh:0} above it, no square rising past {ShoreField.BeachStep:0} - CHOSEN; ")
            .Append(CultureInfo.InvariantCulture,
                $"wearing {ShadeProgram.BeachGraph}: {(program is { Beaches: true } ? "drawn as the engine's beach waves" : "NOT DRAWN - its program did not compile or bind")}; ")
            .Append(CultureInfo.InvariantCulture,
                $"the shore's travel distance {ShadeProgram.BeachTravelShift:0} less than the field's, CHOSEN; its wave shape {ShadeProgram.BeachProfile.Said}, its noise {ShadeProgram.BeachNoise.Said}")
            .AppendLine();
    }

    /// <summary>Each pane's shore as a picture, for the capture to write beside the part - named as its line names it.</summary>
    public static IReadOnlyList<(string File, GamePicture Picture)> ShorePictures(IReadOnlyList<CapturePane> panes)
    {
        ArgumentNullException.ThrowIfNull(panes);
        var pictures = new List<(string, GamePicture)>();
        foreach (CapturePane pane in panes)
        {
            if (pane.Model.Shore is not { } shore)
            {
                continue;
            }

            ReadOnlySpan<float> texels = shore.Texels;
            var rgba = new byte[shore.Width * shore.Height * 4];
            for (var at = 0; at < shore.Width * shore.Height; at++)
            {
                float distance = texels[at * 2];
                (byte r, byte g, byte b) = MathF.Abs(distance) < 0.01f ? ((byte)255, (byte)255, (byte)255)
                    : distance < 0f ? ((byte)(90 + (120 * -distance)), (byte)(60 + (60 * -distance)), (byte)30)
                    : ((byte)20, (byte)(60 + (100 * (1f - distance))), (byte)(110 + (145 * (1f - distance))));
                rgba[at * 4] = r;
                rgba[(at * 4) + 1] = g;
                rgba[(at * 4) + 2] = b;
                rgba[(at * 4) + 3] = 255;
            }

            pictures.Add((panes.Count > 1 ? $"shore-{pane.Book}.png" : ShoreFile, new GamePicture(shore.Width, shore.Height, rgba)));
        }

        return pictures;
    }

    /// <summary>
    /// The materials under each pane's last probe click and those it draws plain - neither texture nor program - each whole
    /// with what it compiles to and what drawing it left out, then their graphs once each, whole.
    /// </summary>
    /// <remarks>
    /// WRITTEN FOR PORT'S WATER AGAIN: laid at last (v0.1.203), the planes came out white - drawn, but with nothing to colour
    /// them - and why is in the material and its graphs, which no part wrote for a doodad. The probed ones first, nearest
    /// first, then the plain ones by how many triangles they cover; a shadow-only material is not drawn, so not listed.
    /// </remarks>
    private static void Materials(StringBuilder body, Func<string, byte[]?>? read, IReadOnlyList<CapturePane> panes)
    {
        var picked = new List<(string Material, string Why)>();
        var named = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int plainInAll = 0;
        foreach (CapturePane pane in panes)
        {
            MonsterModel model = pane.Model;
            foreach (int shape in pane.Probed)
            {
                if (MaterialOf(model, shape) is not { Length: > 0 } worn)
                {
                    continue;
                }

                // TWO GROUNDS MIXED ARE WRITTEN AS THE TWO MATERIALS THEY ARE, the name they are worn under being no file.
                string[] materials = GroundBlend.Parts(worn, out string first, out string second, out string mask) ? [first, second] : [worn];
                string mixed = materials.Length == 2 ? $" · mixed per pixel by {mask}'s green" : string.Empty;
                foreach (string material in materials)
                {
                    if (named.Add(material))
                    {
                        picked.Add((material, $"under the probe in the {pane.Book} book's pane · {Drawn(pane, shape)}{mixed}{From(model, shape)}"));
                    }
                }
            }

            // PLAIN BY TRIANGLES COVERED, most first: what the picture shows most of is what to read first.
            var plain = new Dictionary<string, (long Triangles, int Shape)>(StringComparer.OrdinalIgnoreCase);
            for (var shape = 0; shape < model.Mesh.Shapes.Count; shape++)
            {
                if (Drawn(pane, shape) != "plain"
                    || (shape < model.Blends.Count && model.Blends[shape] == MaterialBlend.ShadowOnly)
                    || MaterialOf(model, shape) is not { Length: > 0 } material)
                {
                    continue;
                }

                long triangles = model.Mesh.Shapes[shape].Count / 3;
                plain[material] = plain.TryGetValue(material, out (long Triangles, int Shape) had) ? (had.Triangles + triangles, had.Shape) : (triangles, shape);
            }

            plainInAll += plain.Count;
            foreach ((string material, (long triangles, int shape)) in plain.OrderByDescending(one => one.Value.Triangles))
            {
                if (named.Add(material))
                {
                    picked.Add((material, string.Create(CultureInfo.InvariantCulture, $"drawn plain in the {pane.Book} book's pane · {triangles} triangles{From(model, shape)}")));
                }
            }
        }

        var paints = new MonsterModels.Paints();
        int found = picked.Count;
        List<(string Material, string Why)> shore = read is null ? [] : ShoreOnes(read, panes, paints, named);
        picked.AddRange(shore.Take(MostShore));
        if (picked.Count == 0)
        {
            return;
        }

        body.AppendLine().Append(CultureInfo.InvariantCulture,
            $"=== the materials under the probe and those drawn plain, whole ({Math.Min(found, MostMaterials)} of {found}; {plainInAll} drawn plain), then those naming a shore's node or with nodes the compile left out ({Math.Min(shore.Count, MostShore)} of {shore.Count})").AppendLine();
        if (read is null)
        {
            foreach ((string material, string why) in picked.Take(Math.Min(found, MostMaterials)))
            {
                body.AppendLine().Append("##### ").Append(material).Append("  (").Append(why).AppendLine(")").AppendLine("  (no install to read it from)");
            }

            return;
        }

        var graphs = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string material, string why) in picked.Take(Math.Min(found, MostMaterials)).Concat(picked.Skip(found)))
        {
            body.AppendLine().Append("##### ").Append(material).Append("  (").Append(why).AppendLine(")");
            if (!CaptureMaterials.Material(read, material, paints, body, graphs, seen))
            {
                continue;
            }

            // AND WHAT DRAWING IT LEFT OUT - a texture that would not read is the step past the compile.
            MonsterModels.Shade shade = paints.Shade(read, material);
            body.Append("drawn: ").AppendLine(shade.Program switch
            {
                { UsesTime: true } => "from its program, running with the clock",
                not null => "from its program",
                null when shade.Plain is not null => "from a plain sheet",
                _ => "plain - nothing to colour it with",
            } + (shade.Skipped.Count > 0 ? " · left out: " + string.Join("; ", shade.Skipped) : string.Empty));
        }

        CaptureMaterials.Graphs(read, graphs, "a graph these materials are instances of", body);
    }

    /// <summary>How the pane draws a shape: from a program, a texture, or plain - as the probe says it.</summary>
    private static string Drawn(CapturePane pane, int shape)
        => pane.Shades is { } shades && shape < shades.Count && shades[shape] is not null
            ? "program"
            : shape < pane.Model.Skins.Count && pane.Model.Skins[shape] is not null ? "texture" : "plain";

    private static string? MaterialOf(MonsterModel model, int shape)
        => shape >= 0 && shape < model.ShapeMaterials.Count && model.ShapeMaterials[shape].Length > 0 ? MaterialFile.Bare(model.ShapeMaterials[shape]) : null;

    private static string From(MonsterModel model, int shape)
        => shape < model.ShapeSources.Count && model.ShapeSources[shape].Length > 0 ? " · from " + model.ShapeSources[shape] : string.Empty;

    /// <summary>Most materials written whole.</summary>
    private const int MostMaterials = 24;

    /// <summary>Most materials written whole for naming a shore's node or for what their compile left out.</summary>
    private const int MostShore = 32;

    /// <summary>
    /// The graph nodes the engine puts the sea onto what stands in it with (effects.ffx): the foam and the wet streaks on rocks, the
    /// beach's, the crashing and the flood waves, their spray - a material's own graph's, not the water pass's.
    /// </summary>
    public static IReadOnlyList<string> ShoreNodes { get; } =
        ["CrashingFoamSurface", "BeachFoamSurface", "CrashingWaves", "BreakingWaves", "FloodWave", "SprayTrigger", "Wetness", "shoreline"];

    /// <summary>
    /// Every material a pane draws, not yet written, that names one of <see cref="ShoreNodes"/> in its file or the graphs it is an
    /// instance of, or whose compile left a node out - the shore's first, then by the triangles they cover.
    /// </summary>
    /// <remarks>
    /// WRITTEN WHEN THE GAME'S ROCKS BY THE SEA SHOWED WHITE STREAKS OF WATER RUNNING DOWN THEM (asked for from the live client,
    /// v0.1.233) and the viewer's did not: the engine draws that in the rock's own material (CrashingFoamSurface, a triplanar foam
    /// scrolling down the face on the crashing wave's phase), so the question is which materials use it and what the compile made
    /// of them - answered here for every material of the room, with no probe click needed.
    /// </remarks>
    private static List<(string Material, string Why)> ShoreOnes(
        Func<string, byte[]?> read, IReadOnlyList<CapturePane> panes, MonsterModels.Paints paints, HashSet<string> named)
    {
        var graphNodes = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var found = new List<(string Material, string Why, bool Shore, long Triangles)>();
        foreach (CapturePane pane in panes)
        {
            MonsterModel model = pane.Model;
            var covered = new Dictionary<string, (long Triangles, int Shape)>(StringComparer.OrdinalIgnoreCase);
            for (var shape = 0; shape < model.Mesh.Shapes.Count; shape++)
            {
                if (MaterialOf(model, shape) is not { Length: > 0 } material || named.Contains(material))
                {
                    continue;
                }

                long triangles = model.Mesh.Shapes[shape].Count / 3;
                covered[material] = covered.TryGetValue(material, out (long Triangles, int Shape) had) ? (had.Triangles + triangles, had.Shape) : (triangles, shape);
            }

            foreach ((string material, (long triangles, int shape)) in covered)
            {
                string[] nodes = NodesOf(read, material, graphNodes);
                IReadOnlyList<string> left = paints.Compiled(read, material).Compiled.Skipped;
                if (nodes.Length == 0 && left.Count == 0 || !named.Add(material))
                {
                    continue;
                }

                string why = (nodes.Length > 0 ? "names " + string.Join(", ", nodes) + " · " : string.Empty)
                    + (left.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $"the compile left out {left.Count} · ") : string.Empty)
                    + string.Create(CultureInfo.InvariantCulture, $"{Drawn(pane, shape)} in the {pane.Book} book's pane · {triangles} triangles{From(model, shape)}");
                found.Add((material, why, nodes.Length > 0, triangles));
            }
        }

        return [.. found.OrderByDescending(one => one.Shore).ThenByDescending(one => one.Triangles).Select(one => (one.Material, one.Why))];
    }

    /// <summary>The shore's nodes a material names in its file or in the graphs it is an instance of - each graph read once.</summary>
    private static string[] NodesOf(Func<string, byte[]?> read, string material, Dictionary<string, string[]> graphNodes)
    {
        byte[]? content = read(MaterialFile.Bare(material).Replace('\\', '/').Trim());
        if (content is not { Length: > 0 })
        {
            return [];
        }

        var nodes = new SortedSet<string>(Named(StatDescriptionFiles.Decode(content)), StringComparer.Ordinal);
        foreach (string graph in MaterialFile.Read(content).Parents)
        {
            string path = graph.Replace('\\', '/').Trim();
            if (path.Length == 0)
            {
                continue;
            }

            if (!graphNodes.TryGetValue(path, out string[]? inGraph))
            {
                byte[]? text = read(path);
                inGraph = text is { Length: > 0 } ? [.. Named(StatDescriptionFiles.Decode(text))] : [];
                graphNodes[path] = inGraph;
            }

            nodes.UnionWith(inGraph);
        }

        return [.. nodes];
    }

    private static IEnumerable<string> Named(string text)
        => ShoreNodes.Where(node => text.Contains(node, StringComparison.OrdinalIgnoreCase));

    /// <summary>A room's doodad files, a line each: the counts, the box and first place, the materials, and why it did not draw.</summary>
    private static void Files(StringBuilder body, IReadOnlyList<DoodadFile> files)
    {
        int lines = 0, laid = 0, missing = 0, capped = 0, unseen = 0;
        foreach (DoodadFile file in files)
        {
            lines += file.Lines;
            laid += file.Laid;
            missing += file.Missing;
            capped += file.Capped;
            unseen += file.Unseen;
        }

        body.Append(CultureInfo.InvariantCulture,
            $"  doodad files: {files.Count}, {lines} lines - {laid} laid, {missing} not drawn, {capped} left out by the cap, {unseen} unseen").AppendLine();
        body.AppendLine("    lines\tlaid\tnot drawn\tcapped\tunseen\ttriangles\tbox in its own frame\tfirst line at\tfile\tmaterials (blend)\twhy not drawn");
        foreach (DoodadFile file in files)
        {
            body.Append(CultureInfo.InvariantCulture,
                $"    {file.Lines}\t{file.Laid}\t{file.Missing}\t{file.Capped}\t{file.Unseen}\t{file.Triangles}\t{Box(file)}\t{Point(file.First)}\t{file.Ao}\t{Materials(file)}\t{file.Why}")
                .AppendLine();
        }
    }

    /// <summary>The mesh's box, or a dash where it did not load.</summary>
    private static string Box(DoodadFile file)
        => file.Triangles == 0 && file.Least == file.Most ? "-" : Point(file.Least) + " .. " + Point(file.Most);

    private static string Point(Vector3 at) => string.Create(CultureInfo.InvariantCulture, $"{at.X:0.#} {at.Y:0.#} {at.Z:0.#}");

    /// <summary>Each distinct material with the blend word its shapes carry, or a dash.</summary>
    private static string Materials(DoodadFile file)
    {
        if (file.Materials.Count == 0)
        {
            return "-";
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var said = new List<string>();
        for (var shape = 0; shape < file.Materials.Count; shape++)
        {
            string mode = shape < file.Modes.Count && file.Modes[shape].Length > 0 ? file.Modes[shape] : "-";
            string one = $"{file.Materials[shape]} ({mode})";
            if (seen.Add(one))
            {
                said.Add(one);
            }
        }

        return string.Join(", ", said);
    }

    /// <summary>
    /// One .ao written whole, then every file it extends, breadth first - each read as the model walk reads it, the
    /// extension added where the extends line has none.
    /// </summary>
    private static void Chain(StringBuilder body, Func<string, byte[]?>? read, string ao)
    {
        var queue = new Queue<(string Path, string Why)>();
        queue.Enqueue((ao, "did not draw"));
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int written = 0;
        while (queue.Count > 0 && written < MostDeep)
        {
            (string path, string why) = queue.Dequeue();
            string said = path.Replace('\\', '/').Trim();
            if (said.Length == 0 || !seen.Add(said))
            {
                continue;
            }

            body.AppendLine().Append("##### ").Append(said).Append("  (").Append(why).AppendLine(")");
            if (read is null)
            {
                body.AppendLine("  (no install to read it from)");
                continue;
            }

            (byte[]? bytes, string found) = Object(read, said);
            if (bytes is not { Length: > 0 })
            {
                body.AppendLine("  (not in the install)");
                continue;
            }

            if (!string.Equals(found, said, StringComparison.Ordinal))
            {
                body.Append("  read as ").AppendLine(found);
            }

            written++;
            CaptureReport.Whole(body, bytes);
            foreach (string up in AnimatedObject.Read(bytes).Extends)
            {
                if (!up.Equals("nothing", StringComparison.OrdinalIgnoreCase))
                {
                    queue.Enqueue((up, "extended by " + said));
                }
            }
        }
    }

    /// <summary>An .ao's bytes as written, or with the extension added where the path has none - see AnimatedObject.Read.</summary>
    private static (byte[]? Bytes, string Path) Object(Func<string, byte[]?> read, string path)
    {
        if (read(path) is { Length: > 0 } bytes)
        {
            return (bytes, path);
        }

        int slash = path.LastIndexOf('/');
        string suffixed = path + AnimatedObject.Suffix;
        return path.IndexOf('.', slash + 1) < 0 ? (read(suffixed), suffixed) : (null, path);
    }
}
