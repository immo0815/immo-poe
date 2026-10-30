using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// A terrain tile's definition (<c>.tdt</c>): which template its geometry is in, and how big it is.
/// </summary>
/// <remarks>
/// THE FILE EVERY PLACED TILE NAMES. TileStruct.TgtFilePtr leads to a <c>.tdt</c> - measured, see
/// RoomProbe - so this is the first hop from "the tile the game put here" to the triangles.
///
/// TWO REFERENCES AND WHERE THEY MEET. zao's <c>libpoe/poe/format/tdt.cpp</c> reads versions 3 to 7
/// in full; annalithic's <c>poeformats/Tdt.cs</c> (2026, written against PoE2 files) reads only the
/// front and adds the one rule zao's does not have: a definition whose leading reference is filled
/// INHERITS another and carries nothing else. Everything read here is in both, and nothing past the
/// size is read at all - the side offsets, the sub-tile blocks and the ground grid are walkability
/// and matching data, not geometry.
///
/// THE STRINGS ARE A TABLE AT THE FRONT and every text field is a CHARACTER offset into it, which
/// is the reader's self-check: a field read from the wrong place lands outside the table or in the
/// middle of a word, so <see cref="Ref"/> takes only offsets at which a string begins, and a template
/// that does not end in <c>.tgt</c> is refused rather than followed.
/// </remarks>
public sealed class TileDefinition
{
    /// <summary>Nothing read.</summary>
    public static TileDefinition None { get; } = new() { Why = "nothing to read" };

    /// <summary>The file's version. zao's reader covers 3 to 7.</summary>
    public int Version { get; private init; }

    /// <summary>The definition this one inherits from, or empty. When filled, nothing else is.</summary>
    public string Inherits { get; private init; } = string.Empty;

    /// <summary>The templates the geometry is in - usually one; the field may list several with <c>;</c>.</summary>
    public IReadOnlyList<string> Templates { get; private init; } = [];

    /// <summary>The tile's tag - a feature name such as a doodad group. Informational.</summary>
    public string Tag { get; private init; } = string.Empty;

    /// <summary>Tiles across, as the definition says. Zero where it inherits.</summary>
    public int Width { get; private init; }

    /// <summary>Tiles down.</summary>
    public int Height { get; private init; }

    /// <summary>
    /// The ground type at each corner, as <c>.gt</c> paths: down-left, down-right, up-right, up-left. Empty where unsaid.
    /// </summary>
    /// <remarks>
    /// annalithic's Tdt.cs ORDER AND PLACE - four string references straight after the size - and
    /// the same self-check as every other field here: an offset that does not start a string, or a
    /// string that is not a <c>.gt</c>, reads as empty rather than as a word from the middle of the
    /// table. The terrain's per-corner byte in memory indexes the same kind of list - see
    /// TileCornerData in the offsets schema.
    /// </remarks>
    public IReadOnlyList<string> Grounds { get; private init; } = [];

    /// <summary>
    /// The ground type at every sub-tile corner, as <c>.gt</c> paths, empty where the file says none: (Width + 1) a row, Height + 1
    /// rows - or empty, and <see cref="GridWhy"/> says why. See <see cref="CornersOf"/> for which corners a sub-tile takes.
    /// </summary>
    /// <remarks>
    /// MEASURED ON PORT (v0.1.247's capture, every laid definition written past its corners): after the side offsets and the
    /// sub-tile blocks - variable in length, read by nothing here - a definition carries (Width + 1) x (Height + 1) u32, each a
    /// string reference to a <c>.gt</c> or 0xFFFFFFFF, just before its <c>.tmd</c> reference where it has one. All 69 laid 3x3
    /// definitions carry one such run of 16 and only one, and it is what the tile's own corners do not say: the boss's harbour
    /// names four grounds in its strings (the ocean, the town fill, the tropical fill and PortTownTrim) while its corners name
    /// two. Row nought lies at the template's least y: the coast's ocean row stands at the sub-tiles of its last mesh row, the
    /// deep ones, and its ocean corner at column three by the third column's deepest sub-tile.
    ///
    /// FOUND RATHER THAN STEPPED TO, because the sub-tile blocks before it are not read: the one place in the bytes past the
    /// corners where that many words in a row are every one a <c>.gt</c> reference or none, at least one a reference. A string
    /// reference must begin a string (the table's own rule) and the string must end in <c>.gt</c>, so a run of other words does
    /// not pass; where no place or more than one fits, there is no grid and the tile's four corners stand for every sub-tile.
    /// </remarks>
    public IReadOnlyList<string> GroundGrid { get; private init; } = [];

    /// <summary>Why <see cref="GroundGrid"/> is empty, or empty where it was found.</summary>
    public string GridWhy { get; private init; } = string.Empty;

    /// <summary>
    /// The four corners' ground types of one sub-tile, numbered as its mesh is (<c>c{x}r{y}</c>, from one, row one at the top) -
    /// least x and least y first, then greatest x, least y, then least x, greatest y, then both greatest; empty where the
    /// definition has no grid.
    /// </summary>
    /// <remarks>
    /// Mesh row y lies at template y from -(y - 1) x 250 to -(y - 2) x 250 (TileModels), so its corners are the grid's rows
    /// Height - y and Height - y + 1, and mesh column x the grid's columns x - 1 and x.
    /// </remarks>
    public IReadOnlyList<string> CornersOf(int x, int y)
    {
        if (GroundGrid.Count == 0 || x < 1 || x > Width || y < 1 || y > Height)
        {
            return [];
        }

        int across = Width + 1;
        int low = Height - y, high = Height - y + 1;
        return [GroundGrid[(low * across) + x - 1], GroundGrid[(low * across) + x], GroundGrid[(high * across) + x - 1], GroundGrid[(high * across) + x]];
    }

    /// <summary>
    /// The edge type on each side, as <c>.et</c> paths: down, right, up, left. Empty where unsaid.
    /// </summary>
    /// <remarks>
    /// zao's <c>sideEts</c> and annalithic's <c>edgeTypeDown</c> to <c>edgeTypeLeft</c>: four string
    /// references straight after the tag, in every version. The same self-check as the grounds - a
    /// string that is not an <c>.et</c> reads as empty. A room's k slot names the same four, in the
    /// same order; see RoomSlot.
    /// </remarks>
    public IReadOnlyList<string> Edges { get; private init; } = [];

    /// <summary>Why nothing was read, or empty.</summary>
    public string Why { get; private init; } = string.Empty;

    /// <summary>
    /// Where the reading stopped, in bytes from the start - everything after it (the side offsets, the sub-tile blocks and the ground
    /// grid zao's reader names) is read by nothing here, and the capture's ground part writes it as it stands. Nought where nothing read.
    /// </summary>
    public int End { get; private init; }

    /// <summary>Whether it read - an inheriting definition reads, with only <see cref="Inherits"/>.</summary>
    public bool Ready => Why.Length == 0;

    /// <summary>Reads a file's bytes. Never throws: a tile that will not read is a tile that does not draw.</summary>
    public static TileDefinition Read(byte[]? content)
    {
        if (content is not { Length: >= 8 })
        {
            return None;
        }

        try
        {
            return Parse(content);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException
            or IndexOutOfRangeException or OverflowException)
        {
            return new TileDefinition { Why = $"the file does not read as a tile definition: {exception.Message}" };
        }
    }

    private static TileDefinition Parse(byte[] raw)
    {
        ReadOnlySpan<byte> file = raw;
        int version = BinaryPrimitives.ReadInt32LittleEndian(file);
        if (version < 3)
        {
            return new TileDefinition { Version = version, Why = $"version {version} is older than either reference reads" };
        }

        long chars = BinaryPrimitives.ReadUInt32LittleEndian(file[4..]);
        if (8 + (chars * 2) > file.Length)
        {
            return new TileDefinition { Version = version, Why = "the string table says it is bigger than the file" };
        }

        var table = new Table(file.Slice(8, (int)chars * 2));
        int at = 8 + ((int)chars * 2);

        string inherits = string.Empty;
        if (version >= 7)
        {
            inherits = table.Ref(Next(file, ref at));
            if (inherits.Length > 0)
            {
                // annalithic's rule: an inheriting definition carries nothing after the reference.
                return new TileDefinition { Version = version, Inherits = inherits };
            }
        }

        string templates = version > 4 ? table.Ref(Next(file, ref at)) : string.Empty;
        string tag = table.Ref(Next(file, ref at));

        // THE FOUR SIDE EDGE TYPES, while there are bytes for them - see Edges.
        var edges = new string[4];
        for (var side = 0; side < 4; side++)
        {
            string type = table.Ref(Next(file, ref at));
            edges[side] = type.EndsWith(".et", StringComparison.OrdinalIgnoreCase) ? type : string.Empty;
        }

        int width;
        int height;
        if (version == 3)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(file[at..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(file[(at + 4)..]);
            at += 8;
        }
        else
        {
            width = (sbyte)file[at];
            height = (sbyte)file[at + 1];
            at += 2;
        }

        // THE CORNER GROUND TYPES, only where the file is the layout annalithic reads (a version past
        // 3, whose size is two bytes) and only while there are bytes for them.
        string[] grounds = [];
        if (version > 3 && at + 16 <= file.Length)
        {
            grounds = new string[4];
            for (var corner = 0; corner < 4; corner++)
            {
                string type = table.Ref(Next(file, ref at));
                grounds[corner] = type.EndsWith(".gt", StringComparison.OrdinalIgnoreCase) ? type : string.Empty;
            }
        }

        if (width is < 1 or > 64 || height is < 1 or > 64)
        {
            return new TileDefinition
            {
                Version = version,
                Why = $"the tile says it is {width}x{height}, which no tile is - the fields before it were read from the wrong place",
            };
        }

        string[] named = [.. templates.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)];
        foreach (string one in named)
        {
            if (!one.EndsWith(TileTemplate.Extension, StringComparison.OrdinalIgnoreCase))
            {
                return new TileDefinition
                {
                    Version = version,
                    Why = $"the template field reads \"{one}\", which is not a .tgt - the layout did not hold",
                };
            }
        }

        (string[] grid, string gridWhy) = grounds.Length > 0 ? Grid(file, at, table, width, height) : ([], "the file names no corner grounds");
        return new TileDefinition
        {
            Version = version,
            Templates = named,
            Tag = tag,
            Width = width,
            Height = height,
            Grounds = grounds,
            Edges = edges,
            End = at,
            GroundGrid = grid,
            GridWhy = gridWhy,
        };
    }

    /// <summary>The sub-tile corners' ground grid past <paramref name="from"/>, found as <see cref="GroundGrid"/> says - or why not.</summary>
    private static (string[] Grid, string Why) Grid(ReadOnlySpan<byte> file, int from, Table table, int width, int height)
    {
        Dictionary<uint, string> grounds = table.Ending(".gt");
        if (grounds.Count == 0)
        {
            return ([], "the string table holds no .gt");
        }

        int count = (width + 1) * (height + 1);
        int found = -1;
        int places = 0;
        for (int at = from; at + (count * 4) <= file.Length; at++)
        {
            var named = false;
            var fits = true;
            for (var one = 0; one < count; one++)
            {
                uint word = BinaryPrimitives.ReadUInt32LittleEndian(file[(at + (one * 4))..]);
                if (grounds.ContainsKey(word))
                {
                    named = true;
                }
                else if (word != uint.MaxValue)
                {
                    fits = false;
                    break;
                }
            }

            if (fits && named)
            {
                places++;
                found = at;
            }
        }

        if (places != 1)
        {
            return ([], places == 0
                ? string.Create(CultureInfo.InvariantCulture, $"no run of {count} ground references past the corners")
                : string.Create(CultureInfo.InvariantCulture, $"{places} places past the corners fit {count} ground references"));
        }

        var grid = new string[count];
        for (var one = 0; one < count; one++)
        {
            uint word = BinaryPrimitives.ReadUInt32LittleEndian(file[(found + (one * 4))..]);
            grid[one] = grounds.GetValueOrDefault(word, string.Empty);
        }

        return (grid, string.Empty);
    }

    private static uint Next(ReadOnlySpan<byte> file, ref int at)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(file[at..]);
        at += 4;
        return value;
    }

    /// <summary>The front table: UTF-16 strings, each ending in a NUL, addressed by character offset.</summary>
    private readonly ref struct Table(ReadOnlySpan<byte> bytes)
    {
        private readonly ReadOnlySpan<byte> _bytes = bytes;

        /// <summary>
        /// The string beginning at a character offset, or empty.
        /// </summary>
        /// <remarks>
        /// ONLY WHERE A STRING BEGINS - at zero or just past a NUL - which is zao's rule: his reader
        /// keys the table by each string's start and anything else resolves to nothing. annalithic's
        /// would read from the middle of a word, and that is exactly the wrong answer a misplaced
        /// field would produce, so it is not taken.
        /// </remarks>
        public string Ref(uint offset)
        {
            long at = (long)offset * 2;
            if (at + 2 > _bytes.Length)
            {
                return string.Empty;
            }

            if (at > 0 && BinaryPrimitives.ReadUInt16LittleEndian(_bytes[(int)(at - 2)..]) != 0)
            {
                return string.Empty;
            }

            int end = (int)at;
            while (end + 2 <= _bytes.Length && BinaryPrimitives.ReadUInt16LittleEndian(_bytes[end..]) != 0)
            {
                end += 2;
            }

            return System.Text.Encoding.Unicode.GetString(_bytes[(int)at..end]);
        }

        /// <summary>Every string of the table ending in <paramref name="suffix"/>, by the character offset it begins at.</summary>
        public Dictionary<uint, string> Ending(string suffix)
        {
            var found = new Dictionary<uint, string>();
            var start = 0;
            for (var at = 0; at + 2 <= _bytes.Length; at += 2)
            {
                if (BinaryPrimitives.ReadUInt16LittleEndian(_bytes[at..]) != 0)
                {
                    continue;
                }

                if (at > start)
                {
                    string text = System.Text.Encoding.Unicode.GetString(_bytes[start..at]);
                    if (text.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                    {
                        found[(uint)(start / 2)] = text;
                    }
                }

                start = at + 2;
            }

            return found;
        }
    }
}

/// <summary>One run of shapes in a sub-tile that wear one material.</summary>
/// <param name="Material">Index into <see cref="TileTemplate.Materials"/>.</param>
/// <param name="Shapes">How many consecutive shapes of the sub-tile's mesh wear it.</param>
public readonly record struct TileRun(int Material, int Shapes);

/// <summary>
/// A tile template (<c>.tgt</c>): where each sub-tile's mesh is, and which materials go on it.
/// </summary>
/// <remarks>
/// TEXT, UTF-16, one keyword per line - read against poe_data_tools' <c>tgt</c> parser and
/// annalithic's <c>Tgt.cs</c>, which agree on everything taken here:
///
///     version 3
///     SourceScene "..."            optional
///     Size 2 1
///     TileMeshRoot "Art/Models/Terrain/.../Name"
///     GroundMask "..."             optional
///     NormalMaterials 3
///         "a.mat" ...
///     MaterialSlots N ...          optional
///     SubTileMaterialIndices       only where there are materials
///         2 0 5 1 3                one line per sub-tile: a count, then pairs (or triples)
///
/// THE MESH OF A SUB-TILE IS NAMED, NOT LISTED: <c>{root}_c{x}r{y}.tgm</c>, or <c>{root}.tgm</c> for
/// a one-by-one tile - annalithic's rule, and zao's exporter builds the same names. Rows count from
/// the bottom: the first line of the material indices is the TOP row, so line
/// <c>x + (height - y) * width</c> belongs to <c>c{x+1}r{y}</c>.
///
/// VERSIONS BELOW 3 name a single mesh in another shape of file, read whole but not drawn - see Older.
/// </remarks>
public sealed class TileTemplate
{
    /// <summary>What a template's file name ends with.</summary>
    public const string Extension = ".tgt";

    /// <summary>Nothing read.</summary>
    public static TileTemplate None { get; } = new() { Why = "nothing to read" };

    /// <summary>The file's version.</summary>
    public int Version { get; private init; }

    /// <summary>Sub-tiles across.</summary>
    public int Width { get; private init; }

    /// <summary>Sub-tiles down.</summary>
    public int Height { get; private init; }

    /// <summary>The path every sub-tile's mesh name is built from.</summary>
    public string MeshRoot { get; private init; } = string.Empty;

    /// <summary>The <c>GroundMask</c> texture the template names, or empty. Not yet drawn with - see ModelDump.</summary>
    public string GroundMask { get; private init; } = string.Empty;

    /// <summary>The materials, in the order the runs index them.</summary>
    public IReadOnlyList<string> Materials { get; private init; } = [];

    /// <summary>Each sub-tile's material runs, in the file's line order. See <see cref="RunsOf"/>.</summary>
    public IReadOnlyList<IReadOnlyList<TileRun>> Runs { get; private init; } = [];

    /// <summary>Why nothing was read, or empty.</summary>
    public string Why { get; private init; } = string.Empty;

    /// <summary>Whether it read.</summary>
    public bool Ready => Why.Length == 0;

    /// <summary>The mesh of the sub-tile in column <paramref name="x"/> and row <paramref name="y"/>, both from one.</summary>
    public string MeshOf(int x, int y)
        => Width == 1 && Height == 1
            ? MeshRoot + TileMesh.Extension
            : string.Create(CultureInfo.InvariantCulture, $"{MeshRoot}_c{x}r{y}{TileMesh.Extension}");

    /// <summary>The material runs of the sub-tile in column x and row y, both from one, or none.</summary>
    public IReadOnlyList<TileRun> RunsOf(int x, int y)
    {
        int line = (x - 1) + ((Height - y) * Width);
        return line >= 0 && line < Runs.Count ? Runs[line] : [];
    }

    /// <summary>Reads a file's bytes, through the game's shared text decode.</summary>
    public static TileTemplate Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install.</summary>
    public static TileTemplate Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        string[] lines = text.Split('\n');
        var at = 0;

        if (!Keyword(lines, ref at, "version", out string rest)
            || !int.TryParse(rest, NumberStyles.Integer, CultureInfo.InvariantCulture, out int version))
        {
            return new TileTemplate { Why = "no version line" };
        }

        if (version < 3)
        {
            return Older(lines, at, version);
        }

        string scene = Keyword(lines, ref at, "SourceScene", out rest) ? Quoted(rest) ?? string.Empty : string.Empty;

        if (!Keyword(lines, ref at, "Size", out rest) || Numbers(rest) is not [int width, int height]
            || width is < 1 or > 64 || height is < 1 or > 64)
        {
            return new TileTemplate { Version = version, Why = "no Size line of two numbers" };
        }

        if (!Keyword(lines, ref at, "TileMeshRoot", out rest) || Quoted(rest) is not { Length: > 0 } root)
        {
            return new TileTemplate { Version = version, Why = "no TileMeshRoot line" };
        }

        string mask = Keyword(lines, ref at, "GroundMask", out rest) ? Quoted(rest) ?? string.Empty : string.Empty;

        var materials = new List<string>();
        if (Keyword(lines, ref at, "NormalMaterials", out rest)
            && int.TryParse(rest.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
        {
            for (var one = 0; one < count && at < lines.Length; one++)
            {
                materials.Add(Quoted(lines[at++]) ?? string.Empty);
            }
        }

        var slotted = new List<string>();
        if (Keyword(lines, ref at, "MaterialSlots", out rest)
            && int.TryParse(rest.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int slots))
        {
            for (var one = 0; one < slots && at < lines.Length; one++)
            {
                slotted.Add(Quoted(lines[at++]) ?? string.Empty);
            }
        }

        var runs = new List<IReadOnlyList<TileRun>>(width * height);
        var leads = new List<IReadOnlyList<int>>(width * height);
        if (materials.Count > 0 && Keyword(lines, ref at, "SubTileMaterialIndices", out rest))
        {
            // THE FIRST SUB-TILE MAY SHARE THE KEYWORD'S LINE, since the grammar only asks for
            // whitespace between them - so what follows the keyword is tried before the next line.
            string first = rest.Trim();
            if (first.Length > 0)
            {
                runs.Add(Run(first, materials.Count, leads));
            }

            while (runs.Count < width * height && at < lines.Length)
            {
                string line = lines[at++].Trim();
                if (line.Length > 0)
                {
                    runs.Add(Run(line, materials.Count, leads));
                }
            }
        }

        return new TileTemplate
        {
            Version = version,
            Width = width,
            Height = height,
            MeshRoot = root.Replace('\\', '/'),
            GroundMask = mask.Replace('\\', '/'),
            Materials = materials,
            Runs = runs,
            SourceScene = scene.Replace('\\', '/'),
            Slots = slotted,
            Leads = leads,
            Unread = Leftover(lines, at),
        };
    }

    /// <summary>The <c>SourceScene</c> the template names, or empty - read, not used.</summary>
    public string SourceScene { get; private init; } = string.Empty;

    /// <summary>The <c>MaterialSlots</c> the template names, in order - read, not used: every one of Port's 66 templates says <c>MaterialSlots 0</c>.</summary>
    public IReadOnlyList<string> Slots { get; private init; } = [];

    /// <summary>
    /// Per sub-tile line, in <see cref="Runs"/>' order, the FIRST number of each run written as a triple - poe_data_tools'
    /// <c>uint1</c>, which annalithic's reading (the last two the material and the shapes) leaves unnamed; empty for a line
    /// of pairs.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<int>> Leads { get; private init; } = [];

    /// <summary>A version 1 template's one mesh (<c>TileMesh "..."</c>), or empty.</summary>
    public string SingleMesh { get; private init; } = string.Empty;

    /// <summary>A version 1 template's number after each material (its <c>NormalMaterials</c> lines are <c>"a.mat" n</c>), in <see cref="Materials"/>' order; else empty.</summary>
    public IReadOnlyList<int> MaterialNumbers { get; private init; } = [];

    /// <summary>The first line the reading left behind, or empty where every non-blank line was taken - the reference asks for the file's end.</summary>
    public string Unread { get; private init; } = string.Empty;

    /// <summary>
    /// A version 1 or 2 template - poe_data_tools' <c>v1_section</c>: <c>TileMesh "..."</c>, an optional
    /// <c>GroundMask "..."</c>, then <c>NormalMaterials N</c> and N lines of a quoted material and a number.
    /// </summary>
    /// <remarks>
    /// READ WHOLE AND NOT DRAWN (from v0.1.360): it names ONE mesh where a version 3 template names a sub-tile mesh per
    /// sub-tile by its size, and which sub-tiles that one mesh covers - and what the number after each material is - no
    /// reference says. So it stays not <see cref="Ready"/>, its <see cref="Why"/> saying so, every field kept.
    /// </remarks>
    private static TileTemplate Older(string[] lines, int at, int version)
    {
        if (!Keyword(lines, ref at, "TileMesh", out string rest) || Quoted(rest) is not { Length: > 0 } mesh)
        {
            return new TileTemplate { Version = version, Why = $"version {version} has no TileMesh line" };
        }

        string mask = Keyword(lines, ref at, "GroundMask", out rest) ? Quoted(rest) ?? string.Empty : string.Empty;
        var materials = new List<string>();
        var numbers = new List<int>();
        if (Keyword(lines, ref at, "NormalMaterials", out rest)
            && int.TryParse(rest.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
        {
            for (var one = 0; one < count && at < lines.Length; one++)
            {
                string line = lines[at++];
                int close = line.LastIndexOf('"');
                materials.Add(Quoted(line) ?? string.Empty);
                numbers.Add(close >= 0 && int.TryParse(line[(close + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int number) ? number : -1);
            }
        }

        return new TileTemplate
        {
            Version = version,
            SingleMesh = mesh.Replace('\\', '/'),
            GroundMask = mask.Replace('\\', '/'),
            Materials = materials,
            MaterialNumbers = numbers,
            Unread = Leftover(lines, at),
            Why = $"version {version} names one mesh, {mesh}, whose sub-tiles no reference gives - read, not drawn",
        };
    }

    private static string Leftover(string[] lines, int at)
    {
        for (; at < lines.Length; at++)
        {
            string line = lines[at].Trim();
            if (line.Length > 0)
            {
                return line.Length > 80 ? line[..80] + "..." : line;
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// One sub-tile's runs: a count, then that many pairs or triples.
    /// </summary>
    /// <remarks>
    /// PAIRS ARE (MATERIAL, SHAPES) and of a triple the LAST TWO are - annalithic's reading, which is
    /// the only one that says what the numbers mean; poe_data_tools reads both shapes without naming
    /// them. A material index past the list is dropped, and a line that is neither shape is no runs.
    /// </remarks>
    private static IReadOnlyList<TileRun> Run(string line, int materials, List<IReadOnlyList<int>> leads)
    {
        int[] numbers = Numbers(line);
        int count = numbers.Length > 0 ? numbers[0] : 0;
        int width = numbers.Length == 0 ? 0 : numbers.Length - 1 == count * 3 ? 3 : numbers.Length - 1 == count * 2 ? 2 : 0;
        if (width == 0 || count <= 0)
        {
            leads.Add([]);
            return [];
        }

        var runs = new List<TileRun>(count);
        int[] lead = width == 3 ? new int[count] : [];
        for (var one = 0; one < count; one++)
        {
            int material = numbers[1 + (one * width) + (width - 2)];
            int shapes = numbers[1 + (one * width) + (width - 1)];
            runs.Add(new TileRun(material < materials ? material : -1, Math.Max(0, shapes)));
            if (width == 3)
            {
                lead[one] = numbers[1 + (one * width)];
            }
        }

        leads.Add(lead);
        return runs;
    }

    /// <summary>
    /// The rest of the next non-blank line when it starts with the keyword, stepping past it - or
    /// false and no step. An optional keyword that is not there costs nothing.
    /// </summary>
    private static bool Keyword(string[] lines, ref int at, string keyword, out string rest)
    {
        int look = at;
        while (look < lines.Length && lines[look].Trim().Length == 0)
        {
            look++;
        }

        if (look < lines.Length)
        {
            string line = lines[look].Trim();
            if (line.StartsWith(keyword, StringComparison.Ordinal)
                && (line.Length == keyword.Length || char.IsWhiteSpace(line[keyword.Length])))
            {
                rest = line[keyword.Length..];
                at = look + 1;
                return true;
            }
        }

        rest = string.Empty;
        return false;
    }

    private static string? Quoted(string line)
    {
        int open = line.IndexOf('"', StringComparison.Ordinal);
        int close = open >= 0 ? line.IndexOf('"', open + 1) : -1;
        return close > open ? line[(open + 1)..close] : null;
    }

    private static int[] Numbers(string text)
    {
        string[] words = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        var numbers = new int[words.Length];
        for (var one = 0; one < words.Length; one++)
        {
            if (!int.TryParse(words[one], NumberStyles.Integer, CultureInfo.InvariantCulture, out numbers[one]))
            {
                return [];
            }
        }

        return numbers;
    }
}

/// <summary>
/// One sub-tile's geometry (<c>.tgm</c>): its props and walls, and its ground.
/// </summary>
/// <remarks>
/// VERSION 9 AND UP WRAP TWO DOLm BLOCKS, which is why this file is short - both are
/// <see cref="SkinnedMesh.Block"/>'s, the same reader the monsters and the props use. The layout
/// between them is poe_data_tools' <c>tgm</c> parser, and annalithic's <c>Tgm.cs</c> agrees with it
/// to the byte:
///
///     u8 version, six floats of box, u16 shapes, u16, u8, u8 tail count
///     DOLm                        the tile's props and walls - may hold nothing
///     per shape: ordinal          u16 at version 9, u32 after it
///                u16              from version 12
///                six floats of box
///     DOLm                        the ground - may hold nothing
///     per shape: u16 ordinal and six floats of box
///     tail count records of 87 bytes
///
/// THE END OF THE FILE IS THE CHECK. poe_data_tools demands it, and a step that went one field
/// wrong anywhere after the first block would not arrive there - so <see cref="Exact"/> says
/// whether it did, and a mismatch is reported rather than hidden. The props are kept either way,
/// because their block was read and verified by the DOLm reader before anything after it was.
///
/// VERSIONS BELOW 9 put the vertices outside a DOLm block, in a layout of their own - see Older.
/// </remarks>
public sealed class TileMesh
{
    /// <summary>What a tile mesh's file name ends with.</summary>
    public const string Extension = ".tgm";

    private const int TailBytes = 87;
    private const int BoxBytes = 24;

    /// <summary>Nothing read.</summary>
    public static TileMesh None { get; } = new() { Why = "nothing to read" };

    /// <summary>The file's version.</summary>
    public int Version { get; private init; }

    /// <summary>The props and walls - the part that carries the template's materials. May be empty.</summary>
    public SkinnedMesh Props { get; private init; } = SkinnedMesh.None;

    /// <summary>The ground under them. May be empty.</summary>
    public SkinnedMesh Ground { get; private init; } = SkinnedMesh.None;

    /// <summary>Whether the walk ended exactly at the end of the file - the layout's self-check.</summary>
    public bool Exact { get; private init; }

    /// <summary>Why nothing was read, or empty.</summary>
    public string Why { get; private init; } = string.Empty;

    /// <summary>Whether it read - a sub-tile with no geometry at all still reads.</summary>
    public bool Ready => Why.Length == 0;

    /// <summary>Reads a file's bytes. Never throws.</summary>
    public static TileMesh Read(byte[]? content)
    {
        if (content is not { Length: > 1 + BoxBytes + 6 })
        {
            return None;
        }

        try
        {
            return Parse(content);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException
            or IndexOutOfRangeException or OverflowException)
        {
            return new TileMesh { Why = $"the file does not read as a tile mesh: {exception.Message}" };
        }
    }

    private static TileMesh Parse(byte[] raw)
    {
        ReadOnlySpan<byte> file = raw;
        int version = file[0];
        if (version < 9)
        {
            return Older(file, version);
        }

        int at = 1 + BoxBytes;
        int headerShapes = BinaryPrimitives.ReadUInt16LittleEndian(file[at..]);
        int word = BinaryPrimitives.ReadUInt16LittleEndian(file[(at + 2)..]);
        at += 4;                                        // Shapes and an unnamed u16.
        int unnamed = file[at++];                       // An unnamed u8.
        int tails = file[at++];

        MeshBlock props = SkinnedMesh.Block(file, at, empty: true);
        if (!props.Ready)
        {
            return new TileMesh { Version = version, Why = props.Why };
        }

        SkinnedMesh main = Mesh(props);
        int bound = (version == 9 ? 2 : 4) + (version >= 12 ? 2 : 0) + BoxBytes;
        at = props.At + (Shapes(props) * bound);

        MeshBlock floor = SkinnedMesh.Block(file, at, empty: true);
        if (!floor.Ready)
        {
            // THE PROPS STAND ON THEIR OWN. Their block read and checked itself; only what follows
            // it did not, and that is said rather than dropping a tile that can be drawn.
            return new TileMesh { Version = version, Props = main, Why = main.Ready ? string.Empty : floor.Why };
        }

        // EACH GROUND SHAPE'S RECORD: a u16, then its box. poe_data_tools calls the u16 the ordinal and neither reference
        // says what it numbers - kept, with the tail entries after them, for the capture's ground part to set against the
        // ground (see CaptureGround).
        at = floor.At;
        var ordinals = new int[Shapes(floor)];
        for (var shape = 0; shape < ordinals.Length; shape++)
        {
            ordinals[shape] = BinaryPrimitives.ReadUInt16LittleEndian(file[at..]);
            at += 2 + BoxBytes;
        }

        int tailEnd = at + (tails * TailBytes);
        byte[] tailBytes = tailEnd <= file.Length ? file[at..tailEnd].ToArray() : [];
        at = tailEnd;

        return new TileMesh
        {
            Version = version,
            Props = main,
            Ground = Mesh(floor),
            Exact = at == file.Length,
            HeaderShapes = headerShapes,
            HeaderWord = word,
            HeaderByte = unnamed,
            GroundOrdinals = ordinals,
            TailData = tailBytes,
        };
    }

    /// <summary>
    /// A file below version 9, whose two meshes sit in a layout of their own rather than in DOLm blocks - poe_data_tools'
    /// <c>v8_section</c> (<c>file_parsers/tgm/parser.rs</c>), read in its order:
    /// <code>
    ///     two geometry records       u16 shapes, u32 vertices, u32 triangles - the props', then the ground's
    ///     u8 vertex format, u8 tail count, and at version 8 alone a u32
    ///     per mesh, props then ground
    ///         per shape              u16 ordinal, six floats of box, u32 first index, u32 index count
    ///         the vertices           props: position, normal, tangent, coordinates (24 bytes)
    ///                                ground: position, normal, tangent, and from version 4 two pairs of coordinates
    ///                                where the format is 3 or one more pair (unnamed, tail_float) where it is 2
    ///         the indices            AFTER the vertices, U16 under 65,536 vertices and U32 from there
    ///     tail count records         70 bytes below version 3, 74 at 3, 78 at 4, 83 at 5 and 6, 87 from 7
    /// </code>
    /// </summary>
    /// <remarks>
    /// REFUSED UNTIL v0.1.360, and drawn only where the walk proves itself twice: it must end EXACTLY at the file's end - the
    /// reference demands that - and each mesh's shape extents must cover its index buffer with no gap, counted in indices as a
    /// DOLm block counts them, or else in triangles (their sum three times short), which the reference's names do not settle.
    /// A walk that fails either is reported in <see cref="Why"/> and nothing of it is drawn. The ground's unnamed pair of
    /// halves (format 2) is kept apart (<see cref="GroundExtra"/>), not taken for coordinates.
    /// </remarks>
    private static TileMesh Older(ReadOnlySpan<byte> file, int version)
    {
        int at = 1 + BoxBytes;
        if (at + 20 + 2 + (version == 8 ? 4 : 0) > file.Length)
        {
            return new TileMesh { Version = version, Why = "the file ends inside its own header" };
        }

        var geometry = new (int Shapes, uint Vertices, uint Triangles)[2];
        for (var one = 0; one < 2; one++)
        {
            geometry[one] = (BinaryPrimitives.ReadUInt16LittleEndian(file[at..]), BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 2)..]), BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 6)..]));
            at += 10;
        }

        int format = file[at++];
        int tails = file[at++];
        uint? extra = null;
        if (version == 8)
        {
            extra = BinaryPrimitives.ReadUInt32LittleEndian(file[at..]);
            at += 4;
        }

        int groundStride = 20 + (version >= 4 && format == 3 ? 8 : 0) + (version >= 4 && format == 2 ? 4 : 0);
        int tailWidth = version switch { < 3 => 70, 3 => 74, 4 => 78, < 7 => 83, _ => 87 };
        long needed = at + ((long)tails * tailWidth);
        for (var one = 0; one < 2; one++)
        {
            (int shapes, uint vertices, uint triangles) = geometry[one];
            needed += (shapes * (2L + BoxBytes + 8)) + ((long)vertices * (one == 0 ? 24 : groundStride)) + ((long)triangles * 3 * (vertices < 0x10000 ? 2 : 4));
        }

        if (needed != file.Length)
        {
            return new TileMesh
            {
                Version = version,
                Why = needed > file.Length
                    ? "the version 8 layout says the file is bigger than it is"
                    : $"the version 8 layout ends {file.Length - needed} bytes before the file does",
            };
        }

        // SAFE TO NARROW: the sum above is the file's own length.
        SkinnedMesh props = SkinnedMesh.None;
        SkinnedMesh ground = SkinnedMesh.None;
        int[] ordinals = [];
        byte[] groundExtra = [];
        for (var one = 0; one < 2; one++)
        {
            (int shapes, uint counted, uint faces) = geometry[one];
            int vertices = (int)counted;
            var extents = new (int From, int Count)[shapes];
            var order = new int[shapes];
            for (var shape = 0; shape < shapes; shape++)
            {
                order[shape] = BinaryPrimitives.ReadUInt16LittleEndian(file[at..]);
                extents[shape] = ((int)BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 2 + BoxBytes)..]), (int)BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 6 + BoxBytes)..]));
                at += 2 + BoxBytes + 8;
            }

            int stride = one == 0 ? 24 : groundStride;
            var positions = new Vector3[vertices];
            var normals = new Vector3[vertices];
            var coordinates = new Vector2[vertices];
            bool coordinated = one == 0 || (version >= 4 && format == 3);
            byte[] kept = one == 1 && version >= 4 && format == 2 ? new byte[vertices * 4] : [];
            for (var point = 0; point < vertices; point++, at += stride)
            {
                positions[point] = new Vector3(BinaryPrimitives.ReadSingleLittleEndian(file[at..]), BinaryPrimitives.ReadSingleLittleEndian(file[(at + 4)..]), BinaryPrimitives.ReadSingleLittleEndian(file[(at + 8)..]));
                var normal = new Vector3((sbyte)file[at + 12] / 127f, (sbyte)file[at + 13] / 127f, (sbyte)file[at + 14] / 127f);
                normals[point] = normal.LengthSquared() > 0.0001f ? Vector3.Normalize(normal) : Vector3.UnitZ;
                if (coordinated)
                {
                    coordinates[point] = new Vector2(
                        (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(file[(at + 20)..])),
                        (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(file[(at + 22)..])));
                }

                if (kept.Length > 0)
                {
                    file.Slice(at + 20, 4).CopyTo(kept.AsSpan(point * 4));
                }
            }

            int width = vertices < 0x10000 ? 2 : 4;
            var indices = new int[(int)faces * 3];
            for (var index = 0; index < indices.Length; index++, at += width)
            {
                indices[index] = width == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(file[at..]) : (int)BinaryPrimitives.ReadUInt32LittleEndian(file[at..]);
                if (indices[index] >= vertices)
                {
                    return new TileMesh { Version = version, Why = $"the version 8 {(one == 0 ? "props'" : "ground's")} index {indices[index]} reaches past its {vertices} vertices" };
                }
            }

            if (Covered(extents, indices.Length) is not { } scale)
            {
                return new TileMesh { Version = version, Why = $"the version 8 {(one == 0 ? "props'" : "ground's")} shapes do not cover its {indices.Length} indices, in indices or in triangles" };
            }

            var facts = new MeshFacts(version, 0, 1, (uint)format, stride, shapes, shapes, (int)faces, vertices, 0, 0);
            SkinnedMesh mesh = Mesh(positions, normals, coordinates, indices, extents, scale, facts);
            if (one == 0)
            {
                props = mesh;
            }
            else
            {
                ground = mesh;
                ordinals = order;
                groundExtra = kept;
            }
        }

        var tailRecords = new byte[tails][];
        for (var one = 0; one < tails; one++, at += tailWidth)
        {
            tailRecords[one] = file.Slice(at, tailWidth).ToArray();
        }

        return new TileMesh
        {
            Version = version,
            Props = props,
            Ground = ground,
            Exact = true,
            HeaderShapes = geometry[0].Shapes,
            HeaderFormat = format,
            HeaderExtra = extra,
            GroundOrdinals = ordinals,
            GroundExtra = groundExtra,
            OlderTail = tailRecords,
        };
    }

    /// <summary>
    /// What one count of a version-8 shape's extent is in indices: 1 where the counts sum to the index buffer, 3 where they
    /// sum to a third of it (triangles), null where neither - and every extent must start where the one before it ended.
    /// </summary>
    private static int? Covered((int From, int Count)[] extents, int indices)
    {
        foreach (int scale in (ReadOnlySpan<int>)[1, 3])
        {
            long next = 0;
            var tiled = true;
            foreach ((int from, int count) in extents)
            {
                if ((long)from * scale != next || count < 0)
                {
                    tiled = false;
                    break;
                }

                next += (long)count * scale;
            }

            if (tiled && next == indices)
            {
                return scale;
            }
        }

        return null;
    }

    /// <summary>A version-8 mesh as the drawing takes one: its shapes numbered, its box from its vertices; nothing where it has no triangles.</summary>
    private static SkinnedMesh Mesh(Vector3[] positions, Vector3[] normals, Vector2[] coordinates, int[] indices, (int From, int Count)[] extents, int scale, MeshFacts facts)
    {
        if (positions.Length == 0 || indices.Length == 0)
        {
            return SkinnedMesh.None;
        }

        var shapes = new MeshShape[extents.Length];
        for (var one = 0; one < shapes.Length; one++)
        {
            shapes[one] = new MeshShape(one.ToString(CultureInfo.InvariantCulture), extents[one].From * scale, extents[one].Count * scale);
        }

        Vector3 least = positions[0];
        Vector3 most = least;
        foreach (Vector3 point in positions)
        {
            least = Vector3.Min(least, point);
            most = Vector3.Max(most, point);
        }

        return SkinnedMesh.Of(positions, normals, indices, least, most, coordinates, shapes, facts);
    }

    /// <summary>A version-8 file's vertex format byte; nought from version 9, whose blocks carry their own.</summary>
    public int HeaderFormat { get; private init; }

    /// <summary>The u32 a version-8 file alone writes after its counts - the reference's <c>extra_header</c>, unnamed; null otherwise.</summary>
    public uint? HeaderExtra { get; private init; }

    /// <summary>A version-8 ground's unnamed pair of halves per vertex (format 2, the reference's <c>tail_float</c>), four bytes each, raw; else empty.</summary>
    public byte[] GroundExtra { get; private init; } = [];

    /// <summary>
    /// A file below version 9's tail records, raw, at that version's width - kept apart from <see cref="TailData"/>, whose
    /// 87-byte layout <see cref="Tail"/> decodes and which the older widths do not share.
    /// </summary>
    public IReadOnlyList<byte[]> OlderTail { get; private init; } = [];

    /// <summary>The header's shape count - poe_data_tools' <c>num_shapes</c>.</summary>
    public int HeaderShapes { get; private init; }

    /// <summary>The header's unnamed u16 after the shape count - poe_data_tools' <c>extra_u16</c>.</summary>
    public int HeaderWord { get; private init; }

    /// <summary>The header's unnamed u8 before the tail count - poe_data_tools' <c>extra_u8</c>.</summary>
    public int HeaderByte { get; private init; }

    /// <summary>Each ground shape's u16 before its box, in shape order - poe_data_tools' <c>ordinal</c>, which neither reference explains.</summary>
    public IReadOnlyList<int> GroundOrdinals { get; private init; } = [];

    /// <summary>The tail entries the header counts, raw: <see cref="TailEntryBytes"/> bytes each - see <see cref="Tail"/>.</summary>
    public byte[] TailData { get; private init; } = [];

    /// <summary>How many bytes one tail entry takes.</summary>
    public const int TailEntryBytes = TailBytes;

    /// <summary>
    /// One tail entry as poe_data_tools' <c>tail_entry</c> reads it: a u32, twelve floats, a u32, then 31 bytes - none of them named.
    /// </summary>
    public (uint First, float[] Floats, uint Second, byte[] Bytes) Tail(int entry)
    {
        ReadOnlySpan<byte> one = TailData.AsSpan(entry * TailBytes, TailBytes);
        var floats = new float[12];
        for (var at = 0; at < floats.Length; at++)
        {
            floats[at] = BinaryPrimitives.ReadSingleLittleEndian(one[(4 + (at * 4))..]);
        }

        return (BinaryPrimitives.ReadUInt32LittleEndian(one), floats, BinaryPrimitives.ReadUInt32LittleEndian(one[52..]), one[56..].ToArray());
    }

    /// <summary>How many per-shape records follow a block: one per shape of its first level, if it has one.</summary>
    private static int Shapes(MeshBlock block) => block.Facts.Details > 0 ? block.Facts.BlockShapes : 0;

    /// <summary>
    /// A block as a mesh, its shapes named by position.
    /// </summary>
    /// <remarks>
    /// A TILE'S SHAPES HAVE NO NAMES in the file - its materials are given by RUNS of shapes in the
    /// template, not by name - so they are numbered, which is what the runs count through.
    /// </remarks>
    private static SkinnedMesh Mesh(MeshBlock block)
    {
        if (block.Positions.Length == 0 || block.Indices.Length == 0)
        {
            return SkinnedMesh.None;
        }

        var shapes = new MeshShape[block.Extents.Length];
        for (var one = 0; one < shapes.Length; one++)
        {
            (int from, int count) = block.Extents[one];
            from = Math.Clamp(from, 0, block.Indices.Length);
            shapes[one] = new MeshShape(
                one.ToString(CultureInfo.InvariantCulture), from, Math.Clamp(count, 0, block.Indices.Length - from));
        }

        Vector3 least = block.Positions[0];
        Vector3 most = least;
        foreach (Vector3 point in block.Positions)
        {
            least = Vector3.Min(least, point);
            most = Vector3.Max(most, point);
        }

        return SkinnedMesh.Of(block.Positions, block.Normals, block.Indices, least, most, block.Coordinates, shapes, block.Facts, block.Colours);
    }
}
