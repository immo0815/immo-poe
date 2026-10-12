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
/// effects.ffx; the distance out to sea is one less the shore's, so the swell rolls in square to the nearest shore; the
/// length along the shore is nought (the swell's angle is nought where a file sets none) and the strength one, a beach -
/// a rocky shore (negative, the clashing state) is not told apart.
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

    private readonly float[] _texels;

    private ShoreField(
        float left, float top, float cell, int width, int height, float[] texels, float level, int land, int waterTriangles, int groundTriangles,
        int standingTriangles, int standingCells)
    {
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
    /// <remarks>
    /// WHAT STANDS IN THE SEA (v0.1.236, asked for from the live client: "the waves run on over the rocks in the game, through them
    /// here"): CHOSEN, the shapes whose own material carries the sea's spray (CrashingFoamSurface) - the game's rocks by the sea and
    /// the pier's stones, authored to have the sea crash on them; the ship's timbers carry none, and the game draws no breaking
    /// round its hull. The engine marks its shore in a field it makes itself; which of the room's doodads it counts is read nowhere.
    /// </remarks>
    public static ShoreField? Of(SkinnedMesh mesh, Func<int, bool> water, Func<int, bool> ground, Func<int, bool>? standing = null)
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

        float[] toLand = Distances(land, width, height, true);
        float[] toWater = Distances(land, width, height, false);
        var texels = new float[width * height * 2];
        for (var at = 0; at < land.Length; at++)
        {
            float distance = land[at] ? -toWater[at] : toLand[at];
            float shore = Math.Clamp(distance * cell / Reach, -1f, 1f);
            texels[at * 2] = shore;
            texels[(at * 2) + 1] = 1f - shore;
        }

        return new ShoreField(least.X, least.Y, cell, width, height, texels, level, lands, waterTriangles, groundTriangles, standingTriangles, standingCells);
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
