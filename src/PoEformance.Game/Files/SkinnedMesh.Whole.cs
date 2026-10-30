using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace PoEformance.Game.Files;

/// <summary>One level of detail of a mesh: its shapes' extents, its indices and its vertices.</summary>
/// <param name="Extents">Each shape's first index and count, as written; a <c>.smd</c> below version 3 writes starts alone, made extents here.</param>
/// <param name="Indices">Three per triangle, as written - not held against the vertex count, which the reference does not either.</param>
/// <param name="Positions">The vertices' positions.</param>
/// <param name="Normals">Their normals, out of the four signed bytes each.</param>
/// <param name="Coordinates">Their first texture coordinates, or nought where the format carries none.</param>
public sealed record MeshLevel(IReadOnlyList<(int From, int Count)> Extents, int[] Indices, Vector3[] Positions, Vector3[] Normals, Vector2[] Coordinates)
{
    /// <summary>How many triangles the level holds.</summary>
    public int Triangles => Indices.Length / 3;
}

/// <summary>
/// A DOLm block read whole - every level of detail, and the per-shape records after them - as poe_data_tools'
/// <c>dolm</c> parser reads it.
/// </summary>
/// <param name="Corner">The <c>c0h</c> word.</param>
/// <param name="Format">The vertex format word.</param>
/// <param name="Shapes">How many shapes each level carries extents for.</param>
/// <param name="Levels">Every level, finest first.</param>
/// <param name="Bit6Records">Per shape 36 bytes where the format's bit 6 is set (the reference's <c>extra_vformat_6</c>), raw; else empty.</param>
/// <param name="Corner2Records">Per shape 4 more where <c>c0h</c> is 2 as well (<c>extra_vformat_6_c0h_2</c>), raw; else empty.</param>
/// <param name="Corner4Word">The 4 bytes after them where <c>c0h</c> is 4 and there is a level (<c>extra_c0h_4</c>), or null.</param>
/// <param name="End">The first byte past the block.</param>
/// <param name="Why">Empty where the block read; else why not.</param>
public sealed record MeshLevels(
    int Corner,
    uint Format,
    int Shapes,
    IReadOnlyList<MeshLevel> Levels,
    IReadOnlyList<byte[]> Bit6Records,
    IReadOnlyList<byte[]> Corner2Records,
    byte[]? Corner4Word,
    int End,
    string Why)
{
    /// <summary>Nothing read, with the reason.</summary>
    public static MeshLevels Stopped(string why) => new(0, 0, 0, [], [], [], null, 0, why);
}

/// <summary>One ellipsoid of a skinned mesh's tail: fifteen floats, a number and, from version 3, a name - none of it named by the reference.</summary>
public sealed record SkinEllipsoid(IReadOnlyList<float> Floats, uint Unk1, string? Name);

/// <summary>One sphere of a skinned mesh's tail: its centre and radius, a number and, from version 3, a name.</summary>
public sealed record SkinSphere(Vector3 Centre, float Radius, uint Unk1, string? Name);

/// <summary>A connection between two of the tail's spheres, by their indices.</summary>
public readonly record struct SkinSphereLink(uint First, uint Second);

/// <summary>One skinned vertex of the tail: a position, four whole numbers and four floats, unnamed.</summary>
public sealed record SkinVertex(Vector3 Position, IReadOnlyList<uint> Unk1, IReadOnlyList<float> Unk2);

/// <summary>
/// What a <c>.smd</c> carries after its geometry and names: ellipsoids, spheres and the links between them, skinned
/// vertices and two lists of references - poe_data_tools' <c>Tail</c>, whose layout its own version word chooses.
/// </summary>
/// <param name="Version">The tail's own version word: 2, 3 or 4.</param>
/// <param name="T3s">How many of the reference's empty <c>t3s</c> the tail counts - entries of no bytes, so only the count.</param>
/// <param name="Refs1">The reference's <c>sv_refs1</c>.</param>
/// <param name="Refs2">The reference's <c>sv_refs2</c>.</param>
/// <param name="Why">Empty where the tail read; else why not.</param>
public sealed record SkinTail(
    uint Version,
    IReadOnlyList<SkinEllipsoid> Ellipsoids,
    IReadOnlyList<SkinSphere> Spheres,
    IReadOnlyList<SkinSphereLink> Links,
    uint T3s,
    IReadOnlyList<SkinVertex> Vertices,
    IReadOnlyList<uint> Refs1,
    IReadOnlyList<uint> Refs2,
    string Why)
{
    /// <summary>Nothing read, with the reason.</summary>
    public static SkinTail Stopped(string why) => new(0, [], [], [], 0, [], [], [], why);
}

/// <summary>
/// A <c>.smd</c> read whole, as poe_data_tools' <c>smd</c> parser reads it: its header, every level of detail, its
/// shape names and its tail.
/// </summary>
/// <param name="Version">The file's version byte.</param>
/// <param name="Format">The outer vertex format byte.</param>
/// <param name="Names">The shape names.</param>
/// <param name="Least">The box's low corner.</param>
/// <param name="Most">Its high corner.</param>
/// <param name="Levels">The geometry: a DOLm block's levels from version 3, the one level written in the file before it.</param>
/// <param name="Word">The word a version 2 file writes after its box (the reference's <c>c04_2</c>), or null.</param>
/// <param name="Tail">What follows the names.</param>
/// <param name="Unread">How many bytes lie past the tail - the reference stops there and asks nothing of them.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record SkinnedMeshWhole(
    int Version,
    int Format,
    IReadOnlyList<string> Names,
    Vector3 Least,
    Vector3 Most,
    MeshLevels Levels,
    uint? Word,
    SkinTail Tail,
    int Unread,
    string Why);

/// <content>The whole-file reads: every level of a DOLm block, and a <c>.smd</c> with its tail.</content>
public sealed partial class SkinnedMesh
{
    /// <summary>
    /// A <c>.smd</c> read whole - every level of detail and its tail, where <see cref="Read(byte[])"/> keeps the first
    /// level alone. Never throws.
    /// </summary>
    /// <remarks>
    /// ON DEMAND, NOT ON THE DRAWING PATH: a portrait needs the finest level and nothing after the names, and reading the
    /// rest for every monster shown would cost every load the coarser copies' decode. A survey, a dump or a later level
    /// of detail asks for this.
    /// </remarks>
    public static SkinnedMeshWhole ReadWhole(byte[]? content)
    {
        if (content is not { Length: > 1 })
        {
            return Whole(0, "nothing to read");
        }

        try
        {
            return Whole(content);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException)
        {
            return Whole(content[0], $"the file does not read as a skinned mesh: {exception.Message}");
        }
    }

    /// <summary>
    /// Every level of the DOLm block at <paramref name="at"/>, and the records after them - the shared block reader
    /// (<see cref="Block"/>) keeps the first level alone. Never throws past a bounds check.
    /// </summary>
    public static MeshLevels LevelsAt(ReadOnlySpan<byte> file, int at)
    {
        if (at < 0 || at + 13 > file.Length || !file[at..].StartsWith(Magic))
        {
            return MeshLevels.Stopped("no DOLm block where the geometry should be");
        }

        at += 4;
        int corner = Read16(file, ref at);
        int lods = file[at++];
        int shapes = Read16(file, ref at);
        uint format = Read32(file, ref at);
        if (at + (lods * 8L) > file.Length)
        {
            return MeshLevels.Stopped("the level table runs past the end");
        }

        var counts = new (uint Triangles, uint Vertices)[lods];
        for (var one = 0; one < lods; one++)
        {
            counts[one] = (Read32(file, ref at), Read32(file, ref at));
        }

        Shape shape = Shape.Of(format);
        long needed = at;
        foreach ((uint triangles, uint vertices) in counts)
        {
            needed += ((long)shapes * 8) + ((long)triangles * 3 * Width(vertices)) + ((long)vertices * shape.Stride);
        }

        bool bit6 = (format >> 6 & 1) == 1;
        needed += (bit6 ? shapes * 36L : 0) + (bit6 && corner == 2 ? shapes * 4L : 0) + (corner == 4 && lods > 0 ? 4 : 0);
        if (needed > file.Length)
        {
            return MeshLevels.Stopped("the block says it is bigger than the file that holds it");
        }

        // SAFE TO NARROW: the sum above bounded every count by the file.
        var levels = new MeshLevel[lods];
        for (var one = 0; one < lods; one++)
        {
            var extents = new (int From, int Count)[shapes];
            for (var extent = 0; extent < shapes; extent++)
            {
                extents[extent] = ((int)Read32(file, ref at), (int)Read32(file, ref at));
            }

            levels[one] = Level(file, ref at, extents, (int)counts[one].Triangles, (int)counts[one].Vertices, shape);
        }

        byte[][] bit6Records = bit6 ? Records(file, ref at, shapes, 36) : [];
        byte[][] corner2Records = bit6 && corner == 2 ? Records(file, ref at, shapes, 4) : [];
        byte[]? word = null;
        if (corner == 4 && lods > 0)
        {
            word = file.Slice(at, 4).ToArray();
            at += 4;
        }

        return new MeshLevels(corner, format, shapes, levels, bit6Records, corner2Records, word, at, string.Empty);
    }

    private static SkinnedMeshWhole Whole(int version, string why)
        => new(version, 0, [], default, default, MeshLevels.Stopped(why), null, SkinTail.Stopped(why), 0, why);

    private static SkinnedMeshWhole Whole(byte[] raw)
    {
        ReadOnlySpan<byte> file = raw;
        int version = file[0];
        int at = 1;
        MeshLevels levels;
        string[] names;
        int format;
        uint? word = null;
        Vector3 least, most;

        if (version >= 3)
        {
            if (at + 1 + 2 + 4 + 24 > file.Length)
            {
                return Whole(version, "the file ends inside its own header");
            }

            format = file[at++];
            int shapes = Read16(file, ref at);
            at += 4;                                    // How many bytes the names take.
            (least, most) = Box(file, ref at);

            levels = LevelsAt(file, at);
            if (levels.Why.Length > 0)
            {
                return new SkinnedMeshWhole(version, format, [], least, most, levels, null, SkinTail.Stopped(levels.Why), 0, levels.Why);
            }

            at = levels.End;
            if (!NamesAt(file, ref at, shapes, null, out names))
            {
                return new SkinnedMeshWhole(version, format, [], least, most, levels, null, SkinTail.Stopped("no names"), 0, "the shape names run past the end");
            }
        }
        else
        {
            if (at + 8 + 1 + 2 + 4 + 24 + (version == 2 ? 4 : 0) > file.Length)
            {
                return Whole(version, "the file ends inside its own header");
            }

            uint triangles = Read32(file, ref at);
            uint vertices = Read32(file, ref at);
            format = file[at++];
            int shapes = Read16(file, ref at);
            at += 4;
            (least, most) = Box(file, ref at);
            if (version == 2)
            {
                word = Read32(file, ref at);
            }

            if (at + (shapes * 8L) > file.Length)
            {
                return Whole(version, "the shape table runs past the end");
            }

            var lengths = new int[shapes];
            var starts = new long[shapes];
            for (var one = 0; one < shapes; one++)
            {
                lengths[one] = (int)Math.Min(Read32(file, ref at), int.MaxValue);
                starts[one] = Read32(file, ref at);
            }

            if (!NamesAt(file, ref at, shapes, lengths, out names))
            {
                return Whole(version, "the shape names run past the end");
            }

            Shape shape = Shape.Older((byte)format);
            if (at + ((long)triangles * 3 * Width(vertices)) + ((long)vertices * shape.Stride) > file.Length)
            {
                return Whole(version, "the geometry says it is bigger than the file that holds it");
            }

            // A START IN TRIANGLES, AND THE NEXT ONE IS WHERE IT ENDS - made extents of indices, as Older reads them.
            var extents = new (int From, int Count)[shapes];
            for (var one = 0; one < shapes; one++)
            {
                long from = Math.Clamp(starts[one] * 3, 0, triangles * 3L);
                long to = one + 1 < shapes ? Math.Clamp(starts[one + 1] * 3, from, triangles * 3L) : triangles * 3L;
                extents[one] = ((int)from, (int)(to - from));
            }

            MeshLevel level = Level(file, ref at, extents, (int)triangles, (int)vertices, shape);
            levels = new MeshLevels(0, (uint)format, shapes, [level], [], [], null, at, string.Empty);
        }

        SkinTail tail = TailAt(file, ref at, version);
        return new SkinnedMeshWhole(version, format, names, least, most, levels, word, tail, tail.Why.Length == 0 ? file.Length - at : 0, tail.Why);
    }

    private static (Vector3 Least, Vector3 Most) Box(ReadOnlySpan<byte> file, ref int at)
    {
        var least = new Vector3(Float(file, at), Float(file, at + 8), Float(file, at + 16));
        var most = new Vector3(Float(file, at + 4), Float(file, at + 12), Float(file, at + 20));
        at += 24;
        return (least, most);
    }

    /// <summary>
    /// The shape names: from version 3 every length first, then every name; below it the lengths came with the shape
    /// table (<paramref name="lengths"/>) and only the names follow. UTF-16, as the reference decodes them.
    /// </summary>
    private static bool NamesAt(ReadOnlySpan<byte> file, ref int at, int shapes, int[]? lengths, out string[] names)
    {
        names = [];
        if (lengths is null)
        {
            if (at + (shapes * 4L) > file.Length)
            {
                return false;
            }

            lengths = new int[shapes];
            for (var one = 0; one < shapes; one++)
            {
                lengths[one] = (int)Math.Min(Read32(file, ref at), int.MaxValue);
            }
        }

        var read = new string[shapes];
        for (var one = 0; one < shapes; one++)
        {
            if (lengths[one] < 0 || at + (long)lengths[one] > file.Length)
            {
                return false;
            }

            read[one] = Encoding.Unicode.GetString(file.Slice(at, lengths[one]));
            at += lengths[one];
        }

        names = read;
        return true;
    }

    /// <summary>One level's indices and vertices at <paramref name="at"/>, the caller having bounded both by the file.</summary>
    private static MeshLevel Level(ReadOnlySpan<byte> file, ref int at, (int From, int Count)[] extents, int triangles, int vertices, Shape shape)
    {
        int width = Width((uint)vertices);
        var indices = new int[triangles * 3];
        for (var one = 0; one < indices.Length; one++, at += width)
        {
            indices[one] = width == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(file[at..]) : (int)BinaryPrimitives.ReadUInt32LittleEndian(file[at..]);
        }

        var positions = new Vector3[vertices];
        var normals = new Vector3[vertices];
        var coordinates = new Vector2[vertices];
        for (var one = 0; one < vertices; one++, at += shape.Stride)
        {
            positions[one] = new Vector3(Float(file, at), Float(file, at + 4), Float(file, at + 8));
            normals[one] = Direction(file, at + shape.Normal);
            if (shape.Coordinate >= 0)
            {
                coordinates[one] = new Vector2(
                    (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(file[(at + shape.Coordinate)..])),
                    (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(file[(at + shape.Coordinate + 2)..])));
            }
        }

        return new MeshLevel(extents, indices, positions, normals, coordinates);
    }

    private static byte[][] Records(ReadOnlySpan<byte> file, ref int at, int count, int width)
    {
        var records = new byte[count][];
        for (var one = 0; one < count; one++, at += width)
        {
            records[one] = file.Slice(at, width).ToArray();
        }

        return records;
    }

    /// <summary>
    /// The tail after the names, by its own version word, in the order the reference reads each: version 2 counts its
    /// ellipsoids, skinned vertices and two reference lists and reads the vertices first; 3 counts seven and reads them
    /// in that order; 4 counts the same seven and reads the vertices and the first references before the rest. An
    /// ellipsoid's or a sphere's name is there from the FILE's version 3.
    /// </summary>
    private static SkinTail TailAt(ReadOnlySpan<byte> file, ref int at, int version)
    {
        if (at + 4 > file.Length)
        {
            return SkinTail.Stopped("no tail version after the names");
        }

        uint kind = Read32(file, ref at);
        int counts = kind == 2 ? 4 : 7;
        if (kind is < 2 or > 4)
        {
            return SkinTail.Stopped($"tail version {kind}, which the reference does not read");
        }

        if (at + (counts * 4L) > file.Length)
        {
            return SkinTail.Stopped("the tail's counts run past the end");
        }

        var count = new uint[counts];
        for (var one = 0; one < counts; one++)
        {
            count[one] = Read32(file, ref at);
        }

        // THE COUNTS IN THE FILE'S ORDER: v2 ellipsoids, vertices, refs1, refs2; v3 and v4 ellipsoids, spheres, links, t3s, vertices, refs1, refs2.
        (uint ellipsoids, uint spheres, uint links, uint t3s, uint vertices, uint refs1, uint refs2) = kind == 2
            ? (count[0], 0u, 0u, 0u, count[1], count[2], count[3])
            : (count[0], count[1], count[2], count[3], count[4], count[5], count[6]);

        var tail = new TailReader(file, at, version >= 3);
        List<SkinEllipsoid>? ellipsoid = null;
        List<SkinSphere>? sphere = null;
        List<SkinSphereLink>? link = null;
        List<SkinVertex>? vertex = null;
        uint[]? first = null;
        uint[]? second = null;
        string? why = kind switch
        {
            2 => tail.Vertices(vertices, out vertex) ?? tail.Words(refs1, out first) ?? tail.Ellipsoids(ellipsoids, out ellipsoid) ?? tail.Words(refs2, out second),
            3 => tail.Ellipsoids(ellipsoids, out ellipsoid) ?? tail.Spheres(spheres, out sphere) ?? tail.Links(links, out link)
                ?? tail.Vertices(vertices, out vertex) ?? tail.Words(refs1, out first) ?? tail.Words(refs2, out second),
            _ => tail.Vertices(vertices, out vertex) ?? tail.Words(refs1, out first) ?? tail.Ellipsoids(ellipsoids, out ellipsoid)
                ?? tail.Spheres(spheres, out sphere) ?? tail.Links(links, out link) ?? tail.Words(refs2, out second),
        };

        at = tail.At;
        return new SkinTail(kind, ellipsoid ?? [], sphere ?? [], link ?? [], t3s, vertex ?? [], first ?? [], second ?? [], why ?? string.Empty);
    }

    /// <summary>The tail's walk: each read bounds its count by the bytes left before it makes anything, and says why it stopped.</summary>
    private ref struct TailReader(ReadOnlySpan<byte> file, int at, bool named)
    {
        private readonly ReadOnlySpan<byte> _file = file;

        public int At { get; private set; } = at;

        public string? Ellipsoids(uint count, out List<SkinEllipsoid>? read)
        {
            read = null;
            if (count > (uint)((_file.Length - At) / (60 + 4 + (named ? 4 : 0))))
            {
                return "the ellipsoids run past the end";
            }

            var list = new List<SkinEllipsoid>((int)count);
            for (uint one = 0; one < count; one++)
            {
                if (_file.Length - At < 64)
                {
                    return "an ellipsoid runs past the end";
                }

                var floats = new float[15];
                for (var value = 0; value < 15; value++)
                {
                    floats[value] = BinaryPrimitives.ReadSingleLittleEndian(_file[(At + (value * 4))..]);
                }

                uint unk1 = BinaryPrimitives.ReadUInt32LittleEndian(_file[(At + 60)..]);
                At += 64;
                if (Name() is not (true, var name))
                {
                    return "an ellipsoid's name runs past the end";
                }

                list.Add(new SkinEllipsoid(floats, unk1, name));
            }

            read = list;
            return null;
        }

        public string? Spheres(uint count, out List<SkinSphere>? read)
        {
            read = null;
            if (count > (uint)((_file.Length - At) / (20 + (named ? 4 : 0))))
            {
                return "the spheres run past the end";
            }

            var list = new List<SkinSphere>((int)count);
            for (uint one = 0; one < count; one++)
            {
                if (_file.Length - At < 20)
                {
                    return "a sphere runs past the end";
                }

                var centre = new Vector3(Single(0), Single(4), Single(8));
                float radius = Single(12);
                uint unk1 = BinaryPrimitives.ReadUInt32LittleEndian(_file[(At + 16)..]);
                At += 20;
                if (Name() is not (true, var name))
                {
                    return "a sphere's name runs past the end";
                }

                list.Add(new SkinSphere(centre, radius, unk1, name));
            }

            read = list;
            return null;
        }

        public string? Links(uint count, out List<SkinSphereLink>? read)
        {
            read = null;
            if (count > (uint)((_file.Length - At) / 8))
            {
                return "the sphere links run past the end";
            }

            var list = new List<SkinSphereLink>((int)count);
            for (uint one = 0; one < count; one++, At += 8)
            {
                list.Add(new SkinSphereLink(BinaryPrimitives.ReadUInt32LittleEndian(_file[At..]), BinaryPrimitives.ReadUInt32LittleEndian(_file[(At + 4)..])));
            }

            read = list;
            return null;
        }

        public string? Vertices(uint count, out List<SkinVertex>? read)
        {
            read = null;
            if (count > (uint)((_file.Length - At) / 44))
            {
                return "the skinned vertices run past the end";
            }

            var list = new List<SkinVertex>((int)count);
            for (uint one = 0; one < count; one++, At += 44)
            {
                var numbers = new uint[4];
                var floats = new float[4];
                for (var value = 0; value < 4; value++)
                {
                    numbers[value] = BinaryPrimitives.ReadUInt32LittleEndian(_file[(At + 12 + (value * 4))..]);
                    floats[value] = Single(28 + (value * 4));
                }

                list.Add(new SkinVertex(new Vector3(Single(0), Single(4), Single(8)), numbers, floats));
            }

            read = list;
            return null;
        }

        public string? Words(uint count, out uint[]? read)
        {
            read = null;
            if (count > (uint)((_file.Length - At) / 4))
            {
                return "a list of references runs past the end";
            }

            var words = new uint[count];
            for (var one = 0; one < words.Length; one++, At += 4)
            {
                words[one] = BinaryPrimitives.ReadUInt32LittleEndian(_file[At..]);
            }

            read = words;
            return null;
        }

        private readonly float Single(int offset) => BinaryPrimitives.ReadSingleLittleEndian(_file[(At + offset)..]);

        /// <summary>A u32 byte length and that many bytes of UTF-16, from the file's version 3; nothing before.</summary>
        private (bool Read, string? Name) Name()
        {
            if (!named)
            {
                return (true, null);
            }

            if (_file.Length - At < 4)
            {
                return (false, null);
            }

            uint bytes = BinaryPrimitives.ReadUInt32LittleEndian(_file[At..]);
            if (bytes > (uint)(_file.Length - At - 4) || bytes % 2 != 0)
            {
                return (false, null);
            }

            string name = Encoding.Unicode.GetString(_file.Slice(At + 4, (int)bytes));
            At += 4 + (int)bytes;
            return (true, name);
        }
    }
}
