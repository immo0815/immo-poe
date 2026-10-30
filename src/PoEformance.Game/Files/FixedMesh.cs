using System.Buffers.Binary;
using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>One piece of a fixed mesh - poe_data_tools' <c>Subcomponent</c>: an unnamed byte, its 12-byte records, raw, and a tag.</summary>
public sealed record FixedMeshPiece(byte Unk1, IReadOnlyList<byte[]> Records, string Tag);

/// <summary>
/// A prop's geometry, out of the game's own <c>.fmt</c> files.
/// </summary>
/// <remarks>
/// THE OTHER HALF OF WHAT A MONSTER IS MADE OF, and the half this tool could not see. An .ao
/// hangs two kinds of thing off a bone: a <c>SkinMesh</c>, which names a .sm and then a .smd, and
/// a <c>FixedMesh</c>, which names a .fmt and nothing else. Nothing here read the second kind, so
/// every one of them came back from the model walk as "no SkinMesh - an effect or a sound" and
/// was left out. Reported from the live client: Malgor, the Nautilord carries a cannon on his
/// shoulder that the pane never drew, and his .ao says why in one line -
/// <c>FixedMesh { fixed_mesh = "…/PirateBossCannon.fmt" }</c>.
///
/// A FIXED MESH IS NOT SKINNED, which is what makes it a different file and an easier one: no
/// bones, no weights, no rig, no manifest beside it. It is a rigid prop that sits where its
/// socket puts it, and it CARRIES ITS OWN MATERIALS - one .mat path per shape, in a string pool
/// at the end of the file - so there is no .sm to look them up in and none is read.
///
/// VERSION 9 WRAPS THE SAME DOLm BLOCK A .smd DOES, which is why this file is short: the
/// geometry is <see cref="SkinnedMesh.Block"/>'s, shared rather than copied, so the four-byte
/// error that cost this project a day stays fixed in one place. Versions below nine put the
/// vertices in a layout of their own, read from v0.1.329 as the reference reads it - see Older.
///
/// THE LAYOUT comes from poe_data_tools' fmt parser. What the reader checks for itself is the
/// string pool: every shape's name and material are OFFSETS into it, so a walk that arrived at
/// the pool one step out lands those offsets in the middle of words or past the end - which is
/// visible, cheap to test, and exactly the failure mode that hid in the .smd reader for months.
/// </remarks>
public sealed class FixedMesh
{
    /// <summary>Nothing read - a missing file, or one that is not a fixed mesh.</summary>
    public static FixedMesh None { get; } = new() { Why = "nothing to read" };

    /// <summary>The geometry, with one named shape per entry of <see cref="Named"/>.</summary>
    public SkinnedMesh Mesh { get; private init; } = SkinnedMesh.None;

    /// <summary>
    /// Each shape's name and the <c>.mat</c> the file puts on it, in the mesh's shape order.
    /// </summary>
    /// <remarks>
    /// THE SAME SHAPE THE .ao's OWN MATERIAL LINES TAKE, deliberately: the model walk already
    /// matches a shape to a material by name through that list, so a fixed mesh's materials go
    /// down the path that is there rather than a second one beside it.
    /// </remarks>
    public IReadOnlyList<(string Shape, string Material)> Named { get; private init; } = [];

    /// <summary>Why nothing was read, or empty where something was.</summary>
    public string Why { get; private init; } = string.Empty;

    /// <summary>Whether there is anything to draw.</summary>
    public bool Ready => Mesh.Ready;

    /// <summary>Reads one out of an open install, by the path a <c>FixedMesh</c> block named.</summary>
    public static FixedMesh Read(GameFiles? files, string? path)
        => files is null || string.IsNullOrWhiteSpace(path)
            ? Failed("no install, or no path")
            : Read(files.Read(path.Replace('\\', '/').Trim()));

    /// <summary>
    /// Reads a file's bytes. A file this cannot make sense of comes back with a reason, not a throw.
    /// </summary>
    /// <remarks>
    /// NEVER THROWS, for the same reason <see cref="SkinnedMesh.Read(byte[])"/> does not: the
    /// caller is a window drawing a monster somebody clicked on, and a prop that will not read is
    /// a prop that does not appear rather than a tool that closes.
    /// </remarks>
    public static FixedMesh Read(byte[]? content)
    {
        if (content is not { Length: > HeaderBytes })
        {
            return Failed("nothing to read");
        }

        try
        {
            return Parse(content);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException
            or IndexOutOfRangeException or OverflowException)
        {
            return Failed($"the file does not read as a fixed mesh: {exception.Message}");
        }
    }

    /// <summary>Version, counts and a box - the smallest a file can be and still have a header.</summary>
    private const int HeaderBytes = 1 + 6 + 24;

    /// <summary>
    /// How wide one of the trailing records is, by version.
    /// </summary>
    /// <remarks>
    /// NOT READ, ONLY STEPPED OVER - their content is unknown to every reader of this format
    /// there is. They still have to be counted exactly, because the string pool sits past them
    /// and the shape names are offsets into it. The widths are the reference parser's.
    /// </remarks>
    private static int Trailing(int version) => version switch
    {
        < 3 => 45,
        3 => 70,
        4 or 5 => 78,
        6 => 83,
        _ => 87,
    };

    private static FixedMesh Parse(byte[] raw)
    {
        ReadOnlySpan<byte> file = raw;
        var at = 0;

        int version = file[at++];

        // VERSIONS BELOW NINE PUT THE VERTICES IN A LAYOUT OF THEIR OWN rather than in a DOLm block - see Older.
        if (version < 9)
        {
            return Older(file, version);
        }

        int shapes = Read16(file, ref at);
        int pieces = file[at++];
        at += 2;                                    // How many of the per-piece records there are.
        int records = file[at++];

        if (at + 24 > file.Length)
        {
            return Failed("the file ends inside its own header");
        }

        // The box, PER AXIS, the same way a .smd writes it: min x, max x, min y, max y, min z, max z.
        var least = new Vector3(Float(file, at), Float(file, at + 8), Float(file, at + 16));
        var most = new Vector3(Float(file, at + 4), Float(file, at + 12), Float(file, at + 20));
        at += 24;

        MeshBlock block = SkinnedMesh.Block(file, at);
        if (!block.Ready)
        {
            return Failed(block.Why);
        }

        at = block.At;

        // ONE PAIR OF OFFSETS PER SHAPE - a name and a material, both into the pool at the end.
        if (at + (shapes * 8L) > file.Length)
        {
            return Failed("the file says it has more shapes than it holds");
        }

        var points = new (int Name, int Material)[shapes];
        for (var one = 0; one < shapes; one++)
        {
            points[one] = ((int)Read32(file, ref at), (int)Read32(file, ref at));
        }

        // The pieces, and then the records each of them owns - every header first, then every piece's records.
        if (at + (pieces * 6L) > file.Length)
        {
            return Failed("the file says it has more pieces than it holds");
        }

        int headers = at;
        long past = at;
        for (var one = 0; one < pieces; one++)
        {
            past += 2 + 4 + (file[at + (one * 6) + 1] * 12L);
        }

        past += records * (long)Trailing(version);
        if (past + 4 > file.Length)
        {
            return Failed("the file ends before its string pool");
        }

        int owned = headers + (pieces * 6);
        at = (int)past;
        int chars = (int)Read32(file, ref at);
        if (chars < 0 || at + (chars * 2L) > file.Length)
        {
            return Failed("the string pool says it is bigger than the file that holds it");
        }

        ReadOnlySpan<byte> pool = file.Slice(at, chars * 2);

        // THE SHAPES OF THE BLOCK AND THE NAMES OF THE HEADER ARE THE SAME SHAPES, and where the
        // two counts disagree the smaller one is all that can be paired. Clamped rather than
        // refused: a prop with one unnamed shape still draws.
        int both = Math.Min(shapes, block.Extents.Length);
        var named = new MeshShape[block.Extents.Length];
        var materials = new (string Shape, string Material)[both];
        for (var one = 0; one < block.Extents.Length; one++)
        {
            (int from, int count) = block.Extents[one];
            string name = one < both
                ? Word(pool, points[one].Name)
                : string.Empty;

            if (name.Length == 0)
            {
                name = $"shape {one.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            }

            from = Math.Clamp(from, 0, block.Indices.Length);
            named[one] = new MeshShape(name, from, Math.Clamp(count, 0, block.Indices.Length - from));

            if (one < both)
            {
                materials[one] = (name, Word(pool, points[one].Material));
            }
        }

        (FixedMeshPiece[] kept, byte[][] trailing) = Extras(file, headers, pieces, owned, records, version, pool);
        return new FixedMesh
        {
            Mesh = SkinnedMesh.Of(
                block.Positions, block.Normals, block.Indices, least, most,
                block.Coordinates, named, block.Facts, block.Colours),
            Named = materials,
            Pieces = kept,
            Records = trailing,
            Unread = file.Length - (at + (chars * 2)),
        };
    }

    /// <summary>
    /// The file's pieces - poe_data_tools' <c>subcomponents</c>: a byte, its 12-byte records and a tag out of the pool - in
    /// the file's order. Empty where it has none.
    /// </summary>
    /// <remarks>
    /// STEPPED OVER UNTIL v0.1.360, AND STILL UNNAMED: no reader of the format says what a piece or its records are. They
    /// are kept so a dump can set them beside the shapes; nothing draws by them.
    /// </remarks>
    public IReadOnlyList<FixedMeshPiece> Pieces { get; private init; } = [];

    /// <summary>The trailing records before the pool, raw - poe_data_tools' <c>d3s</c>, <see cref="Trailing"/> bytes each by version, unnamed.</summary>
    public IReadOnlyList<byte[]> Records { get; private init; } = [];

    /// <summary>
    /// How many bytes lie past the string pool - nought where the walk ended exactly at the file's end, which is where the
    /// reference demands it ends (its last step is a whole parse).
    /// </summary>
    public int Unread { get; private init; }

    /// <summary>
    /// The pieces and the trailing records, from where the pieces' headers lie (<paramref name="headers"/>, six bytes each)
    /// and where the first piece's records lie (<paramref name="owned"/>; every piece's follow in turn, then the trailing ones).
    /// The caller has bounded all of it against the file before the pool was read.
    /// </summary>
    private static (FixedMeshPiece[] Pieces, byte[][] Records) Extras(
        ReadOnlySpan<byte> file, int headers, int pieces, int owned, int records, int version, ReadOnlySpan<byte> pool)
    {
        var kept = new FixedMeshPiece[pieces];
        int at = owned;
        for (var one = 0; one < pieces; one++)
        {
            int header = headers + (one * 6);
            int count = file[header + 1];
            var own = new byte[count][];
            for (var record = 0; record < count; record++, at += 12)
            {
                own[record] = file.Slice(at, 12).ToArray();
            }

            kept[one] = new FixedMeshPiece(file[header], own, Word(pool, (int)BinaryPrimitives.ReadUInt32LittleEndian(file[(header + 2)..])));
        }

        int width = Trailing(version);
        var trailing = new byte[records][];
        for (var one = 0; one < records; one++, at += width)
        {
            trailing[one] = file.Slice(at, width).ToArray();
        }

        return (kept, trailing);
    }

    /// <summary>
    /// One string out of the pool: from an offset in characters, up to the zero that ends it.
    /// </summary>
    /// <remarks>
    /// AN OFFSET THAT LANDS NOWHERE COMES BACK EMPTY, which is the check this reader has. The
    /// offsets are written by the same file the walk stepped through, so one that points past the
    /// pool or into the middle of nothing means the walk arrived in the wrong place - and an empty
    /// name is a shape that falls back rather than a prop made of the wrong bytes.
    /// </remarks>
    private static string Word(ReadOnlySpan<byte> pool, int at)
    {
        if (at < 0 || (at * 2) >= pool.Length)
        {
            return string.Empty;
        }

        int from = at * 2;
        int to = from;
        while (to + 1 < pool.Length && BinaryPrimitives.ReadUInt16LittleEndian(pool[to..]) != 0)
        {
            to += 2;
        }

        return System.Text.Encoding.Unicode.GetString(pool[from..to]);
    }

    private static float Float(ReadOnlySpan<byte> file, int at)
        => BinaryPrimitives.ReadSingleLittleEndian(file[at..]);

    private static int Read16(ReadOnlySpan<byte> file, ref int at)
    {
        int said = BinaryPrimitives.ReadUInt16LittleEndian(file[at..]);
        at += 2;
        return said;
    }

    private static uint Read32(ReadOnlySpan<byte> file, ref int at)
    {
        uint said = BinaryPrimitives.ReadUInt32LittleEndian(file[at..]);
        at += 4;
        return said;
    }

    private static FixedMesh Failed(string why) => new() { Why = why };

    /// <summary>
    /// A file below version 9, whose geometry sits in a layout of its own rather than in a DOLm block.
    /// </summary>
    /// <remarks>
    /// MALGOR'S SPYGLASS IS ONE (v0.1.329): <c>PirateBossCannon.fmt</c> is version 8, and while this refused every such file he
    /// held his cannon in nothing (Port's capture of v0.1.328, monster-files.txt: "did not read: version 8 puts its geometry
    /// outside a DOLm block"). THE LAYOUT IS poe_data_tools' v8_section (crates/poe_data_tools-lib/src/file_parsers/fmt/parser.rs),
    /// read in its order:
    ///
    ///     triangles U32, vertices U32, then the header v9 has - shapes U16, pieces U8, ? U16, records U8 - and the box
    ///     the vertex format U32 (version 8 on; one means a second pair of coordinates per vertex)
    ///     per shape   name U32, material U32 (offsets into the pool), its first triangle U32
    ///     per piece   ? U8, its records U8, a tag U32
    ///     the indices, U16 under 65,536 vertices and U32 from there
    ///     per vertex  position 3 F32, 8 bytes, coordinates 2 F16, 2 more F16 where the format is one
    ///     every piece's records of 12 bytes, the trailing records, the string pool
    ///
    /// THE VERTEX'S EIGHT BYTES ARE UNNAMED in the reference. A DOLm vertex has its normal and tangent there, four signed bytes
    /// each, so the two are taken as candidates and held against the file's own triangles (<see cref="Facing"/>): the first whose
    /// every vector is unit length and lies along the surface's normal is the normal; where neither does, the normals are worked
    /// out from the triangles. Which it was is said in <see cref="NormalsFrom"/>.
    /// </remarks>
    private static FixedMesh Older(ReadOnlySpan<byte> file, int version)
    {
        var at = 1;
        if (at + 8 + 6 + 24 + (version >= 8 ? 4 : 0) > file.Length)
        {
            return Failed("the file ends inside its own header");
        }

        long triangles = Read32(file, ref at);
        long vertices = Read32(file, ref at);
        int shapes = Read16(file, ref at);
        int pieces = file[at++];
        at += 2;
        int records = file[at++];
        var least = new Vector3(Float(file, at), Float(file, at + 8), Float(file, at + 16));
        var most = new Vector3(Float(file, at + 4), Float(file, at + 12), Float(file, at + 20));
        at += 24;
        uint format = version >= 8 ? Read32(file, ref at) : 0u;

        int wide = vertices < 0x10000 ? 2 : 4;
        int stride = 12 + 8 + 4 + (format == 1 ? 4 : 0);
        long needed = at + (shapes * 12L) + (pieces * 6L) + (triangles * 3 * wide) + (vertices * stride);
        if (triangles <= 0 || vertices <= 0 || needed > file.Length)
        {
            return Failed($"the file says it has {triangles} triangles over {vertices} vertices, more than it holds");
        }

        var points = new (int Name, int Material, long First)[shapes];
        for (var one = 0; one < shapes; one++)
        {
            points[one] = ((int)Read32(file, ref at), (int)Read32(file, ref at), Read32(file, ref at));
        }

        // THE PIECES' HEADERS, and how many 12-byte records each owns - those come after the vertices.
        int headers = at;
        long owned = 0;
        for (var one = 0; one < pieces; one++)
        {
            owned += file[at + 1];
            at += 6;
        }

        var indices = new int[(int)(triangles * 3)];
        for (var one = 0; one < indices.Length; one++, at += wide)
        {
            indices[one] = wide == 2 ? BinaryPrimitives.ReadUInt16LittleEndian(file[at..]) : (int)BinaryPrimitives.ReadUInt32LittleEndian(file[at..]);
            if (indices[one] >= vertices)
            {
                return Failed($"index {indices[one]} reaches past the file's {vertices} vertices");
            }
        }

        var positions = new Vector3[vertices];
        var coordinates = new Vector2[vertices];
        var first = new Vector3[vertices];
        var second = new Vector3[vertices];
        for (var one = 0; one < vertices; one++, at += stride)
        {
            positions[one] = new Vector3(Float(file, at), Float(file, at + 4), Float(file, at + 8));
            first[one] = Signed(file, at + 12);
            second[one] = Signed(file, at + 16);
            coordinates[one] = new Vector2(
                (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(file[(at + 20)..])),
                (float)BitConverter.UInt16BitsToHalf(BinaryPrimitives.ReadUInt16LittleEndian(file[(at + 22)..])));
        }

        long past = at + (owned * 12) + (records * (long)Trailing(version));
        if (past + 4 > file.Length)
        {
            return Failed("the file ends before its string pool");
        }

        int ownedAt = at;
        at = (int)past;
        int chars = (int)Read32(file, ref at);
        if (chars < 0 || at + (chars * 2L) > file.Length)
        {
            return Failed("the string pool says it is bigger than the file that holds it");
        }

        ReadOnlySpan<byte> pool = file.Slice(at, chars * 2);

        // EACH SHAPE RUNS FROM ITS FIRST TRIANGLE TO THE NEXT SHAPE'S, the last to the end - in the order of their starts.
        int[] order = [.. Enumerable.Range(0, shapes).OrderBy(one => points[one].First)];
        var named = new MeshShape[shapes];
        var materials = new (string Shape, string Material)[shapes];
        for (var rank = 0; rank < order.Length; rank++)
        {
            int one = order[rank];
            long from = Math.Clamp(points[one].First, 0, triangles);
            long to = rank + 1 < order.Length ? Math.Clamp(points[order[rank + 1]].First, from, triangles) : triangles;
            string name = Word(pool, points[one].Name);
            if (name.Length == 0)
            {
                name = $"shape {one.ToString(System.Globalization.CultureInfo.InvariantCulture)}";
            }

            named[one] = new MeshShape(name, (int)(from * 3), (int)((to - from) * 3));
            materials[one] = (name, Word(pool, points[one].Material));
        }

        (Vector3[] normals, string source) = Facing(positions, indices, first, second);
        var facts = new MeshFacts(version, 0, 1, format, stride, shapes, shapes, (int)triangles, (int)vertices, 0, 0, "normals from " + source);
        (FixedMeshPiece[] kept, byte[][] trailing) = Extras(file, headers, pieces, ownedAt, records, version, pool);
        return new FixedMesh
        {
            Mesh = SkinnedMesh.Of(positions, normals, indices, least, most, coordinates, named, facts),
            Named = materials,
            NormalsFrom = source,
            Pieces = kept,
            Records = trailing,
            Unread = file.Length - (at + (chars * 2)),
        };
    }

    /// <summary>Where an older file's normals came from - its vertices' first four bytes, their second, or its triangles. Empty for a version 9 file, whose DOLm block names them.</summary>
    public string NormalsFrom { get; private init; } = string.Empty;

    /// <summary>How far a candidate's vector may be from unit length, and how many of the vertices must lie along the surface.</summary>
    private const float UnitSlack = 0.15f;

    /// <inheritdoc cref="UnitSlack"/>
    private const float Along = 0.9f;

    /// <summary>
    /// The normals of an older file: the first candidate whose every vector is unit length and lies along the surface the triangles
    /// make - nine in ten within 45 degrees either way round, the file's own facing kept - or the surface's own where neither does.
    /// </summary>
    private static (Vector3[] Normals, string From) Facing(Vector3[] positions, int[] indices, Vector3[] first, Vector3[] second)
    {
        // THE SURFACE'S OWN, each vertex the area-weighted sum of its triangles' normals.
        var surface = new Vector3[positions.Length];
        for (var one = 0; one + 2 < indices.Length; one += 3)
        {
            int a = indices[one], b = indices[one + 1], c = indices[one + 2];
            Vector3 face = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            surface[a] += face;
            surface[b] += face;
            surface[c] += face;
        }

        foreach ((Vector3[] candidate, string name) in new[] { (first, "the vertices' first four bytes"), (second, "the vertices' second four bytes") })
        {
            int asked = 0, along = 0;
            bool unit = true;
            for (var one = 0; one < candidate.Length && unit; one++)
            {
                float length = candidate[one].Length();
                unit = MathF.Abs(length - 1f) <= UnitSlack;
                if (surface[one].LengthSquared() > 1e-12f)
                {
                    asked++;
                    along += MathF.Abs(Vector3.Dot(Vector3.Normalize(surface[one]), candidate[one] / length)) >= MathF.Sqrt(0.5f) ? 1 : 0;
                }
            }

            if (unit && asked > 0 && along >= asked * Along)
            {
                var normals = new Vector3[candidate.Length];
                for (var one = 0; one < normals.Length; one++)
                {
                    normals[one] = Vector3.Normalize(candidate[one]);
                }

                return (normals, name);
            }
        }

        // NEITHER LIES ALONG THE SURFACE: the triangles' own, turned to face away from the middle where most of them face in.
        Vector3 middle = Vector3.Zero;
        foreach (Vector3 at in positions)
        {
            middle += at / positions.Length;
        }

        int outward = 0;
        for (var one = 0; one < positions.Length; one++)
        {
            outward += Vector3.Dot(surface[one], positions[one] - middle) >= 0f ? 1 : -1;
        }

        float sign = outward >= 0 ? 1f : -1f;
        var worked = new Vector3[positions.Length];
        for (var one = 0; one < worked.Length; one++)
        {
            worked[one] = surface[one].LengthSquared() > 1e-12f ? Vector3.Normalize(surface[one]) * sign : Vector3.UnitZ;
        }

        return (worked, "the triangles, neither four bytes lying along them");
    }

    /// <summary>Three signed bytes as a vector, each over 127 - as a DOLm vertex's normal is read, not yet made unit length.</summary>
    private static Vector3 Signed(ReadOnlySpan<byte> file, int at)
        => new((sbyte)file[at] / 127f, (sbyte)file[at + 1] / 127f, (sbyte)file[at + 2] / 127f);
}
