using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// Which material of its ground's group the game drew on each sub-tile on the screen: every material the group offers read over the
/// sub-tile's ground, its own pattern lined up with the game's picture.
/// </summary>
/// <remarks>
/// WHY (v0.1.313, asked for from the live client). A MaterialsList group offers several materials with weights - Port's group with no
/// name Cobblestone01 to 03 and CobblestoneDirt01 twice (30, 25, 40, 20, 20), TropicalFill DrySand01 to 03 (30, 45, 25) - and the game
/// draws one of them by weight, while the painter takes the heaviest everywhere (GroundPainter.Heaviest). Which material the game took,
/// and whether per tile, per sub-tile or otherwise, is the measurement.
///
/// THE PATTERN, NOT THE COLOUR: the game's light, shadow, fog and grade change a material's colour, its stones' and grains' pattern they
/// leave. So the picture's detail - its brightness less its mean over nine by nine pixels - is held against each material's brightness as
/// its own program draws it, at the level the picture reads (ShadeProgram.GroundReader), the materials' coordinates the world times
/// GroundPainter.GroundScale.
///
/// LINED UP, NOT TAKEN AS PLACED (v0.1.314). v0.1.313 held each material at the pixel's place alone and measured nought everywhere, and
/// its capture of Port's pier said why: lined up, the game's stones and the cobblestone materials correlate at r 0.08 to 0.29, the same
/// way round as the tool reads them and at the world times 0.004 - and four units off, at nought. The tool's world place of a pixel is
/// that close and no closer: across the pier the best line-up moved from (2, -12) to (4, -2) units, which a plane 16 units higher made one
/// shift, (12, 6) - the camera's or the engine's own offset (ground_scalemove_uv's zw, a pass uniform no file holds), which one capture
/// cannot tell apart. So each material is shifted up to 24 units each way (<see cref="Window"/>) and the best taken; the picture's detail
/// is summed in cells of two units first (<see cref="Cell"/>), each material read once a cell, so a shift costs a sum over the cells.
///
/// THE CONTROLS ARE THE SAME MATERIAL SHIFTED WHERE NOTHING LINES UP - 48 and 72 units, past a stone and short of the 500 the colour
/// repeats at (<see cref="Away"/>) - and z is how many of their spreads the best line-up stands over their mean. Lined up, Port's pier
/// stood at z 13 to 39; its water at 2 to 6. A winner is clear at z <see cref="Clear"/> and <see cref="Lead"/> over the next material's
/// best - both CHOSEN. The materials of one family share their stones (Port's five cobblestones are one layout, fewer stones in each),
/// so a winner says which density the picture shows, and its lead is small.
/// </remarks>
internal static class CaptureGroundChoices
{
    /// <summary>The detail's window reaches this far each way: nine pixels square, as the comparison's local contrast.</summary>
    private const int Reach = 4;

    /// <summary>The fewest pixels a sub-tile needs to be measured.</summary>
    private const int Least = 200;

    /// <summary>The cells the picture's detail is summed in and each material read at, in world units - two or three pixels near the camera, a fraction of a stone.</summary>
    private const float Cell = 2f;

    /// <summary>How far a material is shifted each way for its best line-up, in cells: 24 units, past the 16 Port's pier needed.</summary>
    private const int Window = 12;

    /// <summary>How far the cells read reach past the sub-tile's own, in cells: the farthest control's shift.</summary>
    private const int Rim = 37;

    /// <summary>How many spreads of its controls a winner's line-up must stand over their mean - CHOSEN.</summary>
    private const double Clear = 5.0;

    /// <summary>How far a winner's correlation must stand over the next material's - CHOSEN.</summary>
    private const double Lead = 0.01;

    /// <summary>The controls' shifts, in cells: 32 ways at 24 cells and 32 at 36 - 48 and 72 units.</summary>
    private static readonly (int X, int Y)[] Away = [.. Enumerable.Range(0, 64).Select(one =>
    {
        double angle = (one % 32) * Math.PI / 16.0, radius = one < 32 ? 24.0 : 36.0;
        return ((int)Math.Round(radius * Math.Cos(angle)), (int)Math.Round(radius * Math.Sin(angle)));
    })];

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

    /// <summary>One material's best line-up on one sub-tile: its correlation, the shift in cells, and its controls' mean and spread - NaN where not read.</summary>
    private readonly record struct Found(double R, int ShiftX, int ShiftY, double AwayMean, double AwaySpread)
    {
        public static Found None => new(double.NaN, 0, 0, double.NaN, double.NaN);

        public double Z => AwaySpread > 1e-12 ? (R - AwayMean) / AwaySpread : double.NaN;
    }

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

        // EACH SUB-TILE'S PIXELS, gathered once - a count, then each sub-tile's run in one array.
        var starts = new int[subTiles.Count + 1];
        for (var pixel = 0; pixel < count; pixel++)
        {
            if (owner[pixel] >= 0 && triangleOf[pixel] >= 0)
            {
                starts[owner[pixel] + 1]++;
            }
        }

        for (var id = 0; id < subTiles.Count; id++)
        {
            starts[id + 1] += starts[id];
        }

        var gathered = new int[starts[^1]];
        var filled = (int[])starts.Clone();
        for (var pixel = 0; pixel < count; pixel++)
        {
            if (owner[pixel] >= 0 && triangleOf[pixel] >= 0)
            {
                gathered[filled[owner[pixel]]++] = pixel;
            }
        }

        int most = subTiles.Max(one => one.Candidates.Length);
        var found = new Found[subTiles.Count * most];
        Array.Fill(found, Found.None);
        int[] measured = [.. Enumerable.Range(0, subTiles.Count).Where(id => starts[id + 1] - starts[id] >= Least).OrderByDescending(id => starts[id + 1] - starts[id])];
        Parallel.ForEach(
            measured,
            () => new Dictionary<ShadeProgram, ShadeProgram.GroundReader>(),
            (id, _, readers) =>
            {
                Register(
                    gathered.AsSpan(starts[id], starts[id + 1] - starts[id]), width, triangleOf, triangles, detail, subTiles[id].Candidates, resolved, readers,
                    found.AsSpan(id * most, most));
                return readers;
            },
            _ => { });

        long[] pixels = [.. Enumerable.Range(0, subTiles.Count).Select(id => (long)(starts[id + 1] - starts[id]))];
        return Said(subTiles, found, most, pixels, resolved, owner, bgr, width, height, out picture);
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

    /// <summary>
    /// Every material of one sub-tile lined up with the picture: the detail summed in cells of the sub-tile's ground, each material read
    /// once a cell over the cells and a rim round them, then its correlation at every shift of the window and at the controls'.
    /// </summary>
    private static void Register(
        ReadOnlySpan<int> pixels, int width, int[] triangleOf, List<CaptureGroundCompare.Triangle> triangles, float[] detail, Candidate[] candidates,
        Dictionary<string, ShadeProgram?> programs, Dictionary<ShadeProgram, ShadeProgram.GroundReader> readers, Span<Found> found)
    {
        // WHERE EACH PIXEL LIES on the ground, and the triangle most of them lie on - whose footprint sets the levels the reads take.
        int n = pixels.Length;
        var xs = new float[n];
        var ys = new float[n];
        float leastX = float.MaxValue, leastY = float.MaxValue, mostX = float.MinValue, mostY = float.MinValue;
        double heights = 0;
        var votes = new Dictionary<int, int>();
        for (var at = 0; at < n; at++)
        {
            int pixel = pixels[at], triangle = triangleOf[pixel];
            Vector3 place = CaptureGroundCompare.PlaceAt(triangles[triangle], pixel % width, pixel / width);
            xs[at] = place.X;
            ys[at] = place.Y;
            heights += place.Z;
            leastX = MathF.Min(leastX, place.X);
            leastY = MathF.Min(leastY, place.Y);
            mostX = MathF.Max(mostX, place.X);
            mostY = MathF.Max(mostY, place.Y);
            votes[triangle] = votes.GetValueOrDefault(triangle) + 1;
        }

        int across = (int)((mostX - leastX) / Cell) + 1, down = (int)((mostY - leastY) / Cell) + 1;
        if (!float.IsFinite(leastX) || across <= 0 || down <= 0 || (long)across * down > 4_000_000)
        {
            return;
        }

        // THE DETAIL IN CELLS: each cell's sum and count, and the whole's - a pixel's own detail, so a cell of several weighs them all.
        var sum = new double[across * down];
        var weight = new int[across * down];
        double s1 = 0, s2 = 0;
        for (var at = 0; at < n; at++)
        {
            int cx = Math.Min(across - 1, (int)((xs[at] - leastX) / Cell)), cy = Math.Min(down - 1, (int)((ys[at] - leastY) / Cell));
            double value = detail[pixels[at]];
            sum[(cy * across) + cx] += value;
            weight[(cy * across) + cx]++;
            s1 += value;
            s2 += value * value;
        }

        double spread = s2 - (s1 * s1 / n);
        if (spread <= 1e-9)
        {
            return;
        }

        int stride = across + (2 * Rim), rows = down + (2 * Rim);
        var cellsAt = new List<int>();
        var sums = new List<double>();
        var weights = new List<double>();
        for (var cy = 0; cy < down; cy++)
        {
            for (var cx = 0; cx < across; cx++)
            {
                int cell = (cy * across) + cx;
                if (weight[cell] > 0)
                {
                    cellsAt.Add(((cy + Rim) * stride) + cx + Rim);
                    sums.Add(sum[cell]);
                    weights.Add(weight[cell]);
                }
            }
        }

        int[] occupied = [.. cellsAt];
        double[] cellSums = [.. sums], cellWeights = [.. weights];
        CaptureGroundCompare.Triangle footprint = triangles[votes.MaxBy(vote => vote.Value).Key];
        var z = (float)(heights / n);
        var values = new float[stride * rows];
        for (var k = 0; k < candidates.Length; k++)
        {
            // A MATERIAL TWO GROUPS OFFER is measured once.
            int earlier = Array.FindIndex(candidates, 0, k, one => string.Equals(one.Material, candidates[k].Material, StringComparison.OrdinalIgnoreCase));
            if (earlier >= 0)
            {
                found[k] = found[earlier];
                continue;
            }

            if (programs[candidates[k].Material] is not { } program)
            {
                continue;
            }

            if (!readers.TryGetValue(program, out ShadeProgram.GroundReader? reader))
            {
                reader = new ShadeProgram.GroundReader(program);
                readers[program] = reader;
            }

            reader.Triangle(footprint.Pa, footprint.Pb, footprint.Pc, footprint.A, footprint.B, footprint.C, GroundPainter.GroundScale);
            float lowest = float.MaxValue, highest = float.MinValue;
            for (var row = 0; row < rows; row++)
            {
                float y = leastY + ((row - Rim + 0.5f) * Cell);
                for (var column = 0; column < stride; column++)
                {
                    float value = reader.BrightnessAt(new Vector3(leastX + ((column - Rim + 0.5f) * Cell), y, z), GroundPainter.GroundScale);
                    values[(row * stride) + column] = value;
                    lowest = MathF.Min(lowest, value);
                    highest = MathF.Max(highest, value);
                }
            }

            if (highest - lowest < 1e-6f)
            {
                continue;
            }

            double best = double.NegativeInfinity;
            int bestX = 0, bestY = 0;
            for (int shiftY = -Window; shiftY <= Window; shiftY++)
            {
                for (int shiftX = -Window; shiftX <= Window; shiftX++)
                {
                    double r = Correlate((shiftY * stride) + shiftX, occupied, cellSums, cellWeights, values, s1, spread, n);
                    if (r > best)
                    {
                        (best, bestX, bestY) = (r, shiftX, shiftY);
                    }
                }
            }

            double[] away = [.. Away.Select(shift => Correlate((shift.Y * stride) + shift.X, occupied, cellSums, cellWeights, values, s1, spread, n)).Where(double.IsFinite)];
            (_, double mean, double spreadAway) = CaptureGroundCompare.Spread(best, away);
            found[k] = double.IsFinite(best) ? new Found(best, bestX, bestY, mean, spreadAway) : Found.None;
        }
    }

    /// <summary>The correlation of the detail with a material shifted by some cells - the material read at each cell's own place plus the shift.</summary>
    private static double Correlate(int shift, int[] cellsAt, double[] sums, double[] weights, float[] value, double s1, double spread, int n)
    {
        double sxy = 0, sx = 0, sxx = 0;
        for (var one = 0; one < cellsAt.Length; one++)
        {
            double v = value[cellsAt[one] + shift];
            double weighed = weights[one] * v;
            sxy += sums[one] * v;
            sx += weighed;
            sxx += weighed * v;
        }

        double vx = sxx - (sx * sx / n);
        return vx <= 1e-12 ? double.NaN : (sxy - (s1 * sx / n)) / Math.Sqrt(spread * vx);
    }

    /// <summary>What each sub-tile's materials came to, each group's wins beside its weights, each piece's sub-tiles, and the tinted picture.</summary>
    private static string Said(
        List<SubTile> subTiles, Found[] found, int most, long[] pixels, Dictionary<string, ShadeProgram?> programs, int[] owner, byte[] bgr,
        int width, int height, out GamePicture? picture)
    {
        Found Of(int id, int k) => found[(id * most) + k];
        string Tail(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));
        string Shift(Found one) => string.Create(CultureInfo.InvariantCulture, $"({one.ShiftX * Cell:+0;-0;0}, {one.ShiftY * Cell:+0;-0;0})");

        // EACH SUB-TILE'S WINNER, where it is clear - against the next MATERIAL, a material two groups offer being one.
        var winners = new int[subTiles.Count];
        var leads = new double[subTiles.Count];
        for (var id = 0; id < subTiles.Count; id++)
        {
            winners[id] = -1;
            Candidate[] candidates = subTiles[id].Candidates;
            int best = -1;
            for (var k = 0; k < candidates.Length; k++)
            {
                if (double.IsFinite(Of(id, k).R) && (best < 0 || Of(id, k).R > Of(id, best).R))
                {
                    best = k;
                }
            }

            if (best < 0)
            {
                continue;
            }

            double next = double.NaN;
            for (var k = 0; k < candidates.Length; k++)
            {
                if (double.IsFinite(Of(id, k).R) && !string.Equals(candidates[k].Material, candidates[best].Material, StringComparison.OrdinalIgnoreCase)
                    && (double.IsNaN(next) || Of(id, k).R > next))
                {
                    next = Of(id, k).R;
                }
            }

            leads[id] = double.IsFinite(next) ? Of(id, best).R - next : double.PositiveInfinity;
            winners[id] = Of(id, best).Z >= Clear && leads[id] >= Lead && pixels[id] >= Least ? best : -1;
        }

        // THE MATERIALS NAMED, each a colour of the picture.
        var materials = subTiles.SelectMany(one => one.Candidates.Select(candidate => candidate.Material)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        string ColourOf(string material) => Palette[materials.FindIndex(one => string.Equals(one, material, StringComparison.OrdinalIgnoreCase)) % Palette.Length].Name;

        long measured = Enumerable.Range(0, subTiles.Count).Where(id => pixels[id] >= Least).Sum(id => pixels[id]);
        int sized = Enumerable.Range(0, subTiles.Count).Count(id => pixels[id] >= Least);
        int oneGround = Enumerable.Range(0, subTiles.Count).Count(id => subTiles[id].Grounds.Count == 1 && pixels[id] >= Least);
        int clear = Enumerable.Range(0, subTiles.Count).Count(id => winners[id] >= 0);
        var said = new StringBuilder();
        said.Append(string.Create(CultureInfo.InvariantCulture,
            $"material choices: {measured} pixels in {sized} sub-tiles of at least {Least} pixels - every material each sub-tile's grounds' groups offer, read over the sub-tile's ground at the level the picture reads (its coordinates the world times {GroundPainter.GroundScale}) and held against the game's picture's detail (its brightness less its mean over nine by nine pixels), summed in cells of {Cell} units; each material shifted up to {Window * Cell} units each way for its best line-up (the stones line up within a few units or not at all), z how many spreads that stands over the same material shifted {Away[0].X * Cell} and {Away[32].X * Cell} units, where nothing lines up. A winner is clear at z {Clear} and {Lead} over the next material's best; {clear} of {sized} sub-tiles have one - {Enumerable.Range(0, subTiles.Count).Count(id => winners[id] >= 0 && subTiles[id].Grounds.Count == 1)} of the {oneGround} of one ground, the clean test\n"));
        foreach ((string material, ShadeProgram? program) in programs.Where(one => one.Value is null))
        {
            said.Append("  not read: ").Append(material).Append(" - its graphs did not compile or a texture did not read\n");
        }

        // WHERE THE WINNERS LINED UP: one shift throughout says the tool's place of a pixel is that far off the game's, the same everywhere.
        int[] lined = [.. Enumerable.Range(0, subTiles.Count).Where(id => winners[id] >= 0)];
        if (lined.Length > 0)
        {
            float[] alongX = [.. lined.Select(id => Of(id, winners[id]).ShiftX * Cell).Order()], alongY = [.. lined.Select(id => Of(id, winners[id]).ShiftY * Cell).Order()];
            float Middle(float[] sorted, float share) => sorted[Math.Clamp((int)MathF.Round(share * (sorted.Length - 1)), 0, sorted.Length - 1)];
            said.Append(string.Create(CultureInfo.InvariantCulture,
                $"  the clear winners lined up shifted by ({Middle(alongX, 0.5f):+0;-0;0}, {Middle(alongY, 0.5f):+0;-0;0}) units at the median - x {Middle(alongX, 0.25f):+0;-0;0} to {Middle(alongX, 0.75f):+0;-0;0} and y {Middle(alongY, 0.25f):+0;-0;0} to {Middle(alongY, 0.75f):+0;-0;0} over the middle half, all x {alongX[0]:+0;-0;0} to {alongX[^1]:+0;-0;0}, y {alongY[0]:+0;-0;0} to {alongY[^1]:+0;-0;0}\n"));
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

        // EACH SUB-TILE, one ground first: every material's best line-up, the winner marked.
        said.Append("  each sub-tile of at least ").Append(Least.ToString(CultureInfo.InvariantCulture))
            .Append(" pixels, one ground first - every material's best r, its z over the controls and the shift it lined up at, the winner marked *:\n");
        foreach (int id in Enumerable.Range(0, subTiles.Count).Where(id => pixels[id] >= Least).OrderBy(id => subTiles[id].Grounds.Count).ThenByDescending(id => pixels[id]))
        {
            SubTile one = subTiles[id];
            said.Append("    ").Append(one.Name).Append(string.Create(CultureInfo.InvariantCulture, $" · {pixels[id]} px · ")).Append(string.Join(" + ", one.Grounds)).Append(" ·");
            foreach (int k in Enumerable.Range(0, one.Candidates.Length).OrderByDescending(k => double.IsFinite(Of(id, k).R) ? Of(id, k).R : double.NegativeInfinity))
            {
                Found measure = Of(id, k);
                said.Append(' ').Append(k == winners[id] ? "*" : string.Empty).Append(Tail(one.Candidates[k].Material)).Append(' ')
                    .Append(double.IsFinite(measure.R)
                        ? string.Create(CultureInfo.InvariantCulture, $"r {measure.R:+0.000;-0.000} z {measure.Z:0.0} at {Shift(measure)}")
                        : "n/a (not read, or the field is even)")
                    .Append(';');
            }

            said.Append(winners[id] >= 0
                ? string.Create(CultureInfo.InvariantCulture, $" ahead by {leads[id]:0.000}")
                : double.IsFinite(leads[id]) ? string.Create(CultureInfo.InvariantCulture, $" no clear winner (ahead by {leads[id]:0.000})") : " no clear winner").Append('\n');
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
