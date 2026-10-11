using System.Globalization;
using System.IO.Compression;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.Ui;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>A room standing near the player, and how many tiles off it is - nought for the room the player stands in.</summary>
/// <param name="Room">The room's file.</param>
/// <param name="Layout">The room as read, for the tiles it covers.</param>
/// <param name="Where">Where it stands.</param>
/// <param name="Tiles">Tiles from the player's tile to the nearest the room covers, counted the long way round a diagonal.</param>
public sealed record RoomNearby(string Room, RoomLayout Layout, RoomCandidate Where, int Tiles);

/// <summary>
/// The text half of the capture key: what one press writes into its folder beside the two pictures and the recording.
/// </summary>
/// <remarks>
/// WHAT A CAPTURE IS FOR. The lighting work - and most work on this tool - needs the same handful
/// of things from one spot in the game: a picture of it, the files the rooms there are built from,
/// and what memory held. Asked for one at a time they arrive from different spots and different
/// builds; one key takes them together, from one place, at one moment.
///
/// THE ROOMS ARE THE ONES ON SCREEN, give or take: every room laid within
/// <see cref="RoomReach"/> tiles of the player's tile. A tile is 250 world units and the camera
/// sees a few of them either side, so that is what the pictures show - and each room's lights
/// are written so a lamp in the picture can be found in a file.
/// </remarks>
public static class CaptureReport
{
    /// <summary>Tiles either side of the player's own a room may be and still be written.</summary>
    public const int RoomReach = 3;

    /// <summary>Ticks of the reader the memory recording runs for at least - three seconds at its thirty a second; longer while the pass inside it runs (CaptureMemory).</summary>
    public const int MemoryFrames = 90;

    /// <summary>The game alone, the overlay hidden for the shot.</summary>
    public const string GameShot = "game.png";

    /// <summary>The game with the overlay over it, as it was when the key went down.</summary>
    public const string OverlayShot = "overlay.png";

    /// <summary>What was captured, where, and what could not be.</summary>
    public const string SummaryFile = "capture.txt";

    /// <summary>Every entity the snapshot held, nearest first.</summary>
    public const string EntitiesFile = "entities.txt";

    /// <summary>Every monster with its mods, buffs and targetable byte, and the marked places beside them - written by the pass inside the recording. See CaptureMonsters.</summary>
    public const string MonstersFile = "monsters.txt";

    /// <summary>The files the area loaded.</summary>
    public const string LoadedFile = "loaded-files.txt";

    /// <summary>The environments of the rooms around the player, whole.</summary>
    public const string EnvironmentsFile = "environments.txt";

    /// <summary>The rooms around the player, each with its doodads' lights.</summary>
    public const string RoomsFile = "rooms.txt";

    /// <summary>The overlay's own reads for a few seconds and one pass reading what the ticked parts ask for - see CaptureMemory. Replayable with --replay.</summary>
    public const string MemoryFile = "memory.rec";

    /// <summary>Every room arranged by its ground and tiles - the Tile Book's line and hover.</summary>
    public const string ArrangedFile = "rooms-arranged.txt";

    /// <summary>The Tile Book's picked row against the rooms on the map, and where it parts with the area.</summary>
    public const string PickFile = "room-pick.txt";

    /// <summary>Every room file the area loaded, whole.</summary>
    public const string RoomFilesFile = "room-files.txt";

    /// <summary>Every tile file the area laid, as a room's slot asks about it.</summary>
    public const string TileIdentitiesFile = "tile-identities.txt";

    /// <summary>Every room of the area's set, each kind of its slots against the tiles the area's tilesets list - and those tiles' identities.</summary>
    public const string SlotCandidatesFile = "slot-candidates.txt";

    /// <summary>The area's tile lists whole, includes followed - the weights and the words beside each tile.</summary>
    public const string TilesetListsFile = "tileset-lists.txt";

    /// <summary>Every listed or laid tile file's definition chain and its doodad list - see <see cref="TileFiles"/>.</summary>
    public const string TileFilesFile = "tile-files.txt";

    /// <summary>The area's masters whole and every file they name - see <see cref="Masters"/>.</summary>
    public const string MastersFile = "area-masters.txt";

    /// <summary>The survey of the entity maps for the rooms' doodads, and every sighting.</summary>
    public const string DoodadsFile = "doodads.txt";

    /// <summary>Every room placed by its doodads - the Tile Book's line and hover.</summary>
    public const string PlacedFile = "rooms-placed.txt";

    /// <summary>The active routes, as the Routes page lists them.</summary>
    public const string RoutesFile = "routes.txt";

    /// <summary>The light hunt's last report.</summary>
    public const string SceneLightFile = "scene-light.txt";

    /// <summary>The model panes' probe lines.</summary>
    public const string ProbeFile = "model-probe.txt";

    /// <summary>The Memory Dissector's places, route and last search.</summary>
    public const string DissectorFile = "dissector.txt";

    /// <summary>The atlas check's report.</summary>
    public const string AtlasLogFile = "atlas-log.txt";

    /// <summary>Where captures go: beside the tool, like its exports.</summary>
    public static string Root => Path.Combine(AppContext.BaseDirectory, "captures");

    /// <summary>A capture's folder name: when, and where - sorting by name sorts by time.</summary>
    public static string FolderName(DateTime local, AreaInfo area)
    {
        string where = Clean(area.Name.Length > 0 ? area.Name : area.Id);
        string when = local.ToString("yyyy-MM-dd HH-mm-ss", CultureInfo.InvariantCulture);
        return where.Length > 0 ? $"{when} {where}" : when;
    }

    /// <summary>
    /// A name made safe for a folder on Windows, wherever this runs.
    /// </summary>
    /// <remarks>
    /// The set is written out rather than asked of Path.GetInvalidFileNameChars, which answers for
    /// the machine running it: on Linux that is the slash and nothing else, and the tests run there.
    /// </remarks>
    public static string Clean(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        var cleaned = new StringBuilder(name.Length);
        foreach (char one in name)
        {
            cleaned.Append(one is '<' or '>' or ':' or '"' or '/' or '\\' or '|' or '?' or '*' || char.IsControl(one) ? '_' : one);
        }

        return cleaned.ToString().Trim().TrimEnd('.');
    }

    /// <summary>The grid cell and tile a world position falls on.</summary>
    public static (int CellX, int CellY, int TileX, int TileY) Where(float worldX, float worldY)
    {
        int cellX = (int)MathF.Floor(worldX / MapView.WorldToGrid);
        int cellY = (int)MathF.Floor(worldY / MapView.WorldToGrid);
        return (cellX, cellY, FloorDiv(cellX, TerrainGrid.CellsPerTile), FloorDiv(cellY, TerrainGrid.CellsPerTile));
    }

    private static int FloorDiv(int value, int by) => (value - (((value % by) + by) % by)) / by;

    /// <summary>
    /// Every room standing within <paramref name="reach"/> tiles of a tile, nearest first - the rooms the player stands in at nought.
    /// </summary>
    /// <remarks>
    /// By the tiles each room COVERS (RoomArrangement.Tiles), not its footprint's rectangle: an
    /// L-shaped room's notch is somebody else's floor, and standing there is not standing in it.
    /// Two rooms at nought is an ordinary answer where they share a rim, or where one stands inside
    /// another - The Assembly's Expedition encounter inside a workshop.
    /// </remarks>
    /// <param name="standing">Every room with where it stands - every place of a room laid more than once. See AreaRooms.Standing.</param>
    /// <param name="tilesX">The area's tiles across.</param>
    /// <param name="tilesY">The area's tiles down.</param>
    /// <param name="tileX">The player's tile.</param>
    /// <param name="tileY">The player's tile.</param>
    /// <param name="reach">Tiles either side of the player's own a room may be and still be listed.</param>
    public static List<RoomNearby> RoomsNear(
        IReadOnlyList<(string Room, RoomLayout Layout, RoomCandidate Where)> standing, int tilesX, int tilesY, int tileX, int tileY, int reach = RoomReach)
    {
        ArgumentNullException.ThrowIfNull(standing);
        var near = new List<RoomNearby>();
        foreach ((string room, RoomLayout layout, RoomCandidate where) in standing)
        {
            // The footprint first: a room whose rectangle is out of reach covers nothing in it.
            if (Gap(tileX, where.X, where.X + where.Width - 1) > reach || Gap(tileY, where.Y, where.Y + where.Height - 1) > reach)
            {
                continue;
            }

            int nearest = int.MaxValue;
            foreach (int tile in RoomArrangement.Tiles(layout, where, tilesX, tilesY))
            {
                int off = Math.Max(Math.Abs((tile % tilesX) - tileX), Math.Abs((tile / tilesX) - tileY));
                nearest = Math.Min(nearest, off);
            }

            if (nearest <= reach)
            {
                near.Add(new RoomNearby(room, layout, where, nearest));
            }
        }

        near.Sort((a, b) =>
        {
            int order = a.Tiles.CompareTo(b.Tiles);
            order = order != 0 ? order : string.CompareOrdinal(a.Room, b.Room);
            order = order != 0 ? order : a.Where.Y.CompareTo(b.Where.Y);
            return order != 0 ? order : a.Where.X.CompareTo(b.Where.X);
        });
        return near;
    }

    private static int Gap(int at, int from, int to) => at < from ? from - at : at > to ? at - to : 0;

    /// <summary>
    /// The summary: when, which build, where the player stood, the camera, and a line for every part of the capture saying what it holds or why it does not.
    /// </summary>
    /// <param name="snapshot">The world as it was when the key went down.</param>
    /// <param name="local">When the key went down.</param>
    /// <param name="version">The version and build, as the overlay's title bar says them - see ToolVersion.With.</param>
    /// <param name="client">The game's client area on the screen - what the pictures cover.</param>
    /// <param name="rooms">The rooms around the player, or null where none were arranged.</param>
    /// <param name="parts">One line per part of the capture.</param>
    public static string Summary(
        WorldSnapshot snapshot,
        DateTime local,
        string version,
        (int X, int Y, int Width, int Height) client,
        IReadOnlyList<RoomNearby>? rooms,
        IEnumerable<string> parts)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(parts);
        var said = new StringBuilder();
        said.Append("capture  ").Append(local.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture))
            .Append(" local, PoEformance ").AppendLine(string.IsNullOrEmpty(version) ? ToolVersion.Said : version);
        AreaInfo area = snapshot.Area;
        said.Append("area     ").Append(area.Name.Length > 0 ? area.Name : "(unnamed)").Append(" (").Append(area.Id).Append(')')
            .Append(", act ").Append(Say(area.Act)).Append(", level ").Append(Say(snapshot.AreaLevel))
            .Append(", hash 0x").Append(snapshot.AreaHash.ToString("X8", CultureInfo.InvariantCulture))
            .Append(", state ").AppendLine(snapshot.State.ToString());
        said.Append("light    ").AppendLine(area.Environment.Length > 0 ? area.Environment : "(the area's environment did not resolve)");
        said.Append("screen   game client at ").Append(Say(client.X)).Append(", ").Append(Say(client.Y))
            .Append(", ").Append(Say(client.Width)).Append(" x ").AppendLine(Say(client.Height));

        if (snapshot.Player is { } player)
        {
            (int cellX, int cellY, int tileX, int tileY) = Where(player.WorldX, player.WorldY);
            said.Append("player   world ").Append(Num(player.WorldX)).Append(' ').Append(Num(player.WorldY)).Append(' ').Append(Num(player.WorldZ))
                .Append(", terrain height ").Append(Num(player.TerrainHeight))
                .Append(", level ").AppendLine(Say(snapshot.PlayerLevel));
            said.Append("         grid cell ").Append(Say(cellX)).Append(", ").Append(Say(cellY))
                .Append(", tile ").Append(Say(tileX)).Append(", ").AppendLine(Say(tileY));

            if (snapshot.Terrain is TerrainGrid grid)
            {
                said.Append("         the grid's height there ").Append(Num(grid.HeightAt(cellX, cellY)))
                    .Append(", the tile's level ").AppendLine(Num(grid.LevelAt(tileX, tileY)));
                if (grid.Tiles is { } tiles)
                {
                    (int subX, int subY) = tiles.SubAt(tileX, tileY);
                    string path = tiles.PathAt(tileX, tileY);
                    said.Append("tile     ").Append(path.Length > 0 ? path : "(none named)")
                        .Append(", laid ").Append(Say(tiles.PlacementAt(tileX, tileY)))
                        .Append(", piece ").Append(Say(subX)).Append(", ").AppendLine(Say(subY));
                }
            }
            else
            {
                said.AppendLine("tile     (no terrain read)");
            }
        }
        else
        {
            said.AppendLine("player   (not read)");
        }

        said.Append("camera  ");
        foreach (float one in snapshot.Matrix)
        {
            said.Append(' ').Append(Num(one));
        }

        said.AppendLine();
        if (snapshot.Terrain is TerrainGrid terrain)
        {
            // The grid's size, so a placing can be run again from the capture on the same grid.
            said.Append("terrain  ").Append(Say(terrain.Width)).Append(" x ").Append(Say(terrain.Height)).Append(" cells, ")
                .Append(Say(terrain.TilesX)).Append(" x ").Append(Say(terrain.TilesY)).AppendLine(" tiles");
        }

        said.AppendLine();
        said.AppendLine("=== rooms around the player");
        if (rooms is null)
        {
            said.AppendLine("(none arranged)");
        }
        else if (rooms.Count == 0)
        {
            said.Append("(none laid within ").Append(Say(RoomReach)).AppendLine(" tiles)");
        }
        else
        {
            foreach (RoomNearby room in rooms)
            {
                said.Append("  ").Append(Room(room)).AppendLine();
            }
        }

        said.AppendLine();
        said.AppendLine("=== what this capture holds");
        foreach (string part in parts)
        {
            said.Append("  ").AppendLine(part);
        }

        return said.ToString();
    }

    /// <summary>One room around the player, in a line.</summary>
    public static string Room(RoomNearby room)
    {
        ArgumentNullException.ThrowIfNull(room);
        RoomCandidate where = room.Where;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"{(room.Tiles == 0 ? "here " : $"{room.Tiles} off")}  {TerrainRooms.NameFor(room.Room)}  at tile {where.X}, {where.Y}, {where.Width} x {where.Height}, {RoomFinder.Said(where.Turn)}  ({room.Room})");
    }

    /// <summary>
    /// Every entity in the snapshot, nearest the player first: where it stands, what it is, and where in memory it was.
    /// </summary>
    /// <remarks>
    /// THE MEMORY SIDE OF A ROOM'S FILES. A doodad that is also an entity - a pot, a lamp, a door -
    /// stands at the height the game gave it, which is what the .arm's float was settled against;
    /// a light in a doodad's rig and a lamp entity at the same spot are the same lamp.
    /// </remarks>
    public static string Entities(WorldSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        float x = snapshot.Player?.WorldX ?? 0f;
        float y = snapshot.Player?.WorldY ?? 0f;
        var said = new StringBuilder();
        said.Append(Say(snapshot.Entities.Count)).Append(" entities, ").Append(Say(snapshot.Remembered))
            .AppendLine(" of them remembered rather than listed this read; distance and position in world units");
        said.AppendLine("distance\tkind\tid\taddress\tx\ty\tz\tterrain\tbounds z\tremembered\tpath\tname");
        foreach (WorldEntity one in snapshot.Entities.OrderBy(one => Distance(one, x, y)))
        {
            said.Append(Num(Distance(one, x, y))).Append('\t').Append(one.Kind).Append('\t').Append(one.Id.ToString(CultureInfo.InvariantCulture))
                .Append("\t0x").Append(one.Address.ToString("X", CultureInfo.InvariantCulture))
                .Append('\t').Append(Num(one.WorldX)).Append('\t').Append(Num(one.WorldY)).Append('\t').Append(Num(one.WorldZ))
                .Append('\t').Append(Num(one.TerrainHeight)).Append('\t').Append(Num(one.ModelBoundsZ))
                .Append('\t').Append(one.IsRemembered ? "yes" : "no")
                .Append('\t').Append(one.Path).Append('\t').AppendLine(one.Name);
        }

        return said.ToString();
    }

    private static float Distance(WorldEntity one, float x, float y) => MathF.Sqrt(((one.WorldX - x) * (one.WorldX - x)) + ((one.WorldY - y) * (one.WorldY - y)));

    /// <summary>
    /// The rooms around the player, each with its place and its doodads' lights - and, for the rooms the player stands in, the room's file whole, which says where each doodad stands.
    /// </summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="rooms">The rooms, nearest first.</param>
    public static string Rooms(Func<string, byte[]?> read, IReadOnlyList<RoomNearby> rooms)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(rooms);
        var said = new StringBuilder();
        foreach (RoomNearby room in rooms)
        {
            said.Append("##### ").AppendLine(Room(room));
            said.Append(ModelDump.DoodadLights(read, room.Room));
            if (room.Tiles == 0)
            {
                said.Append("=== ").Append(room.Room).AppendLine(" as the file has it");
                byte[]? file = read(room.Room);
                said.AppendLine(file is { Length: > 0 } ? StatDescriptionFiles.Decode(file).TrimEnd() : "(not in the install)");
            }

            said.AppendLine();
        }

        return said.ToString();
    }

    /// <summary>
    /// Every file of a capture packed into one zip beside its folder, named like it - what gets sent. Returns the zip's path and the files left out.
    /// </summary>
    /// <remarks>
    /// ASKED FOR because a capture is a dozen files or more and the place they are sent to takes five.
    ///
    /// THE PICTURES AND THE RECORDING ARE STORED, NOT COMPRESSED: a PNG and a Brotli stream are
    /// compressed already, and squeezing them again is seconds of work for nothing. The text files
    /// are compressed, and they are where it pays - the loaded-file list shrinks to a tenth.
    ///
    /// A file that cannot be opened is left out and named, not allowed to sink the rest: the only
    /// one that can be is a recording still being written, and the folder keeps it either way.
    /// </remarks>
    /// <param name="folder">The capture's folder.</param>
    public static (string Zip, List<string> LeftOut) Pack(string folder)
    {
        ArgumentException.ThrowIfNullOrEmpty(folder);
        string zip = Path.TrimEndingDirectorySeparator(folder) + ".zip";
        var leftOut = new List<string>();
        using FileStream file = File.Create(zip);
        using var archive = new ZipArchive(file, ZipArchiveMode.Create);
        foreach (string path in Directory.EnumerateFiles(folder).Order(StringComparer.OrdinalIgnoreCase))
        {
            string name = Path.GetFileName(path);
            bool packed = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".rec", StringComparison.OrdinalIgnoreCase);
            try
            {
                archive.CreateEntryFromFile(path, name, packed ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            }
            catch (IOException)
            {
                leftOut.Add(name);
            }
        }

        return (zip, leftOut);
    }

    /// <summary>The area's loaded files, one to a line, as the game listed them.</summary>
    public static string Loaded(IReadOnlyList<string> loaded)
    {
        ArgumentNullException.ThrowIfNull(loaded);
        var said = new StringBuilder();
        said.Append(Say(loaded.Count)).AppendLine(" files the area loaded");
        foreach (string one in loaded)
        {
            said.AppendLine(one);
        }

        return said.ToString();
    }

    /// <summary>
    /// Every room arranged by its ground and tiles, in a line and its detail: where each was laid, what disagrees there, how many of its tiles can be walked, and the rooms crowded out or not found.
    /// </summary>
    /// <remarks>
    /// THE TILE BOOK'S LINE AND ITS HOVER, built here so the capture writes the very text the window
    /// shows: a report that differs from the screen by a word is a report somebody has to reconcile.
    /// </remarks>
    public static (string Said, string Detail) Arranged(RoomArrangement arranged)
    {
        ArgumentNullException.ThrowIfNull(arranged);
        int moved = arranged.Laid.Count(one => one.Rank > 0);
        string said = string.Create(CultureInfo.InvariantCulture, $"rooms: {arranged.Laid.Count} drawn")
            + (moved > 0 ? string.Create(CultureInfo.InvariantCulture, $", {moved} off their first place") : string.Empty)
            + (arranged.Crowded.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $", {arranged.Crowded.Count} with no free place") : string.Empty)
            + (arranged.Unfound.Count > 0 ? string.Create(CultureInfo.InvariantCulture, $", {arranged.Unfound.Count} not found") : string.Empty);
        var lines = new List<string>(arranged.Laid.Count + arranged.Crowded.Count + arranged.Unfound.Count);
        foreach (RoomLaid one in arranged.Laid)
        {
            // HOW MANY OF ITS TILES CAN BE WALKED, because the map draws the void and unexplored
            // ground the same black: a room outlined in the black stands on ground or it does not,
            // and this is where that is read rather than argued. See RoomLaid.Standing.
            RoomMisses misses = one.Misses;
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{TerrainRooms.NameFor(one.Room)}: tile {one.Where.X}, {one.Where.Y}, {RoomFinder.Said(one.Where.Turn)}, {one.Beside}% beside the joins")
                + string.Create(CultureInfo.InvariantCulture,
                    $" - {misses.Corners.Count} corners and {misses.Tiles.Count} tiles disagree, {misses.Corners.Count + misses.Tiles.Count - misses.Elsewhere} of those at a join")
                + (one.Standing is { } standing
                    ? string.Create(CultureInfo.InvariantCulture, $" - stands on {standing} of its {one.Covers} tiles")
                    : string.Create(CultureInfo.InvariantCulture, $" - {one.Covers} tiles, walkable ground not read"))
                + (one.Rank > 0 ? string.Create(CultureInfo.InvariantCulture, $" - row {one.Rank + 1} of its list, the ones above it taken") : string.Empty));
        }

        lines.AddRange(arranged.Crowded.Select(one => TerrainRooms.NameFor(one) + ": every place on its list covers a tile a surer room holds"));
        lines.AddRange(arranged.Unfound.Select(one => TerrainRooms.NameFor(one) + ": its search found nothing"));
        return (said, string.Join('\n', lines));
    }

    /// <summary>
    /// Every room placed by its doodads or, where they could not, its tiles, in a line and its detail: each room's places with their hits, lines, offset and tiles agreeing - or the tile they are anchored on - what it yielded to whom, or why it has none. See RoomDoodadFinder and RoomTileFinder.
    /// </summary>
    public static (string Said, string Detail) Placed(IReadOnlyList<(string Room, RoomLayout Layout, RoomDoodadPlaces Places)> placed)
    {
        ArgumentNullException.ThrowIfNull(placed);
        int byDoodads = 0, doodadPlaces = 0, byTiles = 0, tilePlaces = 0, unplaced = 0;
        var lines = new List<string>(placed.Count);
        foreach ((string room, _, RoomDoodadPlaces found) in placed)
        {
            string name = TerrainRooms.NameFor(room);
            var said = new List<string>(found.Places.Count + found.Yielded.Count);
            bool tiled = found.Places.Count > 0;
            foreach (RoomDoodadPlace place in found.Places)
            {
                if (place.Anchor.Length > 0)
                {
                    said.Add(ByTiles(place));
                    continue;
                }

                tiled = false;
                said.Add(string.Create(CultureInfo.InvariantCulture,
                    $"tile {place.Where.X}, {place.Where.Y}, {RoomFinder.Said(place.Where.Turn)}: {place.Hits} of {place.Lines} doodads within a tile ({place.PropHits} of {place.Props} props), off {place.MeanOff:0} on average, {Tiles(place)}"));
            }

            // WHAT IT GAVE UP, AND TO WHOM, with the same figures as a kept place - the props and the
            // tiles are what settled it, and a yield is the line somebody reads when the name on the
            // map is the wrong variant. See RoomDoodadFinder.Settle.
            foreach ((RoomDoodadPlace place, string to) in found.Yielded)
            {
                said.Add(string.Create(CultureInfo.InvariantCulture,
                    $"yielded tile {place.Where.X}, {place.Where.Y}, {RoomFinder.Said(place.Where.Turn)} ({place.Hits} of {place.Lines}, {place.PropHits} of {place.Props} props, {Tiles(place)}) to {TerrainRooms.NameFor(to)}"));
            }

            if (found.Places.Count == 0)
            {
                unplaced++;
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{name}: no place - {found.Why}; {found.Matchable} of its {found.Lines} doodad lines stand in the area, {found.Props} of them props, {found.ByModel} told by their model")
                    + (said.Count > 0 ? " - " + string.Join("; ", said) : string.Empty));
                continue;
            }

            if (tiled)
            {
                byTiles++;
                tilePlaces += found.Places.Count;
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"{name}: {found.Places.Count} place{(found.Places.Count == 1 ? string.Empty : "s")} by its tiles - ") + string.Join("; ", said));
                continue;
            }

            byDoodads++;
            doodadPlaces += found.Places.Count;
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"{name}: {found.Places.Count} place{(found.Places.Count == 1 ? string.Empty : "s")}, {found.ByModel} of {found.Matchable} doodads told by their model - ")
                + string.Join("; ", said));
        }

        string line = string.Create(CultureInfo.InvariantCulture, $"rooms: {byDoodads} placed by their doodads at {doodadPlaces} places")
            + (byTiles > 0 ? string.Create(CultureInfo.InvariantCulture, $", {byTiles} by their tiles at {tilePlaces} places") : string.Empty)
            + (unplaced > 0 ? string.Create(CultureInfo.InvariantCulture, $", {unplaced} with no place") : string.Empty);
        return (line, string.Join('\n', lines));

        // The tiles' verdict at the place, or that the room could not be scored against this area's ground - see RoomFinder.Scorer.
        static string Tiles(RoomDoodadPlace place)
            => place.TilesAgree >= 0 ? string.Create(CultureInfo.InvariantCulture, $"{place.TilesAgree} tiles agree") : "tiles not scored";

        // A place by the tiles: a room's own, anchored on a tile; or a footprint drawn once for the room or
        // rooms standing on a tile that the tiles cannot tell apart - the room's whole footprint where
        // every way they stand agrees on it, the anchor tile's own where not - see RoomTileFinder.
        static string ByTiles(RoomDoodadPlace place)
        {
            RoomCandidate where = place.Where;
            string anchor = TerrainRooms.NameFor(place.Anchor);
            string names = string.Join(", ", place.Variants.Select(TerrainRooms.NameFor));
            return (place.Variants.Count, place.WholeRoom) switch
            {
                (0, _) => string.Create(CultureInfo.InvariantCulture,
                    $"tile {where.X}, {where.Y}, {RoomFinder.Said(where.Turn)}: every one of its {where.Big} big slots alike on {anchor}, laid {place.AnchorLaid}x, {where.TilesAgree} of {where.Tiles} slots alike"),
                (1, true) => string.Create(CultureInfo.InvariantCulture,
                    $"tile {where.X}, {where.Y}, {where.Width} x {where.Height}: the room's whole footprint, anchored on {anchor} - it stands there more than one way round, every way on this footprint"),
                (1, false) => string.Create(CultureInfo.InvariantCulture,
                    $"tile {where.X}, {where.Y}: the {anchor} tile's own footprint, {where.Width} x {where.Height} - the room stands on it more than one way round and the tiles cannot say which, so the tile is drawn"),
                (_, true) => string.Create(CultureInfo.InvariantCulture,
                    $"tile {where.X}, {where.Y}, {where.Width} x {where.Height}: the whole footprint {names} all stand on, anchored on {anchor}, drawn once"),
                (_, false) => string.Create(CultureInfo.InvariantCulture,
                    $"tile {where.X}, {where.Y}: the {anchor} tile's own footprint, {where.Width} x {where.Height} - {names} all stand on it and the tiles cannot say which, drawn once"),
            };
        }
    }

    /// <summary>
    /// A survey of the entity maps in a line and its detail: the three numbers, then each room's doodad lines against what was found, each path found and how many stand, and the paths found nowhere - see SleepingDoodads.
    /// </summary>
    /// <param name="done">The survey.</param>
    /// <param name="rooms">The rooms with their files read, or null while they are not.</param>
    public static (string Said, string Detail) Doodads(DoodadSurvey done, IReadOnlyList<(string Room, RoomLayout Layout)>? rooms)
    {
        ArgumentNullException.ThrowIfNull(done);
        if (done.Why.Length > 0)
        {
            return ("doodads: " + done.Why, string.Empty);
        }

        var byPath = new Dictionary<string, (int All, int Asleep)>(StringComparer.OrdinalIgnoreCase);
        var remembered = 0;
        foreach (DoodadSighting one in done.Found)
        {
            (int all, int asleep) = byPath.GetValueOrDefault(one.Path);
            byPath[one.Path] = (all + 1, asleep + (one.Asleep ? 1 : 0));
            remembered += one.Remembered ? 1 : 0;
        }

        // THE BUBBLE'S OWN, counted apart: a remembered sighting is a scripted object the frame's read
        // listed at some moment since the survey - see DoodadMemory - and its number says how much of
        // the placing the survey alone could not have given.
        string said = string.Create(CultureInfo.InvariantCulture,
            $"doodads: {done.Found.Count} entities stand where the rooms name a doodad, on {byPath.Count} paths - sleeping map {done.SleepingNodes} of {done.SleepingSize} walked,"
            + $" awake {done.AwakeNodes}, {done.Named} with a path, read in {done.Milliseconds:0} ms")
            + (remembered > 0 ? string.Create(CultureInfo.InvariantCulture, $", {remembered} remembered from the bubble since") : string.Empty);

        var lines = new List<string>();
        var unfound = new SortedSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach ((string room, RoomLayout layout) in rooms ?? [])
        {
            var stubs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int named = 0, found = 0, standing = 0;
            foreach (RoomDoodad doodad in layout.Doodads)
            {
                if (doodad.Stub.Length == 0)
                {
                    continue;
                }

                named++;
                bool known = byPath.TryGetValue(doodad.Stub, out (int All, int Asleep) count);
                found += known ? 1 : 0;
                if (stubs.Add(doodad.Stub))
                {
                    standing += count.All;
                    if (!known)
                    {
                        unfound.Add(doodad.Stub);
                    }
                }
            }

            lines.Add(named == 0
                ? TerrainRooms.NameFor(room) + ": its doodad lines name no stub"
                : string.Create(CultureInfo.InvariantCulture,
                    $"{TerrainRooms.NameFor(room)}: {found} of its {named} doodad lines name a path found in the area, {standing} such entities over {stubs.Count} paths"));
        }

        foreach ((string path, (int all, int asleep)) in byPath.OrderByDescending(one => one.Value.All).ThenBy(one => one.Key, StringComparer.OrdinalIgnoreCase))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"{path}: {all} entities, {asleep} asleep"));
        }

        if (unfound.Count > 0)
        {
            lines.Add("found nowhere: " + string.Join(", ", unfound));
        }

        return (said, string.Join('\n', lines));
    }

    /// <summary>
    /// Every room file the area may have laid, whole, each under its path - the lines a placing is checked against, beside the sightings they are checked against.
    /// </summary>
    /// <remarks>
    /// WHAT THE FIRST ASSEMBLY CAPTURE LACKED: six rooms without a place, 844 sightings to hold them
    /// against, and not one of the rooms' doodad lines in the folder - rooms.txt prints a file only
    /// for the room the player stands in. The placing is pure (RoomDoodadFinder.Find takes the lines
    /// and the sightings and nothing else), so with the files here it runs again on any machine.
    /// THE WHOLE SET, not the loaded list's rooms alone - see AreaRoomSet for the ten of fifty-three.
    /// </remarks>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="loaded">The files the area loaded - the room files among them and every room of the sets its files and the masters name are printed.</param>
    /// <param name="masters">Every master the install has, as its index lists them - see AreaRoomSet.</param>
    public static string RoomFiles(Func<string, byte[]?> read, IReadOnlyList<string> loaded, IReadOnlyList<string>? masters = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(loaded);
        var said = new StringBuilder();
        List<string> rooms = AreaRoomSet.Files(loaded, read, masters);
        said.Append(Say(rooms.Count)).AppendLine(" room files the area may lay - the loaded list's and every room of the sets its files and the install's masters name");
        said.AppendLine();
        foreach (string path in rooms)
        {
            said.Append("##### ").AppendLine(path);
            byte[]? file = read(path);
            said.AppendLine(file is { Length: > 0 } ? StatDescriptionFiles.Decode(file).TrimEnd() : "(not in the install)");
            said.AppendLine();
        }

        return said.ToString();
    }

    /// <summary>
    /// Every distinct tile file the area laid, with what a room's slot is checked against - size, tag, edge and corner ground types - one tab-separated line a file, or why it did not read.
    /// </summary>
    /// <remarks>
    /// WHAT THE SEARCH NEEDS THAT THE RECORDING CANNOT HOLD. The recording keeps the terrain whole - which
    /// tile file sits on every cell - but a slot is checked against the file's own definition
    /// (RoomPlacements), and the files are the install's, not memory's. With them here the
    /// ground-and-tile search runs again from a capture on any machine, held against the places the
    /// doodads found: the one measure of whether the tiles alone can place a room the doodads cannot,
    /// which The Stone Citadel's corridors without a doodad line made a question. See CaptureReplayTests.
    /// </remarks>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="tiles">The tiles the area laid, whose distinct files are listed.</param>
    public static string TileIdentities(Func<string, byte[]?> read, TerrainTiles tiles)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(tiles);
        var said = new StringBuilder();
        said.Append(Say(tiles.Paths.Count)).AppendLine(" tile files laid in the area, each as a room's slot asks about it - tab-separated, or why it did not read");
        said.AppendLine();
        Identities(said, read, tiles.Paths.Order(StringComparer.OrdinalIgnoreCase));
        return said.ToString();
    }

    /// <summary>The identity table's heading and one line per path - what <see cref="TileIdentities"/> and <see cref="SlotCandidates"/> both write.</summary>
    /// <returns>Each path with its identity, or null where it did not read, in the order given.</returns>
    private static List<(string Path, TileIdentity? Identity)> Identities(StringBuilder said, Func<string, byte[]?> read, IEnumerable<string> paths)
    {
        var found = new List<(string, TileIdentity?)>();
        said.AppendLine("path\twidth\theight\ttag\tedge down\tedge right\tedge up\tedge left\tground down-left\tground down-right\tground up-right\tground up-left");
        foreach (string path in paths)
        {
            (TileDefinition definition, string why) = TileModels.Defined(read, path);
            said.Append(path).Append('\t');
            TileIdentity? identity = TileIdentity.Of(definition);
            found.Add((path, identity));
            if (identity is null)
            {
                said.Append("(not read: ").Append(why.Length > 0 ? why : definition.Why).AppendLine(")");
                continue;
            }

            said.Append(CultureInfo.InvariantCulture, $"{identity.Width}\t{identity.Height}\t{identity.Tag}");
            foreach (string edge in identity.Edges)
            {
                said.Append('\t').Append(edge);
            }

            foreach (string ground in identity.Grounds)
            {
                said.Append('\t').Append(ground);
            }

            said.AppendLine();
        }

        return found;
    }

    /// <summary>
    /// Every room of the area's set, each kind of its slots with the tiles of the area's tilesets that answer it - the measure of what a room could be built from without the area - and those tiles' identities after it.
    /// </summary>
    /// <remarks>
    /// WHAT tile-identities.txt CANNOT SAY. That file holds the tiles the area LAID, which is what the
    /// search needs; building a room without the area asks the other question - of every tile the
    /// tileset OFFERS, how many answer each slot. A boss room whose every kind of slot one tile answers
    /// is settled by the files alone; one with a kind several answer is the generator's choice, and a
    /// build without the area would guess there. The tilesets are the area's masters' (AreaRoomSet.Masters,
    /// the TileSet line of each), their lists read with their includes, and the table at the end is the
    /// same shape as tile-identities.txt so a changed rule can be run over it on any machine
    /// (SlotCandidates, in the test project's reach).
    /// </remarks>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="loaded">The files the area loaded - for its rooms, sets and masters.</param>
    /// <param name="masters">Every master the install has, as its index lists them - see AreaRoomSet.</param>
    public static string SlotCandidates(Func<string, byte[]?> read, IReadOnlyList<string> loaded, IReadOnlyList<string>? masters = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(loaded);
        var said = new StringBuilder();
        (List<string> tiles, List<string> sets, List<string> named) = Listed(read, loaded, masters);
        List<string> rooms = AreaRoomSet.Files(loaded, read, masters);
        said.Append(Say(rooms.Count)).Append(" rooms of the area's set against ").Append(Say(tiles.Count)).Append(" tiles its tilesets list - ")
            .Append(Say(named.Count)).Append(named.Count == 1 ? " master" : " masters").Append(sets.Count > 0 ? ", lists " + string.Join(", ", sets) : ", none naming a tileset").AppendLine();
        said.AppendLine("Each kind of slot once with the tiles alike to it by the search's rule (SlotWants): the size either way round, the tag where the slot names one,"
            + " every edge and ground type the slot names among the tile's. One tile alike settles the kind by the files; several is the generator's choice.");
        said.AppendLine();

        if (tiles.Count == 0)
        {
            said.AppendLine("no tiles: no master of the area names a tileset, so nothing can answer a slot");
            return said.ToString();
        }

        // THE IDENTITIES FIRST INTO A SIDE TABLE, written at the end: the rooms read better before it.
        var table = new StringBuilder();
        List<(string Path, TileIdentity? Identity)> identities = Identities(table, read, tiles);
        int unread = identities.Count(one => one.Identity is null);

        var settled = 0;
        var arenas = 0;
        var arenasSettled = 0;
        var slots = 0;
        var slotsSettled = 0;
        var body = new StringBuilder();
        foreach (string room in rooms)
        {
            RoomSlotCandidates found = Game.World.SlotCandidates.Of(room, RoomLayout.Read(read(room)), identities);
            bool arena = TerrainLandmarks.LooksLikeArena(room);
            body.Append("##### ").Append(room).Append(arena ? "  (arena)" : string.Empty).Append("  -  ").AppendLine(Game.World.SlotCandidates.Said(found));
            slots += found.Slots;
            slotsSettled += found.SlotsSettled;
            if (found.Settled)
            {
                settled++;
            }

            if (arena)
            {
                arenas++;
                if (found.Settled)
                {
                    arenasSettled++;
                }
            }

            foreach (SlotKindAlike kind in found.Kinds)
            {
                body.Append("  ").Append(kind.Kind.Slots == 1 ? "1 slot" : Say(kind.Kind.Slots) + " slots").Append(kind.Kind.Big ? " (big) " : " ")
                    .Append(kind.Kind.Said).Append(" -> ").Append(Say(kind.Tiles.Count)).Append(kind.Tiles.Count == 1 ? " tile" : " tiles");
                if (kind.Tiles.Count is > 0 and <= 6)
                {
                    body.Append(": ").Append(string.Join(", ", kind.Tiles.Select(TileName)));
                }

                body.AppendLine();
            }

            body.AppendLine();
        }

        said.Append("summary: ").Append(Say(settled)).Append(" of ").Append(Say(rooms.Count)).Append(" rooms settled by the files alone (every kind of slot one tile)")
            .Append(" · arena rooms ").Append(Say(arenasSettled)).Append(" of ").Append(Say(arenas))
            .Append(" · slots settled ").Append(Say(slotsSettled)).Append(" of ").Append(Say(slots))
            .Append(" · tiles that did not read ").Append(Say(unread)).AppendLine();
        said.AppendLine();
        said.Append(body);
        said.Append("##### the tilesets' tiles, each as a room's slot asks about it - tab-separated, or why it did not read (").Append(Say(tiles.Count)).AppendLine(")");
        said.Append(table);
        return said.ToString();
    }

    /// <summary>A tile's file name without folder or extension, for the candidate lists - the table at the end has the whole path.</summary>
    private static string TileName(string path) => Path.GetFileNameWithoutExtension(path.Replace('\\', '/'));

    /// <summary>
    /// The tiles the area's tilesets offer, each once in the order the lists name them, with the lists they were read from and the area's masters.
    /// </summary>
    private static (List<string> Tiles, List<string> Sets, List<string> Masters) Listed(
        Func<string, byte[]?> read, IReadOnlyList<string> loaded, IReadOnlyList<string>? masters)
    {
        List<string> named = AreaRoomSet.Masters(loaded, masters);
        var tiles = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lists = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var sets = new List<string>();
        foreach (string master in named)
        {
            byte[]? bytes = read(master.Replace('\\', '/').Trim());
            string? set = TilesetFile.Value(bytes is { Length: > 0 } ? StatDescriptionFiles.Decode(bytes) : null, "TileSet");
            if (set is not { Length: > 0 })
            {
                continue;
            }

            string tst = TilesetFile.Beside(master, set);
            sets.Add(tst);
            foreach (string tile in TilesetIndex.TilesOf(read, tst, lists))
            {
                string path = tile.Replace('\\', '/').Trim();
                if (seen.Add(path))
                {
                    tiles.Add(path);
                }
            }
        }

        return (tiles, sets, named);
    }

    /// <summary>
    /// Every tile file the area's tile lists or fill lists name or its terrain laid, each with its definition followed hop by hop through what it inherits and the tile's doodad list (.tmd) - the laid files no tile list names first, with what explains each.
    /// </summary>
    /// <remarks>
    /// EVERY AREA LAYS FILES NO TILE LIST NAMES - one to twenty in each of the first eleven captures. Sulphuric
    /// Caverns' boss room lays Thickwall/Fill_01 where its list names Murky/Thickwall/Fill_01, and an
    /// Abyss/Lava/Fill_02 beside the list's Fill_01, so a room built from the list can never come out the game's
    /// file there. Three roads are written for each laid file, so they are held against it offline
    /// (CaptureReplayTests) rather than argued: a definition may INHERIT another and carry nothing else
    /// (annalithic's Tdt.cs), so a laid file may be where a listed one's chain ends; a master names a FILL LIST
    /// beside its tile list (FillTiles, a .gft - see FillList), whose tiles are flagged 'fill' here; and two
    /// files may share a template, the geometry.
    ///
    /// A .tmd IS THE TILE'S DOODAD LIST, as Marrow's showed: text, 'version 8', then a line a doodad - position,
    /// rotation, an .ao and an entity type (DoodadNoBlocking, WallSeals, TileUpperDoodad) - the doodads a tile
    /// brings with it, not variants of it. It sits beside the .tdt or beside its template: the loaded list pairs
    /// Art/Models/.../Concave_01_01.tmd with Metadata/.../ThickWall/Concave_01_01.tdt, whose template is
    /// Art/Models/.../Concave_01_01.tgt. Both places are tried and every one found is written whole.
    /// </remarks>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="loaded">The files the area loaded - for its masters.</param>
    /// <param name="masters">Every master the install has, as its index lists them - see AreaRoomSet.</param>
    /// <param name="laid">The tiles the area laid, or null where the terrain was not read - then only the listed files.</param>
    public static string TileFiles(Func<string, byte[]?> read, IReadOnlyList<string> loaded, IReadOnlyList<string>? masters, TerrainTiles? laid)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(loaded);
        (List<string> tiles, _, List<string> named) = Listed(read, loaded, masters);
        var listed = new HashSet<string>(tiles, StringComparer.OrdinalIgnoreCase);

        // THE FILL LISTS' TILES, each with every line that names it.
        var fills = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach ((string list, FillList fill) in FillLists(read, named))
        {
            foreach (FillListEntry entry in fill.Entries)
            {
                if (entry.Path.EndsWith(".tdt", StringComparison.OrdinalIgnoreCase))
                {
                    string words = entry.Words.Count > 0 ? " " + string.Join(' ', entry.Words) : string.Empty;
                    (fills.TryGetValue(entry.Path, out List<string>? lines) ? lines : fills[entry.Path] = [])
                        .Add(string.Create(CultureInfo.InvariantCulture, $"{list} section \"{entry.Section}\" weight {entry.Weight}{words}"));
                }
            }
        }

        var onGround = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in laid?.Paths ?? [])
        {
            if (path.Length > 0)
            {
                onGround.Add(path.Replace('\\', '/').Trim());
            }
        }

        var every = new SortedSet<string>(listed, StringComparer.OrdinalIgnoreCase);
        every.UnionWith(fills.Keys);
        every.UnionWith(onGround);

        // EACH FILE ONCE: the chain's hops, the template the chain ends at, and the doodad list.
        var chains = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var templatesOf = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        var shapes = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var body = new StringBuilder();
        foreach (string path in every)
        {
            string flags = string.Join(", ", ((string[])["listed", "fill", "laid"]).Where((_, at) => at switch
            {
                0 => listed.Contains(path),
                1 => fills.ContainsKey(path),
                _ => onGround.Contains(path),
            }));
            body.Append("##### ").Append(path).Append("  (").Append(flags).AppendLine(")");
            (List<string> hops, IReadOnlyList<string> templates) = Chain(body, read, path);
            chains[path] = hops;
            templatesOf[path] = templates;
            Tmds(body, read, path, templates);
            if (listed.Contains(path))
            {
                foreach (string template in templates)
                {
                    (shapes.TryGetValue(template, out List<string>? sharing) ? sharing : shapes[template] = []).Add(path);
                }
            }
        }

        List<string> unlisted = [.. onGround.Where(path => !listed.Contains(path)).Order(StringComparer.OrdinalIgnoreCase)];
        var said = new StringBuilder();
        said.Append(Say(every.Count)).Append(" tile files - ").Append(Say(listed.Count)).Append(" the area's tile lists name, ")
            .Append(Say(fills.Count)).Append(" its fill lists, ")
            .Append(laid is null ? "the terrain not read" : Say(onGround.Count) + " laid, " + Say(unlisted.Count) + " of those named by no tile list")
            .AppendLine("; each with its definition followed through what it inherits, and the tile's doodad list (.tmd)");
        said.AppendLine("a definition that inherits carries nothing but the reference (annalithic's Tdt.cs); a .tmd is the tile's own doodads, found beside the .tdt or beside its template");
        said.AppendLine();
        said.AppendLine("=== laid, but named by no tile list - the listed files whose chain passes through it, the fill lists that name it, the listed files sharing its template");
        foreach (string path in unlisted)
        {
            said.Append(path).AppendLine();
            said.Append("  reached by inheriting from: ").AppendLine(Joined(tiles.Where(one => chains[one].Skip(1).Contains(path, StringComparer.OrdinalIgnoreCase))));
            said.Append("  in a fill list: ").AppendLine(fills.TryGetValue(path, out List<string>? lines) ? string.Join("; ", lines) : "none");
            said.Append("  shares a template with: ").AppendLine(Joined(templatesOf[path]
                .SelectMany(template => shapes.TryGetValue(template, out List<string>? sharing) ? sharing : [])
                .Distinct(StringComparer.OrdinalIgnoreCase)));
        }

        if (unlisted.Count == 0)
        {
            said.AppendLine(laid is null ? "(the terrain was not read)" : "(none)");
        }

        said.AppendLine();
        said.AppendLine("=== every file: 'inherits' for each hop, then 'defines' (size, tag, templates) or 'not read'; 'tmd' with '| ' text lines, or 'hex' and 'strings'");
        said.Append(body);
        return said.ToString();

        static string Joined(IEnumerable<string> paths)
        {
            string joined = string.Join(", ", paths);
            return joined.Length > 0 ? joined : "none";
        }
    }

    /// <summary>
    /// Writes a tile file's definition hop by hop - an 'inherits' line for each file it inherits, then the one that defines it - and returns every file of the chain, its own first, with the templates the chain ends at.
    /// </summary>
    private static (List<string> Hops, IReadOnlyList<string> Templates) Chain(StringBuilder said, Func<string, byte[]?> read, string path)
    {
        var hops = new List<string>();
        string at = path;
        for (var hop = 0; hop <= TileModels.MostHops; hop++)
        {
            if (hops.Contains(at, StringComparer.OrdinalIgnoreCase))
            {
                said.Append("  loop\t").AppendLine(at);
                return (hops, []);
            }

            hops.Add(at);
            TileDefinition one = TileDefinition.Read(read(at));
            if (!one.Ready)
            {
                said.Append("  not read\t").Append(at).Append('\t').AppendLine(one.Why);
                return (hops, []);
            }

            if (one.Inherits.Length == 0)
            {
                said.Append(CultureInfo.InvariantCulture, $"  defines\t{at}\t{one.Width}x{one.Height}\ttag {one.Tag}\ttemplates {string.Join(';', one.Templates)}").AppendLine();
                return (hops, [.. one.Templates.Select(template => template.Replace('\\', '/').Trim())]);
            }

            at = one.Inherits.Replace('\\', '/').Trim();
            said.Append("  inherits\t").AppendLine(at);
        }

        said.Append("  past ").Append(Say(TileModels.MostHops)).AppendLine(" hops");
        return (hops, []);
    }

    /// <summary>
    /// Writes a tile's doodad lists - the .tmd beside the .tdt and the one beside each template, every one found - or that there is none.
    /// </summary>
    private static void Tmds(StringBuilder said, Func<string, byte[]?> read, string path, IReadOnlyList<string> templates)
    {
        var tried = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var found = 0;
        foreach (string owner in templates.Prepend(path))
        {
            string tmd = Path.ChangeExtension(owner, ".tmd");
            if (!tried.Add(tmd) || read(tmd) is not { Length: > 0 } bytes)
            {
                continue;
            }

            found++;
            said.Append(CultureInfo.InvariantCulture, $"  tmd\t{tmd}\t{bytes.Length} bytes\t{(IsText(bytes) ? "text" : "binary")}").AppendLine();
            Whole(said, bytes);
        }

        if (found == 0)
        {
            said.AppendLine("  tmd\tnone");
        }
    }

    /// <summary>
    /// Every master of the area whole, and every file a master names - room set, fill list, anything else - whole too, once; a tile list only by name, being whole in tileset-lists.txt.
    /// </summary>
    /// <remarks>
    /// THE PART OF THE MASTER NOTHING READ. The tool took two keys of it - TileSet and RoomSet - while
    /// a master is a key-value file of any keys (poe_data_tools' tsi: a map of strings), and RePoE's
    /// VaalFactory names a third, FillTiles. Marrow lays four mine_abyss_fill tiles from its Rooms/Fills
    /// folder that no tile list and no room names; the files a master names are where else a tile can come
    /// from, so they are written as they are rather than through the keys this tool already knows.
    /// </remarks>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="loaded">The files the area loaded - for its masters and the room sets it loaded itself.</param>
    /// <param name="masters">Every master the install has, as its index lists them - see AreaRoomSet.</param>
    public static string Masters(Func<string, byte[]?> read, IReadOnlyList<string> loaded, IReadOnlyList<string>? masters)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(loaded);
        List<string> named = AreaRoomSet.Masters(loaded, masters);
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var body = new StringBuilder();
        var files = 0;
        foreach (string master in named)
        {
            string path = master.Replace('\\', '/').Trim();
            if (!written.Add(path))
            {
                continue;
            }

            body.Append("##### ").Append(path).AppendLine("  (master)");
            byte[]? bytes = read(path);
            if (bytes is not { Length: > 0 })
            {
                body.AppendLine("  (not in the install)");
                continue;
            }

            Whole(body, bytes);
            foreach ((string key, string value) in MasterFile.Read(bytes).Values)
            {
                if (!NamesAFile(value))
                {
                    continue;
                }

                // AS WRITTEN, THEN BESIDE THE MASTER - the first that reads is the file, as RePoE resolves it.
                foreach (string candidate in MasterFile.Candidates(path, value))
                {
                    if (read(candidate) is not { Length: > 0 } file)
                    {
                        continue;
                    }

                    if (written.Add(candidate))
                    {
                        files++;
                        body.Append("##### ").Append(candidate).Append("  (").Append(key).Append(" of ").Append(path).AppendLine(")");
                        if (candidate.EndsWith(TileList.Extension, StringComparison.OrdinalIgnoreCase))
                        {
                            body.Append("  (a tile list - whole in ").Append(TilesetListsFile).AppendLine(")");
                        }
                        else
                        {
                            Summary(body, candidate, file);
                            Whole(body, file);
                        }
                    }

                    break;
                }
            }
        }

        // THE ROOM SETS THE LOADED LIST NAMES ITSELF - a fresh instance's road, which no master need name.
        foreach (string set in loaded)
        {
            string path = set.Replace('\\', '/').Trim();
            if (path.StartsWith("Metadata/Terrain/", StringComparison.OrdinalIgnoreCase)
                && path.EndsWith(".rs", StringComparison.OrdinalIgnoreCase)
                && written.Add(path)
                && read(path) is { Length: > 0 } bytes)
            {
                files++;
                body.Append("##### ").Append(path).AppendLine("  (loaded)");
                Whole(body, bytes);
            }
        }

        var said = new StringBuilder();
        said.Append(Say(named.Count)).Append(named.Count == 1 ? " master" : " masters").Append(" of the area and ").Append(Say(files))
            .Append(files == 1 ? " file" : " files").Append(" they name or the area loaded, each whole - a tile list by name only, being whole in ")
            .Append(TilesetListsFile).AppendLine();
        said.AppendLine();
        said.Append(body);
        return said.ToString();
    }

    /// <summary>What a fill list holds, in a line above it - the counts its text would otherwise have to be read for.</summary>
    private static void Summary(StringBuilder said, string path, byte[] bytes)
    {
        if (!path.EndsWith(FillList.Extension, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        FillList fill = FillList.Read(bytes);
        int rooms = fill.Entries.Count(entry => entry.Path.EndsWith(".arm", StringComparison.OrdinalIgnoreCase));
        said.Append(CultureInfo.InvariantCulture,
            $"  fill list: version {fill.Version}, {fill.Sections.Count} sections, {fill.Entries.Count - rooms} tiles, {rooms} rooms").AppendLine();
    }

    /// <summary>The fill lists the masters name under FillTiles, each read from the first place it resolves to, once.</summary>
    private static List<(string Path, FillList List)> FillLists(Func<string, byte[]?> read, IEnumerable<string> masters)
    {
        var found = new List<(string, FillList)>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string master in masters)
        {
            string path = master.Replace('\\', '/').Trim();
            string named = MasterFile.Read(read(path)).Values.GetValueOrDefault("FillTiles", string.Empty);
            foreach (string candidate in MasterFile.Candidates(path, named))
            {
                if (read(candidate) is not { Length: > 0 } bytes)
                {
                    continue;
                }

                if (seen.Add(candidate))
                {
                    found.Add((candidate, FillList.Read(bytes)));
                }

                break;
            }
        }

        return found;
    }

    /// <summary>Whether a master's value is a file name: one word whose last part after a dot is two to four letters or digits.</summary>
    private static bool NamesAFile(string value)
    {
        if (value.Length == 0 || value.Any(char.IsWhiteSpace))
        {
            return false;
        }

        int dot = value.LastIndexOf('.');
        int extension = value.Length - dot - 1;
        return dot > value.LastIndexOfAny(['/', '\\']) && extension is >= 2 and <= 4 && value[(dot + 1)..].All(char.IsLetterOrDigit);
    }

    /// <summary>The longest file written whole as text; a longer one is cut, and says so.</summary>
    /// <remarks>
    /// RAISED FROM 16384 when Marrow's boss arena carried a doodad list of 16949 characters: cut, its last line read as a
    /// refused line in the replay - the parser doing its job on a file the capture had broken. A boss arena's list is the
    /// longest a tile carries, and the lists are what the tile-doodad replay reads.
    /// </remarks>
    private const int WholeTextMost = 65_536;

    /// <summary>How many of a binary file's opening bytes are written in hex.</summary>
    private const int WholeHexMost = 256;

    /// <summary>Whether bytes decode as text: no control character but line breaks and tabs.</summary>
    private static bool IsText(byte[] bytes)
        => !StatDescriptionFiles.Decode(bytes).Any(c => c < ' ' && c is not ('\r' or '\n' or '\t'));

    /// <summary>Writes a file as it is: its text a '| ' line at a time, or its opening bytes in hex and the strings in it.</summary>
    private static void Whole(StringBuilder said, byte[] bytes)
    {
        string text = StatDescriptionFiles.Decode(bytes);
        if (IsText(bytes))
        {
            string shown = text.Length > WholeTextMost ? text[..WholeTextMost] : text;
            foreach (string line in shown.Split('\n'))
            {
                said.Append("  | ").AppendLine(line.TrimEnd('\r'));
            }

            if (text.Length > WholeTextMost)
            {
                said.Append(CultureInfo.InvariantCulture, $"  | ... cut at {WholeTextMost} of {text.Length} characters").AppendLine();
            }

            return;
        }

        said.Append("  hex\t").AppendLine(Convert.ToHexString(bytes, 0, Math.Min(bytes.Length, WholeHexMost)));
        said.Append("  strings\t").AppendLine(string.Join(" ", Strings(bytes).Select(one => "\"" + one + "\"")));
    }

    /// <summary>The printable runs of four characters or more in a binary file, read both as UTF-16 and as single bytes, each once.</summary>
    private static List<string> Strings(byte[] bytes)
    {
        var found = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var run = new StringBuilder();
        void Close()
        {
            if (run.Length >= 4 && seen.Add(run.ToString()))
            {
                found.Add(run.ToString());
            }

            run.Clear();
        }

        for (var at = 0; at + 1 < bytes.Length; at += 2)
        {
            char c = (char)(bytes[at] | (bytes[at + 1] << 8));
            if (c is >= ' ' and < (char)0x7F)
            {
                run.Append(c);
            }
            else
            {
                Close();
            }
        }

        Close();
        foreach (byte b in bytes)
        {
            if (b is >= 0x20 and < 0x7F)
            {
                run.Append((char)b);
            }
            else
            {
                Close();
            }
        }

        Close();
        return found;
    }

    /// <summary>
    /// The area's tile lists whole, each master's and every list it includes, under their paths - the weights the generator draws by and the words after each tile, which poe_data_tools reads as rotations and no further.
    /// </summary>
    /// <remarks>
    /// WHAT A ROOM IS BUILT FROM WITHOUT THE AREA (RoomBuilder): the pool, its weights, and whatever the
    /// words say about how a tile may be laid - a vocabulary nothing here has read yet, so the lists are
    /// written as they are, for the words to be looked up in rather than guessed at.
    /// </remarks>
    public static string TilesetLists(Func<string, byte[]?> read, IReadOnlyList<string> loaded, IReadOnlyList<string>? masters = null)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(loaded);
        var said = new StringBuilder();
        var written = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var body = new StringBuilder();
        var lists = 0;
        var entries = 0;
        var words = new SortedDictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        List<string> named = AreaRoomSet.Masters(loaded, masters);
        foreach (string master in named)
        {
            byte[]? bytes = read(master.Replace('\\', '/').Trim());
            string? set = TilesetFile.Value(bytes is { Length: > 0 } ? StatDescriptionFiles.Decode(bytes) : null, "TileSet");
            if (set is not { Length: > 0 })
            {
                body.Append("##### ").Append(master).AppendLine("  names no tileset");
                continue;
            }

            (IReadOnlyList<TileListEntry> listed, IReadOnlyList<(string Path, string Text)> texts) = TilesetIndex.EntriesOf(read, TilesetFile.Beside(master, set));
            foreach ((string path, string text) in texts)
            {
                if (!written.Add(path))
                {
                    continue;
                }

                lists++;
                body.Append("##### ").Append(path).Append("  (").Append(master).AppendLine(")");
                body.AppendLine(text.Length > 0 ? text.TrimEnd() : "(not in the install)");
                body.AppendLine();
            }

            foreach (TileListEntry entry in listed)
            {
                entries++;
                foreach (string word in entry.Words)
                {
                    words[word] = words.GetValueOrDefault(word) + 1;
                }
            }
        }

        said.Append(Say(lists)).Append(lists == 1 ? " tile list" : " tile lists").Append(" of ").Append(Say(named.Count)).Append(named.Count == 1 ? " master" : " masters")
            .Append(", ").Append(Say(entries)).AppendLine(" tile lines - each list whole: an optional weight, the tile, then the words after it");
        said.Append("words after the tiles, how often: ").AppendLine(words.Count == 0 ? "none" : string.Join(", ", words.Select(one => $"{one.Key} x{one.Value}")));
        said.AppendLine();
        said.Append(body);
        return said.ToString();
    }

    /// <summary>
    /// The entities each place hit, by id - so a place in rooms-placed.txt can be held against the sightings in doodads.txt entity by entity.
    /// </summary>
    /// <param name="placed">Every room with its places, as settled.</param>
    /// <param name="survey">The survey the places were found in, whose sightings the places index.</param>
    public static string PlacedEntities(IReadOnlyList<(string Room, RoomLayout Layout, RoomDoodadPlaces Places)> placed, DoodadSurvey survey)
    {
        ArgumentNullException.ThrowIfNull(placed);
        ArgumentNullException.ThrowIfNull(survey);
        var said = new StringBuilder();
        said.AppendLine("=== the entities each place hit, by id, and the ones it yielded");
        foreach ((string room, _, RoomDoodadPlaces found) in placed)
        {
            foreach (RoomDoodadPlace place in found.Places)
            {
                Entities(said, TerrainRooms.NameFor(room), place, survey);
            }

            foreach ((RoomDoodadPlace place, _) in found.Yielded)
            {
                Entities(said, TerrainRooms.NameFor(room) + " (yielded)", place, survey);
            }
        }

        return said.ToString();
    }

    private static void Entities(StringBuilder said, string room, RoomDoodadPlace place, DoodadSurvey survey)
    {
        said.Append(room).Append(CultureInfo.InvariantCulture, $" tile {place.Where.X}, {place.Where.Y}, {RoomFinder.Said(place.Where.Turn)}: {place.Entities.Count} entities -");
        foreach (int one in place.Entities)
        {
            said.Append(' ').Append(one >= 0 && one < survey.Found.Count ? survey.Found[one].Id.ToString(CultureInfo.InvariantCulture) : "?");
        }

        said.AppendLine();
    }

    /// <summary>
    /// Every sighting of a survey as a table: id, path, model, where it stands, and which map it came out of - the finder's whole input, so a placing can be redone by hand.
    /// </summary>
    public static string Sightings(DoodadSurvey done)
    {
        ArgumentNullException.ThrowIfNull(done);
        var said = new StringBuilder();
        said.Append(Say(done.Found.Count)).AppendLine(" sightings; position in world units, z the game's way up");
        said.AppendLine("id\tpath\tmodel\tx\ty\tz\tmap");
        foreach (DoodadSighting one in done.Found)
        {
            said.Append(one.Id.ToString(CultureInfo.InvariantCulture)).Append('\t').Append(one.Path).Append('\t').Append(one.Model)
                .Append('\t').Append(Num(one.X)).Append('\t').Append(Num(one.Y)).Append('\t').Append(Num(one.Z))
                .Append('\t').AppendLine(one.Remembered ? "remembered" : one.Asleep ? "sleeping" : "awake");
        }

        return said.ToString();
    }

    /// <summary>A line and its detail as one text, the way the hover reads it.</summary>
    public static string Lined((string Said, string Detail) text) => text.Detail.Length > 0 ? text.Said + '\n' + text.Detail : text.Said;

    private static string Say(int number) => number.ToString(CultureInfo.InvariantCulture);

    private static string Num(float number) => number.ToString("0.###", CultureInfo.InvariantCulture);
}
