using System.Buffers.Binary;
using System.Numerics;
using PoEformance.Core.Diagnostics;
using PoEformance.Core.Memory;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The particle emitters' hunt (particle-emitters.txt): a ParticleEmitterData record is found by how its fields agree wherever it lies, and
/// read as the shaders read it - CommonDeclarations.inc's layout, HalfUtility's UnpackHalf2, gpuparticles.ffx's bit fields.
/// </summary>
public sealed class ParticleEmitterHuntTests
{
    private const ulong Heap = 0x2000_0000;

    [Fact]
    public void ARECORDIsFoundByHowItsFieldsAgreeAndNothingElseIs()
    {
        byte[] heap = new byte[64 * 1024];
        var random = new Random(7);
        for (var at = 0; at < heap.Length; at += 4)
        {
            BinaryPrimitives.WriteSingleLittleEndian(heap.AsSpan(at), (float)(random.NextDouble() * 4 - 2));
        }

        // A BLOCK OF ONES with an identity quaternion at the origin passes every test but the origin's; a record at a place four bytes off
        // a sixteen keeps its place; a second record 160 bytes on makes a run.
        for (var at = 0x1000; at < 0x1000 + 0xA0; at += 4)
        {
            BinaryPrimitives.WriteSingleLittleEndian(heap.AsSpan(at), 1f);
        }

        foreach (int quaternion in new[] { 0x1040, 0x1050 })
        {
            BinaryPrimitives.WriteSingleLittleEndian(heap.AsSpan(quaternion), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(heap.AsSpan(quaternion + 4), 0f);
            BinaryPrimitives.WriteSingleLittleEndian(heap.AsSpan(quaternion + 8), 0f);
        }

        for (var at = 0x1060; at < 0x1080; at += 4)
        {
            BinaryPrimitives.WriteSingleLittleEndian(heap.AsSpan(at), 0f);
        }

        Record(0.925f, new Vector3(11469, 10838, -88), 12.5f).CopyTo(heap, 0x3004);
        Record(1f, new Vector3(11400, 10800, -150), 3f).CopyTo(heap, 0x3004 + 0xA0);
        var memory = new FakeMemoryReader().Place(Heap, heap);
        var space = new PoseHuntTests.Space(new MemoryRegion(Heap, (ulong)heap.Length));

        FloatHuntResult result = FloatHunt.Scan(memory, space, [new FloatNeedle("record", [], 0f, Most: 64)], ParticleEmitterHunt.Sweep, ParticleEmitterHunt.RecordBytes);

        Assert.Equal([Heap + 0x3004, Heap + 0x3004 + 0xA0], result.Sightings.Select(one => one.At).Order());
        Assert.All(result.Sightings, one => Assert.Equal(0, one.Needle));
    }

    [Fact]
    public void ARECORDIsReadAsTheShadersReadIt()
    {
        byte[] bytes = Record(0.925f, new Vector3(1, 2, 3), 7.25f);
        ParticleEmitterHunt.Record record = ParticleEmitterHunt.Decode(bytes);

        Assert.Equal(0.925f, record.Scale.X);
        Assert.Equal(1f / 0.925f, record.InverseScale.Y);
        Assert.Equal(new Vector3(1, 2, 3), record.Translation);
        Assert.Equal(7.25f, record.Time);
        Assert.Equal(7.25f - 0.01f, record.PrevTime);
        Assert.Equal(new Vector2(1f, 0.5f), record.Speed);
        Assert.Equal(new Vector2(2f, 0.25f), record.DeadTime);
        Assert.Equal((37u, 2u), record.Bones);
        Assert.Equal(9u, record.CullTag);
        Assert.Equal(8u, record.GroupSize);
        Assert.Equal(5u, record.LockCondition);
        Assert.Equal(0x23u, record.Flags);
        Assert.Equal("IS_ALIVE IS_INFINITE IS_VISIBLE", ParticleEmitterHunt.Names(record.Flags, ParticleEmitterHunt.EmitterFlags));
        Assert.Equal("TRANSLATION_BONE", ParticleEmitterHunt.Names(record.Locks, ParticleEmitterHunt.LockFlags));
        Assert.Equal(1234u, record.ParticlesStart);
        Assert.Equal(new Vector4(1f, 2f, 0.5f, 0.25f), record.CustomA);
    }

    [Fact]
    public void THEREPLAYDecodesARecordLineAgain()
    {
        byte[] bytes = Record(0.925f, new Vector3(5, 6, 7), 1f);
        List<string> lines = ParticleEmitterHunt.Replay(["noise", $"record\t0x1234\t{Convert.ToHexString(bytes)}"]);

        Assert.Contains(lines, line => line.StartsWith("1 records, 1 read as one", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.Contains("group size 8", StringComparison.Ordinal));
    }

    [Fact]
    public void THEBONESArrayIsWhereTheEmittersRunsOfBonesAgree()
    {
        // THREE EMITTERS with their two bones at 6, 8 and 16, as the staff's lamp: each run w nought then the next bone's w its distance,
        // the array starting at +0x100; a stray run elsewhere that one record alone could vote for; a block of noughts that is no run.
        var place = new Vector3(11439.5f, 10848.4f, -85.8f);
        ParticleEmitterHunt.Record[] voters =
        [
            ParticleEmitterHunt.Decode(Record(0.925f, place, 1f)) with { Bones = (6, 2) },
            ParticleEmitterHunt.Decode(Record(0.925f, place, 1f)) with { Bones = (8, 2) },
            ParticleEmitterHunt.Decode(Record(0.925f, place, 1f)) with { Bones = (16, 2) },
        ];
        byte[] region = new byte[0x2000];
        void Run(int at, Vector3 first, Vector3 second)
        {
            float distance = Vector3.Distance(first, second);
            foreach ((int Offset, Vector3 Pos, float W) bone in new[] { (0, first, 0f), (16, first, 0f), (32, second, distance), (48, second, distance) })
            {
                BinaryPrimitives.WriteSingleLittleEndian(region.AsSpan(at + bone.Offset), bone.Pos.X);
                BinaryPrimitives.WriteSingleLittleEndian(region.AsSpan(at + bone.Offset + 4), bone.Pos.Y);
                BinaryPrimitives.WriteSingleLittleEndian(region.AsSpan(at + bone.Offset + 8), bone.Pos.Z);
                BinaryPrimitives.WriteSingleLittleEndian(region.AsSpan(at + bone.Offset + 12), bone.W);
            }
        }

        var lamp = new Vector3(-16.3f, 6.5f, -118f);
        foreach (int start in new[] { 6, 8, 16 })
        {
            Run(0x100 + (start * ParticleEmitterHunt.BoneBytes), lamp, lamp + new Vector3(0, 0, 10));
        }

        Run(0x1800, lamp, lamp + new Vector3(3, 0, 0));
        var runs = new List<FloatSighting>();
        ParticleEmitterHunt.ChainSweep(region, region.Length, Heap, runs);

        Assert.Equal(4, runs.Count);
        (ulong first, uint agreed) = ParticleEmitterHunt.Votes(runs.Select(one => one.At), voters)[0];
        Assert.Equal(Heap + 0x100, first);
        Assert.Equal(0b111u, agreed);
        List<string> said = ParticleEmitterHunt.SaidBones(region.AsSpan(0x100), voters);
        Assert.All(said, line => Assert.DoesNotContain("NO RUN STARTS THERE", line, StringComparison.Ordinal));
        Assert.Contains("[1] (-16.3, 6.5, -108) w 10", said[0], StringComparison.Ordinal);
    }

    /// <summary>A record as the engine lays it out, with the scale <paramref name="scale"/> and a turn about z.</summary>
    private static byte[] Record(float scale, Vector3 translation, float time)
    {
        byte[] bytes = new byte[ParticleEmitterHunt.RecordBytes];
        void F(int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), value);
        void U(int at, uint value) => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(at), value);
        uint H2(float low, float high) => BitConverter.HalfToUInt16Bits((Half)low) | ((uint)BitConverter.HalfToUInt16Bits((Half)high) << 16);
        Quaternion turn = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.7f);
        for (var axis = 0; axis < 3; axis++)
        {
            F(0x00 + (axis * 4), scale);
            F(0x10 + (axis * 4), scale);
            F(0x20 + (axis * 4), 1f / scale);
            F(0x30 + (axis * 4), 1f / scale);
        }

        F(0x0C, 2.5f);
        U(0x1C, H2(2f, 0.25f));
        U(0x2C, 0x0001_0000);
        U(0x3C, H2(1f, 0.5f));
        foreach (int at in new[] { 0x40, 0x50 })
        {
            F(at, turn.X);
            F(at + 4, turn.Y);
            F(at + 8, turn.Z);
            F(at + 12, turn.W);
        }

        F(0x60, translation.X);
        F(0x64, translation.Y);
        F(0x68, translation.Z);
        F(0x6C, time);
        F(0x70, translation.X);
        F(0x74, translation.Y);
        F(0x78, translation.Z);
        F(0x7C, time - 0.01f);
        U(0x80, H2(0.5f, 0f));
        U(0x84, (37u << 8) | 2u);
        U(0x88, (0x23u << 14) | (5u << 11) | (7u << 5) | 9u);
        U(0x8C, 1234);
        U(0x90, H2(1f, 2f));
        U(0x94, H2(0.5f, 0.25f));
        return bytes;
    }
}
