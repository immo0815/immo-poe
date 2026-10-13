using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// The generator's own rules held against a capture: where it lays the rooms of an area's module (lattice-replay.txt).
/// </summary>
/// <remarks>
/// WHAT STARTED IT: GGG's procedural-generation talk (Rhys Abraham, ExileCon) - rooms laid on a grid,
/// overlapping by one tile along every boundary, their keys merged there by rules, and overlays laid over
/// the layout by matching shapes once it is made. The masters name every one of those (RoomUnitSize,
/// RoomOverlap, OuterBufferSize, EdgeCombination, Overlays) and nothing here read them. A talk is not the
/// game, so each is measured here before anything is built on it.
/// </remarks>
public partial class CaptureReplayTests
{
    /// <summary>Measures where the rooms of the area's module stand against the masters' lattice, and writes the table beside the capture's files.</summary>
    [Fact]
    public void THELATTICEIsHeldAgainstThePlacesFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = LatticeReplay(folder);
        File.WriteAllText(Path.Combine(folder, "lattice-replay.txt"), replay);
        Assert.StartsWith("lattice: ", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// lattice-replay.txt: the masters' lattices; every room's module beside its grid; every place the doodads and the tiles find with NO lattice, each against its room's; and the residues where the capture has no master.
    /// </summary>
    /// <remarks>
    /// PLACED WITHOUT THE LATTICE, so the lattice is held against places it had no hand in - the same
    /// placing the tool made before RoomLattice, doodads first and the tiles for the rest.
    /// </remarks>
    public static string LatticeReplay(string folder)
    {
        var said = new StringBuilder();
        List<MasterFile> masters = Masters(folder);
        RoomLattices lattices = RoomLattices.From(masters);
        said.AppendLine(CultureInfo.InvariantCulture,
            $"lattice: {masters.Count} masters in the capture name {lattices.All.Count} lattices{(lattices.All.Count > 0 ? " - " + string.Join("; ", lattices.All.Select(one => $"side {one.Side}, overlap {one.Overlap}, buffer {one.Buffer}: {one.Said}")) : string.Empty)}");

        Placing placing = Place(folder, onLattice: false);
        said.AppendLine(CultureInfo.InvariantCulture, $"grid {placing.TilesX} x {placing.TilesY} tiles");
        foreach (RoomLattice lattice in lattices.All)
        {
            // THE GRID WHOLE: the buffer, whole steps, the last module's overlap, the buffer again.
            said.AppendLine(CultureInfo.InvariantCulture,
                $"  across {Whole(placing.TilesX, lattice)}, down {Whole(placing.TilesY, lattice)}");
        }

        // EVERY ROOM'S MODULE beside its grid: the overlap plus whole steps each way, or not.
        int fit = 0, other = 0, unfit = 0;
        var rooms = new StringBuilder();
        foreach ((string room, RoomLayout layout) in placing.Rooms)
        {
            int step = layout.ModuleSide - layout.ModuleOverlap;
            bool whole = layout.ModuleOverlap >= 0 && step > 0
                && layout.Width > layout.ModuleOverlap && (layout.Width - layout.ModuleOverlap) % step == 0
                && layout.Height > layout.ModuleOverlap && (layout.Height - layout.ModuleOverlap) % step == 0;
            RoomLattice? itsLattice = lattices.For(layout);
            fit += whole ? 1 : 0;
            unfit += whole ? 0 : 1;
            other += whole && itsLattice is null ? 1 : 0;
            rooms.AppendLine(CultureInfo.InvariantCulture,
                $"  {Short(room)}: module {layout.ModuleSide} {layout.ModuleOverlap}, {layout.Width} x {layout.Height} - {(whole ? "the overlap plus whole steps" : "NOT the overlap plus whole steps")}, {(itsLattice is { } lattice ? "on the area's lattice " + lattice.Said : "free")}");
        }

        said.AppendLine(CultureInfo.InvariantCulture,
            $"rooms: {placing.Rooms.Count}, {fit} of them the overlap plus whole steps of their module each way ({other} of a module no master here names), {unfit} not");
        said.AppendLine().AppendLine("=== every room's module, its grid, and the lattice it is held to").Append(rooms);

        // EVERY PLACE WITH NO LATTICE, the tiles' places settled in where the recording has the terrain.
        TerrainGrid? grid = Terrain(folder, out string how);
        Dictionary<string, TileIdentity>? identities = Identities(folder);
        TerrainTiles? tiles = grid?.Tiles is { } laid && identities is not null && laid.Width == placing.TilesX && laid.Height == placing.TilesY ? laid : null;
        if (tiles is null)
        {
            said.AppendLine(CultureInfo.InvariantCulture, $"the tiles' places left out - {how}{(identities is null ? ", no tile identities" : string.Empty)}");
        }

        List<(string Room, RoomLayout Layout, RoomDoodadPlaces Places)> placed = Settled(placing, tiles, identities, RoomLattices.None);

        int on = 0, off = 0, roomsPlaced = 0, strongestOn = 0;
        var stronger = new List<string>();
        var byStep = new SortedDictionary<int, Dictionary<(int X, int Y), int>>();
        said.AppendLine().AppendLine("=== every place found with no lattice, against its room's");
        foreach ((string room, RoomLayout layout, RoomDoodadPlaces places) in placed)
        {
            if (places.Places.Count == 0)
            {
                continue;
            }

            RoomLattice? lattice = lattices.For(layout);
            int step = layout.ModuleSide - layout.ModuleOverlap;
            int bestOn = -1, bestOff = -1;
            foreach (RoomDoodadPlace place in places.Places)
            {
                RoomCandidate at = place.Where;
                string kind = place.Anchor.Length > 0 ? "tiles" : string.Create(CultureInfo.InvariantCulture, $"{place.Hits} of {place.Lines} doodads, off {place.MeanOff:0}");
                string verdict = "free";
                if (lattice is { } held)
                {
                    bool holds = held.Holds(at.X, at.Y);
                    on += holds ? 1 : 0;
                    off += holds ? 0 : 1;
                    int strength = place.Anchor.Length > 0 ? int.MaxValue : place.Hits;
                    if (holds)
                    {
                        bestOn = Math.Max(bestOn, strength);
                    }
                    else
                    {
                        bestOff = Math.Max(bestOff, strength);
                    }

                    verdict = holds ? "on the lattice" : string.Create(CultureInfo.InvariantCulture, $"OFF the lattice, {Residue(at.X - held.Buffer, held.Step)}, {Residue(at.Y - held.Buffer, held.Step)} past it");
                }
                else if (step > 0 && layout.ModuleOverlap >= 0 && (layout.Width - layout.ModuleOverlap) % step == 0 && (layout.Height - layout.ModuleOverlap) % step == 0 && layout.ModuleSide > 1)
                {
                    // NO MASTER FOR ITS MODULE: the residue of its own step, gathered below.
                    Dictionary<(int X, int Y), int> residues = byStep.TryGetValue(step, out Dictionary<(int X, int Y), int>? known) ? known : byStep[step] = [];
                    (int X, int Y) residue = (Residue(at.X, step), Residue(at.Y, step));
                    residues[residue] = residues.GetValueOrDefault(residue) + 1;
                    verdict = string.Create(CultureInfo.InvariantCulture, $"no master: residue {residue.X}, {residue.Y} of step {step}");
                }

                said.AppendLine(CultureInfo.InvariantCulture, $"  {Short(room)} tile {at.X}, {at.Y}, {RoomFinder.Said(at.Turn)} by {kind}: {verdict}");
            }

            if (lattice is not null)
            {
                roomsPlaced++;
                strongestOn += bestOn >= bestOff ? 1 : 0;
                if (bestOff > bestOn)
                {
                    stronger.Add(Short(room));
                }
            }
        }

        said.AppendLine(CultureInfo.InvariantCulture,
            $"places of rooms held to a lattice: {on} on it, {off} off it; {roomsPlaced} such rooms placed, {strongestOn} with their strongest place on it; {stronger.Count} with a place off it stronger than every place on it{(stronger.Count > 0 ? " - " + string.Join(", ", stronger) : string.Empty)}");

        if (byStep.Count > 0)
        {
            said.AppendLine().AppendLine("=== no master for their module: the places of the module's rooms by residue of its step");
            foreach ((int step, Dictionary<(int X, int Y), int> residues) in byStep)
            {
                said.AppendLine(CultureInfo.InvariantCulture,
                    $"  step {step}: {string.Join("; ", residues.OrderByDescending(one => one.Value).Select(one => $"{one.Key.X}, {one.Key.Y} x{one.Value}"))}");
            }
        }

        // WHAT THE TOOL NOW WRITES, held to the lattice - the line rooms-placed.txt opens with - and every
        // place that differs from the placing with none, either way.
        List<(string Room, RoomLayout Layout, RoomDoodadPlaces Places)> onIt = Settled(Place(folder), tiles, identities, lattices);
        said.AppendLine().Append("=== on the lattice, as the tool now places them: ").AppendLine(CaptureReport.Placed(onIt).Said);
        HashSet<string> before = Places(placed), after = Places(onIt);
        foreach (string gone in before.Where(one => !after.Contains(one)).Order(StringComparer.Ordinal))
        {
            said.Append("  left out: ").AppendLine(gone);
        }

        foreach (string came in after.Where(one => !before.Contains(one)).Order(StringComparer.Ordinal))
        {
            said.Append("  new: ").AppendLine(came);
        }

        return said.ToString();
    }

    /// <summary>The placing with the tiles' places settled in, as the tool settles them, where the tiles are to hand.</summary>
    private static List<(string Room, RoomLayout Layout, RoomDoodadPlaces Places)> Settled(
        Placing placing, TerrainTiles? tiles, Dictionary<string, TileIdentity>? identities, RoomLattices lattices)
        => tiles is null || identities is null
            ? placing.Placed
            : RoomTileFinder.Settled(placing.Placed, (_, layout) => RoomTileFinder.Find(layout, tiles, path => identities.GetValueOrDefault(path), lattices.For(layout)), placing.Survey.Found);

    /// <summary>Every place of a placing in a line's words - the room, where, which way, and by what.</summary>
    private static HashSet<string> Places(List<(string Room, RoomLayout Layout, RoomDoodadPlaces Places)> placed)
    {
        var all = new HashSet<string>(StringComparer.Ordinal);
        foreach ((string room, _, RoomDoodadPlaces places) in placed)
        {
            foreach (RoomDoodadPlace place in places.Places)
            {
                all.Add(string.Create(CultureInfo.InvariantCulture,
                    $"{Short(room)} tile {place.Where.X}, {place.Where.Y}, {RoomFinder.Said(place.Where.Turn)}, {place.Where.Width} x {place.Where.Height} by {(place.Anchor.Length > 0 ? "tiles" : "doodads")}"));
            }
        }

        return all;
    }

    /// <summary>Holds the tiles laid where two placed rooms share a cell against both rooms' slots and the master's edge combinations, and writes the table beside the capture's files.</summary>
    [Fact]
    public void THEJOINSAreHeldAgainstTheEdgeCombinationsFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = JoinsReplay(folder);
        File.WriteAllText(Path.Combine(folder, "joins-replay.txt"), replay);
        Assert.StartsWith("terrain ", replay, StringComparison.Ordinal);
    }

    /// <summary>The four sides of a square as directions in the area's own axes, in the files' order once read as written - for naming a side of the area.</summary>
    private static readonly string[] AreaSides = ["-y", "+x", "+y", "-x"];

    /// <summary>The four corners likewise.</summary>
    private static readonly string[] AreaCorners = ["-x-y", "+x-y", "+x+y", "-x+y"];

    /// <summary>One placed room's one-by-one slot on a cell of the area: its types by the area's own sides and corners, and whether it lies on the room's rim.</summary>
    private sealed record CellAsk(string Room, int Column, int Line, string[] Edges, string[] Grounds, int[] Heights, string[] Pairs, bool Rim);

    /// <summary>
    /// joins-replay.txt: every cell two placed rooms both cover, each room's one-by-one slot there beside the tile the game laid - side by side and corner by corner in the area's own axes - and what the master's edge combinations (.ecf) make of the two; then the same counted over every pair of types that met, and every rim cell one room alone covers for comparison.
    /// </summary>
    /// <remarks>
    /// WHAT IT ASKS: GGG's talk has rooms overlap by one tile along every boundary and their keys merged there by rules, and
    /// every master of the captures says <c>RoomOverlap 1</c> and names an <c>.ecf</c> of lines that read "this edge and that
    /// make this one". So where two rooms' rims lie on one row, is the tile the game laid what the rule makes of the two
    /// slots' types - or one room's, or neither's? Both halves are printed and counted, nothing concluded in the code: the
    /// slot's side read as written and laid as the room was (RoomFinder.Laying), the tile's as written and placed as it was
    /// (TileOrientation.Turn) - the settled convention, SlotWants. Only one-by-one slots and tiles: a big tile's side is
    /// several cells long and a join between big slots is a question of its own.
    /// </remarks>
    public static string JoinsReplay(string folder)
    {
        var said = new StringBuilder();
        TerrainGrid? grid = Terrain(folder, out string how);
        said.AppendLine(how);
        Dictionary<string, TileIdentity>? identities = Identities(folder);
        if (grid?.Tiles is not { } laid || identities is null)
        {
            said.AppendLine(identities is null ? $"nothing to hold the joins against without {CaptureReport.TileIdentitiesFile}" : "nothing to hold the joins against without the terrain's tiles");
            return said.ToString();
        }

        // THE MASTER'S RULES, read as "first and second make third", keyed both ways round.
        var rules = new Dictionary<(string, string), List<EdgeCombination>>();
        foreach ((string path, string text) in MasterSections(folder).Where(one => one.Path.EndsWith(EdgeCombinations.Extension, StringComparison.OrdinalIgnoreCase)))
        {
            EdgeCombinations read = EdgeCombinations.Parse(text);
            said.AppendLine(CultureInfo.InvariantCulture, $"edge combinations {path}: version {read.Version}, {read.Lines.Count} lines");
            foreach (EdgeCombination line in read.Lines)
            {
                said.AppendLine(CultureInfo.InvariantCulture, $"  {Short(line.First)} + {Short(line.Second)} -> {Short(line.Result)}{(line.Number is { } number ? " " + number.ToString(CultureInfo.InvariantCulture) : string.Empty)}");
                (string, string) key = (Short(line.First), Short(line.Second));
                (rules.TryGetValue(key, out List<EdgeCombination>? known) ? known : rules[key] = []).Add(line);
                if (key.Item1 != key.Item2)
                {
                    (string, string) back = (key.Item2, key.Item1);
                    (rules.TryGetValue(back, out List<EdgeCombination>? other) ? other : rules[back] = []).Add(line);
                }
            }
        }

        if (rules.Count == 0)
        {
            said.AppendLine($"no edge combinations in the capture - {CaptureReport.MastersFile} from 0.1.184 on carries the master's");
        }

        // EVERY PLACED ROOM'S ONE-BY-ONE SLOTS on their cells: the doodads' places, then the tiles' for the rest - a
        // footprint drawn once for several rooms says no room's slots and is left out.
        Placing placing = Place(folder);
        List<(string Room, RoomLayout Layout, RoomDoodadPlaces Places)> placed = Settled(placing, laid, identities, Lattices(folder));
        var asks = new Dictionary<(int X, int Y), List<CellAsk>>();
        int instances = 0;
        foreach ((string room, RoomLayout layout, RoomDoodadPlaces places) in placed)
        {
            foreach (RoomDoodadPlace place in places.Places)
            {
                if (place.Variants.Count > 0)
                {
                    continue;
                }

                instances++;
                RoomCandidate at = place.Where;
                Matrix3x2 laying = RoomFinder.Laying(1, 1, at.Turn);
                string name = string.Create(CultureInfo.InvariantCulture, $"{Short(room)}@{at.X},{at.Y}");
                for (var line = 0; line < layout.Height; line++)
                {
                    for (var column = 0; column < layout.Width; column++)
                    {
                        RoomSlot slot = layout.SlotAt(column, line);
                        if (!slot.IsTile || slot.Width != 1 || slot.Height != 1)
                        {
                            continue;
                        }

                        (int cellX, int cellY) = RoomFinder.CornerOf(column, line, 1, 1, layout.Width, layout.Height, at.Turn);
                        var edges = new string[4];
                        var grounds = new string[4];
                        var heights = new int[4];
                        var exits = new string[4];
                        for (var side = 0; side < 4; side++)
                        {
                            int way = AreaWay(laying, SideDirections[side], SideDirections);
                            edges[way] = Short(layout.Named(slot.Edge(side)));

                            // THE SIDE'S PAIR OF NUMBERS after the four edge types - poe_data_tools' exit and virtual exit,
                            // annalithic's edge length and an unknown - in the same side order as the types.
                            exits[way] = string.Create(CultureInfo.InvariantCulture, $"{slot.Numbers[6 + (2 * side)]}/{slot.Numbers[7 + (2 * side)]}");
                            int corner = AreaWay(laying, CornerDirections[side], CornerDirections);
                            grounds[corner] = Short(layout.Named(slot.Ground(side)));
                            heights[corner] = slot.Numbers.Length > 18 + side ? slot.Numbers[18 + side] : 0;
                        }

                        bool rim = column == 0 || line == 0 || column == layout.Width - 1 || line == layout.Height - 1;
                        (int X, int Y) cell = (at.X + cellX, at.Y + cellY);
                        (asks.TryGetValue(cell, out List<CellAsk>? here) ? here : asks[cell] = []).Add(new CellAsk(name, column, line, edges, grounds, heights, exits, rim));
                    }
                }
            }
        }

        // EACH CELL: the laid tile's types by the area's sides and corners, where it is a whole one-by-one tile.
        (string[] Edges, string[] Grounds, string Path)? Laid((int X, int Y) cell)
        {
            string path = laid.PathAt(cell.X, cell.Y);
            int placement = laid.PlacementAt(cell.X, cell.Y);
            if (placement < 0 || laid.SubAt(cell.X, cell.Y) is not (0, 0) || !identities.TryGetValue(path, out TileIdentity? tile)
                || tile.Width != 1 || tile.Height != 1 || tile.Edges.Count != 4 || tile.Grounds.Count != 4)
            {
                return null;
            }

            (int xx, int xy, int yx, int yy) = TileOrientation.OfPlacement(placement).Turn;
            var edges = new string[4];
            var grounds = new string[4];
            for (var side = 0; side < 4; side++)
            {
                edges[Way(xx, xy, yx, yy, SideDirections[side], SideDirections)] = Short(tile.Edges[side]);
                grounds[Way(xx, xy, yx, yy, CornerDirections[side], CornerDirections)] = Short(tile.Grounds[side]);
            }

            return (edges, grounds, path);
        }

        var pairs = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        var corners = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        var alone = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        var byPairs = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        var joins = new SortedDictionary<string, SortedDictionary<string, int>>(StringComparer.Ordinal);
        int shared = 0, sharedLaid = 0, aloneRim = 0, ruled = 0, ruleHeld = 0, sidesMet = 0, eitherHeld = 0;
        var detail = new StringBuilder();
        foreach (((int X, int Y) cell, List<CellAsk> here) in asks.OrderBy(one => one.Key.Y).ThenBy(one => one.Key.X))
        {
            List<CellAsk> rooms = [.. here.DistinctBy(one => one.Room)];
            (string[] Edges, string[] Grounds, string Path)? tile = Laid(cell);
            if (rooms.Count == 1)
            {
                if (rooms[0].Rim && tile is { } lone)
                {
                    aloneRim++;
                    for (var side = 0; side < 4; side++)
                    {
                        Tally(alone, Said(rooms[0].Edges[side]), lone.Edges[side]);
                    }
                }

                continue;
            }

            shared++;
            if (tile is not { } laidHere)
            {
                continue;
            }

            sharedLaid++;
            CellAsk a = rooms[0], b = rooms[1];
            detail.Append(CultureInfo.InvariantCulture, $"  cell {cell.X}, {cell.Y} - {a.Room} slot {a.Column},{a.Line}{(a.Rim ? " rim" : string.Empty)} and {b.Room} slot {b.Column},{b.Line}{(b.Rim ? " rim" : string.Empty)}{(rooms.Count > 2 ? $" and {rooms.Count - 2} more" : string.Empty)}; laid {Short(laidHere.Path)}:");
            for (var side = 0; side < 4; side++)
            {
                string ea = a.Edges[side], eb = b.Edges[side], got = laidHere.Edges[side];
                if (ea == "-" && eb == "-" && got == "-")
                {
                    continue;
                }

                sidesMet++;
                string key = string.CompareOrdinal(ea, eb) <= 0 ? $"{Said(ea)} + {Said(eb)}" : $"{Said(eb)} + {Said(ea)}";
                Tally(pairs, key, got);
                if (ea != "-" && eb != "-")
                {
                    // BOTH NAME A TYPE: the two slots' number pairs on that side beside what was laid - what tells the
                    // edge combinations' lines for one pair of types apart, if the files say it anywhere.
                    string numbers = string.CompareOrdinal(a.Pairs[side], b.Pairs[side]) <= 0 ? $"{a.Pairs[side]} + {b.Pairs[side]}" : $"{b.Pairs[side]} + {a.Pairs[side]}";
                    Tally(byPairs, $"{key}, numbers {numbers}", got);

                    // AND BY JOIN - the two rooms whose rims meet - to tell a choice made once a join from one made a side.
                    string join = string.CompareOrdinal(a.Room, b.Room) <= 0 ? $"{a.Room} + {b.Room}" : $"{b.Room} + {a.Room}";
                    Tally(joins, $"{join}, {key}", got);
                }

                eitherHeld += got == Said(ea) || got == Said(eb) ? 1 : 0;
                string rule = string.Empty;
                if (rules.TryGetValue((ea, eb), out List<EdgeCombination>? made))
                {
                    ruled++;
                    bool held = made.Any(one => Said(Short(one.Result)) == got);
                    ruleHeld += held ? 1 : 0;
                    rule = $" (.ecf: {string.Join(" or ", made.Select(one => Said(Short(one.Result)) + (one.Number is { } n ? " " + n.ToString(CultureInfo.InvariantCulture) : string.Empty)))}{(held ? ", held" : ", NOT held")})";
                }

                detail.Append(CultureInfo.InvariantCulture, $" {AreaSides[side]} {Said(ea)} {a.Pairs[side]} | {Said(eb)} {b.Pairs[side]} -> {got}{rule};");
            }

            for (var corner = 0; corner < 4; corner++)
            {
                string ga = a.Grounds[corner], gb = b.Grounds[corner];
                string key = string.CompareOrdinal(ga, gb) <= 0 ? $"{Said(ga)} + {Said(gb)}" : $"{Said(gb)} + {Said(ga)}";
                Tally(corners, key, laidHere.Grounds[corner]);
            }

            detail.Append(CultureInfo.InvariantCulture, $" heights {string.Join(',', a.Heights)} | {string.Join(',', b.Heights)}").AppendLine();
        }

        said.AppendLine();
        said.AppendLine(CultureInfo.InvariantCulture,
            $"joins: {instances} placed rooms, {asks.Count} cells under their one-by-one slots, {shared} of them under two rooms or more, {sharedLaid} of those with a whole one-by-one tile laid; {sidesMet} sides met there, the laid side one room's type at {eitherHeld}; {ruled} met a pair the edge combinations name, the laid side what they make at {ruleHeld}");

        said.AppendLine().AppendLine("=== where two rooms' slots share a cell: the two edge types that met on a side (sorted), and what the laid tile has there");
        Tallies(said, pairs, rules);
        said.AppendLine().AppendLine("=== where both name a type: the two slots' number pairs on that side (each slot's pair after its four edge types), and what was laid");
        Tallies(said, byPairs, null);
        said.AppendLine().AppendLine("=== the same by join: the two rooms whose slots met, and what was laid on the sides where both name a type");
        Tallies(said, joins, null);
        said.AppendLine().AppendLine("=== the same for the corners' ground types");
        Tallies(said, corners, null);
        said.AppendLine().AppendLine(CultureInfo.InvariantCulture, $"=== for comparison, the {aloneRim} rim cells one room alone covers: the slot's edge type on a side, and the laid tile's there");
        Tallies(said, alone, null);
        said.AppendLine().AppendLine("=== every shared cell with a whole one-by-one tile: both slots' edge types and the laid tile's on each side of the area, then both slots' corner heights").Append(detail);
        return said.ToString();

        static string Said(string type) => type.Length == 0 ? "-" : type;

        static void Tally(SortedDictionary<string, SortedDictionary<string, int>> into, string key, string value)
        {
            SortedDictionary<string, int> got = into.TryGetValue(key, out SortedDictionary<string, int>? known) ? known : into[key] = new(StringComparer.Ordinal);
            got[value] = got.GetValueOrDefault(value) + 1;
        }
    }

    /// <summary>Each key's counts, most first, with what the edge combinations make of it where they name it.</summary>
    private static void Tallies(StringBuilder said, SortedDictionary<string, SortedDictionary<string, int>> tallies, Dictionary<(string, string), List<EdgeCombination>>? rules)
    {
        foreach ((string key, SortedDictionary<string, int> got) in tallies.OrderByDescending(one => one.Value.Values.Sum()))
        {
            said.Append(CultureInfo.InvariantCulture, $"  {key}: {string.Join(", ", got.OrderByDescending(one => one.Value).Select(one => $"{one.Key} x{one.Value}"))}");
            string[] two = key.Split(" + ");
            if (rules is not null && two.Length == 2 && rules.TryGetValue((two[0], two[1]), out List<EdgeCombination>? made))
            {
                said.Append(" - .ecf makes ").Append(string.Join(" or ", made.Select(one => (one.Result.Length == 0 ? "-" : Short(one.Result)) + (one.Number is { } n ? " " + n.ToString(CultureInfo.InvariantCulture) : string.Empty))));
            }

            said.AppendLine();
        }
    }

    /// <summary>Which of the four directions given a room's side or corner lies along once the room is laid (RoomFinder.Laying's linear part).</summary>
    private static int AreaWay(Matrix3x2 laying, (int X, int Y) direction, (int X, int Y)[] ways)
    {
        int ax = (int)MathF.Round((direction.X * laying.M11) + (direction.Y * laying.M21));
        int ay = (int)MathF.Round((direction.X * laying.M12) + (direction.Y * laying.M22));
        return Array.IndexOf(ways, (ax, ay));
    }

    /// <summary>Which of the four directions given a tile's side or corner lies along once the tile is placed (TileOrientation.Turn).</summary>
    private static int Way(int xx, int xy, int yx, int yy, (int X, int Y) direction, (int X, int Y)[] ways)
        => Array.IndexOf(ways, ((xx * direction.X) + (xy * direction.Y), (yx * direction.X) + (yy * direction.Y)));

    /// <summary>Finds where each overlay room of the master's list fits the laid tiles, and holds the placed rooms' missing props against those places; writes the table beside the capture's files.</summary>
    [Fact]
    public void THEOVERLAYSAreHeldAgainstThePlacedRoomsFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = OverlaysReplay(folder);
        File.WriteAllText(Path.Combine(folder, "overlays-replay.txt"), replay);
        Assert.StartsWith("overlays: ", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// overlays-replay.txt: the master's overlay list (.toy) whole; every place each overlay room's slots all fit the tiles the game laid; and every prop line of every room the doodads placed - its entity there or not - by whether it stands under such a place, and by whether the tile under it is what the room's slot asks.
    /// </summary>
    /// <remarks>
    /// WHAT IT ASKS: an overlay line may say <c>+SuppressUnderlyingDoodads</c> - Augury's both do - and the talk has the overlay
    /// pass replace a known shape of the layout with other geometry once the layout is made. If the words do what they say,
    /// a placed room's props are missing where an overlay was laid over it, and present elsewhere; and the tiles there are the
    /// overlay's, not what the room's own slot asks. Both are counted, line by line, before the placing is told about it: a
    /// room whose doodads are short of the two thirds could then be one an overlay covered, not one the area did not lay.
    /// AN OVERLAY'S PLACE IS WHERE ITS EVERY SLOT FITS the laid tile on the slot's cell, by the search's own rule (an unnamed
    /// type as any), every way round - the shape the pass looks for is the shape it leaves.
    /// </remarks>
    public static string OverlaysReplay(string folder)
    {
        var said = new StringBuilder();
        var entries = new List<OverlayEntry>();
        foreach ((string path, string text) in MasterSections(folder).Where(one => one.Path.EndsWith(OverlayList.Extension, StringComparison.OrdinalIgnoreCase)))
        {
            OverlayList list = OverlayList.Parse(text);
            entries.AddRange(list.Entries);
            said.AppendLine(CultureInfo.InvariantCulture, $"overlays: {path}, version {list.Version}, {list.Headers.Count} groups ({string.Join("; ", list.Headers)}), {list.Entries.Count} rooms");
            foreach (OverlayEntry entry in list.Entries)
            {
                said.AppendLine(CultureInfo.InvariantCulture, $"  group {entry.Group}: {entry.Weight} {Short(entry.Room)} {string.Join(' ', entry.Words)}");
            }
        }

        if (entries.Count == 0)
        {
            said.Insert(0, $"overlays: no overlay list in the capture - {CaptureReport.MastersFile} from 0.1.184 on carries the master's\n");
            return said.ToString();
        }

        TerrainGrid? grid = Terrain(folder, out string how);
        said.AppendLine(how);
        Dictionary<string, TileIdentity>? identities = Identities(folder);
        if (grid?.Tiles is not { } laid || grid.Ground is not { } ground || identities is null)
        {
            said.AppendLine("nothing to fit the overlays to without the terrain's tiles, its ground and the tile identities");
            return said.ToString();
        }

        // EVERY PLACE EACH OVERLAY FITS, and the cells of those whose line says it suppresses what lies beneath.
        Placing placing = Place(folder);
        var suppressed = new HashSet<(int X, int Y)>();
        var overlaid = new HashSet<(int X, int Y)>();
        foreach (OverlayEntry entry in entries)
        {
            RoomLayout? layout = placing.Rooms.FirstOrDefault(one => string.Equals(one.Room, entry.Room, StringComparison.OrdinalIgnoreCase)).Layout;
            if (layout is null || layout.Slots.Count == 0)
            {
                said.AppendLine(CultureInfo.InvariantCulture, $"{Short(entry.Room)}: not in {CaptureReport.RoomFilesFile}, or its grid did not read");
                continue;
            }

            List<RoomCandidate> fits = Fits(layout, laid, identities);
            bool suppressing = entry.Says("+SuppressUnderlyingDoodads");
            said.AppendLine(CultureInfo.InvariantCulture,
                $"{Short(entry.Room)}: {layout.Width} x {layout.Height}, module {layout.ModuleSide} {layout.ModuleOverlap}, {layout.Doodads.Count} doodad lines - every slot fits the laid tiles at {fits.Count} places{(suppressing ? ", its line suppressing what lies beneath" : string.Empty)}");
            foreach (RoomCandidate fit in fits.Take(40))
            {
                said.AppendLine(CultureInfo.InvariantCulture, $"  tile {fit.X}, {fit.Y}, {RoomFinder.Said(fit.Turn)}, {fit.Width} x {fit.Height}: laid {Under(laid, fit)}");
            }

            foreach (RoomCandidate fit in fits)
            {
                for (var dy = 0; dy < fit.Height; dy++)
                {
                    for (var dx = 0; dx < fit.Width; dx++)
                    {
                        overlaid.Add((fit.X + dx, fit.Y + dy));
                        if (suppressing)
                        {
                            suppressed.Add((fit.X + dx, fit.Y + dy));
                        }
                    }
                }
            }
        }

        // EVERY PROP LINE OF EVERY ROOM THE DOODADS PLACED, outside the overlays themselves: its entity within a tile of
        // where it stands, or not, against the overlays' cells and against the room's own tiles there.
        HashSet<string> overlayRooms = new(entries.Select(one => one.Room), StringComparer.OrdinalIgnoreCase);
        var byPath = placing.Survey.Found.GroupBy(one => one.Path, StringComparer.OrdinalIgnoreCase).ToDictionary(one => one.Key, one => one.ToList(), StringComparer.OrdinalIgnoreCase);
        float side = RoomDoodadFinder.WithinUnits;
        bool[] walkable = grid.WalkableTileMask();
        int[,] table = new int[3, 2];
        int[,] tiled = new int[2, 2];
        var missing = new StringBuilder();
        for (var one = 0; one < placing.Rooms.Count; one++)
        {
            (string room, RoomLayout layout) = placing.Rooms[one];
            if (overlayRooms.Contains(room))
            {
                continue;
            }

            (RoomScorer? scorer, _) = RoomFinder.Scorer(layout, ground, grid.TilesX, grid.TilesY, laid, path => identities.GetValueOrDefault(path), walkable);
            foreach (RoomDoodadPlace place in placing.Placed[one].Places.Places)
            {
                RoomCandidate at = place.Where;
                HashSet<(int X, int Y)> unlike = scorer?.Score(at.X, at.Y, at.Turn) is { } scored ? [.. scored.Misses.Tiles] : [];
                Matrix3x2 laying = RoomFinder.Laying(layout.Width, layout.Height, at.Turn);
                foreach (RoomDoodad line in layout.Doodads)
                {
                    if (!line.IsProp || !byPath.TryGetValue(line.Stub, out List<DoodadSighting>? same))
                    {
                        continue;
                    }

                    string model = line.Ao.Replace('\\', '/');
                    List<DoodadSighting> mine = [.. same.Where(sighting => sighting.Model.Length == 0 || model.Length == 0 || string.Equals(sighting.Model, model, StringComparison.OrdinalIgnoreCase))];
                    if (mine.Count == 0)
                    {
                        continue;
                    }

                    Vector2 inRoom = Vector2.Transform(new Vector2(line.X, line.Y) / TerrainGrid.CellsPerTile, laying);
                    var stands = new Vector2(at.X + inRoom.X, at.Y + inRoom.Y) * side;
                    bool present = mine.Any(sighting => Vector2.Distance(new Vector2(sighting.X, sighting.Y), stands) <= side);
                    (int X, int Y) cell = ((int)MathF.Floor(stands.X / side), (int)MathF.Floor(stands.Y / side));
                    int where = suppressed.Contains(cell) ? 0 : overlaid.Contains(cell) ? 1 : 2;
                    bool unlikeHere = unlike.Contains(cell);
                    table[where, present ? 0 : 1]++;
                    tiled[unlikeHere ? 0 : 1, present ? 0 : 1]++;
                    if (!present)
                    {
                        missing.AppendLine(CultureInfo.InvariantCulture,
                            $"  {Short(room)}@{at.X},{at.Y} {Short(line.Ao)} at tile {cell.X}, {cell.Y}: {(where == 0 ? "under a suppressing overlay's place" : where == 1 ? "under an overlay's place" : "under no overlay")}, the tile there {(unlikeHere ? "NOT what the room's slot asks" : "what the room's slot asks")} - laid {Short(laid.PathAt(cell.X, cell.Y))}");
                    }
                }
            }
        }

        said.AppendLine();
        said.AppendLine("=== every prop line of the rooms the doodads placed whose path and model stand in the area: its entity within a tile of where it stands (present) or not (missing)");
        said.AppendLine(CultureInfo.InvariantCulture, $"  under a suppressing overlay's place: {table[0, 0]} present, {table[0, 1]} missing");
        said.AppendLine(CultureInfo.InvariantCulture, $"  under another overlay's place: {table[1, 0]} present, {table[1, 1]} missing");
        said.AppendLine(CultureInfo.InvariantCulture, $"  under no overlay: {table[2, 0]} present, {table[2, 1]} missing");
        said.AppendLine(CultureInfo.InvariantCulture, $"  on a tile not what the room's slot asks: {tiled[0, 0]} present, {tiled[0, 1]} missing");
        said.AppendLine(CultureInfo.InvariantCulture, $"  on a tile the room's slot asks for: {tiled[1, 0]} present, {tiled[1, 1]} missing");
        said.AppendLine().AppendLine("=== every missing prop line").Append(missing);

        // THE ROOMS THE DOODADS COULD NOT PLACE, at the best tile their vote found: how many of their deciding lines
        // stand on a suppressing overlay's place there, and whether the two thirds would be reached without them.
        said.AppendLine().AppendLine("=== the rooms the doodads could not place, at the tile their vote led on: the props there, those on a suppressing overlay's place, and the two thirds of the rest");
        for (var one = 0; one < placing.Rooms.Count; one++)
        {
            (string room, RoomLayout layout) = placing.Rooms[one];
            RoomDoodadPlaces places = placing.Placed[one].Places;
            if (overlayRooms.Contains(room) || places.Places.Count > 0 || BestTile().Match(places.Why) is not { Success: true } best)
            {
                continue;
            }

            int x = int.Parse(best.Groups[1].Value, CultureInfo.InvariantCulture), y = int.Parse(best.Groups[2].Value, CultureInfo.InvariantCulture);
            int turn = Array.IndexOf(["as written", "turned 90", "turned 180", "turned 270"], best.Groups[3].Value) + (best.Groups[4].Success ? 4 : 0);
            Matrix3x2 laying = RoomFinder.Laying(layout.Width, layout.Height, turn);
            int props = 0, present = 0, under = 0, underMissing = 0;
            foreach (RoomDoodad line in layout.Doodads)
            {
                if (!line.IsProp || !byPath.TryGetValue(line.Stub, out List<DoodadSighting>? same))
                {
                    continue;
                }

                string model = line.Ao.Replace('\\', '/');
                List<DoodadSighting> mine = [.. same.Where(sighting => sighting.Model.Length == 0 || model.Length == 0 || string.Equals(sighting.Model, model, StringComparison.OrdinalIgnoreCase))];
                if (mine.Count == 0)
                {
                    continue;
                }

                props++;
                Vector2 inRoom = Vector2.Transform(new Vector2(line.X, line.Y) / TerrainGrid.CellsPerTile, laying);
                var stands = new Vector2(x + inRoom.X, y + inRoom.Y) * side;
                bool here = mine.Any(sighting => Vector2.Distance(new Vector2(sighting.X, sighting.Y), stands) <= side);
                present += here ? 1 : 0;
                if (suppressed.Contains(((int)MathF.Floor(stands.X / side), (int)MathF.Floor(stands.Y / side))))
                {
                    under++;
                    underMissing += here ? 0 : 1;
                }
            }

            int rest = props - underMissing;
            said.AppendLine(CultureInfo.InvariantCulture,
                $"  {Short(room)} at tile {x}, {y}, {RoomFinder.Said(turn)}: {present} of {props} props present, {under} on a suppressing overlay's place ({underMissing} of them missing) - {present} of the other {rest}{(rest > 0 && present * 3 >= rest * 2 ? ", two thirds" : ", short of two thirds")}");
        }

        return said.ToString();
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"at tile (\d+), (\d+), (as written|turned 90|turned 180|turned 270)(, mirrored)?")]
    private static partial System.Text.RegularExpressions.Regex BestTile();

    /// <summary>Every place a room's every slot fits the laid tile on the slot's cell, every way round - an unnamed type as any, a big slot's tile laid whole from its first piece.</summary>
    private static List<RoomCandidate> Fits(RoomLayout layout, TerrainTiles laid, Dictionary<string, TileIdentity> identities)
    {
        var slots = new List<(int Left, int Top, int Width, int Height, Ask Ask)>();
        for (var line = 0; line < layout.Height; line++)
        {
            for (var column = 0; column < layout.Width; column++)
            {
                RoomSlot slot = layout.SlotAt(column, line);
                if (slot.IsTile)
                {
                    (int left, int top) = slot.Corner(column, line);
                    slots.Add((left, top, slot.Width, slot.Height, AskOf(layout, slot)));
                }
            }
        }

        var fits = new List<RoomCandidate>();
        for (var turn = 0; turn < 8; turn++)
        {
            (int wide, int tall) = (turn & 1) == 0 ? (layout.Width, layout.Height) : (layout.Height, layout.Width);
            for (var y = 0; y + tall <= laid.Height; y++)
            {
                for (var x = 0; x + wide <= laid.Width; x++)
                {
                    bool all = slots.Count > 0;
                    foreach ((int left, int top, int width, int height, Ask ask) in slots)
                    {
                        (int cx, int cy) = RoomFinder.CornerOf(left, top, width, height, layout.Width, layout.Height, turn);
                        if (laid.SubAt(x + cx, y + cy) is not (0, 0) || !identities.TryGetValue(laid.PathAt(x + cx, y + cy), out TileIdentity? tile) || !Alike(ask, tile))
                        {
                            all = false;
                            break;
                        }
                    }

                    if (all)
                    {
                        fits.Add(new RoomCandidate(x, y, turn, wide, tall, 0, 0));
                    }
                }
            }
        }

        return fits;
    }

    /// <summary>Reads the terrain's two cell grids back out of the capture's recording and writes the same report the capture's pass wrote, beside it.</summary>
    [Fact]
    public void THELANDSCAPEIsReadAgainFromACapture()
    {
        if (Folder is not { Length: > 0 } folder)
        {
            return;
        }

        string replay = LandscapeReplay(folder);
        File.WriteAllText(Path.Combine(folder, "landscape-replay.txt"), replay);
        Assert.StartsWith("landscape: ", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// landscape-replay.txt: CaptureLandscape's report over the recording's own reads - both grids, the terrain's ground and the player - so a change to the report is held against a capture without the game.
    /// </summary>
    /// <remarks>A capture from before the part (CaptureParts.Landscape) holds the walkable grid and not the second one, and says so.</remarks>
    public static string LandscapeReplay(string folder)
    {
        string path = CaptureParts.Find(folder, CaptureReport.MemoryFile);
        if (!File.Exists(path))
        {
            return $"landscape: not read - no {CaptureReport.MemoryFile} in the capture";
        }

        using var replay = ReplayMemoryReader.Load(File.OpenRead(path));
        if (!replay.ResolvedStatics.TryGetValue("GameStates", out ulong gameStates))
        {
            return "landscape: not read - the recording names no GameStates static";
        }

        var rotation = new TerrainRotationTables(
            replay.ResolvedStatics.GetValueOrDefault("TerrainRotationSelector"),
            replay.ResolvedStatics.GetValueOrDefault("TerrainRotatorHelper"));
        PoEformance.Core.Schema.OffsetSchema schema = RealSessionTests.LiveSchema();
        WorldSnapshot snapshot = new WorldReader(replay, schema, rotation).Read(gameStates, CaptureMemory.MostEntities);
        GameChainAddresses chain = GameChain.Resolve(replay, schema, gameStates);
        TerrainLayers layers = TerrainLayers.Read(replay, schema, chain.AreaInstance);
        return CaptureLandscape.Report(layers, snapshot.Terrain, snapshot.Player?.WorldX, snapshot.Player?.WorldY);
    }

    /// <summary>The masters area-masters.txt writes whole, read back - none where the capture has no such file (before 0.1.184).</summary>
    private static List<MasterFile> Masters(string folder)
        => [.. MasterSections(folder).Where(one => one.Path.EndsWith(".tsi", StringComparison.OrdinalIgnoreCase)).Select(one => MasterFile.Parse(one.Text))];

    /// <summary>The lattices the capture's masters name - see RoomLattice.</summary>
    private static RoomLattices Lattices(string folder) => RoomLattices.From(Masters(folder));

    /// <summary>Every file area-masters.txt writes whole, by its path, its lines as the file has them.</summary>
    private static List<(string Path, string Text)> MasterSections(string folder)
    {
        var sections = new List<(string Path, string Text)>();
        string path = CaptureParts.Find(folder, CaptureReport.MastersFile);
        if (!File.Exists(path))
        {
            return sections;
        }

        string current = string.Empty;
        var text = new StringBuilder();
        foreach (string raw in File.ReadLines(path).Append("##### "))
        {
            string line = raw.TrimEnd('\r');
            if (line.StartsWith("##### ", StringComparison.Ordinal))
            {
                if (current.Length > 0)
                {
                    sections.Add((current, text.ToString()));
                }

                int named = line.IndexOf("  (", StringComparison.Ordinal);
                current = (named > 6 ? line[6..named] : line[6..]).Trim();
                text.Clear();
            }
            else if (line.StartsWith("  | ", StringComparison.Ordinal))
            {
                text.AppendLine(line[4..]);
            }
            else if (line == "  |")
            {
                text.AppendLine();
            }
        }

        return sections;
    }

    /// <summary>A grid side against a lattice: the buffer, whole steps, the overlap and the buffer again - or what is left over.</summary>
    private static string Whole(int tiles, RoomLattice lattice)
    {
        int inner = tiles - (2 * lattice.Buffer) - lattice.Overlap;
        return inner >= 0 && inner % lattice.Step == 0
            ? string.Create(CultureInfo.InvariantCulture, $"{tiles} = {lattice.Buffer} + {inner / lattice.Step} x {lattice.Step} + {lattice.Overlap} + {lattice.Buffer}, whole")
            : string.Create(CultureInfo.InvariantCulture, $"{tiles} is NOT {lattice.Buffer} + whole steps of {lattice.Step} + {lattice.Overlap} + {lattice.Buffer} - {Residue(inner, lattice.Step)} over");
    }

    private static int Residue(int at, int step) => ((at % step) + step) % step;
}
