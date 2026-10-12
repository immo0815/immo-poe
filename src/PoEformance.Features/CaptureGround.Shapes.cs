using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>The ground's shapes sub-tile by sub-tile and the tile definitions' unread bytes - see <see cref="CaptureGround"/>.</summary>
/// <remarks>
/// ASKED FOR BY PORT'S CHECKERBOARD (v0.1.246, three captures from the live client). Mixed per pixel by the mask's green, Port's
/// harbour came out in squares of black and cobblestone with straight, stepped sides - on its shallow floor under the water and on a
/// path under the bridge alike - and the probe put the dark square at green 1.000 and the light one beside it at 0.004, both of the
/// boss's one tile. The engine's mix (ground.ffx) is plain: the green at the block's own coordinates, first_ground and second_ground
/// being the material slots 0 and 1. What it does NOT say is which two materials the engine puts in those slots for a piece of
/// ground, or whether it inverts the green there (<c>invert_g_mask</c>, an object uniform) - and the files hold three things nothing
/// reads that could carry it: each ground SHAPE's u16 before its box (poe_data_tools' ordinal - a sub-tile's ground comes in one to
/// five shapes), the tile mesh's unnamed header word and byte and its tail entries, and everything in the tile definition past its
/// corners (zao's side offsets, sub-tile blocks and ground grid). So the part writes them, with each shape's heights and the green at
/// its vertices, and draws each masked template from above three ways - the shapes by ordinal, the green as the mix reads it, the
/// height - so a checkerboard can be held against the shapes and the sub-tiles by eye before any rule is written.
/// </remarks>
public static partial class CaptureGround
{
    /// <summary>Most templates drawn from above; the biggest first.</summary>
    public const int MostShapePictures = 12;

    /// <summary>Most bytes of one tile definition written past what is read.</summary>
    public const int MostRestBytes = 32768;

    /// <summary>Most bytes of tile definitions written past what is read, over the area.</summary>
    public const int MostRestsBytes = 1 << 20;

    /// <summary>A picture's pixels a world unit: forty a sub-tile.</summary>
    private const float PictureScale = 40f / TileModels.Side;

    /// <summary>The gap between a picture's three panels, in pixels.</summary>
    private const int PanelGap = 4;

    /// <summary>A masked template's sub-tile meshes as read, with where each sits.</summary>
    private sealed class Pieces(string mask, int width, int height)
    {
        public string Mask { get; } = mask;

        public int Width { get; } = width;

        public int Height { get; } = height;

        public List<(int X, int Y, TileMesh Mesh)> All { get; } = [];
    }

    /// <summary>The PNG a masked template is drawn from above to, beside ground.txt.</summary>
    private static string ShapesFile(int number, string template)
        => string.Create(CultureInfo.InvariantCulture, $"ground-shapes-{number}-{Path.GetFileNameWithoutExtension(template)}.png");

    /// <summary>
    /// Every masked template's ground shapes sub-tile by sub-tile, written; and the biggest templates drawn from above.
    /// </summary>
    private static List<(string File, GamePicture Picture)> ShapePictures(
        SortedDictionary<string, Pieces> pieces, IReadOnlyList<GroundMask> masks, StringBuilder said)
    {
        said.AppendLine().AppendLine(
            "=== the ground shapes sub-tile by sub-tile - for every template with a ground mask: each sub-tile mesh's header (its shape count, "
            + "its unnamed word and byte, its tail entries) and each ground shape's ordinal, the u16 before its box that neither reference "
            + "names, with its triangles, heights (up being minus), the mask's green at its vertices as GetUserMask reads it, its coordinates "
            + "and the mask's 64-texel cells they fall in (column, row); then each tail entry as poe_data_tools reads it - a word, twelve "
            + "floats, a word, 31 bytes. The biggest templates are drawn from above beside this file, ground-shapes-N-name.png: the shapes "
            + "coloured by ordinal, the green as the mix reads it per pixel, and the height light where high - row 1 at the top, "
            + "sub-tiles ruled");
        var decoded = masks.Where(mask => mask.Picture is not null)
            .ToDictionary(mask => mask.Path, mask => mask.Picture!.Value, StringComparer.OrdinalIgnoreCase);
        var drawnFirst = new HashSet<string>(
            pieces.OrderByDescending(pair => pair.Value.All.Count).ThenBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Take(MostShapePictures).Select(pair => pair.Key),
            StringComparer.OrdinalIgnoreCase);
        var drawn = new List<(string File, GamePicture Picture)>();
        foreach ((string template, Pieces taken) in pieces)
        {
            GamePicture? mask = decoded.TryGetValue(taken.Mask, out GamePicture found) ? found : null;
            var tally = new SortedDictionary<int, (int Shapes, int Triangles)>();
            foreach ((_, _, TileMesh mesh) in taken.All)
            {
                for (var shape = 0; shape < mesh.GroundOrdinals.Count && shape < mesh.Ground.Shapes.Count; shape++)
                {
                    int ordinal = mesh.GroundOrdinals[shape];
                    (int shapes, int triangles) = tally.GetValueOrDefault(ordinal);
                    tally[ordinal] = (shapes + 1, triangles + (mesh.Ground.Shapes[shape].Count / 3));
                }
            }

            said.AppendLine().Append(CultureInfo.InvariantCulture,
                $"  {template}  ({Short(taken.Mask)}{(mask is null ? ", not decoded" : string.Empty)})  {taken.Width}x{taken.Height} · {taken.All.Count} sub-tiles with a ground · ordinals: ")
                .Append(string.Join(", ", tally.Select(pair => string.Create(CultureInfo.InvariantCulture, $"{pair.Key} in {pair.Value.Shapes} shapes ({pair.Value.Triangles} triangles)"))));
            if (drawnFirst.Contains(template))
            {
                string file = ShapesFile(drawn.Count + 1, template);
                drawn.Add((file, Drawn(taken, mask, tally.Keys)));
                said.Append(" · picture ").Append(file);
            }

            said.AppendLine();
            foreach ((int x, int y, TileMesh mesh) in taken.All)
            {
                SubTile(x, y, mesh, mask, said);
            }
        }

        return drawn;
    }

    /// <summary>One sub-tile mesh's header, its ground shapes and its tail entries.</summary>
    private static void SubTile(int x, int y, TileMesh mesh, GamePicture? mask, StringBuilder said)
    {
        SkinnedMesh ground = mesh.Ground;
        int tails = mesh.TailData.Length / TileMesh.TailEntryBytes;
        said.Append(CultureInfo.InvariantCulture,
            $"    c{x}r{y} · header {mesh.HeaderShapes} shapes, word {mesh.HeaderWord}, byte {mesh.HeaderByte}, {tails} tails{(mesh.Exact ? string.Empty : " · NOT EXACT to the file's end")}")
            .AppendLine();

        var seen = new bool[ground.Positions.Length];
        for (var shape = 0; shape < ground.Shapes.Count; shape++)
        {
            MeshShape part = ground.Shapes[shape];
            Array.Clear(seen);
            int vertices = 0, over = 0;
            float low = float.MaxValue, high = float.MinValue, greens = 0;
            float uLeast = float.MaxValue, uMost = float.MinValue, vLeast = float.MaxValue, vMost = float.MinValue;
            var cells = new SortedSet<int>();
            int end = Math.Min(ground.Indices.Length, part.From + part.Count);
            for (int at = Math.Max(0, part.From); at < end; at++)
            {
                int vertex = ground.Indices[at];
                if (seen[vertex])
                {
                    continue;
                }

                seen[vertex] = true;
                vertices++;
                float z = ground.Positions[vertex].Z;
                (low, high) = (MathF.Min(low, z), MathF.Max(high, z));
                if (!ground.Coordinated)
                {
                    continue;
                }

                Vector2 uv = ground.Coordinates[vertex];
                (uLeast, uMost) = (MathF.Min(uLeast, uv.X), MathF.Max(uMost, uv.X));
                (vLeast, vMost) = (MathF.Min(vLeast, uv.Y), MathF.Max(vMost, uv.Y));
                if (mask is { } picture)
                {
                    float green = GroundPainter.Sample(picture, 1, uv.X, uv.Y);
                    greens += green;
                    over += green >= 0.5f ? 1 : 0;
                    int columns = Math.Max(1, picture.Width / 64), rows = Math.Max(1, picture.Height / 64);
                    int column = (int)((uv.X - MathF.Floor(uv.X)) * columns) % columns;
                    int row = (int)((uv.Y - MathF.Floor(uv.Y)) * rows) % rows;
                    cells.Add((row * columns) + column);
                }
            }

            int ordinal = shape < mesh.GroundOrdinals.Count ? mesh.GroundOrdinals[shape] : -1;
            said.Append(CultureInfo.InvariantCulture, $"      shape {shape} · ordinal {ordinal} · {part.Count / 3} triangles, {vertices} vertices");
            if (vertices > 0)
            {
                said.Append(CultureInfo.InvariantCulture, $" · z {low:0}..{high:0}");
            }

            if (ground.Coordinated && vertices > 0)
            {
                said.Append(CultureInfo.InvariantCulture, $" · u {uLeast:0.###}..{uMost:0.###} v {vLeast:0.###}..{vMost:0.###}");
            }

            if (mask is { } read && vertices > 0)
            {
                int columns = Math.Max(1, read.Width / 64);
                said.Append(CultureInfo.InvariantCulture, $" · green {greens / vertices:0.00} ({100.0 * over / vertices:0}% at a half or more) · cells ")
                    .Append(string.Join(" ", cells.Take(8).Select(cell => string.Create(CultureInfo.InvariantCulture, $"{cell % columns},{cell / columns}"))));
                if (cells.Count > 8)
                {
                    said.Append(CultureInfo.InvariantCulture, $" +{cells.Count - 8}");
                }
            }

            said.AppendLine();
        }

        for (var entry = 0; entry < tails; entry++)
        {
            (uint first, float[] floats, uint second, byte[] bytes) = mesh.Tail(entry);
            said.Append(CultureInfo.InvariantCulture, $"      tail {entry} · {first} · ")
                .Append(string.Join(", ", floats.Select(value => value.ToString("G6", CultureInfo.InvariantCulture))))
                .Append(CultureInfo.InvariantCulture, $" · {second} · ")
                .AppendLine(Convert.ToHexString(bytes));
        }
    }

    /// <summary>
    /// A masked template's ground from above, three panels side by side: the shapes by ordinal, the mask's green as the mix reads it
    /// per pixel, and the height - the topmost surface at each pixel, the sub-tiles ruled.
    /// </summary>
    /// <remarks>
    /// THE SUB-TILES WHERE THE ROOM PICTURES PUT THEM (TileModels): column x at (x - 1) * 250 and row y at -(y - 1) * 250, so row 1 is
    /// drawn at the top. The green is interpolated as a card interpolates the block's coordinates and read as GetUserMask reads it,
    /// bilinear and wrapped (GroundPainter.Sample) - the same as the mix draws it in the model pane.
    /// </remarks>
    private static GamePicture Drawn(Pieces taken, GamePicture? mask, IEnumerable<int> ordinals)
    {
        float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
        float lowZ = float.MaxValue, highZ = float.MinValue;
        foreach ((int x, int y, TileMesh mesh) in taken.All)
        {
            (float dx, float dy) = Offset(x, y);
            foreach (Vector3 point in mesh.Ground.Positions)
            {
                (minX, maxX) = (MathF.Min(minX, point.X + dx), MathF.Max(maxX, point.X + dx));
                (minY, maxY) = (MathF.Min(minY, point.Y + dy), MathF.Max(maxY, point.Y + dy));
                (lowZ, highZ) = (MathF.Min(lowZ, point.Z), MathF.Max(highZ, point.Z));
            }
        }

        int width = Math.Max(1, (int)MathF.Ceiling((maxX - minX) * PictureScale));
        int height = Math.Max(1, (int)MathF.Ceiling((maxY - minY) * PictureScale));
        int pixels = width * height;
        var depth = new float[pixels];
        Array.Fill(depth, float.MaxValue);
        var ordinalAt = new int[pixels];
        Array.Fill(ordinalAt, -1);
        var greenAt = new float[pixels];
        Array.Fill(greenAt, -1f);

        foreach ((int x, int y, TileMesh mesh) in taken.All)
        {
            (float dx, float dy) = Offset(x, y);
            SkinnedMesh ground = mesh.Ground;
            for (var shape = 0; shape < ground.Shapes.Count; shape++)
            {
                MeshShape part = ground.Shapes[shape];
                int ordinal = shape < mesh.GroundOrdinals.Count ? mesh.GroundOrdinals[shape] : -1;
                int end = Math.Min(ground.Indices.Length, part.From + part.Count) - 2;
                for (int at = Math.Max(0, part.From); at < end; at += 3)
                {
                    int a = ground.Indices[at], b = ground.Indices[at + 1], c = ground.Indices[at + 2];
                    Raster(ground, a, b, c, dx - minX, maxY - dy, width, height, mask, ordinal, depth, ordinalAt, greenAt);
                }
            }
        }

        // ONE HUE AN ORDINAL, spread round the wheel by the golden ratio so neighbours in number differ in colour.
        var hues = new Dictionary<int, (byte R, byte G, byte B)>();
        var index = 0;
        foreach (int ordinal in ordinals)
        {
            hues[ordinal] = Hue((index++ * 0.618034f) % 1f);
        }

        int full = (width * 3) + (PanelGap * 2);
        var rgba = new byte[full * height * 4];
        float span = MathF.Max(1f, highZ - lowZ);
        for (var row = 0; row < height; row++)
        {
            float worldY = maxY - ((row + 0.5f) / PictureScale);
            bool ruledRow = Ruled(worldY, 1f / PictureScale);
            for (var column = 0; column < width; column++)
            {
                int at = (row * width) + column;
                float worldX = minX + ((column + 0.5f) / PictureScale);
                bool ruled = ruledRow || Ruled(worldX, 1f / PictureScale);
                bool empty = depth[at] == float.MaxValue;
                (byte r, byte g, byte b) shapeColour = empty ? ((byte)24, (byte)24, (byte)24)
                    : hues.TryGetValue(ordinalAt[at], out (byte, byte, byte) hue) ? hue : ((byte)128, (byte)128, (byte)128);
                byte greenGrey = empty || greenAt[at] < 0 ? (byte)24 : (byte)(Math.Clamp(greenAt[at], 0f, 1f) * 255f);
                byte heightGrey = empty ? (byte)24 : (byte)(Math.Clamp(1f - ((depth[at] - lowZ) / span), 0f, 1f) * 255f);
                Put(rgba, full, column, row, ruled ? ((byte)0, (byte)0, (byte)0) : shapeColour);
                Put(rgba, full, width + PanelGap + column, row, ruled && (column + row) % 2 == 0 ? ((byte)255, (byte)0, (byte)0) : (greenGrey, greenGrey, greenGrey));
                Put(rgba, full, (2 * (width + PanelGap)) + column, row, ruled && (column + row) % 2 == 0 ? ((byte)255, (byte)0, (byte)0) : (heightGrey, heightGrey, heightGrey));
            }

            for (var gap = 0; gap < PanelGap; gap++)
            {
                Put(rgba, full, width + gap, row, (255, 255, 255));
                Put(rgba, full, (2 * width) + PanelGap + gap, row, (255, 255, 255));
            }
        }

        return new GamePicture(full, height, rgba);
    }

    /// <summary>Where a sub-tile's mesh sits in its template - see TileModels.</summary>
    private static (float X, float Y) Offset(int x, int y) => ((x - 1) * TileModels.Side, -(y - 1) * TileModels.Side);

    /// <summary>Whether a pixel's place lies within one pixel of a sub-tile's side.</summary>
    private static bool Ruled(float place, float pixel)
    {
        float into = place - (MathF.Floor(place / TileModels.Side) * TileModels.Side);
        return into < pixel;
    }

    /// <summary>
    /// One triangle from above: the topmost surface at each pixel centre it covers keeps its ordinal and its green.
    /// </summary>
    private static void Raster(
        SkinnedMesh ground, int a, int b, int c, float shiftX, float shiftY, int width, int height, GamePicture? mask, int ordinal,
        float[] depth, int[] ordinalAt, float[] greenAt)
    {
        Vector3 pa = ground.Positions[a], pb = ground.Positions[b], pc = ground.Positions[c];
        float ax = (pa.X + shiftX) * PictureScale, ay = (shiftY - pa.Y) * PictureScale;
        float bx = (pb.X + shiftX) * PictureScale, by = (shiftY - pb.Y) * PictureScale;
        float cx = (pc.X + shiftX) * PictureScale, cy = (shiftY - pc.Y) * PictureScale;
        float area = ((bx - ax) * (cy - ay)) - ((by - ay) * (cx - ax));
        if (MathF.Abs(area) < 1e-6f)
        {
            return;
        }

        int left = Math.Max(0, (int)MathF.Floor(MathF.Min(ax, MathF.Min(bx, cx))));
        int right = Math.Min(width - 1, (int)MathF.Ceiling(MathF.Max(ax, MathF.Max(bx, cx))));
        int top = Math.Max(0, (int)MathF.Floor(MathF.Min(ay, MathF.Min(by, cy))));
        int bottom = Math.Min(height - 1, (int)MathF.Ceiling(MathF.Max(ay, MathF.Max(by, cy))));
        bool coordinated = ground.Coordinated && mask is not null;
        Vector2 ua = coordinated ? ground.Coordinates[a] : default, ub = coordinated ? ground.Coordinates[b] : default, uc = coordinated ? ground.Coordinates[c] : default;
        for (int row = top; row <= bottom; row++)
        {
            float py = row + 0.5f;
            for (int column = left; column <= right; column++)
            {
                float px = column + 0.5f;
                float wa = (((bx - px) * (cy - py)) - ((by - py) * (cx - px))) / area;
                float wb = (((cx - px) * (ay - py)) - ((cy - py) * (ax - px))) / area;
                float wc = 1f - wa - wb;
                if (wa < 0f || wb < 0f || wc < 0f)
                {
                    continue;
                }

                float z = (wa * pa.Z) + (wb * pb.Z) + (wc * pc.Z);
                int at = (row * width) + column;
                if (z >= depth[at])
                {
                    continue;
                }

                depth[at] = z;
                ordinalAt[at] = ordinal;
                if (coordinated)
                {
                    Vector2 uv = (wa * ua) + (wb * ub) + (wc * uc);
                    greenAt[at] = GroundPainter.Sample(mask!.Value, 1, uv.X, uv.Y);
                }
            }
        }
    }

    private static void Put(byte[] rgba, int width, int column, int row, (byte R, byte G, byte B) colour)
    {
        int at = ((row * width) + column) * 4;
        rgba[at] = colour.R;
        rgba[at + 1] = colour.G;
        rgba[at + 2] = colour.B;
        rgba[at + 3] = 255;
    }

    /// <summary>A bright colour of a hue, nought to one.</summary>
    private static (byte R, byte G, byte B) Hue(float hue)
    {
        float h = hue * 6f;
        float x = 1f - MathF.Abs((h % 2f) - 1f);
        (float r, float g, float b) = (int)h switch
        {
            0 => (1f, x, 0f),
            1 => (x, 1f, 0f),
            2 => (0f, 1f, x),
            3 => (0f, x, 1f),
            4 => (x, 0f, 1f),
            _ => (1f, 0f, x),
        };

        static byte Of(float value) => (byte)(40 + (value * 200));
        return (Of(r), Of(g), Of(b));
    }

    /// <summary>
    /// Every laid tile file's definition past what <see cref="TileDefinition"/> reads, as bytes - and its string table, which those bytes
    /// may index.
    /// </summary>
    /// <remarks>
    /// zao's reader goes on past the corners - side offsets, sub-tile blocks, a ground grid - and this reads none of it. A big tile could
    /// say there, sub-tile by sub-tile, which grounds lie where; written as it stands, the bytes can be held against the shapes offline.
    /// </remarks>
    private static void Rests(Func<string, byte[]?> read, List<string> laid, StringBuilder said)
    {
        said.AppendLine().AppendLine(
            "=== the laid tile definitions past what is read - each defining file's string table (character offset and text) and its bytes "
            + "from where the reader stops, 32 a line, offsets from that point");
        var done = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int written = 0;
        foreach (string path in laid)
        {
            (string defining, byte[]? raw, TileDefinition definition) = Defining(read, path);
            if (raw is null || !definition.Ready || !done.Add(defining))
            {
                continue;
            }

            int rest = raw.Length - definition.End;
            said.AppendLine().Append(CultureInfo.InvariantCulture,
                $"##### {defining}{(string.Equals(defining, path, StringComparison.OrdinalIgnoreCase) ? string.Empty : $"  (for {path})")}  version {definition.Version} · {raw.Length} bytes, read to {definition.End} · {rest} past")
                .AppendLine();
            said.Append("  strings:");
            int chars = (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(raw.AsSpan(4)), (uint)((raw.Length - 8) / 2));
            int start = 0;
            for (var at = 0; at < chars; at++)
            {
                if (BinaryPrimitives.ReadUInt16LittleEndian(raw.AsSpan(8 + (at * 2))) == 0)
                {
                    if (at > start)
                    {
                        said.Append(CultureInfo.InvariantCulture, $" {start} \"{Encoding.Unicode.GetString(raw, 8 + (start * 2), (at - start) * 2)}\"");
                    }

                    start = at + 1;
                }
            }

            said.AppendLine();
            if (written >= MostRestsBytes)
            {
                said.AppendLine("  (bytes not written: past the part's limit)");
                continue;
            }

            int take = Math.Min(Math.Min(rest, MostRestBytes), MostRestsBytes - written);
            written += take;
            for (var line = 0; line < take; line += 32)
            {
                said.Append(CultureInfo.InvariantCulture, $"  {line:X5}: ")
                    .AppendLine(Convert.ToHexString(raw, definition.End + line, Math.Min(32, take - line)));
            }

            if (take < rest)
            {
                said.Append(CultureInfo.InvariantCulture, $"  ({rest - take} more bytes not written)").AppendLine();
            }
        }
    }

    /// <summary>The file that defines a laid tile, following inheritance, with its bytes and what reads of it.</summary>
    private static (string Path, byte[]? Raw, TileDefinition Definition) Defining(Func<string, byte[]?> read, string path)
    {
        string at = Clean(path);
        for (var hop = 0; hop <= TileModels.MostHops; hop++)
        {
            byte[]? raw = read(at);
            TileDefinition one = TileDefinition.Read(raw);
            if (!one.Ready || one.Inherits.Length == 0)
            {
                return (at, raw, one);
            }

            at = Clean(one.Inherits);
        }

        return (at, null, TileDefinition.None);
    }
}
