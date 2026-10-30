using System.Buffers.Binary;

namespace PoEformance.Game.Files;

/// <summary>One connection of a passive: the passive it leads to, and the curve of the line drawn to it.</summary>
public readonly record struct PassiveGraphLink(uint Passive, int Curvature);

/// <summary>One passive of a graph group: its id, its orbit and place on it, and where it leads.</summary>
/// <param name="Id">The passive's id.</param>
/// <param name="Orbit">Which orbit of its group it sits on.</param>
/// <param name="OrbitPosition">Where on the orbit, clockwise.</param>
/// <param name="Links">Its connections.</param>
public sealed record PassiveGraphNode(uint Id, int Orbit, uint OrbitPosition, IReadOnlyList<PassiveGraphLink> Links);

/// <summary>One group of a passive graph: its place, its flags, two unnamed numbers and its passives.</summary>
/// <param name="X">Its place across.</param>
/// <param name="Y">Its place down.</param>
/// <param name="Flags">Its flags, unnamed.</param>
/// <param name="Unk1">An unnamed number - the reference lists values it has seen per tree kind and names none.</param>
/// <param name="Unk2">An unnamed byte - 1 a cluster jewel's group, by the reference.</param>
/// <param name="Passives">The group's passives.</param>
public sealed record PassiveGraphGroup(float X, float Y, uint Flags, uint Unk1, byte Unk2, IReadOnlyList<PassiveGraphNode> Passives);

/// <summary>
/// A passive skill graph (<c>.psg</c>): the passive tree's or the atlas tree's layout - groups of passives on
/// orbits and the connections between them.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>psg</c> grammar (<c>file_parsers/psg/parser.rs</c>), its PoE2 branch - the tool reads no other
/// game. Little-endian throughout:
/// <code>
///     u8 version, u8 graph type (1 the passive tree, 2 the atlas tree)
///     u8 count, that many u8      passives per orbit
///     u32 count, that many u64    root passives (u32 in PoE1)
///     u32 count of groups, per group
///         f32 x, f32 y, u32 flags, u32 unk1, u8 unk2
///         u32 count of passives, per passive
///             u32 id, i32 orbit, u32 orbit position
///             u32 count of connections, per connection u32 passive, i32 curvature (PoE2 only)
/// </code>
/// and nothing after. Every count is held against the bytes left before anything is made for it, so a wrong
/// layout costs a <see cref="Why"/>, not a gigabyte.
/// </remarks>
/// <param name="Version">The first byte.</param>
/// <param name="GraphType">The second.</param>
/// <param name="PassivesPerOrbit">How many passives each orbit holds.</param>
/// <param name="Roots">The root passives.</param>
/// <param name="Groups">The groups, as many as counted or as read before a stop.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record PassiveGraph(byte Version, byte GraphType, IReadOnlyList<byte> PassivesPerOrbit, IReadOnlyList<ulong> Roots, IReadOnlyList<PassiveGraphGroup> Groups, string Why)
{
    /// <summary>What a passive graph's file name ends with.</summary>
    public const string Extension = ".psg";

    // The least bytes each counted thing takes, for holding a count against what is left.
    private const int GroupBytes = 4 + 4 + 4 + 4 + 1 + 4;
    private const int PassiveBytes = 4 + 4 + 4 + 4;
    private const int LinkBytes = 4 + 4;

    /// <summary>Nothing read.</summary>
    public static PassiveGraph None { get; } = new(0, 0, [], [], [], "nothing to read");

    /// <summary>Reads a file's bytes. Never throws.</summary>
    public static PassiveGraph Read(ReadOnlySpan<byte> file)
    {
        if (file.Length < 2)
        {
            return None;
        }

        byte version = file[0];
        byte type = file[1];
        int at = 2;

        int orbits = file[at++];
        if (file.Length - at < orbits)
        {
            return new PassiveGraph(version, type, [], [], [], "the orbits' counts run past the end");
        }

        byte[] perOrbit = file.Slice(at, orbits).ToArray();
        at += orbits;

        if (!Count(file, ref at, 8, out int rootCount))
        {
            return new PassiveGraph(version, type, perOrbit, [], [], "the root passives run past the end");
        }

        var roots = new ulong[rootCount];
        for (int one = 0; one < rootCount; one++, at += 8)
        {
            roots[one] = BinaryPrimitives.ReadUInt64LittleEndian(file[at..]);
        }

        if (!Count(file, ref at, GroupBytes, out int groupCount))
        {
            return new PassiveGraph(version, type, perOrbit, roots, [], "the groups run past the end");
        }

        var groups = new List<PassiveGraphGroup>(groupCount);
        for (int one = 0; one < groupCount; one++)
        {
            if (file.Length - at < GroupBytes)
            {
                return new PassiveGraph(version, type, perOrbit, roots, groups, $"group {one} runs past the end");
            }

            float x = BinaryPrimitives.ReadSingleLittleEndian(file[at..]);
            float y = BinaryPrimitives.ReadSingleLittleEndian(file[(at + 4)..]);
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 8)..]);
            uint unk1 = BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 12)..]);
            byte unk2 = file[at + 16];
            at += 17;

            if (!Count(file, ref at, PassiveBytes, out int passiveCount))
            {
                return new PassiveGraph(version, type, perOrbit, roots, groups, $"group {one}'s passives run past the end");
            }

            var passives = new PassiveGraphNode[passiveCount];
            for (int passive = 0; passive < passiveCount; passive++)
            {
                if (file.Length - at < PassiveBytes)
                {
                    return new PassiveGraph(version, type, perOrbit, roots, groups, $"group {one}'s passive {passive} runs past the end");
                }

                uint id = BinaryPrimitives.ReadUInt32LittleEndian(file[at..]);
                int orbit = BinaryPrimitives.ReadInt32LittleEndian(file[(at + 4)..]);
                uint position = BinaryPrimitives.ReadUInt32LittleEndian(file[(at + 8)..]);
                at += 12;

                if (!Count(file, ref at, LinkBytes, out int linkCount))
                {
                    return new PassiveGraph(version, type, perOrbit, roots, groups, $"passive {id}'s connections run past the end");
                }

                var links = new PassiveGraphLink[linkCount];
                for (int link = 0; link < linkCount; link++, at += LinkBytes)
                {
                    links[link] = new PassiveGraphLink(
                        BinaryPrimitives.ReadUInt32LittleEndian(file[at..]),
                        BinaryPrimitives.ReadInt32LittleEndian(file[(at + 4)..]));
                }

                passives[passive] = new PassiveGraphNode(id, orbit, position, links);
            }

            groups.Add(new PassiveGraphGroup(x, y, flags, unk1, unk2, passives));
        }

        return new PassiveGraph(version, type, perOrbit, roots, groups, at < file.Length ? $"{file.Length - at} bytes past the last group" : string.Empty);
    }

    /// <summary>A u32 count, stepping past it, where that many things of at least <paramref name="each"/> bytes fit in what is left.</summary>
    private static bool Count(ReadOnlySpan<byte> file, ref int at, int each, out int count)
    {
        count = 0;
        if (file.Length - at < 4)
        {
            return false;
        }

        uint read = BinaryPrimitives.ReadUInt32LittleEndian(file[at..]);
        if (read > (uint)((file.Length - at - 4) / each))
        {
            return false;
        }

        count = (int)read;
        at += 4;
        return true;
    }
}
