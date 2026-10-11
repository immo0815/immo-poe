using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// A room built from its map's tileset and drawn as the area would have laid it - without the area: a tile alike to each slot, laid the measured way round, on a grid of the room's own.
/// </summary>
/// <remarks>
/// THE LAID-ROOM DRAWING ON A GRID OF THE ROOM'S OWN. LaidRoomModels draws a room from the area's grid -
/// which file lies on each tile, which piece, which way - and that grid is a plain thing (TerrainTiles),
/// so one built from the tileset (RoomBuilder) is handed to it as an area the room's size, laid as
/// written at its origin. Nothing of the drawing changes: pieces, placements, doodads at their lines'
/// heights, the probe's names. What the area would have added and this cannot is its ground heights,
/// so the pieces are set at their tiles' own levels, nought, and the doodads on that ground.
///
/// FOR THE MONSTER BOOK, where a map boss's arena room is wanted without standing in the map: the
/// arena slot is settled by one tagged tile, the ring round it is valid tiles chosen by weight. The
/// lines under the picture say how many slots were settled, chosen, or left unmet - see RoomBuild.Said.
/// </remarks>
public static class BuiltRoomModels
{
    /// <summary>
    /// The room built from the tileset and drawn, or why not. Never throws.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="path">The room's <c>.arm</c>.</param>
    /// <param name="tileset">The tile list (<c>.tst</c>) to build from, or the master (<c>.tsi</c>) naming it.</param>
    /// <param name="shaded">Whether each material's shader graphs are read.</param>
    /// <param name="doodads">Most doodads placed - see RoomModels.UsualDoodads.</param>
    /// <param name="tools">Whether the level editor's tools are placed too.</param>
    /// <param name="progress">Where the build says how far it has got, or null.</param>
    /// <param name="heights">How high a doodad whose line carries a height is set.</param>
    public static MonsterModel Of(
        Func<string, byte[]?>? read,
        string? path,
        string? tileset,
        bool shaded = false,
        int doodads = RoomModels.UsualDoodads,
        bool tools = false,
        ModelProgress? progress = null,
        DoodadHeight heights = DoodadHeight.File)
    {
        if (read is null)
        {
            return MonsterModel.None with { Why = "no install to read" };
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return MonsterModel.None with { Why = "no room to build" };
        }

        if (string.IsNullOrWhiteSpace(tileset))
        {
            return MonsterModel.None with { Why = "no tileset to build the room from" };
        }

        string tst = Slashed(tileset);
        if (!tst.EndsWith(TileList.Extension, StringComparison.OrdinalIgnoreCase))
        {
            byte[]? master = read(tst);
            string? set = TilesetFile.Value(master is { Length: > 0 } ? StatDescriptionFiles.Decode(master) : null, "TileSet");
            if (set is not { Length: > 0 })
            {
                return MonsterModel.None with { Why = $"the master names no tileset: {tst}" };
            }

            tst = Slashed(TilesetFile.Beside(tst, set));
        }

        RoomLayout room = RoomLayout.Read(read(Slashed(path)));
        if (!room.Ready)
        {
            return MonsterModel.None with { Why = $"the room did not read: {path} - {room.Why}" };
        }

        // THE POOL: every line of the tileset's lists, each tile once with the heaviest weight it is
        // given, and its identity read once from its definition.
        (IReadOnlyList<TileListEntry> entries, _) = TilesetIndex.EntriesOf(read, tst);
        var pool = new List<RoomBuildTile>();
        var at = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var unread = 0;
        foreach (TileListEntry entry in entries)
        {
            string tile = Slashed(entry.Tile);
            int weight = entry.Weight ?? 0;
            if (at.TryGetValue(tile, out int known))
            {
                if (weight > pool[known].Weight)
                {
                    pool[known] = pool[known] with { Weight = weight, Words = entry.Words };
                }

                continue;
            }

            if (TileIdentity.Of(TileModels.Defined(read, tile).Definition) is not { } identity)
            {
                unread++;
                continue;
            }

            at[tile] = pool.Count;
            pool.Add(new RoomBuildTile(tile, identity, weight, entry.Words));
        }

        if (pool.Count == 0)
        {
            return MonsterModel.None with { Why = $"the tileset lists no tile that reads: {tst}{(unread > 0 ? $" ({unread} did not read)" : string.Empty)}" };
        }

        RoomBuild build = RoomBuilder.Build(room, pool);
        if (build.Tiles is not { } tiles)
        {
            return MonsterModel.None with { Why = build.Why };
        }

        // AN AREA OF THE BUILT GRID, WITH NOTHING BUT ITS TILES: no heights, so every level is nought and
        // the drawing sets each piece at its tile's own level; no walkable cells, which the drawing does
        // not ask for with the sight left out. The room lies at the build's X and Y in it - one tile in
        // where its rim tiles reach past its grid (RoomSlot.Corner).
        int cellsAcross = tiles.Width * TerrainGrid.CellsPerTile;
        int rows = tiles.Height * TerrainGrid.CellsPerTile;
        int bytesPerRow = (cellsAcross + 1) / 2;
        var grid = new TerrainGrid(
            new byte[bytesPerRow * rows], bytesPerRow, rows, tiles.Width, tiles.Height, (TerrainHeightField?)null,
            "built from the tileset: no heights, every tile at level nought", tiles: tiles);

        MonsterModel model = LaidRoomModels.Of(
            read, path, grid, build.X, build.Y, 0, shaded, doodads, tools, atLevel: true, progress: progress, heights: heights);
        string said = build.Said + $" ({pool.Count} tiles in the pool{(unread > 0 ? $", {unread} did not read" : string.Empty)})";
        return model.Ready
            ? model with { Move = said + "; " + model.Move }
            : model with { Why = said + "; " + model.Why };
    }

    private static string Slashed(string path) => path.Replace('\\', '/').Trim();
}
