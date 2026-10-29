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

        return lines;
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
