using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// A room as the area laid it: the tiles the game actually put down under a place the room search found, and the room's own doodads set on them.
/// </summary>
/// <remarks>
/// THE TILES ARE THE AREA'S, NOT THE ROOM'S. Where a room is joined to the map the game lays its own
/// tiles over the room's closed rim - seepage's offices, every one of 58 misses a join's - so the
/// room's file says what the room is and the area what was built. Each tile file is put down once
/// per piece (see TerrainTiles.Pieces), turned the way the game laid it (TileOrientation, the decode
/// the heights are read through), about the middle of its own ground, at the middle of the area
/// tiles the piece covers.
///
/// ONE THING THE FILES DO NOT SAY IS SETTLED BY THE AREA: how a tile's mesh lies on the template
/// the game's turn is read through - as filed, turned, or turned over. All eight ways a square can lie
/// are tried, the turn the game laid each piece applied after. FIRST BY THE GAME'S OWN GROUND TYPES:
/// a tile definition's ground grid names a ground type at every sub-tile corner in the file's frame,
/// and the area holds its own at every tile corner (TileCornerData), so a way is the game's where it
/// lays the grids' corners on the area's - the ways laying the most sub-tiles so are the only ones
/// left (v0.1.262). Then by the ground's heights, among those: every piece's ground is asked how high
/// it is at nine points per tile and held against the height the area itself has there. A slope - a
/// stair, a ramp - fits one way only, so the way the sloped pieces fit best is the way every piece is
/// drawn, and the line under the picture says how well, and how well the next did. With neither to
/// go on the files are taken as filed. Each piece is then raised to the area's own ground, the
/// doodads with it.
///
/// THE CORNERS BEFORE THE HEIGHTS, because the heights alone chose a way that is not one: Port's
/// harbour, a 15 by 21 file, came out laid across its own footprint in the capture's ground on the
/// screen (v0.1.257) - every sub-tile where its neighbour across the diagonal should be, its grid's
/// corners at none of the game's, while laid as filed all ten on the screen met the game's corner
/// types exactly. The heights' misses are summed over the points that land on the file, and a way
/// that turns an oblong file across its footprint drops a third of them, so it summed smaller.
/// NOT BY THE AREA'S INDEX, which v0.1.261 tried: every tile of a piece names its place counted in
/// the LAID footprint from its least corner, whichever way the piece was laid (v0.1.248's grid replay,
/// all of Port's turned pieces), so it says nothing of how the file turns - asked whether it names the
/// file's column and row, it chose the way undoing the commonest placement, and the harbour came out
/// wrong again with 131 of its 315 sub-tiles on the game's corner types.
///
/// THE HEIGHTS COUNT THE GAME'S WAY, DOWN, IN BOTH, and are not tried the other way any more. The
/// area's are GameHelper2's terrain heights, which go into the world as its z, and the world's up is
/// minus z (a health bar floats at z less the model's height); the files' up is minus z as well,
/// which is why the tile book stands its walls up. Trying the other sign as well, as 0.1.102 did, is
/// worse than redundant: a slope turned half round and counted upside down is the same slope, so
/// the two answers cannot be told apart on a ramp or an even stair, and a choice between them is a
/// coin toss that shows as pieces turned round. The room that showed it chose "Y turned over,
/// heights the other way" on 6 of its 14 sloped pieces.
///
/// A PIECE GOES AT ITS TILES' OWN LEVEL (v0.1.316, MEASURED), where the area's own heights are measured
/// from: a tile's sub-tile heights are described relative to its level (TerrainHeightField), the game
/// stands a tile's doodads at their lines' z plus the level (Marrow, Sulphuric), and the game's own
/// cobblestones on Port's pier line up with the pier's materials on the plane at its tile's level
/// (v0.1.313's capture, within 16 units). Fitted - raised until its ground meets the area's on average,
/// the way laid rooms went until v0.1.314 - Port's harbour came out 450 units too high, the sea floor
/// under its water outweighing the pier; Seepage's offices, fitted, came out with their sand over floors
/// the game shows bare. The fit stays behind a switch (<c>fitted</c>), and the line says how far each
/// piece's fit lies from its level either way.
///
/// AND A PIECE SEVERAL TILES ACROSS IS CHECKED AGAINST THE AREA'S OWN INDEX: every one of its area
/// tiles names the column and row of the file it holds, so where the drawing puts each sub-tile can
/// be compared with where the game says it is - counting the rows from the top, as the files are laid
/// out (TileModels), and from the bottom, so that the line says which one the area agrees with.
///
/// THE DOODADS GO WHERE THE ROOM'S CORNERS WERE FOUND - RoomFinder.Laying, the corners' own
/// arithmetic - a doodad's x and y counting the room's columns and lines, the frame
/// RoomModels.SpreadOf already draws a room's doodads in. That is this project's reading and still
/// waits on the game: annalithic/poeterrain is a Path of Exile 1 importer, and both the line that
/// reads its doodads and the loop that places them are commented out, so it settles nothing here.
/// </remarks>
public static class LaidRoomModels
{
    /// <summary>The cells into a tile the ground is asked at, each way: near both edges and in the middle.</summary>
    private static readonly int[] Asked = [4, 11, 18];

    /// <summary>How far a piece's ground may sit from the area's, in world units, and still be said to fit.</summary>
    private const float Fits = 8f;

    /// <summary>How much the area's own ground must rise within a piece for it to count as sloped.</summary>
    private const float Rise = 4f;

    /// <summary>The eight ways a file can lie on its template, as TileOrientation numbers its placements - as filed first.</summary>
    private static readonly int[] Relations = [3, 0, 1, 2, 4, 5, 6, 7];

    /// <summary>
    /// The room laid where the search found it, or why not. Never throws.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="path">The room's <c>.arm</c>.</param>
    /// <param name="grid">The area the room was found in - its tiles and its ground.</param>
    /// <param name="x">The place's lowest area tile across.</param>
    /// <param name="y">The place's lowest area tile down.</param>
    /// <param name="turn">How the room was laid - see RoomFinder.Said.</param>
    /// <param name="shaded">Whether each material's shader graphs are read.</param>
    /// <param name="doodads">Most doodads placed, and the measure of the tiles' triangles too - see RoomModels.UsualDoodads.</param>
    /// <param name="tools">Whether the level editor's tools are placed too.</param>
    /// <param name="fitted">Whether each piece is fitted to the area's ground rather than set at its tiles' own level - see the remarks.</param>
    /// <param name="progress">Where the build says how far it has got, or null - see <see cref="ModelProgress"/>.</param>
    /// <param name="heights">How high a doodad whose line carries a height is set - see <see cref="DoodadHeight"/>.</param>
    /// <param name="entities">The area's entities in memory, to hold the room's doodads against - see <see cref="HeightsSaid"/> - or null.</param>
    /// <param name="camera">The game's camera as last read, for what it can see of the room - see CameraSight - or null.</param>
    /// <param name="sight">Whether what the camera cannot see from anywhere the player can stand is left out - see <see cref="Hiding"/>.</param>
    /// <param name="cut">How far under the area's ground, in world units, everything is left out; nought or less leaves nothing out - see <see cref="Hiding"/>.</param>
    /// <param name="master">The area's master (<c>.tsi</c>), whose MaterialsList paints the ground - empty for the one beside the room's <c>Rooms/</c> (see GroundPainter).</param>
    /// <param name="maskCells">Whether the ground's shapes across its mask's atlas stay on the mask's cells rather than mixed by their corners - see GroundPainter.</param>
    /// <param name="groundHeights">Whether two grounds are weighed by their heights where their graphs turn that on, as the engine's height blending weighs them - see GroundPainter.</param>
    public static MonsterModel Of(
        Func<string, byte[]?>? read,
        string? path,
        TerrainGrid? grid,
        int x,
        int y,
        int turn,
        bool shaded = false,
        int doodads = RoomModels.UsualDoodads,
        bool tools = false,
        bool fitted = false,
        ModelProgress? progress = null,
        DoodadHeight heights = DoodadHeight.File,
        IReadOnlyList<WorldEntity>? entities = null,
        CameraShot? camera = null,
        bool sight = false,
        int cut = 0,
        string master = "",
        bool maskCells = false,
        bool groundHeights = false)
        => Laid(read, path, grid, x, y, turn, shaded, doodads, tools, fitted, progress, heights, entities, camera, sight, cut, master, maskCells, groundHeights, null);

    /// <summary>
    /// A block of the area as it laid it, with no room of its own: every piece over <paramref name="size"/> by <paramref name="size"/> tiles from
    /// (<paramref name="x"/>, <paramref name="y"/>), laid as a laid room lays its pieces, with their ground and their own doodads (.tmd) - see
    /// AreaBlock.
    /// </summary>
    /// <remarks>
    /// THE ROOMS OF AN AREA ARE OFTEN NOT TO BE FOUND, ITS TILES ALWAYS ARE (v0.1.359, DECIDED from the live client): The Red Vale placed 7 of
    /// its 187 rooms - its river rooms are one big slot each and name no doodad, so neither the doodads nor the corner search finds them - while
    /// every tile the area laid is in its terrain with its place and turn. So the block lays the tiles alone: what only a room adds, its own
    /// doodad lines, zones and boss places, is not there, and the line under the picture says so. The master paints the ground and puts its
    /// swaps on the tiles and every doodad laid over them as a laid room's does - the area's own, handed in (the rooms' master.tsi beside their Rooms/).
    /// </remarks>
    public static MonsterModel Block(
        Func<string, byte[]?>? read,
        TerrainGrid? grid,
        int x,
        int y,
        int size,
        string master,
        bool shaded = true,
        int doodads = RoomModels.EveryDoodad,
        ModelProgress? progress = null,
        CameraShot? camera = null,
        bool sight = false,
        IReadOnlyList<(string Room, int X, int Y, int Turn)>? rooms = null)
        => Laid(read, AreaBlock.Path, grid, x, y, 0, shaded, doodads, tools: false, fitted: false, progress, DoodadHeight.File, entities: null, camera, sight,
            cut: 0, master ?? string.Empty, maskCells: false, groundHeights: false, shared: null, asBlock: RoomLayout.Block(size, size), rooms: rooms);

    /// <summary>The walk behind <see cref="Of"/>, with a paint cache that may outlive it.</summary>
    /// <param name="shared">A paint cache outliving the build - the stage sweep's, one for every room - or null for one of its own.</param>
    internal static MonsterModel Laid(
        Func<string, byte[]?>? read,
        string? path,
        TerrainGrid? grid,
        int x,
        int y,
        int turn,
        bool shaded,
        int doodads,
        bool tools,
        bool fitted,
        ModelProgress? progress,
        DoodadHeight heights,
        IReadOnlyList<WorldEntity>? entities,
        CameraShot? camera,
        bool sight,
        int cut,
        string master,
        bool maskCells,
        bool groundHeights,
        MonsterModels.Paints? shared,
        RoomLayout? asBlock = null,
        IReadOnlyList<(string Room, int X, int Y, int Turn)>? rooms = null)
    {
        if (read is null)
        {
            return MonsterModel.None with { Why = "no install to read" };
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return MonsterModel.None with { Why = "no room to read" };
        }

        if (grid?.Tiles is not { } tiles)
        {
            return MonsterModel.None with { Why = "the area's tiles are not read - the room cannot be laid from them" };
        }

        long bytes = 0;
        var files = 0;
        byte[]? Counted(string one)
        {
            byte[]? got = read(one.Replace('\\', '/').Trim());
            if (got is not null)
            {
                Interlocked.Add(ref bytes, got.Length);
                Interlocked.Increment(ref files);
            }

            return got;
        }

        // A BLOCK HAS NO .arm: the area's own tiles over it, and nothing a room would add - see Block.
        RoomLayout room = asBlock ?? RoomLayout.Read(Counted(path));
        if (!room.Ready)
        {
            return MonsterModel.None with { Why = $"the room did not read: {path} - {room.Why}", Bytes = bytes, Files = files };
        }

        (int wide, int tall) = (turn & 1) == 0 ? (room.Width, room.Height) : (room.Height, room.Width);

        // EACH TILE FILE ONCE, however many pieces of it the room holds - and counted for the bar as
        // each is read, which is while the pieces are found: that is where the files are opened.
        MonsterModels.Paints paints = shared ?? new MonsterModels.Paints { Progress = progress };
        var made = new Dictionary<int, Made>();

        // THE GROUND'S PAINTER, from the area's MaterialsList - the master given, or the one beside the room's Rooms/.
        string masterFile = master.Length > 0 ? master : asBlock is null ? GroundPainter.MasterBeside(read, path) : string.Empty;
        (GroundPainter? painter, string unpainted) = GroundPainter.For(Counted, masterFile, paints, maskCells, groundHeights);

        // AND THE GROUND'S OWN SCATTER, strewn as each piece's ground is painted - see GroundScatter. The room's frame lies at the block's
        // least tile of the area, and where the area's walkable grid is known a layer kept off blocking ground is kept off it - known for
        // the whole grid, not the block: a room laid over sea or abyss in a read area holds no walkable cell and still knows it holds none.
        GroundScatter? scatter = painter is null ? null : GroundScatter.For(Counted, masterFile, painter);
        var areaCorner = new Vector2(x, y) * TileModels.Side;
        Func<Vector2, bool>? blocked = scatter is not null && grid.AnyWalkable
            ? at => !grid.IsWalkable((x * TerrainGrid.CellsPerTile) + (int)MathF.Floor(at.X / RoomModels.CellSize), (y * TerrainGrid.CellsPerTile) + (int)MathF.Floor(at.Y / RoomModels.CellSize))
            : null;

        // AND ITS TILEMATERIALOVERRIDES, on the tiles and on every doodad laid over them - the tiles' own, the room's own lines, other rooms'
        // and the ground's scatter (DECIDED from the live client, the tiles' 2026-10-29 and the rooms' lines 2026-10-30; see
        // MonsterModels.Swapped). Crimson Shores' boss room drew pink stromatolites and the tendrils its master hides.
        IReadOnlyDictionary<string, string> swaps = masterFile.Length > 0 ? TilesetIndex.Overrides(Counted, masterFile) : new Dictionary<string, string>();
        IReadOnlyDictionary<string, string>? tileSwaps = swaps.Count > 0 ? swaps : null;
        string tileset = masterFile.Length > 0 ? TilesetIndex.Short(masterFile) : string.Empty;
        var grounds = new List<MonsterModel>();
        Made Make(int id)
        {
            (MonsterModel props, SkinnedMesh ground, int across, int down, TileDoodads lines, IReadOnlyList<GroundPiece> subTiles) =
                TileModels.Apart(Counted, tiles.Paths[id], walls: true, paints, tileSwaps, tileset);
            return Made.Of(props, ground, across, down, lines) with { Pieces = subTiles, Corners = CornersOf(read, tiles.Paths[id], subTiles) };
        }

        // EVERY TILE FILE OF THE BLOCK ON EVERY CORE FIRST, through the shared cache (Paints). The pieces then take them in the order
        // they first ask for them, as they read them before, so what the room is joined from comes in the same order; a file the
        // block does not name - a piece reaching in from outside it - is read when asked, as before.
        int[] block = Distinct(tiles, x, y, wide, tall);
        var ready = new Made[block.Length];
        using (ModelProgress.Step reading = ModelProgress.Begin(progress, "reading the tile files", block.Length))
        {
            Parallel.For(0, block.Length, at =>
            {
                ready[at] = Make(block[at]);
                reading.Advance();
            });
        }

        var prefetched = new Dictionary<int, Made>(block.Length);
        for (var at = 0; at < block.Length; at++)
        {
            prefetched[block[at]] = ready[at];
        }

        Made Of(int id)
        {
            if (!made.TryGetValue(id, out Made? one))
            {
                one = prefetched.TryGetValue(id, out Made? early) ? early : Make(id);
                made[id] = one;
            }

            return one;
        }

        List<TilePiece> pieces = tiles.Pieces(x, y, wide, tall, id => (Of(id).Width, Of(id).Height));

        (Way[] ways, Way chosen, bool settled, int asked) = Weighed(pieces, Of, grid, x, y);

        // LAID, the room's corner at the picture's origin and each piece on the area's own ground.
        var pile = new ModelPile();
        long mostTriangles = (long)(doodads > 0 ? doodads : RoomModels.UsualDoodads) * RoomModels.TrianglesPerDoodad;
        int laidPieces = 0, broken = 0, bare = 0, capped = 0, unturned = 0;
        var brokenFiles = new List<string>();
        var bareFiles = new List<string>();
        void Noted(List<string> files, int id)
        {
            if (files.Count < 4 && !files.Contains(tiles.Paths[id], StringComparer.OrdinalIgnoreCase))
            {
                files.Add(tiles.Paths[id]);
            }
        }

        int several = 0, fromTop = 0, fromBottom = 0;
        int fitsFitted = 0, fitsAtLevel = 0;
        var above = new List<float>(pieces.Count);
        var used = new HashSet<int>();
        var tileDoodads = new List<(RoomDoodad One, Matrix4x4 Place)>();
        int facingAway = 0, bothLists = 0, mirroredLists = 0;
        var withLines = new HashSet<int>();
        foreach (TilePiece piece in pieces)
        {
            Made one = Of(piece.Id);
            if (!piece.Whole)
            {
                broken++;
                Noted(brokenFiles, piece.Id);
                continue;
            }

            if (!one.Props.Ready && !one.Ground.Ready)
            {
                bare++;
                Noted(bareFiles, piece.Id);
                continue;
            }

            Matrix3x2 flat = chosen.Flat(piece, one, x, y);
            if (piece.Width * piece.Height > 1)
            {
                several++;
                fromTop += Named(piece, one, tiles, x, y, flat, fromTheTop: true) ? 1 : 0;
                fromBottom += Named(piece, one, tiles, x, y, flat, fromTheTop: false) ? 1 : 0;
            }

            long cost = (one.Props.Ready ? one.Props.Mesh.Triangles : 0) + (one.Ground.Ready ? one.Ground.Triangles : 0);
            if (pile.Triangles + cost > mostTriangles)
            {
                capped++;
                continue;
            }

            float level = Level(piece, grid);
            Height height = Way.Measure(piece, one, grid, x, y, flat, level);
            if (height.Samples >= 3)
            {
                above.Add(level - height.Fitted);
                fitsFitted += height.MissFitted <= Fits ? 1 : 0;
                fitsAtLevel += height.MissAtLevel <= Fits ? 1 : 0;
            }

            float lift = fitted ? height.Fitted : level;
            Matrix4x4 place = Spatial(flat) * Matrix4x4.CreateTranslation(0f, 0f, lift);
            if (one.Doodads.Lines.Count > 0)
            {
                facingAway += TileDoodadsOf(piece, one.Doodads, one.Width, one.Height, x, y, lift, grid, heights, tileDoodads);
                mirroredLists += Mirrors(piece.Placement) ? 1 : 0;
                if (withLines.Add(piece.Id) && one.Doodads.Both)
                {
                    bothLists++;
                }
            }

            // NAMED BY FILE AND TILE, so the probe can say which piece a pixel belongs to - one string
            // per piece, shared by all of its shapes.
            string called = string.Create(CultureInfo.InvariantCulture, $"{tiles.Paths[piece.Id]} at tile {piece.MinX}, {piece.MinY}");
            if (one.Props.Ready)
            {
                pile.Add(one.Props, place, called);
            }

            if (one.Ground.Ready)
            {
                if (painter?.Paint(tiles.Paths[piece.Id], one.Ground, one.Pieces, place, RoomModels.GroundSource + called) is { Ready: true } painted)
                {
                    pile.Add(painted, place, RoomModels.GroundSource + called);
                    grounds.Add(painted);
                }
                else
                {
                    pile.AddPlain(one.Ground, place, RoomModels.GroundSource + called);
                }

                scatter?.Strew(tiles.Paths[piece.Id], one.Ground, one.Pieces, one.Width, one.Height, place, areaCorner, blocked);
            }

            used.Add(piece.Id);
            laidPieces++;
            unturned += piece.Placement < 0 ? 1 : 0;
        }

        // WHAT CAN BE SEEN, worked out before a doodad is read - see Hiding.
        Vector2 origin = areaCorner;
        Hiding hiding = Hiding.Of(pile, grid, origin, new Vector2(wide, tall) * TileModels.Side, camera, sight, cut, progress);

        // THE DOODADS, where the corners were found and on the area's ground under them.
        Matrix3x2 laying = RoomFinder.Laying(room.Width, room.Height, turn);
        Matrix4x4 beyondFlat = Through(laying);
        var lights = new RoomLights.Gathered(Counted);

        // THE ROOMS THE AREA PLACED WHERE THEY STAND, their own lines - a block's, which has none of its own (see Block).
        (List<(RoomDoodad One, Matrix4x4 Place)> others, string othersSaid) = rooms is { Count: > 0 }
            ? OthersOf(Counted, rooms, x, y, grid, heights)
            : ([], string.Empty);
        RoomModels.Doodads laid = RoomModels.Lay(room, Counted, paints, pile, doodads, tools, one =>
        {
            Vector2 at = Vector2.Transform(new Vector2(one.X, one.Y) / RoomModels.CellsPerTile, laying);
            int cellX = (int)((x + at.X) * RoomModels.CellsPerTile);
            int cellY = (int)((y + at.Y) * RoomModels.CellsPerTile);
            float ground = grid.HeightAt(cellX, cellY);
            float z = heights switch
            {
                DoodadHeight.File when one.Height is { } height => height,
                DoodadHeight.Added when one.Height is { } height => ground + height,
                _ => ground,
            };
            return beyondFlat * Matrix4x4.CreateTranslation(0f, 0f, z);
        }, heights, lights, hiding.Sieve, tileDoodads, tileSwaps, others, scatter?.Lines);

        SkinnedMesh joined = RoomModels.Watered(hiding.Keep(SkinnedMesh.Joined(pile.Joins), pile), pile.Modes);
        if (!joined.Ready)
        {
            return MonsterModel.None with
            {
                Why = $"nothing to draw: {pieces.Count} pieces under the place, {laidPieces} laid, and {laid.Placed} doodads"
                    + string.Concat(hiding.Said().Select(one => "; " + one)),
                Bytes = bytes,
                Files = files,
            };
        }

        var said = new List<string>
        {
            (asBlock is null
                ? string.Create(CultureInfo.InvariantCulture, $"laid as the area laid it at tile {x}, {y}, {RoomFinder.Said(turn)}: {laidPieces} pieces of {used.Count} tile files")
                : string.Create(CultureInfo.InvariantCulture, $"the area round the player as it laid it, {wide} x {tall} tiles from tile {x}, {y} - its tiles and their own doodads, no room's: {laidPieces} pieces of {used.Count} tile files"))
                + (broken > 0 ? string.Create(CultureInfo.InvariantCulture, $", {broken} not whole and left out") : string.Empty)
                + (bare > 0 ? string.Create(CultureInfo.InvariantCulture, $", {bare} with nothing to draw") : string.Empty)
                + (capped > 0 ? string.Create(CultureInfo.InvariantCulture, $", {capped} left out to keep it turnable - the doodads slider raises it") : string.Empty)
                + (unturned > 0 ? string.Create(CultureInfo.InvariantCulture, $", {unturned} laid no way the game's tables said - drawn as their files have them") : string.Empty),
            Heights(ways, chosen, settled),
            Levels(above, fitsFitted, fitsAtLevel, fitted),
        };
        if (asked > 0)
        {
            said.Insert(1, Cornered(ways, chosen, asked));
        }
        if (several > 0)
        {
            said.Add(string.Create(CultureInfo.InvariantCulture,
                $"sub-tiles: of {several} pieces several tiles across, {fromTop} have every tile where the area's own index puts it counting the file's rows from the top, {fromBottom} counting them from the bottom"));
        }

        if (withLines.Count > 0)
        {
            said.Add(string.Create(CultureInfo.InvariantCulture,
                $"tile doodads: {tileDoodads.Count} from the doodad lists of {withLines.Count} tile files, {facingAway} wall tops facing away from the camera left out as the game leaves them")
                + (mirroredLists > 0 ? string.Create(CultureInfo.InvariantCulture, $"; the doodads of {mirroredLists} pieces laid mirrored are mirrored with them, as the game draws a mirrored room's own - DECIDED, not measured on a tile's doodad") : string.Empty)
                + (bothLists > 0 ? string.Create(CultureInfo.InvariantCulture, $"; {bothLists} files carry a list beside the .tdt and one beside the template - the .tdt's taken, which no capture has measured yet") : string.Empty));
        }

        if (othersSaid.Length > 0)
        {
            said.Add(othersSaid);
        }

        if (tileSwaps is not null)
        {
            int swappedTiles = used.Count(id => made.TryGetValue(id, out Made? one) && one.Props.Swapped > 0);
            said.Add(string.Create(CultureInfo.InvariantCulture,
                $"materials: the master's TileMaterialOverrides ({swaps.Count} swaps) put on {swappedTiles} of the {used.Count} tile files and {laid.SwappedFiles} of the doodad files - the swaps reach the tiles, their own doodads, the room's own lines, other rooms' lines and the ground's scatter, DECIDED from the live client (the rooms' lines 2026-10-30; the scatter's chosen, v0.1.368)"));
        }

        said.Add(painter?.Said() ?? "ground: plain - " + unpainted);
        if (scatter is not null)
        {
            said.Add(scatter.Said());
        }

        said.AddRange(laid.Said());
        said.AddRange(hiding.Said());
        string held = HeightsSaid(room, laying, x, y, grid, entities);
        if (held.Length > 0)
        {
            said.Add(held);
        }

        (int groundPainted, IReadOnlyDictionary<string, int> plainGround, int unlisted, IReadOnlyList<string> unlistedNames) =
            painter?.Counted() ?? (0, new Dictionary<string, int>(), 0, []);
        MonsterModel model = RoomModels.Piled(joined, pile, path, made.Values.Select(one => one.Props).Concat(grounds).Concat(laid.Models)) with
        {
            Account = new RoomAccount
            {
                Pieces = laidPieces,
                Broken = broken,
                Bare = bare,
                Capped = capped,
                Unturned = unturned,
                TileFiles = used.Count,
                Swaps = swaps.Count,
                SwappedTiles = used.Count(id => made.TryGetValue(id, out Made? one) && one.Props.Swapped > 0),
                SwappedDoodadFiles = laid.SwappedFiles,
                Unpainted = painter is null ? unpainted : string.Empty,
                GroundPainted = groundPainted,
                GroundPlain = plainGround,
                GroundPlainSubjects = painter?.PlainSubjects() ?? new Dictionary<string, IReadOnlyList<string>>(),
                UnlistedGroundFiles = painter?.UnlistedFiles() ?? new Dictionary<string, string>(),
                BrokenFiles = brokenFiles,
                BareFiles = bareFiles,
                GroundUnlisted = unlisted,
                UnlistedGrounds = unlistedNames,
            },
            Parts = laidPieces + laid.Placed,
            Bytes = bytes,
            Files = files,
            Move = string.Join("; ", said),
            Lights = lights.Lights,
            LightsSaid = lights.Said(),
            DoodadFiles = laid.Files,
            AreaOrigin = origin,
            AreaStamp = RoomKey.Stamp(grid),
            BossSpot = BossSpotOf(room, laying),
        };

        return shaded ? MonsterModels.Shaded(Counted, model, paints) : model;
    }

    /// <summary>
    /// The doodad lines of rooms the area placed, each where its room stands, in the frame of a block whose least tile is (<paramref name="x"/>,
    /// <paramref name="y"/>) - as a laid room lays its own (<see cref="Of"/>): turned and mirrored with its room, on the area's ground under it.
    /// </summary>
    internal static (List<(RoomDoodad One, Matrix4x4 Place)> Lines, string Said) OthersOf(
        Func<string, byte[]?> read, IReadOnlyList<(string Room, int X, int Y, int Turn)> rooms, int x, int y, TerrainGrid grid, DoodadHeight heights)
    {
        var lines = new List<(RoomDoodad One, Matrix4x4 Place)>();
        int read_ = 0, none = 0;
        float size = RoomModels.CellSize;
        foreach ((string path, int rx, int ry, int turn) in rooms)
        {
            RoomLayout room = RoomLayout.Read(read(path));
            if (!room.Ready || room.Doodads.Count == 0)
            {
                none++;
                continue;
            }

            read_++;
            Matrix3x2 laying = RoomFinder.Laying(room.Width, room.Height, turn);
            Matrix4x4 beyondFlat = Through(laying) * Matrix4x4.CreateTranslation((rx - x) * TileModels.Side, (ry - y) * TileModels.Side, 0f);
            foreach (RoomDoodad one in room.Doodads)
            {
                if (one.Ao.Length == 0)
                {
                    continue;
                }

                Vector2 at = Vector2.Transform(new Vector2(one.X, one.Y) / RoomModels.CellsPerTile, laying);
                float ground = grid.HeightAt((int)((rx + at.X) * RoomModels.CellsPerTile), (int)((ry + at.Y) * RoomModels.CellsPerTile));
                float z = heights switch
                {
                    DoodadHeight.File when one.Height is { } height => height,
                    DoodadHeight.Added when one.Height is { } height => ground + height,
                    _ => ground,
                };
                float scale = one.Scale > 0f ? one.Scale : 1f;
                Matrix4x4 place = Matrix4x4.CreateScale(scale) * one.Turning * Matrix4x4.CreateTranslation(one.X * size, one.Y * size, 0f)
                    * beyondFlat * Matrix4x4.CreateTranslation(0f, 0f, z);
                lines.Add((one, place));
            }
        }

        return (lines, string.Create(CultureInfo.InvariantCulture,
            $"rooms placed in the area: {lines.Count} doodad lines of {read_} of the {rooms.Count} room places reaching the block, where the tool placed each (by its doodads or its tiles){(none > 0 ? string.Create(CultureInfo.InvariantCulture, $", {none} with no line or not read") : string.Empty)}"));
    }

    /// <summary>
    /// The ground of every whole piece laid over a block of the area, each placed as a laid room places it - in world units, the area's
    /// tile (0, 0) at the origin - for the capture's picture of the ground on the screen (CaptureGroundScreen).
    /// </summary>
    /// <remarks>
    /// THE ROOM'S OWN ARITHMETIC, not a second copy of it: the way the files lie is weighed from the block's sloped pieces exactly as
    /// <see cref="Of"/> does, so what the capture draws over the game's picture is where the Monster Book's laid room has its ground.
    /// Placed from the area's corner rather than a room's, so a point is a world point.
    ///
    /// EACH PIECE AT ITS TILES' OWN LEVEL, NOT FITTED (v0.1.314, MEASURED). Fitted to the area's ground - the mean of the area's height less
    /// the file's - Port's harbour came out 450 units above the game's: the sea floor under its water, 500 down in the file, outweighs the
    /// pier and the quay where the area's grid says nought. v0.1.313's capture showed it twice over: the material choices' pixel counts
    /// came out as the harbour drawn at -450 and no other height, and the game's own cobblestones line up with the pier's materials on the
    /// plane at its tile's level, nought, within 16 units. Every comparison the capture drew over the harbour until then was measured on
    /// the wrong pixels. The laid room lays its pieces so too from v0.1.316; the line says how far the fit would have put each piece.
    /// </remarks>
    /// <param name="read">The install.</param>
    /// <param name="grid">The area, its tiles read.</param>
    /// <param name="x">The block's lowest tile across.</param>
    /// <param name="y">The block's lowest tile down.</param>
    /// <param name="wide">Tiles across.</param>
    /// <param name="tall">Tiles down.</param>
    /// <param name="said">How the files were found to lie on the area, the laid room's own lines - the corners' and the heights'.</param>
    internal static List<LaidGround> GroundsOver(Func<string, byte[]?> read, TerrainGrid grid, int x, int y, int wide, int tall, out string said)
    {
        var laid = new List<LaidGround>();
        if (grid.Tiles is not { } tiles)
        {
            said = "the area's tiles are not read";
            return laid;
        }

        var paints = new MonsterModels.Paints();
        var made = new Dictionary<int, Made>();
        Made Of(int id)
        {
            if (!made.TryGetValue(id, out Made? one))
            {
                (MonsterModel props, SkinnedMesh ground, int across, int down, TileDoodads lines, IReadOnlyList<GroundPiece> subTiles) =
                    TileModels.Apart(read, tiles.Paths[id], walls: true, paints);
                one = Made.Of(props, ground, across, down, lines) with { Pieces = subTiles, Corners = CornersOf(read, tiles.Paths[id], subTiles) };
                made[id] = one;
            }

            return one;
        }

        List<TilePiece> pieces = tiles.Pieces(x, y, wide, tall, id => (Of(id).Width, Of(id).Height));
        (Way[] ways, Way chosen, bool settled, int asked) = Weighed(pieces, Of, grid, 0, 0);
        said = asked > 0 ? Cornered(ways, chosen, asked) + "; " + Heights(ways, chosen, settled) : Heights(ways, chosen, settled);
        var fits = new List<(float Off, string Piece)>();
        foreach (TilePiece piece in pieces)
        {
            Made one = Of(piece.Id);
            if (!piece.Whole || !one.Ground.Ready)
            {
                continue;
            }

            Matrix3x2 flat = chosen.Flat(piece, one, 0, 0);
            float level = Level(piece, grid);
            Height height = Way.Measure(piece, one, grid, 0, 0, flat, level);
            if (height.Samples > 0)
            {
                fits.Add((height.Fitted - level, string.Create(CultureInfo.InvariantCulture, $"{Path.GetFileNameWithoutExtension(tiles.Paths[piece.Id])} at tile {piece.MinX}, {piece.MinY}")));
            }

            laid.Add(new LaidGround(tiles.Paths[piece.Id], piece, one.Ground, one.Pieces, Spatial(flat) * Matrix4x4.CreateTranslation(0f, 0f, level)));
        }

        // BOTH HALVES: where the pieces lie, and where fitting them to the area's ground would have put them instead.
        (float Off, string Piece) farthest = fits.Count > 0 ? fits.MaxBy(fit => MathF.Abs(fit.Off)) : default;
        said += fits.Count == 0
            ? "; each piece at its tiles' own level"
            : string.Create(CultureInfo.InvariantCulture,
                $"; each piece at its tiles' own level - fitted to the area's ground, {fits.Count(fit => MathF.Abs(fit.Off) > Fits)} of {fits.Count} would sit over {Fits} units off it, the farthest {farthest.Piece} by {farthest.Off:+0;-0;0}");
        return laid;
    }

    /// <summary>
    /// THE WAY THE FILES LIE ON THE TEMPLATE, by the game's own corner ground types and then by the sloped pieces - see the remarks on
    /// the class. As filed comes first, so it is the one kept wherever neither can tell two ways apart.
    /// </summary>
    /// <returns>Every way weighed, the one chosen, whether the heights chose it, and how many sub-tiles' corners the game was asked of.</returns>
    private static (Way[] Ways, Way Chosen, bool Settled, int Asked) Weighed(List<TilePiece> pieces, Func<int, Made> of, TerrainGrid grid, int x, int y)
    {
        var ways = new Way[Relations.Length];
        for (var way = 0; way < ways.Length; way++)
        {
            ways[way] = new Way(Relations[way]);
        }

        // THE GAME'S NAMES ONCE, so a corner is held against a name rather than a path worked out again per way and per corner.
        string[]? names = grid.Ground is { } types ? GroundNames(types) : null;
        int asked = 0;
        foreach (TilePiece piece in pieces)
        {
            Made one = of(piece.Id);
            if (!piece.Whole)
            {
                continue;
            }

            if (names is not null && one.Corners.Count > 0)
            {
                asked += one.Corners.Count(corners => corners.Length == 4);
                foreach (Way way in ways)
                {
                    way.Meet(piece, one, grid.Ground!, names, x, y);
                }
            }

            if (one.Lookup is null || !Way.Rises(piece, grid))
            {
                continue;
            }

            foreach (Way way in ways)
            {
                way.Weigh(piece, one, grid, x, y);
            }
        }

        Way[] allowed = Allowed(ways);
        bool settled = allowed.Any(one => one.Sloped > 0);
        return (ways, settled ? allowed.MinBy(one => one.Residual)! : allowed[0], settled, asked);
    }

    /// <summary>The ways the game's corner types leave - those laying the most sub-tiles' corners on them, as filed first - or all where they lay none.</summary>
    private static Way[] Allowed(Way[] ways)
    {
        int most = ways.Max(one => one.Met);
        return most > 0 ? [.. ways.Where(one => one.Met == most)] : ways;
    }

    /// <summary>What the game's corner types said about the ways a file can lie, for the line under the picture.</summary>
    private static string Cornered(Way[] ways, Way chosen, int asked)
    {
        Way[] allowed = Allowed(ways);
        if (chosen.Met == 0)
        {
            return string.Create(CultureInfo.InvariantCulture,
                $"corners: no way lays any of {asked} sub-tiles' grid corners on the game's own corner types - the heights choose among all {ways.Length}");
        }

        string head = string.Create(CultureInfo.InvariantCulture,
            $"corners: {chosen.Met}/{asked} sub-tiles have their grid's four ground types at the game's own corners with the files {chosen.Said}");
        if (allowed.Length > 1)
        {
            return head + string.Create(CultureInfo.InvariantCulture, $", and as many with {allowed.Length - 1} other ways, which the heights choose among");
        }

        Way next = ways.Where(one => one != chosen).MaxBy(one => one.Met)!;
        return head + string.Create(CultureInfo.InvariantCulture, $"; next {next.Said} {next.Met}/{asked}");
    }

    /// <summary>The area's ground types by the name a grid's corner is held against: '-' for the null slot and for an empty one.</summary>
    internal static string[] GroundNames(TerrainGroundTypes types)
    {
        ArgumentNullException.ThrowIfNull(types);
        var names = new string[types.Types.Count];
        for (var type = 0; type < names.Length; type++)
        {
            names[type] = type == 0 || types.Types[type].Length == 0 ? "-" : TerrainGroundTypes.NameFor(types.Types[type]);
        }

        return names;
    }

    /// <summary>
    /// Whether a sub-tile's four corners from its definition's grid are the game's own corner types at the area's tile corners the place
    /// puts them on, each to each.
    /// </summary>
    /// <remarks>
    /// THE GAME'S OWN CHECK OF A PLACING. Corner k of TileDefinition.CornersOf lies at mesh column x - 1 + (k &amp; 1) and mesh row
    /// 1 - y + (k &gt;&gt; 1) tiles (its remarks); the place takes it into the world from the area tile (<paramref name="x"/>,
    /// <paramref name="y"/>), where TileCornerData holds the game's type. v0.1.257's capture of Port made it by hand: laid as the game's
    /// turn decodes it, all ten harbour sub-tiles on the screen met the game's - one ocean on one side and nothing on the other, which only
    /// the mirror the game laid it with puts the right way round - and laid across its footprint none did.
    /// </remarks>
    /// <param name="corners">The four corners' names, '-' unnamed.</param>
    /// <param name="names">The area's types by name - see <see cref="GroundNames"/>.</param>
    internal static bool CornersMeet(Matrix4x4 place, int x, int y, GroundPiece piece, IReadOnlyList<string> corners, TerrainGroundTypes types, string[] names)
    {
        ArgumentNullException.ThrowIfNull(corners);
        ArgumentNullException.ThrowIfNull(types);
        ArgumentNullException.ThrowIfNull(names);
        if (corners.Count != 4)
        {
            return false;
        }

        for (var corner = 0; corner < 4; corner++)
        {
            var file = new Vector3((piece.X - 1 + (corner & 1)) * TileModels.Side, (1 - piece.Y + (corner >> 1)) * TileModels.Side, 0f);
            Vector3 at = Vector3.Transform(file, place);
            int type = types.At((int)MathF.Round(at.X / TileModels.Side) + x, (int)MathF.Round(at.Y / TileModels.Side) + y);
            if (!string.Equals(corners[corner], type > 0 && type < names.Length ? names[type] : "-", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Each sub-tile's four corners by name from a tile's definition grid, or nothing where it carries none.</summary>
    private static IReadOnlyList<string[]> CornersOf(Func<string, byte[]?> read, string path, IReadOnlyList<GroundPiece> pieces)
    {
        (TileDefinition definition, _) = TileModels.Defined(read, path);
        if (definition.GroundGrid.Count == 0)
        {
            return [];
        }

        var corners = new string[pieces.Count][];
        for (var at = 0; at < pieces.Count; at++)
        {
            corners[at] = [.. definition.CornersOf(pieces[at].X, pieces[at].Y).Select(name => name.Length == 0 ? "-" : TerrainGroundTypes.NameFor(name.Replace('\\', '/')))];
        }

        return corners;
    }

    /// <summary>What the ground said about the ways the corners left, for the line under the picture.</summary>
    private static string Heights(Way[] ways, Way chosen, bool settled)
    {
        if (!settled)
        {
            return $"heights: no sloped piece to settle how the files lie on the area's ground - drawn {chosen.Said}, each piece raised to the area's ground";
        }

        Way[] allowed = Allowed(ways);
        string head = string.Create(CultureInfo.InvariantCulture,
            $"heights: drawn with the files {chosen.Said} before the game's own turn - {chosen.Fitting}/{chosen.Sloped} sloped pieces meet the area's ground within {Fits} units");
        if (allowed.Length == 1)
        {
            return head;
        }

        Way[] ranked = [.. allowed.OrderBy(one => one.Residual)];
        Way next = ranked[0] == chosen ? ranked[1] : ranked[0];
        head += string.Create(CultureInfo.InvariantCulture, $"; next {next.Said} {next.Fitting}/{next.Sloped}");
        if (allowed.Length == 2)
        {
            return head;
        }

        int rest = allowed.Where(one => one != chosen && one != next).Max(one => one.Fitting);
        return head + string.Create(CultureInfo.InvariantCulture, $"; the other {allowed.Length - 2} at most {rest}/{chosen.Sloped}");
    }

    /// <summary>What the two ways of setting a piece's height said, for the line under the picture.</summary>
    private static string Levels(List<float> above, int fitsFitted, int fitsAtLevel, bool fitted)
    {
        string drawn = fitted ? "drawn fitted" : "drawn at the tiles' levels";
        if (above.Count == 0)
        {
            return "level: no piece with a ground to measure - " + drawn;
        }

        above.Sort();
        float Share(float share) => above[Math.Clamp((int)MathF.Round(share * (above.Count - 1)), 0, above.Count - 1)];
        return string.Create(CultureInfo.InvariantCulture,
            $"level: fitted to the area's ground, a piece sits {Share(0.5f):+0;-0;0} units above its tile's own level (the middle half {Share(0.25f):+0;-0;0} to {Share(0.75f):+0;-0;0}, all {above[0]:+0;-0;0} to {above[^1]:+0;-0;0}), and its ground meets the area's within {Fits} units on {fitsAtLevel}/{above.Count} pieces at the tile's level, {fitsFitted}/{above.Count} fitted - {drawn}");
    }

    /// <summary>The level of the tiles a piece covers, which their sub-tile heights are described relative to - their mean, should they differ.</summary>
    private static float Level(TilePiece piece, TerrainGrid grid)
    {
        float sum = 0f;
        for (int tileY = piece.MinY; tileY <= piece.MaxY; tileY++)
        {
            for (int tileX = piece.MinX; tileX <= piece.MaxX; tileX++)
            {
                sum += grid.LevelAt(tileX, tileY);
            }
        }

        return sum / (piece.Width * piece.Height);
    }

    /// <summary>
    /// Whether every area tile of a piece holds the sub-tile the drawing puts over it, by the area's own index: its column, and its row counted from the file's top or its bottom.
    /// </summary>
    private static bool Named(TilePiece piece, Made one, TerrainTiles tiles, int x, int y, Matrix3x2 flat, bool fromTheTop)
    {
        if (!Matrix3x2.Invert(flat, out Matrix3x2 back))
        {
            return false;
        }

        for (int tileY = piece.MinY; tileY <= piece.MaxY; tileY++)
        {
            for (int tileX = piece.MinX; tileX <= piece.MaxX; tileX++)
            {
                Vector2 file = Vector2.Transform(new Vector2(tileX - x + 0.5f, tileY - y + 0.5f) * TileModels.Side, back);
                int column = (int)MathF.Floor((file.X - one.Least.X) / TileModels.Side);
                int row = (int)MathF.Floor((fromTheTop ? one.Most.Y - file.Y : file.Y - one.Least.Y) / TileModels.Side);
                if (tiles.SubAt(tileX, tileY) != (column, row))
                {
                    return false;
                }
            }
        }

        return true;
    }

    /// <summary>
    /// Where a piece's height comes out each way: the lift that fits its ground to the area's, how far its ground then misses the area's, and how far it misses at its tile's level - root mean squares, in world units.
    /// </summary>
    private readonly record struct Height(float Fitted, int Samples, double MissFitted, double MissAtLevel);

    /// <summary>Most doodads named one by one in the heights line; the rest are only counted.</summary>
    private const int MostHeldNamed = 6;

    /// <summary>
    /// The room's doodads held against the entities the game made of them: the height each line carries beside the entity's own z, its terrain height and the area's ground there, and how far it is from where the room puts it.
    /// </summary>
    /// <remarks>
    /// WHY THIS EXISTS. A doodad line carries a value no reference names - RoomDoodad.Height - and the
    /// one pot read by hand from memory sat at exactly its line's -115. That one cannot tell "the value
    /// is the height" from "the ground plus the value", because the ground under it was where the value
    /// put it. The game settles it wherever the two differ, and every doodad it makes an entity of is a
    /// case: found by its stub, which is the entity's own path, and by being the nearest of that path to
    /// where the room puts the doodad - within a tile, or it is somebody else's.
    ///
    /// BOTH HALVES PRINTED, AND COUNTED, NOT CONCLUDED: the z and each reading's prediction, so the line
    /// is read rather than trusted - the same stance as the level line above it.
    /// </remarks>
    internal static string HeightsSaid(RoomLayout room, Matrix3x2 laying, int x, int y, TerrainGrid grid, IReadOnlyList<WorldEntity>? entities)
    {
        if (entities is null)
        {
            return string.Empty;
        }

        var byPath = new Dictionary<string, List<WorldEntity>>(StringComparer.OrdinalIgnoreCase);
        foreach (WorldEntity entity in entities)
        {
            if (entity.Path.Length == 0)
            {
                continue;
            }

            if (!byPath.TryGetValue(entity.Path, out List<WorldEntity>? named))
            {
                named = [];
                byPath[entity.Path] = named;
            }

            named.Add(entity);
        }

        // EACH ENTITY TO ONE DOODAD, the nearest pairs first: two pots a few cells apart would otherwise
        // both claim the one entity nearer to either, and the second pot report a height it never had.
        var pairs = new List<(float Distance, int Doodad, WorldEntity Entity)>();
        var cells = new Vector2[room.Doodads.Count];
        float nearest = float.PositiveInfinity;
        for (var at = 0; at < room.Doodads.Count; at++)
        {
            RoomDoodad one = room.Doodads[at];
            cells[at] = InArea(laying, x, y, new Vector2(one.X, one.Y) / RoomModels.CellsPerTile);
            if (one.Stub.Length == 0 || !byPath.TryGetValue(one.Stub, out List<WorldEntity>? candidates))
            {
                continue;
            }

            foreach (WorldEntity entity in candidates)
            {
                float distance = Vector2.Distance(new Vector2(entity.WorldX, entity.WorldY), cells[at]);
                nearest = MathF.Min(nearest, distance);
                if (distance <= TileModels.Side)
                {
                    pairs.Add((distance, at, entity));
                }
            }
        }

        pairs.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var matched = new WorldEntity?[room.Doodads.Count];
        var taken = new HashSet<WorldEntity>(ReferenceEqualityComparer.Instance);
        foreach ((_, int doodad, WorldEntity entity) in pairs)
        {
            if (matched[doodad] is null && taken.Add(entity))
            {
                matched[doodad] = entity;
            }
        }

        int found = 0, carrying = 0, asHeight = 0, asAdded = 0, asGround = 0;
        var listed = new List<string>(MostHeldNamed);
        for (var at = 0; at < room.Doodads.Count; at++)
        {
            if (matched[at] is not { } entity)
            {
                continue;
            }

            RoomDoodad one = room.Doodads[at];
            var place = new Vector2(entity.WorldX, entity.WorldY);
            found++;
            float ground = grid.HeightAt((int)(entity.WorldX / RoomModels.CellSize), (int)(entity.WorldY / RoomModels.CellSize));
            bool height = one.Height is { } value && MathF.Abs(entity.WorldZ - value) < 1f;
            bool added = one.Height is { } plus && MathF.Abs(entity.WorldZ - (ground + plus)) < 1f;
            bool onGround = MathF.Abs(entity.WorldZ - ground) < 1f;
            carrying += one.Height.HasValue ? 1 : 0;
            asHeight += height ? 1 : 0;
            asAdded += added ? 1 : 0;
            asGround += onGround ? 1 : 0;

            if (listed.Count < MostHeldNamed)
            {
                string exact = one.Exact is { } spot
                    ? string.Create(CultureInfo.InvariantCulture, $", {Vector2.Distance(place, InArea(laying, x, y, spot / TileModels.Side)):0.0} from its exact place")
                    : string.Empty;
                listed.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{Tail(one.Ao)}: line {(one.Height is { } said ? said.ToString("0.#", CultureInfo.InvariantCulture) : "none")}, z {entity.WorldZ:0.#}, its terrain height {entity.TerrainHeight:0.#}, the area's ground {ground:0.#}, {Vector2.Distance(place, cells[at]):0.0} from its cell{exact}"));
            }
        }

        if (found == 0)
        {
            return Missed(room, entities, byPath, nearest);
        }

        string counted = string.Create(CultureInfo.InvariantCulture,
            $"doodad heights: {found} of the room's doodads are in memory as entities, {carrying} with a height in their line - z is that height on {asHeight}, the ground plus it on {asAdded}, the ground on {asGround}");
        return counted + "; " + string.Join("; ", listed) + (found > listed.Count ? string.Create(CultureInfo.InvariantCulture, $"; and {found - listed.Count} more") : string.Empty);
    }

    /// <summary>
    /// Why no doodad was found: how many entities were read, how many carry a path the room names as a stub and how near the nearest came, and how many carry a stub's name in another path.
    /// </summary>
    /// <remarks>
    /// THE NEAR MISSES, because "none" alone cannot say whether the place was wrong, the path was
    /// spelt another way, or the game simply holds none of them - the first report of this line was
    /// exactly that, beside a pot read by hand from memory minutes before.
    /// </remarks>
    private static string Missed(
        RoomLayout room, IReadOnlyList<WorldEntity> entities, Dictionary<string, List<WorldEntity>> byPath, float nearest)
    {
        var stubs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (RoomDoodad one in room.Doodads)
        {
            if (one.Stub.Length > 0 && stubs.Add(one.Stub))
            {
                names.Add(Tail(one.Stub));
            }
        }

        int sharing = 0;
        foreach (string stub in stubs)
        {
            sharing += byPath.TryGetValue(stub, out List<WorldEntity>? same) ? same.Count : 0;
        }

        int alike = 0;
        string example = string.Empty;
        foreach (WorldEntity entity in entities)
        {
            if (entity.Path.Length == 0 || stubs.Contains(entity.Path))
            {
                continue;
            }

            foreach (string name in names)
            {
                if (entity.Path.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    alike++;
                    if (example.Length == 0)
                    {
                        example = entity.Path;
                    }

                    break;
                }
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"doodad heights: none of the room's doodads is in memory as an entity - {entities.Count} entities read, {sharing} with a path the room names as a stub")
            + (sharing > 0 ? string.Create(CultureInfo.InvariantCulture, $", the nearest {nearest:0} units from such a doodad (a tile is {TileModels.Side:0})") : string.Empty)
            + (alike > 0 ? string.Create(CultureInfo.InvariantCulture, $"; {alike} with a stub's name in another path, such as {example}") : string.Empty);
    }

    /// <summary>A place in the room, in tiles from its corner, as a place in the area in world units.</summary>
    private static Vector2 InArea(Matrix3x2 laying, int x, int y, Vector2 inTiles)
    {
        Vector2 at = Vector2.Transform(inTiles, laying);
        return new Vector2(x + at.X, y + at.Y) * TileModels.Side;
    }

    private static string Tail(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>The different tile files the block names, in the order the block first names them.</summary>
    private static int[] Distinct(TerrainTiles tiles, int x, int y, int wide, int tall)
    {
        var seen = new HashSet<int>();
        var named = new List<int>();
        for (int row = y; row < y + tall; row++)
        {
            for (int column = x; column < x + wide; column++)
            {
                int id = tiles.IdAt(column, row);
                if (id >= 0 && seen.Add(id))
                {
                    named.Add(id);
                }
            }
        }

        return [.. named];
    }

    /// <summary>
    /// One laid piece's own doodads, each where the game puts it, into <paramref name="into"/> - and how many wall tops were
    /// left out for facing away from the camera.
    /// </summary>
    /// <remarks>
    /// WHERE, AS MEASURED (CaptureFixtureTests, Marrow): a line's place in the tile's own cells - its exact pair where it has
    /// one - about the tile's middle, through the game's placement as TileOrientation reads it, out to the middle of the area
    /// tiles the piece covers. NOT through the mesh's own matrix: a sub-tile's rows run down -y in the mesh (TileModels) and
    /// down +y in the cells, so a doodad hung on the mesh would stand mirrored; set as the game sets it, a doodad that misses
    /// its wall says the mesh is off, not the doodad.
    ///
    /// THE HEIGHT IN THE TILE'S FRAME, MEASURED: the game stands a tile's doodad at its line's z above the tile's own level -
    /// its entity's TerrainHeight is the two summed, every doodad found in Sulphuric Caverns, 41 of them on tiles at -203
    /// (CaptureFixtureTests). Drawn above the piece's lift, which is that level or the fit to the area's ground, whichever
    /// the piece itself is drawn at, so a doodad stays on the mesh it belongs to. The other two readings stay for
    /// comparison, as they do for a room's doodads (DoodadHeight).
    ///
    /// MIRRORED WITH A MIRRORED PIECE, DECIDED from the live client (2026-10-13) on a room's doodads' evidence: it was turned
    /// to the way its line faces once laid, on the reading that an entity carries an angle and no mirror - and Port's mirrored
    /// boss room refuted that reading for its own doodads (<see cref="Through"/>: the game holds them at minus their lines'
    /// angles and draws the ship's mirror image). A tile's doodads are entities of the same kinds, held at the angle its laid
    /// facing reads (TileDoodadList.Shown, (sin a, -cos a)), which the placement's mirror gives as well; that the game draws
    /// them mirrored is not measured on a tile's doodad.
    /// </remarks>
    /// <param name="piece">The laid piece.</param>
    /// <param name="doodads">Its file's doodad lists.</param>
    /// <param name="width">The file's size in tiles across, as its template has it.</param>
    /// <param name="height">The file's size in tiles down.</param>
    /// <param name="x">The room's lowest area tile across - the picture's origin.</param>
    /// <param name="y">The room's lowest area tile down.</param>
    /// <param name="lift">How far the piece itself is raised.</param>
    /// <param name="grid">The area, for its ground where the doodads are set on it.</param>
    /// <param name="heights">How high a doodad is set - see DoodadHeight.</param>
    /// <param name="into">Where each doodad goes, with its line.</param>
    internal static int TileDoodadsOf(
        TilePiece piece, TileDoodads doodads, int width, int height, int x, int y, float lift, TerrainGrid grid, DoodadHeight heights, List<(RoomDoodad One, Matrix4x4 Place)> into)
    {
        TileOrientation placed = TileOrientation.OfPlacement(piece.Placement);
        (int xx, int xy, int yx, int yy) = placed.IsPlacement ? placed.Turn : (1, 0, 0, 1);
        var turn = new Matrix4x4(xx, yx, 0f, 0f, xy, yy, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f);
        var middle = new Vector2((piece.MinX + piece.MaxX + 1) / 2f, (piece.MinY + piece.MaxY + 1) / 2f) * TileModels.Side;
        var half = new Vector2(width, height) * (TileModels.Side / 2f);
        var corner = new Vector2(x, y) * TileModels.Side;
        int away = 0;
        foreach (RoomDoodad line in doodads.Lines)
        {
            if (!TileDoodadList.Shown(line, placed))
            {
                away++;
                continue;
            }

            Vector2 local = (line.Exact ?? (new Vector2(line.X, line.Y) * RoomModels.CellSize)) - half;
            Vector2 world = middle + new Vector2((xx * local.X) + (xy * local.Y), (yx * local.X) + (yy * local.Y));
            float ground = grid.HeightAt((int)(world.X / RoomModels.CellSize), (int)(world.Y / RoomModels.CellSize));
            float z = heights switch
            {
                DoodadHeight.File => lift + (line.Height ?? 0f),
                DoodadHeight.Added => ground + (line.Height ?? 0f),
                _ => ground,
            };

            Vector2 inRoom = world - corner;
            into.Add((line, Matrix4x4.CreateScale(line.Scale > 0f ? line.Scale : 1f)
                * line.Turning
                * turn
                * Matrix4x4.CreateTranslation(inRoom.X, inRoom.Y, z)));
        }

        return away;
    }

    /// <summary>
    /// What takes a room's doodad, as RoomModels.Lay has turned and placed it in the room, through the room's laying: the laying
    /// whole, a mirror included.
    /// </summary>
    /// <remarks>
    /// A MIRRORED ROOM'S DOODAD IS MIRRORED, MEASURED on Port's boss room laid as written, mirrored (v0.1.265's capture): the game
    /// holds every doodad entity of it at minus its line's angle (Render.RotationCurrent, 187 of 187 matched to their lines) and
    /// carries no mirror beside the angle in Render - yet its ship, boss_boat.ao at angle nought, lays its gangway on the quay the
    /// mirror sends to the other side. The gangway is the ship's own mesh, neither the room's lines nor the harbour tile's name
    /// another, so the game draws the ship's mirror image: the angle the game holds is only the turning half of the laying. Turned
    /// to that angle and not mirrored (v0.1.265, TurnedThrough), the ship put its gangway into the water (said from the live
    /// client). Neither drawing culls by winding, so a mirrored mesh draws whole, as a mirrored tile piece always has.
    /// </remarks>
    internal static Matrix4x4 Through(Matrix3x2 laying)
        => Spatial(Matrix3x2.CreateScale(1f / TileModels.Side) * laying * Matrix3x2.CreateScale(TileModels.Side));

    /// <summary>
    /// Where the room names its boss's place, in the picture's frame - the room's corner its origin, as every piece and doodad has
    /// it - or null where it names none.
    /// </summary>
    /// <remarks>
    /// MEASURED (boss-points-replay.txt): the game stands a map's boss on the point of interest its room tags for him - Marrow's
    /// "force:mapboss", Steaming Springs' "mapboss", Sulphuric Caverns' "mapboss2" each 8 units from the boss the recording holds,
    /// which is the distance from a cell's corner to its middle; so the point is taken at its cell's middle. A room naming
    /// several - Sulphuric's boss room names a "mapboss1" and a "mapboss2", one a boss each - is taken by its first: "mapboss",
    /// then "force:mapboss", then the lowest number; the others are counted, for the line under the picture.
    ///
    /// TURNED, NOT MIRRORED: the point's angle read as a doodad line's (the room pictures turn their doodads by it and draw
    /// right), through the room's laying as a direction, so a mirrored room turns its boss to face the way the mirror sends him -
    /// the game holds a mirrored room's doodads at that angle (<see cref="Through"/>) - while his own model, the server's monster
    /// and no piece of the room, stays the right way round.
    /// </remarks>
    internal static RoomSpot? BossSpotOf(RoomLayout room, Matrix3x2 laying)
    {
        RoomPoint[] marks = [.. room.Points.Where(point => Rank(point.Tag) >= 0).OrderBy(point => Rank(point.Tag))];
        if (marks.Length == 0)
        {
            return null;
        }

        RoomPoint mark = marks[0];
        Vector2 at = Vector2.Transform(new Vector2(mark.X + 0.5f, mark.Y + 0.5f) / RoomModels.CellsPerTile, laying) * TileModels.Side;
        (float sin, float cos) = MathF.SinCos(mark.Turn);
        Vector2 facing = Vector2.TransformNormal(new Vector2(sin, -cos), laying);
        return new RoomSpot(at, MathF.Atan2(facing.X, -facing.Y), mark.Tag, marks.Length - 1);

        // "mapboss" first, "force:mapboss" next, then "mapboss1", "mapboss2" by their number; anything else is no boss's place.
        static int Rank(string tag)
        {
            if (tag.Equals("mapboss", StringComparison.OrdinalIgnoreCase))
            {
                return 0;
            }

            if (tag.Equals("force:mapboss", StringComparison.OrdinalIgnoreCase))
            {
                return 1;
            }

            return tag.StartsWith("mapboss", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(tag.AsSpan(7), NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number < 1000
                ? 2 + number
                : -1;
        }
    }

    /// <summary>Whether a piece laid this way - TileOrientation's placement numbers - is laid mirrored.</summary>
    private static bool Mirrors(int placement)
    {
        TileOrientation placed = TileOrientation.OfPlacement(placement);
        if (!placed.IsPlacement)
        {
            return false;
        }

        (int xx, int xy, int yx, int yy) = placed.Turn;
        return (xx * yy) - (xy * yx) < 0;
    }

    /// <summary>A flat placement as a placement in space, the height left alone.</summary>
    private static Matrix4x4 Spatial(Matrix3x2 flat) => new(
        flat.M11, flat.M12, 0f, 0f,
        flat.M21, flat.M22, 0f, 0f,
        0f, 0f, 1f, 0f,
        flat.M31, flat.M32, 0f, 1f);

    /// <summary>One tile file, made once: its props, its ground and where to ask it a height, its size in tiles, and its box in its own frame - the ground's, or the props' where it has none.</summary>
    private sealed record Made(MonsterModel Props, SkinnedMesh Ground, GroundLookup? Lookup, int Width, int Height, Vector2 Least, Vector2 Most)
    {
        /// <summary>The middle of the box, which a piece is turned about.</summary>
        public Vector2 Middle => (Least + Most) / 2f;

        /// <summary>The doodads the tile brings with it - see TileModels.DoodadsOf.</summary>
        public TileDoodads Doodads { get; init; } = TileDoodads.None;

        /// <summary>Which sub-tile each run of the ground block's triangles is - see GroundPiece.</summary>
        public IReadOnlyList<GroundPiece> Pieces { get; init; } = [];

        /// <summary>Each of <see cref="Pieces"/>' four corners' ground types by name, '-' unnamed, from the definition's grid - empty where it carries none.</summary>
        public IReadOnlyList<string[]> Corners { get; init; } = [];

        public static Made Of(MonsterModel props, SkinnedMesh ground, int across, int down, TileDoodads doodads)
        {
            (Vector3 least, Vector3 most) = ground.Ready
                ? (ground.Least, ground.Most)
                : props.Ready ? (props.Mesh.Least, props.Mesh.Most) : (Vector3.Zero, Vector3.Zero);
            return new Made(
                props,
                ground,
                ground.Ready ? new GroundLookup(ground) : null,
                Math.Max(1, across),
                Math.Max(1, down),
                new Vector2(least.X, least.Y),
                new Vector2(most.X, most.Y))
            {
                Doodads = doodads,
            };
        }
    }

    /// <summary>
    /// One way a file can lie on the template the game's turn is read through, and how well the sloped pieces fit it.
    /// </summary>
    private sealed class Way
    {
        private readonly Matrix3x2 _lies;

        /// <param name="relation">The way, numbered as TileOrientation numbers its placements.</param>
        public Way(int relation)
        {
            TileOrientation lies = TileOrientation.OfPlacement(relation);
            _lies = TileModels.Turned(lies) is { } turned
                ? new Matrix3x2(turned.M11, turned.M12, turned.M21, turned.M22, 0f, 0f)
                : Matrix3x2.Identity;
            (int xx, int xy, int yx, int yy) = lies.Turn;
            Said = (lies.Degrees == 0 && !lies.Mirrored ? "as filed" : lies.ToString()) + " (" + Axis(xx, xy) + ", " + Axis(yx, yy) + ")";
        }

        /// <summary>This way in words, and where the file's x and y go: "mirrored, turned 180° (x, -y)".</summary>
        public string Said { get; }

        /// <summary>Sloped pieces weighed.</summary>
        public int Sloped { get; private set; }

        /// <summary>Of those, the ones whose ground met the area's within <see cref="Fits"/>.</summary>
        public int Fitting { get; private set; }

        /// <summary>The squared misses over every sloped piece, after each was raised to fit as well as it can.</summary>
        public double Residual { get; private set; }

        /// <summary>Sub-tiles whose four grid corners this way lays on the game's own corner types - see <see cref="CornersMeet"/>.</summary>
        public int Met { get; private set; }

        /// <summary>Asks the game's corner types of one piece's sub-tiles laid this way.</summary>
        public void Meet(TilePiece piece, Made one, TerrainGroundTypes types, string[] names, int x, int y)
        {
            Matrix4x4 place = Spatial(Flat(piece, one, x, y));
            for (var at = 0; at < one.Corners.Count && at < one.Pieces.Count; at++)
            {
                Met += CornersMeet(place, x, y, one.Pieces[at], one.Corners[at], types, names) ? 1 : 0;
            }
        }

        /// <summary>
        /// Where a piece's file goes, flat: its middle to the origin, laid on the template this way, the game's turn, then out to the middle of the area tiles it covers - from the room's corner, in world units.
        /// </summary>
        public Matrix3x2 Flat(TilePiece piece, Made one, int x, int y)
        {
            Matrix3x2 flat = Matrix3x2.CreateTranslation(-one.Middle) * _lies;
            if (TileModels.Turned(TileOrientation.OfPlacement(piece.Placement)) is { } turned)
            {
                flat *= new Matrix3x2(turned.M11, turned.M12, turned.M21, turned.M22, 0f, 0f);
            }

            var middle = new Vector2(
                (((piece.MinX + piece.MaxX + 1) / 2f) - x) * TileModels.Side,
                (((piece.MinY + piece.MaxY + 1) / 2f) - y) * TileModels.Side);
            return flat * Matrix3x2.CreateTranslation(middle);
        }

        /// <summary>
        /// The lift that meets a piece's ground with the area's on average, and how far the ground misses the area's fitted that way and set at <paramref name="level"/> - one pass over the same points.
        /// </summary>
        public static Height Measure(TilePiece piece, Made one, TerrainGrid grid, int x, int y, Matrix3x2 flat, float level)
        {
            int middleX = ((piece.MinX + piece.MaxX + 1) * RoomModels.CellsPerTile) / 2;
            int middleY = ((piece.MinY + piece.MaxY + 1) * RoomModels.CellsPerTile) / 2;
            if (one.Lookup is null || !Matrix3x2.Invert(flat, out Matrix3x2 back))
            {
                return new Height(grid.HeightAt(middleX, middleY), 0, double.NaN, double.NaN);
            }

            double sum = 0;
            double squares = 0;
            double atLevel = 0;
            int count = 0;
            foreach ((float file, float area) in Samples(piece, one.Lookup, grid, x, y, back))
            {
                double miss = area - file;
                sum += miss;
                squares += miss * miss;
                atLevel += (miss - level) * (miss - level);
                count++;
            }

            if (count == 0)
            {
                return new Height(grid.HeightAt(middleX, middleY), 0, double.NaN, double.NaN);
            }

            double fitted = sum / count;
            return new Height(
                (float)fitted,
                count,
                Math.Sqrt(Math.Max(0, (squares / count) - (fitted * fitted))),
                Math.Sqrt(atLevel / count));
        }

        /// <summary>Whether the area's own ground rises within a piece - asked of the area alone, before any mesh is.</summary>
        public static bool Rises(TilePiece piece, TerrainGrid grid)
        {
            float low = float.MaxValue;
            float high = float.MinValue;
            for (int tileY = piece.MinY; tileY <= piece.MaxY; tileY++)
            {
                for (int tileX = piece.MinX; tileX <= piece.MaxX; tileX++)
                {
                    foreach (int down in Asked)
                    {
                        foreach (int across in Asked)
                        {
                            float area = grid.HeightAt((tileX * RoomModels.CellsPerTile) + across, (tileY * RoomModels.CellsPerTile) + down);
                            low = Math.Min(low, area);
                            high = Math.Max(high, area);
                        }
                    }
                }
            }

            return high - low >= Rise;
        }

        /// <summary>Weighs one sloped piece against this way.</summary>
        public void Weigh(TilePiece piece, Made one, TerrainGrid grid, int x, int y)
        {
            if (one.Lookup is null || !Matrix3x2.Invert(Flat(piece, one, x, y), out Matrix3x2 back))
            {
                return;
            }

            double sum = 0;
            double squares = 0;
            int count = 0;
            foreach ((float file, float area) in Samples(piece, one.Lookup, grid, x, y, back))
            {
                double miss = area - file;
                sum += miss;
                squares += miss * miss;
                count++;
            }

            if (count < 3)
            {
                return;
            }

            // The squared misses about their mean, which is what is left once the piece is raised.
            double squared = Math.Max(0, squares - (sum * sum / count));
            Sloped++;
            Residual += squared;
            Fitting += Math.Sqrt(squared / count) <= Fits ? 1 : 0;
        }

        /// <summary>The file's ground height and the area's at the points asked, in every tile of the piece.</summary>
        private static IEnumerable<(float File, float Area)> Samples(TilePiece piece, GroundLookup ground, TerrainGrid grid, int x, int y, Matrix3x2 back)
        {
            float cell = TileModels.Side / RoomModels.CellsPerTile;
            for (int tileY = piece.MinY; tileY <= piece.MaxY; tileY++)
            {
                for (int tileX = piece.MinX; tileX <= piece.MaxX; tileX++)
                {
                    foreach (int down in Asked)
                    {
                        foreach (int across in Asked)
                        {
                            var world = new Vector2(
                                ((tileX - x) * TileModels.Side) + ((across + 0.5f) * cell),
                                ((tileY - y) * TileModels.Side) + ((down + 0.5f) * cell));
                            if (ground.HeightAt(Vector2.Transform(world, back)) is { } file)
                            {
                                yield return (file, grid.HeightAt((tileX * RoomModels.CellsPerTile) + across, (tileY * RoomModels.CellsPerTile) + down));
                            }
                        }
                    }
                }
            }
        }

        /// <summary>One file axis's place in words.</summary>
        private static string Axis(int onX, int onY) => (onX, onY) switch
        {
            (1, 0) => "x",
            (-1, 0) => "-x",
            (0, 1) => "y",
            (0, -1) => "-y",
            _ => "?",
        };
    }

    /// <summary>
    /// A ground's triangles filed by the squares they reach, so a height is asked of the few under a point rather than of every one.
    /// </summary>
    /// <remarks>
    /// EIGHT WAYS ASK EIGHT TIMES as many heights as one did, of every sloped piece and then of every
    /// piece laid, and a scan of the whole ground per point grew with the ground's size on top. Filed
    /// once per tile file, a point looks only at its own square's list, in the mesh's order - so the
    /// triangle that answers is the same first one the full scan found.
    /// </remarks>
    private sealed class GroundLookup
    {
        /// <summary>A square's side in world units: a cell and a half, about the size of a ground triangle.</summary>
        private const float Step = 16f;

        /// <summary>The most squares each way, whatever the ground's size.</summary>
        private const int MostSquares = 256;

        private readonly Vector3[] _points;
        private readonly int[] _indices;
        private readonly Vector2 _least;
        private readonly float _stepX;
        private readonly float _stepY;
        private readonly int _across;
        private readonly int _down;
        private readonly int[] _starts;
        private readonly int[] _filed;

        public GroundLookup(SkinnedMesh mesh)
        {
            _points = mesh.Positions;
            _indices = mesh.Indices;
            _least = new Vector2(mesh.Least.X, mesh.Least.Y);
            float wide = MathF.Max(mesh.Most.X - mesh.Least.X, 1e-3f);
            float tall = MathF.Max(mesh.Most.Y - mesh.Least.Y, 1e-3f);
            _across = Math.Clamp((int)MathF.Ceiling(wide / Step), 1, MostSquares);
            _down = Math.Clamp((int)MathF.Ceiling(tall / Step), 1, MostSquares);
            _stepX = wide / _across;
            _stepY = tall / _down;

            // Counted, summed, then filed: two passes and no list per square.
            _starts = new int[(_across * _down) + 1];
            for (var one = 0; one + 2 < _indices.Length; one += 3)
            {
                (int fromX, int fromY, int toX, int toY) = Reach(one);
                for (int squareY = fromY; squareY <= toY; squareY++)
                {
                    for (int squareX = fromX; squareX <= toX; squareX++)
                    {
                        _starts[(squareY * _across) + squareX + 1]++;
                    }
                }
            }

            for (var square = 1; square < _starts.Length; square++)
            {
                _starts[square] += _starts[square - 1];
            }

            _filed = new int[_starts[^1]];
            int[] next = _starts[..^1];
            for (var one = 0; one + 2 < _indices.Length; one += 3)
            {
                (int fromX, int fromY, int toX, int toY) = Reach(one);
                for (int squareY = fromY; squareY <= toY; squareY++)
                {
                    for (int squareX = fromX; squareX <= toX; squareX++)
                    {
                        _filed[next[(squareY * _across) + squareX]++] = one;
                    }
                }
            }
        }

        /// <summary>How high the ground is at a point, from the first triangle over it - or null where none is.</summary>
        public float? HeightAt(Vector2 at)
        {
            int squareX = Square(at.X - _least.X, _stepX, _across);
            int squareY = Square(at.Y - _least.Y, _stepY, _down);
            if (squareX < 0 || squareY < 0)
            {
                return null;
            }

            int square = (squareY * _across) + squareX;
            for (int filed = _starts[square]; filed < _starts[square + 1]; filed++)
            {
                int one = _filed[filed];
                Vector3 a = _points[_indices[one]];
                Vector3 b = _points[_indices[one + 1]];
                Vector3 c = _points[_indices[one + 2]];
                float d = ((b.Y - c.Y) * (a.X - c.X)) + ((c.X - b.X) * (a.Y - c.Y));
                if (MathF.Abs(d) < 1e-6f)
                {
                    continue;
                }

                float wa = (((b.Y - c.Y) * (at.X - c.X)) + ((c.X - b.X) * (at.Y - c.Y))) / d;
                float wb = (((c.Y - a.Y) * (at.X - c.X)) + ((a.X - c.X) * (at.Y - c.Y))) / d;
                float wc = 1f - wa - wb;
                if (wa >= -1e-4f && wb >= -1e-4f && wc >= -1e-4f)
                {
                    return (wa * a.Z) + (wb * b.Z) + (wc * c.Z);
                }
            }

            return null;
        }

        /// <summary>The squares a triangle's box reaches, a hair wider so a point on its edge finds it.</summary>
        private (int FromX, int FromY, int ToX, int ToY) Reach(int one)
        {
            Vector3 a = _points[_indices[one]];
            Vector3 b = _points[_indices[one + 1]];
            Vector3 c = _points[_indices[one + 2]];
            const float hair = 1e-2f;
            float leastX = MathF.Min(a.X, MathF.Min(b.X, c.X)) - _least.X - hair;
            float mostX = MathF.Max(a.X, MathF.Max(b.X, c.X)) - _least.X + hair;
            float leastY = MathF.Min(a.Y, MathF.Min(b.Y, c.Y)) - _least.Y - hair;
            float mostY = MathF.Max(a.Y, MathF.Max(b.Y, c.Y)) - _least.Y + hair;
            return (
                Math.Clamp((int)MathF.Floor(leastX / _stepX), 0, _across - 1),
                Math.Clamp((int)MathF.Floor(leastY / _stepY), 0, _down - 1),
                Math.Clamp((int)MathF.Floor(mostX / _stepX), 0, _across - 1),
                Math.Clamp((int)MathF.Floor(mostY / _stepY), 0, _down - 1));
        }

        /// <summary>Which square an offset falls in, the far edge counted in the last - or -1 outside the ground.</summary>
        private static int Square(float offset, float step, int count)
        {
            if (!float.IsFinite(offset) || offset < -1e-2f)
            {
                return -1;
            }

            int square = (int)MathF.Floor(offset / step);
            return square < count ? Math.Max(square, 0) : offset <= (count * step) + 1e-2f ? count - 1 : -1;
        }
    }

    /// <summary>
    /// What of a laid room is left out unseen: what the game's camera cannot see from anywhere the player can stand, and what lies further under the area's ground than the cut.
    /// </summary>
    /// <remarks>
    /// ASKED FOR FROM THE LIVE CLIENT, over Azmerian Ranges: a room there is large enough that drawing
    /// all of it takes thousands of doodads, and much of what was drawn lay under the ground the game
    /// shows - rock bodies, cliff feet, doodads sunk past the surface. None of it reaches the game's
    /// screen, and all of it cost loading, drawing, lighting and the sun's shadow map.
    ///
    /// IN TWO STEPS, the first before a doodad is read. The tiles are laid first, so the camera's
    /// views are drawn from their solid shapes alone and every doodad's place is asked against them -
    /// see RoomModels.Lay and CameraSight.BoxSeen - and a doodad file none of whose places can be seen
    /// is never loaded. Then the whole room, doodads and all, is drawn into the views again, every
    /// triangle asked, and the mesh rebuilt from what is seen - which is what the picture, its light
    /// and its shadows are then worked out from.
    ///
    /// THE MARGIN IS A TILE, because before a doodad is read nothing says how big it is: a place
    /// counts as seen where anything within a tile of it - times the doodad's scale - can be. A doodad
    /// standing further than that from its own origin, every place of it under ground the tiles show,
    /// is the one this can lose. A file that is read anyway has every place asked again with its own box.
    ///
    /// THE CUT COUNTS FROM THE AREA'S OWN GROUND under each vertex, the terrain heights a laid room is
    /// set on, and down is plus z. A triangle goes where all three corners lie further under it than
    /// the cut; a doodad before it is read where the top of its margin's box does, under the deepest
    /// ground at its box's corners and middle.
    ///
    /// A SHADOW-ONLY CASTER IS KEPT UNLESS IT IS UNDER THE CUT: it is never seen, and casting where
    /// it is not seen is what it is for. Anything else hidden from every view casts no shadow here
    /// any more - something the camera can never see, that the sun would throw a shadow from onto
    /// ground the camera does see, is the one shadow this can lose; the switch puts it back.
    /// </remarks>
    private sealed class Hiding
    {
        /// <summary>How far round a doodad's place the box reaches before the doodad is read, in world units, times its scale.</summary>
        private const float Margin = TileModels.Side;

        private readonly TerrainGrid _grid;
        private readonly Vector2 _origin;
        private readonly CameraSight? _sight;
        private readonly bool _asked;
        private readonly int _cut;
        private readonly string _why;
        private readonly ModelProgress? _progress;
        private long _took;
        private int _total;
        private int _hidden;
        private int _below;

        private Hiding(TerrainGrid grid, Vector2 origin, CameraSight? sight, bool asked, int cut, string why, long took, ModelProgress? progress)
        {
            _progress = progress;
            _grid = grid;
            _origin = origin;
            _sight = sight;
            _asked = asked;
            _cut = Math.Max(0, cut);
            _why = why;
            _took = took;
            Sieve = sight is null && _cut == 0 ? null : new DoodadSieve(Kept, Margin);
        }

        /// <summary>What asks each doodad before it is laid, or null where nothing is left out.</summary>
        public DoodadSieve? Sieve { get; }

        /// <summary>
        /// The camera's views drawn from what the pile holds so far - the tiles - or nothing where neither is asked for.
        /// </summary>
        /// <param name="pile">The tiles laid.</param>
        /// <param name="grid">The area.</param>
        /// <param name="origin">Where the room's corner lies in the world.</param>
        /// <param name="size">How far the room reaches from it, x and y.</param>
        /// <param name="camera">The game's camera, or null.</param>
        /// <param name="sight">Whether what it cannot see is left out.</param>
        /// <param name="cut">How far under the ground everything is left out; nought for nothing.</param>
        /// <param name="progress">Where the build says how far it has got, or null.</param>
        public static Hiding Of(
            ModelPile pile, TerrainGrid grid, Vector2 origin, Vector2 size, CameraShot? camera, bool sight, int cut, ModelProgress? progress)
        {
            if (!sight)
            {
                return new Hiding(grid, origin, null, asked: false, cut, string.Empty, 0, progress);
            }

            using ModelProgress.Step step = ModelProgress.Begin(progress, "finding what the game's camera sees", 1);
            long started = Environment.TickCount64;
            CameraSight? seeing = CameraSight.Over(camera, grid, origin, Vector3.Zero, new Vector3(size, 0f), out string why);
            if (seeing is not null)
            {
                (Vector3[] places, int[] indices) = pile.Solid();
                seeing.See(places, indices, Environment.ProcessorCount);
            }

            step.Advance();
            return new Hiding(grid, origin, seeing, asked: true, cut, why, Environment.TickCount64 - started, progress);
        }

        /// <summary>
        /// The room's mesh with only what is kept - see the remarks - and the counts for the line under the picture.
        /// </summary>
        public SkinnedMesh Keep(SkinnedMesh joined, ModelPile pile)
        {
            if (!joined.Ready || (_sight is null && _cut == 0))
            {
                return joined;
            }

            using ModelProgress.Step step = ModelProgress.Begin(_progress, _sight is null ? "cutting under the ground" : "leaving out what the game's camera cannot see", 1);
            long started = Environment.TickCount64;
            int triangles = joined.Triangles;
            MaterialBlend[] blends = pile.Blends(joined);
            bool[]? below = _cut > 0 ? Below(joined) : null;
            var hides = new bool[triangles];
            var kept = new bool[triangles];
            for (var t = 0; t < triangles; t++)
            {
                bool gone = below is not null && below[t];
                hides[t] = !gone && blends[t] == MaterialBlend.Opaque;
                kept[t] = !gone && (blends[t] == MaterialBlend.ShadowOnly || _sight is null);
            }

            bool[] keep = _sight is null ? kept : _sight.Seen(joined.Positions, joined.Indices, hides, kept, Environment.ProcessorCount);
            int left = 0, under = 0;
            for (var t = 0; t < triangles; t++)
            {
                if (below is not null && below[t])
                {
                    keep[t] = false;
                    under++;
                }
                else if (!keep[t])
                {
                    left++;
                }
            }

            _total = triangles;
            _hidden = left;
            _below = under;
            _took += Environment.TickCount64 - started;
            step.Advance();
            return joined.Keeping(keep);
        }

        /// <summary>The lines under the picture: how much was left out, and why nothing was where it was asked for and could not be.</summary>
        public IEnumerable<string> Said()
        {
            if (_asked)
            {
                yield return _sight is null
                    ? $"nothing hidden left out: {_why}"
                    : string.Create(CultureInfo.InvariantCulture,
                        $"hidden from the game's camera: {_hidden} of {_total} triangles left out - its view from {_sight.Views} places the player can stand within its reach, {_sight.Step:0} units apart; {_took / 1000.0:0.0} s");
            }

            if (_cut > 0)
            {
                yield return string.Create(CultureInfo.InvariantCulture,
                    $"{_below} triangles further than {_cut} units under the area's ground left out");
            }
        }

        /// <summary>Whether anything in a box may be seen and lies above the cut.</summary>
        private bool Kept(Vector3 least, Vector3 most)
        {
            if (_cut > 0)
            {
                // DOWN IS PLUS Z, so the box's top is its least z, and the deepest ground its most.
                float deepest = MathF.Max(
                    MathF.Max(Ground(least.X, least.Y), Ground(most.X, least.Y)),
                    MathF.Max(MathF.Max(Ground(least.X, most.Y), Ground(most.X, most.Y)), Ground((least.X + most.X) * 0.5f, (least.Y + most.Y) * 0.5f)));
                if (least.Z > deepest + _cut)
                {
                    return false;
                }
            }

            return _sight is null || _sight.BoxSeen(least, most);
        }

        /// <summary>Per triangle, whether all three corners lie further under the area's ground than the cut.</summary>
        private bool[] Below(SkinnedMesh mesh)
        {
            Vector3[] places = mesh.Positions;
            var under = new bool[places.Length];
            Parallel.For(0, places.Length, one =>
            {
                Vector3 place = places[one];
                under[one] = place.Z > Ground(place.X, place.Y) + _cut;
            });

            int[] indices = mesh.Indices;
            var below = new bool[mesh.Triangles];
            for (var t = 0; t < below.Length; t++)
            {
                below[t] = under[indices[t * 3]] && under[indices[(t * 3) + 1]] && under[indices[(t * 3) + 2]];
            }

            return below;
        }

        /// <summary>The area's ground under a point of the room's own frame.</summary>
        private float Ground(float x, float y)
            => _grid.HeightAt((int)MathF.Floor((x + _origin.X) / RoomModels.CellSize), (int)MathF.Floor((y + _origin.Y) / RoomModels.CellSize));
    }
}

/// <summary>One whole piece's ground as a laid room lays it, in world units - see LaidRoomModels.GroundsOver.</summary>
/// <param name="Path">The piece's tile file.</param>
/// <param name="Piece">Where the game laid it and which way round.</param>
/// <param name="Ground">Its ground block, in the template's frame, its coordinates the mask's.</param>
/// <param name="Pieces">Which sub-tile each run of the block's triangles is.</param>
/// <param name="Place">The template's frame to the world.</param>
internal readonly record struct LaidGround(string Path, TilePiece Piece, SkinnedMesh Ground, IReadOnlyList<GroundPiece> Pieces, Matrix4x4 Place);
