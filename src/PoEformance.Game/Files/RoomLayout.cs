using System.Globalization;
using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// One of a room's zones: a named rectangle of its cells, inclusive, in the doodads' own unit, with the object type and environment it may name.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>Zone</c> (<c>name x_min y_min x_max y_max</c>, from version 35 the name quoted and two quoted strings and a
/// whole number after - <c>disable_teleports</c>, <c>env_file</c> and <c>uint1</c> there). READ FOR THE WOBBLE: a wobble core's
/// WobblePoint names its <c>sector</c> - "caravan" for Port's ship - and Port's boss room writes
/// <c>"caravan" 26 -102 261 454 "" "" 0</c>, the rectangle round the ship, its least x the ship's hull's own (cell 27). Beside it
/// <c>"hide_teleports" ... "Metadata/MiscellaneousObjects/SectorDisableInstantTeleports"</c> and four <c>no_movement_skills</c>: a
/// zone may run past the room's grid, and one name may stand for several.
/// </remarks>
public readonly record struct RoomZone(string Name, int LeastX, int LeastY, int MostX, int MostY, string Type, string Environment)
{
    /// <summary>Whether a cell lies in the zone, its edges included.</summary>
    public bool Holds(int x, int y) => x >= LeastX && x <= MostX && y >= LeastY && y <= MostY;
}

/// <summary>One doodad a room places: a model, where, turned how far, and how big.</summary>
/// <param name="X">Across the room, in the file's own unit - see <see cref="RoomLayout"/>.</param>
/// <param name="Y">Down the room, in the same unit.</param>
/// <param name="Turn">poe_data_tools' "radians" - an angle, 0 to 2 pi in older files and -pi to pi in later ones.</param>
/// <param name="Scale">Mostly 1; poe_data_tools sees 0 to 250.</param>
/// <param name="Ao">The doodad's <c>.ao</c> - the same kind of file a monster or an item is drawn from.</param>
/// <param name="Stub">
/// The virtual file stub after it: <c>Metadata/MiscellaneousObjects/Doodad</c> for most, and an object's
/// own type for the few the game makes an entity of - which is how one is found in memory.
/// </param>
public readonly record struct RoomDoodad(int X, int Y, float Turn, float Scale, string Ao, string Stub)
{
    /// <summary>
    /// The first of the line's counted floats - the doodad's height in the area's own z - or null where it counts none and the doodad stands on the ground.
    /// </summary>
    /// <remarks>
    /// NEITHER REFERENCE NAMES IT. poe_data_tools reads it as "floats", a count and that many values;
    /// annalithic's poeformats as "unk7". THE GAME DOES: in seepage's 2x2_offices_01 twelve of fifty-four
    /// doodads carry one value - 0, 10, -45, -115, -240 - and none carries two; a VaalPotCluster01 whose
    /// line says -115 has a Render z of -115, and the room laid in its area came out with every doodad
    /// where the game has it once each was set at its value rather than on the ground. Up is minus z, so
    /// -240 is a light hanging over its pots and 10 a mud pile sunk into the floor.
    /// </remarks>
    public float? Height { get; init; }

    /// <summary>
    /// The first of the line's counted float pairs, from version 34 - a place in the room's own units, finer than its cell.
    /// </summary>
    /// <remarks>
    /// READ, NOT YET DRAWN WITH. The four in seepage's offices sit about half a cell past their cell's
    /// corner - 3386.6, 2125.29 for cell 311, 195 at 250 / 23 a cell - which reads as the exact place the
    /// cell rounds; the doodad heights line says how far each is from where the entity really is.
    /// </remarks>
    public Vector2? Exact { get; init; }

    /// <summary>
    /// Whether the line is a plain prop - a stub under <c>Metadata/MiscellaneousObjects/Doodad</c> - rather than a scripted object the game spawns and removes with play.
    /// </summary>
    /// <remarks>
    /// SEEN IN TWO CAPTURES OF ONE AREA, forty minutes apart, the player on the same spot: of 844
    /// doodad entities not one plain prop moved or went, while 17 power-line pieces
    /// (Gallows/.../Objects/GlyphPowerLine, MachinariumPoweredObject) had gone, 10 more had come, and a
    /// checkpoint (Checkpoints/Checkpoint_Endgame) had appeared where the player had touched it. A
    /// room's props say where it stands for as long as the area lasts; its scripted objects say what
    /// the player has done. So the props decide a placing - see RoomDoodadFinder - and the others are
    /// counted beside them.
    ///
    /// THE MECHANISM, learned from a third capture whose client held one checkpoint entity while the
    /// game's map drew four: a scripted object is the server's entity, in the client only while it
    /// stands inside the client's network bubble, and a checkpoint is read only once the player comes
    /// near it. The props the client builds itself from the room files, so they stand across the
    /// whole area from the first frame. The game names no checkpoint in its UI either, beyond the
    /// one being hovered, so there is no second road to the ones outside the bubble.
    /// </remarks>
    public bool IsProp => IsPropPath(Stub);

    /// <summary>Whether a stub is a plain prop's - see <see cref="IsProp"/>. For an entity's path as much as a line's.</summary>
    public static bool IsPropPath(string stub)
    {
        ArgumentNullException.ThrowIfNull(stub);
        return stub.StartsWith("Metadata/MiscellaneousObjects/Doodad", StringComparison.OrdinalIgnoreCase);
    }
}

/// <summary>A point of interest a room names: where, which way, and what for - "mapboss", "checkpoint", "rare" and the like.</summary>
/// <remarks>
/// poe_data_tools' <c>poi</c>, field for field: two whole numbers, a float and a quoted tag, in groups the version counts (9, 10, 5 or
/// 6) between the numbers before the tag and the grid. x and y count the room's cells, as a doodad line's do; the turn is in radians.
/// What a tag means the game says nowhere a reference reads - a room names its boss's place "mapboss", "force:mapboss" or
/// "mapboss1" - so the tag is kept as written. A line not of that shape is passed over, not read shifted.
/// </remarks>
/// <param name="Group">Which of the file's point groups it is in, from nought.</param>
/// <param name="X">Its cell across the room.</param>
/// <param name="Y">Its cell down the room.</param>
/// <param name="Turn">Its angle, in radians.</param>
/// <param name="Tag">Its tag, unquoted - often empty.</param>
public readonly record struct RoomPoint(int Group, int X, int Y, float Turn, string Tag)
{
    /// <summary>One line, or null where it is not three numbers and a quoted tag.</summary>
    public static RoomPoint? Parse(int group, string line)
    {
        ArgumentNullException.ThrowIfNull(line);
        int open = line.IndexOf('"', StringComparison.Ordinal);
        int close = line.LastIndexOf('"');
        if (open < 0 || close <= open)
        {
            return null;
        }

        string[] numbers = line[..open].Split((char[])[' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
        return numbers.Length == 3
            && int.TryParse(numbers[0], NumberStyles.None, CultureInfo.InvariantCulture, out int x)
            && int.TryParse(numbers[1], NumberStyles.None, CultureInfo.InvariantCulture, out int y)
            && float.TryParse(numbers[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float turn)
            ? new RoomPoint(group, x, y, turn, line[(open + 1)..close])
            : null;
    }
}

/// <summary>
/// One slot of a room's grid: what kind it is and, for a <c>k</c> slot, the tile it asks for.
/// </summary>
/// <remarks>
/// THE k SLOT'S NUMBERS, in poe_data_tools' order and annalithic's names, which agree:
///
///     w h   edge types down right up left   exit pairs down right up left (eight)
///     corner ground types down-left down-right up-right up-left   corner heights (four)
///     tag   [origin - which corner of its footprint the record sits at, see Origin]
///
/// A type is an index into the room's string table counted from ONE, nought for none. WHICH WAY
/// "DOWN" IS was settled by the files themselves, not by either reference: neighbouring slots share
/// their corners and sides, and in the channel's 1open_01.arm - a nine by nine grid of one by one
/// slots - only one reading makes them agree. Down is the side towards the grid's FIRST line, so a
/// slot's down-left corner is the grid corner at its own column and line, its up-left one a line
/// further on; read that way, 192 shared corners, 120 shared sides and their exit pairs agree with
/// none against them, while the best of the seven other readings leaves 21 corners in conflict.
/// </remarks>
/// <param name="Kind">The slot's letter: k, f, s, o or n.</param>
/// <param name="Numbers">A k slot's numbers as written, an f slot's one fill type; empty otherwise.</param>
public readonly record struct RoomSlot(char Kind, int[] Numbers)
{
    /// <summary>Whether this asks for a tile - a k slot whose numbers are all there.</summary>
    public bool IsTile => Kind == 'k' && Numbers.Length >= 23;

    /// <summary>A k slot's width, from its first number.</summary>
    public int Width => IsTile ? Numbers[0] : 0;

    /// <summary>A k slot's height.</summary>
    public int Height => IsTile ? Numbers[1] : 0;

    /// <summary>A k slot's edge type on one side - 0 down, 1 right, 2 up, 3 left - as a string index from one, nought for none.</summary>
    public int Edge(int side) => IsTile ? Numbers[2 + side] : 0;

    /// <summary>A k slot's ground type at one corner - 0 down-left, 1 down-right, 2 up-right, 3 up-left - as a string index from one, nought for none.</summary>
    public int Ground(int corner) => IsTile ? Numbers[14 + corner] : 0;

    /// <summary>A k slot's tag, as a string index from one, nought for none.</summary>
    public int Tag => IsTile ? Numbers[22] : 0;

    /// <summary>
    /// Which corner of its footprint a k slot's record sits at - the 24th number, nought where the line has 23 or the number is not one of the four.
    /// </summary>
    /// <remarks>
    /// MEASURED, not read from a reference: neither parser names the number (poe_data_tools reads it as an
    /// optional trailing value). origin-replay.txt over Oasis holds every big slot of the ten rooms the
    /// doodads placed against the laid tile under its record's cell, mapped back into the room: 180 of 181
    /// records sit where this number says - 0 the footprint's first column and first line, 1 its last
    /// column and first line, 2 its last column and last line, 3 its first column and last line. The one
    /// that does not is the boss room's 17, 20, measured at columns 16 to 18 while its neighbour of the same
    /// file laid the same way measured 14 to 16 - two footprints cannot share a column, so it is the
    /// measurement's grouping of pieces that is off there, not the record. Before
    /// it was read, Oasis's boss room put its 21 by 15 arena running right and down from column 22 of a 25
    /// wide grid; it runs LEFT and UP from there.
    /// </remarks>
    public int Origin => IsTile && Numbers.Length > 23 && Numbers[23] is >= 0 and <= 3 ? Numbers[23] : 0;

    /// <summary>
    /// The first column and line of a k slot's footprint in the room, for a record at this column and line - see <see cref="Origin"/>.
    /// </summary>
    /// <remarks>
    /// IT MAY LIE OUTSIDE THE GRID. Oasis's rooms lay their rim tiles one tile past the grid on every side -
    /// a 13 by 13 room's three by three tiles cover columns -1 to 13, a 25 by 25 boss room's -1 to 25 - so a
    /// record at column 1 whose origin is 1 or 2 starts its footprint at column -1.
    /// </remarks>
    public (int Column, int Line) Corner(int column, int line) => Origin switch
    {
        1 => (column - Width + 1, line),
        2 => (column - Width + 1, line - Height + 1),
        3 => (column, line - Height + 1),
        _ => (column, line),
    };
}

/// <summary>
/// A room file (<c>.arm</c>), read as far as its doodads.
/// </summary>
/// <remarks>
/// WHAT A ROOM SAYS AND WHAT IT DOES NOT. Its slot grid names edge and ground TYPES - which tile goes
/// there is chosen when the area is generated, by a rule no reference writes down - so the grid is
/// not geometry and is read here only for its size. Its doodads ARE explicit: a model, a position, an
/// angle and a scale, which is the part that can be drawn exactly.
///
/// THE LAYOUT IS poe_data_tools' <c>arm</c> parser, line for line - UTF-16 text, one record per line,
/// blank lines skipped:
///
///     version N
///     count, then that many quoted strings
///     dimensions            side length [again, below 31] [one more, from 22]
///     numbers               one or more - their sum, twice, is how many "thingy" lines follow
///     "tag"
///     bools                 one or more
///     root slot             k w h ... | f n | s | o | n - a k slot's first two numbers are the grid
///     thingies              sum(numbers) * 2 lines
///     point-of-interest groups, 9 / 10 / 5 / 6 of them by version, each a group (below)
///     "string"              from 35
///     grid                  h lines of w slots
///     doodads               a group
///     doodad connections    a group, from 23
///     decals                a group
///     boss lines            from 22: a count, then that many lines of quoted strings
///     zones                 from 27: a group, counted below 33 and ended by -1 from it
///     tags                  optional: a count and that many words, on one line
///     ground overrides      optional: (w - 1) * (h - 1) types on one line, the grid's inner corners
///
/// A GROUP is a count line and that many lines below version 32, and lines up to a line reading
/// <c>-1</c> from 32. A doodad line, by version:
///
///     x y [n, then n pairs of floats: 34+] radians [four floats: 18+] bool [bool: 25+]
///     n, then n floats   scale   "file.ao"   "stub"   [n key=value: 36+]
///
/// zao's <c>arm.hpp</c> keeps the same fields - his own doodad reader is commented out - and names
/// the four floats a quaternion. Only the angle is used for the turn; the four are stepped over.
///
/// THE GROUND OVERRIDES name a ground type at each inner corner of the grid - the corners between
/// slots, whose own corner types are mostly nought - line by line in the grid's order. poe_data_tools
/// reads them and calls them overrides; that is the whole of what is known, so they are read as
/// written and a type there is taken as the corner's (see RoomFinder). Many rooms have no such line.
///
/// THE BOSS LINES carry poe_data_tools' warning: the last of them is often written without its line
/// end, so whatever follows its quoted strings is the start of the next line, and is read as one.
///
/// THE UNIT OF x AND y IS WRITTEN DOWN NOWHERE. Both are whole numbers, and neither reference says
/// whether they count the 23 cells of a tile or the 250 world units of one; see RoomModels for how
/// the view lets the file settle it.
/// </remarks>
public sealed class RoomLayout
{
    /// <summary>What a room's file name ends with.</summary>
    public const string Extension = ".arm";

    /// <summary>Nothing read.</summary>
    public static RoomLayout None { get; } = new() { Why = "nothing to read" };

    /// <summary>The file's version.</summary>
    public int Version { get; private init; }

    /// <summary>Tiles across, from the root slot - one where the root is not a k slot.</summary>
    public int Width { get; private init; } = 1;

    /// <summary>Tiles down.</summary>
    public int Height { get; private init; } = 1;

    /// <summary>
    /// The side of the module the room is cut to, in tiles - the dimension line's first number; nought where the file has none.
    /// </summary>
    /// <remarks>
    /// THE MASTER'S RoomUnitSize, WRITTEN INTO EVERY ROOM. poe_data_tools reads the line as
    /// <c>side_length</c> (twice below version 31) and, from 22, an unnamed <c>uint1</c>; neither says
    /// more. The captures do: every room of the area's own set carries its master's RoomUnitSize and
    /// RoomOverlap here - Augury "5 1", Marrow "9 1", Sulphuric Caverns "10 1" - and every one of the
    /// 548 rooms of the six committed captures is <see cref="ModuleOverlap"/> plus a whole number
    /// of (side - overlap) tiles each way: Augury's 5, 9, 13 and 21, Oasis's 7, 13, 19, 25 and 31. A fill
    /// room or a league's room carries a module of its own ("3 0", "1 0", "3 1"). See RoomLattice for
    /// where the module puts a room in its area.
    /// </remarks>
    public int ModuleSide { get; private init; }

    /// <summary>
    /// How many tiles two neighbouring modules share - the dimension line's last number from version 22 on; -1 where the file is older or the line has none. See <see cref="ModuleSide"/>.
    /// </summary>
    public int ModuleOverlap { get; private init; } = -1;

    /// <summary>The doodads, in the file's order.</summary>
    public IReadOnlyList<RoomDoodad> Doodads { get; private init; } = [];

    /// <summary>The string table, unquoted, in the file's order - what a slot's types index from one.</summary>
    public IReadOnlyList<string> Strings { get; private init; } = [];

    /// <summary>
    /// The slot grid, line by line as the file writes them, <see cref="Width"/> to a line - empty where it did not read; see <see cref="SlotsWhy"/>.
    /// </summary>
    public IReadOnlyList<RoomSlot> Slots { get; private init; } = [];

    /// <summary>Why the slot grid did not read, or empty. A grid that will not read leaves the doodads standing.</summary>
    public string SlotsWhy { get; private init; } = string.Empty;

    /// <summary>The zones, in the file's order - see <see cref="RoomZone"/>; empty where the file has none or its tail did not read as far.</summary>
    public IReadOnlyList<RoomZone> Zones { get; private init; } = [];

    /// <summary>The points of interest, every group's in the file's order - see <see cref="RoomPoint"/>.</summary>
    public IReadOnlyList<RoomPoint> Points { get; private init; } = [];

    /// <summary>
    /// The ground types the file names at the grid's inner corners, line by line, <see cref="Width"/> - 1 to a line, as string indices from one - empty where the file has no such line; see the class remarks.
    /// </summary>
    public IReadOnlyList<int> GroundOverrides { get; private init; } = [];

    /// <summary>Why what follows the doodads did not read as far as the ground overrides, or empty - empty too where the file simply has none.</summary>
    public string OverridesWhy { get; private init; } = string.Empty;

    /// <summary>The type the file names at an inner grid corner, (1, 1) to (<see cref="Width"/> - 1, <see cref="Height"/> - 1), as a string index from one; nought for none, or outside.</summary>
    public int OverrideAt(int u, int v)
        => GroundOverrides.Count == (Width - 1) * (Height - 1) && u >= 1 && u < Width && v >= 1 && v < Height
            ? GroundOverrides[((v - 1) * (Width - 1)) + u - 1]
            : 0;

    /// <summary>The slot at a column and grid line, line 0 being the file's first and the room's down side.</summary>
    public RoomSlot SlotAt(int column, int line)
        => Slots.Count == Width * Height && (uint)column < (uint)Width && (uint)line < (uint)Height
            ? Slots[(line * Width) + column]
            : default;

    /// <summary>A type a slot names, by its index from one: the string, or empty for nought or an index past the table.</summary>
    public string Named(int index) => index > 0 && index <= Strings.Count ? Strings[index - 1] : string.Empty;

    /// <summary>Why nothing was read, or empty.</summary>
    public string Why { get; private init; } = string.Empty;

    /// <summary>Whether it read.</summary>
    public bool Ready => Why.Length == 0;

    /// <summary>Reads a file's bytes, through the game's shared text decode.</summary>
    public static RoomLayout Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static RoomLayout Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        string[] lines = [.. text.Split('\n').Select(one => one.TrimEnd('\r')).Where(one => one.Trim().Length > 0)];
        var at = 0;

        if (lines.Length == 0 || !lines[0].Trim().StartsWith("version ", StringComparison.Ordinal)
            || !int.TryParse(lines[0].Trim()[8..], NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
        {
            return new RoomLayout { Why = "no version line" };
        }

        at++;
        try
        {
            return Body(lines, ref at, version);
        }
        catch (FormatException exception)
        {
            return new RoomLayout { Version = version, Why = $"line {at + 1}: {exception.Message}" };
        }
    }

    private static RoomLayout Body(string[] lines, ref int at, int version)
    {
        // The strings: a count, then that many quoted lines - what a slot's types index from one.
        int count = Whole(Line(lines, ref at), 0, "the string count");
        if (count < 0 || count > lines.Length - at)
        {
            throw new FormatException($"the string count is {count.ToString(CultureInfo.InvariantCulture)}, and only {lines.Length - at} remain");
        }

        var strings = new string[count];
        for (var one = 0; one < count; one++)
        {
            strings[one] = Line(lines, ref at).Trim().Trim('"').Replace('\\', '/');
        }

        // THE MODULE: the side, written twice below 31, and from 22 the overlap - see ModuleSide. Read where it reads and
        // left unsaid where it does not: the line was stepped over before, and a room must not be lost to it now.
        string[] dimensions = Words(Line(lines, ref at));
        int overlapAt = version < 31 ? 2 : 1;
        int moduleSide = dimensions.Length > 0 && int.TryParse(dimensions[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int side) ? side : 0;
        int moduleOverlap = version >= 22 && dimensions.Length > overlapAt
            && int.TryParse(dimensions[overlapAt], NumberStyles.Integer, CultureInfo.InvariantCulture, out int overlap) ? overlap : -1;
        long thingies = 0;
        foreach (string word in Words(Line(lines, ref at)))
        {
            thingies += Whole(word, "the numbers before the tag");
        }

        Line(lines, ref at);                                         // The tag.
        Line(lines, ref at);                                         // The bools.

        string[] root = Words(Line(lines, ref at));
        int width = 1;
        int height = 1;
        if (root.Length >= 3 && root[0] == "k")
        {
            width = Whole(root[1], "the root slot's width");
            height = Whole(root[2], "the root slot's height");
        }

        if (width is < 1 or > 256 || height is < 1 or > 256)
        {
            throw new FormatException($"the room says it is {width}x{height} tiles");
        }

        Skip(lines, ref at, thingies * 2, "the numbers before the tag, doubled");

        var points = new List<RoomPoint>();
        int groups = version switch
        {
            < 20 => 9,
            < 26 => 10,
            < 29 => 5,
            _ => 6,
        };

        for (var one = 0; one < groups; one++)
        {
            foreach (string line in Group(lines, ref at, version))
            {
                if (RoomPoint.Parse(one, line) is { } point)
                {
                    points.Add(point);
                }
            }
        }

        if (version >= 35)
        {
            Line(lines, ref at);
        }

        // THE GRID, read where it can be and stepped over where it cannot: the doodads after it are
        // the part of a room that is drawn, and a slot this does not know must not cost them.
        var slots = new List<RoomSlot>(width * height);
        string slotsWhy = string.Empty;
        for (var line = 0; line < height; line++)
        {
            string text = Line(lines, ref at);
            if (slotsWhy.Length == 0 && !Slotted(text, width, slots, out string why))
            {
                slotsWhy = $"grid line {line + 1}: {why}";
                slots.Clear();
            }
        }

        var doodads = new List<RoomDoodad>();
        foreach (string line in Group(lines, ref at, version))
        {
            doodads.Add(Doodad(line, version));
        }

        var zones = new List<RoomZone>();
        (int[] overrides, string overridesWhy) = Tail(lines, at, version, width, height, zones);
        return new RoomLayout
        {
            Version = version,
            Width = width,
            Height = height,
            ModuleSide = moduleSide,
            ModuleOverlap = moduleOverlap,
            Doodads = doodads,
            Strings = strings,
            Slots = slotsWhy.Length == 0 ? slots : [],
            SlotsWhy = slotsWhy,
            GroundOverrides = overrides,
            OverridesWhy = overridesWhy,
            Points = points,
            Zones = zones,
        };
    }

    /// <summary>
    /// Everything after the doodads, read as far as the ground overrides at the end - the overrides, or none and why.
    /// </summary>
    /// <remarks>
    /// GIVEN UP, NOT THROWN, where it does not read: the grid and the doodads before it are the room,
    /// and a tail this does not know must not cost them.
    /// </remarks>
    private static (int[] Overrides, string Why) Tail(string[] lines, int at, int version, int width, int height, List<RoomZone> zones)
    {
        try
        {
            if (version >= 23)
            {
                Group(lines, ref at, version);                       // Doodad connections.
            }

            Group(lines, ref at, version);                           // Decals.
            if (version >= 22 && BossLines(lines, ref at) is { } carried)
            {
                lines = [carried, .. lines.AsSpan(at)];
                at = 0;
            }

            if (version >= 27)
            {
                foreach (string line in Group(lines, ref at, version, terminatedFrom: 33))
                {
                    zones.Add(Zone(line, version));
                }
            }

            if (at < lines.Length && Tags(lines[at]))
            {
                at++;
            }

            int inner = (width - 1) * (height - 1);
            if (at >= lines.Length || inner == 0)
            {
                return ([], string.Empty);
            }

            string[] words = Words(lines[at]);
            if (words.Length != inner)
            {
                return ([], $"the line after the tags holds {words.Length} numbers, where the grid's {inner} inner corners would take {inner}");
            }

            var overrides = new int[inner];
            for (var one = 0; one < inner; one++)
            {
                overrides[one] = Whole(words[one], "a ground override");
            }

            return (overrides, string.Empty);
        }
        catch (FormatException exception)
        {
            return ([], exception.Message);
        }
    }

    /// <summary>
    /// The boss lines: a count, then that many lines of quoted strings - and whatever follows the last one's strings, the next line's start, or null.
    /// </summary>
    private static string? BossLines(string[] lines, ref int at)
    {
        int count = Whole(Line(lines, ref at), 0, "the boss lines' count");
        if (count < 0)
        {
            throw new FormatException($"the boss lines' count reads {count}");
        }

        string trailing = string.Empty;
        for (var one = 0; one < count; one++)
        {
            string line = Line(lines, ref at);
            int position = 0;
            int quoted = 0;
            while (true)
            {
                while (position < line.Length && char.IsWhiteSpace(line[position]))
                {
                    position++;
                }

                if (position >= line.Length || line[position] != '"')
                {
                    break;
                }

                position = Quoted(line, position).After;
                quoted++;
            }

            if (quoted == 0)
            {
                throw new FormatException("a boss line with no quoted string");
            }

            trailing = line[position..].Trim();
        }

        return trailing.Length > 0 ? trailing : null;
    }

    /// <summary>Whether a line is the tags: a count, then exactly that many words.</summary>
    private static bool Tags(string line)
    {
        string[] words = Words(line);
        return words.Length > 0
            && int.TryParse(words[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int count)
            && count >= 0
            && words.Length == count + 1;
    }

    /// <summary>
    /// One grid line's slots, added to the list - or false and why, where it does not hold exactly <paramref name="width"/>.
    /// </summary>
    /// <remarks>
    /// A k SLOT'S LAST NUMBER, ITS ORIGIN, IS NOT ALWAYS WRITTEN - poe_data_tools reads it as optional,
    /// annalithic from version 19 - and every slot starts with a letter, so a number after the
    /// twenty-third is the origin and a letter is the next slot.
    /// </remarks>
    private static bool Slotted(string text, int width, List<RoomSlot> slots, out string why)
    {
        string[] words = Words(text);
        var at = 0;
        var found = 0;
        while (at < words.Length)
        {
            string word = words[at++];
            switch (word)
            {
                case "n" or "s" or "o":
                    slots.Add(new RoomSlot(word[0], []));
                    break;

                case "f":
                    if (at >= words.Length || !int.TryParse(words[at++], NumberStyles.Integer, CultureInfo.InvariantCulture, out int fill))
                    {
                        why = "an f slot without its fill type";
                        return false;
                    }

                    slots.Add(new RoomSlot('f', [fill]));
                    break;

                case "k":
                    var numbers = new List<int>(24);
                    while (at < words.Length && numbers.Count < 24
                        && int.TryParse(words[at], NumberStyles.Integer, CultureInfo.InvariantCulture, out int number))
                    {
                        numbers.Add(number);
                        at++;
                    }

                    if (numbers.Count < 23)
                    {
                        why = $"a k slot with {numbers.Count} numbers, where it takes 23 or 24";
                        return false;
                    }

                    slots.Add(new RoomSlot('k', [.. numbers]));
                    break;

                default:
                    why = $"a slot reading \"{word}\"";
                    return false;
            }

            found++;
        }

        why = found == width ? string.Empty : $"{found} slots where the room is {width} wide";
        return found == width;
    }

    /// <summary>One group's lines: counted below version <paramref name="terminatedFrom"/>, ended by a <c>-1</c> line from it.</summary>
    private static List<string> Group(string[] lines, ref int at, int version, int terminatedFrom = 32)
    {
        var kept = new List<string>();
        if (version < terminatedFrom)
        {
            int count = Whole(Line(lines, ref at), 0, "a group's count");
            if (count < 0)
            {
                throw new FormatException($"a group's count reads {count}");
            }

            for (var one = 0; one < count; one++)
            {
                kept.Add(Line(lines, ref at));
            }

            return kept;
        }

        while (true)
        {
            string line = Line(lines, ref at);
            if (line.Trim() == "-1")
            {
                return kept;
            }

            kept.Add(line);
        }
    }

    /// <summary>
    /// One doodad line, field by field as poe_data_tools reads it.
    /// </summary>
    /// <remarks>
    /// THE TWO QUOTED FIELDS ARE TAKEN BY THEIR QUOTES, not by counting words, so a path with a space
    /// in it cannot shift the scale onto a file name - and a line that does not end in two quoted
    /// strings after the numbers is refused, which is the check that the counts before it held.
    /// </remarks>
    private static RoomDoodad Doodad(string line, int version)
    {
        int quote = line.IndexOf('"', StringComparison.Ordinal);
        if (quote < 0)
        {
            throw new FormatException("a doodad line with no quoted file");
        }

        string[] numbers = Words(line[..quote]);
        var at = 0;

        int x = Whole(Next(numbers, ref at), "a doodad's x");
        int y = Whole(Next(numbers, ref at), "a doodad's y");
        // READ, THEN STEPPED - never "at += Whole(Next(ref at))": C# reads the left side before the
        // call moves it, and the count's own word is lost. The field check below caught exactly that.
        Vector2? exact = null;
        if (version >= 34)
        {
            long pairs = Whole(Next(numbers, ref at), "a doodad's pair count");
            int first = at;
            Skip(numbers, ref at, pairs * 2, "a doodad's pairs");
            if (pairs > 0)
            {
                exact = new Vector2(Real(numbers[first], "a doodad's pair"), Real(numbers[first + 1], "a doodad's pair"));
            }
        }

        float turn = Real(Next(numbers, ref at), "a doodad's angle");
        if (version >= 18)
        {
            at += 4;
        }

        at++;                                                        // A bool.
        if (version >= 25)
        {
            at++;                                                    // Another.
        }

        int floats = Whole(Next(numbers, ref at), "a doodad's float count");
        int firstFloat = at;
        Skip(numbers, ref at, floats, "a doodad's floats");
        float? height = floats > 0 ? Real(numbers[firstFloat], "a doodad's float") : null;
        float scale = Real(Next(numbers, ref at), "a doodad's scale");

        if (at != numbers.Length)
        {
            throw new FormatException(
                $"a doodad line has {numbers.Length} numbers before its file and version {version} accounts for {at}");
        }

        (string ao, int after) = Quoted(line, quote);
        int next = line.IndexOf('"', after);
        string stub = next >= 0 ? Quoted(line, next).Text : string.Empty;

        return new RoomDoodad(x, y, turn, scale, ao.Replace('\\', '/'), stub) { Height = height, Exact = exact };
    }

    /// <summary>A zone's line - see <see cref="RoomZone"/>: the name, quoted from version 35, four whole numbers, then from 35 two quoted strings and a number.</summary>
    private static RoomZone Zone(string line, int version)
    {
        string text = line.Trim();
        string name;
        string rest;
        if (text.StartsWith('"'))
        {
            (name, int after) = Quoted(text, 0);
            rest = text[after..];
        }
        else
        {
            int space = text.IndexOfAny([' ', '\t']);
            name = space < 0 ? text : text[..space];
            rest = space < 0 ? string.Empty : text[space..];
        }

        int quote = rest.IndexOf('"', StringComparison.Ordinal);
        string[] numbers = Words(quote < 0 ? rest : rest[..quote]);
        if (numbers.Length < 4)
        {
            throw new FormatException($"a zone line with {numbers.Length} numbers where four are its rectangle");
        }

        string type = string.Empty, environment = string.Empty;
        if (version >= 35 && quote >= 0)
        {
            (type, int after) = Quoted(rest, quote);
            int next = rest.IndexOf('"', after);
            if (next >= 0)
            {
                (environment, _) = Quoted(rest, next);
            }
        }

        return new RoomZone(
            name,
            Whole(numbers[0], "a zone's least x"),
            Whole(numbers[1], "a zone's least y"),
            Whole(numbers[2], "a zone's most x"),
            Whole(numbers[3], "a zone's most y"),
            type.Replace('\\', '/'),
            environment.Replace('\\', '/'));
    }

    private static (string Text, int After) Quoted(string line, int open)
    {
        int close = line.IndexOf('"', open + 1);
        if (close < 0)
        {
            throw new FormatException("an unclosed quote");
        }

        return (line[(open + 1)..close], close + 1);
    }

    private static string Line(string[] lines, ref int at)
    {
        if (at >= lines.Length)
        {
            throw new FormatException("the file ends early");
        }

        return lines[at++];
    }

    private static string Next(string[] words, ref int at)
        => at < words.Length ? words[at++] : throw new FormatException("a line ends early");

    /// <summary>
    /// Steps over a counted run of lines or words, refusing a count the file cannot hold.
    /// </summary>
    /// <remarks>
    /// THE COUNTS ARE READ OUT OF THE FILE AND ADDED TO AN INDEX, and a file is allowed to be
    /// wrong: "-3" is a whole number to the parser above, and a count near two billion is one
    /// whose double wraps negative. Either way the index lands before the start of the array, where
    /// the end-of-file check does not look, and the read throws something this class promised not
    /// to. Checked here against what is actually left, as a <see cref="FormatException"/> like every
    /// other fault in the file, with the count that caused it in the message.
    /// </remarks>
    private static void Skip(string[] over, ref int at, long count, string what)
    {
        if (count < 0 || count > over.Length - at)
        {
            throw new FormatException(
                $"{what} is {count.ToString(CultureInfo.InvariantCulture)}, and only {over.Length - at} remain");
        }

        at += (int)count;
    }

    private static string[] Words(string line) => line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    private static int Whole(string line, int word, string what)
        => Words(line) is { Length: > 0 } words && word < words.Length
            ? Whole(words[word], what)
            : throw new FormatException($"{what} is missing");

    private static int Whole(string word, string what)
        => int.TryParse(word, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value)
            ? value
            : throw new FormatException($"{what} reads \"{word}\", which is not a whole number");

    private static float Real(string word, string what)
        => float.TryParse(word, NumberStyles.Float, CultureInfo.InvariantCulture, out float value)
            ? value
            : throw new FormatException($"{what} reads \"{word}\", which is not a number");
}
