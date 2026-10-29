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
///
/// BUILT FROM A MASTER, THE MASTER'S FILL LIST COMES TOO: a slot asking one ground all round takes that
/// ground's section of it (RoomBuilder's remarks say what measured it), joined by the name each <c>.gt</c>
/// the room's slots name carries - the key a section is written under. Built from a bare tile list, as a
/// key from before v0.1.190 names one, there is no master to name a fill list and the room is built as before.
/// </remarks>
public static class BuiltRoomModels
{
    /// <summary>
    /// The room's turn laying it as written and turned over in x - RoomFinder's numbering.
    /// </summary>
    /// <remarks>
    /// THE GAME LAYS A ROOM EACH INSTANCE ITS OWN WAY, mirrored in some (Port's build replays: its boss room turned 180, then turned
    /// 90 and mirrored; v0.1.262's instance laid the harbour at placement 1, mirrored), while a room built from the tileset is laid
    /// as its .arm writes it - and a mirror is not turned away by any camera (said from the live client: "our harbour diorama is
    /// never the game's, always mirrored somehow"). So a switch turns the built room over, its tiles and its doodads mirrored with
    /// it as the game draws them (LaidRoomModels.Through).
    /// </remarks>
    public const int MirroredTurn = 4;

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
    /// <param name="mirrored">Whether the room is laid turned over in x - see the remarks on <see cref="MirroredTurn"/>.</param>
    /// <param name="maskCells">Whether the ground's shapes across its mask's atlas stay on the mask's cells rather than mixed by their corners - see GroundPainter.</param>
    /// <param name="groundHeights">Whether two grounds are weighed by their heights where their graphs turn that on, as the engine's height blending weighs them - see GroundPainter.</param>
    public static MonsterModel Of(
        Func<string, byte[]?>? read,
        string? path,
        string? tileset,
        bool shaded = false,
        int doodads = RoomModels.UsualDoodads,
        bool tools = false,
        ModelProgress? progress = null,
        DoodadHeight heights = DoodadHeight.File,
        bool mirrored = false,
        bool maskCells = false,
        bool groundHeights = false)
        => Built(read, path, tileset, shaded, doodads, tools, progress, heights, mirrored, maskCells, groundHeights, null);

    /// <summary>The build behind <see cref="Of"/>, with a paint cache that may outlive it.</summary>
    /// <param name="shared">A paint cache outliving the build - the stage sweep's, one for every room - or null for one of its own.</param>
    internal static MonsterModel Built(
        Func<string, byte[]?>? read,
        string? path,
        string? tileset,
        bool shaded,
        int doodads,
        bool tools,
        ModelProgress? progress,
        DoodadHeight heights,
        bool mirrored,
        bool maskCells,
        bool groundHeights,
        MonsterModels.Paints? shared)
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
        string master = string.Empty;
        string? masterText = null;
        if (!tst.EndsWith(TileList.Extension, StringComparison.OrdinalIgnoreCase))
        {
            byte[]? bytes = read(tst);
            masterText = bytes is { Length: > 0 } ? StatDescriptionFiles.Decode(bytes) : null;
            string? set = TilesetFile.Value(masterText, "TileSet");
            if (set is not { Length: > 0 })
            {
                return MonsterModel.None with { Why = $"the master names no tileset: {tst}" };
            }

            master = tst;
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
        var unreadTiles = new List<string>();
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
                if (unreadTiles.Count < 4)
                {
                    unreadTiles.Add(tile);
                }

                continue;
            }

            at[tile] = pool.Count;
            pool.Add(new RoomBuildTile(tile, identity, weight, entry.Words));
        }

        if (pool.Count == 0)
        {
            return MonsterModel.None with { Why = $"the tileset lists no tile that reads: {tst}{(unread > 0 ? $" ({unread} did not read)" : string.Empty)}" };
        }

        (Dictionary<string, IReadOnlyList<RoomBuildTile>> fills, string fillsSaid) = FillsOf(read, master, masterText, room, pool);
        RoomBuild build = RoomBuilder.Build(room, pool, fills);
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

        // TURNED OVER WHERE ASKED: the grid's mirror image, the room laid at its mirrored corner and as written, mirrored - so its
        // tiles go the way a mirror sends them and its doodads are turned the way it sends them, as the game lays it.
        int x = build.X, turn = 0;
        if (mirrored)
        {
            tiles = tiles.MirroredInX(SizesOf(tiles.Paths, pool.Concat(fills.Values.SelectMany(section => section))));
            grid = new TerrainGrid(
                new byte[bytesPerRow * rows], bytesPerRow, rows, tiles.Width, tiles.Height, (TerrainHeightField?)null,
                "built from the tileset: no heights, every tile at level nought", tiles: tiles);
            x = tiles.Width - build.X - room.Width;
            turn = MirroredTurn;
        }

        MonsterModel model = LaidRoomModels.Laid(
            read, path, grid, x, build.Y, turn, shaded, doodads, tools, fitted: false, progress, heights, entities: null, camera: null, sight: false, cut: 0, master,
            maskCells, groundHeights, shared);
        RoomAccount account = (model.Account ?? new RoomAccount()) with
        {
            Slots = build.Slots.Count,
            Unfilled = build.Unfilled,
            Unmet = build.Unmet,
            PoolUnread = unread,
            UnreadTiles = unreadTiles,
        };
        string said = build.Said + $" ({pool.Count} tiles in the pool{(unread > 0 ? $", {unread} did not read" : string.Empty)}{fillsSaid})"
            + (mirrored ? "; turned over in x by the switch, as the game lays the room in some instances" : string.Empty);
        return model.Ready
            ? model with { Move = said + "; " + model.Move, Account = account }
            : model with { Why = said + "; " + model.Why, Account = account };
    }

    /// <summary>
    /// Each tile file's size as its file has it, by the built grid's id - one by one where the build named no such tile.
    /// </summary>
    /// <remarks>
    /// THE POOL AND THE FILL LIST BOTH: v0.1.265 took the pool alone, so the master's fill tiles - Port's 3x3 Building_Fill_01 to 03,
    /// 23 of the boss room's 29 slots - read as one tile each, and turned over every tile of them named the first column; the drawing
    /// then found 56 pieces not whole and left the town round the harbour out.
    /// </remarks>
    /// <param name="paths">The built grid's tile files, by id.</param>
    /// <param name="built">Every tile the build could lay - the pool's and the fill list's.</param>
    internal static Func<int, (int Width, int Height)> SizesOf(IReadOnlyList<string> paths, IEnumerable<RoomBuildTile> built)
    {
        var bySlashed = new Dictionary<string, (int Width, int Height)>(StringComparer.OrdinalIgnoreCase);
        foreach (RoomBuildTile one in built)
        {
            bySlashed.TryAdd(one.Path, (one.Identity.Width, one.Identity.Height));
        }

        var sizes = new (int Width, int Height)[paths.Count];
        for (var id = 0; id < sizes.Length; id++)
        {
            sizes[id] = bySlashed.GetValueOrDefault(Slashed(paths[id]), (1, 1));
        }

        return id => (uint)id < (uint)sizes.Length ? sizes[id] : (1, 1);
    }

    /// <summary>
    /// The master's fill list by the ground each section fills, keyed by the <c>.gt</c> as the room's slots name it - empty
    /// where the master names none - and what of it was joined, for the line under the picture.
    /// </summary>
    private static (Dictionary<string, IReadOnlyList<RoomBuildTile>> Fills, string Said) FillsOf(
        Func<string, byte[]?> read, string master, string? masterText, RoomLayout room, IReadOnlyList<RoomBuildTile> pool)
    {
        var fills = new Dictionary<string, IReadOnlyList<RoomBuildTile>>(StringComparer.OrdinalIgnoreCase);
        if (master.Length == 0 || TilesetFile.Value(masterText, "FillTiles") is not { Length: > 0 } named)
        {
            return (fills, string.Empty);
        }

        // AS WRITTEN, THEN BESIDE THE MASTER - the first that reads, as the capture's masters part resolves it.
        FillList list = FillList.None;
        foreach (string candidate in MasterFile.Candidates(master, named))
        {
            list = FillList.Read(read(candidate));
            if (list.Entries.Count > 0)
            {
                break;
            }
        }

        if (list.Entries.Count == 0)
        {
            return (fills, $", the master's fill list {named} did not read");
        }

        // EACH GROUND THE ROOM'S SLOTS NAME, by its name: a section is written under the name, a slot names the file.
        var grounds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RoomSlot slot in room.Slots)
        {
            for (var corner = 0; corner < 4; corner++)
            {
                if (Slashed(room.Named(slot.Ground(corner))) is { Length: > 0 } ground)
                {
                    grounds.Add(ground);
                }
            }
        }

        var identities = pool.ToDictionary(tile => tile.Path, tile => tile.Identity, StringComparer.OrdinalIgnoreCase);
        int tiles = 0, unread = 0;
        foreach (string ground in grounds)
        {
            string name = GroundType.Name(read(ground));
            var section = new List<RoomBuildTile>();
            foreach (FillListEntry entry in list.Entries)
            {
                if (name.Length == 0 || !string.Equals(entry.Section, name, StringComparison.Ordinal))
                {
                    continue;
                }

                string path = Slashed(entry.Path);
                if (!identities.TryGetValue(path, out TileIdentity? identity))
                {
                    identity = TileIdentity.Of(TileModels.Defined(read, path).Definition);
                    if (identity is null)
                    {
                        unread++;
                        continue;
                    }

                    identities[path] = identity;
                }

                section.Add(new RoomBuildTile(path, identity, entry.Weight, entry.Words));
            }

            if (section.Count > 0)
            {
                fills[ground] = section;
                tiles += section.Count;
            }
        }

        return (fills, $", the master's fill list: {tiles} tiles for {fills.Count} of the room's {grounds.Count} grounds{(unread > 0 ? $", {unread} did not read" : string.Empty)}");
    }

    private static string Slashed(string path) => path.Replace('\\', '/').Trim();
}
