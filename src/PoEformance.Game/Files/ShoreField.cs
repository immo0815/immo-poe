using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// How far each point of a room's water lies from its shore, as the engine's ocean reads it - the shoreline field
/// (<c>shoreline_coord_tex_sampler</c>) made here from the room's own ground, CHOSEN, since the engine makes its own at run time.
/// </summary>
/// <remarks>
/// WHAT THE ENGINE'S HOLDS (water.ffx's GetShorePhase): one less the shore's distance, the length along the shore, the
/// distance out to sea and the shore's strength, in "colour units" - a distance times <c>dist_to_color</c> - over a box of the
/// area (<c>shoreline_coord_planar_aabb</c>). No file holds it and no shader writes it: the engine builds it, from settings the
/// area's .env carries (<c>shoreline_offset</c>, <c>shoreline_resolution</c>) and from what no file read says.
///
/// MADE HERE, and every part of it chosen: a grid of <see cref="CellSize"/> over the water and a margin; a cell is LAND where
/// the room's ground rises above the water standing over it (or over the room's water level where no plane covers it), WATER
/// otherwise; the shore's distance is each water cell's distance to the nearest land that is shore and not a wall (exact, the grid's own Euclidean
/// transform), negative on land by its distance to the water; a distance of <see cref="Reach"/> is one colour unit, so the
/// swell's 25 periods over 2 pi put its crests <see cref="Reach"/> / 3.98 apart - 500, the beach waves' own length in
/// effects.ffx; the length along the shore is nought (the swell's angle is nought where a file sets none) and the strength one,
/// a beach - a rocky shore (negative, the clashing state) is not told apart.
///
/// THE DISTANCE OUT TO SEA IS THE WAVES' WAY IN (v0.1.295, asked for from the live client with four in-game videos). Until then it
/// was one less the shore's, so the swell rolled in square to the nearest shore - and in under Port's ship and out of its other
/// side, while the game's crests run along the channel between the hull and the quay and bend round the pier's end. The engine
/// keeps the two apart (water.ffx's ShoreData: <c>shore_dist</c> from red, <c>ocean_dist</c> from blue) and sends its waves down the
/// slope of the second (GetFieldWaveGeom's <c>planar_wave_dir = -ocean_dist.yz</c>, the swell's tide phase alike), made by its
/// <c>OceanMarkupField</c> from a marking of deep water and land no file read holds (water-shore.txt looks for it). CHOSEN until it
/// is read: the distance out to sea is the shore's where the water lies <see cref="Deep"/> or more from land or at the field's edge
/// - the open sea - and inside that, the open sea's less the way a wave travels from it through the water (<see cref="Travelled"/>):
/// the same as the shore's along a plain coast, smaller in a channel the farther in, and bent round what stands in the way. In the
/// way stands the land, and whatever crosses the water's surface (<c>blocking</c> in <see cref="Of"/>) - a hull, a post, a rock -
/// but not a bridge or a gangway over it.
///
/// ONE THING CROSSES, NOT TWO TOGETHER (v0.1.298, a bar "in the way" along Port's quay, seen from the live client): a cell stood in
/// the way where anything of what may block stood above the water and anything under it - the least and the most z over every such
/// shape at the cell's middle. Port's wobble core, an invisible plane 360 over the water, lay over the quay's sunken foundation
/// bricks 240 under it, and the two together barred the channel's side by the quay, 125 units wide and the plane's length, the
/// waves' way in breaking off along its edge. Now a triangle stands in the way where it itself runs from above the water to under it,
/// and the cells it does so in are those its WATERLINE runs through - where it meets the water's plane, walked cell by cell, so a
/// sheer wall, which covers no cell's middle, stands in the way all the same, and a hull's waterline closes round it.
///
/// A WALL OUT OF DEEP WATER IS NO SHORE (v0.1.299, CHOSEN from the live client, "the channel by the quay stays white"). The field
/// waves' foam is GetFieldFoamProfile's, which adds <c>saturate(exp(-(shore_dist - 150) / 100)) * 0.7</c> - whole foam within 150
/// units of the shore, whatever the crests do - to a breaking foam of 0.4 to 0.7 where the shore is near (its break phase
/// <c>1 - shore_dist / 5000</c>). Measured off Port's capture (v0.1.296) the channel between the ship and the quay is some 300 to
/// 450 units across, so with the quay as shore the whole of it stood white (said from the live client: "always white"). The game draws it blue-green between crests, and the
/// pier's sheltered side dark to its wall (the in-game videos of v0.1.295) - which that formula allows only where the wall is no
/// shore. So land counts towards the shore's distance where ground lies beside it under no more than <see cref="BeachDeep"/> of
/// water - the beach's own band; land meeting only deeper water is a WALL (<see cref="WallCells"/>). Port's ground under its water
/// lies 55 units deep at most or more than 300 (its capture's beach, 21 to 176 against a level of 125), so any depth between splits
/// it alike. The way in still runs to every land, walls included: the crests come along the channel and up to the quay. What the
/// engine marks as its shore is its OceanMarkupField, made from an int grid ClientTileMap::CalculateMapMarkup fills (the program's
/// strings) - read nowhere yet.
///
/// WHAT STANDS IN THE SEA TAKES THE SAME TEST (v0.1.301). v0.1.299 kept it shore whatever the water beside it, and the channel stood
/// white again (said from the live client, crash on and off): v0.1.300's capture still put the quay's edge at a shore's distance of
/// nought, only 495 cells walls - because Port's quay is faced with spraying stone (<c>MSHD_GraveBrickTile01c</c>, OceanSprayFoam)
/// standing out of the water, while the harbour template's ground there is the quay's top (z 0 to 62) or the floor 375 under
/// (z 500), nothing between (v0.1.296's ground.txt). Rocks on a beach stand over shallow ground and stay shore; a faced quay or
/// pier over deep water is a wall (<see cref="StandingWalls"/>).
///
/// THE WAY IN RUNS BY THE GROUND ALONE (v0.1.307, CHOSEN from the live client over waiting for the engine's markup). A video of the
/// game and the Monster Book side by side at Port's checkpoint by the ship, the foam's brightness read along lines through the channel
/// frame by frame, put the game's front at the hull's side 0.3 to 0.7 s before the quay's and at the gangway's end 0.3 to 0.9 s before
/// the stern's - two recordings alike - while the way round the hull ran it along the channel from the stern in, 1.3 to 2.0 s the other
/// way. The game's waves come from the ship's side: as though nothing standing in the water were in their way, as a markup made from
/// the tile map (ClientTileMap::CalculateMapMarkup) would know nothing of a doodad. The markup itself is not in the terrain object nor
/// one hop beyond it (v0.1.306's ocean-markup.txt: the one grid there of the area's proportions, terrain +0x6D8, is a coarse walkable
/// grid). So the distance out to sea is measured from the ground and runs round the ground alone; what stands in the water stays shore
/// for the shore's distance as before. The way round it as well is this field's twin (<see cref="Way"/>), the light panel's switch.
/// </remarks>
public sealed class ShoreField
{
    /// <summary>The distance that is one colour unit - CHOSEN, see the remarks.</summary>
    public const float Reach = 2000f;

    /// <summary>World units a texel, where the water's box allows - CHOSEN; a box too large for <see cref="MostSide"/> takes coarser ones.</summary>
    public const float FinestCell = 25f;

    /// <summary>The most texels a side.</summary>
    public const int MostSide = 512;

    /// <summary>How far past the water the field reaches, in world units - so a shore just outside the water's planes still counts.</summary>
    public const float Margin = 600f;

    /// <summary>The engine's step for the field's slopes: forward differences 30 units apart (GetShorePhaseFields).</summary>
    public const float Eps = 30f;

    /// <summary>How far from land the water is the open sea the waves come in from, in world units - CHOSEN, one wave of the engine's field waves (<c>field_wave_length</c>).</summary>
    public const float Deep = 1000f;

    private readonly float[] _texels;

    /// <summary>Each cell's ground where it lies in the beach's band, NaN elsewhere - see <see cref="Beach"/>.</summary>
    private readonly float[] _beach;

    /// <summary>Whether each cell stood in the waves' way - see <see cref="Of"/>.</summary>
    private readonly bool[] _blocked;

    /// <summary>Whether each cell is a wall - land meeting only deep water, no shore - see the remarks.</summary>
    private readonly bool[] _walls;

    /// <summary>The same field with its way in round what stands in the water - see <see cref="Way"/>; null on that field itself.</summary>
    private ShoreField? _round;

    /// <summary>A field's twin: everything the same but the distance out to sea, whose way in runs round what stands in the water.</summary>
    private ShoreField(ShoreField from, float[] texels, int travelled, int unreached)
        : this(
            from.Left, from.Top, from.CellSize, from.Width, from.Height, texels, from.Level, from.LandCells, from.WaterTriangles, from.GroundTriangles,
            from.StandingTriangles, from.StandingCells, from._beach, from.BlockingTriangles, from.CrossingTriangles, from.BlockedCells, travelled,
            unreached, from._blocked, from.CrossingShapes, from._walls, from.WallCells)
    {
        StandingWalls = from.StandingWalls;
        OverlappedCells = from.OverlappedCells;
        MostLayers = from.MostLayers;
        RoundsWhatStands = true;
    }

    /// <summary>Whether this field's way in runs round what stands in the water - the hull, posts, rocks - rather than by the ground alone.</summary>
    public bool RoundsWhatStands { get; }

    /// <summary>
    /// Of the field <see cref="Of"/> made, whose way in runs by the ground alone: itself, or with <paramref name="round"/> its twin whose
    /// way runs round what stands in the water as well - the light panel's switch, so the two can be held against the game.
    /// </summary>
    public ShoreField Way(bool round) => round && _round is not null ? _round : this;

    private ShoreField(
        float left, float top, float cell, int width, int height, float[] texels, float level, int land, int waterTriangles, int groundTriangles,
        int standingTriangles, int standingCells, float[] beach, int blockingTriangles, int crossingTriangles, int blockedCells, int travelled,
        int unreached, bool[] blocked, IReadOnlyDictionary<int, int> crossingShapes, bool[] walls, int wallCells)
    {
        _walls = walls;
        WallCells = wallCells;
        _blocked = blocked;
        BlockingTriangles = blockingTriangles;
        CrossingTriangles = crossingTriangles;
        CrossingShapes = crossingShapes;
        BlockedCells = blockedCells;
        Travelled = travelled;
        Unreached = unreached;
        _beach = beach;
        StandingTriangles = standingTriangles;
        StandingCells = standingCells;
        Left = left;
        Top = top;
        CellSize = cell;
        Width = width;
        Height = height;
        _texels = texels;
        Level = level;
        LandCells = land;
        WaterTriangles = waterTriangles;
        GroundTriangles = groundTriangles;
    }

    /// <summary>The water's level the field was made against where no plane covers a cell - the water's vertices' middle z.</summary>
    public float Level { get; }

    /// <summary>How many cells were land.</summary>
    public int LandCells { get; }

    /// <summary>How many water triangles the field was made from.</summary>
    public int WaterTriangles { get; }

    /// <summary>How many ground triangles.</summary>
    public int GroundTriangles { get; }

    /// <summary>How many triangles of what stands in the sea - the rocks, see <see cref="Of"/>.</summary>
    public int StandingTriangles { get; }

    /// <summary>How many cells were land by what stands in the sea alone, the ground under them being water.</summary>
    public int StandingCells { get; }

    /// <summary>How many triangles of what may stand in the waves' way were asked - see <see cref="Of"/>.</summary>
    public int BlockingTriangles { get; }

    /// <summary>How many of them crossed the water's surface, each laying its waterline in the way.</summary>
    public int CrossingTriangles { get; }

    /// <summary>The shapes that crossed the water's surface, by their index in the mesh, with how many of their triangles did - for the capture's line.</summary>
    public IReadOnlyDictionary<int, int> CrossingShapes { get; }

    /// <summary>How many water cells stood in the waves' way: something crosses the water's surface there.</summary>
    public int BlockedCells { get; }

    /// <summary>How many water cells took their distance out to sea from the way in, short of the open sea's own.</summary>
    public int Travelled { get; }

    /// <summary>How many water cells no way from the open sea reached - their distance out to sea is the shore's.</summary>
    public int Unreached { get; }

    /// <summary>Whether something crosses the water's surface at a cell, row by row - in the waves' way on the field that <see cref="RoundsWhatStands"/>.</summary>
    public bool InTheWay(int cell) => _blocked[cell];

    /// <summary>How many land cells meet the water as a wall - only water deeper than <see cref="BeachDeep"/> beside them - and are no shore.</summary>
    public int WallCells { get; }

    /// <summary>Whether a cell, row by row, is a wall: land meeting only deep water, from which the shore's distance is not measured.</summary>
    public bool IsWall(int cell) => _walls[cell];

    /// <summary>How many of the walls are what stands in the sea - a spraying stone over deep water, as Port's quay is faced.</summary>
    public int StandingWalls { get; private init; }

    /// <summary>How many cells lie under two water shapes or more - where water drawn unlifted blends a layer for each.</summary>
    public int OverlappedCells { get; private init; }

    /// <summary>The most water shapes over one cell.</summary>
    public int MostLayers { get; private init; }

    /// <summary>The box's least x.</summary>
    public float Left { get; }

    /// <summary>The box's least y.</summary>
    public float Top { get; }

    /// <summary>World units a texel.</summary>
    public float CellSize { get; }

    /// <summary>Texels across.</summary>
    public int Width { get; }

    /// <summary>Texels down.</summary>
    public int Height { get; }

    /// <summary>The texels, row by row, two floats each: the shore's distance and the distance out to sea, in colour units - what the card is handed.</summary>
    public ReadOnlySpan<float> Texels => _texels;

    /// <summary>What the program reads of the box: its least x and y, world units a texel, and one - nought in w meaning no field.</summary>
    public Vector4 Register => new(Left, Top, CellSize, 1f);

    /// <summary>
    /// The field at a place: the shore's distance and the distance out to sea - a linear read of the four nearest texels' centres, clamped at the box's edges, as the card's four loads read it.
    /// </summary>
    public void At(Vector2 place, out float shore, out float ocean)
    {
        float sx = ((place.X - Left) / CellSize) - 0.5f, sy = ((place.Y - Top) / CellSize) - 0.5f;
        sx = Math.Clamp(sx, 0f, Width - 1);
        sy = Math.Clamp(sy, 0f, Height - 1);
        int x0 = (int)sx, y0 = (int)sy;
        int x1 = Math.Min(x0 + 1, Width - 1), y1 = Math.Min(y0 + 1, Height - 1);
        float fx = sx - x0, fy = sy - y0;
        float[] texels = _texels;
        int a = ((y0 * Width) + x0) * 2, b = ((y0 * Width) + x1) * 2;
        int c = ((y1 * Width) + x0) * 2, d = ((y1 * Width) + x1) * 2;
        float Mixed(int part)
        {
            float top = texels[a + part] + ((texels[b + part] - texels[a + part]) * fx);
            float bottom = texels[c + part] + ((texels[d + part] - texels[c + part]) * fx);
            return top + ((bottom - top) * fy);
        }

        shore = Mixed(0);
        ocean = Mixed(1);
    }

    /// <summary>
    /// The cell a place falls in, clamped to the field, and what it is: water, land, a wall, or water something crosses the surface of -
    /// for the probe (ProbeSwell), which asks whether a straight edge in the picture is one of the field's.
    /// </summary>
    public string CellAt(Vector2 place, out int x, out int y)
    {
        x = Math.Clamp((int)MathF.Floor((place.X - Left) / CellSize), 0, Width - 1);
        y = Math.Clamp((int)MathF.Floor((place.Y - Top) / CellSize), 0, Height - 1);
        int at = (y * Width) + x;
        return IsWall(at) ? "a wall" : _texels[at * 2] < 0f ? "land" : InTheWay(at) ? "water something crosses the surface of" : "water";
    }

    /// <summary>
    /// The field at a place and the distance out to sea's slope as GetShorePhaseFields gives it: forward differences <see cref="Eps"/> apart, negated.
    /// </summary>
    public void Fields(Vector2 place, out float shore, out float ocean, out Vector2 oceanSlope)
    {
        At(place, out shore, out ocean);
        At(place + new Vector2(Eps, 0f), out _, out float alongX);
        At(place + new Vector2(0f, Eps), out _, out float alongY);
        oceanSlope = -new Vector2(alongX - ocean, alongY - ocean) / Eps;
    }

    /// <summary>
    /// The field of a room's water, or null where the room has no water or no shore - the engine's "absent shoreline", where the swell stands still and the ripples run whole.
    /// </summary>
    /// <param name="mesh">The room joined.</param>
    /// <param name="water">Whether a shape is water.</param>
    /// <param name="ground">Whether a shape is the room's ground.</param>
    /// <param name="standing">Whether a shape stands in the sea and the sea breaks on it - land where it rises above the water, as the ground is; null for none.</param>
    /// <param name="blocking">Whether a shape may stand in the waves' way - in it where one of its triangles crosses the water's surface, along that triangle's waterline; null for none.</param>
    /// <remarks>
    /// WHAT STANDS IN THE SEA (v0.1.236, asked for from the live client: "the waves run on over the rocks in the game, through them
    /// here"): CHOSEN, the shapes whose own material carries the sea's spray (CrashingFoamSurface) - the game's rocks by the sea and
    /// the pier's stones, authored to have the sea crash on them; the ship's timbers carry none, and the game draws no breaking
    /// round its hull. The engine marks its shore in a field it makes itself; which of the room's doodads it counts is read nowhere.
    /// </remarks>
    public static ShoreField? Of(
        SkinnedMesh mesh, Func<int, bool> water, Func<int, bool> ground, Func<int, bool>? standing = null, Func<int, bool>? blocking = null)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        ArgumentNullException.ThrowIfNull(water);
        ArgumentNullException.ThrowIfNull(ground);
        var least = new Vector2(float.MaxValue);
        var most = new Vector2(float.MinValue);
        var levels = new List<float>();
        for (var shape = 0; shape < mesh.Shapes.Count; shape++)
        {
            if (!water(shape))
            {
                continue;
            }

            MeshShape one = mesh.Shapes[shape];
            for (int at = one.From; at < one.From + one.Count && at < mesh.Indices.Length; at++)
            {
                Vector3 place = mesh.Positions[mesh.Indices[at]];
                least = Vector2.Min(least, new Vector2(place.X, place.Y));
                most = Vector2.Max(most, new Vector2(place.X, place.Y));
                levels.Add(place.Z);
            }
        }

        if (levels.Count == 0)
        {
            return null;
        }

        levels.Sort();
        float level = levels[levels.Count / 2];
        least -= new Vector2(Margin);
        most += new Vector2(Margin);
        float cell = MathF.Max(FinestCell, MathF.Max(most.X - least.X, most.Y - least.Y) / MostSide);
        int width = Math.Clamp((int)MathF.Ceiling((most.X - least.X) / cell), 2, MostSide);
        int height = Math.Clamp((int)MathF.Ceiling((most.Y - least.Y) / cell), 2, MostSide);

        // THE HIGHEST WATER AND GROUND AT EACH CELL'S MIDDLE - up is minus z, so the least z.
        float[] waterTop = Covered(mesh, water, least, cell, width, height, out int waterTriangles);
        float[] groundTop = Covered(mesh, ground, least, cell, width, height, out int groundTriangles);
        int standingTriangles = 0;
        float[]? standingTop = standing is null ? null : Covered(mesh, standing, least, cell, width, height, out standingTriangles);
        var land = new bool[width * height];
        var grounded = new bool[land.Length];
        var stands = new bool[land.Length];
        var lands = 0;
        var standingCells = 0;
        for (var at = 0; at < land.Length; at++)
        {
            float surface = float.IsNaN(waterTop[at]) ? level : waterTop[at];
            land[at] = grounded[at] = !float.IsNaN(groundTop[at]) && groundTop[at] < surface - 1f;
            stands[at] = standingTop is not null && !float.IsNaN(standingTop[at]) && standingTop[at] < surface - 1f;
            if (!land[at] && stands[at])
            {
                land[at] = true;
                standingCells++;
            }

            lands += land[at] ? 1 : 0;
        }

        if (lands == 0 || lands == land.Length)
        {
            return null;
        }

        var shoreLand = new bool[land.Length];
        int walls = Walled(land, shoreLand, groundTop, waterTop, level, width, height, out bool[] wall);
        int standingWalls = 0;
        for (var at = 0; at < wall.Length; at++)
        {
            standingWalls += wall[at] && stands[at] ? 1 : 0;
        }

        int overlapped = Layered(mesh, water, least, cell, width, height, out int mostLayers);

        // THE BEACH'S BAND, CHOSEN: the ground from BeachDeep under the water standing over it to BeachHigh above it.
        var beach = new float[land.Length];
        for (var at = 0; at < beach.Length; at++)
        {
            float surface = float.IsNaN(waterTop[at]) ? level : waterTop[at];
            float floor = groundTop[at];
            beach[at] = !float.IsNaN(floor) && floor >= surface - BeachHigh && floor <= surface + BeachDeep ? floor : float.NaN;
        }

        float[] toLand = Distances(land, width, height, true);
        float[] toWater = Distances(land, width, height, false);

        // IN THE WAVES' WAY: a water cell a triangle's waterline runs through - see the remarks.
        var blocked = new bool[land.Length];
        int blockingTriangles = 0, crossingTriangles = 0, blockedCells = 0;
        var crossingShapes = new Dictionary<int, int>();
        if (blocking is not null)
        {
            bool[] crossed = Crossing(
                mesh, blocking, waterTop, level, least, cell, width, height, crossingShapes, out blockingTriangles, out crossingTriangles);
            for (var at = 0; at < blocked.Length; at++)
            {
                blocked[at] = !land[at] && crossed[at];
                blockedCells += blocked[at] ? 1 : 0;
            }
        }

        // THE WAY IN RUNS TO ANY LAND, walls included - the crests run along a channel and up to a quay - while the shore's distance
        // is to the shore alone (see the remarks).
        float[] toShore = walls == 0 ? toLand : Distances(shoreLand, width, height, true);
        var nearLand = new float[land.Length];
        var shores = new float[land.Length];
        for (var at = 0; at < land.Length; at++)
        {
            nearLand[at] = (land[at] ? -toWater[at] : toLand[at]) * cell;
            shores[at] = land[at] ? nearLand[at] : toShore[at] * cell;
        }

        // THE TWO WAYS IN (v0.1.307, see the remarks): by the ground alone, the default - the open sea measured from the ground and
        // nothing but the ground in the way - and round what stands in the water too, as v0.1.295 to v0.1.306 drew it.
        float[] nearGround = nearLand;
        if (standingCells > 0)
        {
            float[] toGround = Distances(grounded, width, height, true), fromGround = Distances(grounded, width, height, false);
            nearGround = new float[land.Length];
            for (var at = 0; at < land.Length; at++)
            {
                nearGround[at] = (grounded[at] ? -fromGround[at] : toGround[at]) * cell;
            }
        }

        // Two shortest-path walks over the whole grid, each on a core of its own.
        float[] byGround = [], round = [];
        int travelled = 0, unreached = 0, roundTravelled = 0, roundUnreached = 0;
        Parallel.Invoke(
            () => byGround = Oceans(nearGround, grounded, new bool[land.Length], width, height, cell, out travelled, out unreached),
            () => round = Oceans(nearLand, land, blocked, width, height, cell, out roundTravelled, out roundUnreached));
        var field = new ShoreField(
            least.X, least.Y, cell, width, height, Packed(shores, byGround), level, lands, waterTriangles, groundTriangles, standingTriangles,
            standingCells, beach, blockingTriangles, crossingTriangles, blockedCells, travelled, unreached, blocked, crossingShapes, wall, walls)
        {
            StandingWalls = standingWalls,
            OverlappedCells = overlapped,
            MostLayers = mostLayers,
        };
        field._round = new ShoreField(field, Packed(shores, round), roundTravelled, roundUnreached);
        return field;
    }

    /// <summary>The texels of a field: the shore's distance and the distance out to sea, each in colour units, two floats a cell.</summary>
    private static float[] Packed(float[] shores, float[] oceans)
    {
        var texels = new float[shores.Length * 2];
        for (var at = 0; at < shores.Length; at++)
        {
            texels[at * 2] = Math.Clamp(shores[at] / Reach, -1f, 1f);
            texels[(at * 2) + 1] = 1f - Math.Clamp(oceans[at] / Reach, -1f, 1f);
        }

        return texels;
    }

    /// <summary>
    /// How many cells lie under two water shapes or more, and the most at one - where water drawn unlifted blends a layer for each.
    /// </summary>
    private static int Layered(SkinnedMesh mesh, Func<int, bool> water, Vector2 least, float cell, int width, int height, out int most)
    {
        var layers = new byte[width * height];
        var last = new int[width * height];
        Array.Fill(last, -1);
        for (var shape = 0; shape < mesh.Shapes.Count; shape++)
        {
            if (!water(shape))
            {
                continue;
            }

            MeshShape one = mesh.Shapes[shape];
            int end = Math.Min(one.From + one.Count, mesh.Indices.Length) - 2;
            for (int at = one.From; at < end; at += 3)
            {
                Vector3 a = mesh.Positions[mesh.Indices[at]], b = mesh.Positions[mesh.Indices[at + 1]], c = mesh.Positions[mesh.Indices[at + 2]];
                Marked(layers, last, shape, a, b, c, least, cell, width, height);
            }
        }

        var overlapped = 0;
        most = 0;
        foreach (byte count in layers)
        {
            overlapped += count >= 2 ? 1 : 0;
            most = Math.Max(most, count);
        }

        return overlapped;
    }

    /// <summary>One triangle counted into the cells whose middles it covers, once a shape a cell.</summary>
    private static void Marked(byte[] layers, int[] last, int shape, Vector3 a, Vector3 b, Vector3 c, Vector2 least, float cell, int width, int height)
    {
        float area = ((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y));
        if (MathF.Abs(area) < 1e-6f)
        {
            return;
        }

        int x0 = Math.Max(0, (int)MathF.Floor(((MathF.Min(a.X, MathF.Min(b.X, c.X)) - least.X) / cell) - 0.5f));
        int x1 = Math.Min(width - 1, (int)MathF.Ceiling(((MathF.Max(a.X, MathF.Max(b.X, c.X)) - least.X) / cell) - 0.5f));
        int y0 = Math.Max(0, (int)MathF.Floor(((MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) - least.Y) / cell) - 0.5f));
        int y1 = Math.Min(height - 1, (int)MathF.Ceiling(((MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) - least.Y) / cell) - 0.5f));
        for (int y = y0; y <= y1; y++)
        {
            float py = least.Y + ((y + 0.5f) * cell);
            for (int x = x0; x <= x1; x++)
            {
                float px = least.X + ((x + 0.5f) * cell);
                float u = (((b.X - px) * (c.Y - py)) - ((c.X - px) * (b.Y - py))) / area;
                float v = (((c.X - px) * (a.Y - py)) - ((a.X - px) * (c.Y - py))) / area;
                int at = (y * width) + x;
                if (u < 0f || v < 0f || 1f - u - v < 0f || last[at] == shape)
                {
                    continue;
                }

                last[at] = shape;
                layers[at] = (byte)Math.Min(255, layers[at] + 1);
            }
        }
    }

    /// <summary>
    /// Which land is shore and which a wall: land is shore where a water cell beside it, of its eight, has ground under it no deeper
    /// than <see cref="BeachDeep"/> - and a wall where it meets the water and that does not hold, what stands in the sea alike. Land
    /// that meets no water is neither. Fills <paramref name="shoreLand"/> and returns the walls' count.
    /// </summary>
    private static int Walled(
        bool[] land, bool[] shoreLand, float[] groundTop, float[] waterTop, float level, int width, int height, out bool[] wall)
    {
        wall = new bool[land.Length];
        var walls = 0;
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int at = (y * width) + x;
                if (!land[at] || shoreLand[at])
                {
                    continue;
                }

                bool meets = false, gentle = false;
                for (int dy = -1; dy <= 1 && !gentle; dy++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx, ny = y + dy;
                        if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= width || ny >= height)
                        {
                            continue;
                        }

                        int next = (ny * width) + nx;
                        if (land[next])
                        {
                            continue;
                        }

                        meets = true;
                        float surface = float.IsNaN(waterTop[next]) ? level : waterTop[next];
                        if (!float.IsNaN(groundTop[next]) && groundTop[next] <= surface + BeachDeep)
                        {
                            gentle = true;
                            break;
                        }
                    }
                }

                shoreLand[at] = gentle;
                wall[at] = meets && !gentle;
                walls += wall[at] ? 1 : 0;
            }
        }

        return walls;
    }

    /// <summary>
    /// Each cell's distance out to sea, world units: the shore's on land and in the open sea - <see cref="Deep"/> or more from land,
    /// or at the field's edge - and inside it the open sea's less the shortest way through the water from it, round what stands in
    /// the way; a cell in the way takes its water neighbours' way without passing it on. See the class's remarks.
    /// </summary>
    /// <remarks>
    /// A SHORTEST PATH OVER THE GRID'S SIXTEEN WAYS (the eight neighbours and the knight's moves, a step never cutting a corner past a
    /// cell in the way): one cell, the square root of two or of five - within two percent of the straight line, where eight ways
    /// alone would draw the crests as octagons, eight percent off.
    /// </remarks>
    private static float[] Oceans(float[] shores, bool[] land, bool[] blocked, int width, int height, float cell, out int travelled, out int unreached)
    {
        int count = shores.Length;
        var way = new float[count];
        Array.Fill(way, float.PositiveInfinity);
        var heap = new CellHeap(count);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int at = (y * width) + x;
                bool edge = x == 0 || y == 0 || x == width - 1 || y == height - 1;
                if (!land[at] && !blocked[at] && (shores[at] >= Deep || edge))
                {
                    // THE WAY IS KEPT AS THE OPEN SEA'S DISTANCE LESS IT, NEGATED, so the shortest path's sums hold: nought at Deep.
                    way[at] = -shores[at];
                    heap.Push(at, way[at]);
                }
            }
        }

        ReadOnlySpan<(int X, int Y, float Cost)> steps =
        [
            (1, 0, 1f), (-1, 0, 1f), (0, 1, 1f), (0, -1, 1f),
            (1, 1, 1.41421356f), (1, -1, 1.41421356f), (-1, 1, 1.41421356f), (-1, -1, 1.41421356f),
            (1, 2, 2.23606798f), (2, 1, 2.23606798f), (-1, 2, 2.23606798f), (-2, 1, 2.23606798f),
            (1, -2, 2.23606798f), (2, -1, 2.23606798f), (-1, -2, 2.23606798f), (-2, -1, 2.23606798f),
        ];
        var done = new bool[count];
        while (heap.Pop(out int at, out float reached))
        {
            if (done[at] || reached > way[at])
            {
                continue;
            }

            done[at] = true;
            if (blocked[at])
            {
                // IN THE WAY: it takes a way, so the field runs on smoothly under the hull, and passes none on.
                continue;
            }

            int x = at % width, y = at / width;
            foreach ((int dx, int dy, float cost) in steps)
            {
                int nx = x + dx, ny = y + dy;
                if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                {
                    continue;
                }

                int next = (ny * width) + nx;
                if (land[next] || done[next] || !Clear(land, blocked, width, x, y, dx, dy))
                {
                    continue;
                }

                float through = reached + (cost * cell);
                if (through < way[next])
                {
                    way[next] = through;
                    heap.Push(next, through);
                }
            }
        }

        var oceans = new float[count];
        travelled = 0;
        unreached = 0;
        for (var at = 0; at < count; at++)
        {
            if (land[at])
            {
                oceans[at] = shores[at];
            }
            else if (float.IsPositiveInfinity(way[at]))
            {
                oceans[at] = shores[at];
                unreached++;
            }
            else
            {
                oceans[at] = -way[at];
                travelled += oceans[at] < shores[at] - (0.5f * cell) ? 1 : 0;
            }
        }

        return oceans;
    }

    /// <summary>Whether a step passes no cell in the way: a diagonal's two sides, a knight's move's two cells beside its line.</summary>
    private static bool Clear(bool[] land, bool[] blocked, int width, int x, int y, int dx, int dy)
    {
        bool Open(int cx, int cy)
        {
            int at = (cy * width) + cx;
            return !land[at] && !blocked[at];
        }

        int ax = Math.Abs(dx), ay = Math.Abs(dy);
        if (ax + ay == 1)
        {
            return true;
        }

        if (ax == 1 && ay == 1)
        {
            return Open(x + dx, y) && Open(x, y + dy);
        }

        // A KNIGHT'S MOVE crosses the two cells beside the middle of its line.
        return ax == 2
            ? Open(x + (dx / 2), y) && Open(x + (dx / 2), y + dy)
            : Open(x, y + (dy / 2)) && Open(x + dx, y + (dy / 2));
    }

    /// <summary>A least-first heap of cells by their way - pushed again rather than moved, the stale ones skipped on the way out.</summary>
    private sealed class CellHeap(int capacity)
    {
        private int[] _cells = new int[Math.Max(16, capacity)];
        private float[] _keys = new float[Math.Max(16, capacity)];
        private int _count;

        public void Push(int cell, float key)
        {
            if (_count == _cells.Length)
            {
                Array.Resize(ref _cells, _count * 2);
                Array.Resize(ref _keys, _count * 2);
            }

            int at = _count++;
            while (at > 0)
            {
                int parent = (at - 1) >> 1;
                if (_keys[parent] <= key)
                {
                    break;
                }

                _cells[at] = _cells[parent];
                _keys[at] = _keys[parent];
                at = parent;
            }

            _cells[at] = cell;
            _keys[at] = key;
        }

        public bool Pop(out int cell, out float key)
        {
            if (_count == 0)
            {
                cell = -1;
                key = 0f;
                return false;
            }

            cell = _cells[0];
            key = _keys[0];
            int last = --_count;
            int lastCell = _cells[last];
            float lastKey = _keys[last];
            int at = 0;
            while (true)
            {
                int child = (at << 1) + 1;
                if (child >= last)
                {
                    break;
                }

                if (child + 1 < last && _keys[child + 1] < _keys[child])
                {
                    child++;
                }

                if (_keys[child] >= lastKey)
                {
                    break;
                }

                _cells[at] = _cells[child];
                _keys[at] = _keys[child];
                at = child;
            }

            _cells[at] = lastCell;
            _keys[at] = lastKey;
            return true;
        }
    }

    /// <summary>
    /// The cells a set of shapes crosses the water's surface in: every triangle whose own z runs from above the water to under it,
    /// its waterline - the segment where it meets the water's plane - walked through each cell it passes.
    /// </summary>
    /// <remarks>
    /// THE WATER'S PLANE AT THE TRIANGLE'S MIDDLE: the highest water over that cell, the room's level where none covers it. A vertex
    /// is under the water where its z is the plane's or more (up is minus z), so a crossing triangle has exactly two edges from one
    /// side to the other, and its waterline runs between their crossings.
    /// </remarks>
    private static bool[] Crossing(
        SkinnedMesh mesh, Func<int, bool> wanted, float[] waterTop, float level, Vector2 least, float cell, int width, int height,
        Dictionary<int, int> shapes, out int triangles, out int crossing)
    {
        var crossed = new bool[width * height];
        triangles = 0;
        crossing = 0;
        for (var shape = 0; shape < mesh.Shapes.Count; shape++)
        {
            if (!wanted(shape))
            {
                continue;
            }

            MeshShape one = mesh.Shapes[shape];
            int end = Math.Min(one.From + one.Count, mesh.Indices.Length) - 2;
            for (int at = one.From; at < end; at += 3)
            {
                Vector3 a = mesh.Positions[mesh.Indices[at]], b = mesh.Positions[mesh.Indices[at + 1]], c = mesh.Positions[mesh.Indices[at + 2]];
                triangles++;
                float high = MathF.Min(a.Z, MathF.Min(b.Z, c.Z)), low = MathF.Max(a.Z, MathF.Max(b.Z, c.Z));
                int mx = (int)MathF.Floor((((a.X + b.X + c.X) / 3f) - least.X) / cell);
                int my = (int)MathF.Floor((((a.Y + b.Y + c.Y) / 3f) - least.Y) / cell);
                float surface = mx >= 0 && my >= 0 && mx < width && my < height && !float.IsNaN(waterTop[(my * width) + mx])
                    ? waterTop[(my * width) + mx]
                    : level;
                if (!(high < surface - 1f && low > surface + 1f))
                {
                    continue;
                }

                crossing++;
                shapes[shape] = shapes.GetValueOrDefault(shape) + 1;
                Vector2 first = default, second = default;
                var ends = 0;
                Meets(a, b, surface, ref first, ref second, ref ends);
                Meets(b, c, surface, ref first, ref second, ref ends);
                Meets(c, a, surface, ref first, ref second, ref ends);
                if (ends == 2)
                {
                    Walked(crossed, first, second, least, cell, width, height);
                }
            }
        }

        return crossed;
    }

    /// <summary>Where an edge from one side of the water's plane to the other meets it - the first or the second end of a waterline.</summary>
    private static void Meets(Vector3 from, Vector3 to, float surface, ref Vector2 first, ref Vector2 second, ref int ends)
    {
        if ((from.Z >= surface) == (to.Z >= surface))
        {
            return;
        }

        float share = (surface - from.Z) / (to.Z - from.Z);
        var meeting = new Vector2(from.X + ((to.X - from.X) * share), from.Y + ((to.Y - from.Y) * share));
        if (ends == 0)
        {
            first = meeting;
        }
        else
        {
            second = meeting;
        }

        ends++;
    }

    /// <summary>Marks every cell a segment passes through, one step along x or y at a time - so a run of them leaves no gap a diagonal step slips through.</summary>
    private static void Walked(bool[] marks, Vector2 from, Vector2 to, Vector2 least, float cell, int width, int height)
    {
        float x0 = (from.X - least.X) / cell, y0 = (from.Y - least.Y) / cell;
        float x1 = (to.X - least.X) / cell, y1 = (to.Y - least.Y) / cell;
        int x = (int)MathF.Floor(x0), y = (int)MathF.Floor(y0);
        int xEnd = (int)MathF.Floor(x1), yEnd = (int)MathF.Floor(y1);
        float dx = x1 - x0, dy = y1 - y0;
        int stepX = dx > 0f ? 1 : -1, stepY = dy > 0f ? 1 : -1;
        float deltaX = dx != 0f ? MathF.Abs(1f / dx) : float.PositiveInfinity;
        float deltaY = dy != 0f ? MathF.Abs(1f / dy) : float.PositiveInfinity;
        float nextX = dx > 0f ? (x + 1 - x0) * deltaX : dx < 0f ? (x0 - x) * deltaX : float.PositiveInfinity;
        float nextY = dy > 0f ? (y + 1 - y0) * deltaY : dy < 0f ? (y0 - y) * deltaY : float.PositiveInfinity;
        int steps = Math.Abs(xEnd - x) + Math.Abs(yEnd - y);
        for (var step = 0; step <= steps; step++)
        {
            if (x >= 0 && y >= 0 && x < width && y < height)
            {
                marks[(y * width) + x] = true;
            }

            if (nextX < nextY)
            {
                nextX += deltaX;
                x += stepX;
            }
            else
            {
                nextY += deltaY;
                y += stepY;
            }
        }
    }

    /// <summary>How far above the water the beach's sheet reaches, in world units - CHOSEN, so nothing is laid on a pier or a quay.</summary>
    public const float BeachHigh = 100f;

    /// <summary>How far under the water it reaches - CHOSEN: under so much the sea's own medium covers it whole (95% at Port's density).</summary>
    public const float BeachDeep = 300f;

    /// <summary>How far above the ground the sheet is laid - CHOSEN, so it passes the depth test over the ground it lies on.</summary>
    public const float BeachRaise = 4f;

    /// <summary>The most a sheet's square may rise from its lowest corner to its highest - CHOSEN: two cells' height, a beach and not a wall's foot.</summary>
    public const float BeachStep = 2f * FinestCell;

    /// <summary>
    /// The sheet the engine's beach waves are drawn on, laid over the room's ground in the beach's band - or null where none of the
    /// ground lies in it.
    /// </summary>
    /// <remarks>
    /// THE ENGINE MAKES ITS OWN MESH FOR THE BEACH WAVES (BreakingWavesVertex is <c>engineonly</c>, its mesh in no file), lifts its
    /// vertices by the waves and keeps a fragment only where it does not lie behind the scene (CustomDepthTest) - so it lies over
    /// the beach, the waves standing up off it and the foam running over the sand. Which mesh is read nowhere, so this one is
    /// CHOSEN (said from the live client, 2026-10-12, between laying it flat at the water and on the ground): a vertex at every
    /// cell's middle whose ground lies from <see cref="BeachDeep"/> under the water to <see cref="BeachHigh"/> above it,
    /// <see cref="BeachRaise"/> over that ground; two triangles to every square of four such cells whose corners rise no more
    /// than <see cref="BeachStep"/>. Rocks and doodads are not ground, so they stand out of it. Up is minus z.
    /// </remarks>
    public SkinnedMesh? Beach()
    {
        int width = Width, height = Height;
        float[] beach = _beach;
        var at = new int[beach.Length];
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var least = new Vector3(float.MaxValue);
        var most = new Vector3(float.MinValue);
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int cell = (y * width) + x;
                float ground = beach[cell];
                if (float.IsNaN(ground))
                {
                    at[cell] = -1;
                    continue;
                }

                // THE GROUND'S SLOPE by the neighbours in the band, central where both are: up is minus z, so (dz/dx, dz/dy, -1).
                float slopeX = Slope(beach, cell, x > 0 ? cell - 1 : -1, x < width - 1 ? cell + 1 : -1);
                float slopeY = Slope(beach, cell, y > 0 ? cell - width : -1, y < height - 1 ? cell + width : -1);
                var place = new Vector3(Left + ((x + 0.5f) * CellSize), Top + ((y + 0.5f) * CellSize), ground - BeachRaise);
                at[cell] = positions.Count;
                positions.Add(place);
                normals.Add(Vector3.Normalize(new Vector3(slopeX / CellSize, slopeY / CellSize, -1f)));
                least = Vector3.Min(least, place);
                most = Vector3.Max(most, place);
            }
        }

        var indices = new List<int>();
        for (var y = 0; y < height - 1; y++)
        {
            for (var x = 0; x < width - 1; x++)
            {
                int a = (y * width) + x, b = a + 1, c = a + width, d = c + 1;
                if (at[a] < 0 || at[b] < 0 || at[c] < 0 || at[d] < 0)
                {
                    continue;
                }

                float low = MathF.Min(MathF.Min(beach[a], beach[b]), MathF.Min(beach[c], beach[d]));
                float high = MathF.Max(MathF.Max(beach[a], beach[b]), MathF.Max(beach[c], beach[d]));
                if (high - low > BeachStep)
                {
                    continue;
                }

                indices.AddRange([at[a], at[b], at[d], at[a], at[d], at[c]]);
            }
        }

        if (indices.Count == 0)
        {
            return null;
        }

        return SkinnedMesh.Of([.. positions], [.. normals], [.. indices], least, most, shapes: [new MeshShape("beach", 0, indices.Count)]);
    }

    /// <summary>The ground's rise a cell along one way, by the neighbours in the band - central where both are, one-sided where one is.</summary>
    private static float Slope(float[] beach, int cell, int before, int after)
    {
        bool hasBefore = before >= 0 && !float.IsNaN(beach[before]), hasAfter = after >= 0 && !float.IsNaN(beach[after]);
        return hasBefore && hasAfter ? (beach[after] - beach[before]) * 0.5f
            : hasAfter ? beach[after] - beach[cell]
            : hasBefore ? beach[cell] - beach[before]
            : 0f;
    }

    /// <summary>The least z of a set of shapes at each cell's middle, NaN where none covers it.</summary>
    private static float[] Covered(SkinnedMesh mesh, Func<int, bool> wanted, Vector2 least, float cell, int width, int height, out int triangles)
    {
        var top = new float[width * height];
        Array.Fill(top, float.NaN);
        triangles = 0;
        for (var shape = 0; shape < mesh.Shapes.Count; shape++)
        {
            if (!wanted(shape))
            {
                continue;
            }

            MeshShape one = mesh.Shapes[shape];
            int end = Math.Min(one.From + one.Count, mesh.Indices.Length) - 2;
            for (int at = one.From; at < end; at += 3)
            {
                Vector3 a = mesh.Positions[mesh.Indices[at]], b = mesh.Positions[mesh.Indices[at + 1]], c = mesh.Positions[mesh.Indices[at + 2]];
                Laid(top, a, b, c, least, cell, width, height);
                triangles++;
            }
        }

        return top;
    }

    /// <summary>One triangle's least z written into the cells whose middles it covers.</summary>
    private static void Laid(float[] top, Vector3 a, Vector3 b, Vector3 c, Vector2 least, float cell, int width, int height)
    {
        float area = ((b.X - a.X) * (c.Y - a.Y)) - ((c.X - a.X) * (b.Y - a.Y));
        if (MathF.Abs(area) < 1e-6f)
        {
            return;
        }

        int x0 = Math.Max(0, (int)MathF.Floor(((MathF.Min(a.X, MathF.Min(b.X, c.X)) - least.X) / cell) - 0.5f));
        int x1 = Math.Min(width - 1, (int)MathF.Ceiling(((MathF.Max(a.X, MathF.Max(b.X, c.X)) - least.X) / cell) - 0.5f));
        int y0 = Math.Max(0, (int)MathF.Floor(((MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) - least.Y) / cell) - 0.5f));
        int y1 = Math.Min(height - 1, (int)MathF.Ceiling(((MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) - least.Y) / cell) - 0.5f));
        for (int y = y0; y <= y1; y++)
        {
            float py = least.Y + ((y + 0.5f) * cell);
            for (int x = x0; x <= x1; x++)
            {
                float px = least.X + ((x + 0.5f) * cell);
                float u = (((b.X - px) * (c.Y - py)) - ((c.X - px) * (b.Y - py))) / area;
                float v = (((c.X - px) * (a.Y - py)) - ((a.X - px) * (c.Y - py))) / area;
                float w = 1f - u - v;
                if (u < 0f || v < 0f || w < 0f)
                {
                    continue;
                }

                float z = (a.Z * u) + (b.Z * v) + (c.Z * w);
                int at = (y * width) + x;
                if (float.IsNaN(top[at]) || z < top[at])
                {
                    top[at] = z;
                }
            }
        }
    }

    /// <summary>Each cell's distance, in cells, to the nearest cell that is land (or, with <paramref name="toLand"/> false, water) - the exact Euclidean transform, by rows then columns.</summary>
    private static float[] Distances(bool[] land, int width, int height, bool toLand)
    {
        const float far = 1e20f;
        var squared = new float[width * height];
        for (var at = 0; at < squared.Length; at++)
        {
            squared[at] = land[at] == toLand ? 0f : far;
        }

        var line = new float[Math.Max(width, height)];
        var done = new float[line.Length];
        var hulls = new int[line.Length];
        var bounds = new float[line.Length + 1];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                line[x] = squared[(y * width) + x];
            }

            Transform(line, width, done, hulls, bounds);
            for (var x = 0; x < width; x++)
            {
                squared[(y * width) + x] = done[x];
            }
        }

        for (var x = 0; x < width; x++)
        {
            for (var y = 0; y < height; y++)
            {
                line[y] = squared[(y * width) + x];
            }

            Transform(line, height, done, hulls, bounds);
            for (var y = 0; y < height; y++)
            {
                squared[(y * width) + x] = done[y];
            }
        }

        var distances = new float[squared.Length];
        for (var at = 0; at < distances.Length; at++)
        {
            distances[at] = MathF.Sqrt(squared[at]);
        }

        return distances;
    }

    /// <summary>One line's squared distance transform - the lower envelope of parabolas (Felzenszwalb and Huttenlocher).</summary>
    private static void Transform(float[] f, int n, float[] d, int[] v, float[] z)
    {
        var k = 0;
        v[0] = 0;
        z[0] = float.NegativeInfinity;
        z[1] = float.PositiveInfinity;
        for (var q = 1; q < n; q++)
        {
            float s = ((f[q] + (q * q)) - (f[v[k]] + (v[k] * v[k]))) / ((2f * q) - (2f * v[k]));
            while (s <= z[k])
            {
                k--;
                s = ((f[q] + (q * q)) - (f[v[k]] + (v[k] * v[k]))) / ((2f * q) - (2f * v[k]));
            }

            k++;
            v[k] = q;
            z[k] = s;
            z[k + 1] = float.PositiveInfinity;
        }

        k = 0;
        for (var q = 0; q < n; q++)
        {
            while (z[k + 1] < q)
            {
                k++;
            }

            d[q] = ((q - v[k]) * (q - v[k])) + f[v[k]];
        }
    }
}
