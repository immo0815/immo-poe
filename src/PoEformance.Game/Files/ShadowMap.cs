using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// How far along the sun's light each point of a mesh is first met - the depth the sun sees - and so whether a point is in its shadow.
/// </summary>
/// <remarks>
/// THE GAME'S OWN WAY OF ANSWERING THE QUESTION: its sun renders a shadow map (shadows.ffx; the
/// directional light's shadow_map_sampling) and a pixel is in shadow where something nearer the sun
/// was drawn at its place in that map. Here the map is the mesh drawn once along the sun's direction,
/// orthographically, over the mesh's whole extent - a room is a few thousand units across, which a
/// map 2048 square covers at a few units a texel.
///
/// BUILT ONCE PER SUN AND MESH, not per frame: turning the picture moves the eye, not the sun, so
/// the canvas keeps the map until the sun or the mesh changes. Drawn on every core, a triangle at a
/// time, each texel's nearest depth kept by a compare-and-swap - floats past nought order as their
/// bits do, so the depths are kept above nought.
///
/// WHAT CASTS: solid and cut-out shapes, a cut-out one through its texture's alpha as the picture cuts
/// it. Translucent shapes do not, as they write no depth in the picture either.
///
/// COARSE WHILE THE SUN MOVES: a map a quarter as wide costs a fraction of the full one, so a sun
/// dragged round the picture keeps up with the hand, and the full map is drawn once it is let go.
///
/// READ WITH FOUR TEXELS AND AN OFFSET ALONG THE NORMAL: one texel's comparison is a hard staircase
/// along every shadow's edge, and a surface tested against its own depth shades itself in stripes
/// ("acne") unless the point is moved off it by about a texel first.
/// </remarks>
public sealed class ShadowMap
{
    /// <summary>The map's usual side, in texels.</summary>
    public const int Usual = 2048;

    /// <summary>The side while the sun is being moved.</summary>
    public const int Coarse = 512;

    private readonly int[] _depth;
    private readonly int _side;
    private readonly Vector3 _u, _v, _w;
    private readonly Vector2 _least;
    private readonly float _perUnit;
    private readonly float _nearest;

    private ShadowMap(int[] depth, int side, Vector3 u, Vector3 v, Vector3 w, Vector2 least, float perUnit, float nearest, Vector3 direction)
    {
        _depth = depth;
        _side = side;
        _u = u;
        _v = v;
        _w = w;
        _least = least;
        _perUnit = perUnit;
        _nearest = nearest;
        Direction = direction;
    }

    /// <summary>The way the light travels that the map was drawn along.</summary>
    public Vector3 Direction { get; }

    /// <summary>How many world units one texel covers.</summary>
    public float Texel => 1f / _perUnit;

    /// <summary>How many texels the map is across.</summary>
    public int Side => _side;

    /// <summary>
    /// The map of a mesh along a direction.
    /// </summary>
    /// <param name="places">Every vertex, model space.</param>
    /// <param name="indices">Three per triangle.</param>
    /// <param name="triangles">How many triangles.</param>
    /// <param name="direction">The way the light travels.</param>
    /// <param name="casts">Whether a triangle casts at all, by its index.</param>
    /// <param name="cutout">For a cut-out triangle, its texture and coordinates; null for a solid one.</param>
    /// <param name="threads">How many threads may draw it.</param>
    /// <param name="side">How many texels across - <see cref="Usual"/> or <see cref="Coarse"/>.</param>
    /// <param name="bounds">The bounds the map is laid over, where they are not the places' own - a pose's hull (PoseHull) - or null.</param>
    internal static ShadowMap? Build(
        Vector3[] places, int[] indices, int triangles, Vector3 direction, Func<int, bool> casts, Func<int, (Mipmaps Skin, Vector2[] Coordinates)?> cutout, int threads,
        int side = Usual, ShadowBounds? bounds = null)
    {
        if (triangles == 0 || (bounds is { } laid ? Framed(laid, direction, side) : Framed(places, direction, side)) is not { } frame)
        {
            return null;
        }

        (Vector3 u, Vector3 v, Vector3 w, Vector2 least, float perUnit, float nearest, _) = frame;
        var depth = new int[side * side];
        Array.Fill(depth, int.MaxValue);
        var corners = new Vector3[places.Length];
        for (var at = 0; at < places.Length; at++)
        {
            Vector3 place = places[at];
            corners[at] = new Vector3(
                (Vector3.Dot(place, u) - least.X) * perUnit,
                (Vector3.Dot(place, v) - least.Y) * perUnit,
                Vector3.Dot(place, w) - nearest);
        }

        Parallel.For(
            0, triangles,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) },
            one =>
            {
                if (casts(one))
                {
                    Draw(new DepthKeep(depth), side, corners, indices, one, cutout(one));
                }
            });

        return new ShadowMap(depth, side, u, v, w, least, perUnit, nearest, w);
    }

    /// <summary>
    /// The map of a mesh in a frame given, keeping at each texel which triangle is nearest the light as well as how near - see <see cref="ShadowOwners"/>.
    /// </summary>
    /// <param name="places">Every vertex, model space.</param>
    /// <param name="indices">Three per triangle.</param>
    /// <param name="triangles">How many triangles.</param>
    /// <param name="frame">Where the map lies - <see cref="Framed(ReadOnlySpan{Vector3}, Vector3, int)"/>'s, so maps of the same mesh drawn by different rules lie on the same texels.</param>
    /// <param name="casts">Whether a triangle casts at all, by its index.</param>
    /// <param name="cutout">For a triangle cut by its texture's alpha, its texture and coordinates; null for a solid one.</param>
    /// <param name="threads">How many threads may draw it.</param>
    /// <remarks>
    /// THE SAME RASTER AS <see cref="Build"/>'s - one Draw, the keeping handed in - so with the same rules its depths are Build's to the bit
    /// (ShadowOwnersTests). A texel's depth and owner are kept as one long, the depth's bits above the triangle's index, so the nearest wins
    /// and a tie goes to the lower index, whichever thread came first.
    /// </remarks>
    public static ShadowOwners? Owned(
        Vector3[] places, int[] indices, int triangles, ShadowFrame frame, Func<int, bool> casts, Func<int, (Mipmaps Skin, Vector2[] Coordinates)?> cutout,
        int threads)
    {
        ArgumentNullException.ThrowIfNull(places);
        ArgumentNullException.ThrowIfNull(indices);
        ArgumentNullException.ThrowIfNull(casts);
        ArgumentNullException.ThrowIfNull(cutout);
        if (triangles == 0 || frame.Side < 16)
        {
            return null;
        }

        int side = frame.Side;
        var keys = new long[side * side];
        Array.Fill(keys, long.MaxValue);
        var corners = new Vector3[places.Length];
        for (var at = 0; at < places.Length; at++)
        {
            Vector3 place = places[at];
            corners[at] = new Vector3(
                (Vector3.Dot(place, frame.U) - frame.Least.X) * frame.PerUnit,
                (Vector3.Dot(place, frame.V) - frame.Least.Y) * frame.PerUnit,
                Vector3.Dot(place, frame.W) - frame.Nearest);
        }

        Parallel.For(
            0, triangles,
            new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, threads) },
            one =>
            {
                if (casts(one))
                {
                    Draw(new OwnerKeep(keys), side, corners, indices, one, cutout(one));
                }
            });

        return new ShadowOwners(keys, frame);
    }

    /// <summary>How a texel keeps what is drawn on it.</summary>
    private interface IKeep
    {
        void Keep(int at, int bits, int one);
    }

    /// <summary>The nearest depth alone - the picture's map.</summary>
    private readonly struct DepthKeep(int[] depth) : IKeep
    {
        public void Keep(int at, int bits, int one)
        {
            int held = Volatile.Read(ref depth[at]);
            while (bits < held)
            {
                int was = Interlocked.CompareExchange(ref depth[at], bits, held);
                if (was == held)
                {
                    break;
                }

                held = was;
            }
        }
    }

    /// <summary>The nearest depth and the triangle that drew it, as one long - see <see cref="Owned"/>.</summary>
    private readonly struct OwnerKeep(long[] keys) : IKeep
    {
        public void Keep(int at, int bits, int one)
        {
            long key = ((long)bits << 32) | (uint)one;
            long held = Volatile.Read(ref keys[at]);
            while (key < held)
            {
                long was = Interlocked.CompareExchange(ref keys[at], key, held);
                if (was == held)
                {
                    break;
                }

                held = was;
            }
        }
    }

    /// <summary>
    /// Where a map of these places along this direction lies: its axes, its corner, how many texels a unit is and the depth it starts at - or null where there is no map to draw.
    /// </summary>
    /// <remarks>
    /// THE ONE FRAME <see cref="Build"/> DRAWS IN, public so a map drawn elsewhere - the graphics
    /// card's - lies on the same texels and is read by the same <see cref="ShadowFrame"/> arithmetic.
    /// </remarks>
    /// <param name="places">Every vertex, model space.</param>
    /// <param name="direction">The way the light travels.</param>
    /// <param name="side">How many texels across.</param>
    public static ShadowFrame? Framed(ReadOnlySpan<Vector3> places, Vector3 direction, int side) =>
        places.Length == 0 ? null : Framed(Bounded(places, direction), direction, side);

    /// <summary>
    /// The frame a map of places with these bounds along this direction lies in - <see cref="Framed(ReadOnlySpan{Vector3}, Vector3, int)"/>'s, from bounds worked out apart.
    /// </summary>
    /// <remarks>
    /// THE SAME FRAME TO THE BIT whichever way the bounds were gathered: they are a least and a most, and
    /// a stage's bounds joined with a monster's (<see cref="ShadowBounds.With"/>) are the bounds of the
    /// two together. So a pose that moves only the monster scans only the monster.
    /// </remarks>
    public static ShadowFrame? Framed(ShadowBounds bounds, Vector3 direction, int side)
    {
        if (!(direction.LengthSquared() > 0f) || side < 16)
        {
            return null;
        }

        (Vector3 u, Vector3 v, Vector3 w) = Axes(direction);
        Vector2 least = bounds.Least;
        float span = MathF.Max(bounds.Most.X - least.X, bounds.Most.Y - least.Y);
        if (!(span > 0f) || !float.IsFinite(span))
        {
            return null;
        }

        // A TEXEL OF MARGIN EACH SIDE, so nothing lands on the edge the reads clamp to.
        float perUnit = (side - 2) / span;
        least -= new Vector2(1f / perUnit);
        return new ShadowFrame(u, v, w, least, perUnit, bounds.Nearest - 1f, side);
    }

    /// <summary>How far some places reach across a map along this direction and how near the light the nearest comes - see <see cref="Framed(ShadowBounds, Vector3, int)"/>.</summary>
    /// <param name="places">The places, model space.</param>
    /// <param name="direction">The way the light travels; any length but nought.</param>
    public static ShadowBounds Bounded(ReadOnlySpan<Vector3> places, Vector3 direction)
    {
        Vector2 least = new(float.MaxValue), most = new(float.MinValue);
        float nearest = float.MaxValue;
        if (!(direction.LengthSquared() > 0f))
        {
            return new ShadowBounds(least, most, nearest);
        }

        (Vector3 u, Vector3 v, Vector3 w) = Axes(direction);
        foreach (Vector3 place in places)
        {
            var at = new Vector2(Vector3.Dot(place, u), Vector3.Dot(place, v));
            least = Vector2.Min(least, at);
            most = Vector2.Max(most, at);
            nearest = MathF.Min(nearest, Vector3.Dot(place, w));
        }

        return new ShadowBounds(least, most, nearest);
    }

    /// <summary>The map's axes: across, up it, and along the light.</summary>
    private static (Vector3 U, Vector3 V, Vector3 W) Axes(Vector3 direction)
    {
        Vector3 w = Vector3.Normalize(direction);
        Vector3 helper = MathF.Abs(w.Z) < 0.9f ? Vector3.UnitZ : Vector3.UnitX;
        Vector3 u = Vector3.Normalize(Vector3.Cross(helper, w));
        return (u, Vector3.Cross(w, u), w);
    }

    /// <summary>How much of the light reaches a point - one in the open, nought in full shadow, between along an edge.</summary>
    /// <param name="place">The point, model space.</param>
    /// <param name="normal">Its normal, unit length - the point is tested a little off its surface.</param>
    public float Lit(Vector3 place, Vector3 normal)
    {
        float texel = 1f / _perUnit;
        Vector3 off = place + (normal * (texel * 1.5f));
        float x = ((Vector3.Dot(off, _u) - _least.X) * _perUnit) - 0.5f;
        float y = ((Vector3.Dot(off, _v) - _least.Y) * _perUnit) - 0.5f;
        float depth = Vector3.Dot(off, _w) - _nearest - (texel * 2f);
        if (x < 0f || y < 0f || x >= _side - 1 || y >= _side - 1)
        {
            return 1f;
        }

        int x0 = (int)x, y0 = (int)y;
        float fx = x - x0, fy = y - y0;
        float a = Open(x0, y0, depth), b = Open(x0 + 1, y0, depth);
        float c = Open(x0, y0 + 1, depth), d = Open(x0 + 1, y0 + 1, depth);
        return float.Lerp(float.Lerp(a, b, fx), float.Lerp(c, d, fx), fy);
    }

    private float Open(int x, int y, float depth)
    {
        int held = _depth[(y * _side) + x];
        return held == int.MaxValue || depth <= BitConverter.Int32BitsToSingle(held) ? 1f : 0f;
    }

    private static void Draw<TKeep>(TKeep keep, int side, Vector3[] corners, int[] indices, int one, (Mipmaps Skin, Vector2[] Coordinates)? cutout)
        where TKeep : struct, IKeep
    {
        int i0 = indices[one * 3], i1 = indices[(one * 3) + 1], i2 = indices[(one * 3) + 2];
        Vector3 c0 = corners[i0], c1 = corners[i1], c2 = corners[i2];
        float area = ((c1.X - c0.X) * (c2.Y - c0.Y)) - ((c1.Y - c0.Y) * (c2.X - c0.X));
        if (!(MathF.Abs(area) >= 1e-6f))
        {
            return;
        }

        float sign = area < 0f ? -1f : 1f;
        float total = area * sign;
        float inv = 1f / total;
        int left = Math.Max(0, (int)MathF.Floor(MathF.Min(c0.X, MathF.Min(c1.X, c2.X))));
        int right = Math.Min(side - 1, (int)MathF.Ceiling(MathF.Max(c0.X, MathF.Max(c1.X, c2.X))));
        int top = Math.Max(0, (int)MathF.Floor(MathF.Min(c0.Y, MathF.Min(c1.Y, c2.Y))));
        int bottom = Math.Min(side - 1, (int)MathF.Ceiling(MathF.Max(c0.Y, MathF.Max(c1.Y, c2.Y))));

        Mipmaps? skin = null;
        Vector2 s0 = default, s1 = default, s2 = default;
        float level = 0f;
        if (cutout is { } cut)
        {
            skin = cut.Skin;
            s0 = cut.Coordinates[i0];
            s1 = cut.Coordinates[i1];
            s2 = cut.Coordinates[i2];
            level = MeshPicture.Level(c0, c1, c2, s0, s1, s2, area, skin);
        }

        for (int y = top; y <= bottom; y++)
        {
            float py = y + 0.5f;
            for (int x = left; x <= right; x++)
            {
                float px = x + 0.5f;
                float w0 = (((c2.X - c1.X) * (py - c1.Y)) - ((c2.Y - c1.Y) * (px - c1.X))) * sign;
                float w1 = (((c0.X - c2.X) * (py - c2.Y)) - ((c0.Y - c2.Y) * (px - c2.X))) * sign;
                float w2 = total - w0 - w1;
                if (w0 < 0f || w1 < 0f || w2 < 0f)
                {
                    continue;
                }

                float first = w0 * inv, second = w1 * inv, third = w2 * inv;
                if (skin is not null
                    && MeshPicture.Sample4(skin, (first * s0) + (second * s1) + (third * s2), level).W < MeshPicture.CutoutAlpha)
                {
                    continue;
                }

                float away = MathF.Max(0f, (first * c0.Z) + (second * c1.Z) + (third * c2.Z));
                keep.Keep((y * side) + x, BitConverter.SingleToInt32Bits(away), one);
            }
        }
    }
}

/// <summary>
/// A sun's shadow map that knows who cast it: at every texel the nearest depth along the light and the triangle that drew it - see <see cref="ShadowMap.Owned"/>.
/// </summary>
/// <remarks>
/// FOR A CAPTURE, NOT A PICTURE: what makes a shadow on the ground is the question (the Red Vale's square crown shadows), and the picture's
/// map keeps depths alone. Read with the picture's own offsets (<see cref="Lit"/>), so a point is in this map's shadow exactly where it is in
/// the picture's.
/// </remarks>
public sealed class ShadowOwners
{
    private readonly long[] _keys;

    internal ShadowOwners(long[] keys, ShadowFrame frame)
    {
        _keys = keys;
        Frame = frame;
    }

    /// <summary>Where the map lies.</summary>
    public ShadowFrame Frame { get; }

    /// <summary>How many texels across.</summary>
    public int Side => Frame.Side;

    /// <summary>How many world units one texel covers.</summary>
    public float Texel => 1f / Frame.PerUnit;

    /// <summary>The triangle nearest the light at a texel, or -1 where nothing is drawn.</summary>
    public int OwnerAt(int x, int y)
    {
        long key = _keys[(y * Frame.Side) + x];
        return key == long.MaxValue ? -1 : (int)(uint)key;
    }

    /// <summary>Its depth past the map's nearest, or +infinity where nothing is drawn.</summary>
    public float DepthAt(int x, int y)
    {
        long key = _keys[(y * Frame.Side) + x];
        return key == long.MaxValue ? float.PositiveInfinity : BitConverter.Int32BitsToSingle((int)(key >> 32));
    }

    /// <summary>Where a point falls on the map, texels from its corner, and its depth past the map's nearest.</summary>
    public Vector3 On(Vector3 place)
        => new(
            (Vector3.Dot(place, Frame.U) - Frame.Least.X) * Frame.PerUnit,
            (Vector3.Dot(place, Frame.V) - Frame.Least.Y) * Frame.PerUnit,
            Vector3.Dot(place, Frame.W) - Frame.Nearest);

    /// <summary>How much of the light reaches a point - <see cref="ShadowMap.Lit"/>'s reading of this map, to the bit.</summary>
    public float Lit(Vector3 place, Vector3 normal) => Read(place, normal, out _);

    /// <summary>What casts the shadow on a point: the triangle nearest the light among the texels that hide it, or -1 where none does.</summary>
    public int Caster(Vector3 place, Vector3 normal)
    {
        Read(place, normal, out int caster);
        return caster;
    }

    private float Read(Vector3 place, Vector3 normal, out int caster)
    {
        caster = -1;
        float texel = 1f / Frame.PerUnit;
        Vector3 off = place + (normal * (texel * 1.5f));
        float x = ((Vector3.Dot(off, Frame.U) - Frame.Least.X) * Frame.PerUnit) - 0.5f;
        float y = ((Vector3.Dot(off, Frame.V) - Frame.Least.Y) * Frame.PerUnit) - 0.5f;
        float depth = Vector3.Dot(off, Frame.W) - Frame.Nearest - (texel * 2f);
        int side = Frame.Side;
        if (x < 0f || y < 0f || x >= side - 1 || y >= side - 1)
        {
            return 1f;
        }

        int x0 = (int)x, y0 = (int)y;
        float fx = x - x0, fy = y - y0;
        long nearest = long.MaxValue;
        float a = Open(x0, y0, depth, ref nearest), b = Open(x0 + 1, y0, depth, ref nearest);
        float c = Open(x0, y0 + 1, depth, ref nearest), d = Open(x0 + 1, y0 + 1, depth, ref nearest);
        caster = nearest == long.MaxValue ? -1 : (int)(uint)nearest;
        return float.Lerp(float.Lerp(a, b, fx), float.Lerp(c, d, fx), fy);
    }

    private float Open(int x, int y, float depth, ref long nearest)
    {
        long key = _keys[(y * Frame.Side) + x];
        if (key == long.MaxValue || depth <= BitConverter.Int32BitsToSingle((int)(key >> 32)))
        {
            return 1f;
        }

        nearest = Math.Min(nearest, key);
        return 0f;
    }
}

/// <summary>Where a shadow map lies - see <see cref="ShadowMap.Framed(ReadOnlySpan{Vector3}, Vector3, int)"/>.</summary>
/// <param name="U">The map's x axis, model space, unit length.</param>
/// <param name="V">Its y axis.</param>
/// <param name="W">The way the light travels - depth grows along it.</param>
/// <param name="Least">The map's corner along <paramref name="U"/> and <paramref name="V"/>, a texel of margin in.</param>
/// <param name="PerUnit">How many texels one unit covers.</param>
/// <param name="Nearest">The depth the map starts at, a unit before the nearest place.</param>
/// <param name="Side">How many texels across.</param>
public readonly record struct ShadowFrame(Vector3 U, Vector3 V, Vector3 W, Vector2 Least, float PerUnit, float Nearest, int Side);

/// <summary>How far some places reach across a shadow map and how near the light they come - see <see cref="ShadowMap.Bounded"/>.</summary>
/// <param name="Least">The least along the map's two axes.</param>
/// <param name="Most">The most along them.</param>
/// <param name="Nearest">The least depth along the light.</param>
public readonly record struct ShadowBounds(Vector2 Least, Vector2 Most, float Nearest)
{
    /// <summary>The bounds of these places and those together.</summary>
    public ShadowBounds With(ShadowBounds other) =>
        new(Vector2.Min(Least, other.Least), Vector2.Max(Most, other.Most), MathF.Min(Nearest, other.Nearest));
}
