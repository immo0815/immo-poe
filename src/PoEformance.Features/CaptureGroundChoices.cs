using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// Which material of its ground's group the game drew on each sub-tile on the screen: every material the group offers run at every
/// pixel, and its own pattern held against the game's picture.
/// </summary>
/// <remarks>
/// WHY (v0.1.313, asked for from the live client). A MaterialsList group offers several materials with weights - Port's group with no
/// name Cobblestone01 to 03 and CobblestoneDirt01 twice (30, 25, 40, 20, 20), TropicalFill DrySand01 to 03 (30, 45, 25) - and the game
/// draws one of them by weight, while the painter takes the heaviest everywhere (GroundPainter.Heaviest). Port's quay shows cobblestones
/// standing out of sand where no mix the shaders write puts them (v0.1.312's capture: the heights moved nothing between the cobblestone
/// and the sand), which a cobblestone-in-dirt material on some tiles would draw by itself. Which material the game took, and whether
/// per tile, per sub-tile or otherwise, is the measurement.
///
/// THE PATTERN, NOT THE COLOUR: the game's light, shadow, fog and grade change a material's colour, its stones' and grains' pattern they
/// leave. So the picture's detail - its brightness less its mean over nine by nine pixels - is held against each material's brightness as
/// its own program draws it, at the level the picture reads (ShadeProgram.GroundReader), the materials' coordinates the world times
/// GroundPainter.GroundScale. A material that lines up correlates; one that does not, or a picture whose coordinates the engine moves
/// otherwise, does not - the moved controls (the same field against the picture 85 to 120 pixels off) say what nothing gives.
///
/// A WINNER IS CLEAR where its correlation stands three spreads over its moved ones and a twentieth over the next material's; a
/// sub-tile of one ground is the clean test, one of two mixes them and is said apart.
/// </remarks>
internal static class CaptureGroundChoices
{
    /// <summary>The detail's window reaches this far each way: nine pixels square, as the comparison's local contrast.</summary>
    private const int Reach = 4;

    /// <summary>The fewest pixels a sub-tile needs for a line of its own.</summary>
    private const int Least = 200;

    /// <summary>How far a winner's correlation must stand over the next material's.</summary>
    private const double Margin = 0.05;

    /// <summary>How many spreads of its moved controls a winner's correlation must stand over their mean.</summary>
    private const double Spreads = 3.0;

    /// <summary>The colours a winner tints its sub-tile in, by its place among the materials named.</summary>
    private static readonly (byte R, byte G, byte B, string Name)[] Palette =
    [
        (230, 25, 75, "red"), (60, 180, 75, "green"), (0, 130, 200, "blue"), (255, 225, 25, "yellow"), (245, 130, 48, "orange"),
        (145, 30, 180, "purple"), (70, 240, 240, "cyan"), (240, 50, 230, "magenta"), (210, 245, 60, "lime"), (250, 190, 212, "pink"),
    ];

    /// <summary>One laid piece's ground and its sub-tiles' grounds, as ground-on-screen worked them out.</summary>
    internal sealed record Piece(LaidGround One, IReadOnlyList<SubTileGrounds> SubTiles);

    /// <summary>One material a sub-tile may be drawn with: its file, the group offering it, and its weight there.</summary>
    private readonly record struct Candidate(string Material, string Group, int Weight);

    /// <summary>One sub-tile measured: its name, its piece, its grounds, the materials it may be drawn with and the painter's own.</summary>
    private sealed record SubTile(string Name, int Piece, string PieceName, IReadOnlyList<string> Grounds, Candidate[] Candidates, IReadOnlySet<string> Painted);

    /// <summary>
    /// The choices said, and the game's picture with every sub-tile whose winner is clear tinted in its winner's colour - or null where
    /// there is no picture to measure.
    /// </summary>
    /// <param name="pieces">The laid pieces on and round the screen.</param>
    /// <param name="matrix">The game's camera.</param>
    /// <param name="width">The picture's width.</param>
    /// <param name="height">Its height.</param>
    /// <param name="bgr">The game's picture, three bytes a pixel blue first.</param>
    /// <param name="programs">Each ground material's own program, bound (MonsterModels.Paints.Plain), or null where it does not compile.</param>
    /// <param name="picture">The tinted picture.</param>
    public static string Measure(
        IReadOnlyList<Piece> pieces, float[] matrix, int width, int height, byte[]? bgr, Func<string, ShadeProgram?> programs, out GamePicture? picture)
    {
        ArgumentNullException.ThrowIfNull(pieces);
        ArgumentNullException.ThrowIfNull(matrix);
        ArgumentNullException.ThrowIfNull(programs);
        picture = null;
        if (bgr is null || bgr.LongLength < (long)width * height * 3 || width <= 0 || height <= 0)
        {
            return "material choices: no picture of the game to hold the materials against\n";
        }

        // THE NEAREST GROUND A PIXEL, whichever sub-tile it is - one with no ground to choose still hides what lies behind it.
        int count = width * height;
        var depth = new float[count];
        var owner = new int[count];
        var triangleOf = new int[count];
        Array.Fill(owner, -1);
        Array.Fill(triangleOf, -1);
        var subTiles = new List<SubTile>();
        var triangles = new List<CaptureGroundCompare.Triangle>();
        for (var at = 0; at < pieces.Count; at++)
        {
            Draw(pieces[at], at, matrix, width, height, depth, owner, triangleOf, subTiles, triangles);
        }

        if (subTiles.Count == 0)
        {
            return "material choices: no sub-tile with a ground on the screen\n";
        }

        // EACH MATERIAL'S PROGRAM ONCE, and why one did not compile.
        var resolved = new Dictionary<string, ShadeProgram?>(StringComparer.OrdinalIgnoreCase);
        foreach (Candidate candidate in subTiles.SelectMany(one => one.Candidates))
        {
            if (!resolved.ContainsKey(candidate.Material))
            {
                resolved[candidate.Material] = programs(candidate.Material);
            }
        }

        (byte[] light, _) = CaptureGroundCompare.Measures(bgr, width, height);
        float[] detail = Detail(light, width, height);
        int most = subTiles.Max(one => one.Candidates.Length);
        int moves = CaptureGroundCompare.Moves.Length;
        var sums = new CaptureGroundCompare.Sums[subTiles.Count * most * moves];
        int runs = Math.Max(1, Math.Min(count, Environment.ProcessorCount * 8));
        var gate = new object();
        Parallel.For(
            0,
            runs,
            () => new Worker(sums.Length, resolved),
            (run, _, worker) =>
            {
                int from = (int)((long)run * count / runs), to = (int)((long)(run + 1) * count / runs);
                worker.Run(from, to, width, height, most, owner, triangleOf, triangles, subTiles, detail);
                return worker;
            },
            worker =>
            {
                lock (gate)
                {
                    worker.AddTo(sums);
                }
            });

        return Said(subTiles, sums, most, resolved, owner, bgr, width, height, out picture);
    }

    /// <summary>Every ground triangle of a piece into the depth buffer, its sub-tiles with materials to choose numbered.</summary>
    private static void Draw(
        Piece piece, int pieceAt, float[] matrix, int width, int height, float[] depth, int[] owner, int[] triangleOf, List<SubTile> subTiles,
        List<CaptureGroundCompare.Triangle> triangles)
    {
        LaidGround one = piece.One;
        SkinnedMesh ground = one.Ground;
        string file = Path.GetFileNameWithoutExtension(one.Path.Replace('\\', '/'));
        string pieceName = string.Create(CultureInfo.InvariantCulture, $"{file} at tile {one.Piece.MinX}, {one.Piece.MinY}");
        Span<Vector2> at = stackalloc Vector2[3];
        Span<float> inverse = stackalloc float[3];
        Span<Vector3> places = stackalloc Vector3[3];
        int from = 0;
        for (var index = 0; index < one.Pieces.Count; index++)
        {
            GroundPiece sub = one.Pieces[index];
            int first = from;
            from += sub.Triangles;
            int id = -1;
            if (index < piece.SubTiles.Count && piece.SubTiles[index] is { Choices.Count: > 0 } grounds)
            {
                // ONE CANDIDATE A MATERIAL, a material a group offers twice weighed twice.
                var candidates = new List<Candidate>();
                for (var g = 0; g < grounds.Choices.Count; g++)
                {
                    string group = g < grounds.Grounds.Count && grounds.Grounds[g].Length > 0 ? grounds.Grounds[g] : "(the group with no name)";
                    foreach ((string material, int weight) in grounds.Choices[g])
                    {
                        int known = candidates.FindIndex(one => string.Equals(one.Material, material, StringComparison.OrdinalIgnoreCase) && one.Group == group);
                        if (known >= 0)
                        {
                            candidates[known] = candidates[known] with { Weight = candidates[known].Weight + weight };
                        }
                        else
                        {
                            candidates.Add(new Candidate(material, group, weight));
                        }
                    }
                }

                id = subTiles.Count;
                subTiles.Add(new SubTile(
                    string.Create(CultureInfo.InvariantCulture, $"{file} c{sub.X}r{sub.Y}"),
                    pieceAt,
                    pieceName,
                    [.. grounds.Grounds.Select(name => name.Length > 0 ? name : "(the group with no name)")],
                    [.. candidates],
                    new HashSet<string>(grounds.Materials, StringComparer.OrdinalIgnoreCase)));
            }

            for (int triangle = first; triangle < first + sub.Triangles && (triangle * 3) + 2 < ground.Indices.Length; triangle++)
            {
                var seen = true;
                for (var corner = 0; corner < 3 && seen; corner++)
                {
                    Vector3 world = Vector3.Transform(ground.Positions[ground.Indices[(triangle * 3) + corner]], one.Place);
                    (double cx, double cy, double cw) = WorldToScreen.Clip(matrix, world.X, world.Y, world.Z);
                    if (cw <= 1e-6)
                    {
                        seen = false;
                        continue;
                    }

                    places[corner] = world;
                    inverse[corner] = (float)(1.0 / cw);
                    at[corner] = new Vector2((float)((((cx / cw) * 0.5) + 0.5) * width), (float)((0.5 - ((cy / cw) * 0.5)) * height));
                }

                if (!seen)
                {
                    continue;
                }

                var kept = new CaptureGroundCompare.Triangle(at[0], at[1], at[2], inverse[0], inverse[1], inverse[2], places[0], places[1], places[2], id);
                int number = -1;
                if (id >= 0)
                {
                    number = triangles.Count;
                    triangles.Add(kept);
                }

                Fill(kept, number, width, height, depth, owner, triangleOf);
            }
        }
    }

    /// <summary>One triangle into the depth buffer, nearer pixels taking it.</summary>
    private static void Fill(CaptureGroundCompare.Triangle one, int number, int width, int height, float[] depth, int[] owner, int[] triangleOf)
    {
        Vector2 a = one.A, b = one.B, c = one.C;
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

                float w = (one.Wa * weightA) + (one.Wb * weightB) + (one.Wc * weightC);
                int pixel = (y * width) + x;
                if (w <= depth[pixel])
                {
                    continue;
                }

                depth[pixel] = w;
                owner[pixel] = one.Id;
                triangleOf[pixel] = number;
            }
        }
    }

    /// <summary>The picture's brightness less its mean over nine by nine pixels, clamped at the picture's edge - the detail a pattern shows in.</summary>
    internal static float[] Detail(byte[] light, int width, int height)
    {
        // RUNNING SUMS, across each row and then down each column of the row sums.
        var across = new int[width * height];
        for (var y = 0; y < height; y++)
        {
            int row = y * width, sum = 0, left = 0, right = -1;
            for (var x = 0; x < width; x++)
            {
                for (int to = Math.Min(width - 1, x + Reach); right < to;)
                {
                    sum += light[row + ++right];
                }

                for (int start = Math.Max(0, x - Reach); left < start; left++)
                {
                    sum -= light[row + left];
                }

                across[row + x] = sum;
            }
        }

        var detail = new float[width * height];
        for (var x = 0; x < width; x++)
        {
            int sum = 0, top = 0, bottom = -1;
            for (var y = 0; y < height; y++)
            {
                for (int to = Math.Min(height - 1, y + Reach); bottom < to;)
                {
                    sum += across[(++bottom * width) + x];
                }

                for (int start = Math.Max(0, y - Reach); top < start; top++)
                {
                    sum -= across[(top * width) + x];
                }

                int columns = Math.Min(width - 1, x + Reach) - Math.Max(0, x - Reach) + 1;
                int pixel = (y * width) + x;
                detail[pixel] = light[pixel] - ((float)sum / (columns * (bottom - top + 1)));
            }
        }

        return detail;
    }

    /// <summary>One thread's readers and sums over its runs of pixels.</summary>
    private sealed class Worker(int size, Dictionary<string, ShadeProgram?> programs)
    {
        private readonly CaptureGroundCompare.Sums[] _sums = new CaptureGroundCompare.Sums[size];
        private readonly Dictionary<ShadeProgram, ShadeProgram.GroundReader> _readers = [];

        public void Run(
            int from, int to, int width, int height, int most, int[] owner, int[] triangleOf, List<CaptureGroundCompare.Triangle> triangles,
            List<SubTile> subTiles, float[] detail)
        {
            (int X, int Y)[] moves = CaptureGroundCompare.Moves;
            int last = -1;
            var readers = new ShadeProgram.GroundReader?[most];
            for (int at = from; at < to; at++)
            {
                int id = owner[at], index = triangleOf[at];
                if (id < 0 || index < 0)
                {
                    continue;
                }

                CaptureGroundCompare.Triangle one = triangles[index];
                Candidate[] candidates = subTiles[id].Candidates;
                if (index != last)
                {
                    for (var k = 0; k < candidates.Length; k++)
                    {
                        readers[k] = programs[candidates[k].Material] is { } program ? Reader(program) : null;
                        readers[k]?.Triangle(one.Pa, one.Pb, one.Pc, one.A, one.B, one.C, GroundPainter.GroundScale);
                    }

                    last = index;
                }

                int x = at % width, y = at / width;
                Vector3 place = CaptureGroundCompare.PlaceAt(one, x, y);
                for (var k = 0; k < candidates.Length; k++)
                {
                    if (readers[k] is not { } reader)
                    {
                        continue;
                    }

                    float value = reader.BrightnessAt(place, GroundPainter.GroundScale);
                    int slot = ((id * most) + k) * moves.Length;
                    for (var move = 0; move < moves.Length; move++)
                    {
                        int mx = x + moves[move].X, my = y + moves[move].Y;
                        if ((uint)mx < (uint)width && (uint)my < (uint)height)
                        {
                            _sums[slot + move].Add(value, detail[(my * width) + mx]);
                        }
                    }
                }
            }
        }

        public void AddTo(CaptureGroundCompare.Sums[] sums)
        {
            for (var at = 0; at < sums.Length; at++)
            {
                sums[at].Add(in _sums[at]);
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

    /// <summary>What each sub-tile's materials came to, each group's wins beside its weights, each piece's sub-tiles, and the tinted picture.</summary>
    private static string Said(
        List<SubTile> subTiles, CaptureGroundCompare.Sums[] sums, int most, Dictionary<string, ShadeProgram?> programs, int[] owner, byte[] bgr,
        int width, int height, out GamePicture? picture)
    {
        int moves = CaptureGroundCompare.Moves.Length;
        (double R, double Mean, double Spread) Of(int id, int k)
        {
            int slot = ((id * most) + k) * moves;
            double[] moved = [.. Enumerable.Range(1, moves - 1).Select(move => sums[slot + move].R).Where(double.IsFinite)];
            return CaptureGroundCompare.Spread(sums[slot].R, moved);
        }

        // EACH SUB-TILE'S WINNER, where it is clear.
        var winners = new int[subTiles.Count];
        var pixels = new long[subTiles.Count];
        for (var id = 0; id < subTiles.Count; id++)
        {
            winners[id] = -1;
            Candidate[] candidates = subTiles[id].Candidates;

            // THE MOST ANY MATERIAL MEASURED, not the first's: a first material that did not compile measured nothing.
            for (var k = 0; k < candidates.Length; k++)
            {
                pixels[id] = Math.Max(pixels[id], sums[((id * most) + k) * moves].N);
            }

            int best = -1, next = -1;
            for (var k = 0; k < candidates.Length; k++)
            {
                double r = Of(id, k).R;
                if (!double.IsFinite(r))
                {
                    continue;
                }

                if (best < 0 || r > Of(id, best).R)
                {
                    next = best;
                    best = k;
                }
                else if (next < 0 || r > Of(id, next).R)
                {
                    next = k;
                }
            }

            if (best >= 0)
            {
                (double r, double mean, double spread) = Of(id, best);
                bool overMoved = !double.IsFinite(mean) || r > mean + (Spreads * spread);
                bool overNext = next < 0 || r - Of(id, next).R >= Margin;
                winners[id] = overMoved && overNext && pixels[id] >= Least ? best : -1;
            }
        }

        // THE MATERIALS NAMED, each a colour of the picture.
        var materials = subTiles.SelectMany(one => one.Candidates.Select(candidate => candidate.Material)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string Tail(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        string ColourOf(string material) => Palette[materials.FindIndex(one => string.Equals(one, material, StringComparison.OrdinalIgnoreCase)) % Palette.Length].Name;

        long measured = pixels.Sum();
        int oneGround = Enumerable.Range(0, subTiles.Count).Count(id => subTiles[id].Grounds.Count == 1 && pixels[id] >= Least);
        int clear = Enumerable.Range(0, subTiles.Count).Count(id => winners[id] >= 0);
        var said = new StringBuilder();
        said.Append(string.Create(CultureInfo.InvariantCulture,
            $"material choices: {measured} pixels in {subTiles.Count} sub-tiles - every material each sub-tile's grounds' groups offer, run at every pixel at the level the picture reads (its coordinates the world times {GroundPainter.GroundScale}), its brightness held against the game's picture's detail (its brightness less its mean over nine by nine pixels); r at no move (moved: the same against the picture {CaptureGroundCompare.Moves.Length - 1} ways 85 to 120 pixels off). A winner is clear {Spreads} spreads over its moved controls and {Margin} over the next material; {clear} of {subTiles.Count} sub-tiles have one - {Enumerable.Range(0, subTiles.Count).Count(id => winners[id] >= 0 && subTiles[id].Grounds.Count == 1)} of the {oneGround} of one ground with at least {Least} pixels, the clean test\n"));
        foreach ((string material, ShadeProgram? program) in programs.Where(one => one.Value is null))
        {
            said.Append("  not read: ").Append(material).Append(" - its graphs did not compile or a texture did not read\n");
        }

        // EACH GROUP: its materials with their weights, the painter's mark, and how often each won a sub-tile of one ground.
        foreach (string group in subTiles.SelectMany(one => one.Candidates.Select(candidate => candidate.Group)).Distinct(StringComparer.Ordinal))
        {
            var offered = subTiles.SelectMany(one => one.Candidates).Where(candidate => candidate.Group == group)
                .GroupBy(candidate => candidate.Material, StringComparer.OrdinalIgnoreCase).Select(one => one.First()).ToList();
            int total = offered.Sum(one => one.Weight);
            var wins = new Dictionary<string, (int SubTiles, long Pixels)>(StringComparer.OrdinalIgnoreCase);
            int tested = 0;
            for (var id = 0; id < subTiles.Count; id++)
            {
                if (subTiles[id].Grounds.Count != 1 || subTiles[id].Grounds[0] != group || pixels[id] < Least)
                {
                    continue;
                }

                tested++;
                if (winners[id] >= 0)
                {
                    string won = subTiles[id].Candidates[winners[id]].Material;
                    (int count, long px) = wins.GetValueOrDefault(won);
                    wins[won] = (count + 1, px + pixels[id]);
                }
            }

            said.Append("  ").Append(group).Append(string.Create(CultureInfo.InvariantCulture, $" · {tested} sub-tiles of it alone on the screen:"));
            foreach (Candidate candidate in offered)
            {
                (int count, long px) = wins.GetValueOrDefault(candidate.Material);
                bool painted = subTiles.Any(one => one.Grounds.Contains(group) && one.Painted.Contains(candidate.Material));
                said.Append(string.Create(CultureInfo.InvariantCulture,
                    $" {Tail(candidate.Material)} weight {candidate.Weight}{(total > 0 ? $" ({100.0 * candidate.Weight / total:0}%)" : string.Empty)}{(painted ? ", the painter's" : string.Empty)}, {ColourOf(candidate.Material)}: won {count} ({px} px);"));
            }

            said.Append('\n');
        }

        // EACH PIECE: what its sub-tiles of one ground came to, so a choice per piece shows as one winner throughout.
        said.Append("  by piece - the clear winners of its sub-tiles of one ground:\n");
        foreach (IGrouping<int, int> piece in Enumerable.Range(0, subTiles.Count).Where(id => subTiles[id].Grounds.Count == 1 && pixels[id] >= Least).GroupBy(id => subTiles[id].Piece))
        {
            var counted = piece.Where(id => winners[id] >= 0).GroupBy(id => Tail(subTiles[id].Candidates[winners[id]].Material)).OrderByDescending(one => one.Count());
            said.Append("    ").Append(subTiles[piece.First()].PieceName).Append(string.Create(CultureInfo.InvariantCulture, $" · {piece.Count()} sub-tiles:"));
            foreach (IGrouping<string, int> won in counted)
            {
                said.Append(string.Create(CultureInfo.InvariantCulture, $" {won.Key} x{won.Count()}"));
            }

            int unclear = piece.Count(id => winners[id] < 0);
            said.Append(unclear > 0 ? string.Create(CultureInfo.InvariantCulture, $" · {unclear} unclear") : string.Empty).Append('\n');
        }

        // EACH SUB-TILE, one ground first: every material's r, the winner marked.
        said.Append("  each sub-tile of at least ").Append(Least.ToString(CultureInfo.InvariantCulture)).Append(" pixels, one ground first - every material's r (moved), the winner marked *:\n");
        foreach (int id in Enumerable.Range(0, subTiles.Count).Where(id => pixels[id] >= Least).OrderBy(id => subTiles[id].Grounds.Count).ThenByDescending(id => pixels[id]))
        {
            SubTile one = subTiles[id];
            said.Append("    ").Append(one.Name).Append(string.Create(CultureInfo.InvariantCulture, $" · {pixels[id]} px · ")).Append(string.Join(" + ", one.Grounds)).Append(" ·");
            for (var k = 0; k < one.Candidates.Length; k++)
            {
                said.Append(' ').Append(k == winners[id] ? "*" : string.Empty).Append(Tail(one.Candidates[k].Material)).Append(' ').Append(CaptureGroundCompare.Said(Of(id, k))).Append(';');
            }

            said.Append(winners[id] < 0 ? " no clear winner" : string.Empty).Append('\n');
        }

        picture = Tinted(bgr, width, height, owner, id => winners[id] < 0 ? -1 : materials.FindIndex(one => string.Equals(one, subTiles[id].Candidates[winners[id]].Material, StringComparison.OrdinalIgnoreCase)));
        return said.ToString();
    }

    /// <summary>The game's picture with each sub-tile whose winner is clear half covered in its winner's colour.</summary>
    private static GamePicture Tinted(byte[] bgr, int width, int height, int[] owner, Func<int, int> colourOf)
    {
        var rgba = new byte[width * height * 4];
        var colours = new Dictionary<int, int>();
        for (int pixel = 0, from = 0, to = 0; pixel < owner.Length; pixel++, from += 3, to += 4)
        {
            rgba[to] = bgr[from + 2];
            rgba[to + 1] = bgr[from + 1];
            rgba[to + 2] = bgr[from];
            rgba[to + 3] = 255;
            int id = owner[pixel];
            if (id < 0)
            {
                continue;
            }

            if (!colours.TryGetValue(id, out int colour))
            {
                colour = colourOf(id);
                colours[id] = colour;
            }

            if (colour >= 0)
            {
                (byte r, byte g, byte b, _) = Palette[colour % Palette.Length];
                rgba[to] = (byte)((rgba[to] + r) >> 1);
                rgba[to + 1] = (byte)((rgba[to + 1] + g) >> 1);
                rgba[to + 2] = (byte)((rgba[to + 2] + b) >> 1);
            }
        }

        return new GamePicture(width, height, rgba);
    }
}
