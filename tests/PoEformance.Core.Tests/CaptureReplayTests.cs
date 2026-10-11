using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Core.Memory;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// The placing and the ground-and-tile search run again from a capture, without the game: POEF_CAPTURE names the capture's folder; what the tool would write to rooms-placed.txt from its room-files.txt and doodads.txt goes to placing-replay.txt beside them, and the search over the recording's terrain to search-replay.txt.
/// </summary>
/// <remarks>
/// A DEV TOOL IN THE SHAPE OF A TEST, because the test project is where the finder and the capture's
/// texts are already built and referenced. With the variable unset it does nothing, so CI never
/// touches it; set, it is how a change to RoomDoodadFinder is held against a real area before it goes
/// near the game - the capture key's reason for writing every room file (CaptureReport.RoomFiles).
/// Run it with <c>POEF_CAPTURE=&lt;folder&gt; dotnet test --filter CaptureReplay</c>.
///
/// THE SEARCH REPLAY ANSWERS ONE QUESTION: whether the tiles alone could place a room its doodads
/// cannot - a corridor with no doodad line, of which The Stone Citadel has several, or a room of big
/// slots alone, which is every checkpoint and the boss of Sinter Rift. The recording holds the terrain
/// whole (the capture's world pass reads it), the room files are beside it, and tile-identities.txt
/// says what each laid tile's file asks - a capture from 0.1.155 on; without it the search scores
/// corners alone and says so. Three things are printed so both halves of every comparison are on the
/// page: how often each tile file was laid (a file laid once pins a room by itself), what each kind of
/// slot in a room asks and which laid files answer it by RoomPlacements' own rule, and the search's best
/// places beside each place the doodads found, scored by the same yardstick and ranked.
/// </remarks>
public class CaptureReplayTests
{
    /// <summary>How many of the rarest laid files are listed.</summary>
    private const int RarestListed = 40;

    /// <summary>The capture's folder, from POEF_CAPTURE - or null, and the replay does nothing.</summary>
    private static string? Folder => Environment.GetEnvironmentVariable("POEF_CAPTURE");

    /// <summary>Places every room of the capture by its doodads and writes the result beside the capture's files.</summary>
    [Fact]
    public void THEPLACINGRunsAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = Replay(folder);
        File.WriteAllText(Path.Combine(folder, "placing-replay.txt"), replay);
        Assert.StartsWith("grid ", replay, StringComparison.Ordinal);
        Assert.Contains("\nrooms: ", replay, StringComparison.Ordinal);
    }

    /// <summary>Searches every room of the capture over its recording's terrain and writes the result, with each doodad place's rank, beside the capture's files.</summary>
    [Fact]
    public void THESEARCHRunsAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = SearchReplay(folder);
        File.WriteAllText(Path.Combine(folder, "search-replay.txt"), replay);
        Assert.StartsWith("terrain ", replay, StringComparison.Ordinal);
    }

    /// <summary>rooms-placed.txt as the tool would write it from the capture's room files and sightings.</summary>
    public static string Replay(string folder)
    {
        Placing placing = Place(folder);
        return string.Create(CultureInfo.InvariantCulture, $"grid {placing.TilesX} x {placing.TilesY} tiles, {placing.Survey.Found.Count} sightings, {placing.Rooms.Count} rooms\n")
            + CaptureReport.Lined(CaptureReport.Placed(placing.Placed)) + "\n\n" + CaptureReport.PlacedEntities(placing.Placed, placing.Survey)
            + "\n" + CaptureReport.Lined(CaptureReport.Doodads(placing.Survey, placing.Rooms));
    }

    /// <summary>
    /// search-replay.txt: the laid tile files rarest first, then every room - what each kind of its slots asks and which laid files answer, the tiles under each place its doodads found, and its search by ground and tiles with the doodad places ranked by the same yardstick.
    /// </summary>
    public static string SearchReplay(string folder)
    {
        var said = new StringBuilder();
        TerrainGrid? grid = Terrain(folder, out string how);
        said.AppendLine(how);
        if (grid is not { Ground: { } ground })
        {
            said.AppendLine(grid is null ? "nothing to search without the terrain" : "nothing to search without the ground types");
            return said.ToString();
        }

        Placing placing = Place(folder);
        Dictionary<string, TileIdentity>? identities = Identities(folder);
        Func<string, TileIdentity?>? identity = identities is null ? null : file => identities.GetValueOrDefault(file);
        said.AppendLine(identities is null
            ? $"no {CaptureReport.TileIdentitiesFile} in the capture - the tiles are not checked, the corners alone are"
            : string.Create(CultureInfo.InvariantCulture, $"{identities.Count} tile identities from {CaptureReport.TileIdentitiesFile}"));
        if (grid.TilesX != placing.TilesX || grid.TilesY != placing.TilesY)
        {
            said.AppendLine(CultureInfo.InvariantCulture, $"the recording's grid is {grid.TilesX} x {grid.TilesY} tiles where capture.txt says {placing.TilesX} x {placing.TilesY} - the recording's is searched");
        }

        // HOW ALONE EACH LAID FILE IS: a file laid once pins whatever room asks for it.
        Dictionary<string, int> cells = Cells(grid.Tiles);
        if (identities is not null && cells.Count > 0)
        {
            said.AppendLine();
            said.AppendLine(CultureInfo.InvariantCulture, $"=== the {cells.Count} tile files laid, rarest first: cells, and how often each was laid (cells over its size)");
            foreach ((string path, int count) in cells.OrderBy(one => Instances(one.Key, one.Value, identities)).ThenBy(one => one.Key, StringComparer.OrdinalIgnoreCase).Take(RarestListed))
            {
                said.Append("  ").AppendLine(LaidSaid(path, count, identities, grid.Tiles));
            }

            if (cells.Count > RarestListed)
            {
                said.AppendLine(CultureInfo.InvariantCulture, $"  ... and {cells.Count - RarestListed} more");
            }
        }

        // WHERE A FILE'S FIRST PIECE SITS, by the tile's own placement: how far its pieces run right and
        // down from the cell carrying it, and the piece on each neighbour. The tile placing stands on the
        // first piece being the laid footprint's lowest corner whichever way the tile went - measured on
        // the boss's square tiles - so the oblong ones are held against it here, placement by placement.
        if (identities is not null && grid.Tiles is { } everyLaid)
        {
            var firsts = new SortedDictionary<string, SortedDictionary<int, SortedDictionary<string, int>>>(StringComparer.OrdinalIgnoreCase);
            for (var y = 0; y < everyLaid.Height; y++)
            {
                for (var x = 0; x < everyLaid.Width; x++)
                {
                    if (everyLaid.SubAt(x, y) is not (0, 0))
                    {
                        continue;
                    }

                    string path = everyLaid.PathAt(x, y);
                    if (!identities.TryGetValue(path, out TileIdentity? tile) || (tile.Width == 1 && tile.Height == 1))
                    {
                        continue;
                    }

                    int id = everyLaid.IdAt(x, y);
                    var wide = 1;
                    while (everyLaid.IdAt(x + wide, y) == id && everyLaid.SubAt(x + wide, y) == (wide, 0))
                    {
                        wide++;
                    }

                    var tall = 1;
                    while (everyLaid.IdAt(x, y + tall) == id && everyLaid.SubAt(x, y + tall) == (0, tall))
                    {
                        tall++;
                    }

                    string Piece(int nx, int ny) => everyLaid.IdAt(nx, ny) == id ? string.Create(CultureInfo.InvariantCulture, $"{everyLaid.SubAt(nx, ny).X},{everyLaid.SubAt(nx, ny).Y}") : "-";
                    string seen = string.Create(CultureInfo.InvariantCulture, $"runs {wide} x {tall}, right {Piece(x + 1, y)} down {Piece(x, y + 1)} left {Piece(x - 1, y)} up {Piece(x, y - 1)}");
                    string file = string.Create(CultureInfo.InvariantCulture, $"{Short(path)} {tile.Width}x{tile.Height}");
                    if (!firsts.TryGetValue(file, out SortedDictionary<int, SortedDictionary<string, int>>? byPlacement))
                    {
                        byPlacement = [];
                        firsts[file] = byPlacement;
                    }

                    if (!byPlacement.TryGetValue(everyLaid.PlacementAt(x, y), out SortedDictionary<string, int>? patterns))
                    {
                        patterns = new SortedDictionary<string, int>(StringComparer.Ordinal);
                        byPlacement[everyLaid.PlacementAt(x, y)] = patterns;
                    }

                    patterns[seen] = patterns.GetValueOrDefault(seen) + 1;
                }
            }

            said.AppendLine();
            said.AppendLine("=== where each laid file's first piece sits, by placement: how far its pieces run right and down from it, and the piece on each neighbour ('-' another tile)");
            foreach ((string file, SortedDictionary<int, SortedDictionary<string, int>> byPlacement) in firsts)
            {
                said.Append("  ").AppendLine(file);
                foreach ((int placement, SortedDictionary<string, int> patterns) in byPlacement)
                {
                    said.AppendLine(CultureInfo.InvariantCulture, $"    placement {placement}: {string.Join("; ", patterns.Select(one => $"{one.Key} x{one.Value}"))}");
                }
            }
        }

        bool[] walkable = grid.WalkableTileMask();
        for (var one = 0; one < placing.Rooms.Count; one++)
        {
            (string room, RoomLayout layout) = placing.Rooms[one];
            said.AppendLine();
            said.Append("=== ").Append(Path.GetFileNameWithoutExtension(room)).Append(CultureInfo.InvariantCulture, $", {layout.Width} x {layout.Height}").AppendLine();

            // THE ROOM'S ASKS, each kind of slot once, and the laid files alike by RoomPlacements' own
            // rule: the size as an unordered pair, the tag where the slot names one, the edge and ground
            // types as sets. A kind with one file alike, laid once, is the room's own fingerprint.
            if (identities is not null)
            {
                foreach (Ask ask in Asks(layout))
                {
                    List<string> alike = [.. identities.Where(laid => Alike(ask, laid.Value)).Select(laid => laid.Key).OrderBy(path => path, StringComparer.OrdinalIgnoreCase)];
                    said.Append("  slot ").Append(ask.Said).Append(CultureInfo.InvariantCulture, $": {alike.Count} laid files alike");
                    if (alike.Count is > 0 and <= 4)
                    {
                        said.Append(" - ").Append(string.Join("; ", alike.Select(path => LaidSaid(path, cells.GetValueOrDefault(path), identities, grid.Tiles))));
                    }

                    said.AppendLine();
                }
            }

            // THE OTHER HALF, FIRST: what lies under each place the doodads found, whether or not the search
            // can run - and every big slot against the tile on its own cell, by the rule of 0.1.157 (an
            // unnamed type as any) and the stricter one before it, so the rule stays held against known places.
            RoomDoodadPlaces places = placing.Placed[one].Places;
            foreach (RoomDoodadPlace place in places.Places)
            {
                RoomCandidate at = place.Where;
                said.AppendLine(CultureInfo.InvariantCulture, $"  doodads at tile {at.X}, {at.Y}, {RoomFinder.Said(at.Turn)}, {at.Width} x {at.Height} on the grid - laid under it: {Under(grid.Tiles, at)}");
                if (identities is null || grid.Tiles is not { } laidTiles)
                {
                    continue;
                }

                for (var line = 0; line < layout.Height; line++)
                {
                    for (var column = 0; column < layout.Width; column++)
                    {
                        RoomSlot slot = layout.SlotAt(column, line);
                        if (!slot.IsTile || (slot.Width == 1 && slot.Height == 1))
                        {
                            continue;
                        }

                        Ask ask = AskOf(layout, slot);
                        (int left, int top) = slot.Corner(column, line);
                        (int cellX, int cellY) = RoomFinder.CornerOf(left, top, slot.Width, slot.Height, layout.Width, layout.Height, at.Turn);
                        int x = at.X + cellX, y = at.Y + cellY;
                        string path = laidTiles.PathAt(x, y);
                        TileIdentity? laid = identities.GetValueOrDefault(path);
                        string verdict = laid is null ? "no identity" : !Alike(ask, laid) ? "unlike" : Strictly(ask, laid) ? "alike" : "alike, an unnamed type as any";

                        // AND WHICH PIECE of its file the tile on the slot's own cell is: (0, 0) there says the
                        // slot's corner is the tile's corner, which is what pins a placement to the tile and not
                        // merely into it; the placement is how the tile was laid, -1 where the rotation tables
                        // were not in the recording.
                        (int pieceX, int pieceY) = laidTiles.SubAt(x, y);
                        said.AppendLine(CultureInfo.InvariantCulture, $"    slot {column}, {line} {ask.Said} - laid {(laid is null ? Short(path) : LaidSaid(path, 0, identities, null))} piece {pieceX}, {pieceY} placement {laidTiles.PlacementAt(x, y)}: {verdict}");
                    }
                }
            }

            // THE TILE PLACING ITSELF, as the tool now runs it for a room the doodads could not place.
            if (grid.Tiles is { } tilesLaid && identity is not null)
            {
                RoomTilePlaces byTiles = RoomTileFinder.Find(layout, tilesLaid, identity);
                said.Append(CultureInfo.InvariantCulture, $"  tiles: {byTiles.Places.Count} places on its {byTiles.Anchor} slot, {byTiles.Anchors} anchor cells, {byTiles.Tried} candidates tried");
                said.AppendLine(byTiles.Why.Length > 0 ? " - " + byTiles.Why : string.Empty);
                foreach (RoomTilePlace place in byTiles.Places)
                {
                    said.AppendLine(CultureInfo.InvariantCulture, $"    tile {place.Where.X}, {place.Where.Y}, {RoomFinder.Said(place.Where.Turn)}: {place.Where.BigAgree} of {place.Where.Big} big slots, {place.Where.TilesAgree} of {place.Where.Tiles} slots alike, on {Short(place.Anchor)} laid {place.AnchorLaid}x at {place.AnchorX}, {place.AnchorY}{(place.AnchorTagged ? ", tagged" : string.Empty)}");
                }

                // WHY A LONELY LINE PICKED OR DID NOT: where each place puts the line, against the nearest
                // entity of the line's path - for the places whose footprint covers an entity's tile.
                float side = RoomDoodadFinder.WithinUnits;
                foreach (RoomDoodad line in layout.Doodads)
                {
                    if (line.Stub.Length == 0)
                    {
                        continue;
                    }

                    List<DoodadSighting> entities = [.. placing.Survey.Found.Where(one => string.Equals(one.Path, line.Stub, StringComparison.OrdinalIgnoreCase))];
                    if (entities.Count == 0)
                    {
                        continue;
                    }

                    said.AppendLine(CultureInfo.InvariantCulture, $"    line {Short(line.Stub)} at {line.X}, {line.Y} cells of the room: {entities.Count} entities - {string.Join("; ", entities.Select(one => string.Create(CultureInfo.InvariantCulture, $"{one.X:0}, {one.Y:0} (tile {one.X / side:0.0}, {one.Y / side:0.0})")))}");
                    foreach (RoomTilePlace place in byTiles.Places)
                    {
                        RoomCandidate at = place.Where;
                        bool covers = entities.Any(one => one.X / side >= at.X && one.X / side < at.X + at.Width && one.Y / side >= at.Y && one.Y / side < at.Y + at.Height);
                        if (!covers)
                        {
                            continue;
                        }

                        Matrix3x2 laying = RoomFinder.Laying(layout.Width, layout.Height, at.Turn);
                        Vector2 inRoom = Vector2.Transform(new Vector2(line.X, line.Y) / TerrainGrid.CellsPerTile, laying);
                        var where = new Vector2(at.X + inRoom.X, at.Y + inRoom.Y) * side;
                        float nearest = entities.Min(one => Vector2.Distance(new Vector2(one.X, one.Y), where));
                        said.AppendLine(CultureInfo.InvariantCulture, $"      place tile {at.X}, {at.Y}, {RoomFinder.Said(at.Turn)} covers an entity: the line stands at tile {where.X / side:0.0}, {where.Y / side:0.0}, the nearest entity {nearest:0} units off ({(nearest <= side ? "within a tile - confirms" : "too far")})");
                    }
                }
            }

            RoomSearch search = RoomFinder.Find(layout, ground, grid.TilesX, grid.TilesY, grid.Tiles, identity, walkable: walkable);
            if (search.Why.Length > 0)
            {
                said.Append("  no search - ").AppendLine(search.Why);
                continue;
            }

            said.Append(CultureInfo.InvariantCulture, $"  search: {search.Fits} places fit every corner, {search.Candidates.Count} listed by their {(search.TileChecked ? "tiles, then corners" : "corners")} - {search.Corners} corners in the stamp, {search.Free} free, {search.Left} big slots left out").AppendLine();

            // HOW ALONE THE BEST PLACE IS: every listed place scoring exactly as the first does.
            var ties = 0;
            if (search.Candidates.Count > 0)
            {
                RoomCandidate top = search.Candidates[0];
                while (ties < search.Candidates.Count && search.Candidates[ties].TilesAgree == top.TilesAgree && search.Candidates[ties].Matched == top.Matched)
                {
                    ties++;
                }
            }

            for (var rank = 0; rank < Math.Min(5, search.Candidates.Count); rank++)
            {
                said.Append("  ").AppendLine(Said(rank + 1, search.Candidates[rank], search.Misses[rank]));
            }

            if (ties > 1)
            {
                said.AppendLine(CultureInfo.InvariantCulture, $"  {ties} places tie at the top");
            }

            // WHERE THE BEST PLACE PARTS WITH THE AREA, slot by slot: which asks the tiles there do not answer.
            if (search.Candidates.Count > 0)
            {
                foreach (RoomPart part in search.Parts(search.Candidates[0]).Where(part => !part.IsCorner).Take(10))
                {
                    said.AppendLine(CultureInfo.InvariantCulture, $"    slot {part.RoomU}, {part.RoomV} at tile {part.X}, {part.Y} ({(part.Sides.Length > 0 ? part.Sides : "inside")}, {part.Join}): wants {part.Wanted} - laid {part.Laid}");
                }
            }

            if (places.Places.Count == 0)
            {
                continue;
            }

            // THE DOODAD PLACES BY THE SEARCH'S OWN YARDSTICK, ranked against its list.
            (RoomScorer? scorer, string why) = RoomFinder.Scorer(layout, ground, grid.TilesX, grid.TilesY, grid.Tiles, identity, walkable);
            foreach (RoomDoodadPlace place in places.Places)
            {
                RoomCandidate at = place.Where;
                RoomPlace? scored = scorer?.Score(at.X, at.Y, at.Turn);
                if (scored is null)
                {
                    said.AppendLine(CultureInfo.InvariantCulture, $"  doodads at tile {at.X}, {at.Y}, {RoomFinder.Said(at.Turn)}: not scored - {why}");
                    continue;
                }

                int above = 0, rank = -1;
                for (var listed = 0; listed < search.Candidates.Count; listed++)
                {
                    RoomCandidate candidate = search.Candidates[listed];
                    if (candidate.X == at.X && candidate.Y == at.Y && candidate.Turn == at.Turn)
                    {
                        rank = listed;
                    }

                    if (candidate.TilesAgree > scored.Where.TilesAgree || (candidate.TilesAgree == scored.Where.TilesAgree && candidate.Matched > scored.Where.Matched))
                    {
                        above++;
                    }
                }

                said.Append("  doodads at ").Append(Said(0, scored.Where, scored.Misses));
                if (rank >= 0)
                {
                    said.Append(CultureInfo.InvariantCulture, $" - rank {rank + 1}");
                }
                else
                {
                    said.Append(" - not among the listed");
                }

                said.AppendLine(CultureInfo.InvariantCulture, $", {above} listed places rank above it");
            }
        }

        // WHAT THE TOOL WOULD NOW WRITE to rooms-placed.txt: the doodads' places with the tiles' settled in.
        if (grid.Tiles is { } laidAll && identity is not null)
        {
            var tilePlaces = new Dictionary<string, RoomTilePlaces>(StringComparer.OrdinalIgnoreCase);
            List<(string Room, RoomLayout Layout, RoomDoodadPlaces Places)> settled = RoomTileFinder.Settled(
                placing.Placed,
                (room, layout) => tilePlaces.TryGetValue(room, out RoomTilePlaces? had) ? had : tilePlaces[room] = RoomTileFinder.Find(layout, laidAll, identity),
                placing.Survey.Found);
            said.AppendLine();
            said.AppendLine("=== rooms-placed.txt as the tool would now write it, the tiles' places settled in");
            said.AppendLine(CaptureReport.Lined(CaptureReport.Placed(settled)));
        }

        return said.ToString();
    }

    /// <summary>Measures which side of a laid tile meets which side of its slot, over every room the doodads placed, and writes the table beside the capture's files.</summary>
    [Fact]
    public void THEORIENTATIONOfATileOnItsSlotIsMeasuredFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = OrientationReplay(folder);
        File.WriteAllText(Path.Combine(folder, "orientation-replay.txt"), replay);
        Assert.StartsWith("terrain ", replay, StringComparison.Ordinal);
    }

    /// <summary>The four sides in the files' order - down, right, up, left - as directions in a square's own axes, down being minus y.</summary>
    private static readonly (int X, int Y)[] SideDirections = [(0, -1), (1, 0), (0, 1), (-1, 0)];

    /// <summary>The four corners in the files' order - down-left, down-right, up-right, up-left - likewise.</summary>
    private static readonly (int X, int Y)[] CornerDirections = [(-1, -1), (1, -1), (1, 1), (-1, 1)];

    /// <summary>The eight ways a square's own axes may be read: four quarter turns, each as it is and with x flipped first - a convention is one of these.</summary>
    private static readonly (int Xx, int Xy, int Yx, int Yy)[] Dihedral =
    [
        (1, 0, 0, 1), (0, -1, 1, 0), (-1, 0, 0, -1), (0, 1, -1, 0),
        (-1, 0, 0, 1), (0, -1, -1, 0), (1, 0, 0, -1), (0, 1, 1, 0),
    ];

    /// <summary>
    /// orientation-replay.txt: for every slot of every room the doodads placed, the slot's named edge and ground types side by side with the laid tile's, under each of the 64 conventions a slot's sides and a tile's sides could be read by - and which convention the area agrees with.
    /// </summary>
    /// <remarks>
    /// WHY IT IS MEASURED AND NOT ASSUMED. The room search compares a slot's types with a tile's as SETS,
    /// because which side of a turned tile meets which side of a turned room was never settled - and a
    /// room built from the tileset without the area has to turn each tile it picks the right way, or a
    /// border runs the wrong way round the arena. A capture holds both halves at once: the room's place
    /// and turn (its doodads found it), the slot grid with each side's type, and under every slot the tile
    /// the game laid with the placement it laid it at. So every reading of the two conventions is tried
    /// and counted: a room side is one of the four directions in the room's own axes turned one of eight
    /// ways and then laid as the room was (RoomFinder.Laying); a tile side is one of the four directions in
    /// the template's axes turned one of eight ways and then placed as the tile was (TileOrientation.Turn).
    /// Where the two directions agree the two types must, if the slot names one. The convention is the
    /// pair under which they always do.
    /// </remarks>
    public static string OrientationReplay(string folder)
    {
        var said = new StringBuilder();
        TerrainGrid? grid = Terrain(folder, out string how);
        said.AppendLine(how);
        if (grid?.Tiles is not { } laid)
        {
            said.AppendLine("nothing to measure without the terrain's tiles");
            return said.ToString();
        }

        Dictionary<string, TileIdentity>? identities = Identities(folder);
        if (identities is null)
        {
            said.AppendLine($"nothing to measure without {CaptureReport.TileIdentitiesFile}");
            return said.ToString();
        }

        Placing placing = Place(folder);

        // EVERY SLOT WITH A WHOLE TILE ON IT: the slot's corner carrying the tile's first piece, the tile
        // the slot's size either way round, and its placement decoded.
        var samples = new List<(string Room, int Column, int Line, int Turn, string[] SlotEdges, string[] SlotGrounds, string Tile, int Placement, TileIdentity Laid)>();
        for (var one = 0; one < placing.Rooms.Count; one++)
        {
            (string room, RoomLayout layout) = placing.Rooms[one];
            foreach (RoomDoodadPlace place in placing.Placed[one].Places.Places)
            {
                RoomCandidate at = place.Where;
                for (var line = 0; line < layout.Height; line++)
                {
                    for (var column = 0; column < layout.Width; column++)
                    {
                        RoomSlot slot = layout.SlotAt(column, line);
                        if (!slot.IsTile)
                        {
                            continue;
                        }

                        (int left, int top) = slot.Corner(column, line);
                        (int cellX, int cellY) = RoomFinder.CornerOf(left, top, slot.Width, slot.Height, layout.Width, layout.Height, at.Turn);
                        int x = at.X + cellX, y = at.Y + cellY;
                        if (laid.SubAt(x, y) is not (0, 0) || laid.PlacementAt(x, y) < 0
                            || !identities.TryGetValue(laid.PathAt(x, y), out TileIdentity? tile)
                            || !((tile.Width == slot.Width && tile.Height == slot.Height) || (tile.Width == slot.Height && tile.Height == slot.Width)))
                        {
                            continue;
                        }

                        string[] edges = [layout.Named(slot.Edge(0)), layout.Named(slot.Edge(1)), layout.Named(slot.Edge(2)), layout.Named(slot.Edge(3))];
                        string[] grounds = [layout.Named(slot.Ground(0)), layout.Named(slot.Ground(1)), layout.Named(slot.Ground(2)), layout.Named(slot.Ground(3))];
                        samples.Add((Short(room), column, line, at.Turn, edges, grounds, laid.PathAt(x, y), laid.PlacementAt(x, y), tile));
                    }
                }
            }
        }

        said.AppendLine(CultureInfo.InvariantCulture, $"{samples.Count} slots with a whole tile on them, over {samples.Select(one => one.Room).Distinct().Count()} rooms the doodads placed; room turns {string.Join(", ", samples.Select(one => one.Turn).Distinct().Order())}, tile placements {string.Join(", ", samples.Select(one => one.Placement).Distinct().Order())}");
        if (samples.Count == 0)
        {
            return said.ToString();
        }

        // EVERY PAIR OF CONVENTIONS, counted: named slot sides agreeing and disagreeing with the tile side
        // that lies the same way, and the corners likewise.
        var scores = new List<(int RoomWay, int TileWay, int EdgesAgree, int EdgesDisagree, int CornersAgree, int CornersDisagree)>();
        for (var roomWay = 0; roomWay < 8; roomWay++)
        {
            for (var tileWay = 0; tileWay < 8; tileWay++)
            {
                int ea = 0, ed = 0, ca = 0, cd = 0;
                foreach (var sample in samples)
                {
                    Count(sample, roomWay, tileWay, SideDirections, sample.SlotEdges, sample.Laid.Edges, ref ea, ref ed);
                    Count(sample, roomWay, tileWay, CornerDirections, sample.SlotGrounds, sample.Laid.Grounds, ref ca, ref cd);
                }

                scores.Add((roomWay, tileWay, ea, ed, ca, cd));
            }
        }

        said.AppendLine();
        said.AppendLine("=== the 64 pairs of conventions, best first: room sides read one of eight ways (0 as the files have them: down is minus v, right plus u; 1-3 quarter turns; 4-7 the same with u flipped first), tile sides likewise in the template's axes");
        foreach (var score in scores.OrderByDescending(one => one.EdgesAgree + one.CornersAgree - (3 * (one.EdgesDisagree + one.CornersDisagree))).Take(12))
        {
            said.AppendLine(CultureInfo.InvariantCulture, $"  room way {score.RoomWay}, tile way {score.TileWay}: edges {score.EdgesAgree} agree, {score.EdgesDisagree} disagree · corners {score.CornersAgree} agree, {score.CornersDisagree} disagree");
        }

        // THE BEST PAIR'S DISAGREEMENTS, each one printed whole, so a rule that nearly holds shows where it does not.
        var best = scores.OrderByDescending(one => one.EdgesAgree + one.CornersAgree - (3 * (one.EdgesDisagree + one.CornersDisagree))).First();
        said.AppendLine();
        said.AppendLine(CultureInfo.InvariantCulture, $"=== under room way {best.RoomWay}, tile way {best.TileWay}: every slot, the slot's types beside the tile's on the side that lies the same way");
        foreach (var sample in samples)
        {
            said.Append(CultureInfo.InvariantCulture, $"  {sample.Room} slot {sample.Column}, {sample.Line} ({RoomFinder.Said(sample.Turn)}) on {Short(sample.Tile)} placement {sample.Placement} ({TileOrientation.OfPlacement(sample.Placement)}):");
            Beside(said, sample, best.RoomWay, best.TileWay, SideDirections, sample.SlotEdges, sample.Laid.Edges, ["down", "right", "up", "left"]);
            Beside(said, sample, best.RoomWay, best.TileWay, CornerDirections, sample.SlotGrounds, sample.Laid.Grounds, ["down-left", "down-right", "up-right", "up-left"]);
            said.AppendLine();
        }

        return said.ToString();
    }

    /// <summary>Builds every room the doodads placed from the capture's tileset and holds it, cell by cell, against the room the game laid there; writes the table beside the capture's files.</summary>
    [Fact]
    public void THEBUILDIsHeldAgainstTheLaidRoomFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = BuildReplay(folder);
        File.WriteAllText(Path.Combine(folder, "build-replay.txt"), replay);
        Assert.StartsWith("terrain ", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// build-replay.txt: each room the doodads placed, built from the tileset's tiles as slot-candidates.txt lists them (every tile with its identity; weights from tileset-lists.txt where the capture has it, else none), beside the tiles the game laid under the room's place - per slot whether the same file was chosen and whether it lies the same way.
    /// </summary>
    /// <remarks>
    /// WHAT AGREEMENT MEANS HERE. A slot several tiles answer is the generator's choice, and the build
    /// chooses by weight; a different file there is not a fault, and the count says how many such slots
    /// there are. A slot ONE tile answers must come out the game's file, and every slot whose file agrees
    /// must come out laid the game's way once the room's own turn is taken off - that is the orientation
    /// rule (RoomBuilder.Placement) held against the area a second time, through the build itself.
    /// </remarks>
    public static string BuildReplay(string folder)
    {
        var said = new StringBuilder();
        TerrainGrid? grid = Terrain(folder, out string how);
        said.AppendLine(how);
        if (grid?.Tiles is not { } laid)
        {
            said.AppendLine("nothing to hold the build against without the terrain's tiles");
            return said.ToString();
        }

        List<RoomBuildTile>? pool = Pool(folder, out string poolHow);
        said.AppendLine(poolHow);
        if (pool is null)
        {
            return said.ToString();
        }

        Placing placing = Place(folder);
        int rooms = 0, slots = 0, sameFile = 0, sameWay = 0, otherWay = 0, settledSame = 0, settled = 0, chosen = 0, unfilled = 0;
        var byPath = pool.ToDictionary(tile => tile.Path, StringComparer.OrdinalIgnoreCase);
        var body = new StringBuilder();
        for (var one = 0; one < placing.Rooms.Count; one++)
        {
            (string room, RoomLayout layout) = placing.Rooms[one];
            if (placing.Placed[one].Places.Places.Count == 0)
            {
                continue;
            }

            RoomBuild build = RoomBuilder.Build(layout, pool);
            rooms++;
            body.AppendLine();
            body.Append("=== ").Append(Short(room)).Append(": ").AppendLine(build.Said);
            if (build.Tiles is null)
            {
                continue;
            }

            foreach (RoomDoodadPlace place in placing.Placed[one].Places.Places)
            {
                RoomCandidate at = place.Where;
                body.AppendLine(CultureInfo.InvariantCulture, $"  against the room laid at tile {at.X}, {at.Y}, {RoomFinder.Said(at.Turn)}:");
                foreach (RoomBuildSlot slot in build.Slots)
                {
                    slots++;
                    (int cellX, int cellY) = RoomFinder.CornerOf(slot.Left, slot.Top, slot.Width, slot.Height, layout.Width, layout.Height, at.Turn);
                    int x = at.X + cellX, y = at.Y + cellY;
                    string laidPath = laid.PathAt(x, y);
                    int laidPlacement = laid.PlacementAt(x, y);
                    if (slot.Tile.Length == 0)
                    {
                        unfilled++;
                        body.AppendLine(CultureInfo.InvariantCulture, $"    slot {slot.Column}, {slot.Line} {slot.Said}: NO TILE ALIKE - the game laid {Short(laidPath)}");
                        continue;
                    }

                    if (slot.Candidates == 1)
                    {
                        settled++;
                    }
                    else
                    {
                        chosen++;
                    }

                    bool file = string.Equals(slot.Tile, laidPath, StringComparison.OrdinalIgnoreCase);
                    int expected = Composed(at.Turn, slot.Placement);
                    bool way = file && expected == laidPlacement;

                    // THE GAME'S WAY, taken back into the room, held against the slot's sides: a slot whose named
                    // sides several ways meet is the generator's choice of way as much as a slot several tiles
                    // answer is its choice of file - only a way meeting fewer sides than the build's is a fault.
                    int gameWay = -1;
                    for (var candidate = 0; candidate < 8 && laidPlacement >= 0; candidate++)
                    {
                        if (Composed(at.Turn, candidate) == laidPlacement)
                        {
                            gameWay = candidate;
                            break;
                        }
                    }

                    int gameFits = file && gameWay >= 0 ? RoomBuilder.FitsAt(layout, layout.SlotAt(slot.Column, slot.Line), byPath[slot.Tile].Identity, gameWay) : -1;
                    bool allowed = file && !way && gameFits >= slot.Fits;
                    sameFile += file ? 1 : 0;
                    sameWay += way ? 1 : 0;
                    otherWay += allowed ? 1 : 0;
                    settledSame += slot.Candidates == 1 && file ? 1 : 0;
                    string verdict = !file
                        ? (slot.Candidates == 1 ? "SETTLED BUT DIFFERENT" : $"another of {slot.Candidates} valid")
                        : way ? "same file, same way"
                        : allowed ? $"same file, another way the sides allow (laid {laidPlacement}, {gameWay} in the room, meeting {gameFits}/{slot.Named})"
                        : $"same file, LAID placement {laidPlacement} ({gameWay} in the room, meeting {gameFits}/{slot.Named}) where the build's {slot.Placement} under the room's turn gives {expected}";
                    body.AppendLine(CultureInfo.InvariantCulture, $"    slot {slot.Column}, {slot.Line} ({slot.Width}x{slot.Height}, {slot.Candidates} alike, fits {slot.Fits}/{slot.Named}): built {Short(slot.Tile)} placement {slot.Placement} - laid {Short(laidPath)} placement {laidPlacement}: {verdict}");
                }
            }
        }

        said.AppendLine();
        said.AppendLine(CultureInfo.InvariantCulture,
            $"{rooms} rooms built and held against their places: {slots} slots, {settled} settled by one tile ({settledSame} the game's file), {chosen} chosen among several ({sameFile - settledSame} the game's file), {unfilled} with no tile alike; of the {sameFile} slots with the game's file, {sameWay} lie the game's way and {otherWay} another way the slot's sides allow as well");
        said.Append(body);
        return said.ToString();
    }

    /// <summary>
    /// The placement a tile built as written comes out at once the room is laid turned: the room's turn after the tile's placement, as one of the eight - or -1 where the two do not compose to one.
    /// </summary>
    private static int Composed(int roomTurn, int placement)
    {
        if (placement < 0)
        {
            return -1;
        }

        Matrix3x2 laying = RoomFinder.Laying(1, 1, roomTurn);
        int l11 = (int)MathF.Round(laying.M11), l21 = (int)MathF.Round(laying.M21), l12 = (int)MathF.Round(laying.M12), l22 = (int)MathF.Round(laying.M22);
        (int xx, int xy, int yx, int yy) = TileOrientation.OfPlacement(placement).Turn;

        // A direction goes through the tile's placement first (template to room) and the room's laying after
        // (room to area): world = L * T, with L read off Laying's linear part as it acts on a direction.
        int cxx = (l11 * xx) + (l21 * yx), cxy = (l11 * xy) + (l21 * yy);
        int cyx = (l12 * xx) + (l22 * yx), cyy = (l12 * xy) + (l22 * yy);
        for (var candidate = 0; candidate < 8; candidate++)
        {
            if (TileOrientation.OfPlacement(candidate).Turn == (cxx, cxy, cyx, cyy))
            {
                return candidate;
            }
        }

        return -1;
    }

    /// <summary>The tileset's tiles with their identities out of slot-candidates.txt's table, weighted from tileset-lists.txt where the capture has it - or null and why.</summary>
    private static List<RoomBuildTile>? Pool(string folder, out string how)
    {
        string candidates = Path.Combine(folder, CaptureReport.SlotCandidatesFile);
        if (!File.Exists(candidates))
        {
            how = $"no {CaptureReport.SlotCandidatesFile} in the capture - a capture from 0.1.176 on carries the tileset's tiles";
            return null;
        }

        var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var words = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        string lists = Path.Combine(folder, CaptureReport.TilesetListsFile);
        if (File.Exists(lists))
        {
            foreach (TileListEntry entry in TileList.Parse(File.ReadAllText(lists)).Entries)
            {
                string tile = entry.Tile.Replace('\\', '/').Trim();
                if (entry.Weight is { } weight && weight > weights.GetValueOrDefault(tile))
                {
                    weights[tile] = weight;
                    words[tile] = entry.Words;
                }
            }
        }

        var pool = new List<RoomBuildTile>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var table = false;
        var unread = 0;
        foreach (string raw in File.ReadLines(candidates))
        {
            if (raw.StartsWith("##### the tilesets' tiles", StringComparison.Ordinal))
            {
                table = true;
                continue;
            }

            if (!table)
            {
                continue;
            }

            string[] cells = raw.TrimEnd('\r').Split('\t');
            if (cells.Length < 12
                || !int.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(cells[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
            {
                unread += cells.Length >= 2 && cells[1].StartsWith("(not read", StringComparison.Ordinal) ? 1 : 0;
                continue;
            }

            string path = cells[0];
            if (seen.Add(path))
            {
                pool.Add(new RoomBuildTile(path, new TileIdentity(width, height, cells[3], cells[4..8], cells[8..12]), weights.GetValueOrDefault(path), words.GetValueOrDefault(path) ?? []));
            }
        }

        how = string.Create(CultureInfo.InvariantCulture, $"{pool.Count} tiles in the pool from {CaptureReport.SlotCandidatesFile}{(unread > 0 ? $", {unread} did not read" : string.Empty)}, {weights.Count} weighted from {CaptureReport.TilesetListsFile}{(File.Exists(lists) ? string.Empty : " (not in the capture - the first of equals is taken)")}");
        return pool.Count > 0 ? pool : null;
    }

    /// <summary>Measures where a big slot's record sits in the footprint its tile actually covers, by the slot's trailing number; writes the table beside the capture's files.</summary>
    [Fact]
    public void THEORIGINOfABigSlotIsMeasuredFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = OriginReplay(folder);
        File.WriteAllText(Path.Combine(folder, "origin-replay.txt"), replay);
        Assert.StartsWith("terrain ", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// origin-replay.txt: for every slot bigger than one tile in every room the doodads placed, the record's column and line, its size and its trailing number, beside the rectangle in the room's own coordinates that the laid piece under the record's cell covers - so which corner of its footprint a record sits at can be read off against that number.
    /// </summary>
    /// <remarks>
    /// WHY. Oasis's boss room records its 21 by 15 arena slot at column 22, line 13 of a 25 by 25 grid,
    /// where a footprint running right and down from the record cannot lie - and its k lines carry a
    /// 24th number the reader has always stepped over (the remark's "[origin]"). The laid tile under
    /// the record, mapped back through the room's own laying, says what the record is a corner OF.
    /// </remarks>
    public static string OriginReplay(string folder)
    {
        var said = new StringBuilder();
        TerrainGrid? grid = Terrain(folder, out string how);
        said.AppendLine(how);
        if (grid?.Tiles is not { } laid)
        {
            said.AppendLine("nothing to measure without the terrain's tiles");
            return said.ToString();
        }

        Dictionary<string, TileIdentity>? identities = Identities(folder);
        Placing placing = Place(folder);
        (int Width, int Height) Size(int id)
            => identities is not null && id >= 0 && id < laid.Paths.Count && identities.TryGetValue(laid.Paths[id], out TileIdentity? tile) ? (tile.Width, tile.Height) : (1, 1);

        said.AppendLine("record column, line; size w x h; the 24th number ('-' where the line has 23); then the laid piece under the record's own cell as the room's coordinates hold it: columns u0..u1, lines v0..v1, its file and placement");
        for (var one = 0; one < placing.Rooms.Count; one++)
        {
            (string room, RoomLayout layout) = placing.Rooms[one];
            foreach (RoomDoodadPlace place in placing.Placed[one].Places.Places)
            {
                RoomCandidate at = place.Where;
                said.AppendLine();
                said.AppendLine(CultureInfo.InvariantCulture, $"=== {Short(room)}, {layout.Width} x {layout.Height}, laid at tile {at.X}, {at.Y}, {RoomFinder.Said(at.Turn)}");
                Matrix3x2 laying = RoomFinder.Laying(layout.Width, layout.Height, at.Turn);
                if (!Matrix3x2.Invert(laying, out Matrix3x2 back))
                {
                    continue;
                }

                for (var line = 0; line < layout.Height; line++)
                {
                    for (var column = 0; column < layout.Width; column++)
                    {
                        RoomSlot slot = layout.SlotAt(column, line);
                        if (!slot.IsTile || (slot.Width == 1 && slot.Height == 1))
                        {
                            continue;
                        }

                        (int cellX, int cellY) = RoomFinder.CellOf(column, line, layout.Width, layout.Height, at.Turn);
                        int x = at.X + cellX, y = at.Y + cellY;
                        List<TilePiece> pieces = laid.Pieces(x, y, 1, 1, Size);
                        string origin = slot.Numbers.Length > 23 ? slot.Numbers[23].ToString(CultureInfo.InvariantCulture) : "-";
                        if (pieces.Count == 0)
                        {
                            said.AppendLine(CultureInfo.InvariantCulture, $"  record {column}, {line}  {slot.Width}x{slot.Height}  origin {origin}: no tile under its cell {x}, {y}");
                            continue;
                        }

                        TilePiece piece = pieces[0];

                        // THE PIECE'S CORNERS BACK IN THE ROOM: cell centres through the inverse laying, in tiles from the footprint's corner.
                        Vector2 a = Vector2.Transform(new Vector2(piece.MinX - at.X + 0.5f, piece.MinY - at.Y + 0.5f), back);
                        Vector2 b = Vector2.Transform(new Vector2(piece.MaxX - at.X + 0.5f, piece.MaxY - at.Y + 0.5f), back);
                        int u0 = (int)MathF.Floor(MathF.Min(a.X, b.X)), u1 = (int)MathF.Floor(MathF.Max(a.X, b.X));
                        int v0 = (int)MathF.Floor(MathF.Min(a.Y, b.Y)), v1 = (int)MathF.Floor(MathF.Max(a.Y, b.Y));

                        // A PIECE NOT THE SLOT'S SIZE READS NOTHING: Steppe fills three by three slots with one by
                        // one Blank tiles, and a one by one piece always reads as the record's first column and line.
                        bool sized = u1 - u0 + 1 == slot.Width && v1 - v0 + 1 == slot.Height;
                        string reading = sized
                            ? $"record is {(column == u0 ? "left" : column == u1 ? "right" : "inside")}-{(line == v0 ? "first" : line == v1 ? "last" : "inside")}"
                            : "the piece is not the slot's size - no reading";
                        said.AppendLine(CultureInfo.InvariantCulture,
                            $"  record {column}, {line}  {slot.Width}x{slot.Height}  origin {origin}: piece columns {u0}..{u1}, lines {v0}..{v1} ({u1 - u0 + 1}x{v1 - v0 + 1}{(piece.Whole ? string.Empty : ", not whole")}) {Short(laid.Paths[piece.Id])} placement {piece.Placement}; {reading}");
                    }
                }
            }
        }

        return said.ToString();
    }

    /// <summary>Estimates the weight a tile list gives a tile it writes no weight for, from what the game laid in one capture or several; writes the table beside the first capture's files.</summary>
    [Fact]
    public void THEWEIGHTOfAnUnweightedTileIsMeasuredFromCaptures()
    {
        string[] folders = Folders();
        if (folders.Length == 0)
        {
            return;
        }

        string replay = WeightReplay(folders);
        File.WriteAllText(Path.Combine(folders[0], "weight-replay.txt"), replay);
        Assert.Contains("samples", replay, StringComparison.Ordinal);
    }

    /// <summary>Holds every laid file no tile list names against the listed files' chains and .tmd files, from a capture's tile-files.txt; writes the table beside the capture's files.</summary>
    [Fact]
    public void THETILEFILESExplainTheLaidFilesNoListNamesFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = TileFilesReplay(folder);
        File.WriteAllText(Path.Combine(folder, "tile-files-replay.txt"), replay);
        Assert.StartsWith("tile files", replay, StringComparison.Ordinal);
    }

    /// <summary>A tile-files.txt and an area-masters.txt written and read back: each laid file no tile list names is explained the way the area is built.</summary>
    [Fact]
    public void THETILEFILESReplayReadsWhatTheCaptureWrites()
    {
        string folder = Path.Combine(Path.GetTempPath(), $"poeformance-tile-files-{Guid.NewGuid():N}");
        Directory.CreateDirectory(folder);
        try
        {
            string[] loaded = ["Metadata/Terrain/Maps/Test/master.tsi"];
            File.WriteAllText(
                Path.Combine(folder, CaptureReport.TileFilesFile),
                CaptureReport.TileFiles(CaptureReportTests.TileFilesInstall, loaded, null, CaptureReportTests.TileFilesLaid()));
            File.WriteAllText(Path.Combine(folder, CaptureReport.MastersFile), CaptureReport.Masters(CaptureReportTests.TileFilesInstall, loaded, null));

            string replay = TileFilesReplay(folder).ReplaceLineEndings("\n");
            Assert.Contains("4 laid files no tile list names: 1 reached by inheriting, 1 in a fill list, 1 sharing a template with a listed file, 1 not explained", replay, StringComparison.Ordinal);
            Assert.Contains("Fill_01: inherited - Metadata/Terrain/Test/Murky/Fill_01.tdt lies as Metadata/Terrain/Test/Fill_01.tdt", replay, StringComparison.Ordinal);
            Assert.Contains("Fill_02: in a fill list", replay, StringComparison.Ordinal);
            Assert.Contains("Passage: shares a template with Metadata/Terrain/Test/Features/Passage_feature.tdt", replay, StringComparison.Ordinal);
            Assert.Contains("Alone: NOT EXPLAINED - Metadata/Terrain/Test/Alone.tdt", replay, StringComparison.Ordinal);
            Assert.Contains("listed files: 3, 1 inheriting, 2 with a doodad list (.tmd)", replay, StringComparison.Ordinal);
            Assert.Contains("fill list Metadata/Terrain/Maps/Test/fills.gft: version 2, 1 sections, 1 tiles (1 laid), 1 rooms", replay, StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }

    /// <summary>
    /// tile-files-replay.txt: every laid file no tile list names, and what explains it - a listed file whose
    /// chain passes through it, a fill list naming it, or a listed file with the same template - read back from
    /// tile-files.txt, and the fill lists read back from area-masters.txt with the tool's own parser
    /// (FillList), so a changed reading of any of them runs again over a capture without the game.
    /// </summary>
    /// <remarks>
    /// THE ORDER IS THE EVIDENCE'S, strongest first: a chain is the file saying what it is, a fill list is the
    /// master naming the file, and a shared template is only the same geometry. A .tmd explains nothing here -
    /// it is the tile's own doodad list (Marrow: 'version 8', a line a doodad) - and is only counted.
    /// </remarks>
    public static string TileFilesReplay(string folder)
    {
        var said = new StringBuilder();
        string path = Path.Combine(folder, CaptureReport.TileFilesFile);
        if (!File.Exists(path))
        {
            said.AppendLine(CultureInfo.InvariantCulture, $"tile files: no {CaptureReport.TileFilesFile} in the capture - a capture from 0.1.183 on carries it");
            return said.ToString();
        }

        // THE BLOCKS: each file's flags, its chain (itself first), the templates it ends at, and whether it carries a doodad list.
        var listed = new List<string>();
        var fill = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var laid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chains = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var templates = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        var doodads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string current = string.Empty;
        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("##### ", StringComparison.Ordinal))
            {
                int flags = line.LastIndexOf("  (", StringComparison.Ordinal);
                current = line[6..flags];
                string[] kinds = line[(flags + 3)..^1].Split(", ");
                if (kinds.Contains("listed"))
                {
                    listed.Add(current);
                }

                if (kinds.Contains("fill"))
                {
                    fill.Add(current);
                }

                if (kinds.Contains("laid"))
                {
                    laid.Add(current);
                }

                chains[current] = [current];
                templates[current] = [];
            }
            else if (current.Length > 0 && line.StartsWith("  inherits\t", StringComparison.Ordinal))
            {
                chains[current].Add(line[11..]);
            }
            else if (current.Length > 0 && line.StartsWith("  defines\t", StringComparison.Ordinal))
            {
                string shapes = line.Split('\t')[^1];
                templates[current] = shapes.StartsWith("templates ", StringComparison.Ordinal)
                    ? shapes[10..].Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    : [];
            }
            else if (current.Length > 0 && line.StartsWith("  tmd\t", StringComparison.Ordinal) && !line.EndsWith("\tnone", StringComparison.Ordinal))
            {
                doodads.Add(current);
            }
        }

        var listedSet = new HashSet<string>(listed, StringComparer.OrdinalIgnoreCase);
        List<string> unlisted = [.. laid.Where(one => !listedSet.Contains(one)).Order(StringComparer.OrdinalIgnoreCase)];
        int inherited = 0, filled = 0, shaped = 0, unexplained = 0;
        var lines = new StringBuilder();
        foreach (string file in unlisted)
        {
            List<string> heirs = [.. listed.Where(one => chains[one].Skip(1).Contains(file, StringComparer.OrdinalIgnoreCase))];
            List<string> twins = [.. listed.Where(one => templates[one].Intersect(templates[file], StringComparer.OrdinalIgnoreCase).Any())];
            if (heirs.Count > 0)
            {
                inherited++;
                lines.AppendLine(CultureInfo.InvariantCulture, $"  {Short(file)}: inherited - {string.Join(", ", heirs)} lies as {file}");
            }
            else if (fill.Contains(file))
            {
                filled++;
                lines.AppendLine(CultureInfo.InvariantCulture, $"  {Short(file)}: in a fill list - {file}");
            }
            else if (twins.Count > 0)
            {
                shaped++;
                lines.AppendLine(CultureInfo.InvariantCulture, $"  {Short(file)}: shares a template with {string.Join(", ", twins)} - {file}");
            }
            else
            {
                unexplained++;
                lines.AppendLine(CultureInfo.InvariantCulture, $"  {Short(file)}: NOT EXPLAINED - {file}");
            }
        }

        said.AppendLine(CultureInfo.InvariantCulture,
            $"tile files: {unlisted.Count} laid files no tile list names: {inherited} reached by inheriting, {filled} in a fill list, {shaped} sharing a template with a listed file, {unexplained} not explained");
        said.AppendLine(CultureInfo.InvariantCulture,
            $"listed files: {listed.Count}, {listed.Count(one => chains[one].Count > 1)} inheriting, {listed.Count(doodads.Contains)} with a doodad list (.tmd)");
        FillLists(said, folder, laid);
        said.AppendLine();
        said.Append(lines);
        return said.ToString();
    }

    /// <summary>Holds every laid tile whose list line carries words against the way the game laid it; writes the table beside the capture's files.</summary>
    [Fact]
    public void THEWORDSAreHeldAgainstTheLaidTilesFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = WordsReplay(folder);
        File.WriteAllText(Path.Combine(folder, "words-replay.txt"), replay);
        Assert.StartsWith("words:", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// words-replay.txt: every tile a tile list or fill list writes words after (I, R90, R180, R270 in Marrow's), and every
    /// time the area laid it - the placement in the area, and where a placed room holds it, the way it lies in the room.
    /// </summary>
    /// <remarks>
    /// WHY MEASURE FIRST. poe_data_tools reads the words as strings it calls rotations and says nothing of what they do;
    /// Marrow lists its cave entrance once with I and once with R180 and its boss arena with I. Whether a word fixes the
    /// tile's way in the AREA or in its ROOM, or only picks which file stands for which turn, the two placements side by
    /// side say - which is why both halves are printed and nothing is concluded here. The build obeys the words once a
    /// capture with a worded tile laid shows which.
    /// </remarks>
    public static string WordsReplay(string folder)
    {
        var said = new StringBuilder();
        var words = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        void Word(string tile, IReadOnlyList<string> written, string from)
        {
            if (written.Count > 0)
            {
                (words.TryGetValue(tile, out List<string>? known) ? known : words[tile] = []).Add($"{string.Join(' ', written)} ({from})");
            }
        }

        string lists = Path.Combine(folder, CaptureReport.TilesetListsFile);
        if (File.Exists(lists))
        {
            foreach (TileListEntry entry in TileList.Parse(File.ReadAllText(lists)).Entries)
            {
                Word(entry.Tile, entry.Words, "tile list");
            }
        }

        foreach (FillListEntry entry in FillEntries(folder))
        {
            Word(entry.Path, entry.Words, $"fill list, section {entry.Section}");
        }

        TerrainGrid? grid = Terrain(folder, out string how);
        said.AppendLine(CultureInfo.InvariantCulture, $"words: {words.Count} tile files carry words in the area's lists; {how}");
        if (words.Count == 0 || grid?.Tiles is not { } laid)
        {
            said.AppendLine(words.Count == 0 ? "no tile line carries a word" : "nothing to hold them against without the terrain's tiles");
            return said.ToString();
        }

        // EVERY LAID TILE ONCE, at its first piece: the placement the area gives it.
        var inArea = new Dictionary<string, List<(int X, int Y, int Placement)>>(StringComparer.OrdinalIgnoreCase);
        for (var y = 0; y < laid.Height; y++)
        {
            for (var x = 0; x < laid.Width; x++)
            {
                string path = laid.PathAt(x, y);
                if (path.Length > 0 && words.ContainsKey(path) && laid.SubAt(x, y) is (0, 0))
                {
                    (inArea.TryGetValue(path, out List<(int, int, int)>? found) ? found : inArea[path] = []).Add((x, y, laid.PlacementAt(x, y)));
                }
            }
        }

        // AND WHERE A PLACED ROOM HOLDS IT: the room's turn and the way the tile lies in the room, as the build replay reads it.
        var inRoom = new Dictionary<(int X, int Y), List<string>>();
        Placing placing = Place(folder);
        for (var one = 0; one < placing.Rooms.Count; one++)
        {
            (string room, RoomLayout layout) = placing.Rooms[one];
            foreach (RoomDoodadPlace place in placing.Placed[one].Places.Places)
            {
                RoomCandidate at = place.Where;
                for (var line = 0; line < layout.Height; line++)
                {
                    for (var column = 0; column < layout.Width; column++)
                    {
                        RoomSlot slot = layout.SlotAt(column, line);
                        if (!slot.IsTile)
                        {
                            continue;
                        }

                        (int left, int top) = slot.Corner(column, line);
                        (int cellX, int cellY) = RoomFinder.CornerOf(left, top, slot.Width, slot.Height, layout.Width, layout.Height, at.Turn);
                        (int x, int y) = (at.X + cellX, at.Y + cellY);
                        if (!words.ContainsKey(laid.PathAt(x, y)) || laid.SubAt(x, y) is not (0, 0))
                        {
                            continue;
                        }

                        int placement = laid.PlacementAt(x, y);
                        int way = Enumerable.Range(0, 8).FirstOrDefault(candidate => Composed(at.Turn, candidate) == placement, -1);
                        (inRoom.TryGetValue((x, y), out List<string>? rooms) ? rooms : inRoom[(x, y)] = [])
                            .Add(string.Create(CultureInfo.InvariantCulture, $"{Short(room)} slot {column}, {line}, room {RoomFinder.Said(at.Turn)}: in the room {way}"));
                    }
                }
            }
        }

        int laidFiles = inArea.Count, instances = inArea.Values.Sum(found => found.Count);
        said.AppendLine(CultureInfo.InvariantCulture, $"{laidFiles} of them laid, {instances} times; {inRoom.Count} of those inside a room the doodads placed");
        said.AppendLine();
        foreach ((string tile, List<string> written) in words.OrderBy(one => one.Key, StringComparer.OrdinalIgnoreCase))
        {
            said.AppendLine(CultureInfo.InvariantCulture, $"{tile}: {string.Join("; ", written)}");
            if (!inArea.TryGetValue(tile, out List<(int X, int Y, int Placement)>? found))
            {
                said.AppendLine("  not laid");
                continue;
            }

            said.AppendLine(CultureInfo.InvariantCulture, $"  laid {found.Count}x, placements in the area: {string.Join(", ", found.GroupBy(one => one.Placement).OrderBy(group => group.Key).Select(group => $"{group.Key} x{group.Count()}"))}");
            foreach ((int x, int y, int placement) in found)
            {
                string rooms = inRoom.TryGetValue((x, y), out List<string>? held) ? string.Join("; ", held) : "no placed room holds it";
                said.AppendLine(CultureInfo.InvariantCulture, $"    tile {x}, {y}: placement {placement} - {rooms}");
            }
        }

        return said.ToString();
    }

    /// <summary>Every line of the fill lists area-masters.txt carries, read back with FillList - none where the capture has no such file.</summary>
    private static List<FillListEntry> FillEntries(string folder)
    {
        var entries = new List<FillListEntry>();
        string path = Path.Combine(folder, CaptureReport.MastersFile);
        if (!File.Exists(path))
        {
            return entries;
        }

        string current = string.Empty;
        var text = new StringBuilder();
        foreach (string raw in File.ReadLines(path).Append("##### "))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("##### ", StringComparison.Ordinal))
            {
                if (current.EndsWith(FillList.Extension, StringComparison.OrdinalIgnoreCase))
                {
                    entries.AddRange(FillList.Parse(text.ToString()).Entries);
                }

                int named = line.IndexOf("  (", StringComparison.Ordinal);
                current = named > 6 ? line[6..named] : line[6..];
                text.Clear();
            }
            else if (line.StartsWith("  | ", StringComparison.Ordinal))
            {
                text.AppendLine(line[4..]);
            }
        }

        return entries;
    }

    /// <summary>The fill lists of area-masters.txt read back with FillList, each with how many of its tiles the area laid.</summary>
    private static void FillLists(StringBuilder said, string folder, HashSet<string> laid)
    {
        string path = Path.Combine(folder, CaptureReport.MastersFile);
        if (!File.Exists(path))
        {
            said.AppendLine(CultureInfo.InvariantCulture, $"fill lists: no {CaptureReport.MastersFile} in the capture - a capture from 0.1.184 on carries it");
            return;
        }

        string current = string.Empty;
        var text = new StringBuilder();
        void Close()
        {
            if (current.EndsWith(FillList.Extension, StringComparison.OrdinalIgnoreCase))
            {
                FillList list = FillList.Parse(text.ToString());
                int rooms = list.Entries.Count(entry => entry.Path.EndsWith(".arm", StringComparison.OrdinalIgnoreCase));
                int onGround = list.Entries.Count(entry => laid.Contains(entry.Path));
                said.AppendLine(CultureInfo.InvariantCulture,
                    $"fill list {current}: version {list.Version}, {list.Sections.Count} sections, {list.Entries.Count - rooms} tiles ({onGround} laid), {rooms} rooms");
            }

            text.Clear();
        }

        foreach (string raw in File.ReadLines(path))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("##### ", StringComparison.Ordinal))
            {
                Close();
                int named = line.IndexOf("  (", StringComparison.Ordinal);
                current = named > 6 ? line[6..named] : line[6..];
            }
            else if (line.StartsWith("  | ", StringComparison.Ordinal))
            {
                text.AppendLine(line[4..]);
            }
        }

        Close();
    }

    /// <summary>The captures to read together: POEF_CAPTURES, several folders split by the platform's path separator - else POEF_CAPTURE alone, else none.</summary>
    private static string[] Folders()
    {
        string? several = Environment.GetEnvironmentVariable("POEF_CAPTURES");
        if (!string.IsNullOrWhiteSpace(several))
        {
            return [.. several.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        }

        return Folder is { Length: > 0 } one ? [one] : [];
    }

    /// <summary>
    /// weight-replay.txt: the weight a tile list gives a tile it writes none for, estimated from the game's own choices - every slot of every placed room whose candidates mix written weights with unwritten ones, the tile the game laid there, and the likelihood of those choices across the unwritten weight.
    /// </summary>
    /// <remarks>
    /// WHY IT HAS TO BE MEASURED. A tile list's weight is optional (poe_data_tools reads Option&lt;u32&gt;
    /// and says no more; poeformats does not read the lists at all), and what the generator takes for a
    /// missing one is in the game's code, not in a file - one value for every map. Oasis writes two
    /// weights in 124 lines, Steppe writes 1, 20 and 300 on most of its 72 and leaves the cliffs and
    /// ledges bare, so the build's "the heaviest wins" ranks a written weight against a guess.
    ///
    /// THE MODEL IS AN ASSUMPTION, stated so the reading can be judged: the generator draws a slot's tile
    /// from the tiles that answer it, each in proportion to its weight. The candidates are SlotWants' rule
    /// (an unnamed type as any), which may be wider than the generator's, so the set-equal rule is
    /// printed beside it - two candidate sets, two estimates; where they part, the rule matters more than
    /// the data. WHETHER A TAGGED TILE IS DRAWN for a slot that does not ask its tag is open too, so both
    /// readings are printed: Stronghold lays the tagged forced_blank in 40 of the 51 empty one by one
    /// slots the build gives it, as if drawn, while Steppe lays it, and the untagged Blank, in three by
    /// three slots no draw by size could reach - and its empty one by one slots, where Blank (300) and
    /// forced_blank meet, are what moves the estimate most. A slot only informs where both kinds answer;
    /// one whose laid tile is not among its candidates is counted out (a wrong place, a big slot filled
    /// with one by one tiles or the other way round) and said so. Each laid cell counts once, however many
    /// places claim it.
    /// </remarks>
    public static string WeightReplay(IReadOnlyList<string> folders)
    {
        var said = new StringBuilder();
        // FOUR READINGS: SlotWants' rule or the sets equal, each with tagged tiles in every slot's draw or
        // only in a slot asking their tag - see the remarks.
        string[] readings = ["SlotWants' rule, tagged tiles drawn", "SlotWants' rule, tagged tiles only for their tag", "the sets equal, tagged tiles drawn", "the sets equal, tagged tiles only for their tag"];
        var samplesOf = new List<(IReadOnlyList<int> Written, int Unwritten, int Laid)>[readings.Length];
        for (var reading = 0; reading < readings.Length; reading++)
        {
            samplesOf[reading] = [];
        }

        var lines = new StringBuilder();
        int outOfCandidates = 0, oneKind = 0, notFirstPiece = 0;
        foreach (string folder in folders)
        {
            TerrainGrid? grid = Terrain(folder, out string how);
            said.AppendLine(CultureInfo.InvariantCulture, $"{folder}: {how}");
            List<RoomBuildTile>? pool = Pool(folder, out string poolHow);
            said.AppendLine(CultureInfo.InvariantCulture, $"  {poolHow}");
            Dictionary<string, int> written = Written(folder);
            if (grid?.Tiles is not { } laid || pool is null || written.Count == 0)
            {
                said.AppendLine("  nothing to read: no tiles, no pool or no written weight");
                continue;
            }

            var counted = new HashSet<(int X, int Y)>();
            Placing placing = Place(folder);
            for (var one = 0; one < placing.Rooms.Count; one++)
            {
                (string room, RoomLayout layout) = placing.Rooms[one];
                foreach (RoomDoodadPlace place in placing.Placed[one].Places.Places)
                {
                    RoomCandidate at = place.Where;
                    for (var line = 0; line < layout.Height; line++)
                    {
                        for (var column = 0; column < layout.Width; column++)
                        {
                            RoomSlot slot = layout.SlotAt(column, line);
                            if (!slot.IsTile)
                            {
                                continue;
                            }

                            (int left, int top) = slot.Corner(column, line);
                            (int cellX, int cellY) = RoomFinder.CornerOf(left, top, slot.Width, slot.Height, layout.Width, layout.Height, at.Turn);
                            int x = at.X + cellX, y = at.Y + cellY;
                            if (laid.SubAt(x, y) is not (0, 0))
                            {
                                notFirstPiece++;
                                continue;
                            }

                            if (!counted.Add((x, y)))
                            {
                                continue;
                            }

                            string laidPath = laid.PathAt(x, y);
                            Ask ask = AskOf(layout, slot);
                            bool ForItsTag(int at2) => pool[at2].Identity.Tag.Length == 0 || ask.Tag.Length > 0;
                            Sample(samplesOf[0], at2 => Alike(ask, pool[at2].Identity), readings[0]);
                            Sample(samplesOf[1], at2 => ForItsTag(at2) && Alike(ask, pool[at2].Identity), readings[1]);
                            Sample(samplesOf[2], at2 => Strictly(ask, pool[at2].Identity), readings[2]);
                            Sample(samplesOf[3], at2 => ForItsTag(at2) && Strictly(ask, pool[at2].Identity), readings[3]);

                            void Sample(List<(IReadOnlyList<int> Written, int Unwritten, int Laid)> samples, Func<int, bool> answers, string rule)
                            {
                                var weights = new List<int>();
                                int unwritten = 0, chosen = int.MinValue;
                                for (var at2 = 0; at2 < pool.Count; at2++)
                                {
                                    if (!answers(at2))
                                    {
                                        continue;
                                    }

                                    bool isLaid = string.Equals(pool[at2].Path, laidPath, StringComparison.OrdinalIgnoreCase);
                                    if (written.TryGetValue(pool[at2].Path, out int weight))
                                    {
                                        chosen = isLaid ? weights.Count : chosen;
                                        weights.Add(weight);
                                    }
                                    else
                                    {
                                        chosen = isLaid ? -1 : chosen;
                                        unwritten++;
                                    }
                                }

                                if (chosen == int.MinValue)
                                {
                                    outOfCandidates += rule == readings[0] ? 1 : 0;
                                    return;
                                }

                                if (weights.Count == 0 || unwritten == 0)
                                {
                                    oneKind += rule == readings[0] ? 1 : 0;
                                    return;
                                }

                                samples.Add((weights, unwritten, chosen));
                                lines.AppendLine(CultureInfo.InvariantCulture,
                                    $"  {Path.GetFileName(folder.TrimEnd('/', '\\'))} {Short(room)} slot {column}, {line} ({rule}): laid {Short(laidPath)} {(chosen >= 0 ? $"weight {weights[chosen]}" : "UNWRITTEN")} - candidates written [{string.Join(", ", weights)}] and {unwritten} unwritten");
                            }
                        }
                    }
                }
            }
        }

        said.AppendLine();
        said.AppendLine(CultureInfo.InvariantCulture,
            $"samples: {string.Join(", ", readings.Select((reading, at) => $"{samplesOf[at].Count} by {reading}"))}; counted out by the first reading: {outOfCandidates} whose laid tile is no candidate, {oneKind} whose candidates are all written or all unwritten; {notFirstPiece} with no first piece on the slot's corner");
        said.AppendLine("model: a slot's tile drawn in proportion to its weight, an unwritten one weighing d - the log-likelihood of the game's choices across d, its best d, and the range within 1.92 of the best (about 95%)");
        for (var reading = 0; reading < readings.Length; reading++)
        {
            Estimate(said, readings[reading], samplesOf[reading]);
        }
        said.AppendLine();
        said.AppendLine("each sample, both halves - the laid tile and what it was drawn from:");
        said.Append(lines);
        return said.ToString();
    }

    /// <summary>The weights a capture's tile lists write, by tile - the heaviest where a tile is listed twice.</summary>
    private static Dictionary<string, int> Written(string folder)
    {
        var weights = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        string lists = Path.Combine(folder, CaptureReport.TilesetListsFile);
        if (!File.Exists(lists))
        {
            return weights;
        }

        foreach (TileListEntry entry in TileList.Parse(File.ReadAllText(lists)).Entries)
        {
            string tile = entry.Tile.Replace('\\', '/').Trim();
            if (entry.Weight is { } weight && (!weights.TryGetValue(tile, out int known) || weight > known))
            {
                weights[tile] = weight;
            }
        }

        return weights;
    }

    /// <summary>The log-likelihood of the samples across the unwritten weight, on a log grid, with its best and its 95% range.</summary>
    private static void Estimate(StringBuilder said, string rule, List<(IReadOnlyList<int> Written, int Unwritten, int Laid)> samples)
    {
        said.AppendLine();
        if (samples.Count == 0)
        {
            said.AppendLine(CultureInfo.InvariantCulture, $"{rule}: no sample");
            return;
        }

        int laidUnwritten = samples.Count(one => one.Laid < 0);
        said.AppendLine(CultureInfo.InvariantCulture, $"{rule}: {samples.Count} samples, the game laid an unwritten tile in {laidUnwritten} and a written one in {samples.Count - laidUnwritten}");
        double Likelihood(double d)
        {
            double sum = 0;
            foreach ((IReadOnlyList<int> weights, int unwritten, int laid) in samples)
            {
                double total = weights.Sum() + (unwritten * d);
                sum += Math.Log(laid >= 0 ? weights[laid] : d) - Math.Log(total);
            }

            return sum;
        }

        // FROM 0.01 TO 100000, forty steps a decade: fine enough for a range read in powers of ten.
        var grid = new List<(double D, double L)>();
        for (var step = -80; step <= 200; step++)
        {
            double d = Math.Pow(10, step / 40.0);
            grid.Add((d, Likelihood(d)));
        }

        (double bestD, double bestL) = grid.MaxBy(one => one.L);
        double low = grid.Where(one => one.L >= bestL - 1.92).Min(one => one.D);
        double high = grid.Where(one => one.L >= bestL - 1.92).Max(one => one.D);
        said.AppendLine(CultureInfo.InvariantCulture, $"  best d = {bestD:0.###}, 95% range {low:0.###} to {high:0.###}{(high >= grid[^1].D ? " (open above)" : string.Empty)}{(low <= grid[0].D ? " (open below)" : string.Empty)}");
        foreach (double d in (double[])[0.1, 1, 10, 20, 50, 100, 300, 1000])
        {
            said.AppendLine(CultureInfo.InvariantCulture, $"  d = {d,6}: log-likelihood {Likelihood(d):0.00} ({Likelihood(d) - bestL:0.00} from the best)");
        }
    }

    /// <summary>Counts, for one slot under one pair of conventions, the named slot sides (or corners) that agree and disagree with the tile's on the same way.</summary>
    private static void Count(
        (string Room, int Column, int Line, int Turn, string[] SlotEdges, string[] SlotGrounds, string Tile, int Placement, TileIdentity Laid) sample,
        int roomWay, int tileWay, (int X, int Y)[] directions, string[] slotTypes, IReadOnlyList<string> tileTypes, ref int agree, ref int disagree)
    {
        for (var side = 0; side < 4; side++)
        {
            if (slotTypes[side].Length == 0)
            {
                continue;
            }

            int met = Meets(sample.Turn, sample.Placement, roomWay, tileWay, directions, side);
            if (met < 0 || met >= tileTypes.Count)
            {
                continue;
            }

            if (string.Equals(Clean(slotTypes[side]), Clean(tileTypes[met]), StringComparison.OrdinalIgnoreCase))
            {
                agree++;
            }
            else
            {
                disagree++;
            }
        }
    }

    /// <summary>Prints one slot's named types beside the tile's on the side that lies the same way.</summary>
    private static void Beside(
        StringBuilder said,
        (string Room, int Column, int Line, int Turn, string[] SlotEdges, string[] SlotGrounds, string Tile, int Placement, TileIdentity Laid) sample,
        int roomWay, int tileWay, (int X, int Y)[] directions, string[] slotTypes, IReadOnlyList<string> tileTypes, string[] names)
    {
        for (var side = 0; side < 4; side++)
        {
            if (slotTypes[side].Length == 0)
            {
                continue;
            }

            int met = Meets(sample.Turn, sample.Placement, roomWay, tileWay, directions, side);
            string tileType = met >= 0 && met < tileTypes.Count ? Short(tileTypes[met]) : "?";
            bool same = met >= 0 && met < tileTypes.Count && string.Equals(Clean(slotTypes[side]), Clean(tileTypes[met]), StringComparison.OrdinalIgnoreCase);
            said.Append(CultureInfo.InvariantCulture, $" {names[side]} {Short(slotTypes[side])} ~ tile {(met >= 0 ? names[met] : "?")} {tileType}{(same ? string.Empty : " MISS")};");
        }
    }

    /// <summary>
    /// Which side (or corner) of the tile lies the way the slot's side does, under a pair of conventions - or -1 for none.
    /// </summary>
    /// <remarks>
    /// THE SLOT'S SIDE goes through the room's reading, then the room's laying (RoomFinder.Laying, its linear
    /// part on a direction); THE TILE'S SIDE through the tile's reading, then the tile's placement
    /// (TileOrientation.Turn, template to world). The two land in the area's own axes, and are compared there.
    /// </remarks>
    private static int Meets(int roomTurn, int placement, int roomWay, int tileWay, (int X, int Y)[] directions, int side)
    {
        (int dx, int dy) = Read(Dihedral[roomWay], directions[side]);
        Matrix3x2 laying = RoomFinder.Laying(1, 1, roomTurn);
        int ax = (int)MathF.Round((dx * laying.M11) + (dy * laying.M21));
        int ay = (int)MathF.Round((dx * laying.M12) + (dy * laying.M22));

        (int xx, int xy, int yx, int yy) = TileOrientation.OfPlacement(placement).Turn;
        for (var k = 0; k < 4; k++)
        {
            (int ex, int ey) = Read(Dihedral[tileWay], directions[k]);
            int wx = (xx * ex) + (xy * ey);
            int wy = (yx * ex) + (yy * ey);
            if (wx == ax && wy == ay)
            {
                return k;
            }
        }

        return -1;
    }

    private static (int X, int Y) Read((int Xx, int Xy, int Yx, int Yy) way, (int X, int Y) direction)
        => ((way.Xx * direction.X) + (way.Xy * direction.Y), (way.Yx * direction.X) + (way.Yy * direction.Y));

    private static string Clean(string type) => type.Replace('\\', '/').Trim();

    /// <summary>One place in a line: where, how it was laid, and both halves of what agrees there.</summary>
    private static string Said(int rank, RoomCandidate where, RoomMisses misses)
        => string.Create(
            CultureInfo.InvariantCulture,
            $"{(rank > 0 ? rank + ". " : string.Empty)}tile {where.X}, {where.Y}, {RoomFinder.Said(where.Turn)}: {where.TilesAgree} of {where.Tiles} tiles agree, {where.Matched} of {where.Corners} corners, {RoomArrangement.Beside(where, misses)}% beside, {misses.Elsewhere} misses at no join");

    /// <summary>What a kind of slot asks of the tile under it, in the terms RoomPlacements compares: the size, the tag, the edge and ground types sorted.</summary>
    private sealed record Ask(int Width, int Height, string Tag, string[] Edges, string[] Grounds)
    {
        public string Said => string.Create(
            CultureInfo.InvariantCulture,
            $"{Width}x{Height}{(Tag.Length > 0 ? " \"" + Tag + "\"" : string.Empty)} edges [{string.Join(", ", Edges.Select(Short))}] grounds [{string.Join(", ", Grounds.Select(Short))}]");
    }

    /// <summary>Each kind of slot in a room once, in the order its first slot comes.</summary>
    private static List<Ask> Asks(RoomLayout room)
    {
        var asks = new List<Ask>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var line = 0; line < room.Height; line++)
        {
            for (var column = 0; column < room.Width; column++)
            {
                RoomSlot slot = room.SlotAt(column, line);
                if (!slot.IsTile)
                {
                    continue;
                }

                Ask ask = AskOf(room, slot);
                if (seen.Add(ask.Said))
                {
                    asks.Add(ask);
                }
            }
        }

        return asks;
    }

    /// <summary>What one slot asks, in RoomPlacements' terms.</summary>
    private static Ask AskOf(RoomLayout room, RoomSlot slot)
        => new(
            slot.Width,
            slot.Height,
            room.Named(slot.Tag),
            Sorted([room.Named(slot.Edge(0)), room.Named(slot.Edge(1)), room.Named(slot.Edge(2)), room.Named(slot.Edge(3))]),
            Sorted([room.Named(slot.Ground(0)), room.Named(slot.Ground(1)), room.Named(slot.Ground(2)), room.Named(slot.Ground(3))]));

    /// <summary>Whether a laid file answers a slot's ask - SlotWants' rule, an unnamed type in the ask taken as any, kept in step with it by hand.</summary>
    private static bool Alike(Ask ask, TileIdentity laid)
        => SameSize(ask, laid)
            && (ask.Tag.Length == 0 || string.Equals(ask.Tag, laid.Tag, StringComparison.OrdinalIgnoreCase))
            && Covers(laid.Edges, ask.Edges)
            && Covers(laid.Grounds, ask.Grounds);

    /// <summary>The rule before 0.1.157, the sets equal - printed beside the other so a capture still shows where the two part.</summary>
    private static bool Strictly(Ask ask, TileIdentity laid)
        => SameSize(ask, laid)
            && (ask.Tag.Length == 0 || string.Equals(ask.Tag, laid.Tag, StringComparison.OrdinalIgnoreCase))
            && Sorted(laid.Edges).AsSpan().SequenceEqual(ask.Edges, StringComparer.OrdinalIgnoreCase)
            && Sorted(laid.Grounds).AsSpan().SequenceEqual(ask.Grounds, StringComparer.OrdinalIgnoreCase);

    private static bool SameSize(Ask ask, TileIdentity laid)
        => (laid.Width == ask.Width && laid.Height == ask.Height) || (laid.Width == ask.Height && laid.Height == ask.Width);

    /// <summary>Whether every named type the ask lists is among the laid ones, each laid one answering at most one ask.</summary>
    private static bool Covers(IReadOnlyList<string> laid, string[] asks)
    {
        List<string> pool = [.. laid.Select(one => one.Replace('\\', '/'))];
        foreach (string ask in asks)
        {
            if (ask.Length == 0)
            {
                continue;
            }

            int found = pool.FindIndex(one => string.Equals(one, ask, StringComparison.OrdinalIgnoreCase));
            if (found < 0)
            {
                return false;
            }

            pool.RemoveAt(found);
        }

        return true;
    }

    private static string[] Sorted(IEnumerable<string> four)
    {
        string[] sorted = [.. four.Select(one => one.Replace('\\', '/'))];
        Array.Sort(sorted, StringComparer.OrdinalIgnoreCase);
        return sorted;
    }

    /// <summary>A path's last name without its extension, or a dash for none.</summary>
    private static string Short(string path) => path.Length == 0 ? "-" : Path.GetFileNameWithoutExtension(path);

    /// <summary>How many cells each tile file covers in the area.</summary>
    private static Dictionary<string, int> Cells(TerrainTiles? tiles)
    {
        var cells = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        if (tiles is null)
        {
            return cells;
        }

        for (var y = 0; y < tiles.Height; y++)
        {
            for (var x = 0; x < tiles.Width; x++)
            {
                string path = tiles.PathAt(x, y);
                if (path.Length > 0)
                {
                    cells[path] = cells.GetValueOrDefault(path) + 1;
                }
            }
        }

        return cells;
    }

    /// <summary>How often a file was laid: its cells over its size, rounded up - one where its size is not known.</summary>
    private static int Instances(string path, int cells, Dictionary<string, TileIdentity> identities)
    {
        int size = identities.TryGetValue(path, out TileIdentity? identity) ? Math.Max(1, identity.Width * identity.Height) : cells;
        return (cells + size - 1) / Math.Max(1, size);
    }

    /// <summary>A laid file in a line: its path under Metadata/Terrain, size, tag, edges and grounds, cells and times laid - and where, for a file laid once.</summary>
    private static string LaidSaid(string path, int cells, Dictionary<string, TileIdentity> identities, TerrainTiles? tiles)
    {
        string name = path.StartsWith("Metadata/Terrain/", StringComparison.OrdinalIgnoreCase) ? path["Metadata/Terrain/".Length..] : path;
        if (!identities.TryGetValue(path, out TileIdentity? identity))
        {
            return string.Create(CultureInfo.InvariantCulture, $"{name}  (no identity)  {cells} cells");
        }

        var said = new StringBuilder(name);
        said.Append(CultureInfo.InvariantCulture, $"  {identity.Width}x{identity.Height}{(identity.Tag.Length > 0 ? " \"" + identity.Tag + "\"" : string.Empty)}");
        said.Append(CultureInfo.InvariantCulture, $" edges [{string.Join(", ", identity.Edges.Select(Short))}] grounds [{string.Join(", ", identity.Grounds.Select(Short))}]");
        if (cells > 0)
        {
            int laid = Instances(path, cells, identities);
            said.Append(CultureInfo.InvariantCulture, $"  {cells} cells = laid {laid}x");
            if (laid == 1 && tiles is not null && Bounds(tiles, path) is var (x0, y0, x1, y1))
            {
                said.Append(CultureInfo.InvariantCulture, $" at tiles {x0}..{x1}, {y0}..{y1}");
            }
        }

        return said.ToString();
    }

    /// <summary>The tiles a file covers, as the corners of their bounding box - or null where it covers none.</summary>
    private static (int X0, int Y0, int X1, int Y1)? Bounds(TerrainTiles tiles, string path)
    {
        int x0 = int.MaxValue, y0 = int.MaxValue, x1 = -1, y1 = -1;
        for (var y = 0; y < tiles.Height; y++)
        {
            for (var x = 0; x < tiles.Width; x++)
            {
                if (string.Equals(tiles.PathAt(x, y), path, StringComparison.OrdinalIgnoreCase))
                {
                    x0 = Math.Min(x0, x);
                    y0 = Math.Min(y0, y);
                    x1 = Math.Max(x1, x);
                    y1 = Math.Max(y1, y);
                }
            }
        }

        return x1 < 0 ? null : (x0, y0, x1, y1);
    }

    /// <summary>The distinct files laid under a footprint, most cells first.</summary>
    private static string Under(TerrainTiles? tiles, RoomCandidate at)
    {
        if (tiles is null)
        {
            return "(tiles not read)";
        }

        var under = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var y = at.Y; y < at.Y + at.Height; y++)
        {
            for (var x = at.X; x < at.X + at.Width; x++)
            {
                string path = tiles.PathAt(x, y);
                under[path.Length > 0 ? path : "(none)"] = under.GetValueOrDefault(path.Length > 0 ? path : "(none)") + 1;
            }
        }

        return string.Join(", ", under.OrderByDescending(one => one.Value).ThenBy(one => one.Key, StringComparer.OrdinalIgnoreCase)
            .Select(one => string.Create(CultureInfo.InvariantCulture, $"{Short(one.Key)} x{one.Value}")));
    }

    /// <summary>A capture's rooms placed by their doodads, as the tool would place them.</summary>
    /// <param name="Rooms">Every room of room-files.txt, read.</param>
    /// <param name="Survey">Every sighting of doodads.txt.</param>
    /// <param name="TilesX">The grid's tiles across, as capture.txt says.</param>
    /// <param name="TilesY">The grid's tiles down.</param>
    /// <param name="Placed">Every room with its places, settled.</param>
    private sealed record Placing(
        List<(string Room, RoomLayout Layout)> Rooms,
        DoodadSurvey Survey,
        int TilesX,
        int TilesY,
        List<(string Room, RoomLayout Layout, RoomDoodadPlaces Places)> Placed);

    /// <summary>Every room of the capture placed by its doodads over the capture's sightings.</summary>
    private static Placing Place(string folder)
    {
        List<(string Room, RoomLayout Layout)> rooms = Rooms(File.ReadAllText(Path.Combine(folder, CaptureReport.RoomFilesFile)));
        DoodadSurvey survey = Survey(File.ReadAllText(Path.Combine(folder, CaptureReport.DoodadsFile)));
        (int tilesX, int tilesY) = Tiles(File.ReadAllText(Path.Combine(folder, CaptureReport.SummaryFile)), survey);

        var found = new List<(string Room, RoomDoodadPlaces Places)>(rooms.Count);
        foreach ((string room, RoomLayout layout) in rooms)
        {
            found.Add((room, RoomDoodadFinder.Find(layout.Doodads, layout.Width, layout.Height, survey.Found, tilesX, tilesY)));
        }

        List<(string Room, RoomDoodadPlaces Places)> settled = RoomDoodadFinder.Settle(found, tilesX, tilesY);
        var placed = new List<(string Room, RoomLayout Layout, RoomDoodadPlaces Places)>(rooms.Count);
        for (var one = 0; one < rooms.Count; one++)
        {
            placed.Add((rooms[one].Room, rooms[one].Layout, settled[one].Places));
        }

        return new Placing(rooms, survey, tilesX, tilesY, placed);
    }

    /// <summary>
    /// The terrain as the capture's world pass read it, out of memory.rec - or null and why. The replay serves the newest read at or before its last frame, which is the pass's.
    /// </summary>
    private static TerrainGrid? Terrain(string folder, out string how)
    {
        string path = Path.Combine(folder, CaptureReport.MemoryFile);
        if (!File.Exists(path))
        {
            how = $"terrain not read: no {CaptureReport.MemoryFile} in the capture";
            return null;
        }

        using var replay = ReplayMemoryReader.Load(File.OpenRead(path));
        if (!replay.ResolvedStatics.TryGetValue("GameStates", out ulong gameStates))
        {
            how = "terrain not read: the recording names no GameStates static";
            return null;
        }

        // THE LIVE SCHEMA, not the one the committed recordings replay against: a capture is of the client
        // as it is. THE ROTATION TABLES from the recording's statics, where the pass noted them, so each
        // tile's placement decodes as it did live.
        var rotation = new TerrainRotationTables(
            replay.ResolvedStatics.GetValueOrDefault("TerrainRotationSelector"),
            replay.ResolvedStatics.GetValueOrDefault("TerrainRotatorHelper"));
        var world = new WorldReader(replay, RealSessionTests.LiveSchema(), rotation);
        TerrainGrid? grid = world.Read(gameStates, CaptureMemory.MostEntities).Terrain;
        if (grid is null)
        {
            how = "terrain not read from the recording";
            return null;
        }

        string groundSaid = grid.Ground is null ? "not read" : string.Create(CultureInfo.InvariantCulture, $"{grid.Ground.Types.Count} named");
        string tilesSaid = grid.Tiles is null ? "not read" : string.Create(CultureInfo.InvariantCulture, $"{grid.Tiles.Paths.Count} files laid");
        string rotationSaid = rotation.IsResolved ? "resolved" : "not both among the recording's statics (" + string.Join(", ", replay.ResolvedStatics.Keys.Order(StringComparer.Ordinal)) + ")";
        how = string.Create(CultureInfo.InvariantCulture, $"terrain {grid.TilesX} x {grid.TilesY} tiles from the recording, ground types {groundSaid}, tiles {tilesSaid}, rotation tables {rotationSaid}");
        return grid;
    }

    /// <summary>
    /// Each laid tile file's identity from tile-identities.txt, by path - or null where the capture has none (before 0.1.155), and the search scores corners alone.
    /// </summary>
    private static Dictionary<string, TileIdentity>? Identities(string folder)
    {
        string path = Path.Combine(folder, CaptureReport.TileIdentitiesFile);
        if (!File.Exists(path))
        {
            return null;
        }

        var known = new Dictionary<string, TileIdentity>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in File.ReadLines(path))
        {
            string[] cells = raw.TrimEnd('\r').Split('\t');
            if (cells.Length < 12
                || !int.TryParse(cells[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int width)
                || !int.TryParse(cells[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out int height))
            {
                continue;
            }

            known[cells[0]] = new TileIdentity(width, height, cells[3], cells[4..8], cells[8..12]);
        }

        return known;
    }

    /// <summary>Every room of room-files.txt, each block under its "##### path" header parsed as the file.</summary>
    private static List<(string Room, RoomLayout Layout)> Rooms(string text)
    {
        var rooms = new List<(string Room, RoomLayout Layout)>();
        string? room = null;
        var block = new List<string>();
        void Close()
        {
            if (room is not null)
            {
                rooms.Add((room, RoomLayout.Parse(string.Join('\n', block))));
            }

            block.Clear();
        }

        foreach (string line in text.Split('\n'))
        {
            if (line.StartsWith("##### ", StringComparison.Ordinal))
            {
                Close();
                room = line[6..].Trim();
            }
            else
            {
                block.Add(line.TrimEnd('\r'));
            }
        }

        Close();
        return rooms;
    }

    /// <summary>Every sighting of doodads.txt's table.</summary>
    private static DoodadSurvey Survey(string text)
    {
        var found = new List<DoodadSighting>();
        var inTable = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (!inTable)
            {
                inTable = line.StartsWith("id\tpath\tmodel", StringComparison.Ordinal);
                continue;
            }

            string[] cells = line.Split('\t');
            if (cells.Length < 7)
            {
                continue;
            }

            found.Add(new DoodadSighting(
                uint.Parse(cells[0], CultureInfo.InvariantCulture), cells[1], cells[2],
                float.Parse(cells[3], CultureInfo.InvariantCulture), float.Parse(cells[4], CultureInfo.InvariantCulture), float.Parse(cells[5], CultureInfo.InvariantCulture),
                cells[6] == "sleeping") { Remembered = cells[6] == "remembered" });
        }

        return new DoodadSurvey(0, 0, 0, 0, found, 0d, string.Empty);
    }

    /// <summary>The grid's size from capture.txt's terrain line, or past the farthest sighting where a capture predates the line.</summary>
    private static (int TilesX, int TilesY) Tiles(string summary, DoodadSurvey survey)
    {
        // WITHOUT THE CARRIAGE RETURN, or the line's last word is "tiles\r" and every capture written on
        // Windows fell through to the guess below - 46 x 81 for a grid of 45 x 93.
        foreach (string raw in summary.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("terrain  ", StringComparison.Ordinal))
            {
                string[] words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                int tiles = Array.IndexOf(words, "tiles");
                if (tiles >= 3 && int.TryParse(words[tiles - 3], CultureInfo.InvariantCulture, out int x) && int.TryParse(words[tiles - 1], CultureInfo.InvariantCulture, out int y))
                {
                    return (x, y);
                }
            }
        }

        float farX = 0f, farY = 0f;
        foreach (DoodadSighting one in survey.Found)
        {
            farX = Math.Max(farX, one.X);
            farY = Math.Max(farY, one.Y);
        }

        return ((int)(farX / RoomDoodadFinder.WithinUnits) + 10, (int)(farY / RoomDoodadFinder.WithinUnits) + 10);
    }
}
