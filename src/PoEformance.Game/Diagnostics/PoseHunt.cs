using System.Globalization;
using System.Numerics;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Game.Files;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// A rig as the files hold it, for finding its pose in the game's memory: each bone's name and parent, and where it rests - in its
/// parent's frame (the .ast's own bind) and in the model's (the tree composed).
/// </summary>
/// <param name="Piece">The .ao it is the rig of - a piece the player is drawn with, or one such a piece hangs off itself.</param>
/// <param name="Path">The rig's own file.</param>
/// <param name="Names">Each bone's name.</param>
/// <param name="Parents">Each bone's parent, -1 for a root.</param>
/// <param name="Local">Each bone's rest in its parent's frame, the file's own matrix (row vectors: a child's model matrix is its local one times its parent's).</param>
/// <param name="Model">Each bone's rest in the model's frame.</param>
public sealed record PoseRig(
    string Piece, string Path, IReadOnlyList<string> Names, IReadOnlyList<int> Parents, IReadOnlyList<Matrix4x4> Local, IReadOnlyList<Matrix4x4> Model)
{
    /// <summary>The bones' prefix the engine simulates - its physics bones (a lantern's, a skirt's, a feather's).</summary>
    public const string SimulatedPrefix = "phys_";

    /// <summary>How many bones.</summary>
    public int Count => Names.Count;

    /// <summary>A rig's rest read off its skeleton, or null where its tree does not hold together.</summary>
    public static PoseRig? Of(string piece, string path, AnimationSkeleton? rig)
    {
        if (SkeletonPose.Of(rig) is not { } pose || rig is null)
        {
            return null;
        }

        return new PoseRig(
            piece, path, [.. rig.Bones.Select(bone => bone.Name)], [.. pose.Parents], [.. rig.Bones.Select(bone => bone.Bind)], [.. pose.BindModel]);
    }

    /// <summary>Whether the engine simulates this bone itself.</summary>
    public bool Simulated(int bone) => Names[bone].StartsWith(SimulatedPrefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>Whether this bone or one above it is simulated - so where it stands in the model's frame is the simulation's.</summary>
    public bool Moved(int bone)
    {
        for (int at = bone, steps = 0; at >= 0 && steps <= Count; at = Parents[at], steps++)
        {
            if (Simulated(at))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>How many of its bones the engine simulates.</summary>
    public int SimulatedCount => Enumerable.Range(0, Count).Count(Simulated);
}

/// <summary>Whose frame a pose array's translations are in.</summary>
public enum PoseFrame
{
    /// <summary>Each bone's in its parent's - the .ast's own bind, which an animation turning the bones leaves as it is.</summary>
    Local,

    /// <summary>Each bone's in the model's - the tree composed, which holds still for a rigid piece at rest.</summary>
    Model,

    /// <summary>
    /// Each bone's in the game's world - the model's through the piece's placement (PiecePlacement.World), read with every sweep (v0.1.407,
    /// see PoseWorld).
    /// </summary>
    World,
}

/// <summary>How a bone's translation lies in its record.</summary>
public enum PoseLayout
{
    /// <summary>x, y and z side by side - a matrix's last row, or a translation of its own.</summary>
    Packed,

    /// <summary>x, y and z sixteen bytes apart - a matrix's last column, written by rows of four.</summary>
    Columns,
}

/// <summary>A skeleton's pose found in memory: whose rig, in which frame and layout, the bytes from one bone to the next, and where bone nought's x lies.</summary>
/// <param name="Rig">The rig, by its place in the list looked for.</param>
/// <param name="Frame">Whose frame its translations are in.</param>
/// <param name="Layout">How a translation lies in its record.</param>
/// <param name="Stride">The bytes from one bone's record to the next.</param>
/// <param name="First">The address of bone nought's x.</param>
/// <param name="Matched">How many of its fixed bones stood at their rest when it was checked.</param>
/// <param name="Fixed">How many bones of the rig hold their rest in its frame - neither simulated nor, in the model's frame, under a simulated one.</param>
/// <param name="Votes">How many bones' rest places, found in the sweep, agreed on this array.</param>
/// <param name="Stray">How far the rig's simulated bones stood from their rest in this frame when it was found, on average - nought where it has none.</param>
public sealed record PoseArray(int Rig, PoseFrame Frame, PoseLayout Layout, int Stride, ulong First, int Matched, int Fixed, int Votes, float Stray = 0f)
{
    /// <summary>For an array in the world, its piece's place by its index in the places looked for - -1 for the other frames.</summary>
    public int Place { get; init; } = -1;

    /// <summary>Whether it changed between the search's two reads of it (<see cref="PoseHunt.Changing"/>) - a live pose, not a copy of the rest.</summary>
    public bool Moving { get; init; }
}

/// <summary>What the search looked for and found: the needles, the places found and agreed on, and the arrays kept.</summary>
/// <param name="Arrays">The arrays kept, each rig's best first - those in the world after the rest's.</param>
/// <param name="Lines">What it says of itself, for the report.</param>
public sealed record PoseHuntResult(IReadOnlyList<PoseArray> Arrays, IReadOnlyList<string> Lines)
{
    /// <summary>The places in the world where a simulated bone may be kept, or a fixed bone stands alone (PoseWorld).</summary>
    public IReadOnlyList<WorldCandidate> Candidates { get; init; } = [];

    /// <summary>The pieces' places as read again once the sweep was done - those the world arrays and candidates name.</summary>
    public IReadOnlyList<PiecePlace> Places { get; init; } = [];
}

/// <summary>
/// Finds each rig's pose in the whole of the game's memory by its rest: every bone's translation where an array of the rig's bones would
/// keep it, side by side at some stride.
/// </summary>
/// <remarks>
/// WHY (v0.1.404, DECIDED from the live client: measure the swing before it is simulated). The bone watch of v0.1.401 walked three hops of
/// pointers from each piece the player is drawn with and kept the 48 blocks whose turn matrices changed most - and found the body's bones,
/// the robe's swinging in step every 3.2 s, and the staff's placement four times over, but no array of the staff's own 19 bones: a rigid
/// weapon's pose changes in one bone, the lamp's, so ranked by changing turns it fell behind the body's, and a 19-bone array of 64-byte
/// records is longer than the kilobyte each block was. Searched for by what it must hold instead, it cannot be missed wherever it lies.
///
/// WHAT AN ARRAY MUST HOLD. In its PARENT'S frame a bone's translation is its bone's length and way, which an animation turning the bones
/// leaves as the file wrote it - so an animated body's array holds most of its rest; in the MODEL's frame a rigid piece at rest holds all of
/// it but its simulated bones. Each frame is looked for laid two ways: x, y and z side by side (a 4x4's last row, or a translation of its
/// own) or sixteen bytes apart (a 4x4's last column by rows, a 3x4's). A world frame is not looked for: the hand moves under the search.
///
/// HOW. Each rig's most telling fixed bones - three coordinates away from nought first - are needles of one FloatHunt sweep on every core,
/// nearest the first piece's object first. Each place found votes, for every stride an array may take, for the address bone nought would lie
/// at; an address three bones agree on is read whole and kept where half its fixed bones stand at their rest (CHOSEN, both).
/// </remarks>
public static class PoseHunt
{
    /// <summary>How far a found translation may lie from the file's - the engine composes and stores the same floats. CHOSEN.</summary>
    public const float Tolerance = 0.01f;

    /// <summary>Most needles a rig is looked for by, in one frame and layout - its most telling bones. CHOSEN.</summary>
    public const int MostNeedles = 12;

    /// <summary>
    /// Most places kept of one rest, nearest the anchor first - CHOSEN: the 04:49 sweep of 2026-11-01 held 58 of its 118 needles at
    /// FloatHunt's 64, so an array farther out than 64 copies of a rest went unseen.
    /// </summary>
    public const int MostRestPlaces = 256;

    /// <summary>Most arrays kept of one rig, the most matched first. CHOSEN.</summary>
    public const int MostPerRig = 8;

    /// <summary>Most arrays kept in all - each is read thirty times a second over the watch. CHOSEN.</summary>
    public const int MostArrays = 40;

    /// <summary>The least share of a rig's fixed bones an array must hold at their rest. CHOSEN.</summary>
    public const double LeastShare = 0.5;

    /// <summary>
    /// How long the search waits between its two reads of what it found, to tell a live pose from a copy of one - CHOSEN: a few frames,
    /// in which a live pose moves by hundredths while the person stands still. The 06:00 capture of 2026-11-01 kept eight copies of the
    /// body's rest a rig, every one of them still over the watch, and the arrays and places it kept in the world were two thirds still too.
    /// </summary>
    public static readonly TimeSpan LiveWait = TimeSpan.FromMilliseconds(300);

    /// <summary>The bytes from one bone's record to the next an array may take: packed positions, transforms of their own, 3x4 and 4x4 matrices and those padded.</summary>
    public static readonly int[] Strides = [12, 16, 32, 40, 44, 48, 52, 56, 64, 72, 80, 96, 112, 128, 144, 160, 192, 256];

    /// <summary>The bytes a translation spans in its layout.</summary>
    public static int Span(PoseLayout layout) => layout == PoseLayout.Packed ? 12 : 36;

    /// <summary>Where a bone rests in a frame.</summary>
    public static Vector3 Rest(PoseRig rig, PoseFrame frame, int bone)
    {
        ArgumentNullException.ThrowIfNull(rig);
        return frame == PoseFrame.Local ? rig.Local[bone].Translation : rig.Model[bone].Translation;
    }

    /// <summary>Whether a bone holds its rest in a frame: not simulated, and in the model's frame under no simulated bone.</summary>
    public static bool Holds(PoseRig rig, PoseFrame frame, int bone)
    {
        ArgumentNullException.ThrowIfNull(rig);
        return frame == PoseFrame.Local ? !rig.Simulated(bone) : !rig.Moved(bone);
    }

    /// <summary>The translation laid at an offset of some bytes.</summary>
    public static Vector3 At(ReadOnlySpan<byte> bytes, int at, PoseLayout layout)
    {
        int step = layout == PoseLayout.Packed ? 4 : 16;
        return new Vector3(
            BitConverter.ToSingle(bytes.Slice(at, 4)), BitConverter.ToSingle(bytes.Slice(at + step, 4)), BitConverter.ToSingle(bytes.Slice(at + (2 * step), 4)));
    }

    /// <summary>One needle's owners: the bones of the rigs whose rest it is, in a frame and layout.</summary>
    /// <param name="Rig">The rig.</param>
    /// <param name="Frame">The frame.</param>
    /// <param name="Layout">The layout.</param>
    /// <param name="Bone">The bone.</param>
    public readonly record struct Owner(int Rig, PoseFrame Frame, PoseLayout Layout, int Bone);

    /// <summary>
    /// The needles each rig is looked for by - its most telling fixed bones in each frame and layout - and for each needle the bones it is
    /// the rest of: two bones resting at one place, or one place in both frames (a root's child where the root rests at the identity), share
    /// one needle.
    /// </summary>
    public static (List<FloatNeedle> Needles, List<List<Owner>> Owners) Needles(IReadOnlyList<PoseRig> rigs)
    {
        ArgumentNullException.ThrowIfNull(rigs);
        var needles = new List<FloatNeedle>();
        var owners = new List<List<Owner>>();
        var index = new Dictionary<(PoseLayout, float, float, float), int>();
        for (var rig = 0; rig < rigs.Count; rig++)
        {
            PoseRig one = rigs[rig];
            foreach (PoseFrame frame in (ReadOnlySpan<PoseFrame>)[PoseFrame.Local, PoseFrame.Model])
            {
                // THE MOST TELLING FIRST: three coordinates away from nought, then the farthest - a bone at (0, 0, 10) is a pair of noughts
                // beside a ten, which memory is full of.
                int[] chosen = [.. Enumerable.Range(0, one.Count)
                    .Where(bone => Holds(one, frame, bone) && Telling(Rest(one, frame, bone)))
                    .OrderByDescending(bone => Away(Rest(one, frame, bone)))
                    .ThenByDescending(bone => Farthest(Rest(one, frame, bone)))
                    .Take(MostNeedles)];
                foreach (PoseLayout layout in (ReadOnlySpan<PoseLayout>)[PoseLayout.Packed, PoseLayout.Columns])
                {
                    foreach (int bone in chosen)
                    {
                        Vector3 rest = Rest(one, frame, bone);
                        var key = (layout, rest.X, rest.Y, rest.Z);
                        if (!index.TryGetValue(key, out int needle))
                        {
                            index[key] = needle = needles.Count;
                            float[] values = layout == PoseLayout.Packed
                                ? [rest.X, rest.Y, rest.Z]
                                : [rest.X, float.NaN, float.NaN, float.NaN, rest.Y, float.NaN, float.NaN, float.NaN, rest.Z];
                            needles.Add(new FloatNeedle(
                                string.Create(CultureInfo.InvariantCulture, $"{System.IO.Path.GetFileName(one.Piece)} {one.Names[bone]} {frame} {layout}"), values, Tolerance, Most: MostRestPlaces));
                            owners.Add([]);
                        }

                        owners[needle].Add(new Owner(rig, frame, layout, bone));
                    }
                }
            }
        }

        return (needles, owners);
    }

    /// <summary>
    /// Looks for every rig in the whole of the memory the game writes, nearest <paramref name="anchor"/> first, and keeps the arrays the
    /// places found agree on - and, where pieces' places are given, the arrays and candidates of their bones in the world (PoseWorld) from the
    /// same sweep.
    /// </summary>
    /// <param name="reader">The game's memory.</param>
    /// <param name="regions">Its regions - those the game cannot write and the driver's slow heaps are left out (WritableRegions).</param>
    /// <param name="rigs">The rigs.</param>
    /// <param name="anchor">An address in the game's heap to look out from - a piece's object.</param>
    /// <param name="progress">How far the sweep has got, and its stop.</param>
    /// <param name="places">The pieces whose bones are looked for in the world too, each by its own rig - null or empty for none.</param>
    /// <param name="wait">How long it waits between its two reads of what it found - <see cref="LiveWait"/> where not given.</param>
    public static PoseHuntResult Find(
        IMemoryReader reader,
        IMemoryRegions regions,
        IReadOnlyList<PoseRig> rigs,
        ulong anchor,
        FloatHuntProgress? progress,
        IReadOnlyList<PiecePlace>? places = null,
        TimeSpan? wait = null)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(regions);
        ArgumentNullException.ThrowIfNull(rigs);
        (List<FloatNeedle> needles, List<List<Owner>> owners) = Needles(rigs);
        int rests = needles.Count;
        var worldOf = new List<(int Place, PoseLayout Layout)>();
        needles.AddRange(PoseWorld.Needles(rigs, places ?? [], worldOf));
        if (needles.Count == 0)
        {
            return new PoseHuntResult([], ["no rig had a bone telling enough to look for, and no piece's place was given"]);
        }

        var writable = new WritableRegions(regions);
        FloatHuntResult swept = FloatHunt.Run(reader, writable, needles, anchor, progress, budget: 2 * HeapScan.ByteBudget);
        List<PoseArray> arrays = Arrays(reader, rigs, owners, [.. swept.Sightings.Where(one => one.Needle < rests)], out int addresses, out int read, anchor, wait ?? LiveWait);
        var lines = new List<string>
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"the sweep: {rests} needles for {rigs.Count} rigs and {needles.Count - rests} for {places?.Count ?? 0} pieces in the world, {swept.BytesScanned / 1e9:0.0} GB of {swept.RegionsWalked:N0} regions on {swept.Workers} threads in {swept.Took.TotalSeconds:0.0} s{(swept.Stopped ? " (stopped)" : swept.Truncated ? " (its budget ended it)" : string.Empty)} - left out {writable.Unwritable / 1e9:0.0} GB the game cannot write and {writable.Slow / 1e9:0.0} GB of the driver's uncached and write-combined heaps; {swept.Sightings.Count(one => one.Needle < rests)} places of the rests found ({swept.Capped.Count(one => one < rests)} needles past their {MostRestPlaces}), {addresses} addresses three bones agreed on, {read} read whole, {arrays.Count} kept - {arrays.Count(one => one.Moving)} of them moving between two reads {(wait ?? LiveWait).TotalSeconds:0.##} s apart"),
        };
        foreach (FloatHuntKind kind in swept.Kinds.Take(6))
        {
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {kind.Kind}: {kind.Regions:N0} regions, {kind.Bytes / 1e9:0.00} GB, read {kind.Reading.TotalSeconds:0.0} s, looked through {kind.Sweeping.TotalSeconds:0.0} s"));
        }

        if (places is not { Count: > 0 })
        {
            return new PoseHuntResult(arrays, lines);
        }

        // THE WORLD: every place a piece's box held, read again with its World of the same moment.
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"the world: {places.Count} pieces looked for round their placement's World, {swept.Sightings.Count(one => one.Needle >= rests)} places in their boxes ({swept.Capped.Count(one => one >= rests)} boxes past their {PoseWorld.MostPlaces}); a place stands for a fixed bone within {PoseWorld.Tolerance} of its rest in the model's units, for a simulated one at {1 - PoseWorld.RatioSlack:0.##} to {1 + PoseWorld.RatioSlack:0.##} of its rest distance from its nearest fixed ancestor"));
        (List<PoseArray> world, List<WorldCandidate> candidates, List<PiecePlace> fresh) = PoseWorld.Found(
            reader, rigs, places, [.. swept.Sightings.Where(one => one.Needle >= rests).Select(one => (one.At, worldOf[one.Needle - rests].Place, worldOf[one.Needle - rests].Layout))], lines, wait ?? LiveWait);
        return new PoseHuntResult([.. arrays, .. world], lines) { Candidates = candidates, Places = fresh };
    }

    /// <summary>
    /// The arrays the places found agree on: each place votes, for every stride, for where bone nought would lie; an address
    /// <see cref="Agreeing"/> bones agree on is read whole and kept where at least <see cref="LeastShare"/> of the rig's fixed bones
    /// stand at their rest - an array found in both frames once, in the frame holding more.
    /// </summary>
    /// <param name="reader">The game's memory.</param>
    /// <param name="rigs">The rigs looked for.</param>
    /// <param name="owners">Each needle's owners.</param>
    /// <param name="sightings">Where the needles were found.</param>
    /// <param name="addresses">How many addresses enough bones agreed on.</param>
    /// <param name="read">How many of those were read whole.</param>
    /// <param name="anchor">
    /// The piece's object: of arrays holding as much of their rest, the nearer it are kept first - in a town another player wielding the
    /// same staff holds an array of the same rig.
    /// </param>
    /// <param name="wait">
    /// How long it waits between its two reads of every array found, those that changed kept first (<see cref="Changing"/>) - none where
    /// not given.
    /// </param>
    public static List<PoseArray> Arrays(
        IMemoryReader reader,
        IReadOnlyList<PoseRig> rigs,
        IReadOnlyList<List<Owner>> owners,
        IReadOnlyList<FloatSighting> sightings,
        out int addresses,
        out int read,
        ulong anchor = 0,
        TimeSpan wait = default)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(rigs);
        ArgumentNullException.ThrowIfNull(owners);
        ArgumentNullException.ThrowIfNull(sightings);

        // HOW MANY NEEDLES EACH RIG HAS IN A FRAME AND LAYOUT: a rig of two telling bones cannot gather three votes.
        var needlesOf = new Dictionary<(int, PoseFrame, PoseLayout), HashSet<int>>();
        foreach (List<Owner> one in owners)
        {
            foreach (Owner owner in one)
            {
                (int, PoseFrame, PoseLayout) key = (owner.Rig, owner.Frame, owner.Layout);
                if (!needlesOf.TryGetValue(key, out HashSet<int>? bones))
                {
                    needlesOf[key] = bones = [];
                }

                bones.Add(owner.Bone);
            }
        }

        // A COUNT, NOT A SET OF BONES: a bone's needle in a frame and layout is one, and a place is found once a needle, so each bone votes
        // for an address once - and with 256 places a rest the keys run to hundreds of thousands, a set each the heavier by far.
        var votes = new Dictionary<(int Rig, PoseFrame Frame, PoseLayout Layout, int Stride, ulong First), int>();
        foreach (FloatSighting sighting in sightings)
        {
            foreach (Owner owner in owners[sighting.Needle])
            {
                foreach (int stride in Strides)
                {
                    // A RECORD HOLDS ITS TRANSLATION WHOLE: x, y and z sixteen bytes apart want 36 bytes a bone at least.
                    if (stride < Span(owner.Layout))
                    {
                        continue;
                    }

                    ulong back = (ulong)owner.Bone * (ulong)stride;
                    if (sighting.At < back)
                    {
                        continue;
                    }

                    var key = (owner.Rig, owner.Frame, owner.Layout, stride, sighting.At - back);
                    votes[key] = votes.GetValueOrDefault(key) + 1;
                }
            }
        }

        var found = new List<PoseArray>();
        addresses = 0;
        read = 0;
        foreach (((int rig, PoseFrame frame, PoseLayout layout, int stride, ulong first), int bones) in votes)
        {
            int need = Agreeing(needlesOf.GetValueOrDefault((rig, frame, layout))?.Count ?? 0);
            if (bones < need)
            {
                continue;
            }

            addresses++;
            PoseRig one = rigs[rig];
            int length = ((one.Count - 1) * stride) + Span(layout);
            byte[] bytes = new byte[length];
            if (!reader.TryRead(first, bytes))
            {
                continue;
            }

            read++;
            (int matched, int fixedBones) = Matching(one, frame, layout, stride, bytes, 0);
            if (fixedBones > 0 && matched >= Math.Max(need, (int)Math.Ceiling(LeastShare * fixedBones)))
            {
                found.Add(new PoseArray(rig, frame, layout, stride, first, matched, fixedBones, bones, Stray(one, frame, layout, stride, bytes)));
            }
        }

        // ONE ARRAY ONCE: found in both frames where they agree, kept in the one holding more of its rest - and where both hold all of it
        // (a rigid piece whose fixed bones all hang off its root, the root at rest at the identity, rests alike in both), in the one whose
        // simulated bones stand nearer their rest: a lantern swinging in the model's frame stands a few units from its model rest and some
        // 145 from its rest on its parent.
        List<PoseArray> kept = [.. found
            .GroupBy(one => (one.Rig, one.Layout, one.Stride, one.First))
            .Select(group => group
                .OrderByDescending(one => (double)one.Matched / one.Fixed)
                .ThenBy(one => one.Stray)
                .ThenByDescending(one => one.Votes)
                .ThenBy(one => one.Frame)
                .First())];

        // WHICH ARE LIVE: every array read again after a short wait, those that changed kept first - a copy of the rest never moves.
        bool[] moving = Changing(reader, [.. kept.Select(one => (one.First, ((rigs[one.Rig].Count - 1) * one.Stride) + Span(one.Layout)))], wait);
        kept = [.. kept.Select((one, at) => one with { Moving = moving[at] })];

        // EACH RIG'S BEST, the rigs the engine simulates a bone of first, at most so many in all.
        return [.. kept
            .GroupBy(one => one.Rig)
            .OrderByDescending(group => rigs[group.Key].SimulatedCount > 0)
            .ThenBy(group => group.Key)
            .SelectMany(group => group
                .OrderByDescending(one => one.Moving)
                .ThenByDescending(one => (double)one.Matched / one.Fixed)
                .ThenBy(one => one.First > anchor ? one.First - anchor : anchor - one.First)
                .ThenByDescending(one => one.Votes)
                .Take(MostPerRig))
            .Take(MostArrays)];
    }

    /// <summary>
    /// Which of some spans of memory changed over a wait: each read now, then again once the wait is over - one that does not read counts
    /// as still. With no wait nothing can have changed, and nothing is read.
    /// </summary>
    /// <param name="reader">The game's memory.</param>
    /// <param name="spans">Each span's address and length.</param>
    /// <param name="wait">How long to wait between the two reads.</param>
    public static bool[] Changing(IMemoryReader reader, IReadOnlyList<(ulong At, int Bytes)> spans, TimeSpan wait)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(spans);
        var moving = new bool[spans.Count];
        if (wait <= TimeSpan.Zero || spans.Count == 0)
        {
            return moving;
        }

        var first = new byte[spans.Count][];
        for (var at = 0; at < spans.Count; at++)
        {
            byte[] bytes = new byte[Math.Max(0, spans[at].Bytes)];
            first[at] = reader.TryRead(spans[at].At, bytes) ? bytes : [];
        }

        Thread.Sleep(wait);
        for (var at = 0; at < spans.Count; at++)
        {
            if (first[at].Length == 0)
            {
                continue;
            }

            byte[] again = new byte[first[at].Length];
            moving[at] = reader.TryRead(spans[at].At, again) && !again.AsSpan().SequenceEqual(first[at]);
        }

        return moving;
    }

    /// <summary>How many bones must agree on an address: three, or every needle a rig has where it has fewer.</summary>
    public static int Agreeing(int needles) => Math.Max(1, Math.Min(3, needles));

    /// <summary>How many of a rig's fixed bones stand at their rest in an array read at <paramref name="first"/> of some bytes, and how many it has.</summary>
    public static (int Matched, int Fixed) Matching(PoseRig rig, PoseFrame frame, PoseLayout layout, int stride, ReadOnlySpan<byte> bytes, int first)
    {
        ArgumentNullException.ThrowIfNull(rig);
        int matched = 0, fixedBones = 0;
        for (var bone = 0; bone < rig.Count; bone++)
        {
            if (!Holds(rig, frame, bone))
            {
                continue;
            }

            fixedBones++;
            int at = first + (bone * stride);
            if (at < 0 || at + Span(layout) > bytes.Length)
            {
                continue;
            }

            Vector3 found = At(bytes, at, layout), rest = Rest(rig, frame, bone);
            if (MathF.Abs(found.X - rest.X) <= Tolerance && MathF.Abs(found.Y - rest.Y) <= Tolerance && MathF.Abs(found.Z - rest.Z) <= Tolerance)
            {
                matched++;
            }
        }

        return (matched, fixedBones);
    }

    /// <summary>How far a rig's simulated bones stand from their rest in a frame in an array, on average - nought where it has none.</summary>
    public static float Stray(PoseRig rig, PoseFrame frame, PoseLayout layout, int stride, ReadOnlySpan<byte> bytes)
    {
        ArgumentNullException.ThrowIfNull(rig);
        float sum = 0;
        int count = 0;
        for (var bone = 0; bone < rig.Count; bone++)
        {
            int at = bone * stride;
            if (!rig.Simulated(bone) || at + Span(layout) > bytes.Length)
            {
                continue;
            }

            float far = Vector3.Distance(At(bytes, at, layout), Rest(rig, frame, bone));
            sum += float.IsFinite(far) ? far : float.MaxValue / 1024;
            count++;
        }

        return count == 0 ? 0f : sum / count;
    }

    /// <summary>Whether a rest is worth looking for: it has a coordinate a float hunt can anchor on - not nought, not one.</summary>
    private static bool Telling(Vector3 rest) => Farthest(rest) >= 2f;

    /// <summary>How many of its coordinates stand half a unit or more from nought.</summary>
    private static int Away(Vector3 rest) => (MathF.Abs(rest.X) >= 0.5f ? 1 : 0) + (MathF.Abs(rest.Y) >= 0.5f ? 1 : 0) + (MathF.Abs(rest.Z) >= 0.5f ? 1 : 0);

    private static float Farthest(Vector3 rest) => MathF.Max(MathF.Abs(rest.X), MathF.Max(MathF.Abs(rest.Y), MathF.Abs(rest.Z)));
}
