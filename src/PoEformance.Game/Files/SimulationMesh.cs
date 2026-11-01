using System.Buffers.Binary;
using System.Numerics;
using System.Text;

namespace PoEformance.Game.Files;

/// <summary>One vertex of a cloth's simulation mesh: where it rests in the model, and the four bones and weights that carry its target.</summary>
/// <param name="Position">Its rest, in the mesh's own frame - the bind pose, as the geometry's positions are.</param>
/// <param name="Bones">Four bone numbers of the mesh's rig (poe_data_tools' <c>unk1</c>).</param>
/// <param name="Weights">Their four weights, floats (poe_data_tools' <c>unk2</c>).</param>
public readonly record struct SimulationVertex(Vector3 Position, Int4 Bones, Vector4 Weights);

/// <summary>Four ints - a simulation vertex's bone numbers.</summary>
public readonly record struct Int4(int X, int Y, int Z, int W)
{
    /// <summary>The one at <paramref name="index"/>, nought to three.</summary>
    public int this[int index] => index switch { 0 => X, 1 => Y, 2 => Z, _ => W };
}

/// <summary>A collision sphere of the mesh, its name where the file writes one.</summary>
public readonly record struct SimulationSphere(Vector3 Centre, float Radius, uint Unknown, string Name);

/// <summary>A collision ellipsoid of the mesh: its fifteen floats as written (poe_data_tools reads them unnamed), its name where written.</summary>
public readonly record struct SimulationEllipsoid(float[] Floats, uint Unknown, string Name);

/// <summary>
/// What a <c>.smd</c> carries past its geometry and shape names: a cloth's simulation mesh and the body's collision shapes - the tail
/// poe_data_tools reads (<c>file_parsers/smd</c>, versions 2, 3 and 4 of it).
/// </summary>
/// <remarks>
/// WHY IT IS READ (v0.1.420, chains, cloth and hair decided from the live client). The engine's cloth is not a chain of bones: its
/// ClientAnimationController setup (module+0x2863C00 in the build of 2026-10-27) takes the SkinMesh component's simulation meshes - a
/// vertex of 44 bytes, a position and four bones and weights, exactly this tail's <c>skinned_vertices</c> - makes a particle of every
/// vertex, its target the position skinned by those bones (each bone's offset taken from its bind), builds its constraints from the
/// triangles of <c>sv_refs1</c>, and sets every vertex <c>sv_refs2</c> names kinematic - it follows the animation and holds the rest. The
/// spheres, their connections (capsules between two spheres) and the ellipsoids are the collision shapes the same setup
/// (module+0x2862510) hands the physics world. Named here by what the engine does with them; poe_data_tools calls them unk1, unk2,
/// sv_refs1 and sv_refs2.
/// </remarks>
/// <param name="TailVersion">The tail's own version word: 2, 3 or 4, nought where the file carries none.</param>
/// <param name="Vertices">The simulation mesh's vertices.</param>
/// <param name="Triangles">Its triangles, three vertex numbers each (<c>sv_refs1</c>).</param>
/// <param name="Pinned">The vertices the engine sets kinematic - they follow the animation (<c>sv_refs2</c>).</param>
/// <param name="Spheres">The collision spheres.</param>
/// <param name="Connections">The capsules, two sphere numbers each.</param>
/// <param name="Ellipsoids">The collision ellipsoids.</param>
/// <param name="Unnamed">How many of the empty records poe_data_tools calls <c>t3s</c> the tail counts.</param>
/// <param name="Why">Why nothing was read, or empty.</param>
public sealed record SimulationMesh(
    int TailVersion,
    IReadOnlyList<SimulationVertex> Vertices,
    IReadOnlyList<int> Triangles,
    IReadOnlyList<int> Pinned,
    IReadOnlyList<SimulationSphere> Spheres,
    IReadOnlyList<(int From, int To)> Connections,
    IReadOnlyList<SimulationEllipsoid> Ellipsoids,
    int Unnamed,
    string Why)
{
    /// <summary>Nothing read.</summary>
    public static SimulationMesh None { get; } = new(0, [], [], [], [], [], [], 0, string.Empty);

    /// <summary>Whether it carries a cloth to simulate.</summary>
    public bool Simulates => Vertices.Count > 0;

    /// <summary>The most records of one kind believed - a count past it is a misread, not a cloth.</summary>
    private const int MostRecords = 1 << 20;

    /// <summary>
    /// Reads the tail starting at <paramref name="at"/>: its version word, then the counts and the records in the order that version
    /// writes them. <paramref name="fileVersion"/> is the <c>.smd</c>'s own, which gates the names of spheres and ellipsoids (from 3).
    /// </summary>
    public static SimulationMesh Read(ReadOnlySpan<byte> file, int at, int fileVersion)
    {
        try
        {
            return Parse(file, at, fileVersion);
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or IndexOutOfRangeException or OverflowException
            or DecoderFallbackException or InvalidDataException)
        {
            return None with { Why = $"the tail does not read: {exception.Message}" };
        }
    }

    private static SimulationMesh Parse(ReadOnlySpan<byte> file, int at, int fileVersion)
    {
        if (at < 0 || at + 4 > file.Length)
        {
            return None with { Why = at == file.Length ? string.Empty : "no tail after the shape names" };
        }

        int version = (int)U32(file, ref at);
        int ellipsoids, spheres = 0, connections = 0, unnamed = 0, vertices, pins, pinned;
        if (version == 2)
        {
            ellipsoids = Count(file, ref at);
            vertices = Count(file, ref at);
            pins = Count(file, ref at);
            pinned = Count(file, ref at);
        }
        else if (version is 3 or 4)
        {
            ellipsoids = Count(file, ref at);
            spheres = Count(file, ref at);
            connections = Count(file, ref at);
            unnamed = Count(file, ref at);
            vertices = Count(file, ref at);
            pins = Count(file, ref at);
            pinned = Count(file, ref at);
        }
        else
        {
            return None with { TailVersion = version, Why = $"a tail of version {version}, which no reader knows" };
        }

        var vertexList = new List<SimulationVertex>();
        var triangles = new List<int>();
        var pinnedList = new List<int>();
        var sphereList = new List<SimulationSphere>();
        var connectionList = new List<(int, int)>();
        var ellipsoidList = new List<SimulationEllipsoid>();

        // EACH VERSION ITS OWN ORDER, as poe_data_tools' v2_tail, v3_tail and v4_tail read it; the t3s are empty records.
        switch (version)
        {
            case 2:
                ReadVertices(file, ref at, vertices, vertexList);
                Ints(file, ref at, pins, triangles);
                ReadEllipsoids(file, ref at, ellipsoids, fileVersion, ellipsoidList);
                Ints(file, ref at, pinned, pinnedList);
                break;
            case 3:
                ReadEllipsoids(file, ref at, ellipsoids, fileVersion, ellipsoidList);
                ReadSpheres(file, ref at, spheres, fileVersion, sphereList);
                ReadConnections(file, ref at, connections, connectionList);
                ReadVertices(file, ref at, vertices, vertexList);
                Ints(file, ref at, pins, triangles);
                Ints(file, ref at, pinned, pinnedList);
                break;
            default:
                ReadVertices(file, ref at, vertices, vertexList);
                Ints(file, ref at, pins, triangles);
                ReadEllipsoids(file, ref at, ellipsoids, fileVersion, ellipsoidList);
                ReadSpheres(file, ref at, spheres, fileVersion, sphereList);
                ReadConnections(file, ref at, connections, connectionList);
                Ints(file, ref at, pinned, pinnedList);
                break;
        }

        string why = at == file.Length ? string.Empty : $"{file.Length - at} bytes past the tail unread";
        return new SimulationMesh(version, vertexList, triangles, pinnedList, sphereList, connectionList, ellipsoidList, unnamed, why);
    }

    private static int Count(ReadOnlySpan<byte> file, ref int at)
    {
        uint count = U32(file, ref at);
        return count > MostRecords ? throw new InvalidDataException($"a count of {count}") : (int)count;
    }

    private static void ReadVertices(ReadOnlySpan<byte> file, ref int at, int count, List<SimulationVertex> into)
    {
        for (var one = 0; one < count; one++)
        {
            var position = new Vector3(F32(file, ref at), F32(file, ref at), F32(file, ref at));
            var bones = new Int4((int)U32(file, ref at), (int)U32(file, ref at), (int)U32(file, ref at), (int)U32(file, ref at));
            var weights = new Vector4(F32(file, ref at), F32(file, ref at), F32(file, ref at), F32(file, ref at));
            into.Add(new SimulationVertex(position, bones, weights));
        }
    }

    private static void Ints(ReadOnlySpan<byte> file, ref int at, int count, List<int> into)
    {
        for (var one = 0; one < count; one++)
        {
            into.Add((int)U32(file, ref at));
        }
    }

    private static void ReadEllipsoids(ReadOnlySpan<byte> file, ref int at, int count, int fileVersion, List<SimulationEllipsoid> into)
    {
        for (var one = 0; one < count; one++)
        {
            var floats = new float[15];
            for (var each = 0; each < floats.Length; each++)
            {
                floats[each] = F32(file, ref at);
            }

            uint unknown = U32(file, ref at);
            into.Add(new SimulationEllipsoid(floats, unknown, fileVersion >= 3 ? Name(file, ref at) : string.Empty));
        }
    }

    private static void ReadSpheres(ReadOnlySpan<byte> file, ref int at, int count, int fileVersion, List<SimulationSphere> into)
    {
        for (var one = 0; one < count; one++)
        {
            var centre = new Vector3(F32(file, ref at), F32(file, ref at), F32(file, ref at));
            float radius = F32(file, ref at);
            uint unknown = U32(file, ref at);
            into.Add(new SimulationSphere(centre, radius, unknown, fileVersion >= 3 ? Name(file, ref at) : string.Empty));
        }
    }

    private static void ReadConnections(ReadOnlySpan<byte> file, ref int at, int count, List<(int, int)> into)
    {
        for (var one = 0; one < count; one++)
        {
            into.Add(((int)U32(file, ref at), (int)U32(file, ref at)));
        }
    }

    /// <summary>A name: its length in bytes, then that many bytes of UTF-16.</summary>
    private static string Name(ReadOnlySpan<byte> file, ref int at)
    {
        var length = (int)U32(file, ref at);
        if (length < 0 || at + length > file.Length)
        {
            throw new InvalidDataException($"a name of {length} bytes");
        }

        string name = Encoding.Unicode.GetString(file.Slice(at, length));
        at += length;
        return name;
    }

    private static uint U32(ReadOnlySpan<byte> file, ref int at)
    {
        uint value = BinaryPrimitives.ReadUInt32LittleEndian(file[at..(at + 4)]);
        at += 4;
        return value;
    }

    private static float F32(ReadOnlySpan<byte> file, ref int at)
    {
        float value = BinaryPrimitives.ReadSingleLittleEndian(file[at..(at + 4)]);
        at += 4;
        return value;
    }
}
