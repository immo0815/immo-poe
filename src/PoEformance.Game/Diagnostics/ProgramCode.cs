using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using PoEformance.Core.Memory;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// The game's program as it runs, for reading the engine's own code offline (program-code.txt): its image whole beside the report
/// (<see cref="ImageFile"/>), and where the words of a bone simulation stand in it - the ClientAnimationController keys an .ao writes,
/// and the names the program gives its physics - with every place of its code and data that reads each one, and the function round it.
/// </summary>
/// <remarks>
/// WHY (v0.1.419, asked for from the live client, 2026-11-01: "we go all in and do chains, cloth and hair directly"). The .ao files name
/// the simulation of every physics bone in a ClientAnimationController block - linear_allowed_contraction, bend_stiffness,
/// bend_allowed_angle, the two viscosities, the enclosure, the animation's force and velocity factors, gravity, collisions - and the survey
/// of v0.1.418 found 8,147 files carrying 2,825 sets of them, most of them cloth and hair. What each number does is written in no file and
/// no reference: the lamp alone was measured, off the game's light list, as a pendulum (BoneSwing.cs). A set leaves out whatever keeps the
/// engine's default, and the defaults are in no file either. The parser that reads the keys and the solver that steps the bones are the
/// engine's own code, so the code is read: the image is written whole, and the report says where to start - each key's text, the code that
/// loads its address (a lea of [rip + disp32]) and the function that does it by the image's own function table (.pdata), and the data
/// words pointing at it (a table of keys). Disassembled offline; nothing here runs or patches the game.
///
/// OFF UNTIL TICKED: the image is a hundred megabytes, which no ordinary capture wants.
/// </remarks>
public static class ProgramCode
{
    /// <summary>The image as read, beside the report: byte for byte from the module's base, a page that did not read left nought.</summary>
    public const string ImageFile = "program-code.bin";

    /// <summary>
    /// The ClientAnimationController keys the install's .ao files write (ao-simulations.txt's count of them, v0.1.418) and the block's
    /// name - matched whole.
    /// </summary>
    public static readonly string[] Keys =
    [
        "ClientAnimationController", "linear_allowed_contraction", "linear_stiffness", "bend_stiffness", "bend_allowed_angle",
        "normal_viscosity_multiplier", "tangent_viscosity_multiplier", "viscosity_multiplier", "enclosure_radius", "enclosure_radius_additive",
        "enclosure_angle", "animation_force_factor", "animation_velocity_factor", "animation_velocity_multiplier", "animation_max_velocity",
        "use_shock_propagation", "gravity", "enable_collisions", "enable_heightmap_collisions", "attachment_groups", "recreate_on_scale",
        "attachment_bones", "bone_group",
    ];

    /// <summary>Words of the program's own names for its physics - matched anywhere in a string, any case. CHOSEN.</summary>
    public static readonly string[] Words =
    [
        "physic", "cloth", "simulat", "verlet", "enclosure", "shock_prop", "shockprop", "viscosity", "contraction", "ragdoll", "jiggle",
        "dangle", "softbody", "pendulum", "phys_", "bonephys", "boneconstraint", "constraint",
    ];

    /// <summary>The fewest letters a string is taken with.</summary>
    private const int FewestLetters = 4;

    /// <summary>The most letters a string is read to.</summary>
    private const int MostLetters = 512;

    /// <summary>The most strings a word lists; the rest are counted.</summary>
    private const int MostStrings = 400;

    /// <summary>The most places reading one string that are listed.</summary>
    private const int MostReaders = 24;

    /// <summary>How many bytes round a place are written with it.</summary>
    private const int Round = 16;

    /// <summary>A section of the image, as its header names it.</summary>
    /// <param name="Name">Its name.</param>
    /// <param name="Rva">Where it starts, from the module's base.</param>
    /// <param name="Size">How long it is in memory.</param>
    /// <param name="Flags">Its characteristics.</param>
    public readonly record struct Section(string Name, int Rva, int Size, uint Flags)
    {
        /// <summary>Whether the section's code runs (IMAGE_SCN_MEM_EXECUTE).</summary>
        public bool Runs => (Flags & 0x20000000u) != 0;

        /// <summary>Whether a place lies inside it.</summary>
        public bool Holds(long rva) => rva >= Rva && rva < (long)Rva + Size;
    }

    /// <summary>A function of the image's own table (.pdata): where it starts and ends, and where its unwinding is described.</summary>
    /// <param name="Begin">Its first byte, from the module's base.</param>
    /// <param name="End">One past its last.</param>
    /// <param name="Unwind">Its unwind information.</param>
    public readonly record struct Function(int Begin, int End, int Unwind);

    /// <summary>A string found, where it stands and what it says.</summary>
    /// <param name="Rva">Where its first letter stands, from the module's base.</param>
    /// <param name="Text">What it says.</param>
    /// <param name="Wide">Whether it is two bytes a letter.</param>
    public readonly record struct Found(int Rva, string Text, bool Wide);

    /// <summary>A place reading a string: in code by [rip + disp32], or in data as a pointer.</summary>
    /// <param name="Rva">Where the displacement or the pointer stands.</param>
    /// <param name="Target">The string's place it reaches.</param>
    /// <param name="Code">Whether it is code.</param>
    public readonly record struct Reader(int Rva, int Target, bool Code);

    /// <summary>A capture's progress and its stop.</summary>
    public sealed class Running
    {
        private int _phase;
        private int _stop;

        /// <summary>Whether it was asked to stop.</summary>
        public bool Stopping => Volatile.Read(ref _stop) != 0;

        /// <summary>Asks it to stop, keeping what it found.</summary>
        public void Stop() => Volatile.Write(ref _stop, 1);

        /// <summary>The window's line: where it stands.</summary>
        public string Line => Volatile.Read(ref _phase) switch
        {
            0 => "copying the program's image",
            1 => "writing the image beside the report",
            2 => "finding the simulation's words in it and what reads them",
            _ => "writing",
        };

        /// <summary>How far it is.</summary>
        public float Fraction => Volatile.Read(ref _phase) switch
        {
            0 => 0.05f,
            1 => 0.4f,
            2 => 0.7f,
            _ => 0.95f,
        };

        /// <summary>A phase begun.</summary>
        internal void Phase(int phase) => Volatile.Write(ref _phase, phase);
    }

    /// <summary>
    /// Copies the running program's image, writes it into <paramref name="folder"/> as <see cref="ImageFile"/>, and reports where the
    /// simulation's words stand in it and what reads them.
    /// </summary>
    /// <param name="reader">The game's memory - past the tap: the image's megabytes belong in no recording.</param>
    /// <param name="folder">Where the report goes, the image beside it.</param>
    /// <param name="running">The progress and the stop.</param>
    public static List<string> Write(IMemoryReader reader, string folder, Running running)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(running);
        if (reader.ModuleBase == 0 || reader.ModuleSize == 0)
        {
            return ["program code: no module to read"];
        }

        var clock = Stopwatch.StartNew();
        running.Phase(0);
        var image = new byte[reader.ModuleSize];
        long unread = ShaderStageHunt.Copy(reader, image);
        double copied = clock.Elapsed.TotalSeconds;
        if (running.Stopping)
        {
            return ["program code: stopped while copying the image - nothing written"];
        }

        running.Phase(1);
        File.WriteAllBytes(Path.Combine(folder, ImageFile), image);
        running.Phase(2);
        List<string> lines = Read(image, reader.ModuleBase, running);
        lines.Insert(0, string.Create(CultureInfo.InvariantCulture,
            $"program code: module 0x{reader.ModuleBase:X}, 0x{image.Length:X} bytes copied in {copied:0.0} s, 0x{unread:X} of them unreadable (left nought); written whole beside this file as {ImageFile} - byte n is module+n; {clock.Elapsed.TotalSeconds:0.0} s in all"));
        running.Phase(3);
        return lines;
    }

    /// <summary>
    /// The report over an image: its sections and function table, then every string naming a key or a word of a bone simulation with
    /// every place reading it and the function round each - shared by the capture and its offline reader.
    /// </summary>
    /// <param name="image">The image, byte n at module+n.</param>
    /// <param name="moduleBase">Where it was loaded - what a pointer in its data holds.</param>
    /// <param name="running">The stop, or null.</param>
    public static List<string> Read(byte[] image, ulong moduleBase, Running? running = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        var lines = new List<string>();
        List<Section> sections = Sections(image, out string header);
        lines.Add(header);
        foreach (Section section in sections)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  section\t{section.Name}\tmodule+0x{section.Rva:X}\t0x{section.Size:X} bytes\tflags 0x{section.Flags:X8}{(section.Runs ? "\tcode" : string.Empty)}"));
        }

        List<Function> functions = Functions(image, out string table);
        lines.Add(table);

        List<Found> found = Strings(image, sections, running);
        Dictionary<int, List<Reader>> readers = Readers(image, sections, found, moduleBase, running);

        lines.Add(string.Empty);
        lines.Add("=== the ClientAnimationController keys and the words of a bone simulation in the program: every string naming one, where it stands, "
            + "and every place reading it - code by [rip + disp32] (the instruction's displacement, the function round it by .pdata, the bytes round it), "
            + "data as a pointer (the words round it: a table of keys sets each beside its offset and default)");
        foreach (string key in Keys)
        {
            Write(lines, $"key \"{key}\"", [.. found.Where(one => one.Text == key)], readers, functions, image);
        }

        foreach (string word in Words)
        {
            Found[] named = [.. found.Where(one => one.Text.Contains(word, StringComparison.OrdinalIgnoreCase) && !Keys.Contains(one.Text))];
            Write(lines, $"word \"{word}\"", named, readers, functions, image);
        }

        return lines;
    }

    /// <summary>The image's sections, read off its own header, and a line saying what the header says.</summary>
    public static List<Section> Sections(byte[] image, out string line)
    {
        ArgumentNullException.ThrowIfNull(image);
        var sections = new List<Section>();
        if (image.Length < 0x40 || image[0] != (byte)'M' || image[1] != (byte)'Z')
        {
            line = "image: no MZ header";
            return sections;
        }

        int pe = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
        if (pe <= 0 || pe + 0x18 > image.Length || BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(pe)) != 0x00004550)
        {
            line = "image: no PE header";
            return sections;
        }

        int count = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(pe + 6));
        uint stamp = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(pe + 8));
        int optional = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(pe + 20));
        int first = pe + 24 + optional;
        for (var one = 0; one < count && first + ((one + 1) * 40) <= image.Length; one++)
        {
            int at = first + (one * 40);
            string name = Encoding.ASCII.GetString(image, at, 8).TrimEnd('\0');
            int size = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at + 8));
            int rva = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at + 12));
            uint flags = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(at + 36));
            sections.Add(new Section(name, rva, Math.Min(size, Math.Max(0, image.Length - rva)), flags));
        }

        line = string.Create(CultureInfo.InvariantCulture,
            $"image: PE header at 0x{pe:X}, built {DateTimeOffset.FromUnixTimeSeconds(stamp):yyyy-MM-dd HH:mm:ss} UTC (stamp 0x{stamp:X8}), {sections.Count} sections");
        return sections;
    }

    /// <summary>The image's function table (the exception directory, .pdata) sorted by start, and a line saying how many.</summary>
    public static List<Function> Functions(byte[] image, out string line)
    {
        ArgumentNullException.ThrowIfNull(image);
        var functions = new List<Function>();
        line = "functions: no exception directory";
        if (image.Length < 0x40)
        {
            return functions;
        }

        int pe = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(0x3C));
        int optional = pe + 24;
        if (pe <= 0 || optional + 112 + (4 * 8) > image.Length || BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(optional)) != 0x20B)
        {
            return functions;
        }

        // THE EXCEPTION DIRECTORY is the fourth of PE32+'s data directories, 112 bytes into the optional header.
        int rva = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(optional + 112 + (3 * 8)));
        int size = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(optional + 112 + (3 * 8) + 4));
        if (rva <= 0 || size <= 0 || (long)rva + size > image.Length)
        {
            return functions;
        }

        for (int at = rva; at + 12 <= rva + size; at += 12)
        {
            int begin = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at));
            int end = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at + 4));
            int unwind = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at + 8));
            if (begin > 0 && end > begin)
            {
                functions.Add(new Function(begin, end, unwind));
            }
        }

        functions.Sort((one, other) => one.Begin.CompareTo(other.Begin));
        line = string.Create(CultureInfo.InvariantCulture,
            $"functions: {functions.Count:N0} in the exception directory at module+0x{rva:X} (0x{size:X} bytes)");
        return functions;
    }

    /// <summary>The function a place of code lies in, or null.</summary>
    public static Function? FunctionAt(List<Function> functions, int rva)
    {
        ArgumentNullException.ThrowIfNull(functions);
        int low = 0, high = functions.Count - 1;
        while (low <= high)
        {
            int middle = (low + high) >>> 1;
            Function one = functions[middle];
            if (rva < one.Begin)
            {
                high = middle - 1;
            }
            else if (rva >= one.End)
            {
                low = middle + 1;
            }
            else
            {
                return one;
            }
        }

        return null;
    }

    /// <summary>
    /// The function a fragment of code belongs to: its own entry's, or - where its unwind information is chained (UNW_FLAG_CHAININFO) -
    /// the entry it is chained to, followed to the end.
    /// </summary>
    public static Function Rooted(byte[] image, Function function)
    {
        ArgumentNullException.ThrowIfNull(image);
        Function at = function;
        for (var hops = 0; hops < 8; hops++)
        {
            int info = at.Unwind & ~1;
            if (info <= 0 || info + 4 > image.Length || ((image[info] >> 3) & 0x4) == 0)
            {
                return at;
            }

            // THE PARENT'S ENTRY follows the unwind codes, which are two bytes each and padded to an even count.
            int codes = image[info + 2];
            int parent = info + 4 + (((codes + 1) & ~1) * 2);
            if (parent + 12 > image.Length)
            {
                return at;
            }

            at = new Function(
                BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(parent)),
                BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(parent + 4)),
                BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(parent + 8)));
        }

        return at;
    }

    /// <summary>
    /// Every string of the image's sections that do not run naming a key whole or a word anywhere - ASCII and two bytes a letter alike,
    /// ended by a nought - on every core.
    /// </summary>
    public static List<Found> Strings(byte[] image, List<Section> sections, Running? running = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(sections);
        var keys = new HashSet<string>(Keys, StringComparer.Ordinal);
        var found = new ConcurrentBag<Found>();
        const int Slice = 1 << 20;
        foreach (Section section in sections.Where(one => !one.Runs && one.Size > 0))
        {
            int slices = (section.Size + Slice - 1) / Slice;
            Parallel.For(0, slices, slice =>
            {
                if (running?.Stopping == true)
                {
                    return;
                }

                int from = section.Rva + (slice * Slice), to = Math.Min(section.Rva + section.Size, from + Slice);
                Scan(image, from, to, keys, found);
            });
        }

        return [.. found.OrderBy(one => one.Rva)];
    }

    /// <summary>The strings starting in one slice of a section: a letter that follows a nought or a non-letter begins one.</summary>
    private static void Scan(byte[] image, int from, int to, HashSet<string> keys, ConcurrentBag<Found> found)
    {
        for (int at = from; at < to; at++)
        {
            // ASCII: a printable run, ended by a nought, begun where the byte before is no printable letter.
            if (Printable(image[at]) && (at == 0 || !Printable(image[at - 1])))
            {
                int end = at;
                while (end < image.Length && end - at < MostLetters && Printable(image[end]))
                {
                    end++;
                }

                if (end - at >= FewestLetters && end < image.Length && image[end] == 0)
                {
                    string text = Encoding.ASCII.GetString(image, at, end - at);
                    if (Named(text, keys))
                    {
                        found.Add(new Found(at, text, Wide: false));
                    }
                }
            }

            // TWO BYTES A LETTER: printable then nought, pair by pair, on an even place.
            if ((at & 1) == 0 && at + 1 < image.Length && Printable(image[at]) && image[at + 1] == 0
                && (at < 2 || !(Printable(image[at - 2]) && image[at - 1] == 0)))
            {
                int end = at;
                while (end + 1 < image.Length && (end - at) / 2 < MostLetters && Printable(image[end]) && image[end + 1] == 0)
                {
                    end += 2;
                }

                if ((end - at) / 2 >= FewestLetters && end + 1 < image.Length && image[end] == 0 && image[end + 1] == 0)
                {
                    string text = Encoding.Unicode.GetString(image, at, end - at);
                    if (Named(text, keys))
                    {
                        found.Add(new Found(at, text, Wide: true));
                    }
                }
            }
        }
    }

    private static bool Printable(byte one) => one is >= 0x20 and < 0x7F;

    private static bool Named(string text, HashSet<string> keys)
    {
        if (keys.Contains(text))
        {
            return true;
        }

        foreach (string word in Words)
        {
            if (text.Contains(word, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Every place reading a string found: in the sections that run, four bytes behind a ModRM byte addressing [rip + disp32] (mod 00, r/m
    /// 101) that land on the string's first letter from their own end - a lea or a mov of its address, no immediate after it; in the rest,
    /// an eight-byte word holding the module's base plus the string's place.
    /// </summary>
    public static Dictionary<int, List<Reader>> Readers(byte[] image, List<Section> sections, List<Found> found, ulong moduleBase, Running? running = null)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(sections);
        ArgumentNullException.ThrowIfNull(found);
        var targets = new HashSet<int>(found.Select(one => one.Rva));
        var readers = new ConcurrentBag<Reader>();
        const int Slice = 1 << 20;
        foreach (Section section in sections.Where(one => one.Size > 0))
        {
            int slices = (section.Size + Slice - 1) / Slice;
            Parallel.For(0, slices, slice =>
            {
                if (running?.Stopping == true)
                {
                    return;
                }

                int from = section.Rva + (slice * Slice), to = Math.Min(section.Rva + section.Size, from + Slice);
                if (section.Runs)
                {
                    for (int at = Math.Max(from, 1); at + 4 <= to; at++)
                    {
                        if ((image[at - 1] & 0xC7) != 0x05)
                        {
                            continue;
                        }

                        long target = at + 4L + BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at));
                        if (target >= 0 && target < image.Length && targets.Contains((int)target))
                        {
                            readers.Add(new Reader(at, (int)target, Code: true));
                        }
                    }
                }
                else
                {
                    for (int at = (from + 7) & ~7; at + 8 <= to; at += 8)
                    {
                        ulong word = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(at));
                        if (word >= moduleBase && word < moduleBase + (ulong)image.Length && targets.Contains((int)(word - moduleBase)))
                        {
                            readers.Add(new Reader(at, (int)(word - moduleBase), Code: false));
                        }
                    }
                }
            });
        }

        return readers.GroupBy(one => one.Target).ToDictionary(group => group.Key, group => group.OrderBy(one => one.Rva).ToList());
    }

    /// <summary>One key's or word's strings with their readers.</summary>
    private static void Write(List<string> lines, string title, Found[] named, Dictionary<int, List<Reader>> readers, List<Function> functions, byte[] image)
    {
        lines.Add(string.Create(CultureInfo.InvariantCulture, $"--- {title}: {named.Length} string(s)"));
        foreach (Found one in named.Take(MostStrings))
        {
            List<Reader> its = readers.GetValueOrDefault(one.Rva) ?? [];
            int code = its.Count(reader => reader.Code);
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  module+0x{one.Rva:X}{(one.Wide ? " (wide)" : string.Empty)} \"{one.Text}\": read by {code} place(s) of code and {its.Count - code} of data"));
            foreach (Reader reader in its.Take(MostReaders))
            {
                if (reader.Code)
                {
                    string within = FunctionAt(functions, reader.Rva) is { } function
                        ? Rooted(image, function) is var root && root.Begin != function.Begin
                            ? string.Create(CultureInfo.InvariantCulture, $"in module+0x{function.Begin:X}..0x{function.End:X}, a part of module+0x{root.Begin:X}..0x{root.End:X}")
                            : string.Create(CultureInfo.InvariantCulture, $"in module+0x{function.Begin:X}..0x{function.End:X}")
                        : "in no function of the table";
                    lines.Add(string.Create(CultureInfo.InvariantCulture,
                        $"    code module+0x{reader.Rva:X} {within}: {Hex(image, reader.Rva - Round, reader.Rva)} [{Hex(image, reader.Rva, reader.Rva + 4)}] {Hex(image, reader.Rva + 4, reader.Rva + 4 + Round)}"));
                }
                else
                {
                    lines.Add(string.Create(CultureInfo.InvariantCulture,
                        $"    data module+0x{reader.Rva:X}: {Around(image, reader.Rva)}"));
                }
            }

            if (its.Count > MostReaders)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"    ... and {its.Count - MostReaders} more"));
            }
        }

        if (named.Length > MostStrings)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  ... and {named.Length - MostStrings} more strings"));
        }
    }

    private static string Hex(byte[] image, int from, int to)
    {
        from = Math.Clamp(from, 0, image.Length);
        to = Math.Clamp(to, from, image.Length);
        return Convert.ToHexString(image, from, to - from);
    }

    /// <summary>The eight-byte words round a data place - four before it, the place itself in brackets, four after.</summary>
    private static string Around(byte[] image, int place)
    {
        var text = new StringBuilder();
        for (int at = Math.Max(0, place - 32); at + 8 <= Math.Min(image.Length, place + 40); at += 8)
        {
            ulong word = BinaryPrimitives.ReadUInt64LittleEndian(image.AsSpan(at));
            text.Append(at == place ? "[" : string.Empty).Append(word.ToString("X16", CultureInfo.InvariantCulture)).Append(at == place ? "] " : " ");
        }

        return text.ToString().TrimEnd();
    }
}
