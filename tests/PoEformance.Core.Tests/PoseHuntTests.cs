using System.Numerics;
using System.Text;
using PoEformance.Core.Memory;
using PoEformance.Features;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The bone watch's search for each piece's skeleton by its rest (PoseHunt, v0.1.404) and its reading of a simulated bone's swing on its
/// parent (PoseSwing): an array of the Exile's Staff's 19 bones found in the model's frame among noise, an animated body's in its parents'
/// frames however its bones turn, and a lantern swinging as a damped pendulum measured back to its period and damping ratio.
/// </summary>
public sealed class PoseHuntTests
{
    private const ulong Heap = 0x2_0000_0000;
    private const int HeapBytes = 0x8000;

    /// <summary>The staff's rig as v0.1.401's capture wrote it: its model rests, every bone's turn the identity.</summary>
    internal static readonly (string Name, int Parent, Vector3 At)[] Staff =
    [
        ("root_jntBnd", -1, Vector3.Zero),
        ("base_lamp_jntBnd", 0, new(-14.542f, -0.024f, -141.029f)),
        ("phys_tri_lamp_jntBnd", 1, new(-16.321f, 6.505f, -118.003f)),
        ("aux_blade_1_jntBnd", 0, new(-2.75f, 0.5f, -121.714f)),
        ("aux_blade_2_jntBnd", 0, new(-7.829f, 0f, -131.047f)),
        ("aux_blade_3_jntBnd", 0, new(-13.148f, -1.25f, -135.211f)),
        ("aux_blade_4_jntBnd", 0, new(-20.75f, 5.5f, -145.88f)),
        ("aux_mid_jntBnd", 0, new(-2.75f, 0.5f, -123.464f)),
        ("aux_handle_1_jntBnd", 0, new(0f, 0.75f, 49.536f)),
        ("aux_handle_2_jntBnd", 0, new(1f, -1.5f, 25.62f)),
        ("aux_handle_3_jntBnd", 0, new(0f, 0.5f, -1.63f)),
        ("aux_handle_4_jntBnd", 0, new(2.5f, 2.5f, -25.38f)),
        ("aux_handle_5_jntBnd", 0, new(0.75f, 0f, -51.88f)),
        ("aux_handle_6_jntBnd", 0, new(1.5f, -2.5f, -88.13f)),
        ("aux_handle_7_jntBnd", 0, new(4.25f, -4f, -100.964f)),
        ("aux_blade_5_jntBnd", 0, new(4.25f, -1.5f, -126.464f)),
        ("aux_blade_6_jntBnd", 0, new(10.182f, -10.498f, -140.439f)),
        ("aux_blade_cyl_1_jntBnd", 0, new(-2.75f, -2.25f, -121.714f)),
        ("aux_handle_1_jntBnd1", 0, new(0f, -1f, 49.536f)),
    ];

    /// <summary>
    /// The staff's pose written as 4x4 matrices by rows in its model's frame, 64 bytes a bone, among floats of every size - and a second
    /// copy of three of its bones alone: the array is found where bone nought's x lies, in the model's frame, side by side, every fixed bone
    /// at its rest; the three bones alone, which no array of the rig holds, are not.
    /// </summary>
    [Fact]
    public void ASTAFFSPoseIsFoundByItsRestAmongNoise()
    {
        PoseRig rig = Rig("WandererStaffHeld.ao", Staff);
        byte[] heap = Noise(HeapBytes, 7);
        const int At = 0x2310;
        WriteModel(heap, At, rig, lamp: Matrix4x4.CreateRotationX(0.3f));

        // THREE OF ITS BONES ALONE, as a lone copy of a few places would stand.
        for (var bone = 3; bone < 6; bone++)
        {
            WriteMatrix(heap, 0x6000 + ((bone - 3) * 64), Matrix4x4.CreateTranslation(rig.Model[bone].Translation));
        }

        var memory = new FakeMemoryReader().Place(Heap, heap);
        PoseHuntResult found = PoseHunt.Find(memory, new Space(new MemoryRegion(Heap, HeapBytes)), [rig], Heap, null);

        PoseArray array = Assert.Single(found.Arrays);
        Assert.Equal(PoseFrame.Model, array.Frame);
        Assert.Equal(PoseLayout.Packed, array.Layout);
        Assert.Equal(64, array.Stride);
        Assert.Equal(Heap + At + 48, array.First);
        Assert.Equal(array.Fixed, array.Matched);
        Assert.Equal(18, array.Fixed);
        Assert.InRange(array.Stray, 6f, 8f);
        Assert.Contains("the sweep:", found.Lines[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// A body walking: every bone turned anew at each frame, its translations in its parents' frames as the file wrote them, written as
    /// 3x4 matrices by rows (the translation each row's last float) 48 bytes a bone - found in its parents' frames, sixteen bytes apart.
    /// </summary>
    [Fact]
    public void ANANIMATEDBodysPoseIsFoundInItsParentsFrames()
    {
        var bones = new (string Name, int Parent, Vector3 At)[24];
        bones[0] = ("root_jntBnd", -1, Vector3.Zero);
        for (var bone = 1; bone < bones.Length; bone++)
        {
            bones[bone] = ($"bone_{bone}_jntBnd", bone - 1, new Vector3(3.5f + bone, -2.25f * bone, 11.75f - (0.5f * bone)));
        }

        PoseRig rig = Rig("Body.ao", bones, local: true);
        byte[] heap = Noise(HeapBytes, 11);
        const int At = 0x1A00;
        for (var bone = 0; bone < rig.Count; bone++)
        {
            Matrix4x4 turn = Matrix4x4.CreateFromYawPitchRoll(0.1f * bone, -0.07f * bone, 0.3f);
            Vector3 place = rig.Local[bone].Translation;
            float[] row =
            [
                turn.M11, turn.M12, turn.M13, place.X, turn.M21, turn.M22, turn.M23, place.Y, turn.M31, turn.M32, turn.M33, place.Z,
            ];
            for (var one = 0; one < row.Length; one++)
            {
                BitConverter.GetBytes(row[one]).CopyTo(heap, At + (bone * 48) + (one * 4));
            }
        }

        var memory = new FakeMemoryReader().Place(Heap, heap);
        PoseHuntResult found = PoseHunt.Find(memory, new Space(new MemoryRegion(Heap, HeapBytes)), [rig], Heap, null);
        PoseArray array = Assert.Single(found.Arrays, one => one.Frame == PoseFrame.Local);
        Assert.Equal(PoseLayout.Columns, array.Layout);
        Assert.Equal(48, array.Stride);
        Assert.Equal(Heap + At + 12, array.First);
        Assert.Equal(rig.Count, array.Matched);
    }

    /// <summary>
    /// A lantern swinging on its staff as a damped pendulum - twenty degrees, a period of one second, a damping ratio of a tenth - watched
    /// in the model's frame: its records keep their turns as rows before x, it alone moved, and both its place and its turn on its parent
    /// come back to the period and the damping ratio; the whole reading survives the file and is written.
    /// </summary>
    [Fact]
    public void ADAMPEDPendulumIsMeasuredBackToItsPeriodAndDamping()
    {
        const double Zeta = 0.1, Natural = 2 * Math.PI / 1.0, Amplitude = 20;
        double damped = Natural * Math.Sqrt(1 - (Zeta * Zeta));
        PoseRig rig = Rig("WandererStaffHeld.ao", Staff);
        int size = ((rig.Count - 1) * 64) + 12 + (2 * BoneWatch.SkeletonMargin);
        var block = new BoneWatchSeries.Block("skeleton 1", 0x3_0000_0000, size, []);
        var place = new BoneWatchSeries.Block("his place", 0x3_1000_0000, 12, []);
        for (var read = 0; read < 360; read++)
        {
            double at = read / 30.0;
            float angle = (float)(Amplitude * Math.Exp(-Zeta * Natural * at) * Math.Cos(damped * at) * Math.PI / 180);
            byte[] bytes = new byte[size];
            WriteModel(bytes, BoneWatch.SkeletonMargin - 48, rig, lamp: Matrix4x4.CreateRotationX(angle));
            block.Reads.Add((at, bytes));
            byte[] where = new byte[12];
            BitConverter.GetBytes((float)(300 * at)).CopyTo(where, 0);
            place.Reads.Add((at, where));
        }

        var skeleton = new BoneWatchSeries.Skeleton(0, rig, PoseFrame.Model, PoseLayout.Packed, 64, BoneWatch.SkeletonMargin, 18, 18, 12);
        TurnRead how = PoseSwing.Turns(block, skeleton);
        Assert.Equal(TurnKind.RowsBefore48, how.Kind);
        Assert.False(how.Flip);
        Assert.True(how.Agreement < 0.01, $"the fixed bones' turns lie {how.Agreement} degrees from their rest");

        IReadOnlyList<BoneSwing> swings = PoseSwing.Swings(block, skeleton, how);
        Assert.Equal(["place", "turn"], swings.Select(one => one.By));
        foreach (BoneSwing swing in swings)
        {
            Assert.Equal("phys_tri_lamp_jntBnd", swing.Bone);
            Assert.Equal("base_lamp_jntBnd", swing.Parent);
            Assert.InRange(swing.Signed.Select(MathF.Abs).Max(), 19.9f, 20.05f);
            Assert.InRange(MathF.Abs(swing.Axis.X), 0.99f, 1.001f);
            Assert.InRange(swing.PeriodByPeaks, (2 * Math.PI / damped) - 0.04, (2 * Math.PI / damped) + 0.04);
            Assert.InRange(swing.DampingRatio, Zeta - 0.01, Zeta + 0.01);
            Assert.True(swing.Extrema.Count >= 10, $"{swing.Extrema.Count} turning points");
        }

        // A PENDULUM KEEPS ITS LENGTH: the lamp's distance from its parent stays its rest's.
        Assert.InRange(swings[0].LeastRatio, 0.9999f, 1.0001f);
        Assert.InRange(swings[0].MostRatio, 0.9999f, 1.0001f);

        // THE FILE: the skeleton, its rig whole and the player's place come back, and the reading writes them.
        var series = new BoneWatchSeries([block, place], ["bone watch: a test"], [skeleton], 1);
        BoneWatchSeries? back = BoneWatchSeries.Read(series.ToBytes());
        Assert.NotNull(back);
        BoneWatchSeries.Skeleton again = Assert.Single(back.Skeletons);
        Assert.Equal(1, back.Player);
        Assert.Equal(rig.Names, again.Rig.Names);
        Assert.Equal(rig.Model[2], again.Rig.Model[2]);
        Assert.Equal(skeleton with { Rig = again.Rig }, again);
        IReadOnlyList<string> lines = back.Describe();
        Assert.Contains(lines, line => line.StartsWith("--- skeleton 1: WandererStaffHeld.ao's rig (", StringComparison.Ordinal) && line.Contains("Model frame", StringComparison.Ordinal));
        Assert.StartsWith("  moved over the watch: 1 of 19 bones: phys_tri_lamp_jntBnd (", lines.Single(line => line.StartsWith("  moved over the watch:", StringComparison.Ordinal)), StringComparison.Ordinal);
        Assert.Contains(lines, line => line.StartsWith("  phys_tri_lamp_jntBnd on base_lamp_jntBnd by its place:", StringComparison.Ordinal) && line.Contains("damping ratio 0.", StringComparison.Ordinal));
        Assert.Contains("  t\this speed\tphys_tri_lamp_jntBnd place\tphys_tri_lamp_jntBnd turn", lines);
        Assert.Contains(lines, line => line.StartsWith("  ", StringComparison.Ordinal) && line.Split('\t') is [_, "300", _, _]);
    }

    /// <summary>A file of v0.1.401 to v0.1.403 - its blocks alone - still reads, with no skeleton and no place of his.</summary>
    [Fact]
    public void AFILEOfTheFirstVersionStillReads()
    {
        using var stream = new MemoryStream();
        using (var write = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            write.Write("POEFBONES1"u8.ToArray());
            write.Write(1);
            write.Write("bone watch: old");
            write.Write(1);
            write.Write("block");
            write.Write(0x1000UL);
            write.Write(4);
            write.Write(1);
            write.Write(0.5);
            write.Write(new byte[] { 1, 2, 3, 4 });
        }

        BoneWatchSeries? back = BoneWatchSeries.Read(stream.ToArray());
        Assert.NotNull(back);
        Assert.Empty(back.Skeletons);
        Assert.Equal(-1, back.Player);
        Assert.Equal([1, 2, 3, 4], Assert.Single(back.Blocks).Reads[0].Bytes);
    }

    /// <summary>
    /// A piece's rigs from the install: its own first, then those of the pieces it hangs off itself, each rig once - a piece with none
    /// left out.
    /// </summary>
    [Fact]
    public void APIECESRigsAreItsOwnAndThoseOfWhatItHangsOffItself()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["staff/rig.ast"] = Packed.Skeleton(
            [("root_jntBnd", 255, 1, 0f), ("phys_tri_lamp_jntBnd", 255, 255, -100f)],
            [("idle_01", string.Empty, 2, 30, 0x6f, 0, 0)]);
        install.Files["staff/Staff.ao"] = Encoding.UTF8.GetBytes(
            "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"staff/rig.ast\"\n\t}\n"
            + "\tAttachedAnimatedObject\n\t{\n\t\tattached_object = \"root_jntBnd staff/Dangle.ao\"\n\t\tattached_object = \"root_jntBnd staff/Plain.ao\"\n\t}\n}\n");
        install.Files["staff/Dangle.ao"] = Encoding.UTF8.GetBytes(
            "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t}\n}\n");
        install.Files["staff/Plain.ao"] = Encoding.UTF8.GetBytes("version 3\nclient\n{\n}\n");

        IReadOnlyList<PoseRig> rigs = PieceRigs.Of(install.Read, "staff/Staff.ao");
        Assert.Equal(["staff/rig.ast", "art/rig.ast"], rigs.Select(one => one.Path));
        Assert.Equal("staff/Staff.ao", rigs[0].Piece);
        Assert.Equal(1, rigs[0].SimulatedCount);
        Assert.True(rigs[0].Moved(1));
        Assert.False(rigs[0].Moved(0));
        Assert.Equal("staff/Dangle.ao", rigs[1].Piece);
    }

    /// <summary>A rig from bones' model rests (or, with <paramref name="local"/>, their rests in their parents' frames), every turn the identity.</summary>
    internal static PoseRig Rig(string piece, (string Name, int Parent, Vector3 At)[] bones, bool local = false)
    {
        var model = new Matrix4x4[bones.Length];
        var locals = new Matrix4x4[bones.Length];
        for (var bone = 0; bone < bones.Length; bone++)
        {
            (_, int parent, Vector3 rest) = bones[bone];
            Vector3 over = parent < 0 ? Vector3.Zero : model[parent].Translation;
            model[bone] = Matrix4x4.CreateTranslation(local ? over + rest : rest);
            locals[bone] = Matrix4x4.CreateTranslation(model[bone].Translation - over);
        }

        return new PoseRig(piece, "art/" + piece + ".ast", [.. bones.Select(one => one.Name)], [.. bones.Select(one => one.Parent)], locals, model);
    }

    /// <summary>A rig's pose in its model's frame as 4x4 matrices by rows, 64 bytes a bone from <paramref name="at"/> - the lamp turned about its parent by <paramref name="lamp"/>.</summary>
    private static void WriteModel(byte[] bytes, int at, PoseRig rig, Matrix4x4 lamp)
    {
        for (var bone = 0; bone < rig.Count; bone++)
        {
            Matrix4x4 model = rig.Model[bone];
            if (rig.Simulated(bone))
            {
                // ON ITS PARENT: the parent's place, the rest's way from it turned by the swing.
                Vector3 over = rig.Model[rig.Parents[bone]].Translation;
                model = Matrix4x4.CreateTranslation(-over) * lamp * Matrix4x4.CreateTranslation(over);
                model = Matrix4x4.CreateTranslation(rig.Model[bone].Translation) * model;
            }

            WriteMatrix(bytes, at + (bone * 64), model);
        }
    }

    /// <summary>A matrix's sixteen floats by rows at <paramref name="at"/>.</summary>
    internal static void WriteMatrix(byte[] bytes, int at, Matrix4x4 matrix)
    {
        float[] values =
        [
            matrix.M11, matrix.M12, matrix.M13, matrix.M14, matrix.M21, matrix.M22, matrix.M23, matrix.M24,
            matrix.M31, matrix.M32, matrix.M33, matrix.M34, matrix.M41, matrix.M42, matrix.M43, matrix.M44,
        ];
        for (var one = 0; one < values.Length; one++)
        {
            BitConverter.GetBytes(values[one]).CopyTo(bytes, at + (one * 4));
        }
    }

    /// <summary>Floats of every size from a seed, as a heap holds them - never a rest of the rigs here.</summary>
    internal static byte[] Noise(int length, int seed)
    {
        var random = new Random(seed);
        byte[] bytes = new byte[length];
        for (var at = 0; at + 4 <= length; at += 4)
        {
            float value = (float)((random.NextDouble() - 0.5) * Math.Pow(10, random.Next(-3, 5)));
            BitConverter.GetBytes(value).CopyTo(bytes, at);
        }

        return bytes;
    }

    internal sealed class Space(params MemoryRegion[] regions) : IMemoryRegions
    {
        public IEnumerable<MemoryRegion> Regions() => regions;
    }
}
