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
/// otherwise; the shore's distance is each water cell's distance to the nearest land (exact, the grid's own Euclidean
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

    private ShoreField(
        float left, float top, float cell, int width, int height, float[] texels, float level, int land, int waterTriangles, int groundTriangles,
        int standingTriangles, int standingCells, float[] beach, int blockingTriangles, int blockedCells, int travelled, int unreached, bool[] blocked)
    {
        _blocked = blocked;
        BlockingTriangles = blockingTriangles;
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

    /// <summary>How many water cells stood in the waves' way: something crosses the water's surface there.</summary>
    public int BlockedCells { get; }

    /// <summary>How many water cells took their distance out to sea from the way in, short of the open sea's own.</summary>
    public int Travelled { get; }

    /// <summary>How many water cells no way from the open sea reached - their distance out to sea is the shore's.</summary>
    public int Unreached { get; }

    /// <summary>Whether a cell, row by row, stood in the waves' way: something crosses the water's surface there.</summary>
    public bool InTheWay(int cell) => _blocked[cell];

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
    /// <param name="blocking">Whether a shape may stand in the waves' way - in it where it crosses the water's surface, its top above and its bottom under; null for none.</param>
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
        var lands = 0;
        var standingCells = 0;
        for (var at = 0; at < land.Length; at++)
        {
            float surface = float.IsNaN(waterTop[at]) ? level : waterTop[at];
            land[at] = !float.IsNaN(groundTop[at]) && groundTop[at] < surface - 1f;
            if (!land[at] && standingTop is not null && !float.IsNaN(standingTop[at]) && standingTop[at] < surface - 1f)
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

        // IN THE WAVES' WAY: a water cell something crosses the surface of, its top above the water and its bottom under.
        var blocked = new bool[land.Length];
        int blockingTriangles = 0, blockedCells = 0;
        if (blocking is not null)
        {
            (float[] top, float[] bottom) = Spanned(mesh, blocking, least, cell, width, height, out blockingTriangles);
            for (var at = 0; at < blocked.Length; at++)
            {
                float surface = float.IsNaN(waterTop[at]) ? level : waterTop[at];
                blocked[at] = !land[at] && !float.IsNaN(top[at]) && top[at] < surface - 1f && bottom[at] > surface + 1f;
                blockedCells += blocked[at] ? 1 : 0;
            }
        }

        var shores = new float[land.Length];
        for (var at = 0; at < land.Length; at++)
        {
            shores[at] = (land[at] ? -toWater[at] : toLand[at]) * cell;
        }

        float[] oceans = Oceans(shores, land, blocked, width, height, cell, out int travelled, out int unreached);
        var texels = new float[width * height * 2];
        for (var at = 0; at < land.Length; at++)
        {
            texels[at * 2] = Math.Clamp(shores[at] / Reach, -1f, 1f);
            texels[(at * 2) + 1] = 1f - Math.Clamp(oceans[at] / Reach, -1f, 1f);
        }

        return new ShoreField(
            least.X, least.Y, cell, width, height, texels, level, lands, waterTriangles, groundTriangles, standingTriangles, standingCells, beach,
            blockingTriangles, blockedCells, travelled, unreached, blocked);
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

    /// <summary>The least and the most z of a set of shapes at each cell's middle, NaN where none covers it - its top and its bottom.</summary>
    private static (float[] Top, float[] Bottom) Spanned(SkinnedMesh mesh, Func<int, bool> wanted, Vector2 least, float cell, int width, int height, out int triangles)
    {
        var top = new float[width * height];
        var bottom = new float[width * height];
        Array.Fill(top, float.NaN);
        Array.Fill(bottom, float.NaN);
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
                Laid(top, a, b, c, least, cell, width, height, bottom);
                triangles++;
            }
        }

        return (top, bottom);
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

    /// <summary>One triangle's least z written into the cells whose middles it covers - and its most into <paramref name="bottom"/> where one is given.</summary>
    private static void Laid(float[] top, Vector3 a, Vector3 b, Vector3 c, Vector2 least, float cell, int width, int height, float[]? bottom = null)
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

                if (bottom is not null && (float.IsNaN(bottom[at]) || z > bottom[at]))
                {
                    bottom[at] = z;
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
