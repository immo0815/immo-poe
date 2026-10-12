using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// The laid tiles' ground over the game's own picture: every sub-tile of two grounds or more outlined and named, and the ground
/// shapes in it whose coordinates run across the mask's atlas drawn - so which ground the game draws there is read off its picture.
/// </summary>
/// <remarks>
/// BUILT FOR THE CHECKERBOARD (v0.1.251). A sub-tile of two grounds is mixed by the template mask's green at the block's own
/// coordinates, as the engine's GroundGenerateWeights_1a1b mixes them. Most of Port's harbour shapes point into one cell of
/// newmask.dds - green 0.02 or 1.00, one ground or the other - but its slopes carry coordinates laid across the mask a third a
/// sub-tile, and read there the atlas's 64-texel cells come out as squares of the two grounds (v0.1.248's ground.txt: the shore
/// strip c1r6 to c6r6, and from v0.1.250 the pier's edge c13r10, c13r11 and c14r11). The game draws no checkerboard there, and
/// the shader text does not say why: which coordinate fills <c>uv2</c> is wired in the engine's own code. So the game's picture
/// answers it - this lays the same sub-tiles over it where the Monster Book's laid room has them (LaidRoomModels.GroundsOver),
/// and the ground the game drew under each slope is the measurement.
///
/// PRINT BOTH HALVES: each sub-tile's line gives the grid's corners and what they come to beside the game's own corner types at
/// that place (TileCornerData), so a sub-tile drawn in the wrong place shows as a disagreement in the file before the picture is
/// believed.
/// </remarks>
public static class CaptureGroundScreen
{
    /// <summary>The ground masks' atlas cell, in texels: newmask.dds is four by eight cells of 64 (v0.1.199).</summary>
    private const int Cell = 64;

    /// <summary>How many tiles past the screen's corners on the player's plane the block reaches - ground above or below it runs past them.</summary>
    private const int Margin = 3;

    /// <summary>The most tiles each way, round the player - a screen showing more is looking past the area.</summary>
    private const int MostTiles = 64;

    /// <summary>How big a label's pixel is.</summary>
    private const int LabelScale = 3;

    private static readonly byte[] White = [255, 255, 255];
    private static readonly byte[] Orange = [255, 160, 0];
    private static readonly byte[] Magenta = [255, 0, 255];
    private static readonly byte[] Black = [0, 0, 0];

    /// <summary>
    /// The ground on the screen, said; and the picture drawn over, or null where there is nothing to draw on or with.
    /// </summary>
    /// <param name="read">The install.</param>
    /// <param name="snapshot">The world as the key went down: the terrain, its tiles, the camera.</param>
    /// <param name="loaded">The files the area loaded, which the area's masters are found from - see AreaRoomSet.Masters.</param>
    /// <param name="masters">Every master (<c>.tsi</c>) the install has, as its index lists them.</param>
    /// <param name="bgr">The game's picture, three bytes a pixel blue first, top row first - or null for a black one.</param>
    /// <param name="width">The picture's width, which is the screen's.</param>
    /// <param name="height">Its height.</param>
    /// <param name="pictureSaid">What the picture is, for the first line.</param>
    /// <param name="picture">The picture drawn over.</param>
    public static string Write(
        Func<string, byte[]?> read,
        WorldSnapshot snapshot,
        IReadOnlyList<string> loaded,
        IReadOnlyList<string> masters,
        byte[]? bgr,
        int width,
        int height,
        string pictureSaid,
        out GamePicture? picture)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(loaded);
        ArgumentNullException.ThrowIfNull(masters);
        picture = null;
        if (snapshot.Terrain is not { Tiles: not null } grid)
        {
            return "ground on screen: the area's tiles were not read\n";
        }

        if (snapshot.Player is not { } player || snapshot.Matrix is not { Length: >= 16 } matrix || width <= 0 || height <= 0)
        {
            return "ground on screen: no player, camera or screen to project with\n";
        }

        (int x0, int y0, int x1, int y1) = Block(matrix, player, grid, width, height);
        List<LaidGround> laid = LaidRoomModels.GroundsOver(read, grid, x0, y0, x1 - x0 + 1, y1 - y0 + 1);

        // THE GROUND TYPES AT THE LAID PIECES' CORNERS, which the MaterialsList painting them must name.
        var types = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in laid.Select(one => one.Path).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            (TileDefinition definition, _) = TileModels.Defined(read, path);
            types.UnionWith(definition.Grounds.Concat(definition.GroundGrid).Where(corner => corner.Length > 0));
        }

        (GroundPainter? painter, string chosen, IReadOnlyList<string> weighed) = MasterOf(read, AreaRoomSet.Masters(loaded, masters), types, new MonsterModels.Paints());
        if (painter is null)
        {
            return "ground on screen: no master of the area names a MaterialsList to name the grounds with\n"
                + string.Concat(weighed.Select(line => "  " + line + "\n"));
        }

        var canvas = new Canvas(width, height, bgr);
        var body = new StringBuilder();
        var lines = new StringBuilder();
        int mixed = 0, wideOnes = 0, offScreen = 0;
        foreach (LaidGround one in laid)
        {
            int triangles = one.Ground.Indices.Length / 3;
            (IReadOnlyList<SubTileGrounds> subTiles, string mask, GamePicture? maskPicture) = painter.SubTilesOf(one.Path, one.Pieces, triangles);
            lines.Clear();
            int oneGround = 0, several = 0, away = 0;
            var plain = new SortedDictionary<string, int>(StringComparer.Ordinal);

            int from = 0;
            for (var at = 0; at < one.Pieces.Count && at < subTiles.Count; at++)
            {
                GroundPiece piece = one.Pieces[at];
                SubTileGrounds grounds = subTiles[at];
                int first = from;
                from += piece.Triangles;
                if (grounds.Grounds.Count == 1)
                {
                    oneGround++;
                    continue;
                }

                if (grounds.Grounds.Count == 0 && !grounds.Why.StartsWith("of three", StringComparison.Ordinal))
                {
                    plain[grounds.Why] = plain.GetValueOrDefault(grounds.Why) + 1;
                    continue;
                }

                several++;
                (Vector3[] corners, Vector3 middle) = Outline(one, piece, grid);
                if (!Screen(matrix, middle, width, height, out Vector2 centre) || centre.X < 0 || centre.Y < 0 || centre.X >= width || centre.Y >= height)
                {
                    away++;
                    offScreen++;
                    continue;
                }

                mixed++;
                List<ShapeSeen> shapes = Shapes(one, first, piece.Triangles, maskPicture);
                bool wide = shapes.Exists(shape => shape.Wide);
                wideOnes += wide ? 1 : 0;

                foreach (ShapeSeen shape in shapes.Where(shape => shape.Wide))
                {
                    for (int triangle = shape.First; triangle < shape.First + shape.Triangles; triangle++)
                    {
                        Vector3 a = Laid(one, triangle, 0), b = Laid(one, triangle, 1), c = Laid(one, triangle, 2);
                        Edge(canvas, matrix, a, b, Magenta);
                        Edge(canvas, matrix, b, c, Magenta);
                        Edge(canvas, matrix, c, a, Magenta);
                    }
                }

                byte[] rim = grounds.Grounds.Count >= 2 ? White : Orange;
                for (var corner = 0; corner < 4; corner++)
                {
                    Edge(canvas, matrix, corners[corner], corners[(corner + 1) % 4], rim);
                }

                string name = string.Create(CultureInfo.InvariantCulture, $"c{piece.X}r{piece.Y}");
                canvas.Label(name, (int)centre.X, (int)centre.Y);

                int tileX = (int)MathF.Floor(middle.X / TileModels.Side), tileY = (int)MathF.Floor(middle.Y / TileModels.Side);
                lines.Append("  ").Append(name)
                    .Append(string.Create(CultureInfo.InvariantCulture, $" · area tile {tileX}, {tileY} · on screen {centre.X:F0}, {centre.Y:F0}"))
                    .Append(" · grid corners ").Append(string.Join(", ", grounds.Corners.Select(Short)))
                    .Append(" → ").Append(grounds.Grounds.Count > 0 ? string.Join(" + ", grounds.Grounds.Select(group => group.Length > 0 ? group : "(the group with no name)")) : grounds.Why)
                    .Append(" · the game's corners there ").AppendLine(GameCorners(grid, tileX, tileY));
                foreach (ShapeSeen shape in shapes)
                {
                    lines.Append(string.Create(CultureInfo.InvariantCulture,
                        $"    shape {shape.Index} · {shape.Triangles} triangles · z {shape.Low:F0}..{shape.High:F0} · green {shape.Green:F2} · the mask's coordinates span {shape.SpanU:F0} x {shape.SpanV:F0} texels over {shape.Cells} cells"))
                        .AppendLine(shape.Wide ? " · ACROSS THE ATLAS (drawn)" : string.Empty);
                }
            }

            // EVERY SUB-TILE ACCOUNTED FOR on the piece's own line: of one ground, of several (and how many of those off the screen), or
            // plain and why - so a piece that draws nothing says which it was, not merely nothing.
            body.Append("== ").Append(one.Path)
                .Append(string.Create(CultureInfo.InvariantCulture, $" at tile {one.Piece.MinX}, {one.Piece.MinY}, {one.Piece.Width} x {one.Piece.Height}, placement {one.Piece.Placement}"))
                .Append(" · mask ").Append(mask.Length == 0 ? "none" : mask + (maskPicture is { } decoded ? string.Create(CultureInfo.InvariantCulture, $" ({decoded.Width} x {decoded.Height})") : " (did not read)"))
                .Append(string.Create(CultureInfo.InvariantCulture, $" · sub-tiles: {oneGround} of one ground, {several} of two or more"))
                .Append(away > 0 ? string.Create(CultureInfo.InvariantCulture, $" ({away} off the screen)") : string.Empty)
                .AppendLine(string.Concat(plain.Select(why => string.Create(CultureInfo.InvariantCulture, $", {why.Value} plain {why.Key}"))));
            body.Append(lines);
        }

        picture = canvas.Picture;
        var said = new StringBuilder();
        said.Append(string.Create(CultureInfo.InvariantCulture,
            $"ground on screen: {mixed} sub-tiles of two or more grounds on the screen, {wideOnes} of them with ground shapes across the mask's atlas; {laid.Count} pieces over tiles {x0}..{x1}, {y0}..{y1}"))
            .Append(offScreen > 0 ? string.Create(CultureInfo.InvariantCulture, $", {offScreen} such sub-tiles off the screen") : string.Empty)
            .Append(" - over ").AppendLine(pictureSaid);
        said.Append("grounds named by ").Append(chosen).Append("'s MaterialsList (").Append(painter.From)
            .AppendLine(") - of the area's masters, the one whose list names the most of the ground types at the laid pieces' corners:");
        foreach (string line in weighed)
        {
            said.Append("  ").AppendLine(line);
        }

        said.AppendLine();
        said.AppendLine("Each sub-tile of two grounds is outlined white (orange: three or four) and named as ground.txt names its mesh, cXrY; the ground shapes in it whose");
        said.AppendLine("coordinates span a cell of the mask's atlas or more are drawn magenta, triangle by triangle - where the tool's mix reads the atlas's cells as squares.");
        said.AppendLine("Placed as the Monster Book's laid room places the pieces (LaidRoomModels.GroundsOver), projected with the camera as the key went down.");
        said.AppendLine("The grid's corners are a sub-tile's own (least x, then most x; least y first in the template), '-' unnamed; the game's are TileCornerData's at the");
        said.AppendLine("area tile under the sub-tile's middle (x, x + 1 at y, then at y + 1), '-' its null slot - the same four names in some order where the two agree.");
        said.AppendLine();
        said.Append(body);
        return said.ToString();
    }

    /// <summary>
    /// Of the area's masters, the one whose MaterialsList names the most of the given ground types - the first on a tie - and what
    /// each came to, a line a master.
    /// </summary>
    /// <remarks>
    /// THE AREA'S OWN, AND NOT THE FIRST. v0.1.251 took the first master of the install's whole index that named any list, a list of
    /// another tileset, and every sub-tile came out plain - nought sub-tiles of two grounds in five areas. And among an area's own the
    /// first is not it either: Act 4's areas list worldmaps/act4/master.tsi first, whose MaterialsList holds the unnamed group alone,
    /// while the area's own (gallows/act4/4_3_1, 4_10, 4_13) names its grounds (v0.1.251's captures of Whakapanu Island, The
    /// Excavation and Plunder's Point). Which list the game paints an area's ground from is the engine's; the one naming the grounds
    /// laid is the one that can.
    /// </remarks>
    internal static (GroundPainter? Painter, string Master, IReadOnlyList<string> Weighed) MasterOf(
        Func<string, byte[]?> read, IEnumerable<string> masters, IReadOnlyCollection<string> types, MonsterModels.Paints paints)
    {
        GroundPainter? best = null;
        string chosen = string.Empty;
        int most = -1;
        var weighed = new List<string>();
        foreach (string master in masters)
        {
            (GroundPainter? painter, string why) = GroundPainter.For(read, master, paints);
            if (painter is null)
            {
                weighed.Add(master + ": " + why);
                continue;
            }

            int named = types.Count(painter.Lists);
            weighed.Add(string.Create(CultureInfo.InvariantCulture, $"{master}: its MaterialsList {painter.From} names {named} of {types.Count}"));
            if (named > most)
            {
                (best, chosen, most) = (painter, master, named);
            }
        }

        return (best, chosen, weighed);
    }

    /// <summary>One ground shape of a sub-tile, as the mask sees it.</summary>
    private readonly record struct ShapeSeen(int Index, int First, int Triangles, float Low, float High, float Green, float SpanU, float SpanV, int Cells, bool Wide);

    /// <summary>The tiles the screen shows on the player's plane, with a margin, round the player and no more than <see cref="MostTiles"/> each way.</summary>
    private static (int X0, int Y0, int X1, int Y1) Block(float[] matrix, WorldEntity player, TerrainGrid grid, int width, int height)
    {
        float least = float.MaxValue, mostX = float.MinValue, leastY = float.MaxValue, mostY = float.MinValue;
        foreach ((float sx, float sy) in (ReadOnlySpan<(float, float)>)[(0, 0), (width, 0), (0, height), (width, height), (width / 2f, 0), (width / 2f, height), (0, height / 2f), (width, height / 2f)])
        {
            if (WorldToScreen.OnGround(matrix, sx, sy, player.WorldZ, width, height) is { } on)
            {
                least = MathF.Min(least, on.X);
                mostX = MathF.Max(mostX, on.X);
                leastY = MathF.Min(leastY, on.Y);
                mostY = MathF.Max(mostY, on.Y);
            }
        }

        int playerX = (int)MathF.Floor(player.WorldX / TileModels.Side), playerY = (int)MathF.Floor(player.WorldY / TileModels.Side);
        int half = MostTiles / 2;
        if (least > mostX)
        {
            least = mostX = player.WorldX;
            leastY = mostY = player.WorldY;
        }

        int x0 = Math.Max(Math.Max(0, playerX - half), (int)MathF.Floor(least / TileModels.Side) - Margin);
        int x1 = Math.Min(Math.Min(grid.TilesX - 1, playerX + half), (int)MathF.Floor(mostX / TileModels.Side) + Margin);
        int y0 = Math.Max(Math.Max(0, playerY - half), (int)MathF.Floor(leastY / TileModels.Side) - Margin);
        int y1 = Math.Min(Math.Min(grid.TilesY - 1, playerY + half), (int)MathF.Floor(mostY / TileModels.Side) + Margin);
        return (x0, y0, Math.Max(x0, x1), Math.Max(y0, y1));
    }

    /// <summary>A sub-tile's four corners in the world, on the area's ground, and its middle - its mesh lies at ((x - 1) * 250, -(y - 1) * 250) in the template.</summary>
    private static (Vector3[] Corners, Vector3 Middle) Outline(LaidGround one, GroundPiece piece, TerrainGrid grid)
    {
        float left = (piece.X - 1) * TileModels.Side, bottom = -(piece.Y - 1) * TileModels.Side;
        Vector3 On(float x, float y)
        {
            Vector3 at = Vector3.Transform(new Vector3(x, y, 0f), one.Place);
            return at with { Z = grid.HeightAt((int)MathF.Floor(at.X / RoomModels.CellSize), (int)MathF.Floor(at.Y / RoomModels.CellSize)) };
        }

        Vector3[] corners = [On(left, bottom), On(left + TileModels.Side, bottom), On(left + TileModels.Side, bottom + TileModels.Side), On(left, bottom + TileModels.Side)];
        return (corners, On(left + (TileModels.Side / 2f), bottom + (TileModels.Side / 2f)));
    }

    /// <summary>The ground shapes of one sub-tile's run of triangles: their heights, the mask's green and how far their coordinates reach across it.</summary>
    private static List<ShapeSeen> Shapes(LaidGround one, int first, int count, GamePicture? mask)
    {
        var seen = new List<ShapeSeen>();
        SkinnedMesh ground = one.Ground;
        int end = first + count;
        for (var index = 0; index < ground.Shapes.Count; index++)
        {
            MeshShape shape = ground.Shapes[index];
            int from = Math.Max(first, shape.From / 3), to = Math.Min(end, (shape.From + shape.Count) / 3);
            if (from >= to)
            {
                continue;
            }

            float low = float.MaxValue, high = float.MinValue, green = 0f;
            float leastU = float.MaxValue, mostU = float.MinValue, leastV = float.MaxValue, mostV = float.MinValue;
            var cells = new HashSet<(int, int)>();
            int corners = 0;
            int maskWidth = mask?.Width ?? 0, maskHeight = mask?.Height ?? 0;
            for (int triangle = from; triangle < to; triangle++)
            {
                for (var corner = 0; corner < 3; corner++)
                {
                    int vertex = ground.Indices[(triangle * 3) + corner];
                    float z = ground.Positions[vertex].Z;
                    low = MathF.Min(low, z);
                    high = MathF.Max(high, z);
                    if (vertex >= ground.Coordinates.Length || mask is not { } picture)
                    {
                        continue;
                    }

                    Vector2 uv = ground.Coordinates[vertex];
                    green += GroundPainter.Sample(picture, 1, uv.X, uv.Y);
                    corners++;
                    leastU = MathF.Min(leastU, uv.X);
                    mostU = MathF.Max(mostU, uv.X);
                    leastV = MathF.Min(leastV, uv.Y);
                    mostV = MathF.Max(mostV, uv.Y);
                    cells.Add(((int)MathF.Floor(uv.X * maskWidth / Cell), (int)MathF.Floor(uv.Y * maskHeight / Cell)));
                }
            }

            float spanU = corners > 0 ? (mostU - leastU) * maskWidth : 0f, spanV = corners > 0 ? (mostV - leastV) * maskHeight : 0f;
            seen.Add(new ShapeSeen(
                index, from, to - from, low, high, corners > 0 ? green / corners : float.NaN, spanU, spanV, cells.Count, spanU >= Cell || spanV >= Cell));
        }

        return seen;
    }

    /// <summary>One corner of one triangle of the block, in the world.</summary>
    private static Vector3 Laid(LaidGround one, int triangle, int corner)
        => Vector3.Transform(one.Ground.Positions[one.Ground.Indices[(triangle * 3) + corner]], one.Place);

    /// <summary>The game's own four corner types round an area tile, by name, '-' for its null slot.</summary>
    private static string GameCorners(TerrainGrid grid, int tileX, int tileY)
    {
        if (grid.Ground is not { } ground)
        {
            return "(not read)";
        }

        string Name(int type) => type <= 0 || type >= ground.Types.Count || ground.Types[type].Length == 0 ? "-" : TerrainGroundTypes.NameFor(ground.Types[type]);
        return string.Join(", ", Name(ground.At(tileX, tileY)), Name(ground.At(tileX + 1, tileY)), Name(ground.At(tileX, tileY + 1)), Name(ground.At(tileX + 1, tileY + 1)));
    }

    /// <summary>A ground type's file by its name, '-' where unnamed.</summary>
    private static string Short(string path) => path.Length == 0 ? "-" : TerrainGroundTypes.NameFor(path.Replace('\\', '/'));

    /// <summary>Where a world point lands on the screen, or false behind the camera - off the screen in front of it still lands somewhere.</summary>
    private static bool Screen(float[] matrix, Vector3 point, int width, int height, out Vector2 at)
    {
        (double x, double y, double w) = WorldToScreen.Clip(matrix, point.X, point.Y, point.Z);
        if (w <= 1e-6)
        {
            at = default;
            return false;
        }

        at = new Vector2((float)((((x / w) * 0.5) + 0.5) * width), (float)((0.5 - ((y / w) * 0.5)) * height));
        return true;
    }

    /// <summary>A world edge projected and drawn, where both ends are in front of the camera.</summary>
    private static void Edge(Canvas canvas, float[] matrix, Vector3 from, Vector3 to, byte[] colour)
    {
        if (Screen(matrix, from, canvas.Width, canvas.Height, out Vector2 a) && Screen(matrix, to, canvas.Width, canvas.Height, out Vector2 b))
        {
            canvas.Line(a, b, colour);
        }
    }

    /// <summary>
    /// The picture drawn over: the game's own, as RGBA, with lines and labels laid on it.
    /// </summary>
    internal sealed class Canvas
    {
        /// <summary>
        /// The digits and the two letters a sub-tile's name needs, three pixels by five, a row a three-bit number, the left pixel the high bit.
        /// </summary>
        private static readonly Dictionary<char, int[]> Glyphs = new()
        {
            ['0'] = [7, 5, 5, 5, 7],
            ['1'] = [2, 6, 2, 2, 7],
            ['2'] = [7, 1, 7, 4, 7],
            ['3'] = [7, 1, 7, 1, 7],
            ['4'] = [5, 5, 7, 1, 1],
            ['5'] = [7, 4, 7, 1, 7],
            ['6'] = [7, 4, 7, 5, 7],
            ['7'] = [7, 1, 1, 1, 1],
            ['8'] = [7, 5, 7, 5, 7],
            ['9'] = [7, 5, 7, 1, 7],
            ['c'] = [0, 7, 4, 4, 7],
            ['r'] = [0, 5, 6, 4, 4],
        };

        /// <param name="width">Pixels across.</param>
        /// <param name="height">Pixels down.</param>
        /// <param name="bgr">The picture to draw over, three bytes a pixel blue first - or null for black.</param>
        public Canvas(int width, int height, byte[]? bgr)
        {
            Width = width;
            Height = height;
            Rgba = new byte[width * height * 4];
            bool whole = bgr is not null && bgr.LongLength >= (long)width * height * 3;
            for (int pixel = 0, from = 0, to = 0; pixel < width * height; pixel++, from += 3, to += 4)
            {
                if (whole)
                {
                    Rgba[to] = bgr![from + 2];
                    Rgba[to + 1] = bgr[from + 1];
                    Rgba[to + 2] = bgr[from];
                }

                Rgba[to + 3] = 255;
            }
        }

        public int Width { get; }

        public int Height { get; }

        public byte[] Rgba { get; }

        public GamePicture Picture => new(Width, Height, Rgba);

        /// <summary>A line between two points, clipped to the picture - one far off it, a projection gone wide, drawn not at all.</summary>
        public void Line(Vector2 from, Vector2 to, byte[] colour)
        {
            float reach = 4f * (Width + Height);
            if (!float.IsFinite(from.X) || !float.IsFinite(from.Y) || !float.IsFinite(to.X) || !float.IsFinite(to.Y)
                || MathF.Abs(from.X) > reach || MathF.Abs(from.Y) > reach || MathF.Abs(to.X) > reach || MathF.Abs(to.Y) > reach)
            {
                return;
            }

            if ((from.X < 0 && to.X < 0) || (from.Y < 0 && to.Y < 0) || (from.X >= Width && to.X >= Width) || (from.Y >= Height && to.Y >= Height))
            {
                return;
            }

            int x0 = (int)MathF.Round(from.X), y0 = (int)MathF.Round(from.Y), x1 = (int)MathF.Round(to.X), y1 = (int)MathF.Round(to.Y);
            int dx = Math.Abs(x1 - x0), dy = -Math.Abs(y1 - y0), sx = x0 < x1 ? 1 : -1, sy = y0 < y1 ? 1 : -1, error = dx + dy;
            while (true)
            {
                Set(x0, y0, colour);
                if (x0 == x1 && y0 == y1)
                {
                    break;
                }

                int twice = 2 * error;
                if (twice >= dy)
                {
                    error += dy;
                    x0 += sx;
                }

                if (twice <= dx)
                {
                    error += dx;
                    y0 += sy;
                }
            }
        }

        /// <summary>Text centred on a point, white on a black rim so it reads over any ground.</summary>
        public void Label(string text, int x, int y)
        {
            int step = 4 * LabelScale;
            int left = x - ((text.Length * step) / 2), top = y - ((5 * LabelScale) / 2);
            for (var rim = 0; rim < 2; rim++)
            {
                for (var at = 0; at < text.Length; at++)
                {
                    if (!Glyphs.TryGetValue(text[at], out int[]? rows))
                    {
                        continue;
                    }

                    for (var row = 0; row < 5; row++)
                    {
                        for (var column = 0; column < 3; column++)
                        {
                            if ((rows[row] & (4 >> column)) == 0)
                            {
                                continue;
                            }

                            int px = left + (at * step) + (column * LabelScale), py = top + (row * LabelScale);
                            int grow = rim == 0 ? 1 : 0;
                            for (int dy = -grow; dy < LabelScale + grow; dy++)
                            {
                                for (int dx = -grow; dx < LabelScale + grow; dx++)
                                {
                                    Set(px + dx, py + dy, rim == 0 ? Black : White);
                                }
                            }
                        }
                    }
                }
            }
        }

        private void Set(int x, int y, byte[] colour)
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            {
                return;
            }

            int at = ((y * Width) + x) * 4;
            Rgba[at] = colour[0];
            Rgba[at + 1] = colour[1];
            Rgba[at + 2] = colour[2];
        }
    }
}
