using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// The flow fields recognised among the lists found - the river's flow map and the guiding fog's - and their way home: who points at
/// them, and at those, up to a static of the program or the area instance - the chain a live read of the map needs.
/// </summary>
/// <remarks>
/// WHAT THE MAP IS, MEASURED (v0.1.352's The Red Vale capture, the schema's RiverFlowField): four floats a texel, x and y the flow in
/// world units a second, z the sources and sinks, w the distance to the bank, positive in the water - the flow standing exactly where w is
/// not below nought (the bank's own texels hold minus nought), as GetFlowSample's readers in water.ffx take it. THE GUIDING FOG'S LIST IS
/// LAID OUT THE SAME (list 2 there: its flow only where w >= 0 too, over 98% of the grid against the river's 11%, at 3 a second against
/// 50), so a list laid out so is a flow field (<see cref="FlowMap"/>), its share of the grid written to tell them apart, and the way home
/// is sought for each; the counts are written either way.
///
/// THE WAY HOME (v0.1.353, asked for from the live client: the map read live where the Monster Book's stage is the area's own). The map
/// is made at run time and was found by a sweep of the whole of memory, which no frame can afford; a chain of pointers from something
/// the tool already reaches can. So, level by level, the whole of writable memory is searched for 8-byte words pointing at most
/// <see cref="Reach"/> before a place - the holder at the first level, the places found at the one before after it - each found place
/// named by the object round it where a vtable of the program stands at most <see cref="Reach"/> before it (its class by the program's own
/// run-time type information), and a place inside the program's image (a static) or the area instance ends its chain. Every chain found
/// is written as the reads that follow it. CHOSEN: the reach, the levels, a level's seconds and the places kept a level.
/// </remarks>
public static partial class RiverFlowHunt
{
    /// <summary>How far before a place a pointer may point and still be taken for its object's - CHOSEN.</summary>
    public const int Reach = 0x800;

    /// <summary>Levels of the way home looked up before it stops - CHOSEN.</summary>
    public const int MostLevels = 4;

    /// <summary>How long one level's sweep looks before it stops itself - CHOSEN.</summary>
    public const int LevelSeconds = 30;

    /// <summary>The most places kept at one level, those pointing nearest their target's place first - CHOSEN.</summary>
    public const int MostLinks = 32;

    /// <summary>The most pointers one level collects before it keeps no more - a window in a crowded place cannot fill the memory.</summary>
    public const int MostPointers = 1 << 20;

    /// <summary>How much of an object on a way home is written, past the place pointed at - CHOSEN.</summary>
    public const int PlaceBytes = 0x100;

    /// <summary>The most places of the program's code written for one static.</summary>
    public const int MostReferences = 16;

    /// <summary>How much of the area instance counts as it - CHOSEN, past the schema's last field (0x8D0).</summary>
    public const int AreaSpan = 0x1000;

    /// <summary>A list read as a flow field laid out as the river's flow map: whether it is one, and the lines saying why.</summary>
    /// <param name="Is">Whether its texels are laid out as the flow map's.</param>
    /// <param name="Lines">The counts, for the report.</param>
    public sealed record FlowReading(bool Is, IReadOnlyList<string> Lines);

    /// <summary>One pointer found on the way home: where it stands, what it holds, and the place it was found for.</summary>
    /// <param name="Level">One for a pointer at the holder, two for one at a level-one place, and on.</param>
    /// <param name="Slot">Where the pointer stands.</param>
    /// <param name="Value">What it points at.</param>
    /// <param name="Target">The place it lies at most <see cref="Reach"/> before.</param>
    /// <param name="Below">The link whose slot the target is, or minus one for a holder.</param>
    /// <param name="Holder">Which holder's way it is.</param>
    public readonly record struct Link(int Level, ulong Slot, ulong Value, ulong Target, int Below, int Holder);

    /// <summary>
    /// How a list of four floats a texel reads as a flow field laid out as the river's flow map - x y the flow, z sources and sinks, w the
    /// distance to the bank, the flow only where w is not below nought and w of both signs - or null for a list of another texel's size.
    /// </summary>
    public static FlowReading? FlowMap(byte[] data, int width, int height, int element)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (element != 16 || width <= 0 || height <= 0)
        {
            return null;
        }

        int texels = Math.Min(width * height, data.Length / 16);
        var speeds = new float[texels];
        int flowing = 0, flowingInWater = 0, stillInWater = 0, inWater = 0, sources = 0, sinks = 0;
        float least = float.MaxValue, most = float.MinValue;
        ReadOnlySpan<byte> bytes = data;
        for (var texel = 0; texel < texels; texel++)
        {
            ReadOnlySpan<byte> one = bytes.Slice(texel * 16, 16);
            float x = BinaryPrimitives.ReadSingleLittleEndian(one), y = BinaryPrimitives.ReadSingleLittleEndian(one[4..]);
            float z = BinaryPrimitives.ReadSingleLittleEndian(one[8..]), w = BinaryPrimitives.ReadSingleLittleEndian(one[12..]);
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z) || !float.IsFinite(w))
            {
                return new FlowReading(false, ["  not a flow field: a float that is not finite"]);
            }

            // MINUS NOUGHT IS THE BANK: w >= 0 holds for it, as GetFlowSample's readers compare.
            bool water = w >= 0f;
            inWater += water ? 1 : 0;
            least = MathF.Min(least, w);
            most = MathF.Max(most, w);
            sources += z > 0f ? 1 : 0;
            sinks += z < 0f ? 1 : 0;
            if (x != 0f || y != 0f)
            {
                speeds[flowing++] = MathF.Sqrt((x * x) + (y * y));
                flowingInWater += water ? 1 : 0;
            }
            else if (water)
            {
                stillInWater++;
            }
        }

        bool isMap = flowing > 0 && flowingInWater == flowing && least < 0f && most > 0f;
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"  {(isMap ? "a flow field laid out as the river's flow map" : "not a flow field as the river's flow map is laid out")} (GetFlowSample: x y the flow in units a second, z sources and sinks, w the distance to the bank, the water where w >= 0): flow at {flowing} of {texels} texels, {flowingInWater} of them where w >= 0; w >= 0 at {inWater} ({(double)inWater / texels:P1} of the grid), {stillInWater} of them still; w {least:G6} to {most:G6}; sources at {sources} texels, sinks at {sinks}"),
        };
        if (flowing > 0)
        {
            Span<float> sorted = speeds.AsSpan(0, flowing);
            sorted.Sort();
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"    speed (the engine's before 2 x water_flow_intensity and its cap of 100): median {sorted[flowing / 2]:0.0}, 95% under {sorted[Math.Min(flowing - 1, flowing * 95 / 100)]:0.0}, at most {sorted[^1]:0.0}"));
        }

        return new FlowReading(isMap, lines);
    }

    /// <summary>
    /// The flow fields' way home: the whole of writable memory searched level by level for pointers at most <see cref="Reach"/> before a
    /// holder, then before those places, up to a static of the program or the area instance - and every chain found written as its reads.
    /// </summary>
    /// <param name="reader">Past the tap; its module is the program's image.</param>
    /// <param name="regions">The game's memory regions.</param>
    /// <param name="areaInstance">The area instance, a root.</param>
    /// <param name="holders">Each flow field's holder - where its begin and end stand - and its name in the report.</param>
    /// <param name="running">Where it says how far it is, and is stopped.</param>
    public static List<string> Home(IMemoryReader reader, IMemoryRegions regions, ulong areaInstance, IReadOnlyList<(ulong Holder, string Name)> holders, Running running)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(holders);
        ArgumentNullException.ThrowIfNull(running);
        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"=== the flow fields' way home: who points at most 0x{Reach:X} before each holder, then before those places, up to {MostLevels} levels, ending at a static of the program (module 0x{reader.ModuleBase:X}, 0x{reader.ModuleSize:X} bytes) or the area instance (0x{areaInstance:X}, 0x{AreaSpan:X} bytes) - a chain below reads the pointer at its first place and adds the number after the arrow, then reads the pointer there and adds the next, the last sum the holder"),
        };
        if (holders.Count == 0)
        {
            lines.Add("  no list is a flow field laid out as the river's flow map - no way home sought");
            return lines;
        }

        var links = new List<Link>();
        var classes = new Dictionary<ulong, string?>();
        var seen = new HashSet<ulong>();

        // THE FIRST LEVEL'S TARGETS: the holders themselves.
        var targets = new List<(ulong At, int Below, int Holder)>();
        for (var at = 0; at < holders.Count; at++)
        {
            targets.Add((holders[at].Holder, -1, at));
            seen.Add(holders[at].Holder);
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {holders[at].Name} at 0x{holders[at].Holder:X}{Named(reader, holders[at].Holder, classes)}"));
        }

        var roots = new List<int>();
        for (var level = 1; level <= MostLevels && targets.Count > 0 && !running.Stopping; level++)
        {
            long budget = 2 * HeapScan.ByteBudget;
            running.Level(level, budget);
            (List<(ulong Slot, ulong Value, int Target)> found, Walked walked) = Pointers(reader, regions, targets, running, budget);

            // THE NEAREST FIRST: a pointer at its object's start lies nearest the place its object holds; a place twice or inside the
            // object it points into is no way home.
            var kept = found
                .Where(hit => !seen.Contains(hit.Slot) && !(hit.Slot >= hit.Value && hit.Slot <= targets[hit.Target].At))
                .GroupBy(hit => hit.Slot)
                .Select(same => same.MinBy(hit => targets[hit.Target].At - hit.Value))
                .OrderBy(hit => targets[hit.Target].At - hit.Value)
                .ThenBy(hit => hit.Slot > targets[hit.Target].At ? hit.Slot - targets[hit.Target].At : targets[hit.Target].At - hit.Slot)
                .Take(MostLinks)
                .ToList();
            seen.UnionWith(kept.Select(hit => hit.Slot));
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  level {level}: {found.Count} pointers found{(found.Count >= MostPointers ? " (no more kept)" : string.Empty)}, {kept.Count} kept - {walked.Said("the first place")}"));
            var next = new List<(ulong At, int Below, int Holder)>();
            foreach ((ulong slot, ulong value, int target) in kept)
            {
                (ulong at, int below, int holder) = targets[target];
                var link = new Link(level, slot, value, at, below, holder);
                links.Add(link);
                string root = RootOf(reader, areaInstance, slot);
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"    0x{slot:X} holds 0x{value:X}, 0x{at - value:X} before 0x{at:X}{(root.Length > 0 ? " - " + root : string.Empty)}{Inside(reader, slot, classes)}{Named(reader, value, classes)}"));
                if (root.Length > 0)
                {
                    roots.Add(links.Count - 1);
                }
                else
                {
                    next.Add((slot, links.Count - 1, holder));
                }
            }

            targets = next;
        }

        lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {roots.Count} ways home found{(running.Stopping ? " - STOPPED by a click" : string.Empty)}"));
        foreach (int root in roots)
        {
            lines.Add("    " + Chain(reader, areaInstance, links, root, holders));
        }

        if (roots.Count == 0 && targets.Count > 0)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  the last level's places, where a further level would look: {string.Join(", ", targets.Take(MostLinks).Select(target => $"0x{target.At:X}"))}"));
        }

        if (roots.Count > 0)
        {
            lines.Add(string.Empty);
            lines.AddRange(Places(reader, links, roots, holders, classes));
            ulong[] statics = [.. roots.Select(root => links[root].Slot)
                .Where(slot => reader.ModuleSize > 0 && slot >= reader.ModuleBase && slot < reader.ModuleBase + reader.ModuleSize)
                .Distinct()
                .Order()];
            if (statics.Length > 0)
            {
                lines.Add(string.Empty);
                lines.AddRange(References(reader, statics));
            }
        }

        return lines;
    }

    /// <summary>
    /// Every place a chain reads, whole: each static from 0x40 before it to 0x40 after, each object from 0x20 before the place pointed at
    /// to <see cref="PlaceBytes"/> after, a word a line - its value, and what it is: a place of the chains, a word of the program (and the
    /// class its vtable names), another pointer, or two ints and two floats.
    /// </summary>
    private static List<string> Places(
        IMemoryReader reader, List<Link> links, List<int> roots, IReadOnlyList<(ulong Holder, string Name)> holders, Dictionary<ulong, string?> classes)
    {
        // THE NAMES a word may be: the holders, and every place a chain reads.
        var names = new Dictionary<ulong, string>();
        foreach ((ulong holder, string name) in holders)
        {
            names.TryAdd(holder, name);
        }

        var objects = new SortedSet<ulong>();
        var statics = new SortedSet<ulong>();
        foreach (int root in roots)
        {
            statics.Add(links[root].Slot);
            for (int at = root; at >= 0; at = links[at].Below)
            {
                objects.Add(links[at].Value);
                names.TryAdd(links[at].Value, string.Create(CultureInfo.InvariantCulture, $"the chain's object at 0x{links[at].Value:X}"));
            }
        }

        var lines = new List<string>
        {
            string.Create(CultureInfo.InvariantCulture,
                $"=== the places the ways home read, whole: every static from 0x40 before it to 0x40 after, every object from 0x20 before the place pointed at to 0x{PlaceBytes:X} after, a word a line"),
        };
        foreach (ulong root in statics)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  the static module+0x{root - reader.ModuleBase:X} (0x{root:X})"));
            lines.AddRange(Words(reader, root, 0x40, 0x48, names, classes));
        }

        foreach (ulong one in objects)
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {names[one]}"));
            lines.AddRange(Words(reader, one, 0x20, PlaceBytes, names, classes));
        }

        return lines;
    }

    /// <summary>The words from <paramref name="before"/> bytes before a place to <paramref name="after"/> after it, each said.</summary>
    private static IEnumerable<string> Words(IMemoryReader reader, ulong at, int before, int after, Dictionary<ulong, string> names, Dictionary<ulong, string?> classes)
    {
        // WHAT LIES BEFORE MAY NOT READ (a place at the start of its region): then from the place itself.
        var bytes = new byte[before + after];
        if (at < (ulong)before || !reader.TryRead(at - (ulong)before, bytes))
        {
            before = 0;
            bytes = new byte[after];
            if (!reader.TryRead(at, bytes))
            {
                yield return "    did not read";
                yield break;
            }
        }

        for (var offset = 0; offset + 8 <= bytes.Length; offset += 8)
        {
            ulong word = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(offset));
            int relative = offset - before;
            string place = relative < 0
                ? string.Create(CultureInfo.InvariantCulture, $"-0x{-relative:X}")
                : string.Create(CultureInfo.InvariantCulture, $"+0x{relative:X}");
            yield return string.Create(CultureInfo.InvariantCulture, $"    {place,6} 0x{word:X16}  {Said(reader, word, names, classes)}");
        }
    }

    /// <summary>What a word is, for a place's listing.</summary>
    private static string Said(IMemoryReader reader, ulong word, Dictionary<ulong, string> names, Dictionary<ulong, string?> classes)
    {
        if (names.TryGetValue(word, out string? name))
        {
            return name;
        }

        foreach ((ulong place, string known) in names)
        {
            if (word < place && place - word <= Reach)
            {
                return string.Create(CultureInfo.InvariantCulture, $"0x{place - word:X} before {known}");
            }
        }

        if (reader.ModuleSize > 0 && word >= reader.ModuleBase && word < reader.ModuleBase + reader.ModuleSize)
        {
            return string.Create(CultureInfo.InvariantCulture, $"module+0x{word - reader.ModuleBase:X}{(ClassOf(reader, word, classes) is { } type ? ", a vtable of " + type : string.Empty)}");
        }

        if (MemoryReaderExtensions.IsPlausiblePointer(word) && (word & 7) == 0)
        {
            return "a pointer";
        }

        int low = (int)(uint)word, high = (int)(uint)(word >> 32);
        float a = BitConverter.Int32BitsToSingle(low), b = BitConverter.Int32BitsToSingle(high);
        return string.Create(CultureInfo.InvariantCulture, $"ints {low} {high}, floats {a:G6} {b:G6}");
    }

    /// <summary>
    /// The program's code that reads each static: every place in the image whose four bytes, read as a displacement from the end of
    /// themselves, land on it behind a ModRM byte addressing [rip + disp32] (mod 00, r/m 101) - how a static is reached in x64 - each
    /// written with the bytes round it and the shortest run round it that is found once in the image, the displacement left open (^):
    /// what the schema's statics need to find it again after a restart or a patch.
    /// </summary>
    private static List<string> References(IMemoryReader reader, IReadOnlyList<ulong> statics)
    {
        var lines = new List<string>
        {
            "=== the code reading each static: [rip + disp32] references in the program's image, the bytes round each, and the shortest pattern round it found once in the image (the static resolves at its ^ as the schema's statics do, the instruction ending after the displacement)",
        };
        var image = new byte[reader.ModuleSize];
        for (var at = 0; at < image.Length; at += 4 * 1024 * 1024)
        {
            reader.TryRead(reader.ModuleBase + (ulong)at, image.AsSpan(at, Math.Min(4 * 1024 * 1024, image.Length - at)));
        }

        foreach (ulong target in statics)
        {
            var found = new List<int>();
            long relative = (long)(target - reader.ModuleBase);
            for (var at = 1; at + 4 <= image.Length && found.Count < MostReferences; at++)
            {
                if ((image[at - 1] & 0xC7) == 0x05 && at + 4L + BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(at)) == relative)
                {
                    found.Add(at);
                }
            }

            lines.Add(string.Create(CultureInfo.InvariantCulture,
                $"  module+0x{relative:X}: {found.Count}{(found.Count >= MostReferences ? " or more" : string.Empty)} places read it"));
            foreach (int at in found)
            {
                int from = Math.Max(0, at - 16), to = Math.Min(image.Length, at + 16);
                lines.Add(string.Create(CultureInfo.InvariantCulture,
                    $"    module+0x{at:X} (its displacement): {Hex(image, from, at)} [{Hex(image, at, at + 4)}] {Hex(image, at + 4, to)}"));
                lines.Add("      " + (Unique(image, at) is { } pattern ? "found once: " + pattern : "no run of up to 16 bytes either side is found once"));
            }
        }

        return lines;
    }

    /// <summary>
    /// The shortest pattern round a displacement found once in the image, or null: every site sharing the two bytes before it gathered once,
    /// each one's run of like bytes before and after measured, and a pattern of so many bytes before and after is found once where no other
    /// site's runs reach both.
    /// </summary>
    private static string? Unique(byte[] image, int at)
    {
        const int Most = 16;
        if (at < 2)
        {
            return null;
        }

        // EVERY SITE whose two bytes before the displacement are this one's, and how far each agrees with it either way.
        var reaches = new List<(int Before, int After)>();
        ReadOnlySpan<byte> all = image;
        ReadOnlySpan<byte> pair = all.Slice(at - 2, 2);
        for (int from = 0, found; (found = all[from..].IndexOf(pair)) >= 0; from += found + 1)
        {
            int site = from + found + 2;
            if (site == at || site + 4 > image.Length)
            {
                continue;
            }

            int before = 2, after = 0;
            while (before < Most && at - before - 1 >= 0 && site - before - 1 >= 0 && image[at - before - 1] == image[site - before - 1])
            {
                before++;
            }

            while (after < Most && at + 4 + after < image.Length && site + 4 + after < image.Length && image[at + 4 + after] == image[site + 4 + after])
            {
                after++;
            }

            reaches.Add((before, after));
        }

        foreach (int total in (ReadOnlySpan<int>)[3, 5, 7, 9, 11, 13, 15, 17, 20, 24, 28, 32])
        {
            for (int before = Math.Min(total, Most); before >= Math.Max(2, total - Most); before--)
            {
                int after = total - before;
                if (at - before < 0 || at + 4 + after > image.Length || reaches.Exists(one => one.Before >= before && one.After >= after))
                {
                    continue;
                }

                return string.Join(' ', new[] { Hex(image, at - before, at), "^ ?? ?? ?? ??", Hex(image, at + 4, at + 4 + after) }.Where(part => part.Length > 0));
            }
        }

        return null;
    }

    /// <summary>Bytes as hex, spaced.</summary>
    private static string Hex(byte[] bytes, int from, int to)
    {
        var text = new StringBuilder(Math.Max(0, to - from) * 3);
        for (int at = from; at < to; at++)
        {
            text.Append(at > from ? " " : string.Empty).Append(bytes[at].ToString("X2", CultureInfo.InvariantCulture));
        }

        return text.ToString();
    }

    /// <summary>A chain from its root down to its holder, as the reads that follow it.</summary>
    private static string Chain(IMemoryReader reader, ulong areaInstance, List<Link> links, int root, IReadOnlyList<(ulong Holder, string Name)> holders)
    {
        var said = new StringBuilder(RootOf(reader, areaInstance, links[root].Slot));
        for (int at = root; at >= 0; at = links[at].Below)
        {
            said.Append(CultureInfo.InvariantCulture, $" -> +0x{links[at].Target - links[at].Value:X}");
        }

        return said.Append(" = ").Append(holders[links[root].Holder].Name).ToString();
    }

    /// <summary>"module+0x..." for a static, "the area instance +0x..." for a place in it, else nothing.</summary>
    private static string RootOf(IMemoryReader reader, ulong areaInstance, ulong slot)
    {
        if (reader.ModuleSize > 0 && slot >= reader.ModuleBase && slot < reader.ModuleBase + reader.ModuleSize)
        {
            return string.Create(CultureInfo.InvariantCulture, $"module+0x{slot - reader.ModuleBase:X}");
        }

        return areaInstance != 0 && slot >= areaInstance && slot < areaInstance + AreaSpan
            ? string.Create(CultureInfo.InvariantCulture, $"the area instance +0x{slot - areaInstance:X}")
            : string.Empty;
    }

    /// <summary>
    /// Every 8-byte word of writable memory pointing at most <see cref="Reach"/> before one of the targets, on every core, nearest the
    /// first target first.
    /// </summary>
    private static (List<(ulong Slot, ulong Value, int Target)> Found, Walked Walked) Pointers(
        IMemoryReader reader, IMemoryRegions regions, IReadOnlyList<(ulong At, int Below, int Holder)> targets, Running running, long budget)
    {
        // THE TARGETS' WINDOWS SORTED BY THEIR LEAST, all one length: a value lies in a window when the window's least is at most it and
        // no more than Reach below it.
        (ulong Least, int Target)[] windows = [.. targets
            .Select((target, index) => (Least: target.At >= Reach ? target.At - Reach : 0UL, Target: index))
            .OrderBy(window => window.Least)];
        ulong[] leasts = [.. windows.Select(window => window.Least)];
        ulong lowest = leasts[0], highest = windows.Max(window => targets[window.Target].At);
        long total = 0;
        void Add(List<(ulong, ulong, int)> found, ulong slot, ulong value, int target)
        {
            if (Interlocked.Increment(ref total) <= MostPointers)
            {
                found.Add((slot, value, target));
            }
        }

        return Walk<(ulong, ulong, int)>(reader, regions, targets[0].At, running, budget, LevelSeconds, (chunk, address, found) =>
        {
            for (var at = 0; at + 8 <= chunk.Length; at += 8)
            {
                ulong value = BinaryPrimitives.ReadUInt64LittleEndian(chunk[at..]);
                if (value < lowest || value > highest)
                {
                    continue;
                }

                int index = Array.BinarySearch(leasts, value);
                for (int one = index >= 0 ? index : ~index - 1; one >= 0 && value - leasts[one] <= Reach; one--)
                {
                    if (value <= targets[windows[one].Target].At)
                    {
                        Add(found, address + (ulong)at, value, windows[one].Target);
                    }
                }

                // EQUAL LEASTS AFTER THE ONE FOUND: a binary search lands on any of them.
                for (int one = index >= 0 ? index + 1 : leasts.Length; one < leasts.Length && leasts[one] == value; one++)
                {
                    Add(found, address + (ulong)at, value, windows[one].Target);
                }
            }
        });
    }

    /// <summary>", a Class" where a vtable of the program stands at the place, else nothing.</summary>
    private static string Named(IMemoryReader reader, ulong at, Dictionary<ulong, string?> classes)
    {
        var word = new byte[8];
        return reader.TryRead(at, word) && ClassOf(reader, BinaryPrimitives.ReadUInt64LittleEndian(word), classes) is { } name
            ? ", pointing at a " + name
            : string.Empty;
    }

    /// <summary>
    /// ", after a Class's vtable at 0x... (+0x...)" where a vtable of the program stands at most <see cref="Reach"/> before the place, the
    /// nearest - its object's, unless that object has none and a neighbour's stands before it.
    /// </summary>
    private static string Inside(IMemoryReader reader, ulong slot, Dictionary<ulong, string?> classes)
    {
        if (reader.ModuleSize == 0 || slot < Reach)
        {
            return string.Empty;
        }

        var bytes = new byte[Reach + 8];
        ulong from = slot - Reach;
        if (!reader.TryRead(from, bytes))
        {
            return string.Empty;
        }

        for (int at = Reach; at >= 0; at -= 8)
        {
            if (ClassOf(reader, BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(at)), classes) is { } name)
            {
                return string.Create(CultureInfo.InvariantCulture, $", after a {name}'s vtable at 0x{from + (ulong)at:X} (+0x{Reach - at:X})");
            }
        }

        return string.Empty;
    }

    /// <summary>
    /// The class a vtable of the program belongs to, by MSVC's run-time type information: the complete object locator a word before the
    /// vtable (signature 1, its own place's offset from the image at +0x14), its type descriptor's name at +0x10 - or null.
    /// </summary>
    public static string? ClassOf(IMemoryReader reader, ulong vtable, Dictionary<ulong, string?> classes)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(classes);
        ulong image = reader.ModuleBase, end = reader.ModuleBase + reader.ModuleSize;
        if (reader.ModuleSize == 0 || vtable < image + 8 || vtable >= end || (vtable & 7) != 0)
        {
            return null;
        }

        if (classes.TryGetValue(vtable, out string? known))
        {
            return known;
        }

        string? name = null;
        var word = new byte[8];
        var locator = new byte[24];
        var text = new byte[256];
        if (reader.TryRead(vtable - 8, word)
            && BinaryPrimitives.ReadUInt64LittleEndian(word) is var col && col >= image && col + 24 <= end
            && reader.TryRead(col, locator)
            && BinaryPrimitives.ReadUInt32LittleEndian(locator) == 1
            && (ulong)BinaryPrimitives.ReadUInt32LittleEndian(locator.AsSpan(20)) == col - image
            && image + BinaryPrimitives.ReadUInt32LittleEndian(locator.AsSpan(12)) is var descriptor && descriptor + 16 < end
            && reader.TryRead(descriptor + 16, text))
        {
            int length = Array.IndexOf(text, (byte)0);
            string mangled = Encoding.ASCII.GetString(text, 0, length < 0 ? text.Length : length);
            name = mangled.StartsWith(".?A", StringComparison.Ordinal) ? Demangled(mangled) : null;
        }

        classes[vtable] = name;
        return name;
    }

    /// <summary>".?AVRiverMarkupField@Renderer@@" as Renderer::RiverMarkupField; a template's or a lambda's name as it stands.</summary>
    public static string Demangled(string mangled)
    {
        ArgumentNullException.ThrowIfNull(mangled);
        if (mangled.Length < 6 || !mangled.EndsWith("@@", StringComparison.Ordinal) || mangled.AsSpan(4).IndexOfAny('?', '$') >= 0)
        {
            return mangled;
        }

        string[] parts = mangled[4..^2].Split('@', StringSplitOptions.RemoveEmptyEntries);
        Array.Reverse(parts);
        return string.Join("::", parts);
    }
}
