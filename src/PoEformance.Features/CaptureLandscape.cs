using System.Globalization;
using System.Text;
using PoEformance.Game.Ui;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// landscape.txt: the terrain's second cell grid held against the walkable one and the ground under it - what settles whether GridLandscapeData is the projectiles' grid (TerrainLayers).
/// </summary>
/// <remarks>
/// PRINT BOTH HALVES. The question is the hypothesis TerrainLayers states: a grid of "tall geometry" beside the walkable one,
/// the one a projectile or a leap asks. If it is, a wall is unwalkable AND blocks, while a chasm or a pool is unwalkable and
/// does not - so the table that says it is the landscape value by walkability and by the ground type under the cell, and most of
/// all along the edge of where anybody can stand, where walls and chasms both begin. Every count is written, nothing concluded;
/// the crop round the player is for holding the numbers against the picture of the same place.
/// </remarks>
public static class CaptureLandscape
{
    /// <summary>How many cells each way the crop round the player covers.</summary>
    public const int Crop = 48;

    /// <summary>The report, from both grids and the terrain the same pass read - the ground types are its. Never throws.</summary>
    /// <param name="layers">Both grids, raw.</param>
    /// <param name="grid">The terrain read in the same pass, for the ground under each cell - or null, and the ground is left out.</param>
    /// <param name="playerX">The player's world x, or null for no crop.</param>
    /// <param name="playerY">The player's world y.</param>
    public static string Report(TerrainLayers layers, TerrainGrid? grid, float? playerX, float? playerY)
    {
        ArgumentNullException.ThrowIfNull(layers);
        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture,
            $"landscape: walkable grid {layers.Walkable.Length} bytes, landscape grid {layers.Landscape.Length} bytes, {layers.Stride} bytes a row - {layers.Width} x {layers.Rows} cells");
        if (!layers.Ready)
        {
            said.Append(" - not compared: ").AppendLine(layers.Why);
            return said.ToString();
        }

        said.AppendLine();
        int width = layers.Width, rows = layers.Rows, stride = layers.Stride;
        byte[] walkable = layers.Walkable, landscape = layers.Landscape;

        // EVERY CELL: the walkable nibble against the landscape nibble.
        var both = new long[16, 16];
        for (var y = 0; y < rows; y++)
        {
            int row = y * stride;
            for (var x = 0; x < stride; x++)
            {
                byte w = walkable[row + x], l = landscape[row + x];
                both[w & 0x0F, l & 0x0F]++;
                both[w >> 4, l >> 4]++;
            }
        }

        said.AppendLine().AppendLine("=== every cell: its walkable nibble (a line each) against its landscape nibble (a column each) - nought walkable is no ground to stand on");
        Table(said, both, "walkable");

        // THE GROUND UNDER EACH CELL - the type at its nearest tile corner, as the area's corner array names it - and the
        // edge of where anybody can stand: an unwalkable cell with a walkable one beside it, across a side.
        TerrainGroundTypes? ground = grid?.Ground;
        if (ground is null || grid is null)
        {
            said.AppendLine().AppendLine("the ground under each cell left out - the pass read no ground types");
        }
        else
        {
            // COUNTED BY THE TYPE'S INDEX, a slot past the list for "outside", the names put on after: a million and more
            // cells, and nothing made per cell.
            int outside = ground.Types.Count;
            var byGround = new long[outside + 1, 2, 16];
            var edge = new long[outside + 1, 16];
            for (var y = 0; y < rows; y++)
            {
                int cornerY = (int)MathF.Round(y / (float)TerrainGrid.CellsPerTile);
                for (var x = 0; x < width; x++)
                {
                    int type = ground.At((int)MathF.Round(x / (float)TerrainGrid.CellsPerTile), cornerY);
                    type = type < 0 || type >= outside ? outside : type;
                    bool stands = TerrainLayers.Nibble(walkable, stride, x, y) != 0;
                    int value = TerrainLayers.Nibble(landscape, stride, x, y);
                    byGround[type, stands ? 1 : 0, value]++;
                    if (!stands && (Stands(walkable, stride, x - 1, y) || Stands(walkable, stride, x + 1, y) || Stands(walkable, stride, x, y - 1) || Stands(walkable, stride, x, y + 1)))
                    {
                        edge[type, value]++;
                    }
                }
            }

            string Name(int type) => type == outside ? "(outside the corners)" : ground.Types[type].Length == 0 ? "(the blank slot)" : TerrainGroundTypes.NameFor(ground.Types[type]);
            var named = new SortedDictionary<string, long[]>(StringComparer.Ordinal);
            var edges = new SortedDictionary<string, long[]>(StringComparer.Ordinal);
            for (var type = 0; type <= outside; type++)
            {
                for (var stands = 0; stands < 2; stands++)
                {
                    Add(named, Name(type) + (stands == 1 ? ", walkable" : ", not walkable"), byGround, type, stands);
                }

                var line = new long[16];
                for (var value = 0; value < 16; value++)
                {
                    line[value] = edge[type, value];
                }

                if (line.Any(one => one > 0))
                {
                    Merge(edges, Name(type), line);
                }
            }

            said.AppendLine().AppendLine("=== by the ground at each cell's nearest tile corner, walkable or not: how many cells carry each landscape nibble");
            Lines(said, named);
            said.AppendLine().AppendLine("=== the edge of where anybody can stand - an unwalkable cell with a walkable one across a side - by the ground there: each landscape nibble");
            Lines(said, edges);
        }

        // THE PLAYER'S SURROUNDINGS, one hex digit a cell, the two grids side by side - to hold against the game's picture.
        if (playerX is { } worldX && playerY is { } worldY)
        {
            int px = (int)(worldX / MapView.WorldToGrid), py = (int)(worldY / MapView.WorldToGrid);
            int left = px - (Crop / 2), top = py - (Crop / 2);
            said.AppendLine().AppendLine(CultureInfo.InvariantCulture,
                $"=== {Crop} x {Crop} cells round the player (cell {px}, {py}, marked *), a line each from y {top}: the walkable nibble, then the landscape nibble, one hex digit a cell");
            for (int y = top; y < top + Crop; y++)
            {
                said.Append("  ");
                Row(said, walkable, stride, left, y, px, py);
                said.Append("   ");
                Row(said, landscape, stride, left, y, px, py);
                said.AppendLine();
            }
        }

        return said.ToString();
    }

    private static bool Stands(byte[] walkable, int stride, int x, int y) => TerrainLayers.Nibble(walkable, stride, x, y) != 0;

    /// <summary>One type's counts, walkable or not, under its name - two slots of one name (a type listed twice) summed.</summary>
    private static void Add(SortedDictionary<string, long[]> into, string name, long[,,] counts, int type, int stands)
    {
        var line = new long[16];
        for (var value = 0; value < 16; value++)
        {
            line[value] = counts[type, stands, value];
        }

        if (line.Any(one => one > 0))
        {
            Merge(into, name, line);
        }
    }

    private static void Merge(SortedDictionary<string, long[]> into, string name, long[] line)
    {
        if (into.TryGetValue(name, out long[]? had))
        {
            for (var value = 0; value < 16; value++)
            {
                had[value] += line[value];
            }
        }
        else
        {
            into[name] = line;
        }
    }

    private static void Lines(StringBuilder said, SortedDictionary<string, long[]> counts)
    {
        foreach ((string key, long[] values) in counts)
        {
            said.Append("  ").Append(key).Append(':');
            for (var value = 0; value < values.Length; value++)
            {
                if (values[value] > 0)
                {
                    said.Append(CultureInfo.InvariantCulture, $" {value} x{values[value]}");
                }
            }

            said.AppendLine();
        }
    }

    private static void Table(StringBuilder said, long[,] counts, string lines)
    {
        var columns = new List<int>();
        for (var column = 0; column < 16; column++)
        {
            for (var line = 0; line < 16; line++)
            {
                if (counts[line, column] > 0)
                {
                    columns.Add(column);
                    break;
                }
            }
        }

        said.Append(CultureInfo.InvariantCulture, $"  {lines,-10}");
        foreach (int column in columns)
        {
            said.Append(CultureInfo.InvariantCulture, $" {"landscape " + column,12}");
        }

        said.AppendLine();
        for (var line = 0; line < 16; line++)
        {
            long total = 0;
            foreach (int column in columns)
            {
                total += counts[line, column];
            }

            if (total == 0)
            {
                continue;
            }

            said.Append(CultureInfo.InvariantCulture, $"  {line,-10}");
            foreach (int column in columns)
            {
                said.Append(CultureInfo.InvariantCulture, $" {counts[line, column],12}");
            }

            said.AppendLine();
        }
    }

    private static void Row(StringBuilder said, byte[] grid, int stride, int left, int y, int px, int py)
    {
        for (int x = left; x < left + Crop; x++)
        {
            said.Append(x == px && y == py ? '*' : "0123456789abcdef"[TerrainLayers.Nibble(grid, stride, x, y)]);
        }
    }
}
