using System.Buffers.Binary;
using System.Numerics;
using PoEformance.Game.Diagnostics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The cloth watch's reading (v0.1.425): a series of a cloth's targets and bodies, written as the watch writes them and read back, has its
/// steps taken again from its own states - and a cloth stepped in the tool's order is found again exactly in that order and in no other.
/// </summary>
public sealed class ClothWatchTests
{
    private const float Dt = 0.0101f;

    /// <summary>A skirt-like simulation: gravity, the pull towards the animation, bends, drag, shocks and collisions all on - and an enclosure tight enough to bite every swing.</summary>
    private static ClothSettings Skirt() => ClothSettings.Defaults with
    {
        LinearAllowedContraction = 0.3f,
        BendStiffness = 300f,
        BendAllowedAngle = 0.8f,
        NormalViscosity = 0.12f,
        TangentViscosity = 0.06f,
        EnclosureRadius = 1f,
        EnclosureAngle = 0.1f,
        AnimationVelocityFactor = 1f,
        AnimationForceFactor = 5f,
    };

    /// <summary>A cloth of four rows of three hanging down (+z), its top row pinned.</summary>
    private static ClothRig Hanging(ClothSettings settings)
    {
        var rest = new List<Vector3>();
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                rest.Add(new Vector3(column * 10f, 0f, row * 10f));
            }
        }

        var triangles = new List<int>();
        for (var row = 0; row < 3; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                int at = (row * 3) + column;
                triangles.AddRange([at, at + 1, at + 3, at + 1, at + 4, at + 3]);
            }
        }

        return new ClothRig(rest, [.. rest.Select(_ => new Int4(-1, -1, -1, -1))], new Vector4[rest.Count], triangles, [0, 1, 2], [], settings, "hanging");
    }

    /// <summary>Its targets at a time: the whole cloth swung sideways and lifted a little, as a hip carries a skirt.</summary>
    private static Vector3[] Targets(ClothRig rig, double seconds)
    {
        var offset = new Vector3(20f * MathF.Sin((float)(2 * Math.PI * seconds)), 6f * MathF.Sin((float)(3 * Math.PI * seconds)), -4f * MathF.Cos((float)(2 * Math.PI * seconds)));
        return [.. rig.Rest.Select(rest => rest + offset)];
    }

    /// <summary>The object's head as the game lays it: its block of parameters from +0xD8.</summary>
    private static byte[] Head(ClothSettings s)
    {
        var head = new byte[ClothLive.ObjectBytes];
        float[] block = s.ObjectBlock();
        for (var one = 0; one < block.Length; one++)
        {
            BinaryPrimitives.WriteSingleLittleEndian(head.AsSpan(0xD8 + (one * 4)), block[one]);
        }

        return head;
    }

    /// <summary>The setup the watch would read of <paramref name="rig"/>: its constraints in its own order, its folds, depths, parents and shocks.</summary>
    private static ClothWatchSeries.Setup SetupOf(ClothRig rig)
    {
        ClothSettings s = rig.Settings;
        var container = new byte[0x1C8];
        container[0x1A9] = s.Collisions ? (byte)1 : (byte)0;
        return new ClothWatchSeries.Setup(
            0x1000,
            Head(s),
            container,
            [.. Enumerable.Range(0, rig.Count).Select(one => (long)one)],
            [.. rig.Depth],
            [.. rig.Parent],
            [.. rig.Triangles],
            [.. rig.Edges.SelectMany(e => new[] { e.A, e.B, e.C, e.D })],
            [.. Enumerable.Range(0, rig.Count).Where(one => rig.Pinned[one])],
            [.. rig.Shocks.Select(x => ((long)x.Parent, (long)x.Child, 0f))],
            [.. rig.Edges.Select(e => new ClothWatchSeries.Distance(e.A, e.B, 0f, s.LinearStiffness, s.LinearAllowedContraction))],
            [.. rig.Bends.Select((e, at) => new ClothWatchSeries.Bend(e.A, e.B, e.C, e.D, s.BendStiffness, rig.RestFolds[at], s.BendAllowedAngle))],
            [],
            0f);
    }

    /// <summary>A Targets event's bytes: each particle's target and last.</summary>
    private static byte[] TargetBytes(Vector3[] target, Vector3[] last)
    {
        var bytes = new byte[target.Length * ClothWatchSeries.TargetBytes];
        for (var p = 0; p < target.Length; p++)
        {
            Put(bytes, p * ClothWatchSeries.TargetBytes, target[p]);
            Put(bytes, (p * ClothWatchSeries.TargetBytes) + 12, last[p]);
        }

        return bytes;
    }

    /// <summary>A Bodies event's bytes: the step's seconds, the bodies as the world keeps them, no contacts.</summary>
    private static byte[] BodyBytes(ClothRig rig, ReadOnlySpan<Vector3> places, ReadOnlySpan<Vector3> moves)
    {
        var bytes = new byte[8 + (places.Length * ClothWatchSeries.BodyBytes) + 4];
        BinaryPrimitives.WriteSingleLittleEndian(bytes, Dt);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), places.Length);
        for (var p = 0; p < places.Length; p++)
        {
            int at = 8 + (p * ClothWatchSeries.BodyBytes);
            Put(bytes, at, places[p]);
            Put(bytes, at + 0x0C, moves[p]);
            BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 0x30), 1f);
            bytes[at + 0x34] = rig.Pinned[p] ? (byte)1 : (byte)0;
        }

        return bytes;
    }

    /// <summary>One capsule across the cloth's middle, its axis along x three units in front of it - the cloth meets it.</summary>
    private static byte[] CapsuleBytes()
    {
        var bytes = new byte[4 + ClothWatchSeries.ShapeBytes];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, 1);
        int at = 4 + ClothWatchSeries.CapsuleBytes;
        Put(bytes, at, new Vector3(-10f, 3f, 25f));
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 12), 4f);
        Put(bytes, at + 16, new Vector3(40f, 3f, 25f));
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 28), 4f);
        return bytes;
    }

    private static void Put(byte[] bytes, int at, Vector3 value)
    {
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at), value.X);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 4), value.Y);
        BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(at + 8), value.Z);
    }

    /// <summary>
    /// A series of a cloth the tool's own world stepped in the tool's order - targets, step, after - each frame read as the watch reads
    /// one: the targets first, the bodies after them in the same read.
    /// </summary>
    private static byte[] Stepped(ClothRig rig, int frames, Vector3 wind = default)
    {
        var world = ClothWorld.Recorded([rig], 2, [(0, 1)]);
        world.Wind = wind;
        Vector3[] start = Targets(rig, 0);
        world.Seed(0, start, new Vector3[rig.Count], start, start, Dt);
        Vector3[] centres = [new(-10f, 3f, 25f), new(40f, 3f, 25f)];
        float[] radii = [4f, 4f];
        using var writer = new ClothWatchSeries.Writer();
        writer.Add(ClothWatchSeries.Kind.Capsules, -1, 0, CapsuleBytes());
        writer.Add(ClothWatchSeries.Kind.Targets, 0, 0, TargetBytes(start, start));
        writer.Add(ClothWatchSeries.Kind.Bodies, 0, 0, BodyBytes(rig, world.Particles(0), world.Moves(0)));
        Vector3[] last = start;
        for (var frame = 1; frame <= frames; frame++)
        {
            double time = frame * Dt;
            Vector3[] now = Targets(rig, time);
            writer.Add(ClothWatchSeries.Kind.Targets, 0, time, TargetBytes(now, last));
            world.Retarget(0, now);
            world.Spheres(centres, centres, radii);
            world.StepOnce(Dt);
            world.Settle();
            writer.Add(ClothWatchSeries.Kind.Bodies, 0, time, BodyBytes(rig, world.Particles(0), world.Moves(0)));
            last = now;
        }

        return writer.Finish(Vector3.Zero, 0x2000, -1, frames * Dt, frames, 0, string.Empty, [SetupOf(rig)], [ClothWatchSeries.Tool.Of("hanging", rig)], [(7, 1, 2)]);
    }

    /// <summary>
    /// THE REPLAY FINDS THE TOOL'S OWN STEPS AGAIN, EXACTLY, AND ONLY IN THE TOOL'S ORDER: a cloth swung by its targets, pulled by the
    /// animation, dragged, bent, enclosed and meeting a capsule, stepped by the tool's world and read as the watch reads the game's, is taken
    /// again step by step from each read state onto the next targets - and lands on the next state in the order it was stepped in, while
    /// the other two orders do not. So against the game's own reads the same reading says which order the engine runs, and how far the
    /// port's step is from the engine's.
    /// </summary>
    [Fact]
    public void THEREPLAYFindsTheToolsOwnStepsAgainExactlyAndOnlyInItsOrder()
    {
        ClothRig rig = Hanging(Skirt());
        ClothWatchSeries series = Assert.IsType<ClothWatchSeries>(ClothWatchSeries.Read(Stepped(rig, 120)));
        Assert.Single(series.Setups);
        Assert.Equal(1 + (2 * 121), series.Events.Count);
        Assert.Equal("hanging", series.Tools[0]?.Name);

        // A FRAME RUNS FROM ITS TARGETS TO THE NEXT: the last targets' frame has no next to end it and is not taken.
        IReadOnlyList<ClothWatchSeries.OrderFit> fits = series.Fits(0);
        Assert.Equal(4, fits.Count);
        Assert.Equal(119, fits[0].Steps);
        Assert.Equal(fits[0].Steps, fits[0].Exact);
        Assert.True(fits[0].MostWorst < 1e-4f, $"the tool's order off by {fits[0].MostWorst}");
        Assert.True(fits[1].MedianWorst > 10 * ClothWatchSeries.OnTarget, $"step, targets, after off by only {fits[1].MedianWorst}");
        Assert.True(fits[2].MedianWorst > 10 * ClothWatchSeries.OnTarget, $"targets, after, step off by only {fits[2].MedianWorst}");
        Assert.True(fits[3].MostWorst > ClothWatchSeries.OnTarget, $"targets, step without the after off by only {fits[3].MostWorst}");

        // AND ITS SETUP IS ITS OWN CLOTH'S: the report finds nothing otherwise.
        string said = string.Join("\n", series.Report());
        Assert.Contains("pinned alike", said);
        Assert.Contains("distances in the same order", said);
        Assert.Contains("bends: 0 of", said);
        Assert.Contains("depth otherwise at 0, parent at 0", said);
        Assert.Contains("its simulation the game's", said);
    }

    /// <summary>
    /// THE ENGINE'S DRAG RELAXES TO ITS WIND (module+0x13AB700: the velocity less the wind's decayed in the particle's frame, the wind added
    /// back): a cloth with nothing pinned, no gravity and no pull, blown long enough, moves with the wind.
    /// </summary>
    [Fact]
    public void THEDRAGRelaxesTheClothToTheWind()
    {
        ClothSettings still = ClothSettings.Defaults with { Gravity = 0f, NormalViscosity = 1f, TangentViscosity = 1f, Collisions = false, ShockPropagation = false };
        ClothRig rig = new(
            [new(0f, 0f, 0f), new(10f, 0f, 0f), new(0f, 10f, 0f)], [.. Enumerable.Repeat(new Int4(-1, -1, -1, -1), 3)], new Vector4[3], [0, 1, 2], [], [], still, "blown");
        var wind = new Vector3(120f, -40f, 30f);
        var world = ClothWorld.Recorded([rig], 0, []);
        world.Wind = wind;
        Vector3[] start = [.. rig.Rest];
        world.Seed(0, start, new Vector3[3], start, start, Dt);
        for (var frame = 0; frame < 400; frame++)
        {
            world.Retarget(0, start);
            world.StepOnce(Dt);
            world.Settle();
        }

        foreach (Vector3 move in world.Moves(0))
        {
            Assert.True(Vector3.Distance(move / Dt, wind) < 0.01f * wind.Length(), $"moving at {move / Dt}, the wind {wind}");
        }
    }

    /// <summary>
    /// THE REPLAY FITS THE WIND A CLOTH WAS BLOWN BY: a cloth stepped under a steady wind and read as the watch reads the game's is taken
    /// again in the tool's order with a wind fitted to each frame - the wind found is the one that blew, and the steps land exactly again.
    /// </summary>
    [Fact]
    public void THEREPLAYFitsTheWindTheClothWasBlownBy()
    {
        ClothRig rig = Hanging(Skirt());
        var wind = new Vector3(60f, -20f, 10f);
        ClothWatchSeries series = Assert.IsType<ClothWatchSeries>(ClothWatchSeries.Read(Stepped(rig, 120, wind)));
        string said = string.Join("\n", series.Report());
        Assert.Contains("the tool's order with a wind fitted to each frame", said);
        string line = said.Split('\n').Single(one => one.Contains("a wind fitted to each frame", StringComparison.Ordinal));

        // NEARLY LINEAR, NOT QUITE: the enclosure and the capsule bend the wind's effect, so the fit is held to a unit or two of the wind
        // and nearly every frame - measured 118 of 119 within 0.01, the mean (59.6, -18.5, 10.6).
        System.Text.RegularExpressions.Match exact = System.Text.RegularExpressions.Regex.Match(line, @"(\d+) of 119 frames within 0\.01 throughout");
        Assert.True(exact.Success && int.Parse(exact.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) >= 116, line);
        System.Text.RegularExpressions.Match mean = System.Text.RegularExpressions.Regex.Match(line, @"on the mean \((-?[\d.]+), (-?[\d.]+), (-?[\d.]+)\)");
        Assert.True(mean.Success, line);
        var found = new Vector3(
            float.Parse(mean.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(mean.Groups[2].Value, System.Globalization.CultureInfo.InvariantCulture),
            float.Parse(mean.Groups[3].Value, System.Globalization.CultureInfo.InvariantCulture));
        Assert.True(Vector3.Distance(found, wind) < 3f, $"found {found} for {wind}");
    }

    /// <summary>
    /// A CLOTH OF THE GAME'S OWN SETUP STEPS AS THE CLOTH IT WAS READ FROM: ClothRig.Recorded from a cloth's constraints, folds, depths,
    /// parents and shocks, stepped on the same targets, stands where the cloth itself stands - to the bit.
    /// </summary>
    [Fact]
    public void ARECORDEDClothStepsAsTheClothItWasReadFrom()
    {
        ClothRig rig = Hanging(Skirt());
        ClothWatchSeries.Setup setup = SetupOf(rig);
        ClothRig recorded = ClothWatchSeries.Rig(setup, setup.Settings(rig.Settings.ShockPropagation), out string why);
        Assert.StartsWith($"{rig.Edges.Length} of {rig.Edges.Length} distances, {rig.Bends.Length} of {rig.Bends.Length} bends and {rig.Shocks.Length} of {rig.Shocks.Length} shock links", why, StringComparison.Ordinal);
        Assert.Equal(rig.Settings, recorded.Settings);

        var one = ClothWorld.Recorded([rig], 0, []);
        var two = ClothWorld.Recorded([recorded], 0, []);
        Vector3[] start = Targets(rig, 0);
        one.Seed(0, start, new Vector3[rig.Count], start, start, Dt);
        two.Seed(0, start, new Vector3[rig.Count], start, start, Dt);
        for (var frame = 1; frame <= 60; frame++)
        {
            Vector3[] now = Targets(rig, frame * Dt);
            foreach (ClothWorld world in (ClothWorld[])[one, two])
            {
                world.Retarget(0, now);
                world.StepOnce(Dt);
                world.Settle();
            }
        }

        Assert.Equal(one.Particles(0).ToArray(), two.Particles(0).ToArray());
        Assert.Equal(one.Moves(0).ToArray(), two.Moves(0).ToArray());
    }
}
