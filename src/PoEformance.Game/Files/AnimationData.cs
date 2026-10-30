using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>One stage of an animation data group: a name, a time and three floats, unnamed.</summary>
public sealed record AnimationDataStage(string Name, uint Time, Vector3 Floats);

/// <summary>One bone rotation of a group: the bone, the order its coordinates are written in, and the coordinates.</summary>
/// <param name="Bone">The quoted bone name.</param>
/// <param name="Order">The unquoted coordinate order (<c>xyz</c> and the like); its length times the group's count is how many follow.</param>
/// <param name="Coordinates">The coordinates as written.</param>
public sealed record AnimationDataRotation(string Bone, string Order, IReadOnlyList<float> Coordinates);

/// <summary>One group of an animation data file.</summary>
/// <param name="Name">The quoted name - an animation's, by the reference's reading.</param>
/// <param name="Type">The unquoted word after it (<c>animation_type</c>).</param>
/// <param name="Time">The whole number after that (<c>animation_time</c>).</param>
/// <param name="Stages">The stages the next line counts.</param>
/// <param name="Floats">The counted floats: none before version 2, optional in 2, always from 3.</param>
/// <param name="Rotations">The bone rotations, from version 4; null before.</param>
/// <param name="ExtraInts">The two lines of -1 or a whole number a group may end with (null for -1), or null where it does not.</param>
public sealed record AnimationDataGroup(
    string Name,
    string Type,
    uint Time,
    IReadOnlyList<AnimationDataStage> Stages,
    IReadOnlyList<float>? Floats,
    IReadOnlyList<AnimationDataRotation>? Rotations,
    (uint? First, uint? Second)? ExtraInts);

/// <summary>A named list of bones, from version 5.</summary>
public sealed record AnimationDataBones(string Name, IReadOnlyList<string> Bones);

/// <summary>
/// An animation data file (<c>.amd</c>): per animation its stages, floats, bone rotations and bone groups.
/// </summary>
/// <remarks>
/// poe_data_tools' <c>amd</c> grammar (<c>file_parsers/amd/parser.rs</c>). ITS LINES ARE CUT AT TABS TOO
/// (<c>split_inclusive(['\n', '\t'])</c>, "some files have missing newlines"), trimmed, the blank and <c>//</c>
/// ones dropped, so a stage written <c>"name"&lt;tab&gt;100&lt;tab&gt;0 0 0</c> on one line is its three lines:
/// <code>
///     version N
///     G                                   how many groups
///     per group   "name"                  each its own line
///                 type
///                 time
///                 S                       how many stages, then per stage
///                     "name"
///                     time
///                     f f f
///                 [N f ...]               a count and that many floats: none in version 1, optional in 2, from 3
///                 R [C]                   from version 4: R rotations of C coordinates per letter, then per one
///                     "bone" order f ...  C x len(order) floats
///                 [n]                     optionally two lines of -1 or a whole number
///                 [n]
///     BoneGroups B                        from version 5, then per bone group
///         "name" N "bone" ...
/// </code>
/// The reference names what it can (<c>animation_type</c>, <c>animation_time</c>, <c>animation_stages</c>) and
/// leaves the floats and the trailing whole numbers unnamed; nothing here reads them for their meaning.
/// </remarks>
/// <param name="Version">Off the version line.</param>
/// <param name="Groups">The groups, as many as the count says or as many as read before a stop.</param>
/// <param name="BoneGroups">The bone groups, from version 5; null before.</param>
/// <param name="Why">Empty where the whole file read; else where and why it stopped.</param>
public sealed record AnimationData(int Version, IReadOnlyList<AnimationDataGroup> Groups, IReadOnlyList<AnimationDataBones>? BoneGroups, string Why)
{
    /// <summary>What an animation data file's name ends with.</summary>
    public const string Extension = ".amd";

    /// <summary>Nothing read.</summary>
    public static AnimationData None { get; } = new(0, [], null, "nothing to read");

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static AnimationData Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static AnimationData Parse(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return None;
        }

        FileLines lines = FileLines.Of(text, "//", tabs: true);
        int at = 0;
        if (lines.Version(ref at) is not int version)
        {
            return None with { Why = $"{lines.Said(0)} is not a version line" };
        }

        var groups = new List<AnimationDataGroup>();
        if (!Count(lines, ref at, out uint count))
        {
            return new AnimationData(version, groups, null, $"{lines.Said(at)} is not the count of groups");
        }

        for (uint one = 0; one < count; one++)
        {
            string? why = Group(lines, ref at, version, out AnimationDataGroup? group);
            if (why is not null)
            {
                return new AnimationData(version, groups, null, $"group {one}: {why}");
            }

            groups.Add(group!);
        }

        List<AnimationDataBones>? bones = null;
        if (version >= 5)
        {
            var header = new LineWords(at < lines.Count ? lines[at] : default);
            if (!header.Literal("BoneGroups") || !header.Space() || !header.UInt(out uint boneGroups) || !header.End)
            {
                return new AnimationData(version, groups, null, $"{lines.Said(at)} is not BoneGroups and a count");
            }

            at++;
            bones = new List<AnimationDataBones>((int)Math.Min(boneGroups, (uint)(lines.Count - at)));
            for (uint one = 0; one < boneGroups; one++, at++)
            {
                if (at >= lines.Count || !BoneGroup(lines[at], out AnimationDataBones? bone))
                {
                    return new AnimationData(version, groups, bones, $"{lines.Said(at)} is not a bone group");
                }

                bones.Add(bone!);
            }
        }

        return new AnimationData(version, groups, bones, at < lines.Count ? $"{lines.Said(at)} is past what the file counts" : string.Empty);
    }

    private static string? Group(FileLines lines, ref int at, int version, out AnimationDataGroup? group)
    {
        group = null;
        if (!Whole(lines, at, out string name, quoted: true))
        {
            return $"{lines.Said(at)} is not a quoted name";
        }

        if (!Whole(lines, at + 1, out string type, quoted: false))
        {
            return $"{lines.Said(at + 1)} is not a type";
        }

        at += 2;
        if (!Count(lines, ref at, out uint time) || !Count(lines, ref at, out uint stageCount))
        {
            return $"{lines.Said(at)} is not a time and a count of stages";
        }

        var stages = new List<AnimationDataStage>((int)Math.Min(stageCount, (uint)(lines.Count - at) / 3));
        for (uint one = 0; one < stageCount; one++)
        {
            if (!Whole(lines, at, out string stage, quoted: true))
            {
                return $"{lines.Said(at)} is not a stage's quoted name";
            }

            at++;
            if (!Count(lines, ref at, out uint stageTime))
            {
                return $"{lines.Said(at)} is not a stage's time";
            }

            var floats = new LineWords(at < lines.Count ? lines[at] : default);
            if (!floats.Float(out float x) || !floats.Space() || !floats.Float(out float y) || !floats.Space() || !floats.Float(out float z) || !floats.End)
            {
                return $"{lines.Said(at)} is not a stage's three floats";
            }

            at++;
            stages.Add(new AnimationDataStage(stage, stageTime, new Vector3(x, y, z)));
        }

        // THE FLOATS BY VERSION, as the reference dispatches: none in 1, optional in 2, required from 3 (0 fails).
        List<float>? counted = null;
        if (version == 0)
        {
            return "version 0 has no reading";
        }

        if (version >= 2)
        {
            if (at < lines.Count && Floats(lines[at], out counted))
            {
                at++;
            }
            else if (version >= 3)
            {
                return $"{lines.Said(at)} is not a count and that many floats";
            }
        }

        List<AnimationDataRotation>? rotations = null;
        if (version >= 4)
        {
            var header = new LineWords(at < lines.Count ? lines[at] : default);
            if (!header.UInt(out uint rotationCount))
            {
                return $"{lines.Said(at)} is not a count of rotations";
            }

            uint coordinates = 0;
            LineWords mark = header;
            if (!header.Space() || !header.UInt(out coordinates))
            {
                header = mark;
                coordinates = 0;
            }

            if (!header.End)
            {
                return $"{lines.Said(at)} is not a count of rotations and of coordinates";
            }

            at++;
            rotations = new List<AnimationDataRotation>((int)Math.Min(rotationCount, (uint)(lines.Count - at)));
            for (uint one = 0; one < rotationCount; one++, at++)
            {
                if (at >= lines.Count || !Rotation(lines[at], coordinates, out AnimationDataRotation? rotation))
                {
                    return $"{lines.Said(at)} is not a bone rotation of {coordinates} coordinates a letter";
                }

                rotations.Add(rotation!);
            }
        }

        (uint?, uint?)? extra = null;
        if (at + 1 < lines.Count && Nullable(lines[at], out uint? first) && Nullable(lines[at + 1], out uint? second))
        {
            extra = (first, second);
            at += 2;
        }

        group = new AnimationDataGroup(name, type, time, stages, counted, rotations, extra);
        return null;
    }

    private static bool Whole(FileLines lines, int at, out string text, bool quoted)
    {
        text = string.Empty;
        if (at >= lines.Count)
        {
            return false;
        }

        var words = new LineWords(lines[at]);
        return (quoted ? words.Quoted(out text) : words.Word(out text)) && words.End;
    }

    /// <summary>A line of one whole number, stepping past it.</summary>
    private static bool Count(FileLines lines, ref int at, out uint count)
    {
        count = 0;
        if (at >= lines.Count)
        {
            return false;
        }

        var words = new LineWords(lines[at]);
        if (!words.UInt(out count) || !words.End)
        {
            return false;
        }

        at++;
        return true;
    }

    private static bool Floats(ReadOnlySpan<char> line, out List<float>? floats)
    {
        floats = null;
        var words = new LineWords(line);
        if (!words.UInt(out uint count))
        {
            return false;
        }

        var read = new List<float>((int)Math.Min(count, (uint)line.Length));
        for (uint one = 0; one < count; one++)
        {
            if (!words.Space() || !words.Float(out float value))
            {
                return false;
            }

            read.Add(value);
        }

        if (!words.End)
        {
            return false;
        }

        floats = read;
        return true;
    }

    private static bool Rotation(ReadOnlySpan<char> line, uint coordinates, out AnimationDataRotation? rotation)
    {
        rotation = null;
        var words = new LineWords(line);
        if (!words.Quoted(out string bone) || !words.Space() || !words.Word(out string order))
        {
            return false;
        }

        long count = (long)coordinates * order.Length;
        if (count > line.Length)
        {
            return false;
        }

        var values = new float[count];
        for (int one = 0; one < count; one++)
        {
            if (!words.Space() || !words.Float(out values[one]))
            {
                return false;
            }
        }

        if (!words.End)
        {
            return false;
        }

        rotation = new AnimationDataRotation(bone, order, values);
        return true;
    }

    private static bool Nullable(ReadOnlySpan<char> line, out uint? value)
    {
        var words = new LineWords(line);
        return words.NullableUInt(out value) && words.End;
    }

    private static bool BoneGroup(ReadOnlySpan<char> line, out AnimationDataBones? group)
    {
        group = null;
        var words = new LineWords(line);
        if (!words.Quoted(out string name) || !words.Space() || !words.UInt(out uint count) || count > line.Length)
        {
            return false;
        }

        var bones = new List<string>((int)count);
        for (uint one = 0; one < count; one++)
        {
            if (!words.Space() || !words.Quoted(out string bone))
            {
                return false;
            }

            bones.Add(bone);
        }

        if (!words.End)
        {
            return false;
        }

        group = new AnimationDataBones(name, bones);
        return true;
    }
}
