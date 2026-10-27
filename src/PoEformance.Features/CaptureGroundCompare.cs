using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// Two ways a sub-tile of two grounds may be mixed, each held against the ground the game drew - the capture's measure of what the
/// engine reads where the mix's squares do not show.
/// </summary>
/// <remarks>
/// WHY TWO CANDIDATES (v0.1.264). The tool mixes two grounds by the template mask's green at the block's own coordinates, as the
/// engine's GroundGenerateWeights_1a1b does, and on a shape whose coordinates run across the mask's atlas that comes out in squares.
/// Measured twice, the game draws none: The Excavation (v0.1.256) and Port's quay (v0.1.262), where cobblestone and sand differ by ten
/// levels of brightness and the picture steps by nothing across the squares' edges. The engine has a second way, GroundGenerateWeights_2a
/// (ground.ffx and internalonly.ffx, whole in ground.txt): the engine mask's red, read from the world position through two per-tile
/// transforms into an atlas of transitions - which no file holds - and the game's own corner types laid on its picture put the
/// cobblestone where the corners name it and the sand where they name sand, thinning between. So the second candidate is the plainest
/// field the corners make: the second ground's share bilinear across the sub-tile from its four corners. Neither is the engine's
/// transition; which one the picture follows is the measurement.
///
/// THE NEAREST GROUND ONLY: every ground triangle of the block is drawn into one depth buffer, so a pixel is held against the sub-tile
/// whose ground the camera sees there - the sea floor under a pier's top is left out. What stands on the ground (a crate, the water, a
/// player) is in the game's picture and not here; the moved controls - the same fields held against the picture a little way off - say
/// how much correlation such things and the light's large shapes give for nothing.
///
/// AND BOTH WEIGHED BY THE GROUNDS' HEIGHTS (v0.1.312, asked for from the live client): the engine's GroundInterpolate weighs the two by
/// what their graphs write as height where either turns GROUND_HEIGHT_BLENDING on (ShadeProgram.Grounds) - so each ground's own program
/// is run at every measured pixel, its reads at the level the picture would take (ShadeProgram.GroundReader), and the mask's green and
/// the corners' share are weighed as the engine weighs them: two candidates more. Their heights are said beside them - how often each
/// ground's comes under a half, and how far the weighing moved the share - so a candidate that changed nothing is seen to have.
/// </remarks>
internal static class CaptureGroundCompare
{
    /// <summary>The offsets the candidates are held against the picture at: none first, then the moved controls.</summary>
    internal static readonly (int X, int Y)[] Moves = [(0, 0), (-120, 0), (120, 0), (0, -120), (0, 120), (-85, -85), (85, 85), (-85, 85), (85, -85)];

    /// <summary>The local contrast's window reaches this far each way: nine pixels square, a cobblestone's edge and some of its neighbour.</summary>
    private const int Reach = 4;

    /// <summary>The fewest pixels a sub-tile needs for a line of its own.</summary>
    private const int Least = 200;

    /// <summary>The candidates, in the order the sums keep them.</summary>
    private const int Mask = 0, Corners = 1, MaskHeights = 2, CornersHeights = 3, Candidates = 4;

    /// <summary>A height under this is a ground's low place, which its neighbour may take.</summary>
    private const float Low = 0.5f;

    /// <summary>The picture's measures, in the order the sums keep them.</summary>
    private const int Brightness = 0, Contrast = 1;

    /// <summary>One laid piece's ground, its sub-tiles' grounds and its mask, as ground-on-screen worked them out.</summary>
    internal sealed record Piece(LaidGround One, IReadOnlyList<SubTileGrounds> SubTiles, GamePicture? Mask);

    /// <summary>
    /// The comparison said, and the game's picture with every measured pixel tinted by the corners' share of the second ground - or null
    /// where there is no picture to measure.
    /// </summary>
    public static string Measure(IReadOnlyList<Piece> pieces, float[] matrix, int width, int height, byte[]? bgr, out GamePicture? picture)
        => Measure(pieces, matrix, width, height, bgr, null, out picture, out _);

    /// <summary>
    /// The comparison said, the game's picture tinted by the corners' share, and tinted by the mask's green weighed by the grounds' heights
    /// - the engine's own mix as its shaders write it - where <paramref name="programs"/> reads them; null where there is no picture.
    /// </summary>
    /// <param name="programs">Each ground material's program, bound and keeping its height (MonsterModels.Paints.Heighted), or null for none.</param>
    public static string Measure(
        IReadOnlyList<Piece> pieces, float[] matrix, int width, int height, byte[]? bgr, Func<string, ShadeProgram?>? programs,
        out GamePicture? picture, out GamePicture? heightsPicture)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(matrix);
        picture = null;
        heightsPicture = null;
        if (bgr is null || bgr.LongLength < (long)width * height * 3 || width <= 0 || height <= 0)
        {
            return "mask against corners: no picture of the game to hold the two against\n";
        }

        // THE NEAREST GROUND A PIXEL: its inverse w, whose sub-tile of two grounds (-1 none), and both candidates' values there.
        int count = width * height;
        var depth = new float[count];
        var owner = new int[count];
        Array.Fill(owner, -1);
        var green = new byte[count];
        var share = new byte[count];
        var named = new List<Named>();
        List<Triangle>? triangles = programs is null ? null : [];
        int[]? triangleOf = programs is null ? null : new int[count];
        if (triangleOf is not null)
        {
            Array.Fill(triangleOf, -1);
        }
        foreach (Piece piece in pieces)
        {
            Draw(piece, matrix, width, height, depth, owner, green, share, named, triangles, triangleOf);
        }

        if (named.Count == 0)
        {
            return "mask against corners: no sub-tile of two grounds on the screen\n";
        }

        (byte[] light, float[] contrast) = Measures(bgr, width, height);

        // THE HEIGHTS, where the grounds' programs read them: the two weighed candidates, and what the heights were.
        Weighing weighing = Weigh(programs, named, triangles, triangleOf, owner, green, share, width);

        // THE SUMS: per sub-tile and per pair of grounds, for each candidate, measure and move.
        var pairs = named.Select(one => one.Pair).Distinct(StringComparer.Ordinal).ToList();
        var bySubTile = new Sums[named.Count, Candidates, 2, Moves.Length];
        var byPair = new Sums[pairs.Count, Candidates, 2, Moves.Length];
        int[] pairOf = [.. named.Select(one => pairs.IndexOf(one.Pair))];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                int at = (y * width) + x, id = owner[at];
                if (id < 0)
                {
                    continue;
                }

                float byMask = green[at] / 255f, byCorners = share[at] / 255f;
                bool weighed = weighing.Weighs[id];
                float byMaskHeights = weighed ? weighing.Mask[at] / 255f : 0f, byCornersHeights = weighed ? weighing.Corners[at] / 255f : 0f;
                int pair = pairOf[id];
                for (var move = 0; move < Moves.Length; move++)
                {
                    int mx = x + Moves[move].X, my = y + Moves[move].Y;
                    if ((uint)mx >= (uint)width || (uint)my >= (uint)height)
                    {
                        continue;
                    }

                    int there = (my * width) + mx;
                    float l = light[there], k = contrast[there];
                    bySubTile[id, Mask, Brightness, move].Add(byMask, l);
                    bySubTile[id, Mask, Contrast, move].Add(byMask, k);
                    bySubTile[id, Corners, Brightness, move].Add(byCorners, l);
                    bySubTile[id, Corners, Contrast, move].Add(byCorners, k);
                    byPair[pair, Mask, Brightness, move].Add(byMask, l);
                    byPair[pair, Mask, Contrast, move].Add(byMask, k);
                    byPair[pair, Corners, Brightness, move].Add(byCorners, l);
                    byPair[pair, Corners, Contrast, move].Add(byCorners, k);
                    if (weighed)
                    {
                        bySubTile[id, MaskHeights, Brightness, move].Add(byMaskHeights, l);
                        bySubTile[id, MaskHeights, Contrast, move].Add(byMaskHeights, k);
                        bySubTile[id, CornersHeights, Brightness, move].Add(byCornersHeights, l);
                        bySubTile[id, CornersHeights, Contrast, move].Add(byCornersHeights, k);
                        byPair[pair, MaskHeights, Brightness, move].Add(byMaskHeights, l);
                        byPair[pair, MaskHeights, Contrast, move].Add(byMaskHeights, k);
                        byPair[pair, CornersHeights, Brightness, move].Add(byCornersHeights, l);
                        byPair[pair, CornersHeights, Contrast, move].Add(byCornersHeights, k);
                    }
                }
            }
        }

        var said = new StringBuilder();
        long measured = 0;
        for (var id = 0; id < named.Count; id++)
        {
            measured += bySubTile[id, Mask, Brightness, 0].N;
        }

        said.Append(string.Create(CultureInfo.InvariantCulture,
            $"mask against corners: {measured} pixels where the nearest ground is a sub-tile of two grounds, {named.Count} such sub-tiles - the game's picture held against the mix's green at the block's coordinates (mask) and the second ground's share bilinear from the sub-tile's four corners (corners), and each weighed by the two grounds' heights as the engine's height blending weighs them (mask by heights, corners by heights: {weighing.Said}); r is the correlation, moved the same field held against the picture {Moves.Length - 1} ways 85 to 120 pixels off\n"));
        for (var pair = 0; pair < pairs.Count; pair++)
        {
            int inPair = pairOf.Count(one => one == pair);
            bool weighed = byPair[pair, MaskHeights, Brightness, 0].N > 0;
            said.Append(string.Create(CultureInfo.InvariantCulture, $"  {pairs[pair]} · {byPair[pair, Mask, Brightness, 0].N} pixels in {inPair} sub-tiles\n"));
            foreach ((int measure, string what) in new[] { (Brightness, "brightness"), (Contrast, "local contrast") })
            {
                said.Append("    ").Append(what).Append(": mask ").Append(Said(Whole(byPair, pair, Mask, measure)))
                    .Append(" · corners ").Append(Said(Whole(byPair, pair, Corners, measure)));
                if (weighed)
                {
                    said.Append(" · mask by heights ").Append(Said(Whole(byPair, pair, MaskHeights, measure)))
                        .Append(" · corners by heights ").Append(Said(Whole(byPair, pair, CornersHeights, measure)));
                }

                said.Append('\n');
                said.Append("      within each sub-tile, pixel-weighted: mask ").Append(Said(Within(bySubTile, pairOf, pair, Mask, measure)))
                    .Append(" · corners ").Append(Said(Within(bySubTile, pairOf, pair, Corners, measure)));
                if (weighed)
                {
                    said.Append(" · mask by heights ").Append(Said(Within(bySubTile, pairOf, pair, MaskHeights, measure)))
                        .Append(" · corners by heights ").Append(Said(Within(bySubTile, pairOf, pair, CornersHeights, measure)));
                }

                said.Append('\n');
            }

            said.Append("    heights: ").Append(weighing.Of(pairOf, pair)).Append('\n');
        }

        said.Append("  each sub-tile of at least ").Append(Least.ToString(CultureInfo.InvariantCulture))
            .Append(" pixels - its corners in ground.txt's order, the second ground's marked 2; r at no move, mask then corners, and by heights where weighed:\n");
        for (var id = 0; id < named.Count; id++)
        {
            long pixels = bySubTile[id, Mask, Brightness, 0].N;
            if (pixels < Least)
            {
                continue;
            }

            said.Append(string.Create(CultureInfo.InvariantCulture,
                $"    {named[id].Name} · {pixels} px · {named[id].Pair} · corners {named[id].Pattern} · brightness {R(bySubTile[id, Mask, Brightness, 0])} {R(bySubTile[id, Corners, Brightness, 0])} · contrast {R(bySubTile[id, Mask, Contrast, 0])} {R(bySubTile[id, Corners, Contrast, 0])}"));
            if (weighing.Weighs[id])
            {
                said.Append(string.Create(CultureInfo.InvariantCulture,
                    $" · by heights brightness {R(bySubTile[id, MaskHeights, Brightness, 0])} {R(bySubTile[id, CornersHeights, Brightness, 0])} · contrast {R(bySubTile[id, MaskHeights, Contrast, 0])} {R(bySubTile[id, CornersHeights, Contrast, 0])}"));
            }

            said.Append('\n');
        }

        picture = Tinted(bgr, width, height, owner, share);
        heightsPicture = weighing.Any ? Tinted(bgr, width, height, owner, weighing.Mask) : null;
        return said.ToString();
    }

    /// <summary>One sub-tile of two grounds measured: its name as ground.txt has it, its pair, its corners, and its two grounds' materials.</summary>
    private readonly record struct Named(string Name, string Pair, string Pattern, string First, string Second);

    /// <summary>One triangle of a sub-tile as it lies on the screen and in the world - what a pixel's place is worked out from.</summary>
    internal readonly record struct Triangle(Vector2 A, Vector2 B, Vector2 C, float Wa, float Wb, float Wc, Vector3 Pa, Vector3 Pb, Vector3 Pc, int Id);

    /// <summary>The two candidates weighed by the heights, per pixel; which sub-tiles were weighed; and what the heights came to, per sub-tile.</summary>
    private sealed class Weighing(int count, int subTiles)
    {
        public byte[] Mask { get; } = new byte[count];

        public byte[] Corners { get; } = new byte[count];

        public bool[] Weighs { get; } = new bool[subTiles];

        public string[] Why { get; } = new string[subTiles];

        public long[] Pixels { get; } = new long[subTiles];

        public long[] LowFirst { get; } = new long[subTiles];

        public long[] LowSecond { get; } = new long[subTiles];

        public double[] MaskMoved { get; } = new double[subTiles];

        public double[] CornersMoved { get; } = new double[subTiles];

        public bool Any => Array.Exists(Weighs, one => one);

        public string Said { get; set; } = "not read";

        /// <summary>What the heights came to over one pair's sub-tiles, or why they were not weighed.</summary>
        public string Of(int[] pairOf, int pair)
        {
            long pixels = 0, lowFirst = 0, lowSecond = 0;
            double maskMoved = 0, cornersMoved = 0;
            string why = string.Empty;
            for (var id = 0; id < pairOf.Length; id++)
            {
                if (pairOf[id] != pair)
                {
                    continue;
                }

                if (!Weighs[id])
                {
                    why = Why[id] ?? string.Empty;
                    continue;
                }

                pixels += Pixels[id];
                lowFirst += LowFirst[id];
                lowSecond += LowSecond[id];
                maskMoved += MaskMoved[id];
                cornersMoved += CornersMoved[id];
            }

            return pixels == 0
                ? "not weighed - " + (why.Length > 0 ? why : "no pixel")
                : string.Create(CultureInfo.InvariantCulture,
                    $"{pixels} pixels weighed · the first ground's height under {Low} on {100.0 * lowFirst / pixels:0.0}% of them, the second's on {100.0 * lowSecond / pixels:0.0}% · the weighing moved the mask's share by {maskMoved / pixels:0.000} on average and the corners' by {cornersMoved / pixels:0.000}");
        }
    }

    /// <summary>
    /// Each measured pixel's two grounds' heights, read with their own programs at the levels the picture would take, and the mask's green
    /// and the corners' share weighed by them as the engine weighs them - on every core.
    /// </summary>
    private static Weighing Weigh(
        Func<string, ShadeProgram?>? programs, List<Named> named, List<Triangle>? triangles, int[]? triangleOf, int[] owner, byte[] green, byte[] share,
        int width)
    {
        var weighing = new Weighing(owner.Length, named.Count);
        if (programs is null || triangles is null || triangleOf is null)
        {
            Array.Fill(weighing.Why, "no programs to read the heights with");
            return weighing;
        }

        // EACH SUB-TILE'S TWO PROGRAMS, resolved once: weighed where both are there and either turns the height blending on.
        var resolved = new Dictionary<string, ShadeProgram?>(StringComparer.OrdinalIgnoreCase);
        ShadeProgram? Of(string material)
        {
            if (material.Length == 0)
            {
                return null;
            }

            if (!resolved.TryGetValue(material, out ShadeProgram? program))
            {
                program = programs(material);
                resolved[material] = program;
            }

            return program;
        }

        var pairs = new (ShadeProgram? First, ShadeProgram? Second)[named.Count];
        for (var id = 0; id < named.Count; id++)
        {
            ShadeProgram? first = Of(named[id].First), second = Of(named[id].Second);
            pairs[id] = (first, second);
            weighing.Why[id] = first is null || second is null
                ? "a ground's program did not compile or a texture of it did not read: " + (first is null ? named[id].First : named[id].Second)
                : !first.BlendsGroundHeights && !second.BlendsGroundHeights ? "neither ground's graphs turn the engine's height blending on"
                : string.Empty;
            weighing.Weighs[id] = weighing.Why[id].Length == 0;
        }

        int rows = Rows(owner.Length);
        var gate = new object();
        Parallel.For(
            0,
            rows,
            () => new Worker(named.Count),
            (row, _, worker) =>
            {
                int from = (int)((long)row * owner.Length / rows), to = (int)((long)(row + 1) * owner.Length / rows);
                worker.Run(from, to, width, triangles, triangleOf, owner, green, share, pairs, weighing);
                return worker;
            },
            worker =>
            {
                lock (gate)
                {
                    worker.AddTo(weighing);
                }
            });

        long weighedPixels = weighing.Pixels.Sum();
        weighing.Said = weighing.Any
            ? string.Create(CultureInfo.InvariantCulture, $"{weighedPixels} pixels weighed, each ground's own program run at the level the picture would read it, the materials' coordinates the world times {GroundPainter.GroundScale}")
            : "not weighed - " + (weighing.Why.FirstOrDefault(why => why.Length > 0) ?? "no sub-tile");
        return weighing;
    }

    /// <summary>Where a pixel's middle lies in the world on one triangle - its weights on the screen, corrected for the perspective by the corners' inverse w.</summary>
    internal static Vector3 PlaceAt(Triangle one, int x, int y)
    {
        float px = x + 0.5f, py = y + 0.5f;
        Vector2 a = one.A, b = one.B, c = one.C;
        float area = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
        float wa = (((b.X - px) * (c.Y - py)) - ((b.Y - py) * (c.X - px))) / area;
        float wb = (((c.X - px) * (a.Y - py)) - ((c.Y - py) * (a.X - px))) / area;
        float wc = 1f - wa - wb;
        float w = (one.Wa * wa) + (one.Wb * wb) + (one.Wc * wc);
        return ((one.Pa * (one.Wa * wa)) + (one.Pb * (one.Wb * wb)) + (one.Pc * (one.Wc * wc))) / w;
    }

    /// <summary>How many runs of pixels the weighing is shared out in - a few a core.</summary>
    private static int Rows(int count) => Math.Max(1, Math.Min(count, Environment.ProcessorCount * 8));

    /// <summary>One thread's readers and tallies over its runs of pixels.</summary>
    private sealed class Worker(int subTiles)
    {
        private readonly Dictionary<ShadeProgram, ShadeProgram.GroundReader> _readers = [];
        private readonly long[] _pixels = new long[subTiles];
        private readonly long[] _lowFirst = new long[subTiles];
        private readonly long[] _lowSecond = new long[subTiles];
        private readonly double[] _maskMoved = new double[subTiles];
        private readonly double[] _cornersMoved = new double[subTiles];

        public void Run(
            int from, int to, int width, List<Triangle> triangles, int[] triangleOf, int[] owner, byte[] green, byte[] share,
            (ShadeProgram? First, ShadeProgram? Second)[] pairs, Weighing weighing)
        {
            int last = -1;
            ShadeProgram.GroundReader? first = null, second = null;
            for (int at = from; at < to; at++)
            {
                int id = owner[at], index = triangleOf[at];
                if (id < 0 || index < 0 || !weighing.Weighs[id])
                {
                    continue;
                }

                Triangle one = triangles[index];
                if (index != last)
                {
                    first = Reader(pairs[id].First!);
                    second = Reader(pairs[id].Second!);
                    first.Triangle(one.Pa, one.Pb, one.Pc, one.A, one.B, one.C, GroundPainter.GroundScale);
                    second.Triangle(one.Pa, one.Pb, one.Pc, one.A, one.B, one.C, GroundPainter.GroundScale);
                    last = index;
                }

                Vector3 place = PlaceAt(one, at % width, at / width);
                float h0 = first!.HeightAt(place, GroundPainter.GroundScale), h1 = second!.HeightAt(place, GroundPainter.GroundScale);
                float g = green[at] / 255f, c = share[at] / 255f;
                float byMask = ShadeProgram.WeighedShare(g, h0, h1), byCorners = ShadeProgram.WeighedShare(c, h0, h1);
                weighing.Mask[at] = (byte)((255f * Math.Clamp(byMask, 0f, 1f)) + 0.5f);
                weighing.Corners[at] = (byte)((255f * Math.Clamp(byCorners, 0f, 1f)) + 0.5f);
                _pixels[id]++;
                _lowFirst[id] += h0 < Low ? 1 : 0;
                _lowSecond[id] += h1 < Low ? 1 : 0;
                _maskMoved[id] += MathF.Abs(byMask - g);
                _cornersMoved[id] += MathF.Abs(byCorners - c);
            }
        }

        public void AddTo(Weighing weighing)
        {
            for (var id = 0; id < _pixels.Length; id++)
            {
                weighing.Pixels[id] += _pixels[id];
                weighing.LowFirst[id] += _lowFirst[id];
                weighing.LowSecond[id] += _lowSecond[id];
                weighing.MaskMoved[id] += _maskMoved[id];
                weighing.CornersMoved[id] += _cornersMoved[id];
            }
        }

        private ShadeProgram.GroundReader Reader(ShadeProgram program)
        {
            if (!_readers.TryGetValue(program, out ShadeProgram.GroundReader? reader))
            {
                reader = new ShadeProgram.GroundReader(program);
                _readers[program] = reader;
            }

            return reader;
        }
    }

    /// <summary>A piece's ground into the depth buffer, every triangle - its sub-tiles of two grounds with both candidates' values.</summary>
    private static void Draw(
        Piece piece, float[] matrix, int width, int height, float[] depth, int[] owner, byte[] green, byte[] share, List<Named> named,
        List<Triangle>? triangles, int[]? triangleOf)
    {
        LaidGround one = piece.One;
        SkinnedMesh ground = one.Ground;
        string file = Path.GetFileNameWithoutExtension(one.Path.Replace('\\', '/'));
        Span<Vector2> at = stackalloc Vector2[3];
        Span<Vector4> over = stackalloc Vector4[3];
        Span<float> inverse = stackalloc float[3];
        Span<Vector3> places = stackalloc Vector3[3];
        int from = 0;
        for (var index = 0; index < one.Pieces.Count; index++)
        {
            GroundPiece sub = one.Pieces[index];
            int first = from;
            from += sub.Triangles;
            int id = -1;
            float[]? seconds = null;
            if (index < piece.SubTiles.Count && piece.SubTiles[index] is { Grounds.Count: 2, Seconds.Count: 4 } grounds)
            {
                id = named.Count;
                seconds = [.. grounds.Seconds.Select(second => second ? 1f : 0f)];
                named.Add(new Named(
                    string.Create(CultureInfo.InvariantCulture, $"{file} c{sub.X}r{sub.Y}"),
                    (grounds.Grounds[0].Length > 0 ? grounds.Grounds[0] : "(the group with no name)") + " + " + grounds.Grounds[1],
                    string.Join(" ", grounds.Seconds.Select(second => second ? "2" : "1")),
                    grounds.Materials.Count == 2 ? grounds.Materials[0] : string.Empty,
                    grounds.Materials.Count == 2 ? grounds.Materials[1] : string.Empty));
            }

            // The sub-tile's own corner in the mesh: column x - 1, row 1 - y, a tile each way (TileDefinition.CornersOf).
            float left = (sub.X - 1) * TileModels.Side, bottom = (1 - sub.Y) * TileModels.Side;
            for (int triangle = first; triangle < first + sub.Triangles && (triangle * 3) + 2 < ground.Indices.Length; triangle++)
            {
                var seen = true;
                for (var corner = 0; corner < 3 && seen; corner++)
                {
                    int vertex = ground.Indices[(triangle * 3) + corner];
                    Vector3 file3 = ground.Positions[vertex];
                    Vector3 world = Vector3.Transform(file3, one.Place);
                    places[corner] = world;
                    (double cx, double cy, double cw) = WorldToScreen.Clip(matrix, world.X, world.Y, world.Z);
                    if (cw <= 1e-6)
                    {
                        seen = false;
                        continue;
                    }

                    float w = (float)(1.0 / cw);
                    at[corner] = new Vector2((float)((((cx / cw) * 0.5) + 0.5) * width), (float)((0.5 - ((cy / cw) * 0.5)) * height));
                    Vector2 uv = vertex < ground.Coordinates.Length ? ground.Coordinates[vertex] : Vector2.Zero;
                    over[corner] = new Vector4(uv.X, uv.Y, (file3.X - left) / TileModels.Side, (file3.Y - bottom) / TileModels.Side) * w;
                    inverse[corner] = w;
                }

                if (seen)
                {
                    // THE TRIANGLE KEPT where its pixels' heights are to be read - see Weigh.
                    int kept = -1;
                    if (id >= 0 && triangles is not null)
                    {
                        kept = triangles.Count;
                        triangles.Add(new Triangle(at[0], at[1], at[2], inverse[0], inverse[1], inverse[2], places[0], places[1], places[2], id));
                    }

                    Fill(at, over, inverse, width, height, depth, owner, green, share, id, seconds, piece.Mask, triangleOf, kept);
                }
            }
        }
    }

    /// <summary>One triangle into the depth buffer, nearer pixels taking it - and, of a sub-tile of two grounds, both candidates' values.</summary>
    private static void Fill(
        ReadOnlySpan<Vector2> at, ReadOnlySpan<Vector4> over, ReadOnlySpan<float> inverse, int width, int height, float[] depth, int[] owner, byte[] green, byte[] share, int id, float[]? seconds, GamePicture? mask,
        int[]? triangleOf, int index)
    {
        Vector2 a = at[0], b = at[1], c = at[2];
        float area = ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
        float reach = 4f * (width + height);
        if (!float.IsFinite(area) || MathF.Abs(area) < 1e-3f
            || MathF.Max(MathF.Abs(a.X), MathF.Max(MathF.Abs(b.X), MathF.Abs(c.X))) > reach
            || MathF.Max(MathF.Abs(a.Y), MathF.Max(MathF.Abs(b.Y), MathF.Abs(c.Y))) > reach)
        {
            return;
        }

        int leftmost = Math.Max(0, (int)MathF.Floor(MathF.Min(a.X, MathF.Min(b.X, c.X))));
        int rightmost = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))));
        int top = Math.Max(0, (int)MathF.Floor(MathF.Min(a.Y, MathF.Min(b.Y, c.Y))));
        int lowest = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))));
        float scale = 1f / area, stepA = (b.Y - c.Y) * scale, stepB = (c.Y - a.Y) * scale;
        for (int y = top; y <= lowest; y++)
        {
            float py = y + 0.5f, px = leftmost + 0.5f;
            float weightA = (((b.X - px) * (c.Y - py)) - ((b.Y - py) * (c.X - px))) * scale;
            float weightB = (((c.X - px) * (a.Y - py)) - ((c.Y - py) * (a.X - px))) * scale;
            for (int x = leftmost; x <= rightmost; x++, weightA += stepA, weightB += stepB)
            {
                float weightC = 1f - weightA - weightB;
                if (weightA < 0f || weightB < 0f || weightC < 0f)
                {
                    continue;
                }

                float w = (inverse[0] * weightA) + (inverse[1] * weightB) + (inverse[2] * weightC);
                int pixel = (y * width) + x;
                if (w <= depth[pixel])
                {
                    continue;
                }

                depth[pixel] = w;
                owner[pixel] = id;
                if (triangleOf is not null)
                {
                    triangleOf[pixel] = index;
                }

                if (id < 0)
                {
                    continue;
                }

                Vector4 v = ((over[0] * weightA) + (over[1] * weightB) + (over[2] * weightC)) / w;
                green[pixel] = mask is { } picture ? (byte)(255f * Math.Clamp(GroundPainter.Sample(picture, 1, v.X, v.Y), 0f, 1f)) : (byte)0;
                share[pixel] = (byte)(255f * Share(seconds!, Math.Clamp(v.Z, 0f, 1f), Math.Clamp(v.W, 0f, 1f)));
            }
        }
    }

    /// <summary>The second ground's share at a place in the sub-tile, bilinear from its corners: k at (k &amp; 1, k &gt;&gt; 1).</summary>
    internal static float Share(float[] seconds, float s, float t)
        => (seconds[0] * (1f - s) * (1f - t)) + (seconds[1] * s * (1f - t)) + (seconds[2] * (1f - s) * t) + (seconds[3] * s * t);

    /// <summary>The picture's brightness a pixel, and its local contrast - the brightness's spread over a window nine pixels square.</summary>
    internal static (byte[] Light, float[] Contrast) Measures(byte[] bgr, int width, int height)
    {
        var light = new byte[width * height];
        for (int pixel = 0, from = 0; pixel < light.Length; pixel++, from += 3)
        {
            light[pixel] = (byte)(((114 * bgr[from]) + (587 * bgr[from + 1]) + (299 * bgr[from + 2]) + 500) / 1000);
        }

        // RUNNING SUMS, a column's window down then the row's across - every pixel the same nine by nine, clamped at the picture's edge.
        var contrast = new float[width * height];
        var column = new long[width];
        var squares = new long[width];
        for (var y = 0; y < height; y++)
        {
            Array.Clear(column);
            Array.Clear(squares);
            int top = Math.Max(0, y - Reach), lowest = Math.Min(height - 1, y + Reach);
            for (int row = top; row <= lowest; row++)
            {
                for (var x = 0; x < width; x++)
                {
                    int l = light[(row * width) + x];
                    column[x] += l;
                    squares[x] += l * l;
                }
            }

            long sum = 0, square = 0;
            int left = 0, right = -1, rows = lowest - top + 1;
            for (var x = 0; x < width; x++)
            {
                for (int to = Math.Min(width - 1, x + Reach); right < to;)
                {
                    right++;
                    sum += column[right];
                    square += squares[right];
                }

                for (int from = Math.Max(0, x - Reach); left < from; left++)
                {
                    sum -= column[left];
                    square -= squares[left];
                }

                double n = (double)rows * (right - left + 1), mean = sum / n;
                contrast[(y * width) + x] = (float)Math.Sqrt(Math.Max(0.0, (square / n) - (mean * mean)));
            }
        }

        return (light, contrast);
    }

    /// <summary>The game's picture with each measured pixel half covered by the corners' share - blue the first ground, red the second.</summary>
    private static GamePicture Tinted(byte[] bgr, int width, int height, int[] owner, byte[] share)
    {
        var rgba = new byte[width * height * 4];
        for (int pixel = 0, from = 0, to = 0; pixel < owner.Length; pixel++, from += 3, to += 4)
        {
            rgba[to] = bgr[from + 2];
            rgba[to + 1] = bgr[from + 1];
            rgba[to + 2] = bgr[from];
            rgba[to + 3] = 255;
            if (owner[pixel] >= 0)
            {
                rgba[to] = (byte)((rgba[to] + share[pixel]) >> 1);
                rgba[to + 1] = (byte)((rgba[to + 1] + 40) >> 1);
                rgba[to + 2] = (byte)((rgba[to + 2] + 255 - share[pixel]) >> 1);
            }
        }

        return new GamePicture(width, height, rgba);
    }

    /// <summary>A pair's correlation at no move, and the moved ones' mean and spread.</summary>
    private static (double R, double Mean, double Spread) Whole(Sums[,,,] sums, int pair, int candidate, int measure)
    {
        double r = sums[pair, candidate, measure, 0].R;
        double[] moved = [.. Enumerable.Range(1, Moves.Length - 1).Select(move => sums[pair, candidate, measure, move].R).Where(double.IsFinite)];
        return Spread(r, moved);
    }

    /// <summary>The sub-tiles' own correlations of one pair, weighted by their pixels - at no move, and each move's alike.</summary>
    private static (double R, double Mean, double Spread) Within(Sums[,,,] sums, int[] pairOf, int pair, int candidate, int measure)
    {
        double Weighted(int move)
        {
            double total = 0, weight = 0;
            for (var id = 0; id < pairOf.Length; id++)
            {
                double r = sums[id, candidate, measure, move].R;
                if (pairOf[id] != pair || !double.IsFinite(r))
                {
                    continue;
                }

                total += r * sums[id, candidate, measure, move].N;
                weight += sums[id, candidate, measure, move].N;
            }

            return weight > 0 ? total / weight : double.NaN;
        }

        double[] moved = [.. Enumerable.Range(1, Moves.Length - 1).Select(Weighted).Where(double.IsFinite)];
        return Spread(Weighted(0), moved);
    }

    internal static (double R, double Mean, double Spread) Spread(double r, double[] moved)
    {
        if (moved.Length == 0)
        {
            return (r, double.NaN, double.NaN);
        }

        double mean = moved.Average();
        return (r, mean, Math.Sqrt(moved.Sum(one => (one - mean) * (one - mean)) / moved.Length));
    }

    internal static string Said((double R, double Mean, double Spread) one)
        => !double.IsFinite(one.R) ? "r n/a (the field is even)"
            : !double.IsFinite(one.Mean) ? string.Create(CultureInfo.InvariantCulture, $"r {one.R:+0.000;-0.000} (no move stays on the picture)")
            : string.Create(CultureInfo.InvariantCulture, $"r {one.R:+0.000;-0.000} (moved {one.Mean:+0.000;-0.000} +/- {one.Spread:0.000})");

    private static string R(Sums sums) => double.IsFinite(sums.R) ? sums.R.ToString("+0.00;-0.00", CultureInfo.InvariantCulture) : "n/a";

    /// <summary>The sums a correlation is worked out from.</summary>
    internal struct Sums
    {
        private double _x, _y, _xx, _yy, _xy;

        public long N { get; private set; }

        /// <summary>Another's sums added in - one thread's to the whole's.</summary>
        public void Add(in Sums other)
        {
            N += other.N;
            _x += other._x;
            _y += other._y;
            _xx += other._xx;
            _yy += other._yy;
            _xy += other._xy;
        }

        public void Add(double x, double y)
        {
            N++;
            _x += x;
            _y += y;
            _xx += x * x;
            _yy += y * y;
            _xy += x * y;
        }

        /// <summary>The correlation, or NaN where either side does not vary.</summary>
        public readonly double R
        {
            get
            {
                if (N < 2)
                {
                    return double.NaN;
                }

                double sx = _xx - (_x * _x / N), sy = _yy - (_y * _y / N), sxy = _xy - (_x * _y / N);
                return sx <= 1e-9 || sy <= 1e-9 ? double.NaN : sxy / Math.Sqrt(sx * sy);
            }
        }
    }
}
