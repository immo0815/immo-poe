using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;
using PoEformance.Game.World;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// Looks for the engine's ocean markup - the grid its OceanMarkupField is made from, where the sea's shore is - in the area's
/// terrain and one hop beyond it, and writes every grid of the area's shape it finds as a map beside the walkable grid.
/// </summary>
/// <remarks>
/// WHAT THE PROGRAM SAYS (water-shore.txt's strings, v0.1.295): <c>OceanMarkupField(IDevice*, const int*, vector2_int, vector4,
/// float, bool, float)</c> - a grid of ints, its size, a box - and <c>ClientTileMap::CalculateMapMarkup()</c>, a method that takes
/// nothing and returns nothing, so what it works out it keeps in the tile map or hands on. The terrain this tool reads at
/// AreaInstance + TerrainMetadata is a C++ object of its own (a vtable at its first slot, a back-pointer to the area after it) - the
/// likeliest ClientTileMap. So the hunt reads it, and every object it points to, for std::vectors whose length is a grid of the
/// area's shape: the tiles, their corners, the cells, at several resolutions each.
///
/// WHY IT MATTERS (v0.1.299): the tool makes its own shore, CHOSEN - and chose a wall out of deep water to be no shore because the
/// game's channel by Port's quay is blue where the field waves' formula would whiten the water near any shore. The engine's own
/// markup settles which of its land it counts.
///
/// A DIAGNOSTIC: nothing is concluded here. A grid is printed with the walkable grid and the corners' ground types sampled at the
/// same places; a miss is printed with how far the hunt looked. Nothing of it goes into the recording - it reads past the tap.
/// </remarks>
public static class OceanMarkupHunt
{
    /// <summary>How much of the terrain object to read, from its start.</summary>
    public const int TerrainBytes = 0x4000;

    /// <summary>How much to read at each pointer followed.</summary>
    public const int WindowBytes = 0x400;

    /// <summary>How many pointers to follow - one per qword of what is swept, so the guard never decides the reach.</summary>
    public const int MostFollowed = TerrainBytes / 8;

    /// <summary>The longest vector read whole.</summary>
    public const int MostGridBytes = 64 * 1024 * 1024;

    /// <summary>The most grids written as maps.</summary>
    public const int MostGrids = 24;

    /// <summary>The widest map written, in characters.</summary>
    public const int MapColumns = 128;

    /// <summary>The most vectors listed that match no shape.</summary>
    private const int MostOthers = 80;

    /// <summary>How each map writes a category, by how many cells hold it - the commonest first.</summary>
    private const string Ranks = "0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ";

    /// <summary>
    /// The tile multiples a grid may be laid at: whole tiles, the shoreline's resolutions, sub-tiles and cells - and the environments'
    /// water.flow_resolution, 1 to 8 (water.txt; Port 7), for the grid a river's flow map is made from (v0.1.351, RiverMarkupField's bools).
    /// </summary>
    private static readonly int[] Multiples = [1, 2, 3, 4, 6, 7, 8, 12, 16, 23, 46];

    /// <summary>The cell steps a grid may be laid at - a grid of one value a k by k cells.</summary>
    private static readonly int[] CellSteps = [2, 3, 4, 6, 8];

    /// <summary>The element sizes a vector is read as.</summary>
    private static readonly int[] Sizes = [1, 2, 4, 8, 16];

    /// <summary>One std::vector found: where its triple sat, its first byte and its length.</summary>
    private readonly record struct Found(string Where, ulong Begin, long Bytes);

    /// <summary>A shape a grid of the area may have.</summary>
    private readonly record struct Shape(string Name, int Width, int Height);

    /// <summary>
    /// The hunt, run once: its report's lines. Never throws.
    /// </summary>
    /// <param name="reader">Past the tap, so a whole-terrain read does not crowd the recording.</param>
    /// <param name="schema">The offsets.</param>
    /// <param name="areaInstance">The area instance the terrain sits in.</param>
    public static IReadOnlyList<string> Run(IMemoryReader reader, OffsetSchema schema, ulong areaInstance)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        var lines = new List<string>();
        try
        {
            Hunt(reader, schema, areaInstance, lines);
        }
        catch (KeyNotFoundException missing)
        {
            lines.Add("ocean markup: the schema lacks " + missing.Message);
        }

        return lines;
    }

    private static void Hunt(IMemoryReader reader, OffsetSchema schema, ulong areaInstance, List<string> lines)
    {
        if (!MemoryReaderExtensions.IsPlausiblePointer(areaInstance))
        {
            lines.Add("ocean markup: no area instance");
            return;
        }

        StructDef terrain = schema.Structs["TerrainMetadata"];
        ulong terrainBase = areaInstance + (ulong)schema.Structs["AreaInstance"].OffsetOf("TerrainMetadata");
        long tilesX = reader.Read<long>(terrainBase + (ulong)terrain.OffsetOf("TotalTilesX"));
        long tilesY = reader.Read<long>(terrainBase + (ulong)terrain.OffsetOf("TotalTilesY"));
        if (tilesX is < 1 or > 4096 || tilesY is < 1 or > 4096)
        {
            lines.Add($"ocean markup: the terrain's tile counts read {tilesX} by {tilesY}");
            return;
        }

        TerrainLayers layers = TerrainLayers.Read(reader, schema, areaInstance);
        byte[] corners = Corners(reader, terrainBase + (ulong)terrain.OffsetOf("TileCornerData"), (int)((tilesX + 1) * (tilesY + 1) * 3));
        List<Shape> shapes = Shapes((int)tilesX, (int)tilesY, layers);

        // THE TERRAIN OBJECT, and one hop beyond every pointer in it.
        byte[] swept = Block(reader, terrainBase, TerrainBytes);
        var found = new Dictionary<ulong, Found>();
        Vectors(swept, "terrain", found, terrain.Fields.GroupBy(field => field.Offset).ToDictionary(same => same.Key, same => same.First().Name));
        var followed = new HashSet<ulong>();
        for (var at = 0; at + 8 <= swept.Length && followed.Count < MostFollowed; at += 8)
        {
            ulong pointer = BinaryPrimitives.ReadUInt64LittleEndian(swept.AsSpan(at));
            if (!MemoryReaderExtensions.IsPlausiblePointer(pointer) || found.ContainsKey(pointer) || !followed.Add(pointer))
            {
                continue;
            }

            var window = new byte[WindowBytes];
            if (reader.TryRead(pointer, window))
            {
                Vectors(window, $"terrain+0x{at:X} -> ", found);
            }
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"ocean markup: {found.Count} vectors in the terrain's first 0x{TerrainBytes:X} bytes and one hop beyond {followed.Count} of its pointers ({WindowBytes} bytes each); the area {tilesX} x {tilesY} tiles, {layers.Width} x {layers.Rows} cells"));
        lines.Add("  shapes a grid of the area may have: " + string.Join(", ", shapes.Select(shape => $"{shape.Name} {shape.Width}x{shape.Height}")));
        lines.Add(string.Empty);

        // EVERY VECTOR WHOSE LENGTH IS A SHAPE, at every element size that divides it.
        var matched = new List<(Found Vector, int Size, Shape Shape)>();
        var others = new List<Found>();
        foreach (Found vector in found.Values.OrderBy(one => one.Where, StringComparer.Ordinal))
        {
            var any = false;
            foreach (int size in Sizes)
            {
                if (vector.Bytes % size != 0)
                {
                    continue;
                }

                long count = vector.Bytes / size;
                foreach (Shape shape in shapes)
                {
                    if (count == (long)shape.Width * shape.Height)
                    {
                        matched.Add((vector, size, shape));
                        any = true;
                    }
                }
            }

            if (!any && vector.Bytes >= 1024)
            {
                others.Add(vector);
            }
        }

        lines.Add($"=== {matched.Count} vectors whose length is a grid of the area's shape");
        foreach ((Found vector, int size, Shape shape) in matched)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  {vector.Where}  {vector.Bytes} bytes at {vector.Begin:X}: {shape.Name} {shape.Width}x{shape.Height} of {size} bytes"));
        }

        lines.Add(string.Empty);
        lines.Add($"=== {others.Count} other vectors of 1 KB or more, with the grids near the area's proportions their length makes");
        foreach (Found vector in others.OrderByDescending(one => one.Bytes).Take(MostOthers))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  {vector.Where}  {vector.Bytes} bytes at {vector.Begin:X}{Proportioned(vector.Bytes, tilesX, tilesY)}"));
        }

        lines.Add(string.Empty);
        var drawn = new HashSet<(int, int)>();
        foreach ((Found vector, int size, Shape shape) in matched.Take(MostGrids))
        {
            Grid(reader, vector, size, shape, layers, corners, (int)tilesX, (int)tilesY, lines);
            drawn.Add((shape.Width, shape.Height));
        }

        if (matched.Count > MostGrids)
        {
            lines.Add($"... {matched.Count - MostGrids} more grids not drawn");
        }

        // THE OTHER HALF: the walkable grid and the corners' ground types at every resolution a grid was drawn at.
        foreach ((int width, int height) in drawn)
        {
            lines.Add(string.Empty);
            lines.Add($"=== the walkable grid at {width}x{height}: '#' walkable, '.' not");
            lines.AddRange(Map(width, height, (x, y) => Walkable(layers, x, y, width, height) ? '#' : '.'));
            lines.Add(string.Empty);
            lines.Add($"=== the corners' ground types at {width}x{height}: TileCornerData's byte 0, the slot of GroundTypeFiles, as a digit or letter");
            lines.AddRange(Map(width, height, (x, y) => Corner(corners, (int)tilesX, (int)tilesY, x, y, width, height)));
        }
    }

    /// <summary>Every grid shape tried: the tiles and their corners at each multiple, and the cells.</summary>
    private static List<Shape> Shapes(int tilesX, int tilesY, TerrainLayers layers)
    {
        var shapes = new List<Shape>();
        foreach (int multiple in Multiples)
        {
            shapes.Add(new Shape($"tiles x{multiple}", tilesX * multiple, tilesY * multiple));
            shapes.Add(new Shape($"tile corners x{multiple}", (tilesX * multiple) + 1, (tilesY * multiple) + 1));
        }

        if (layers.Width > 0 && layers.Rows > 0)
        {
            shapes.Add(new Shape("cells", layers.Width, layers.Rows));
            shapes.Add(new Shape("cell corners", layers.Width + 1, layers.Rows + 1));

        }

        // A CELL IN EVERY k EACH WAY (v0.1.303): Port's terrain holds a grid of one byte a three by three cells at +0x6D8 (1127 x 483,
        // the Canal Hideout's 460 x 460) that v0.1.302 listed and could not draw. Counted over the area's own cells, 23 a tile - not the
        // walkable grid's width, which is its stride twice and a cell more where the tiles' cells are odd: Port's 147 tiles are 3381
        // cells, its stride 1691 bytes, and v0.1.303 looked for 1128 x 483 beside the grid's 1127 x 483 and drew nothing.
        int cellsX = tilesX * TerrainGrid.CellsPerTile, cellsY = tilesY * TerrainGrid.CellsPerTile;
        foreach (int every in CellSteps)
        {
            int across = cellsX / every, down = cellsY / every;
            shapes.Add(new Shape($"cells / {every}", across, down));
            shapes.Add(new Shape($"cell corners / {every}", across + 1, down + 1));
            if (cellsX % every != 0 || cellsY % every != 0)
            {
                // The remainder kept as a part cell, should the engine round up rather than down.
                int upAcross = (cellsX + every - 1) / every, upDown = (cellsY + every - 1) / every;
                shapes.Add(new Shape($"cells / {every} rounded up", upAcross, upDown));
                shapes.Add(new Shape($"cell corners / {every} rounded up", upAcross + 1, upDown + 1));
            }
        }

        return [.. shapes.Distinct()];
    }

    /// <summary>The grids a length makes at one and four bytes a cell whose proportions lie within 3% of the area's.</summary>
    private static string Proportioned(long bytes, long tilesX, long tilesY)
    {
        double aspect = (double)tilesX / tilesY;
        var said = new StringBuilder();
        foreach (int size in (int[])[1, 4])
        {
            if (bytes % size != 0)
            {
                continue;
            }

            long count = bytes / size;
            for (long width = 2; width * width <= count * 64 && width <= 65536; width++)
            {
                if (count % width != 0)
                {
                    continue;
                }

                long height = count / width;
                if (Math.Abs(((double)width / height / aspect) - 1.0) < 0.03)
                {
                    said.Append(CultureInfo.InvariantCulture, $"; {width}x{height} of {size}");
                }
            }
        }

        return said.ToString();
    }

    /// <summary>A grid read whole and written: its values counted, each with how much of it is walkable, then a map of it.</summary>
    private static void Grid(
        IMemoryReader reader, Found vector, int size, Shape shape, TerrainLayers layers, byte[] corners, int tilesX, int tilesY, List<string> lines)
    {
        lines.Add(string.Create(CultureInfo.InvariantCulture,
            $"=== grid {vector.Where} at {vector.Begin:X}: {shape.Name} {shape.Width}x{shape.Height}, {size} bytes a cell"));
        if (vector.Bytes > MostGridBytes)
        {
            lines.Add("  too long to read");
            return;
        }

        var bytes = new byte[vector.Bytes];
        if (!reader.TryRead(vector.Begin, bytes))
        {
            lines.Add("  unreadable");
            return;
        }

        // THE FIRST FOUR BYTES OF A CELL, as an int - the field's own type - and as a float where the bytes make one.
        int count = shape.Width * shape.Height;
        var values = new int[count];
        for (var at = 0; at < count; at++)
        {
            ReadOnlySpan<byte> cell = bytes.AsSpan(at * size, size);
            values[at] = size switch
            {
                1 => cell[0],
                2 => BinaryPrimitives.ReadUInt16LittleEndian(cell),
                _ => BinaryPrimitives.ReadInt32LittleEndian(cell),
            };
        }

        var counts = new Dictionary<int, int>();
        var walkable = new Dictionary<int, int>();
        for (var at = 0; at < count; at++)
        {
            int value = values[at];
            counts[value] = counts.GetValueOrDefault(value) + 1;
            if (Walkable(layers, at % shape.Width, at / shape.Width, shape.Width, shape.Height))
            {
                walkable[value] = walkable.GetValueOrDefault(value) + 1;
            }
        }

        List<KeyValuePair<int, int>> ranked = [.. counts.OrderByDescending(one => one.Value).ThenBy(one => one.Key)];
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {counts.Count} distinct values"));
        if (counts.Count > Ranks.Length)
        {
            int least = values.Min(), most = values.Max();
            float asFloatLeast = BitConverter.Int32BitsToSingle(least), asFloatMost = BitConverter.Int32BitsToSingle(most);
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  from {least} to {most} as ints ({asFloatLeast:G6} to {asFloatMost:G6} as floats); the commonest: {string.Join(", ", ranked.Take(12).Select(one => $"{one.Key} x{one.Value}"))}"));
            lines.Add("  map: each cell's value between the least and the most, nought to nine");
            lines.AddRange(Map(shape.Width, shape.Height, (x, y) =>
            {
                long span = Math.Max(1L, (long)most - least);
                long step = ((long)values[(y * shape.Width) + x] - least) * 10 / (span + 1);
                return (char)('0' + Math.Clamp(step, 0, 9));
            }));
            return;
        }

        var rank = new Dictionary<int, char>();
        for (var at = 0; at < ranked.Count; at++)
        {
            (int value, int cells) = (ranked[at].Key, ranked[at].Value);
            rank[value] = Ranks[at];
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  '{Ranks[at]}' {value} (0x{value:X}{(size >= 4 ? $", {BitConverter.Int32BitsToSingle(value):G6} as a float" : string.Empty)}): {cells} cells, {walkable.GetValueOrDefault(value)} walkable"));
        }

        lines.Add("  map: row nought first, each cell by its value's letter above");
        lines.AddRange(Map(shape.Width, shape.Height, (x, y) => rank[values[(y * shape.Width) + x]]));
    }

    /// <summary>A grid as rows of characters, sampled to at most <see cref="MapColumns"/> across and kept in proportion.</summary>
    private static IEnumerable<string> Map(int width, int height, Func<int, int, char> at)
    {
        int step = Math.Max(1, (width + MapColumns - 1) / MapColumns);
        var row = new StringBuilder(MapColumns + 8);
        for (var y = 0; y < height; y += step)
        {
            row.Clear().Append("  ");
            for (var x = 0; x < width; x += step)
            {
                row.Append(at(x, y));
            }

            yield return row.ToString();
        }
    }

    /// <summary>Whether the walkable cell at a grid cell's place is walkable.</summary>
    private static bool Walkable(TerrainLayers layers, int x, int y, int width, int height)
    {
        if (layers.Width == 0 || layers.Rows == 0)
        {
            return false;
        }

        int cx = (int)((x + 0.5) * layers.Width / width), cy = (int)((y + 0.5) * layers.Rows / height);
        return TerrainLayers.Nibble(layers.Walkable, layers.Stride, cx, cy) != 0;
    }

    /// <summary>The corner's ground type nearest a grid cell's place, as a character.</summary>
    private static char Corner(byte[] corners, int tilesX, int tilesY, int x, int y, int width, int height)
    {
        int cx = (int)Math.Round((x + 0.5) * tilesX / width), cy = (int)Math.Round((y + 0.5) * tilesY / height);
        int at = ((cy * (tilesX + 1)) + cx) * 3;
        return at < 0 || at >= corners.Length ? ' ' : corners[at] < Ranks.Length ? Ranks[corners[at]] : '+';
    }

    /// <summary>The corners' bytes, or none.</summary>
    private static byte[] Corners(IMemoryReader reader, ulong vector, int expected)
    {
        ulong first = reader.ReadPointer(vector), last = reader.ReadPointer(vector + 8);
        if (first == 0 || last <= first || (long)(last - first) != expected)
        {
            return [];
        }

        var bytes = new byte[expected];
        return reader.TryRead(first, bytes) ? bytes : [];
    }

    /// <summary>A block read whole, or page by page where whole fails - unread pages left nought.</summary>
    private static byte[] Block(IMemoryReader reader, ulong at, int length)
    {
        var bytes = new byte[length];
        if (reader.TryRead(at, bytes))
        {
            return bytes;
        }

        const int Page = 0x1000;
        for (var offset = 0; offset < length; offset += Page)
        {
            if (!reader.TryRead(at + (ulong)offset, bytes.AsSpan(offset, Math.Min(Page, length - offset))))
            {
                bytes.AsSpan(offset, Math.Min(Page, length - offset)).Clear();
            }
        }

        return bytes;
    }

    /// <summary>One grid read back from the report: where it was, its shape, and its map's rows as written.</summary>
    public sealed record ReadGrid(string Heading, IReadOnlyList<string> Rows);

    /// <summary>The grids of a report read back - every "=== grid" section with its map.</summary>
    public static IReadOnlyList<ReadGrid> Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var grids = new List<ReadGrid>();
        string? heading = null;
        var rows = new List<string>();
        var mapping = false;
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.StartsWith("=== ", StringComparison.Ordinal))
            {
                if (heading is not null)
                {
                    grids.Add(new ReadGrid(heading, [.. rows]));
                }

                heading = trimmed.StartsWith("=== grid ", StringComparison.Ordinal) ? trimmed[9..] : null;
                rows.Clear();
                mapping = false;
                continue;
            }

            if (heading is null)
            {
                continue;
            }

            if (trimmed.StartsWith("  map:", StringComparison.Ordinal))
            {
                mapping = true;
            }
            else if (mapping && trimmed.StartsWith("  ", StringComparison.Ordinal) && trimmed.Length > 2)
            {
                rows.Add(trimmed[2..]);
            }
            else if (mapping)
            {
                mapping = false;
            }
        }

        if (heading is not null)
        {
            grids.Add(new ReadGrid(heading, [.. rows]));
        }

        return grids;
    }

    /// <summary>The grids read back, each with how many of its map's characters stand for each value - ocean-markup-replay.txt.</summary>
    public static string Report(IReadOnlyList<ReadGrid> grids)
    {
        ArgumentNullException.ThrowIfNull(grids);
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"ocean markup replay: {grids.Count} grids read back").AppendLine();
        foreach (ReadGrid grid in grids)
        {
            var counts = new SortedDictionary<char, int>();
            foreach (char one in grid.Rows.SelectMany(row => row))
            {
                counts[one] = counts.GetValueOrDefault(one) + 1;
            }

            text.Append(CultureInfo.InvariantCulture, $"  {grid.Heading}: {grid.Rows.Count} rows of {(grid.Rows.Count > 0 ? grid.Rows[0].Length : 0)}, ")
                .AppendJoin(", ", counts.Select(one => $"'{one.Key}' x{one.Value}"))
                .AppendLine();
        }

        return text.ToString();
    }

    /// <summary>Every std::vector triple in a block - begin, end, capacity, plausible and in order - by its begin, a field the schema names said by its name.</summary>
    private static void Vectors(byte[] block, string where, Dictionary<ulong, Found> found, IReadOnlyDictionary<int, string>? named = null)
    {
        for (var at = 0; at + 24 <= block.Length; at += 8)
        {
            ulong begin = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(at));
            ulong end = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(at + 8));
            ulong capacity = BinaryPrimitives.ReadUInt64LittleEndian(block.AsSpan(at + 16));
            if (!MemoryReaderExtensions.IsPlausiblePointer(begin) || end <= begin || capacity < end
                || end - begin > MostGridBytes || capacity - begin > 2UL * MostGridBytes || found.ContainsKey(begin))
            {
                continue;
            }

            string name = named is not null && named.TryGetValue(at, out string? field) ? $" ({field})" : string.Empty;
            found[begin] = new Found(string.Create(CultureInfo.InvariantCulture, $"{where}+0x{at:X}{name}"), begin, (long)(end - begin));
        }
    }
}
