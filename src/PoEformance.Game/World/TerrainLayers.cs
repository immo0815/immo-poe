using PoEformance.Core.Memory;
using PoEformance.Core.Schema;

namespace PoEformance.Game.World;

/// <summary>
/// The terrain's two cell grids raw, as the game keeps them side by side: the walkable one every reader uses, and GridLandscapeData beside it, the projectiles' grid - see <see cref="Read"/>.
/// </summary>
/// <param name="Walkable">GridWalkableData's bytes, two cells to a byte, the even cell in the low nibble.</param>
/// <param name="Landscape">GridLandscapeData's bytes - the same packing, if the same length.</param>
/// <param name="Stride">BytesPerRow, which both grids are read by.</param>
/// <param name="Why">Why the two were not read, or empty.</param>
public sealed record TerrainLayers(byte[] Walkable, byte[] Landscape, int Stride, string Why)
{
    /// <summary>The largest grid read - a vector this long or longer is not a terrain grid.</summary>
    public const int MostBytes = 64 * 1024 * 1024;

    /// <summary>Whether both read and are one length.</summary>
    public bool Ready => Why.Length == 0;

    /// <summary>Cells across.</summary>
    public int Width => Stride * 2;

    /// <summary>Cells down.</summary>
    public int Rows => Stride > 0 ? Walkable.Length / Stride : 0;

    /// <summary>A cell's nibble in a grid of this packing, nought outside it.</summary>
    public static int Nibble(byte[] grid, int stride, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(grid);
        int at = (y * stride) + (x >> 1);
        if (x < 0 || y < 0 || x >= stride * 2 || (uint)at >= (uint)grid.Length)
        {
            return 0;
        }

        byte both = grid[at];
        return (x & 1) == 0 ? both & 0x0F : both >> 4;
    }

    /// <summary>
    /// Both grids out of the area's terrain, each in one read - inside a capture's recording, so a replay finds them. Never throws.
    /// </summary>
    /// <remarks>
    /// WHAT THE SECOND GRID IS, MEASURED (v0.1.213, Traitor's Passage and the Titan Grotto, committed as fixtures): the
    /// projectiles' grid. GameHelper2 names the two vectors GridWalkableData and GridLandscapeData and reads the first; ExileCore
    /// (PoE1) lays the same pair out as LayerMelee and LayerRanged, and GGG's procedural-generation talk has a tile's grid flag
    /// "tall geometry" that blocks projectiles. The captures settled it: both grids are nought where they stop - a body, a
    /// projectile - and count up from their noughts to a cap of five; along the edge of where anybody can stand a wall is nought
    /// in the second grid (2875 of 2875) and a chasm five (395 of 395, and 3413 of 3413 over the Grotto's abyss), and no walkable
    /// cell is nought in it. CaptureLandscape writes the counts that say so on every capture; the schema's field keeps them.
    /// </remarks>
    public static TerrainLayers Read(IMemoryReader reader, OffsetSchema schema, ulong areaInstance)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);
        try
        {
            if (!MemoryReaderExtensions.IsPlausiblePointer(areaInstance))
            {
                return Not("no area instance");
            }

            StructDef terrain = schema.Structs["TerrainMetadata"];
            ulong terrainBase = areaInstance + (ulong)schema.Structs["AreaInstance"].OffsetOf("TerrainMetadata");
            int stride = reader.Read<int>(terrainBase + (ulong)terrain.OffsetOf("BytesPerRow"));
            if (stride is <= 1 or > 16384)
            {
                return Not($"a row stride of {stride}");
            }

            (byte[]? walkable, string walkableWhy) = Vector(reader, terrainBase + (ulong)terrain.OffsetOf("GridWalkableData"), stride);
            if (walkable is null)
            {
                return Not("the walkable grid: " + walkableWhy);
            }

            (byte[]? landscape, string landscapeWhy) = Vector(reader, terrainBase + (ulong)terrain.OffsetOf("GridLandscapeData"), stride);
            if (landscape is null)
            {
                return Not("the landscape grid: " + landscapeWhy) with { Walkable = walkable, Stride = stride };
            }

            // THE SAME SHAPE OR NOTHING: a second grid of another length is another thing, and reading it with the walkable
            // grid's packing would print a table of nonsense - see the schema's own note on this vector.
            return landscape.Length != walkable.Length
                ? new TerrainLayers(walkable, landscape, stride, $"the landscape grid is {landscape.Length} bytes and the walkable one {walkable.Length} - not one shape")
                : new TerrainLayers(walkable, landscape, stride, string.Empty);
        }
        catch (KeyNotFoundException missing)
        {
            return Not("the schema lacks " + missing.Message);
        }
    }

    private static TerrainLayers Not(string why) => new([], [], 0, why);

    /// <summary>A byte vector's contents in one read, or null and why.</summary>
    private static (byte[]? Bytes, string Why) Vector(IMemoryReader reader, ulong at, int stride)
    {
        ulong first = reader.ReadPointer(at);
        ulong last = reader.ReadPointer(at + 8);
        if (first == 0 || last <= first)
        {
            return (null, "empty");
        }

        ulong length = last - first;
        if (length > MostBytes || length % (ulong)stride != 0)
        {
            return (null, $"{length} bytes, not whole rows of {stride} under {MostBytes}");
        }

        var bytes = new byte[length];
        return reader.TryRead(first, bytes) ? (bytes, string.Empty) : (null, $"unreadable at {first:X}");
    }
}
