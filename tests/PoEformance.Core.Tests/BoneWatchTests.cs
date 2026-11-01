using System.Numerics;
using PoEformance.Core.Schema;
using PoEformance.Game.Diagnostics;

namespace PoEformance.Core.Tests;

/// <summary>
/// The bone watch's reading (BoneWatchSeries): turn matrices told from other floats, a skeleton's run of them found at its stride, and each
/// matrix set against the run's first - a weapon's own bones holding still on its root while it is carried about, a simulated bone swinging
/// on it - over reads kept and read back from the file.
/// </summary>
public sealed class BoneWatchTests
{
    /// <summary>
    /// A turn is three rows of one length at right angles, not mirrored - the first three floats of each row of four, or three packed; a
    /// scaled turn is one, a sheared or mirrored one is not.
    /// </summary>
    [Fact]
    public void ATURNIsToldFromOtherFloats()
    {
        byte[] bytes = new byte[64];
        Write(bytes, 0, Matrix4x4.CreateFromYawPitchRoll(0.3f, -0.7f, 1.1f) * Matrix4x4.CreateScale(2f));
        Assert.True(BoneWatchSeries.IsTurn(bytes, 0, 16));

        Write(bytes, 0, Matrix4x4.CreateScale(-1f, 1f, 1f));
        Assert.False(BoneWatchSeries.IsTurn(bytes, 0, 16));

        Write(bytes, 0, new Matrix4x4(1f, 0.2f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f));
        Assert.False(BoneWatchSeries.IsTurn(bytes, 0, 16));
        Assert.False(BoneWatchSeries.IsTurn(new byte[64], 0, 16));
    }

    /// <summary>
    /// A root carried about and turning, a bone fixed on it, and a bone swinging on it by ten degrees every one and a half seconds: the run of
    /// three is found at its stride; the fixed bone holds still on the first as its own turn sweeps through the root's; the swinging one's
    /// angle on the first comes to its swing (the first read at its middle) and repeats at its period - and the reads come back whole from the file.
    /// </summary>
    [Fact]
    public void ASWINGINGBoneIsToldFromABoneFixedOnItsRoot()
    {
        const int Reads = 300;
        const float Period = 1.5f;
        var block = new BoneWatchSeries.Block("staff +0x40 ->", 0x7000_0000, 0x100, []);
        Matrix4x4 fixedOn = Matrix4x4.CreateFromYawPitchRoll(0.4f, 0.2f, -0.3f) * Matrix4x4.CreateTranslation(0f, 0f, -80f);
        Matrix4x4 lamp = Matrix4x4.CreateTranslation(0f, 0f, -120f);
        for (var read = 0; read < Reads; read++)
        {
            double at = read / 30.0;
            float angle = 10f * MathF.PI / 180f * MathF.Sin(2f * MathF.PI * (float)at / Period);
            Matrix4x4 root = Matrix4x4.CreateRotationZ(0.9f * (float)at) * Matrix4x4.CreateRotationX(0.3f * MathF.Sin((float)at)) * Matrix4x4.CreateTranslation(100f * (float)at, 0f, 0f);
            byte[] bytes = new byte[0x100];
            Write(bytes, 0x10, root);
            Write(bytes, 0x50, fixedOn * root);
            Write(bytes, 0x90, Matrix4x4.CreateRotationX(angle) * lamp * root);
            block.Reads.Add((at, bytes));
        }

        BoneWatchSeries.Run run = Assert.Single(BoneWatchSeries.Runs(block));
        Assert.Equal(new BoneWatchSeries.Run(0x10, 64, 3, 16), run);

        (float[][] against, float[][] own, bool byRows) = BoneWatchSeries.Against(block, run);
        Assert.True(byRows);
        Assert.True(against[1].Max() < 0.05f, $"the fixed bone turns on its root by {against[1].Max()}");
        Assert.True(own[1].Max() > 30f);
        Assert.InRange(against[2].Max(), 9.9f, 10.05f);
        double[] times = [.. block.Reads.Select(one => one.At)];

        // THE SWING SIGNED, along the axis it swings about - its angle alone passes nought twice a period.
        (float[] swing, Vector3 axis) = BoneWatchSeries.Swing(block, run, 2, byRows);
        Assert.InRange(swing.Min(), -10.05f, -9.9f);
        Assert.InRange(swing.Max(), 9.9f, 10.05f);
        Assert.InRange(MathF.Abs(axis.X), 0.999f, 1.001f);
        Assert.InRange(WobbleProbe.Period(times, swing), Period - 0.05, Period + 0.05);

        // THE FILE: the reads back whole, and the reading says the swinging bone's table.
        var series = new BoneWatchSeries([block], ["bone watch: a test"]);
        BoneWatchSeries? back = BoneWatchSeries.Read(series.ToBytes());
        Assert.NotNull(back);
        Assert.Equal(Reads, Assert.Single(back.Blocks).Reads.Count);
        Assert.Equal(block.Reads[^1].Bytes, back.Blocks[0].Reads[^1].Bytes);
        IReadOnlyList<string> lines = back.Describe();
        Assert.Equal("bone watch: a test", lines[0]);
        Assert.Contains(lines, line => line.StartsWith("  run at +0x10: 3 matrices, 64 bytes apart, rows 16 apart - turned on the first as rows", StringComparison.Ordinal));
        Assert.Contains(lines, line => line.StartsWith("  2\t+0x90\t", StringComparison.Ordinal) && line.Contains("\t1.50 s\t", StringComparison.Ordinal));
        Assert.Contains("  t\tm2", lines);
        Assert.Null(BoneWatchSeries.Read([1, 2, 3]));
    }

    /// <summary>The same bones written by columns are set against their root the other way round, and hold still on it so.</summary>
    [Fact]
    public void BONESWrittenByColumnsAreSetAgainstTheirRootTheOtherWayRound()
    {
        var block = new BoneWatchSeries.Block("columns", 0x7100_0000, 0x80, []);
        Matrix4x4 fixedOn = Matrix4x4.CreateFromYawPitchRoll(0.4f, 0.2f, -0.3f);
        for (var read = 0; read < 60; read++)
        {
            Matrix4x4 root = Matrix4x4.CreateRotationZ(0.05f * read) * Matrix4x4.CreateRotationY(0.02f * read);
            byte[] bytes = new byte[0x80];
            Write(bytes, 0x00, Matrix4x4.Transpose(root));
            Write(bytes, 0x40, Matrix4x4.Transpose(fixedOn * root));
            block.Reads.Add((read / 30.0, bytes));
        }

        BoneWatchSeries.Run run = Assert.Single(BoneWatchSeries.Runs(block));
        (float[][] against, _, bool byRows) = BoneWatchSeries.Against(block, run);
        Assert.False(byRows);
        Assert.True(against[1].Max() < 0.05f);
    }

    /// <summary>With no player to read, the watch says so and reads nothing more.</summary>
    [Fact]
    public void WITHNoPlayerTheWatchSaysSo()
    {
        OffsetSchema schema = SchemaJson.Load(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(ComponentSweepTests.Fixture("x"))!)!)!, "schema", "poe2.offsets.json"));
        var watch = new BoneWatch(new FakeMemoryReader(), schema, seconds: 0.05);
        Assert.StartsWith("bone watch: no player read", Assert.Single(watch.Run(0, null)), StringComparison.Ordinal);
    }

    /// <summary>A matrix's sixteen floats by rows at <paramref name="at"/>.</summary>
    private static void Write(byte[] bytes, int at, Matrix4x4 matrix)
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
}
