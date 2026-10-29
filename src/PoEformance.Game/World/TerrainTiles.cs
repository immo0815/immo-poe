namespace PoEformance.Game.World;

/// <summary>One piece the game laid: a tile file put down once, how, and the area tiles it covers.</summary>
/// <param name="Id">The tile file's id - see <see cref="TerrainTiles.Paths"/>.</param>
/// <param name="Placement">How it was laid, 0 to 7 - see TileOrientation.Placement - or -1 where not known.</param>
/// <param name="MinX">Its lowest area tile across.</param>
/// <param name="MinY">Its lowest area tile down.</param>
/// <param name="MaxX">Its highest area tile across.</param>
/// <param name="MaxY">Its highest area tile down.</param>
/// <param name="Whole">Whether it covers exactly the tiles its file is made of, laid that way round.</param>
public readonly record struct TilePiece(int Id, int Placement, int MinX, int MinY, int MaxX, int MaxY, bool Whole)
{
    /// <summary>Area tiles across.</summary>
    public int Width => MaxX - MinX + 1;

    /// <summary>Area tiles down.</summary>
    public int Height => MaxY - MinY + 1;
}

/// <summary>
/// Every tile of the area as the game laid it: which file, which piece of that file, and which way round.
/// </summary>
/// <remarks>
/// WHAT THE TILE PASS ALREADY READ, kept: the terrain reader looks every tile's file up to group the
/// rooms, and the room search needs the same answer per tile - which definition lies where - to
/// check a room's slots against what was actually put down. A path id per tile rather than a string,
/// for the reason the rooms use one: an area is tens of thousands of tiles built from a few hundred
/// files. Immutable once built, like the grid it rides on.
/// </remarks>
public sealed class TerrainTiles
{
    private readonly int[] _ids;
    private readonly byte[] _subX;
    private readonly byte[] _subY;
    private readonly sbyte[] _placements;

    /// <param name="paths">The distinct tile files, by id.</param>
    /// <param name="ids">A path id per tile, row by row, -1 where the tile names none.</param>
    /// <param name="subX">Each tile's own column within its file's template.</param>
    /// <param name="subY">Each tile's own row within it.</param>
    /// <param name="placements">How each was laid, 0 to 7 - see TileOrientation.Placement - or -1.</param>
    /// <param name="width">Tiles across.</param>
    /// <param name="height">Tiles down.</param>
    public TerrainTiles(IReadOnlyList<string> paths, int[] ids, byte[] subX, byte[] subY, sbyte[] placements, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(subX);
        ArgumentNullException.ThrowIfNull(subY);
        ArgumentNullException.ThrowIfNull(placements);
        long count = (long)Math.Max(0, width) * Math.Max(0, height);
        if (ids.LongLength < count || subX.LongLength < count || subY.LongLength < count || placements.LongLength < count)
        {
            throw new ArgumentException("every per-tile array must cover the area");
        }

        Paths = paths;
        _ids = ids;
        _subX = subX;
        _subY = subY;
        _placements = placements;
        Width = width;
        Height = height;
    }

    /// <summary>The distinct tile files, by id.</summary>
    public IReadOnlyList<string> Paths { get; }

    /// <summary>Tiles across.</summary>
    public int Width { get; }

    /// <summary>Tiles down.</summary>
    public int Height { get; }

    /// <summary>The path ids row by row, for a scan that has already kept itself inside the area - see RoomPlacements.</summary>
    internal int[] Ids => _ids;

    /// <summary>The path id of the tile at a column and row, or -1 outside the area or where it names none.</summary>
    public int IdAt(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? _ids[(y * Width) + x] : -1;

    /// <summary>The file of the tile at a column and row, or empty.</summary>
    public string PathAt(int x, int y) => IdAt(x, y) is var id and >= 0 && id < Paths.Count ? Paths[id] : string.Empty;

    /// <summary>The tile's own place within its file's template, or (-1, -1) outside the area.</summary>
    public (int X, int Y) SubAt(int x, int y)
        => (uint)x < (uint)Width && (uint)y < (uint)Height ? (_subX[(y * Width) + x], _subY[(y * Width) + x]) : (-1, -1);

    /// <summary>How the tile at a column and row was laid, 0 to 7, or -1.</summary>
    public int PlacementAt(int x, int y) => (uint)x < (uint)Width && (uint)y < (uint)Height ? _placements[(y * Width) + x] : -1;

    /// <summary>
    /// The same tiles with the whole grid turned over in x: each piece at the columns its mirror image covers, laid the way a mirror
    /// sends it (TileOrientation.MirroredInX), each tile naming its place counted from its piece's least corner again.
    /// </summary>
    /// <remarks>
    /// THE PLACE A TILE NAMES IS ITS PLACE IN THE LAID FOOTPRINT, whichever way the piece went (v0.1.248's ground-grid replay, every
    /// turned piece of Port), so a mirrored piece's tiles count their columns from its other end.
    /// </remarks>
    /// <param name="size">A tile file's size in tiles as its file has it, by id - see <see cref="Pieces"/>.</param>
    public TerrainTiles MirroredInX(Func<int, (int Width, int Height)> size)
    {
        var ids = new int[_ids.Length];
        Array.Fill(ids, -1);
        var subX = new byte[_ids.Length];
        var subY = new byte[_ids.Length];
        var placements = new sbyte[_ids.Length];
        Array.Fill(placements, (sbyte)-1);
        foreach (TilePiece piece in Pieces(0, 0, Width, Height, size))
        {
            int placement = TileOrientation.MirroredInX(piece.Placement);
            for (int y = piece.MinY; y <= piece.MaxY; y++)
            {
                for (int x = piece.MinX; x <= piece.MaxX; x++)
                {
                    int from = (y * Width) + x;
                    if (_ids[from] != piece.Id)
                    {
                        continue;
                    }

                    int to = (y * Width) + (Width - 1 - x);
                    ids[to] = piece.Id;
                    subX[to] = (byte)(piece.MaxX - x);
                    subY[to] = _subY[from];
                    placements[to] = (sbyte)placement;
                }
            }
        }

        return new TerrainTiles(Paths, ids, subX, subY, placements, Width, Height);
    }

    /// <summary>
    /// The pieces laid over a block of the area, each once - a file several tiles across counted as one piece, not one per tile.
    /// </summary>
    /// <param name="x">The block's lowest tile across.</param>
    /// <param name="y">The block's lowest tile down.</param>
    /// <param name="width">Tiles across.</param>
    /// <param name="height">Tiles down.</param>
    /// <param name="size">A tile file's size in tiles as its file has it, by id - one by one where not known.</param>
    /// <remarks>
    /// A PIECE IS FOUND BY THE CORNER ITS TILES NAME: each tile of a piece names its place in the laid footprint,
    /// counted from the piece's least corner whichever way it was laid - MEASURED on every turned piece of Port
    /// (v0.1.248's ground-grid replay) and of Crimson Shores' boss room (v0.1.343's capture, placements 0 to 7) -
    /// so the tile at (x, y) naming (sx, sy) belongs to the piece whose corner is (x - sx, y - sy), and the piece
    /// is every tile of that file, laid that way, naming that corner. It may reach past the block, a piece being
    /// laid whole. One that does not come out the file's size, that way round, is said not whole.
    ///
    /// NOT A FLOOD FILL ANY MORE. Before v0.1.344 a piece was the run of side-by-side tiles of one file, laid one way,
    /// that never named the same place twice - which joins two pieces of one file laid the same way side by side
    /// wherever their names do not clash across the seam. Crimson Shores lays two <c>deep_fill_01</c> so at its boss
    /// room's edge: the fill took the left piece's last column (names 2, 0..2) beside its own first two, was drawn a
    /// tile off its place, and its own last column was left out as not whole - a 250 by 750 hole in the sea floor.
    /// </remarks>
    public List<TilePiece> Pieces(int x, int y, int width, int height, Func<int, (int Width, int Height)> size)
    {
        ArgumentNullException.ThrowIfNull(size);
        var pieces = new List<TilePiece>();
        int left = Math.Max(0, x), top = Math.Max(0, y);
        int right = Math.Min(Width, x + width), bottom = Math.Min(Height, y + height);
        if (right <= left || bottom <= top)
        {
            return pieces;
        }

        // TAKEN FOR THE BLOCK ALONE: a piece's tiles past it are never asked after, so they need no mark.
        int blockWidth = right - left;
        var taken = new bool[blockWidth * (bottom - top)];
        for (int row = top; row < bottom; row++)
        {
            for (int column = left; column < right; column++)
            {
                int at = (row * Width) + column;
                int id = _ids[at];
                if (id < 0 || taken[((row - top) * blockWidth) + column - left])
                {
                    continue;
                }

                taken[((row - top) * blockWidth) + column - left] = true;
                int placement = _placements[at];
                (int wide, int tall) = size(id);
                if (Math.Max(1, wide) * Math.Max(1, tall) == 1)
                {
                    pieces.Add(new TilePiece(id, placement, column, row, column, row, Whole: true));
                    continue;
                }

                // LAID A QUARTER ROUND, a piece's columns run down the area: placements 4 to 7 swap the axes.
                (int across, int down) = placement >= 4 ? (tall, wide) : (wide, tall);
                int cornerX = column - _subX[at], cornerY = row - _subY[at];
                if (_subX[at] >= across || _subY[at] >= down)
                {
                    // A NAME PAST THE FILE'S OWN SIZE is a tile no piece of it can hold: alone, and not whole.
                    pieces.Add(new TilePiece(id, placement, column, row, column, row, Whole: false));
                    continue;
                }

                int minX = column, maxX = column, minY = row, maxY = row, found = 0;
                for (int cy = cornerY; cy < cornerY + down; cy++)
                {
                    for (int cx = cornerX; cx < cornerX + across; cx++)
                    {
                        if ((uint)cx >= (uint)Width || (uint)cy >= (uint)Height)
                        {
                            continue;
                        }

                        int one = (cy * Width) + cx;
                        if (_ids[one] != id || _placements[one] != placement || _subX[one] != cx - cornerX || _subY[one] != cy - cornerY)
                        {
                            continue;
                        }

                        found++;
                        (minX, maxX, minY, maxY) = (Math.Min(minX, cx), Math.Max(maxX, cx), Math.Min(minY, cy), Math.Max(maxY, cy));
                        if (cx >= left && cx < right && cy >= top && cy < bottom)
                        {
                            taken[((cy - top) * blockWidth) + cx - left] = true;
                        }
                    }
                }

                pieces.Add(new TilePiece(id, placement, minX, minY, maxX, maxY, Whole: found == across * down));
            }
        }

        return pieces;
    }
}
