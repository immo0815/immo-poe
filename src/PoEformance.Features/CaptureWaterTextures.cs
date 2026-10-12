using System.Globalization;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>A texture the water textures' part decoded, and the pictures its channels are written to beside the part.</summary>
/// <param name="Number">Its place in the part, from one.</param>
/// <param name="Path">Its path in the install.</param>
/// <param name="Picture">Its top level, decoded.</param>
public sealed record WaterTexture(int Number, string Path, GamePicture Picture)
{
    /// <summary>The file one channel is written to: <c>water-texture-N-name-r.png</c> and so on.</summary>
    public string FileOf(char channel) =>
        string.Create(CultureInfo.InvariantCulture, $"water-texture-{Number}-{System.IO.Path.GetFileNameWithoutExtension(Path)}-{channel}.png");
}

/// <summary>
/// The capture's water textures part: where the engine's ripple texture may be - every texture whose path names the water, its
/// size and format, the likeliest decoded channel by channel, and the strings the game's own program holds that name one.
/// </summary>
/// <remarks>
/// WRITTEN BECAUSE THE WAVES LOOK AND MOVE OTHERWISE THAN THE GAME'S (said from the live client, v0.1.223). The engine's eight
/// octaves are its own (water.ffx, GetFlowSurfaceField), and so is the way it reads them (GetWaterVolumeStateFieldOctave): what
/// they read is not. <c>surface_tex_sampler</c>, "ripple heightfield and gradient", is a pass texture the engine binds itself,
/// 128 texels a side, its red the height and its green and blue the height's slope a texel - and no shader source names its
/// file, nor any capture's loaded files. So it is looked for two ways: in the install, by the words a path to it would hold
/// (the walk keeps only those, <see cref="Words"/>), and in the program, whose strings a path bound by the engine would be one of.
/// A texture 128 square is marked, as are the decoded ones; <c>ShadeProgram.RippleSheet</c>'s texels are what it replaces.
/// </remarks>
public static class CaptureWaterTextures
{
    /// <summary>The words a texture's path is kept by at the install's walk - and a string in the program.</summary>
    public static IReadOnlyList<string> Words { get; } =
        ["ripple", "water", "ocean", "wave", "caustic", "foam", "flowmap", "river", "splash", "surface", "noise", "shore", "coast", "swell", "tide", "distortion"];

    /// <summary>How strongly each word says "the ripple texture", first the strongest - the order headers are read and pictures chosen in.</summary>
    private static readonly (string Word, int Weight)[] Weights =
    [
        ("ripple", 8), ("water", 5), ("ocean", 5), ("wave", 4), ("swell", 4), ("shore", 3), ("tide", 3), ("surface", 3), ("caustic", 2),
        ("foam", 2), ("flowmap", 2), ("distortion", 2), ("coast", 2), ("river", 2), ("splash", 1), ("noise", 1),
    ];

    /// <summary>Most textures read for their header.</summary>
    private const int MostHeaders = 400;

    /// <summary>Most textures decoded into pictures.</summary>
    private const int MostPictures = 12;

    /// <summary>The widest a texture is decoded at - the ripple texture is 128.</summary>
    private const int WidestPicture = 1024;

    /// <summary>Most strings of the program written.</summary>
    private const int MostStrings = 600;

    /// <summary>The shortest run of characters taken for a string.</summary>
    private const int ShortestString = 6;

    /// <summary>The part's text: a line saying what it holds, the textures, the pictures and the program's strings. Never throws.</summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="textures">Every texture whose path holds one of <see cref="Words"/>, as the install's walk found them.</param>
    /// <param name="programs">The game's program files, read from disk.</param>
    /// <param name="pictures">The textures decoded, for their channels to be written beside the part.</param>
    public static string Write(
        Func<string, byte[]?> read, IReadOnlyList<string>? textures, IReadOnlyList<string>? programs, out IReadOnlyList<WaterTexture> pictures)
    {
        ArgumentNullException.ThrowIfNull(read);
        var body = new StringBuilder();
        string listed = Textures(read, textures, body, out List<WaterTexture> decoded);
        pictures = decoded;
        string strings = Programs(programs, body);
        return listed + "; " + strings + Environment.NewLine + body;
    }

    /// <summary>How strongly a path says "the ripple texture": its words' weights summed.</summary>
    internal static int Weight(string path)
    {
        var weight = 0;
        foreach ((string word, int one) in Weights)
        {
            if (path.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                weight += one;
            }
        }

        return weight;
    }

    /// <summary>Every listed texture, strongest first, its header read up to the limit - and the likeliest decoded.</summary>
    private static string Textures(Func<string, byte[]?> read, IReadOnlyList<string>? textures, StringBuilder said, out List<WaterTexture> decoded)
    {
        decoded = [];
        said.AppendLine().AppendLine("=== the install's textures whose paths name the water, strongest words first - size, levels, format")
            .AppendLine("    (the engine's ripple texture is 128 square: red the height, green and blue its slope a texel - marked <<)");
        if (textures is not { Count: > 0 })
        {
            said.AppendLine("(no textures: the install walk found none, or it had not run when the key went down)");
            return "no water textures listed";
        }

        string[] ordered = [.. textures.Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(Weight).ThenBy(one => one, StringComparer.OrdinalIgnoreCase)];
        var sizes = new List<(string Path, int Width, int Height, int Weight)>();
        int square = 0, unread = 0;
        foreach (string path in ordered)
        {
            if (sizes.Count + unread >= MostHeaders)
            {
                said.Append("  ").Append(path).AppendLine("  (header not read: past the part's limit)");
                continue;
            }

            byte[]? raw = GameArt.ReadRaw(read, path);
            if (raw is not { Length: >= 20 })
            {
                unread++;
                said.Append("  ").Append(path).AppendLine("  (did not read)");
                continue;
            }

            int height = BitConverter.ToInt32(raw, 12), width = BitConverter.ToInt32(raw, 16);
            bool ripple = width == ShadeProgram.RippleSide && height == ShadeProgram.RippleSide;
            square += ripple ? 1 : 0;
            sizes.Add((path, width, height, Weight(path)));
            said.Append(ripple ? "<<" : "  ").Append(path).Append("  ").AppendLine(ModelDump.Header(raw));
        }

        // THE LIKELIEST DECODED: the ripple's size first, then by the words - each channel beside this file.
        said.AppendLine().AppendLine("=== decoded, the ripple's size first and then by the words - each channel beside this file as a picture");
        IEnumerable<(string Path, int Width, int Height, int Weight)> chosen = sizes
            .Where(one => one.Width is > 0 and <= WidestPicture && one.Height is > 0 and <= WidestPicture)
            .OrderByDescending(one => one.Width == ShadeProgram.RippleSide && one.Height == ShadeProgram.RippleSide)
            .ThenByDescending(one => one.Weight)
            .ThenBy(one => one.Width * one.Height)
            .Take(MostPictures);
        foreach ((string path, int width, int height, _) in chosen)
        {
            byte[]? raw = GameArt.ReadRaw(read, path);
            said.AppendLine().Append(CultureInfo.InvariantCulture, $"{decoded.Count + 1}. {path} - {width} x {height}").AppendLine();
            if (GameArt.Decode(raw) is not { Ready: true } picture)
            {
                said.AppendLine("   (did not decode)");
                continue;
            }

            var texture = new WaterTexture(decoded.Count + 1, path, picture);
            decoded.Add(texture);
            foreach (string line in ModelDump.Channels(picture).TrimEnd().Split('\n'))
            {
                said.Append("   ").AppendLine(line.TrimEnd('\r'));
            }

            said.Append("   pictures: ").AppendLine(string.Join(", ", "rgba".Select(texture.FileOf)));
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{ordered.Length} textures name the water, {sizes.Count} read, {square} at the ripple's 128 square, {decoded.Count} decoded");
    }

    /// <summary>The strings each program holds that hold one of the words, those naming a file first.</summary>
    private static string Programs(IReadOnlyList<string>? programs, StringBuilder said)
    {
        said.AppendLine().AppendLine("=== the game's program: its strings holding the water's words - a texture the engine binds itself is named here if anywhere");
        if (programs is not { Count: > 0 })
        {
            said.AppendLine("(no program: the install's folder was not found, or holds none of the game's programs)");
            return "no program to search";
        }

        var total = 0;
        foreach (string program in programs)
        {
            byte[] bytes;
            try
            {
                bytes = File.ReadAllBytes(program);
            }
            catch (Exception failed) when (failed is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                said.Append("- ").Append(program).Append(": not read, ").AppendLine(failed.Message);
                continue;
            }

            SortedDictionary<string, int> found = Strings(bytes);
            total += found.Count;
            said.Append(CultureInfo.InvariantCulture, $"- {program}: {bytes.Length:N0} bytes, {found.Count} strings").AppendLine();
            IEnumerable<KeyValuePair<string, int>> shown = found
                .OrderByDescending(one => Named(one.Key))
                .ThenByDescending(one => Weight(one.Key))
                .ThenBy(one => one.Key, StringComparer.Ordinal)
                .Take(MostStrings);
            foreach ((string text, int times) in shown)
            {
                said.Append("  ").Append(text);
                if (times > 1)
                {
                    said.Append(CultureInfo.InvariantCulture, $"  (x{times})");
                }

                said.AppendLine();
            }

            if (found.Count > MostStrings)
            {
                said.Append(CultureInfo.InvariantCulture, $"  ... and {found.Count - MostStrings} more").AppendLine();
            }
        }

        return string.Create(CultureInfo.InvariantCulture, $"{total} strings in the program name them");
    }

    /// <summary>Whether a string names a file - a path or a picture's extension.</summary>
    private static bool Named(string text) =>
        text.Contains(".dds", StringComparison.OrdinalIgnoreCase) || text.Contains(".tga", StringComparison.OrdinalIgnoreCase)
        || text.Contains(".png", StringComparison.OrdinalIgnoreCase) || text.Contains('/', StringComparison.Ordinal);

    /// <summary>
    /// Every run of printable characters in the bytes, single-byte and two-byte (little-endian) both, that holds one of the words - with how many times it stands.
    /// </summary>
    /// <remarks>
    /// TESTED ON THE BYTES, lowered into one buffer: a program holds millions of runs, and only the few holding a word are made strings.
    /// </remarks>
    internal static SortedDictionary<string, int> Strings(ReadOnlySpan<byte> bytes)
    {
        var found = new SortedDictionary<string, int>(StringComparer.Ordinal);
        var lowered = new byte[LongestString];

        // ONE BYTE A CHARACTER, then TWO, the high one nought, at either alignment.
        Runs(bytes, 0, 1, lowered, found);
        Runs(bytes, 0, 2, lowered, found);
        Runs(bytes, 1, 2, lowered, found);
        return found;
    }

    /// <summary>The longest run tested - a longer one is no name.</summary>
    private const int LongestString = 1024;

    /// <summary>The words, lower case, as bytes.</summary>
    private static readonly byte[][] WordBytes = [.. Words.Select(word => Encoding.ASCII.GetBytes(word.ToLowerInvariant()))];

    /// <summary>The runs at one width from one offset: each character's low byte printable, and a wide one's high byte nought.</summary>
    private static void Runs(ReadOnlySpan<byte> bytes, int offset, int width, byte[] lowered, SortedDictionary<string, int> found)
    {
        var start = -1;
        var length = 0;
        int at = offset;
        for (; at + width <= bytes.Length; at += width)
        {
            byte low = bytes[at];
            if (low is >= 0x20 and <= 0x7E && (width == 1 || bytes[at + 1] == 0))
            {
                if (start < 0)
                {
                    start = at;
                    length = 0;
                }

                if (length < LongestString)
                {
                    lowered[length] = low is >= (byte)'A' and <= (byte)'Z' ? (byte)(low + 32) : low;
                }

                length++;
                continue;
            }

            Ended(bytes, start, at, length, width, lowered, found);
            start = -1;
        }

        Ended(bytes, start, at, length, width, lowered, found);
    }

    /// <summary>A run that ended: kept where it is long enough, not too long, and holds a word.</summary>
    private static void Ended(ReadOnlySpan<byte> bytes, int start, int end, int length, int width, byte[] lowered, SortedDictionary<string, int> found)
    {
        if (start < 0 || length < ShortestString || length > LongestString)
        {
            return;
        }

        ReadOnlySpan<byte> run = lowered.AsSpan(0, length);
        foreach (byte[] word in WordBytes)
        {
            if (run.IndexOf(word) >= 0)
            {
                string text = width == 1 ? Encoding.ASCII.GetString(bytes[start..end]) : Encoding.Unicode.GetString(bytes[start..end]);
                found[text] = found.TryGetValue(text, out int times) ? times + 1 : 1;
                return;
            }
        }
    }
}
