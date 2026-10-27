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
                Chain(body, read, file.Ao, "did not draw");
            }

            if (unlaid.Count > MostWhole)
            {
                body.AppendLine().Append(CultureInfo.InvariantCulture, $"{unlaid.Count - MostWhole} more not written - named in the lines above").AppendLine();
            }
        }

        int moving = Moving(body, read, panes);

        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture,
            $"{panes.Count} model panes showing a model, {files} doodad files of their rooms with what became of each one's lines, {unlaid.Count} that did not draw written whole, {moving} with a rig or among the largest written whole")
            .AppendLine();
        said.Append(body);
        return said.ToString();
    }

    /// <summary>How many of the largest doodad files are written whole beside those with a rig - see <see cref="Moving"/>.</summary>
    private const int Largest = 3;

    /// <summary>
    /// Every doodad file of the rooms shown that carries a rig, and the few largest, written whole with what each extends and its
    /// rig's animations - and how many were.
    /// </summary>
    /// <remarks>
    /// WRITTEN FOR PORT'S SHIP (asked for from the live client): in the game boss_boat.ao rocks with the waves, in the Monster Book it
    /// stands still, as every doodad of a laid room does (RoomModels: a rig stays in its rest pose). Its materials are plain PBR
    /// graphs with no vertex motion (v0.1.287's capture), so what moves it is its .ao - a rig and an animation the game plays - or
    /// the engine itself; the file says which. The largest go in whatever they carry, the ship being 355,726 triangles of a room.
    /// </remarks>
    private static int Moving(StringBuilder body, Func<string, byte[]?>? read, IReadOnlyList<CapturePane> panes)
    {
        DoodadFile[] all = [.. panes.SelectMany(pane => pane.Model.DoodadFiles).DistinctBy(file => file.Ao, StringComparer.OrdinalIgnoreCase)];
        DoodadFile[] rigged = [.. all.Where(file => file.Rig.Length > 0 || file.Animations.Count > 0)];
        DoodadFile[] largest = [.. all.Except(rigged).OrderByDescending(file => file.Triangles).Take(Largest)];
        if (rigged.Length + largest.Length == 0)
        {
            return 0;
        }

        body.AppendLine().Append(CultureInfo.InvariantCulture,
            $"=== the doodad files with a rig ({rigged.Length}) and the {largest.Length} largest without one, whole, with their animations - what the game may move them by").AppendLine();
        foreach (DoodadFile file in rigged.Concat(largest).Take(MostWhole))
        {
            body.AppendLine().Append("  ").Append(file.Ao).Append(CultureInfo.InvariantCulture, $"  {file.Triangles} triangles, {file.Lines} lines, {file.Laid} laid")
                .AppendLine(file.Rig.Length > 0 ? "  rig " + file.Rig : "  no rig");
            foreach (string animation in file.Animations)
            {
                body.Append("    animation\t").AppendLine(animation);
            }

            Chain(body, read, file.Ao, file.Rig.Length > 0 ? "has a rig" : "among the largest");
        }

        return Math.Min(rigged.Length + largest.Length, MostWhole);
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
                $"{shore.Width} x {shore.Height} cells of {shore.CellSize:0.#} from ({shore.Left:0}, {shore.Top:0}), {shore.LandCells} land, ")
            .Append(CultureInfo.InvariantCulture,
                $"{shore.WallCells} of it walls meeting only water deeper than {ShoreField.BeachDeep:0} over its ground ({shore.StandingWalls} of them what stands in the sea) - no shore, the shore's distance measured past them, CHOSEN; ")
            .Append(CultureInfo.InvariantCulture,
                $"{shore.OverlappedCells} cells under two water shapes or more (at most {shore.MostLayers}) - where water drawn unlifted blends a layer for each; ")
            .Append(CultureInfo.InvariantCulture,
                $"the shore's distance {least:0.###} to {most:0.###} colour units, one being {ShoreField.Reach:0} - CHOSEN, the engine makes its own; picture {picture}, ")
            .AppendLine("row nought the least y, land brown by how far in, the walls grey, water blue by how far out, the shoreline white");
        ShoreField round = shore.Way(true);
        string way = Path.ChangeExtension(picture, null);
        body.Append(CultureInfo.InvariantCulture,
                $"  way in	the distance out to sea the waves run down (ocean_dist): the open sea's, {ShoreField.Deep:0} or more from land or at the field's edge, and inside it the way a wave travels from there through the water - CHOSEN until the engine's OceanMarkupField is read; ")
            .Append(CultureInfo.InvariantCulture,
                $"BY THE GROUND ALONE, the default from v0.1.307: {shore.Travelled} cells' way in shorter than their distance to the ground, {shore.Unreached} reached by no way, picture {way}-way.png; ")
            .Append(CultureInfo.InvariantCulture,
                $"ROUND WHAT STANDS IN THE WATER as well, the light panel's \"way round\": {shore.StandingCells} cells of what stands in the sea and {round.BlockedCells} water cells in the way (of {round.BlockingTriangles} solid triangles asked, {round.CrossingTriangles} crossing the water's surface: the cells their waterlines run through), {round.Travelled} cells' way in shorter than their shore's distance, {round.Unreached} reached by no way, picture {way}-way-round.png; ")
            .Append(Crossers(shore, model))
            .AppendLine($"each picture water by its way in, a white line every {WayStep:0} units - where the game's crests should run - land brown, the cells something crosses red, bright where they stand in the way");
        Beach(body, model);
    }

    /// <summary>What crossed the water's surface, the most triangles first - each shape's material and where it came from: the half the cells in the way were made from.</summary>
    private static string Crossers(ShoreField shore, MonsterModel model)
    {
        if (shore.CrossingShapes.Count == 0)
        {
            return "nothing crossing it; ";
        }

        const int Most = 12;
        var said = new StringBuilder("crossing it: ");
        foreach (KeyValuePair<int, int> one in shore.CrossingShapes.OrderByDescending(one => one.Value).Take(Most))
        {
            string material = one.Key < model.ShapeMaterials.Count ? Path.GetFileName(model.ShapeMaterials[one.Key]) : "?";
            string source = one.Key < model.ShapeSources.Count ? model.ShapeSources[one.Key] : "?";
            said.Append(CultureInfo.InvariantCulture, $"{one.Value} of shape {one.Key}, {material} from {source}; ");
        }

        if (shore.CrossingShapes.Count > Most)
        {
            said.Append(CultureInfo.InvariantCulture, $"and {shore.CrossingShapes.Count - Most} more shapes; ");
        }

        return said.ToString();
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
                (byte r, byte g, byte b) = shore.IsWall(at) ? ((byte)150, (byte)150, (byte)150)
                    : MathF.Abs(distance) < 0.01f ? ((byte)255, (byte)255, (byte)255)
                    : distance < 0f ? ((byte)(90 + (120 * -distance)), (byte)(60 + (60 * -distance)), (byte)30)
                    : ((byte)20, (byte)(60 + (100 * (1f - distance))), (byte)(110 + (145 * (1f - distance))));
                rgba[at * 4] = r;
                rgba[(at * 4) + 1] = g;
                rgba[(at * 4) + 2] = b;
                rgba[(at * 4) + 3] = 255;
            }

            string name = panes.Count > 1 ? $"shore-{pane.Book}.png" : ShoreFile;
            pictures.Add((name, new GamePicture(shore.Width, shore.Height, rgba)));
            pictures.Add((Path.ChangeExtension(name, null) + "-way.png", Way(shore)));
            pictures.Add((Path.ChangeExtension(name, null) + "-way-round.png", Way(shore.Way(true))));
        }

        return pictures;
    }

    /// <summary>The step the way-in picture draws its lines at, world units - a quarter of the engine's field wave.</summary>
    private const float WayStep = 250f;

    /// <summary>
    /// A shore's way in as a picture: water shaded by its distance out to sea, a white line wherever it crosses a whole <see cref="WayStep"/>
    /// - the crests' lines - land brown, the cells something crosses red: bright where the way runs round them, dark where it runs under.
    /// </summary>
    private static GamePicture Way(ShoreField shore)
    {
        ReadOnlySpan<float> texels = shore.Texels;
        int width = shore.Width, height = shore.Height;
        var ocean = new float[width * height];
        for (var at = 0; at < ocean.Length; at++)
        {
            ocean[at] = (1f - texels[(at * 2) + 1]) * ShoreField.Reach;
        }

        var rgba = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int at = (y * width) + x;
                bool land = texels[at * 2] < 0f;
                float here = ocean[at];
                float band = MathF.Floor(here / WayStep);
                bool line = !land
                    && ((x + 1 < width && texels[(at + 1) * 2] >= 0f && MathF.Floor(ocean[at + 1] / WayStep) != band)
                        || (y + 1 < height && texels[(at + width) * 2] >= 0f && MathF.Floor(ocean[at + width] / WayStep) != band));
                float shade = Math.Clamp(here / ShoreField.Reach, -1f, 1f);
                (byte r, byte g, byte b) = shore.InTheWay(at) ? (shore.RoundsWhatStands ? ((byte)220, (byte)40, (byte)40) : ((byte)110, (byte)25, (byte)25))
                    : land ? ((byte)110, (byte)75, (byte)35)
                    : line ? ((byte)255, (byte)255, (byte)255)
                    : ((byte)20, (byte)Math.Clamp(60 + (100 * (1f - shade)), 0, 255), (byte)Math.Clamp(110 + (145 * (1f - shade)), 0, 255));
                rgba[at * 4] = r;
                rgba[(at * 4) + 1] = g;
                rgba[(at * 4) + 2] = b;
                rgba[(at * 4) + 3] = 255;
            }
        }

        return new GamePicture(width, height, rgba);
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
                string mixed = materials.Length < 2 ? string.Empty
                    : mask.Length > 0 ? $" · mixed per pixel by {mask}'s green"
                    : " · mixed per pixel by its sub-tiles' corners (a shape across the mask's atlas, CHOSEN)";
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
    private static void Chain(StringBuilder body, Func<string, byte[]?>? read, string ao, string reason)
    {
        var queue = new Queue<(string Path, string Why)>();
        queue.Enqueue((ao, reason));
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
