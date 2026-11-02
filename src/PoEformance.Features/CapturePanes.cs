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

    /// <summary>
    /// The programs the pane draws each shape with - the flat or the glossy list, swapped for their spray twins where <see cref="Light"/> asks -
    /// or null where it draws none.
    /// </summary>
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

    /// <summary>The water the pane draws with - the area's, over the room's shore (its way in <see cref="Light"/>'s) and the river's flow.</summary>
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

    /// <summary>
    /// What the card holds of the particles the pane runs, read back as the key went down - each emitter's record and globals as last written,
    /// its pool member by member (ModelGpu.ParticleState) - or empty where it runs none.
    /// </summary>
    public IReadOnlyList<string> Particles { get; init; } = [];
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

            foreach (string line in pane.Particles)
            {
                body.Append("  particles\t").AppendLine(line);
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

        // ONE COMPILE CACHE FOR BOTH SECTIONS: the materials every pane wears are compiled for the first, and the swaps' are among them.
        var paints = new MonsterModels.Paints();
        Materials(body, read, panes, paints);
        string swapped = Swaps(body, read, panes, paints);

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
            .AppendLine(swapped.Length > 0 ? ", " + swapped : string.Empty);
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

    /// <summary>Most side of a probed texture's picture - CHOSEN: an atlas of 1024 x 2048 drawn at 512 x 1024 still shows a part's region.</summary>
    private const int ProbeTextureSide = 1024;

    /// <summary>
    /// The textures the materials under each pane's last probe click read, each as two pictures beside the part - its colour as stored,
    /// opaque, with the probed shapes' triangles drawn over it in magenta where their coordinates lie, and its alpha as grey - and a line
    /// for each saying what it is. So what a surface's texture paints where the probe stood, and how opaque it makes it, is read off the
    /// files rather than guessed from the picture.
    /// </summary>
    /// <remarks>
    /// WRITTEN FOR THE EXILE'S STAFF'S LANTERN (v0.1.454): the game shows a bright yellow-white core through its glass where the Monster
    /// Book draws the glass black, and its material (WandererStaff_normalc.mat: PBRMetallicOpacity over the staff's colour atlas, the
    /// alpha times 4 by TWEAK_Transparency) says nothing of what the atlas holds there.
    /// </remarks>
    public static (IReadOnlyList<(string File, GamePicture Picture)> Pictures, IReadOnlyList<string> Lines) ProbeTextures(
        Func<string, byte[]?>? read, IReadOnlyList<CapturePane> panes)
    {
        ArgumentNullException.ThrowIfNull(panes);
        var pictures = new List<(string, GamePicture)>();
        var lines = new List<string>();
        if (read is null)
        {
            return (pictures, lines);
        }

        var paints = new MonsterModels.Paints();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (CapturePane pane in panes)
        {
            MonsterModel model = pane.Model;
            foreach (IGrouping<string, int> material in pane.Probed
                .Where(shape => MaterialOf(model, shape) is { Length: > 0 })
                .GroupBy(shape => MaterialOf(model, shape)!, StringComparer.OrdinalIgnoreCase))
            {
                (ShadeCompile compiled, _) = paints.Compiled(read, material.Key.Replace('\\', '/').Trim());
                if (compiled.Program is not { Plain: < 0 } program)
                {
                    lines.Add($"{material.Key}: no program to read textures of");
                    continue;
                }

                int[] shapes = [.. material.Distinct()];
                foreach (ShadeTexture texture in program.Textures.Concat(program.Glossy?.Textures ?? []).DistinctBy(one => one.Path, StringComparer.OrdinalIgnoreCase))
                {
                    if (!written.Add(material.Key + "|" + texture.Path))
                    {
                        continue;
                    }

                    if (GameArt.Decode(GameArt.ReadRaw(read, texture.Path)) is not { Ready: true } picture)
                    {
                        lines.Add($"{material.Key}: {texture.Path} did not decode");
                        continue;
                    }

                    string stem = string.Create(CultureInfo.InvariantCulture, $"probe-texture-{(pictures.Count / 2) + 1}-{Path.GetFileNameWithoutExtension(texture.Path)}");
                    (GamePicture colour, GamePicture alpha, string coverage) = Probed(picture, model, shapes);
                    pictures.Add((stem + ".png", colour));
                    pictures.Add((stem + "-alpha.png", alpha));
                    lines.Add(string.Create(CultureInfo.InvariantCulture,
                        $"{material.Key}: {texture.Path}{(texture.Srgb ? " (sRGB)" : string.Empty)}, {picture.Width} x {picture.Height} -> {stem}.png and {stem}-alpha.png at {colour.Width} x {colour.Height}; shapes {string.Join(", ", shapes)} {coverage}"));
                }
            }
        }

        return (pictures, lines);
    }

    /// <summary>
    /// A texture shrunk to <see cref="ProbeTextureSide"/>, its colour opaque with the shapes' triangles drawn over it, its alpha as grey, and
    /// what the shapes' coordinates cover of it - the box and the texels under them, the mean colour and alpha there.
    /// </summary>
    private static (GamePicture Colour, GamePicture Alpha, string Coverage) Probed(GamePicture picture, MonsterModel model, int[] shapes)
    {
        var step = 1;
        while (picture.Width / step > ProbeTextureSide || picture.Height / step > ProbeTextureSide)
        {
            step *= 2;
        }

        int width = Math.Max(1, picture.Width / step), height = Math.Max(1, picture.Height / step);
        var colour = new byte[width * height * 4];
        var alpha = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int from = (((y * step) * picture.Width) + (x * step)) * 4;
                int to = ((y * width) + x) * 4;
                colour[to] = picture.Rgba[from];
                colour[to + 1] = picture.Rgba[from + 1];
                colour[to + 2] = picture.Rgba[from + 2];
                colour[to + 3] = 255;
                byte a = picture.Rgba[from + 3];
                alpha[to] = a;
                alpha[to + 1] = a;
                alpha[to + 2] = a;
                alpha[to + 3] = 255;
            }
        }

        // THE SHAPES' TRIANGLES IN THE TEXTURE'S COORDINATES, wrapped into it as the sampler wraps them, and the texels they cover counted
        // at the texture's own size.
        Vector2[] uv = model.Mesh.Coordinates;
        int[] indices = model.Mesh.Indices;
        float leastU = float.MaxValue, leastV = float.MaxValue, mostU = float.MinValue, mostV = float.MinValue;
        long covered = 0;
        double red = 0, green = 0, blue = 0, opacity = 0;
        var counted = new HashSet<int>();
        foreach (int shape in shapes)
        {
            if (shape < 0 || shape >= model.Mesh.Shapes.Count || uv.Length == 0)
            {
                continue;
            }

            MeshShape part = model.Mesh.Shapes[shape];
            for (int at = part.From; at + 2 < part.From + part.Count && at + 2 < indices.Length; at += 3)
            {
                Vector2 a = Wrapped(uv, indices[at]), b = Wrapped(uv, indices[at + 1]), c = Wrapped(uv, indices[at + 2]);
                foreach (Vector2 one in (ReadOnlySpan<Vector2>)[a, b, c])
                {
                    leastU = MathF.Min(leastU, one.X);
                    leastV = MathF.Min(leastV, one.Y);
                    mostU = MathF.Max(mostU, one.X);
                    mostV = MathF.Max(mostV, one.Y);
                }

                Line(colour, width, height, a, b);
                Line(colour, width, height, b, c);
                Line(colour, width, height, c, a);
                Cover(picture, a, b, c, counted, ref covered, ref red, ref green, ref blue, ref opacity);
            }
        }

        string coverage = covered == 0
            ? "cover no texel of it"
            : string.Create(CultureInfo.InvariantCulture,
                $"cover u {leastU:F3}..{mostU:F3}, v {leastV:F3}..{mostV:F3} - {covered} texels, mean colour ({red / covered:F0}, {green / covered:F0}, {blue / covered:F0}) as stored and alpha {opacity / covered:F0} of 255 there");
        return (new GamePicture(width, height, colour), new GamePicture(width, height, alpha), coverage);
    }

    /// <summary>A vertex's coordinates wrapped into nought to one, as the sampler wraps them.</summary>
    private static Vector2 Wrapped(Vector2[] uv, int vertex)
        => vertex >= 0 && vertex < uv.Length ? new Vector2(uv[vertex].X - MathF.Floor(uv[vertex].X), uv[vertex].Y - MathF.Floor(uv[vertex].Y)) : Vector2.Zero;

    /// <summary>A magenta line between two places in a texture's coordinates.</summary>
    private static void Line(byte[] rgba, int width, int height, Vector2 from, Vector2 to)
    {
        float x0 = from.X * (width - 1), y0 = from.Y * (height - 1), x1 = to.X * (width - 1), y1 = to.Y * (height - 1);
        int steps = Math.Clamp((int)MathF.Ceiling(MathF.Max(MathF.Abs(x1 - x0), MathF.Abs(y1 - y0))), 1, 4096);
        for (var s = 0; s <= steps; s++)
        {
            float t = s / (float)steps;
            int x = (int)MathF.Round(x0 + ((x1 - x0) * t)), y = (int)MathF.Round(y0 + ((y1 - y0) * t));
            if (x >= 0 && x < width && y >= 0 && y < height)
            {
                int at = ((y * width) + x) * 4;
                rgba[at] = 255;
                rgba[at + 1] = 0;
                rgba[at + 2] = 255;
            }
        }
    }

    /// <summary>The texels inside a triangle in a texture's coordinates, each counted once, their colour and alpha summed.</summary>
    private static void Cover(
        GamePicture picture, Vector2 a, Vector2 b, Vector2 c, HashSet<int> counted, ref long covered, ref double red, ref double green, ref double blue, ref double opacity)
    {
        float ax = a.X * picture.Width, ay = a.Y * picture.Height, bx = b.X * picture.Width, by = b.Y * picture.Height, cx = c.X * picture.Width, cy = c.Y * picture.Height;
        int left = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx)))), right = Math.Min(picture.Width - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
        int top = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy)))), bottom = Math.Min(picture.Height - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));
        float area = ((bx - ax) * (cy - ay)) - ((cx - ax) * (by - ay));
        if (MathF.Abs(area) < 1e-6f || (long)(right - left + 1) * (bottom - top + 1) > 4_000_000)
        {
            return;
        }

        for (int y = top; y <= bottom; y++)
        {
            for (int x = left; x <= right; x++)
            {
                float px = x + 0.5f, py = y + 0.5f;
                float w0 = ((bx - px) * (cy - py)) - ((cx - px) * (by - py));
                float w1 = ((cx - px) * (ay - py)) - ((ax - px) * (cy - py));
                float w2 = ((ax - px) * (by - py)) - ((bx - px) * (ay - py));
                bool inside = area > 0 ? w0 >= 0 && w1 >= 0 && w2 >= 0 : w0 <= 0 && w1 <= 0 && w2 <= 0;
                int texel = (y * picture.Width) + x;
                if (!inside || !counted.Add(texel))
                {
                    continue;
                }

                int at = texel * 4;
                covered++;
                red += picture.Rgba[at];
                green += picture.Rgba[at + 1];
                blue += picture.Rgba[at + 2];
                opacity += picture.Rgba[at + 3];
            }
        }
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
    private static void Materials(StringBuilder body, Func<string, byte[]?>? read, IReadOnlyList<CapturePane> panes, MonsterModels.Paints paints)
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
            Whole(body, read, material, why, paints, graphs, seen);
        }

        CaptureMaterials.Graphs(read, graphs, "a graph these materials are instances of", body);
    }

    /// <summary>
    /// One material whole under its heading - the file, what it compiles to and how it is drawn - its graphs added to <paramref name="graphs"/>
    /// to be written once after.
    /// </summary>
    private static void Whole(
        StringBuilder body, Func<string, byte[]?> read, string material, string why, MonsterModels.Paints paints, List<string> graphs, HashSet<string> seen)
    {
        body.AppendLine().Append("##### ").Append(material).Append("  (").Append(why).AppendLine(")");
        if (!CaptureMaterials.Material(read, material, paints, body, graphs, seen))
        {
            return;
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

    /// <summary>
    /// Most swap pairs whose two materials are written whole - CHOSEN: the masters of the committed captures swap 2 to 97 materials each
    /// (Augury 78, Marrow 66, Traitor's Passage 97), and two materials with their graphs run to thousands of lines.
    /// </summary>
    private const int MostSwapsWhole = 12;

    /// <summary>What a list of the swap's losses or gains says when it holds none.</summary>
    private const string NoChange = "nothing";

    /// <summary>
    /// The master's material swaps each room and stage shown wears, no probe click needed: for a picture made under a master that swaps
    /// any, every pair whose material it wears with the shapes and triangles wearing it, most first, and how many of the list it does not
    /// wear; each such pair's two materials as compiled, side by side, and what the swap loses and gains - and where it changes anything,
    /// both materials whole. Returns what it came to for the part's first line, or empty where no pane shows a room or a stage.
    /// </summary>
    /// <remarks>
    /// WRITTEN FOR PORT'S QUAY RUNOFF: the water running down the quay's face (ShadeProgram.Occluded, "spray from occlusion") went from
    /// the Monster Book's stage when, from v0.1.344, a laid or built room put its master's TileMaterialOverrides on its tiles - Port's
    /// material_overrides.tmo swaps the quay's MSHD_GraveBrickTile01c.mat, OceanSprayFoam over TRO_MossBlend and the very material the
    /// twin is built for, for MPRT_GraveBrickTile01c.mat. Nothing in a capture said what a swap does to the material it puts in place, and
    /// it went unseen for thirty versions.
    ///
    /// AS COMPILED, BEFORE ANY TEXTURE IS READ: whether a material sprays, reads or casts the wetness, is the engine's water or sets its
    /// own alpha is its graphs', and the compile reads no texture - so a pair costs two compiles at most, through the cache the materials
    /// section filled, which holds the worn one already. Only a pair written whole reads its textures, and says whether they read as the
    /// materials section does.
    ///
    /// WORN, NOT SWAPPED: the model keeps what its shapes wear, not what they wore, so a shape wearing a swap's material of its own counts
    /// under that pair too, and under every pair putting the same material in place - said as "wearing", never as "swapped".
    /// </remarks>
    private static string Swaps(StringBuilder body, Func<string, byte[]?>? read, IReadOnlyList<CapturePane> panes, MonsterModels.Paints paints)
    {
        // EVERY ROOM AND STAGE, a master swapping anything there or not: "under no master" rules a swap out as plainly as a pair rules one in.
        // A STAGE THAT STANDS, not one asked for: a monster whose stage did not build still names it (StagedModels.Of, "no stage: ...") and
        // stands on nothing, and "made under no master" said of him would read as the answer of the stage he asked for - Port's laid harbour,
        // say, once the player has left the area it was laid in.
        CapturePane[] shown = [.. panes.Where(pane => pane.Model.MasterSwaps.Count > 0 || pane.Model.MasterFile.Length > 0
            || pane.Model.Kind == ModelKind.Room || pane.Model.Staged)];
        if (shown.Length == 0)
        {
            return string.Empty;
        }

        body.AppendLine().Append(CultureInfo.InvariantCulture,
                $"=== the master's material swaps each stage wears ({shown.Count(pane => pane.Model.MasterSwaps.Count > 0)} of {shown.Length} rooms and stages shown made under a master that swaps materials)")
            .AppendLine()
            .AppendLine("  every pair whose material a picture wears, with the shapes and triangles wearing it - swapped there or wearing it of its own, the model keeping what its shapes wear and not what they wore - then its two materials as compiled, before any texture is read, and what the swap loses and gains");

        var worn = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var changed = new List<(string From, string To, string Master, string Lost, string Gained, string WornBy)>();
        foreach (CapturePane pane in shown)
        {
            MonsterModel model = pane.Model;
            body.AppendLine().Append("  the ").Append(pane.Book).Append(" book's pane\t");
            if (model.MasterSwaps.Count == 0)
            {
                body.AppendLine(model.MasterFile.Length > 0
                    ? $"made under {model.MasterFile}, whose TileMaterialOverrides swap nothing - every material its own"
                    : "made under no master - every material its own");
                continue;
            }

            string master = model.MasterFile.Length > 0 ? model.MasterFile : "a master the model does not name";
            List<(string From, string To, int Shapes, long Triangles)> pairs = Worn(model);
            body.Append(CultureInfo.InvariantCulture,
                $"made under {master}, whose TileMaterialOverrides swap {model.MasterSwaps.Count} materials: {pairs.Count} of them worn here, {model.MasterSwaps.Count - pairs.Count} not").AppendLine();
            foreach ((string from, string to, int shapes, long triangles) in pairs)
            {
                worn.Add(from + "\n" + to);
                body.Append(CultureInfo.InvariantCulture, $"    {shapes} shapes, {triangles} triangles\twearing the swap's material {to} - the master swaps {from} for it").AppendLine();
                if (read is null)
                {
                    body.AppendLine("      compiled\t(no install to read them from)");
                    continue;
                }

                (List<string> before, string beforeSaid) = Able(read, paints, from);
                (List<string> after, string afterSaid) = Able(read, paints, to);
                string lost = Listed([.. before.Except(after, StringComparer.Ordinal)]);
                string gained = Listed([.. after.Except(before, StringComparer.Ordinal)]);
                body.Append("      compiled\t").Append(Path.GetFileName(from)).Append(": ").Append(beforeSaid)
                    .Append(" · ").Append(Path.GetFileName(to)).Append(": ").Append(afterSaid)
                    .Append(" · LOST: ").Append(lost).Append(" · GAINED: ").AppendLine(gained);
                if ((lost != NoChange || gained != NoChange) && !changed.Exists(one => Same(one.From, from) && Same(one.To, to)))
                {
                    changed.Add((from, to, master, lost, gained,
                        string.Create(CultureInfo.InvariantCulture, $"worn by {shapes} shapes, {triangles} triangles in the {pane.Book} book's pane")));
                }
            }
        }

        if (read is null)
        {
            return string.Create(CultureInfo.InvariantCulture, $"{worn.Count} pairs of the masters' material swaps worn, not compiled - no install to read them from");
        }

        if (changed.Count > 0)
        {
            body.AppendLine().Append(CultureInfo.InvariantCulture,
                    $"=== the two materials of each swap that changes what its material can do, whole ({Math.Min(changed.Count, MostSwapsWhole)} of {changed.Count} pairs - at most {MostSwapsWhole}, CHOSEN - the rest a line each), then their graphs once each")
                .AppendLine();
            var graphs = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach ((string from, string to, string master, string lost, string gained, string wornBy) in changed.Take(MostSwapsWhole))
            {
                if (written.Add(from))
                {
                    Whole(body, read, from, $"the master {master} swaps it for {Path.GetFileName(to)} · LOST: {lost} · GAINED: {gained}", paints, graphs, seen);
                }

                if (written.Add(to))
                {
                    Whole(body, read, to, $"the swap's material in place of {Path.GetFileName(from)}, {wornBy}", paints, graphs, seen);
                }
            }

            if (changed.Count > MostSwapsWhole)
            {
                body.AppendLine();
                foreach ((string from, string to, _, string lost, string gained, _) in changed.Skip(MostSwapsWhole))
                {
                    body.Append("  not written whole\t").Append(from).Append(" swapped for ").Append(to)
                        .Append(" · LOST: ").Append(lost).Append(" · GAINED: ").AppendLine(gained);
                }
            }

            CaptureMaterials.Graphs(read, graphs, "a graph these swapped materials are instances of", body);
        }

        return string.Create(CultureInfo.InvariantCulture, $"{worn.Count} pairs of the masters' material swaps worn, {changed.Count} of them changing what their material can do");
    }

    private static bool Same(string one, string other) => string.Equals(one, other, StringComparison.OrdinalIgnoreCase);

    private static string Listed(IReadOnlyList<string> some) => some.Count > 0 ? string.Join(", ", some) : NoChange;

    /// <summary>
    /// The pairs of a model's master swaps whose material it wears, each with the shapes and triangles wearing it - most triangles first.
    /// </summary>
    /// <remarks>
    /// A SHAPE LEFT WITH NO TRIANGLE WEARS NOTHING - every one of them hidden from the game's camera (LaidRoomModels) - and two grounds
    /// mixed are the two materials they are, the name they are worn under being no file (GroundBlend), as the materials section has them.
    /// </remarks>
    private static List<(string From, string To, int Shapes, long Triangles)> Worn(MonsterModel model)
    {
        var wearing = new Dictionary<string, (int Shapes, long Triangles)>(StringComparer.OrdinalIgnoreCase);
        for (var shape = 0; shape < model.Mesh.Shapes.Count; shape++)
        {
            long triangles = model.Mesh.Shapes[shape].Count / 3;
            if (triangles == 0 || MaterialOf(model, shape) is not { Length: > 0 } worn)
            {
                continue;
            }

            string[] materials = GroundBlend.Parts(worn, out string first, out string second, out _) ? [MaterialFile.Bare(first), MaterialFile.Bare(second)] : [worn];
            foreach (string material in materials)
            {
                wearing[material] = wearing.TryGetValue(material, out (int Shapes, long Triangles) had) ? (had.Shapes + 1, had.Triangles + triangles) : (1, triangles);
            }
        }

        var pairs = new List<(string From, string To, int Shapes, long Triangles)>();
        foreach ((string from, string to) in model.MasterSwaps)
        {
            if (wearing.TryGetValue(MaterialFile.Bare(to), out (int Shapes, long Triangles) on))
            {
                pairs.Add((MaterialFile.Bare(from), MaterialFile.Bare(to), on.Shapes, on.Triangles));
            }
        }

        pairs.Sort((one, other) => one.Triangles != other.Triangles ? other.Triangles.CompareTo(one.Triangles)
            : one.Shapes != other.Shapes ? other.Shapes.CompareTo(one.Shapes)
            : StringComparer.OrdinalIgnoreCase.Compare(one.From, other.From));
        return pairs;
    }

    /// <summary>
    /// What a material can do as compiled, before any texture is read, each in a few words and always in one order - the things a swap may
    /// take away or bring - and the line saying them with what the compile left out.
    /// </summary>
    /// <remarks>
    /// CASTS WETNESS AS THE CARD'S WETNESS PASS TELLS A CASTER (ModelGpu's Casts): the engine's water of any kind, or a program carrying a
    /// wetness to cast (the beach's). The water of every kind is the engine's pass, not its graphs (MonsterModels.Paints' compile), so it is
    /// never "compiled whole" - the compile says it left the graphs out.
    /// </remarks>
    private static (List<string> Can, string Said) Able(Func<string, byte[]?> read, MonsterModels.Paints paints, string material)
    {
        MaterialFile paint = paints.Paint(read, material);
        if (ReferenceEquals(paint, MaterialFile.None))
        {
            return ([], "not in the install, or not a material that reads");
        }

        ShadeCompile compiled = paints.Compiled(read, material).Compiled;
        ShadeProgram? program = compiled.Program;
        WaterKind water = ShadeProgram.KindOf(paint.ShaderType);
        var can = new List<string>();
        void Add(bool has, string what)
        {
            if (has)
            {
                can.Add(what);
            }
        }

        string? drawn = water switch
        {
            WaterKind.Ocean => "drawn as the engine's ocean",
            WaterKind.River => "drawn as the engine's river",
            WaterKind.Still => "drawn as the engine's standing water",
            _ => program switch
            {
                null => null,
                { Plain: >= 0 } => "drawn from a plain sheet",
                _ => "drawn from its program",
            },
        };
        Add(drawn is not null, drawn ?? string.Empty);
        Add(program is { Sprays: true }, "sprays");
        Add(program?.Occluded is not null, "spray from occlusion");
        Add(program is { ReadsWetness: true }, "reads wetness");
        Add(water != WaterKind.None || program?.Wetness is not null, "casts wetness");
        Add(program is { HasAlpha: true }, "sets its own alpha");
        Add(water == WaterKind.None && program is not null && compiled.Skipped.Count == 0, "compiled whole");

        // NOTHING WITHOUT A PROGRAM: every other word asks one, or is the water's, which always has one.
        string said = (can.Count > 0 ? string.Join(", ", can) : "no program - drawn from the texture the walk found, or plain")
            + (compiled.Skipped.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $"; the compile left out {compiled.Skipped.Count}") : string.Empty)
            + (compiled.Lost.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $"; lost {compiled.Lost.Count} on the other channels") : string.Empty);
        return (can, said);
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
