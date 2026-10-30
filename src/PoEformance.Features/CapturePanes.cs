using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Entities;
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

    /// <summary>Where the picture on screen was looked at from - see CameraPose - or null where nothing was drawn.</summary>
    public CameraPose? View { get; init; }

    /// <summary>The monster's row, for the .ao files it names - null where the pane shows no monster (CaptureMonsterFiles).</summary>
    public MonsterVariety? Variety { get; init; }

    /// <summary>The monster's metadata path as the book asked for it - empty where it shows none.</summary>
    public string Path { get; init; } = string.Empty;

    /// <summary>How the install unpacks a bundle of keyframes, or null where nothing can - the pane's own (MonsterPortrait).</summary>
    public Func<ReadOnlyMemory<byte>, int, byte[]?>? Unpack { get; init; }

    /// <summary>
    /// The game's light as the pane's light panel builds it with its switches - whether or not the pane draws under it now (<see cref="LightOn"/>) -
    /// for the parts that hold the pane against the game's picture; null where the book has no panel or the model is no picture.
    /// </summary>
    public SceneLight? Light { get; init; }

    /// <summary>Whether the pane draws under <see cref="Light"/> now - its "game light" ticked - or under its own lamp.</summary>
    public bool LightOn { get; init; }

    /// <summary>What the light panel says of the light: its switches, the environment, what was assumed, the sun, the cube and the grade.</summary>
    public IReadOnlyList<string> LightSaid { get; init; } = [];

    /// <summary>The area's dust the pane's materials read.</summary>
    public Vector3 Dust { get; init; } = EnvironmentSettings.AssumedDust;

    /// <summary>The water the pane draws with - the area's, over the room's shore and the river's flow.</summary>
    public WaterLook Water { get; init; } = WaterLook.EngineDefault;

    /// <summary>The clock the pane's picture stood at, seconds.</summary>
    public float Clock { get; init; }

    /// <summary>The ink a shape with no texture is drawn in.</summary>
    public Vector3 Ink { get; init; }

    /// <summary>The blends the pane draws each shape with - not always the model's own (MonsterPortrait's Blends) - or null for every shape solid.</summary>
    public IReadOnlyList<MaterialBlend>? Blends { get; init; }

    /// <summary>The vertices as the pane drew them where a pose moved them - a copy - or null for the mesh's own.</summary>
    public Vector3[]? Positions { get; init; }

    /// <summary>Their normals, beside <see cref="Positions"/>.</summary>
    public Vector3[]? Normals { get; init; }
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
/// <summary>A shore field's numbers as the capture writes them (CapturePanes.FieldFormat), read back - every distance in world units, rows from the least y.</summary>
/// <param name="Width">Cells across.</param>
/// <param name="Height">Cells down.</param>
/// <param name="Left">The least x.</param>
/// <param name="Top">The least y.</param>
/// <param name="Cell">World units a cell.</param>
/// <param name="Level">The water's level.</param>
/// <param name="Kinds">Each cell's kind, ShoreField's bits.</param>
/// <param name="Shore">The shore's distance.</param>
/// <param name="ByGround">The way in by the ground.</param>
/// <param name="Round">The way in round what stands in the water.</param>
/// <param name="PastWalls">The way in past the walls.</param>
/// <param name="GroundShore">The ground's shore's distance.</param>
public sealed record ShoreNumbers(
    int Width, int Height, float Left, float Top, float Cell, float Level, byte[] Kinds, float[] Shore, float[] ByGround, float[] Round,
    float[] PastWalls, float[] GroundShore);

public static class CapturePanes
{
    /// <summary>
    /// The camera's line: the pose as the pane holds it, and what it comes to - the eye, the way it looks, the near plane - so a probe's
    /// depths and a picture's framing can be read back from the capture.
    /// </summary>
    public static string Camera(in CameraPose view)
    {
        static string At(Vector3 point) => string.Create(CultureInfo.InvariantCulture, $"({point.X:0.###}, {point.Y:0.###}, {point.Z:0.###})");
        const float Degrees = 180f / MathF.PI;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(view.Parallel ? "parallel" : $"perspective, {CameraPose.FieldOfView * Degrees:0.#} degrees top to bottom")}, focus {At(view.Focus)}, turn {view.Turn * Degrees:0.##} degrees, tilt {view.Tilt * Degrees:0.##} degrees, distance {view.Distance:0.###} - the eye at {At(view.Eye)} looking along {At(view.Forward)}, right {At(view.Right)}{(view.Parallel ? string.Empty : string.Create(CultureInfo.InvariantCulture, $", near plane {view.Near:0.###}"))}");
    }

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

            if (pane.View is { Valid: true } view)
            {
                body.Append("  camera\t").AppendLine(Camera(view));
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
            Seams(body, model);

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
    /// <summary>Where the room's water shapes meet, as drawn - whether their edges carry each other's corners (WaterSeams), where it has two or more.</summary>
    private static void Seams(StringBuilder body, MonsterModel model)
    {
        var water = new bool[model.Mesh.Shapes.Count];
        for (var shape = 0; shape < water.Length && shape < model.Modes.Count; shape++)
        {
            water[shape] = model.Modes[shape] == MonsterModels.Paints.OceanMode;
        }

        WaterSeams seams = WaterSeams.Of(model.Mesh, water);
        if (seams.Edges == 0)
        {
            return;
        }

        body.Append(CultureInfo.InvariantCulture,
                $"  water seams\tthe water shapes' {seams.Edges} border edges as drawn: {seams.Matched} meet another water shape corner to corner, ")
            .Append(CultureInfo.InvariantCulture,
                $"{seams.Junctions} carry {seams.Corners} corners of another in their middle - T-junctions, which open a crack onto what lies beneath once the water is lifted - ")
            .Append(CultureInfo.InvariantCulture, $"and {seams.Outer} run along no other water shape (the water's own border)")
            .AppendLine(seams.Samples.Count == 0
                ? string.Empty
                : "; such corners at " + string.Join(", ", seams.Samples.Select(one => string.Create(CultureInfo.InvariantCulture, $"({one.X:0.#}, {one.Y:0.#})"))));
    }

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
        float groundLeast = float.MaxValue, groundMost = float.MinValue;
        foreach (float one in shore.GroundTexels)
        {
            groundLeast = MathF.Min(groundLeast, one);
            groundMost = MathF.Max(groundMost, one);
        }

        string way = Path.ChangeExtension(picture, null);
        body.Append(CultureInfo.InvariantCulture,
                $"  ground's shore	the distance to the shore of the ground alone, what stands in the sea no shore - the field waves' break phase takes it where the light panel's \"break by ground\" says, CHOSEN: ")
            .AppendLine(CultureInfo.InvariantCulture,
                $"{shore.GroundFarther} water cells farther from it than from the shore, its distance {groundLeast:0.###} to {groundMost:0.###} colour units; picture {way}-ground.png, coloured as {picture}");
        ShoreField round = shore.Way(true);
        body.Append(CultureInfo.InvariantCulture,
                $"  way in	the distance out to sea the waves run down (ocean_dist): the open sea's, {ShoreField.Deep:0} or more from land or at the field's edge, and inside it the way a wave travels from there through the water - CHOSEN until the engine's OceanMarkupField is read; ")
            .Append(CultureInfo.InvariantCulture,
                $"BY THE GROUND ALONE, the default from v0.1.307: {shore.Travelled} cells' way in shorter than their distance to the ground, {shore.Unreached} reached by no way, picture {way}-way.png; ")
            .Append(CultureInfo.InvariantCulture,
                $"ROUND WHAT STANDS IN THE WATER as well, the light panel's \"way round\": {shore.StandingCells} cells of what stands in the sea and {round.BlockedCells} water cells in the way (of {round.BlockingTriangles} solid triangles asked, {round.CrossingTriangles} crossing the water's surface: the cells their waterlines run through), {round.Travelled} cells' way in shorter than their shore's distance, {round.Unreached} reached by no way, picture {way}-way-round.png; ")
            .Append(Crossers(shore, model))
            .Append(CultureInfo.InvariantCulture,
                $"PAST THE WALLS, the light panel's \"past walls\" (v0.1.333, CHOSEN to hold against the game): {shore.Way(false, true).WallGround} cells of a wall's own ground - its nearest coast a wall within {ShoreField.WallReach:0} - in no wave's way, {shore.Way(false, true).Travelled} cells' way in shorter than their distance to the ground that steers, {shore.Way(false, true).Unreached} reached by no way, picture {way}-way-past.png; ")
            .Append(CultureInfo.InvariantCulture, $"every cell's kind and distances as numbers: {way}-field.bin ({FieldFormat}); ")
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

            string name = panes.Count > 1 ? $"shore-{pane.Book}.png" : ShoreFile;
            pictures.Add((name, Distances(shore, at => shore.Texels[at * 2])));
            pictures.Add((Path.ChangeExtension(name, null) + "-ground.png", Distances(shore, at => shore.GroundTexels[at])));
            pictures.Add((Path.ChangeExtension(name, null) + "-way.png", Way(shore)));
            pictures.Add((Path.ChangeExtension(name, null) + "-way-round.png", Way(shore.Way(true))));
            pictures.Add((Path.ChangeExtension(name, null) + "-way-past.png", Way(shore.Way(false, true))));
        }

        return pictures;
    }

    /// <summary>What a field's numbers hold, as its line says it - see <see cref="ShoreFields"/>.</summary>
    public const string FieldFormat =
        "little-endian: 'SHF1', width and height as int32, the least x and y, the cell and the water level as float32, then a byte a cell of its kind"
        + " (bits: 1 land, 2 ground, 4 standing in the sea, 8 wall, 16 shore, 32 in the way round, 64 ground the way past the walls counts) and"
        + " five float32 arrays a cell each, world units: the shore's distance, the way in by the ground, round what stands, past the walls, and"
        + " the ground's shore's distance; rows from the least y";

    /// <summary>
    /// Each pane's shore as numbers, for the capture to write beside the part - every cell's kind and every distance the pictures draw, so
    /// a video of the game held against the capture's camera can be read into the room's frame and set beside each way in (v0.1.333).
    /// </summary>
    public static IReadOnlyList<(string File, byte[] Bytes)> ShoreFields(IReadOnlyList<CapturePane> panes)
    {
        ArgumentNullException.ThrowIfNull(panes);
        var fields = new List<(string, byte[])>();
        foreach (CapturePane pane in panes)
        {
            if (pane.Model.Shore is { } shore)
            {
                string name = panes.Count > 1 ? $"shore-{pane.Book}.png" : ShoreFile;
                fields.Add((Path.ChangeExtension(name, null) + "-field.bin", FieldBytes(shore)));
            }
        }

        return fields;
    }

    /// <summary>A field's numbers read back - see <see cref="FieldFormat"/>; null where the bytes are not one.</summary>
    public static ShoreNumbers? ReadField(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 28 || !bytes[..4].SequenceEqual("SHF1"u8))
        {
            return null;
        }

        int width = BinaryPrimitives.ReadInt32LittleEndian(bytes[4..]), height = BinaryPrimitives.ReadInt32LittleEndian(bytes[8..]);
        long cells = (long)width * height;
        if (width <= 0 || height <= 0 || bytes.Length != 28 + cells + (cells * 5 * 4))
        {
            return null;
        }

        var arrays = new float[5][];
        int at = 28 + (int)cells;
        for (var one = 0; one < arrays.Length; one++)
        {
            arrays[one] = new float[cells];
            for (var cell = 0; cell < cells; cell++, at += 4)
            {
                arrays[one][cell] = BinaryPrimitives.ReadSingleLittleEndian(bytes[at..]);
            }
        }

        return new ShoreNumbers(
            width, height, BinaryPrimitives.ReadSingleLittleEndian(bytes[12..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[16..]),
            BinaryPrimitives.ReadSingleLittleEndian(bytes[20..]), BinaryPrimitives.ReadSingleLittleEndian(bytes[24..]), bytes.Slice(28, (int)cells).ToArray(),
            arrays[0], arrays[1], arrays[2], arrays[3], arrays[4]);
    }

    /// <summary>A field's numbers - see <see cref="FieldFormat"/>.</summary>
    public static byte[] FieldBytes(ShoreField shore)
    {
        ArgumentNullException.ThrowIfNull(shore);
        int cells = shore.Width * shore.Height;
        var bytes = new byte[4 + 8 + 16 + cells + (cells * 5 * 4)];
        "SHF1"u8.CopyTo(bytes);
        var span = bytes.AsSpan();
        BinaryPrimitives.WriteInt32LittleEndian(span[4..], shore.Width);
        BinaryPrimitives.WriteInt32LittleEndian(span[8..], shore.Height);
        BinaryPrimitives.WriteSingleLittleEndian(span[12..], shore.Left);
        BinaryPrimitives.WriteSingleLittleEndian(span[16..], shore.Top);
        BinaryPrimitives.WriteSingleLittleEndian(span[20..], shore.CellSize);
        BinaryPrimitives.WriteSingleLittleEndian(span[24..], shore.Level);
        shore.Kinds.CopyTo(span[28..]);
        int at = 28 + cells;
        ReadOnlySpan<float> shores = shore.Texels, groundShore = shore.GroundTexels;
        for (var cell = 0; cell < cells; cell++, at += 4)
        {
            BinaryPrimitives.WriteSingleLittleEndian(span[at..], shores[cell * 2] * ShoreField.Reach);
        }

        foreach (ShoreField way in (ShoreField[])[shore, shore.Way(true), shore.Way(false, true)])
        {
            ReadOnlySpan<float> texels = way.Texels;
            for (var cell = 0; cell < cells; cell++, at += 4)
            {
                BinaryPrimitives.WriteSingleLittleEndian(span[at..], (1f - texels[(cell * 2) + 1]) * ShoreField.Reach);
            }
        }

        for (var cell = 0; cell < cells; cell++, at += 4)
        {
            BinaryPrimitives.WriteSingleLittleEndian(span[at..], groundShore[cell] * ShoreField.Reach);
        }

        return bytes;
    }

    /// <summary>
    /// A shore's distances as a picture, row nought the least y: land brown by how far in, the walls grey, water blue by how far out,
    /// the shoreline white.
    /// </summary>
    private static GamePicture Distances(ShoreField shore, Func<int, float> distanceOf)
    {
        var rgba = new byte[shore.Width * shore.Height * 4];
        for (var at = 0; at < shore.Width * shore.Height; at++)
        {
            float distance = distanceOf(at);
            (byte r, byte g, byte b) = shore.IsWall(at) ? ((byte)150, (byte)150, (byte)150)
                : MathF.Abs(distance) < 0.01f ? ((byte)255, (byte)255, (byte)255)
                : distance < 0f ? ((byte)(90 + (120 * -distance)), (byte)(60 + (60 * -distance)), (byte)30)
                : ((byte)20, (byte)(60 + (100 * (1f - distance))), (byte)(110 + (145 * (1f - distance))));
            rgba[at * 4] = r;
            rgba[(at * 4) + 1] = g;
            rgba[(at * 4) + 2] = b;
            rgba[(at * 4) + 3] = 255;
        }

        return new GamePicture(shore.Width, shore.Height, rgba);
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
                    : (mask.Length > 0 ? $" · mixed per pixel by {mask}'s green" : " · mixed per pixel by its sub-tiles' corners (a shape across the mask's atlas, CHOSEN)")
                        + (GroundBlend.Heighted(worn) ? ", weighed by their heights where their graphs turn that on (the engine's height blending)" : string.Empty);
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
