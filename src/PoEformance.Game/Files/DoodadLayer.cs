using System.Globalization;

namespace PoEformance.Game.Files;

/// <summary>One mesh a doodad layer scatters: the <c>.fmt</c>, the number after it and the points after that.</summary>
/// <param name="Mesh">The <c>.fmt</c> it scatters.</param>
/// <param name="Number">The number after it - unnamed in every reference.</param>
/// <param name="Points">The pairs after the number, each written <c>(a,b)</c> or <c>[a b]</c> - values between nought and one, unnamed.</param>
public sealed record DoodadLayerEntry(string Mesh, float Number, IReadOnlyList<(float A, float B)> Points);

/// <summary>
/// A doodad layer (<c>.dlp</c>): the meshes a ground material scatters over the ground it paints - the grass, ferns, fallen leaves and
/// strewn weapons a MaterialsList names after a material (GroundChoice.Layers).
/// </summary>
/// <remarks>
/// THE GRAMMAR IS poe_data_tools' <c>dlp</c> PARSER, to the condition: UTF-16 text, an optional <c>version N</c> line, then the header -
/// below version 3 one line (the least and most scale, waving and on-blocking as 0 or 1, then up to six numbers the second game's
/// version 2 adds: the most rotation in degrees and five unnamed), from version 3 a line each, <c>- Key value</c> (RandomScale min max,
/// AllowWaving, AllowOnBlocking, MaxRotation, MinEdgeScale, AudioType, DelayMultiplier, SizeMultiplier, TimeMultiplier, Seed, and any
/// other key kept as written) - then a line a mesh: its <c>.fmt</c> quoted, a number, and pairs of numbers between nought and one.
/// WHERE THE GAME PUTS THEM IS IN NO FILE: the client scatters a layer itself, so what the number and the pairs mean is read off the
/// files and the game's picture before any rule is given (the capture's ground-scatter.txt).
/// </remarks>
public sealed class DoodadLayer
{
    /// <summary>What a doodad layer's file name ends with.</summary>
    public const string Extension = ".dlp";

    /// <summary>Nothing read.</summary>
    public static DoodadLayer None { get; } = new() { Why = "nothing to read" };

    /// <summary>The version line's number, or -1 where the file has none.</summary>
    public int Version { get; private init; } = -1;

    /// <summary>The least scale - RandomScale's first, or the version 2 header's first.</summary>
    public float ScaleMin { get; private init; } = 1f;

    /// <summary>The most scale.</summary>
    public float ScaleMax { get; private init; } = 1f;

    /// <summary>Whether the meshes may wave - AllowWaving.</summary>
    public bool AllowWaving { get; private init; }

    /// <summary>Whether they may stand on ground nothing walks on - AllowOnBlocking.</summary>
    public bool AllowOnBlocking { get; private init; }

    /// <summary>The most a mesh is turned, in degrees - MaxRotation - or null where unset.</summary>
    public int? MaxRotation { get; private init; }

    /// <summary>The seed - Seed - or null where unset.</summary>
    public int? Seed { get; private init; }

    /// <summary>Every header the file names after the first two, by key, as written - the version 2 header's unnamed numbers under their places.</summary>
    public IReadOnlyList<(string Key, string Value)> Headers { get; private init; } = [];

    /// <summary>The meshes, in file order.</summary>
    public IReadOnlyList<DoodadLayerEntry> Entries { get; private init; } = [];

    /// <summary>Why the file did not read to its end, or empty.</summary>
    public string Why { get; private init; } = string.Empty;

    /// <summary>Whether the whole file read.</summary>
    public bool Ready => Why.Length == 0;

    /// <summary>Reads a file's bytes, through the game's shared text decode. Never throws.</summary>
    public static DoodadLayer Read(byte[]? content)
        => content is not { Length: > 0 } ? None : Parse(StatDescriptionFiles.Decode(content));

    /// <summary>Reads the text. Public so the format can be tested without an install. Never throws.</summary>
    public static DoodadLayer Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return None;
        }

        var lines = new List<string>();
        foreach (string raw in text.ReplaceLineEndings("\n").Split('\n'))
        {
            string line = raw.Trim();
            if (line.Length > 0 && !line.StartsWith("//", StringComparison.Ordinal))
            {
                lines.Add(line);
            }
        }

        var at = 0;
        int version = -1;
        if (at < lines.Count && lines[at].StartsWith("version", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(lines[at].AsSpan(7).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int written))
        {
            version = written;
            at++;
        }

        float scaleMin = 1f, scaleMax = 1f;
        bool waving = false, blocking = false;
        int? rotation = null, seed = null;
        var headers = new List<(string Key, string Value)>();
        if (version >= 3)
        {
            // A LINE A HEADER, "- Key value".
            while (at < lines.Count && lines[at].StartsWith('-'))
            {
                string[] words = Words(lines[at][1..]);
                at++;
                if (words.Length == 0)
                {
                    continue;
                }

                string key = words[0];
                string rest = string.Join(' ', words.Skip(1));
                switch (key)
                {
                    case "RandomScale" when words.Length >= 3 && Number(words[1], out float least) && Number(words[2], out float most):
                        (scaleMin, scaleMax) = (least, most);
                        break;
                    case "AllowWaving":
                        waving = true;
                        break;
                    case "AllowOnBlocking":
                        blocking = true;
                        break;
                    case "MaxRotation" when words.Length >= 2 && int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out int degrees):
                        rotation = degrees;
                        break;
                    case "Seed" when words.Length >= 2 && int.TryParse(words[1], NumberStyles.None, CultureInfo.InvariantCulture, out int seeded):
                        seed = seeded;
                        break;
                    default:
                        headers.Add((key, rest));
                        break;
                }
            }
        }
        else if (at < lines.Count && !lines[at].StartsWith('"'))
        {
            // ONE LINE BELOW VERSION 3: least and most scale, waving, on blocking, then up to six more numbers.
            string[] words = Words(lines[at]);
            at++;
            if (words.Length < 4 || !Number(words[0], out scaleMin) || !Number(words[1], out scaleMax))
            {
                return new DoodadLayer { Version = version, Why = "the header line does not read" };
            }

            waving = words[2] == "1";
            blocking = words[3] == "1";
            string[] names = ["MaxRotation", "uint1", "uint2", "float1", "AudioType", "float2"];
            for (var more = 4; more < words.Length && more - 4 < names.Length; more++)
            {
                if (more == 4 && int.TryParse(words[more], NumberStyles.None, CultureInfo.InvariantCulture, out int degrees))
                {
                    rotation = degrees;
                }
                else
                {
                    headers.Add((names[more - 4], words[more]));
                }
            }
        }

        var entries = new List<DoodadLayerEntry>();
        for (; at < lines.Count; at++)
        {
            if (Entry(lines[at]) is not { } entry)
            {
                return new DoodadLayer
                {
                    Version = version, ScaleMin = scaleMin, ScaleMax = scaleMax, AllowWaving = waving, AllowOnBlocking = blocking,
                    MaxRotation = rotation, Seed = seed, Headers = headers, Entries = entries,
                    Why = string.Create(CultureInfo.InvariantCulture, $"line {at + 1} does not read as a mesh: {lines[at]}"),
                };
            }

            entries.Add(entry);
        }

        return new DoodadLayer
        {
            Version = version, ScaleMin = scaleMin, ScaleMax = scaleMax, AllowWaving = waving, AllowOnBlocking = blocking,
            MaxRotation = rotation, Seed = seed, Headers = headers, Entries = entries,
        };
    }

    /// <summary>One mesh's line - <c>"a.fmt" n (x,y) [x y] ...</c> - or null.</summary>
    private static DoodadLayerEntry? Entry(string line)
    {
        if (!line.StartsWith('"'))
        {
            return null;
        }

        int close = line.IndexOf('"', 1);
        if (close < 0)
        {
            return null;
        }

        string mesh = line[1..close].Replace('\\', '/');
        string rest = line[(close + 1)..].Trim();
        int space = rest.IndexOfAny([' ', '\t']);
        string first = space < 0 ? rest : rest[..space];
        if (!Number(first, out float number))
        {
            return null;
        }

        var points = new List<(float, float)>();
        string tail = space < 0 ? string.Empty : rest[space..].Trim();
        var from = 0;
        while (from < tail.Length)
        {
            char open = tail[from];
            if (char.IsWhiteSpace(open))
            {
                from++;
                continue;
            }

            char shut = open switch { '(' => ')', '[' => ']', _ => '\0' };
            int end = shut == '\0' ? -1 : tail.IndexOf(shut, from + 1);
            if (end < 0)
            {
                return null;
            }

            string[] pair = tail[(from + 1)..end].Split([',', ' ', '\t'], StringSplitOptions.RemoveEmptyEntries);
            if (pair.Length != 2 || !Number(pair[0], out float a) || !Number(pair[1], out float b))
            {
                return null;
            }

            points.Add((a, b));
            from = end + 1;
        }

        return new DoodadLayerEntry(mesh, number, points);
    }

    private static string[] Words(string line) => line.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries);

    private static bool Number(string text, out float value)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
}
