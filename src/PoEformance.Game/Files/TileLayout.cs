using System.Buffers.Binary;
using System.Text;

namespace PoEformance.Game.Files;

/// <summary>
/// A tile definition (<c>.tdt</c>) read WHOLE by zao's layout - every field after the corners the tool's own reader
/// (<see cref="TileDefinition"/>) steps past - with where each field stood, and whether the walk ends exactly at the file's end.
/// </summary>
/// <remarks>
/// A MEASUREMENT, NOT YET A READER THE TOOL DRAWS BY. The layout is zao's <c>libpoe/poe/format/tdt.cpp</c> from
/// <c>gitlab.com/zao/poe-cpp</c>, whose last commit (2022-07-02) is older than PoE2: a PoE1 reference, settled for nothing here
/// until PoE2 files agree. What it reads past the corner grounds: a u16 he calls zero, a second size of two bytes, eight bytes of
/// side offsets (read by his own code two ways - a pair a side, real and virtual, in game_data.cpp; four edge offsets then four
/// ground offsets in level_gen.cpp), a flags byte (bit 0 heights at the end, bit 1 a ground grid, bit 4 a block of a u32 and six
/// floats), the sub-tiles - a type and a value each, then by the type a 23 x 23 byte block, a block of a counted length, both or
/// neither, and in version 4 alone a template of its own - the ground grid, the heights, the <c>.tmd</c> (6 on) and a reference
/// and a byte (7). His reader then asks for the file's end: a field read from the wrong place does not land on it, which is the
/// self-check this exists for (the capture's tile-layout part counts how many of the install's files it holds for).
///
/// THE WALK GOES ON WHERE HIS READER WOULD STOP AT A STRING. His leading reference, template, tag and a version 4 sub-tile's
/// template must each name a string of the table; this reads on and writes such a field down in <see cref="Refused"/>, so the
/// layout and the strings are judged apart. Where a field does not fit - an unknown sub-tile type, a block past the file's end -
/// it stops and <see cref="Stop"/> says where.
///
/// A VERSION 7 FILE WHOSE LEADING REFERENCE IS FILLED is read on as zao reads it; annalithic's Tdt.cs, which the tool's reader
/// follows, takes such a file to carry nothing else. <see cref="LeadEnd"/> keeps the byte after the reference, so both readings can
/// be held against the file's length.
/// </remarks>
public sealed class TileLayout
{
    /// <summary>The cells a tile is across - zao's <c>tileSize</c>: a sub-tile's fixed block is one byte a cell.</summary>
    public const int Cells = 23;

    /// <summary>zao's <c>paddedSize</c>: the side of version 3's distance block.</summary>
    public const int Padded = 31;

    /// <summary>The flags bit for the heights at the end - zao's <c>FlagsHasTailBytes</c>.</summary>
    public const byte FlagHeights = 0b1;

    /// <summary>The flags bit for the ground grid - zao's <c>FlagsHasGtGrid</c>.</summary>
    public const byte FlagGrid = 0b10;

    /// <summary>The flags bit zao names and reads nothing for - <c>FlagsHasUnkBit2</c>.</summary>
    public const byte FlagUnknown2 = 0b100;

    /// <summary>The flags bit for the block of a u32 and six floats - zao's <c>FlagsHasUnkBit4</c>.</summary>
    public const byte FlagExtra = 0b1_0000;

    private readonly List<TileLayoutField> _fields = [];
    private readonly List<string> _refused = [];
    private readonly List<TileLayoutSubTile> _subTiles = [];

    private TileLayout()
    {
    }

    /// <summary>The file's version, or nought where there were not four bytes.</summary>
    public int Version { get; private set; }

    /// <summary>The file's length in bytes.</summary>
    public int Length { get; private set; }

    /// <summary>The strings of the front table by the character offset each begins at, empty ones included as zao keys them.</summary>
    public IReadOnlyDictionary<uint, string> Strings { get; private set; } = new Dictionary<uint, string>();

    /// <summary>Every field walked, in the file's order: its name, where it began, how many bytes it took and what it said.</summary>
    public IReadOnlyList<TileLayoutField> Fields => _fields;

    /// <summary>The fields zao's reader would refuse though the walk went on: a reference his reader requires naming no string.</summary>
    public IReadOnlyList<string> Refused => _refused;

    /// <summary>Version 7's leading reference (the tool's "inherits"), or none.</summary>
    public TileRef? Lead { get; private set; }

    /// <summary>The byte after <see cref="Lead"/> - where annalithic's reading has a filled one end - or nought.</summary>
    public int LeadEnd { get; private set; }

    /// <summary>The template every sub-tile shares (version 5 on, and version 3 after its block), or none.</summary>
    public TileRef? Template { get; private set; }

    /// <summary>The tag.</summary>
    public TileRef? Tag { get; private set; }

    /// <summary>The four side edge types as written, in zao's <c>sideEts</c> order.</summary>
    public IReadOnlyList<TileRef> SideEdges { get; private set; } = [];

    /// <summary>Tiles across.</summary>
    public int Width { get; private set; }

    /// <summary>Tiles down.</summary>
    public int Height { get; private set; }

    /// <summary>The four corner ground types as written, in zao's <c>sideGts</c> order.</summary>
    public IReadOnlyList<TileRef> SideGrounds { get; private set; } = [];

    /// <summary>zao's <c>zero1</c>: two bytes (four in version 3) he expects to be nought.</summary>
    public uint Zero { get; private set; }

    /// <summary>zao's <c>dim2</c>, a second size of two signed bytes (version 4 on).</summary>
    public (int X, int Y) Size2 { get; private set; }

    /// <summary>zao's eight <c>sideOffsets</c> - bytes, or version 3's u32s.</summary>
    public IReadOnlyList<int> SideOffsets { get; private set; } = [];

    /// <summary>The flags byte (version 4 on).</summary>
    public byte Flags { get; private set; }

    /// <summary>The sub-tiles as listed (version 4 on).</summary>
    public IReadOnlyList<TileLayoutSubTile> SubTiles => _subTiles;

    /// <summary>The block of <see cref="FlagExtra"/>: its u32, or null where the flag is clear.</summary>
    public uint? ExtraWord { get; private set; }

    /// <summary>The block of <see cref="FlagExtra"/>: its six floats, or empty.</summary>
    public IReadOnlyList<float> ExtraFloats { get; private set; } = [];

    /// <summary>The ground grid, (Width + 1) a row and Height + 1 rows as zao reads it - or empty where the file carries none.</summary>
    public IReadOnlyList<TileRef> GroundGrid { get; private set; } = [];

    /// <summary>The bytes at the end zao calls <c>tailBytes</c> and reads the four corner heights from, (Width + 1) x (Height + 1) - or empty.</summary>
    public IReadOnlyList<byte> Heights { get; private set; } = [];

    /// <summary>The tile's doodad list (version 6 on), or none.</summary>
    public TileRef? Tmd { get; private set; }

    /// <summary>Version 7's last reference, or none.</summary>
    public TileRef? Last { get; private set; }

    /// <summary>Version 7's last byte, after <see cref="Last"/>.</summary>
    public int? LastByte { get; private set; }

    /// <summary>Where the walk stopped, in bytes from the start.</summary>
    public int End { get; private set; }

    /// <summary>Why the walk stopped before the file's end, or what is left past it - empty where it ended exactly at the end.</summary>
    public string Stop { get; private set; } = string.Empty;

    /// <summary>Whether zao's layout took the file to its last byte and not one past.</summary>
    public bool Whole => Stop.Length == 0;

    /// <summary>The four corner heights zao's TileKey reads from <see cref="Heights"/>: down-left, down-right, up-right, up-left - or null.</summary>
    /// <remarks>
    /// His reading, written here to be held against a room's slots, not taken: row nought is the least y (his "bottom"), a row
    /// Width + 1 bytes - <c>tail[0]</c>, <c>tail[Width]</c>, <c>tail[Height * (Width + 1) + Width]</c>, <c>tail[Height * (Width + 1)]</c>.
    /// </remarks>
    public (int DownLeft, int DownRight, int UpRight, int UpLeft)? CornerHeights
    {
        get
        {
            int across = Width + 1;
            if (Heights.Count != across * (Height + 1) || Heights.Count == 0)
            {
                return null;
            }

            int top = Height * across;
            return (Heights[0], Heights[Width], Heights[top + Width], Heights[top]);
        }
    }

    /// <summary>Walks a file's bytes by zao's layout. Never throws.</summary>
    public static TileLayout Read(byte[]? content)
    {
        var layout = new TileLayout();
        if (content is null)
        {
            layout.Stop = "no file";
            return layout;
        }

        layout.Length = content.Length;
        var walk = new Walk(content, layout);
        walk.Run();
        if (walk.Short.Length > 0)
        {
            layout.Stop = walk.Short;
        }

        layout.End = walk.At;
        if (layout.Stop.Length == 0 && walk.At != content.Length)
        {
            layout.Stop = FormattableString.Invariant($"{content.Length - walk.At} bytes past the walk's end");
        }

        return layout;
    }

    /// <summary>What a reference names, in a few words: the string, or none and the word itself.</summary>
    public static string Said(TileRef? one)
        => one is not { } named ? "-" : named.Text is { } text ? (text.Length > 0 ? text : "\"\"") : FormattableString.Invariant($"(no string at 0x{named.Raw:X})");

    /// <summary>The walk itself: a place in the bytes and the layout it fills.</summary>
    private sealed class Walk(byte[] bytes, TileLayout layout)
    {
        public int At { get; private set; }

        /// <summary>The first field that reached past the file, in words - empty while every one fitted. Past it nothing is read.</summary>
        public string Short { get; private set; } = string.Empty;

        public void Run()
        {
            layout.Version = (int)U32("version");
            if (layout.Version is < 3 or > 7)
            {
                layout.Stop = FormattableString.Invariant($"version {layout.Version} is outside the 3 to 7 zao reads");
                return;
            }

            if (!ReadTable())
            {
                return;
            }

            if (layout.Version >= 7)
            {
                layout.Lead = Ref("leading reference", strict: true);
                layout.LeadEnd = At;
            }

            if (layout.Version > 4)
            {
                layout.Template = Ref("template", strict: true);
            }

            layout.Tag = Ref("tag", strict: true);
            layout.SideEdges = [Ref("side edge 0"), Ref("side edge 1"), Ref("side edge 2"), Ref("side edge 3")];
            if (layout.Version == 3)
            {
                layout.Width = (int)U32("width");
                layout.Height = (int)U32("height");
            }
            else
            {
                layout.Width = (sbyte)U8("width");
                layout.Height = (sbyte)U8("height");
            }

            layout.SideGrounds = [Ref("corner ground 0"), Ref("corner ground 1"), Ref("corner ground 2"), Ref("corner ground 3")];
            layout.Zero = layout.Version == 3 ? U32("zero") : U16("zero");
            if (layout.Version > 3)
            {
                layout.Size2 = ((sbyte)U8("second width"), (sbyte)U8("second height"));
            }

            var offsets = new int[8];
            int start = At;
            for (var one = 0; one < offsets.Length; one++)
            {
                offsets[one] = layout.Version == 3 ? (int)Raw32() : Raw8();
            }

            layout.SideOffsets = offsets;
            Field("side offsets", start, string.Join(' ', offsets));

            if (layout.Version == 3)
            {
                Skip("version 3 sub-tile: u32", 4);
                Skip("version 3 sub-tile: fixed block", Cells * Cells);
                Skip("version 3 sub-tile: u32", 4);
                Skip("version 3 sub-tile: distance block", Padded * Padded);
                U8("version 3: byte");
                layout.Template = Ref("template", strict: true);
            }
            else if (!SubTiles())
            {
                return;
            }

            if ((layout.Flags & FlagExtra) != 0)
            {
                layout.ExtraWord = U32("extra block: u32");
                var floats = new float[6];
                start = At;
                for (var one = 0; one < floats.Length; one++)
                {
                    floats[one] = BitConverter.Int32BitsToSingle((int)Raw32());
                }

                layout.ExtraFloats = floats;
                Field("extra block: six floats", start, string.Join(' ', floats.Select(one => one.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture))));
            }

            int corners = Math.Max(0, layout.Width + 1) * Math.Max(0, layout.Height + 1);
            if (layout.Version == 3 || (layout.Flags & FlagGrid) != 0)
            {
                var grid = new TileRef[corners];
                start = At;
                for (var one = 0; one < corners; one++)
                {
                    grid[one] = RawRef();
                }

                layout.GroundGrid = grid;
                Field("ground grid", start, FormattableString.Invariant($"{corners} references, {grid.Count(one => one.Text is { Length: > 0 })} naming a string"));
            }

            if (layout.Version == 3)
            {
                Skip("version 3: four zero words", 16);
            }

            if ((layout.Flags & FlagHeights) != 0)
            {
                start = At;
                layout.Heights = Bytes(corners, "heights");
                Field("heights", start, string.Join(' ', layout.Heights));
            }

            if (layout.Version >= 6)
            {
                layout.Tmd = Ref("tmd");
            }

            if (layout.Version >= 7)
            {
                layout.Last = Ref("last reference");
                layout.LastByte = U8("last byte");
            }
        }

        /// <summary>The front table, keyed by each string's start as zao's reader keys it - false where it does not read.</summary>
        private bool ReadTable()
        {
            uint chars = U32("table length in characters");
            long bytesOf = (long)chars * 2;
            if (At + bytesOf > bytes.Length)
            {
                layout.Stop = FormattableString.Invariant($"the table says {chars} characters, past the file's end");
                return false;
            }

            var strings = new Dictionary<uint, string>();
            int start = At;
            int end = At + (int)bytesOf;
            uint offset = 0;
            while (offset < chars)
            {
                int from = start + ((int)offset * 2);
                int to = from;
                while (to + 2 <= end && BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(to)) != 0)
                {
                    to += 2;
                }

                if (to + 2 > end)
                {
                    layout.Stop = FormattableString.Invariant($"the table's string at character {offset} runs past the table without its NUL");
                    At = end;
                    return false;
                }

                strings[offset] = Encoding.Unicode.GetString(bytes, from, to - from);
                offset += (uint)((to - from) / 2) + 1;
            }

            layout.Strings = strings;
            At = end;
            Field("string table", start, FormattableString.Invariant($"{strings.Count} strings"));
            return true;
        }

        /// <summary>The flags, the count and every sub-tile - false where one does not fit zao's types.</summary>
        private bool SubTiles()
        {
            layout.Flags = U8("flags");
            int count = U16("sub-tiles");
            int first = At;
            for (var index = 0; index < count && Short.Length == 0; index++)
            {
                int start = At;
                byte type = Raw8();
                byte value = Raw8();

                // zao's twelve types and the blocks each carries; any other stops the walk, as it stops his reader.
                (bool Fixed, bool Varying)? carries = type switch
                {
                    0x00 or 0x02 or 0x09 => (true, true),
                    0x03 or 0x0B or 0x1B => (true, false),
                    0x04 or 0x06 or 0x0D => (false, true),
                    0x07 or 0x0F or 0x1F => (false, false),
                    _ => null,
                };

                if (Short.Length > 0)
                {
                    break;
                }

                if (carries is not { } blocks)
                {
                    layout.Stop = FormattableString.Invariant($"sub-tile {index} at byte {start} is of type 0x{type:X2}, none of the twelve zao reads");
                    At = start;
                    return false;
                }

                byte[]? fixedBlock = blocks.Fixed ? Bytes(Cells * Cells, "a sub-tile's fixed block") : null;
                byte[]? varying = null;
                if (blocks.Varying)
                {
                    int length = Raw16();
                    varying = Bytes(length, "a sub-tile's block");
                }

                TileRef? template = null;
                if (layout.Version == 4)
                {
                    template = RawRef();
                    if (template.Value.Text is null)
                    {
                        layout._refused.Add(FormattableString.Invariant($"sub-tile {index}'s template 0x{template.Value.Raw:X} names no string"));
                    }
                }

                layout._subTiles.Add(new TileLayoutSubTile(start, type, value, fixedBlock, varying, template));
            }

            Field("sub-tiles", first, FormattableString.Invariant($"{count} listed, types {string.Join(' ', layout._subTiles.Select(one => one.Type.ToString("X2", System.Globalization.CultureInfo.InvariantCulture)).Distinct())}"));
            return true;
        }

        private TileRef Ref(string name, bool strict = false)
        {
            int start = At;
            TileRef one = RawRef();
            if (strict && one.Text is null)
            {
                layout._refused.Add(FormattableString.Invariant($"{name} 0x{one.Raw:X} names no string"));
            }

            Field(name, start, Said(one));
            return one;
        }

        private TileRef RawRef()
        {
            uint raw = Raw32();
            return new TileRef(raw, layout.Strings.TryGetValue(raw, out string? text) ? text : null);
        }

        private uint U32(string name)
        {
            int start = At;
            uint value = Raw32();
            Field(name, start, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return value;
        }

        private ushort U16(string name)
        {
            int start = At;
            ushort value = Raw16();
            Field(name, start, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return value;
        }

        private byte U8(string name)
        {
            int start = At;
            byte value = Raw8();
            Field(name, start, value.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return value;
        }

        private uint Raw32()
        {
            if (!Has(4, "a word"))
            {
                return 0;
            }

            uint value = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(At));
            At += 4;
            return value;
        }

        private ushort Raw16()
        {
            if (!Has(2, "two bytes"))
            {
                return 0;
            }

            ushort value = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(At));
            At += 2;
            return value;
        }

        private byte Raw8() => Has(1, "a byte") ? bytes[At++] : (byte)0;

        private byte[] Bytes(int count, string what)
        {
            if (!Has(count, what))
            {
                return [];
            }

            byte[] block = bytes.AsSpan(At, count).ToArray();
            At += count;
            return block;
        }

        private void Skip(string name, int count)
        {
            int start = At;
            if (Has(count, name))
            {
                At += count;
            }

            Field(name, start, FormattableString.Invariant($"{count} bytes"));
        }

        /// <summary>
        /// Whether <paramref name="count"/> bytes are left - else the first shortfall is kept and nothing past it is read: every read
        /// after it gives nought without moving, so the walk runs out without a field read from past the file's end.
        /// </summary>
        private bool Has(int count, string what)
        {
            if (Short.Length > 0)
            {
                return false;
            }

            if (count >= 0 && At + count <= bytes.Length)
            {
                return true;
            }

            Short = FormattableString.Invariant($"{what} at byte {At} needs {count} bytes, {bytes.Length - At} are left");
            return false;
        }

        private void Field(string name, int start, string said) => layout._fields.Add(new TileLayoutField(name, start, At - start, said));
    }
}

/// <summary>A string reference as written: the word, and the string of the table it names - null where it names none.</summary>
/// <param name="Raw">The word as written.</param>
/// <param name="Text">The string beginning at that character offset, or null.</param>
public readonly record struct TileRef(uint Raw, string? Text);

/// <summary>One field of a walked tile definition.</summary>
/// <param name="Name">What zao's layout calls it.</param>
/// <param name="At">Where it began, in bytes from the start.</param>
/// <param name="Bytes">How many bytes it took.</param>
/// <param name="Said">Its value in words.</param>
public readonly record struct TileLayoutField(string Name, int At, int Bytes, string Said);

/// <summary>One sub-tile of a walked definition, version 4 on.</summary>
/// <param name="At">Where it began.</param>
/// <param name="Type">zao's <c>type</c>, which says which blocks follow.</param>
/// <param name="Value">zao's <c>val</c>.</param>
/// <param name="Fixed">The 23 x 23 block, a byte a cell - or null where the type carries none.</param>
/// <param name="Varying">The block of a counted length - or null.</param>
/// <param name="Template">Version 4's own template of the sub-tile - or null.</param>
public sealed record TileLayoutSubTile(int At, byte Type, byte Value, byte[]? Fixed, byte[]? Varying, TileRef? Template);
