using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The bone watch's search in the world (PoseWorld, v0.1.407): the memory the game cannot write and the driver's slow heaps left out, a
/// box's places kept past FloatHunt's 64, the Exile's Staff's bones found in the world through its placement's World - an array by its fixed
/// bones, the lamp's lone state by its length from its parent, a lone copy of that parent - and the watch read back in the piece's frame:
/// a lamp swinging as a damped pendulum on a hand that walks and turns measured back to its damping, a copy of the rest said in one line.
/// </summary>
public sealed partial class PoseWorldTests
{
    private const ulong Heap = 0x2_0000_0000;
    private const ulong Slow = 0x3_0000_0000;
    private const int HeapBytes = 0x10000;

    /// <summary>The staff's place in the world: scaled as the player's pieces are, turned, at the 04:49 capture's wand's place.</summary>
    private static readonly Matrix4x4 World =
        Matrix4x4.CreateScale(0.925f) * Matrix4x4.CreateRotationZ(0.5f) * Matrix4x4.CreateTranslation(6120f, 13216.3f, -78.2f);

    /// <summary>Only what the game writes, and none of the driver's slow heaps, is handed on - and how much of each was left out is said.</summary>
    [Fact]
    public void THEWALKLeavesOutWhatTheGameCannotWriteAndTheDriversSlowHeaps()
    {
        var regions = new WritableRegions(new PoseHuntTests.Space(
            new MemoryRegion(0x1000, 0x1000, 0x04, 0x20000),
            new MemoryRegion(0x2000, 0x2000, 0x404, 0x20000),
            new MemoryRegion(0x4000, 0x3000, 0x02, 0x40000),
            new MemoryRegion(0x7000, 0x1000, 0x204, 0x20000),
            new MemoryRegion(0x8000, 0x1000, 0x40, 0x1000000),
            new MemoryRegion(0x9000, 0x1000)));

        Assert.Equal([0x1000UL, 0x8000UL, 0x9000UL], regions.Regions().Select(one => one.Address));
        Assert.Equal(0x3000, regions.Unwritable);
        Assert.Equal(0x3000, regions.Slow);
    }

    /// <summary>A needle asking for more places than FloatHunt's 64 keeps them, nearest the anchor first; one that does not ask is capped.</summary>
    [Fact]
    public void ANEEDLEAskingForMorePlacesKeepsThem()
    {
        byte[] bytes = new byte[0x4000];
        for (var at = 0; at + 12 <= bytes.Length; at += 64)
        {
            BitConverter.TryWriteBytes(bytes.AsSpan(at, 4), 6120f + (at / 64));
            BitConverter.TryWriteBytes(bytes.AsSpan(at + 4, 4), 13216f);
        }

        var memory = new FakeMemoryReader().Place(Heap, bytes);
        var space = new PoseHuntTests.Space(new MemoryRegion(Heap, (ulong)bytes.Length));
        FloatHuntResult result = FloatHunt.Run(
            memory,
            space,
            [new FloatNeedle("box", [6150f, 13216f], 300f, Most: 1000), new FloatNeedle("capped", [6150f, 13216f], 300f)]);

        Assert.Equal(256, result.Sightings.Count(one => one.Needle == 0));
        Assert.Equal(FloatHunt.MostPerNeedle, result.Sightings.Count(one => one.Needle == 1));
        Assert.Equal([1], result.Capped);
    }

    /// <summary>
    /// The staff in the world, through its placement's World (scale 0.925, a turn, the area's place): its array of 4x4s by rows found by its
    /// fixed bones, the lamp swung in it; the lamp's lone state elsewhere found at its length from its parent; a lone copy of that parent
    /// found at its rest - and a copy of the array in a write-combined heap not looked through.
    /// </summary>
    [Fact]
    public void THESTAFFSBonesAreFoundInTheWorldThroughItsPlacement()
    {
        PoseRig rig = PoseHuntTests.Rig("WandererStaffHeld.ao", PoseHuntTests.Staff);
        byte[] heap = PoseHuntTests.Noise(HeapBytes, 5);
        PoseHuntTests.WriteMatrix(heap, 0x1000, World);
        WriteWorld(heap, 0x2000, rig, Matrix4x4.CreateRotationX(0.4f), World);

        // THE LAMP'S OWN STATE, as a simulation would keep it: its place in the world swung otherwise, its last place beside it.
        Vector3 lamp = Lamp(rig, Matrix4x4.CreateRotationY(-0.3f));
        WriteVector(heap, 0x6000, Vector3.Transform(lamp, World));
        WriteVector(heap, 0x600C, Vector3.Transform(Lamp(rig, Matrix4x4.CreateRotationY(-0.28f)), World));

        // A LONE COPY OF ITS PARENT'S PLACE - where the light or the flame would be hung.
        WriteVector(heap, 0x6400, Vector3.Transform(rig.Model[1].Translation, World));

        byte[] slow = new byte[0x2000];
        WriteWorld(slow, 0x100, rig, Matrix4x4.Identity, World);
        var memory = new FakeMemoryReader().Place(Heap, heap).Place(Slow, slow);
        var space = new PoseHuntTests.Space(new MemoryRegion(Heap, HeapBytes, 0x04, 0x20000), new MemoryRegion(Slow, (ulong)slow.Length, 0x404, 0x20000));

        PoseHuntResult found = PoseHunt.Find(memory, space, [rig], Heap, null, [new PiecePlace(0, Heap + 0x1000, World)]);

        PoseArray array = Assert.Single(found.Arrays);
        Assert.Equal(PoseFrame.World, array.Frame);
        Assert.Equal(PoseLayout.Packed, array.Layout);
        Assert.Equal(64, array.Stride);
        Assert.Equal(Heap + 0x2000 + 48, array.First);
        Assert.Equal(18, array.Fixed);
        Assert.Equal(array.Fixed, array.Matched);
        Assert.Equal(0, array.Place);
        Assert.InRange(array.Stray, 8f, 11f);

        WorldCandidate state = Assert.Single(found.Candidates, one => one.At == Heap + 0x6000);
        Assert.Equal((2, 1, PoseLayout.Packed), (state.Bone, state.Parent, state.Layout));
        Assert.InRange(state.Ratio, 0.999f, 1.001f);
        WorldCandidate copy = Assert.Single(found.Candidates, one => one.At == Heap + 0x6400);
        Assert.Equal((1, -1), (copy.Bone, copy.Parent));
        Assert.DoesNotContain(found.Candidates, one => one.At >= Slow);
        Assert.Equal(World, Assert.Single(found.Places).World);
        Assert.Contains(found.Lines, line => line.Contains("GB of the driver's uncached and write-combined heaps", StringComparison.Ordinal));
        Assert.Contains(found.Lines, line => line.StartsWith("  WandererStaffHeld.ao in the world: ", StringComparison.Ordinal) && line.Contains(", 1 arrays kept", StringComparison.Ordinal));
    }

    /// <summary>
    /// The watch in the world read back: a hand walking and turning, the lamp swinging on it as a damped pendulum - twenty degrees, a period of
    /// one second, a damping ratio of a tenth - in the world array and in a lone place, each taken back into the staff's frame by the World read
    /// with it; both come back to the damping, the file holds it all, and a file of v0.1.404 still reads.
    /// </summary>
    [Fact]
    public void ALAMPSwingingOnAWalkingHandIsMeasuredInThePiecesFrame()
    {
        const double Zeta = 0.1, Natural = 2 * Math.PI / 1.0, Amplitude = 20;
        double damped = Natural * Math.Sqrt(1 - (Zeta * Zeta));
        PoseRig rig = PoseHuntTests.Rig("WandererStaffHeld.ao", PoseHuntTests.Staff);
        int size = ((rig.Count - 1) * 64) + 12 + (2 * BoneWatch.SkeletonMargin);
        var place = new BoneWatchSeries.Block("WandererStaffHeld.ao's World", 0x4_0000_0000, 64, []);
        var array = new BoneWatchSeries.Block("skeleton 1", 0x4_1000_0000, size, []);
        var lone = new BoneWatchSeries.Block("places in the world", 0x4_2000_0000, (2 * BoneWatch.CandidateMargin) + 12, []);
        var his = new BoneWatchSeries.Block("his place", 0x4_3000_0000, 12, []);
        for (var read = 0; read < 360; read++)
        {
            double at = read / 30.0;
            Matrix4x4 world = Matrix4x4.CreateScale(0.925f) * Matrix4x4.CreateRotationZ((float)(0.5 + (0.3 * Math.Sin(at)))) * Matrix4x4.CreateTranslation(6120f + (float)(250 * at), 13216.3f, -78.2f);
            float angle = (float)(Amplitude * Math.Exp(-Zeta * Natural * at) * Math.Cos(damped * at) * Math.PI / 180);
            Matrix4x4 swing = Matrix4x4.CreateRotationX(angle);

            byte[] matrix = new byte[64];
            PoseHuntTests.WriteMatrix(matrix, 0, world);
            place.Reads.Add((at, matrix));
            byte[] bytes = new byte[size];
            WriteWorld(bytes, BoneWatch.SkeletonMargin - 48, rig, swing, world);
            array.Reads.Add((at, bytes));
            byte[] state = new byte[lone.Size];
            WriteVector(state, BoneWatch.CandidateMargin, Vector3.Transform(Lamp(rig, swing), world));
            lone.Reads.Add((at, state));
            byte[] where = new byte[12];
            WriteVector(where, 0, new Vector3(world.M41, world.M42, world.M43));
            his.Reads.Add((at, where));
        }

        var series = new BoneWatchSeries(
            [place, array, lone, his],
            ["bone watch: a test"],
            [new BoneWatchSeries.Skeleton(1, rig, PoseFrame.World, PoseLayout.Packed, 64, BoneWatch.SkeletonMargin, 18, 18, 12) { Place = 0 }],
            3,
            [new BoneWatchSeries.Place(0, 0, rig)],
            [new BoneWatchSeries.Candidate(2, BoneWatch.CandidateMargin, PoseLayout.Packed, 0, 2, 1)]);
        byte[] file = series.ToBytes();
        BoneWatchSeries? back = BoneWatchSeries.Read(file);
        Assert.NotNull(back);
        Assert.Equal(0, Assert.Single(back.Skeletons).Place);
        Assert.Equal(series.Candidates, back.Candidates);
        Assert.Equal(rig.Names, Assert.Single(back.Places).Rig.Names);

        IReadOnlyList<string> lines = back.Describe();
        Assert.Contains(lines, line => line.StartsWith("--- skeleton 1: WandererStaffHeld.ao's rig (", StringComparison.Ordinal) && line.Contains("World frame", StringComparison.Ordinal));
        Assert.Contains("  taken into the piece's frame by its placement's World, read with it at each sweep - and read there by places alone", lines);
        Assert.StartsWith("  moved over the watch: 1 of 19 bones: phys_tri_lamp_jntBnd (", lines.Single(line => line.StartsWith("  moved over the watch:", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.InRange(Damping(lines.Single(line => line.StartsWith("  phys_tri_lamp_jntBnd on base_lamp_jntBnd by its place:", StringComparison.Ordinal))), Zeta - 0.01, Zeta + 0.01);

        // THE LONE PLACE: taken for the lamp on its parent, swinging, its own damping and a table over time.
        Assert.Contains("1 places watched: 1 taken for a simulated bone, 1 of them swinging; 0 at a fixed bone's rest - 1 following their piece, 0 left behind, 0 moving otherwise, 0 beside a piece that held still, 0 not read with their piece's World", lines);
        Assert.Contains(lines, line => line.StartsWith("  FOLLOWS ITS PIECE: 0x420000020 side by side, WandererStaffHeld.ao: taken for phys_tri_lamp_jntBnd on base_lamp_jntBnd - its distance 1..1 of the rest's", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("--- WandererStaffHeld.ao's World at 0x400000000: 360 reads over 12.0 s - its root went ", StringComparison.Ordinal) && line.EndsWith(": it moved; 1 places near it watched - 1 following it, 0 left behind, 0 moving otherwise", StringComparison.Ordinal));
        Assert.InRange(Damping(lines.Single(line => line.StartsWith("  phys_tri_lamp_jntBnd on base_lamp_jntBnd by its place in the world:", StringComparison.Ordinal))), Zeta - 0.01, Zeta + 0.01);
        Assert.Contains("  the swinging places over time:", lines);
        Assert.Contains("  t\this speed\tphys_tri_lamp_jntBnd place in the world", lines);

        // A FILE OF v0.1.404: the same bytes up to the skeletons, under its own magic - no places, no candidates, the skeleton its own.
        byte[] two = file[..^(4 + Place(rig) + 4 + 4 + 24)];
        "POEFBONES2"u8.CopyTo(two);
        BoneWatchSeries? old = BoneWatchSeries.Read(two);
        Assert.NotNull(old);
        Assert.Empty(old.Places);
        Assert.Empty(old.Candidates);
        Assert.Equal(-1, Assert.Single(old.Skeletons).Place);
    }

    /// <summary>A skeleton that held its rest throughout is said in one line, its bones not set out one by one.</summary>
    [Fact]
    public void ACOPYOfTheRestIsSaidInOneLine()
    {
        PoseRig rig = PoseHuntTests.Rig("WandererStaffHeld.ao", PoseHuntTests.Staff);
        int size = ((rig.Count - 1) * 64) + 12 + (2 * BoneWatch.SkeletonMargin);
        var block = new BoneWatchSeries.Block("skeleton 1", 0x5_0000_0000, size, []);
        byte[] still = new byte[size];
        WriteWorld(still, BoneWatch.SkeletonMargin - 48, rig, Matrix4x4.Identity, Matrix4x4.Identity);
        for (var read = 0; read < 30; read++)
        {
            block.Reads.Add((read / 30.0, still));
        }

        var series = new BoneWatchSeries([block], ["bone watch: a test"], [new BoneWatchSeries.Skeleton(0, rig, PoseFrame.Model, PoseLayout.Packed, 64, BoneWatch.SkeletonMargin, 18, 18, 12)]);
        IReadOnlyList<string> lines = series.Describe();
        Assert.Contains("  moved over the watch: 0 of 19 bones - a copy of the rest, or a pose that held still: its bones are not set out one by one", lines);
        Assert.DoesNotContain(lines, line => line.Contains(" by its place:", StringComparison.Ordinal));
        Assert.Contains("(no piece's place in the world was watched)", lines);
    }

    /// <summary>
    /// The search's two reads (PoseHunt.Changing, v0.1.409): a span that changed between them is moving, one that did not is still, one that
    /// does not read counts as still - and with no wait nothing is read at all.
    /// </summary>
    [Fact]
    public void ALIVESpanIsToldFromAStillOneByReadingItTwice()
    {
        var memory = new FakeMemoryReader().Place(Heap, new byte[0x100]);
        var reader = new Ticking(memory, Heap + 0x10);
        (ulong At, int Bytes)[] spans = [(Heap, 0x20), (Heap + 0x40, 0x20), (Slow, 0x20)];

        Assert.Equal([true, false, false], PoseHunt.Changing(reader, spans, TimeSpan.FromMilliseconds(1)));
        long reads = memory.Reads;
        Assert.Equal([false, false, false], PoseHunt.Changing(reader, spans, TimeSpan.Zero));
        Assert.Equal(reads, memory.Reads);
    }

    /// <summary>
    /// Two arrays of the staff in the world, every fixed bone at its rest in both: a copy of the rest beside the anchor, and farther off one
    /// whose lamp moves between the search's two reads - the live one is kept first, and both are said to be what they are. The 06:00
    /// capture of 2026-11-01 kept eight copies of a rest before anything live (v0.1.409).
    /// </summary>
    [Fact]
    public void ALIVEArrayInTheWorldIsKeptBeforeACopyOfTheRest()
    {
        PoseRig rig = PoseHuntTests.Rig("WandererStaffHeld.ao", PoseHuntTests.Staff);
        byte[] heap = PoseHuntTests.Noise(HeapBytes, 11);
        PoseHuntTests.WriteMatrix(heap, 0x1000, World);
        WriteWorld(heap, 0x2000, rig, Matrix4x4.Identity, World);
        WriteWorld(heap, 0x6000, rig, Matrix4x4.CreateRotationX(0.4f), World);
        var reader = new Ticking(new FakeMemoryReader().Place(Heap, heap), Heap + 0x6000 + (2 * 64) + 48);

        PoseHuntResult found = PoseHunt.Find(
            reader, new PoseHuntTests.Space(new MemoryRegion(Heap, HeapBytes)), [rig], Heap, null, [new PiecePlace(0, Heap + 0x1000, World)], TimeSpan.FromMilliseconds(1));

        PoseArray[] world = [.. found.Arrays.Where(one => one.Frame == PoseFrame.World)];
        Assert.Equal([Heap + 0x6000 + 48, Heap + 0x2000 + 48], world.Select(one => one.First));
        Assert.Equal([true, false], world.Select(one => one.Moving));
        Assert.Contains(found.Lines, line => line.StartsWith("  WandererStaffHeld.ao in the world: ", StringComparison.Ordinal) && line.Contains(", 2 arrays kept (1 moving)", StringComparison.Ordinal));
    }

    /// <summary>
    /// A rig of one fixed bone - the 06:00 capture's cape, its root at the identity and three simulated bones - makes no array in the world: one
    /// bone agreeing made an "array" at every stride from each copy of its World's place. The copies are still kept, each alone (v0.1.409).
    /// </summary>
    [Fact]
    public void ARIGOfFewerThanThreeFixedBonesMakesNoArrayInTheWorld()
    {
        PoseRig cape = PoseHuntTests.Rig(
            "CapeAttach.ao",
            [
                ("root_jntBnd", -1, Vector3.Zero),
                ("phys_cape_01_jntBnd", 0, new(0f, 12f, -20f)),
                ("phys_cape_02_jntBnd", 1, new(0f, 14f, -42f)),
                ("phys_cape_03_jntBnd", 2, new(0f, 15f, -63f)),
            ]);
        byte[] heap = PoseHuntTests.Noise(HeapBytes, 13);
        PoseHuntTests.WriteMatrix(heap, 0x1000, World);
        var root = new Vector3(World.M41, World.M42, World.M43);
        for (var copy = 0; copy < 4; copy++)
        {
            WriteVector(heap, 0x3000 + (copy * 0x100), root);
        }

        PoseHuntResult found = PoseHunt.Find(
            new FakeMemoryReader().Place(Heap, heap), new PoseHuntTests.Space(new MemoryRegion(Heap, HeapBytes)), [cape], Heap, null, [new PiecePlace(0, Heap + 0x1000, World)], TimeSpan.Zero);

        Assert.DoesNotContain(found.Arrays, one => one.Frame == PoseFrame.World);
        Assert.Equal(4, found.Candidates.Count(one => one.Bone == 0 && one.Parent < 0 && one.At >= Heap + 0x3000 && one.At < Heap + 0x3400));
        Assert.Contains(found.Lines, line => line.StartsWith("  CapeAttach.ao in the world: ", StringComparison.Ordinal) && line.Contains(" - 0 addresses three of them agreed on, 0 arrays kept (0 moving)", StringComparison.Ordinal));
    }

    /// <summary>
    /// The reading of the pieces' places (v0.1.409): he walks a thousand units with the staff while the focus's World holds still - said so, as
    /// the 06:00 capture's focus held its World to the bit - and of the places near them one follows the staff, one is left behind where it
    /// began and one beside the still focus tells nothing.
    /// </summary>
    [Fact]
    public void APLACEThatHeldStillWhileHeWalkedIsSaidSo()
    {
        PoseRig staff = PoseHuntTests.Rig("WandererStaffHeld.ao", PoseHuntTests.Staff);
        PoseRig focus = PoseHuntTests.Rig("BetaDoryaniFocusHeld.ao", PoseHuntTests.Staff);
        var moving = new BoneWatchSeries.Block("WandererStaffHeld.ao's World", 0x6_0000_0000, 64, []);
        var still = new BoneWatchSeries.Block("BetaDoryaniFocusHeld.ao's World", 0x6_1000_0000, 64, []);
        var near = new BoneWatchSeries.Block("places in the world", 0x6_2000_0000, 128, []);
        var his = new BoneWatchSeries.Block("his place", 0x6_3000_0000, 12, []);
        Matrix4x4 held = World * Matrix4x4.CreateTranslation(-300f, 40f, 0f);
        for (var read = 0; read < 120; read++)
        {
            double at = read / 30.0;
            Matrix4x4 world = World * Matrix4x4.CreateTranslation((float)(250 * at), 0f, 0f);
            byte[] matrix = new byte[64];
            PoseHuntTests.WriteMatrix(matrix, 0, world);
            moving.Reads.Add((at, matrix));
            byte[] other = new byte[64];
            PoseHuntTests.WriteMatrix(other, 0, held);
            still.Reads.Add((at, other));
            byte[] places = new byte[128];
            WriteVector(places, 32, new Vector3(world.M41, world.M42, world.M43));
            WriteVector(places, 64, new Vector3(World.M41, World.M42, World.M43));
            WriteVector(places, 96, new Vector3(held.M41, held.M42, held.M43));
            near.Reads.Add((at, places));
            byte[] where = new byte[12];
            WriteVector(where, 0, new Vector3(world.M41, world.M42, world.M43));
            his.Reads.Add((at, where));
        }

        var series = new BoneWatchSeries(
            [moving, still, near, his],
            ["bone watch: a test"],
            null,
            3,
            [new BoneWatchSeries.Place(0, 0, staff), new BoneWatchSeries.Place(1, 0, focus)],
            [
                new BoneWatchSeries.Candidate(2, 32, PoseLayout.Packed, 0, 0, -1),
                new BoneWatchSeries.Candidate(2, 64, PoseLayout.Packed, 0, 0, -1),
                new BoneWatchSeries.Candidate(2, 96, PoseLayout.Packed, 1, 0, -1),
            ]);
        IReadOnlyList<string> lines = series.Describe();

        Assert.Contains(lines, line => line.StartsWith("he walked 991.7 along his path over 4.0 s", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("--- WandererStaffHeld.ao's World at 0x600000000: 120 reads over 4.0 s - its root went 991.7 along its path and 991.7 at most from where it began, turned up to 0 degrees", StringComparison.Ordinal) && line.EndsWith(": it moved; 2 places near it watched - 1 following it, 1 left behind, 0 moving otherwise", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("--- BetaDoryaniFocusHeld.ao's World at 0x610000000: 120 reads over 4.0 s - its root went 0 along its path", StringComparison.Ordinal) && line.Contains(": STILL WHILE HE WALKED - not the piece's live placement", StringComparison.Ordinal) && line.EndsWith("; 1 places near it watched - nothing told of them, their piece held still", StringComparison.Ordinal));
        Assert.Contains("3 places watched: 0 taken for a simulated bone, 0 of them swinging; 3 at a fixed bone's rest - 1 following their piece, 1 left behind, 0 moving otherwise, 1 beside a piece that held still, 0 not read with their piece's World", lines);
        string[] heads = [.. lines.Where(line => line.Contains(" side by side, ", StringComparison.Ordinal)).Select(line => line[..line.IndexOf(" side by side", StringComparison.Ordinal)])];
        Assert.Equal(["  FOLLOWS ITS PIECE: 0x620000020", "  left behind: 0x620000040", "  its piece still: 0x620000060"], heads);
    }

    /// <summary>Memory one place of which changes at every read covering it - a live pose - over a fake process.</summary>
    private sealed class Ticking(FakeMemoryReader memory, ulong live) : IMemoryReader
    {
        private float _tick = 1000f;

        public bool IsAttached => true;

        public int ProcessId => memory.ProcessId;

        public ulong ModuleBase => memory.ModuleBase;

        public uint ModuleSize => memory.ModuleSize;

        public bool TryRead(ulong address, Span<byte> destination)
        {
            if (!memory.TryRead(address, destination))
            {
                return false;
            }

            if (live >= address && live + 4 <= address + (ulong)destination.Length)
            {
                BitConverter.TryWriteBytes(destination[(int)(live - address)..], _tick++);
            }

            return true;
        }

        public void Dispose()
        {
        }
    }

    /// <summary>The bytes a place takes in the file - its block, the matrix's offset and its rig whole.</summary>
    private static int Place(PoseRig rig)
    {
        int bytes = 4 + 4 + Text(rig.Piece) + Text(rig.Path) + 4;
        for (var bone = 0; bone < rig.Count; bone++)
        {
            bytes += Text(rig.Names[bone]) + 4 + 64 + 64;
        }

        return bytes;
    }

    /// <summary>A string's bytes as BinaryWriter writes it: its length in sevens, then its UTF-8.</summary>
    private static int Text(string text)
    {
        int length = System.Text.Encoding.UTF8.GetByteCount(text);
        int prefix = 1;
        for (int left = length >> 7; left > 0; left >>= 7)
        {
            prefix++;
        }

        return prefix + length;
    }

    /// <summary>The damping ratio a swing's line says.</summary>
    private static double Damping(string line)
        => double.Parse(DampingRatio().Match(line).Groups[1].Value, CultureInfo.InvariantCulture);

    [GeneratedRegex(@"damping ratio (\d+\.\d+)")]
    private static partial Regex DampingRatio();

    /// <summary>The lamp's place in the staff's frame, swung on its parent.</summary>
    private static Vector3 Lamp(PoseRig rig, Matrix4x4 swing)
    {
        Vector3 over = rig.Model[1].Translation;
        return Vector3.Transform(rig.Model[2].Translation - over, swing) + over;
    }

    /// <summary>A rig's pose in the world as 4x4 matrices by rows, 64 bytes a bone: each bone's model rest - the lamp swung on its parent - through the World.</summary>
    private static void WriteWorld(byte[] bytes, int at, PoseRig rig, Matrix4x4 swing, Matrix4x4 world)
    {
        for (var bone = 0; bone < rig.Count; bone++)
        {
            Matrix4x4 model = rig.Model[bone];
            if (rig.Simulated(bone))
            {
                Vector3 over = rig.Model[rig.Parents[bone]].Translation;
                model = Matrix4x4.CreateTranslation(rig.Model[bone].Translation) * Matrix4x4.CreateTranslation(-over) * swing * Matrix4x4.CreateTranslation(over);
            }

            PoseHuntTests.WriteMatrix(bytes, at + (bone * 64), model * world);
        }
    }

    private static void WriteVector(byte[] bytes, int at, Vector3 vector)
    {
        BitConverter.TryWriteBytes(bytes.AsSpan(at, 4), vector.X);
        BitConverter.TryWriteBytes(bytes.AsSpan(at + 4, 4), vector.Y);
        BitConverter.TryWriteBytes(bytes.AsSpan(at + 8, 4), vector.Z);
    }
}
