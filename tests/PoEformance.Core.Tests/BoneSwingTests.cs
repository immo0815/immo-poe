using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// A held piece's lamp swinging as the engine swings it (v0.1.416): the pendulum held against the game's own lamp, the swung bone in the rig,
/// the model's build, and the swing stepped with a pose's clock.
/// </summary>
public sealed class BoneSwingTests
{
    /// <summary>
    /// THE GAME'S OWN LAMP (the 09:29 capture, every tick): seeded where the game held it, thirty ticks of the pendulum on the measured top end
    /// within a few degrees of where the game's lamp went - over every stretch on which he did not turn, which is what the swing is for. The
    /// same steps without the kick end 33 degrees off at the median, with all of it 43 (worked out on the same fixture when it was measured).
    /// </summary>
    [Fact]
    public void THEGAMESLampSwingsAsThePendulum()
    {
        var rows = new List<(double At, Vector3 Top, Vector3 Lamp, bool Read, double Facing)>();
        foreach (string line in File.ReadLines(ComponentSweepTests.Fixture(Path.Combine("lamp-swing", "staff-lamp-swing.tsv"))))
        {
            if (line.StartsWith('#'))
            {
                continue;
            }

            string[] parts = line.Split('\t');
            float F(int at) => float.Parse(parts[at], CultureInfo.InvariantCulture);
            var lamp = new Vector3(F(4), F(5), F(6));
            rows.Add((double.Parse(parts[0], CultureInfo.InvariantCulture), new Vector3(F(1), F(2), F(3)), lamp, !float.IsNaN(lamp.X), double.Parse(parts[7], CultureInfo.InvariantCulture)));
        }

        Assert.True(rows.Count > 1000, $"{rows.Count} ticks");

        // A TICK IS CALM where it and its neighbours were read, densely, and his facing held still (under half a degree a tick).
        int count = rows.Count;
        bool Dense(int at) => at > 0 && rows[at].At - rows[at - 1].At < 0.015;
        double Turned(int at) => at <= 0 ? 0 : Math.Abs(Math.IEEERemainder(rows[at].Facing - rows[at - 1].Facing, 2 * Math.PI)) * 180 / Math.PI;
        bool Calm(int at) => at >= 4 && at < count - 1
            && Enumerable.Range(at - 3, 5).All(one => rows[one].Read) && Enumerable.Range(at - 2, 4).All(Dense)
            && Turned(at - 1) < 0.5 && Turned(at) < 0.5 && Turned(at + 1) < 0.5;

        const int Steps = 30;
        const float Rod = 22.3368f;
        var ends = new List<double>();
        for (var start = 5; start < count - Steps - 1; start += 5)
        {
            if (!Enumerable.Range(start, Steps).All(Calm))
            {
                continue;
            }

            var bob = new Pendulum();
            bob.Seed(
                rows[start].Lamp - rows[start].Top, rows[start - 1].Lamp - rows[start - 1].Top, rows[start].Top, rows[start - 1].Top, rows[start - 2].Top);
            for (int at = start; at < start + Steps; at++)
            {
                bob.Step(rows[at + 1].Top, Rod);
                Assert.Equal(Rod, bob.Offset.Length(), 2);
            }

            Vector3 game = Vector3.Normalize(rows[start + Steps].Lamp - rows[start + Steps].Top);
            ends.Add(Math.Acos(Math.Clamp(Vector3.Dot(game, Vector3.Normalize(bob.Offset)), -1f, 1f)) * 180 / Math.PI);
        }

        Assert.True(ends.Count >= 15, $"{ends.Count} calm runs");
        ends.Sort();
        double median = ends[ends.Count / 2];
        Assert.True(median < 8, $"thirty ticks end {median:0.00} degrees off the game's lamp at the median");
        Assert.True(ends[(int)(ends.Count * 0.9)] < 25, $"and {ends[(int)(ends.Count * 0.9)]:0.00} at the ninetieth hundredth");
    }

    /// <summary>
    /// Hanging still from a still top it stays straight down; let go off the vertical it swings back and forth about straight down, its rod's
    /// length held, its period near the small swing's 2π / sqrt(Fall) ticks, and dying away by <see cref="Pendulum.Keep"/> a tick.
    /// </summary>
    [Fact]
    public void AFREELampSwingsAboutStraightDownAndDiesAway()
    {
        const float Rod = 24f;
        var top = new Vector3(5f, -3f, 100f);
        var bob = new Pendulum();
        bob.Hang(top, Rod);
        for (var tick = 0; tick < 200; tick++)
        {
            bob.Step(top, Rod);
        }

        Assert.True(Vector3.Distance(new Vector3(0f, 0f, Rod), bob.Offset) < 1e-4f, $"hanging still it stands at {bob.Offset}");

        // TEN DEGREES OFF along x, at rest.
        var off = new Vector3(Rod * MathF.Sin(0.1745f), 0f, Rod * MathF.Cos(0.1745f));
        bob.Seed(off, off, top, top, top);
        var crossings = new List<int>();
        float most = 0f, last = off.X;
        for (var tick = 1; tick <= 1000; tick++)
        {
            bob.Step(top, Rod);
            Assert.Equal(Rod, bob.Offset.Length(), 3);
            Assert.True(Math.Abs(bob.Offset.Y) < 1e-3f, "a swing along x stays along x");
            if (last > 0f && bob.Offset.X <= 0f)
            {
                crossings.Add(tick);
            }

            last = bob.Offset.X;
            if (tick > 900)
            {
                most = MathF.Max(most, MathF.Abs(bob.Offset.X));
            }
        }

        double period = (crossings[^1] - crossings[0]) / (double)(crossings.Count - 1);
        double small = 2 * Math.PI / Math.Sqrt(Pendulum.Fall);
        Assert.InRange(period, small * 0.97, small * 1.03);
        Assert.True(most < off.X * 0.05f, $"a thousand ticks on it still swings {most:0.000} along x");
    }

    /// <summary>The top jerked along +x for a tick kicks the lamp towards -x, a tick late, by <see cref="Pendulum.Inertia"/> of the jerk.</summary>
    [Fact]
    public void THELampIsKickedAgainstItsTopsAccelerationATickLate()
    {
        const float Rod = 24f;
        var bob = new Pendulum();
        bob.Hang(Vector3.Zero, Rod);
        bob.Step(new Vector3(2f, 0f, 0f), Rod);
        Assert.True(Vector3.Distance(new Vector3(0f, 0f, Rod), bob.Offset) < 1e-4f, $"the tick the top jumps the lamp still hangs at {bob.Offset}");

        bob.Step(new Vector3(4f, 0f, 0f), Rod);
        Vector3 kicked = bob.Offset;
        Assert.True(kicked.X < 0f, $"the lamp went {kicked.X} along x");
        var expected = Vector3.Normalize(new Vector3(-Pendulum.Inertia * 2f, 0f, Rod + (Pendulum.Fall * Rod))) * Rod;
        Assert.True(Vector3.Distance(expected, kicked) < 1e-4f, $"kicked to {kicked}, the steps say {expected}");
    }

    /// <summary>
    /// One bone's skin matrix worked down its chain alone is the whole pose's to the bit, at every bone of a real rig and at times through its
    /// animation - and leaves the pose as it stood.
    /// </summary>
    [Fact]
    public void ABONEsSkinDownItsChainIsTheWholePosesToTheBit()
    {
        (AnimationSkeleton rig, SkeletonPose pose, AnimationTracks tracks) = Blackguard();
        var other = SkeletonPose.Of(rig)!;
        foreach (float time in (float[])[0f, 1.5f, tracks.Frames * 0.37f, tracks.Frames * 0.91f])
        {
            pose.Take(tracks, time);
            other.Take(tracks, 0f);
            Matrix4x4[] before = [.. other.Skin];
            for (var bone = 0; bone < rig.Bones.Count; bone++)
            {
                Assert.Equal(pose.Skin[bone], other.SkinAt(tracks, time, bone));
            }

            Assert.Equal(before, other.Skin);
        }
    }

    /// <summary>
    /// A swung bone appended under a bone follows it rigidly in every pose - so whatever takes a pose without swinging it finds the lamp on the
    /// hand - its own tracks staying its rig's, and the rig counting it apart.
    /// </summary>
    [Fact]
    public void ASWUNGBoneFollowsItsParentInEveryPose()
    {
        (AnimationSkeleton rig, _, AnimationTracks tracks) = Blackguard();
        int hand = rig.Bones.Count - 1;
        AnimationSkeleton? swinging = rig.Swinging([("swung:lamp", hand), ("swung:other", 0)]);
        Assert.NotNull(swinging);
        Assert.Equal(rig.Bones.Count + 2, swinging.Bones.Count);
        Assert.Equal(2, swinging.Swung);
        Assert.Equal(rig.Animations.Count, swinging.Animations.Count);

        SkeletonPose pose = SkeletonPose.Of(swinging)!;
        Assert.Equal(hand, pose.Parents[rig.Bones.Count]);
        Assert.Equal(0, pose.Parents[rig.Bones.Count + 1]);
        Assert.True(Distance(Matrix4x4.Identity, pose.BindModel[rig.Bones.Count]) < 1e-3f, "a swung bone rests at the model's origin");

        AnimationTracks played = swinging.Played(swinging.Animations[0], (_, _) => null);
        Assert.Equal(tracks.Tracks.Count(one => one.Bone < rig.Bones.Count), played.Tracks.Count);
        foreach (float time in (float[])[0f, played.Frames * 0.5f])
        {
            pose.Take(played, time);
            Assert.True(Distance(pose.Skin[hand], pose.Skin[rig.Bones.Count]) < 1e-3f, "the lamp hangs from the hand as authored");
            Assert.True(Distance(pose.Skin[0], pose.Skin[rig.Bones.Count + 1]) < 1e-3f, "and the other from the root");
        }

        Assert.Null(rig.Swinging([("swung:nowhere", rig.Bones.Count + 7)]));
    }

    /// <summary>
    /// The swinger hangs each lamp straight down at its first pose, keeps its rod as the pose holds it at every tick through a real animation,
    /// sets the swung bone's skin so the rod's end lands where the swing holds it, and with the switch off hangs it from its socket as authored.
    /// </summary>
    [Fact]
    public void THESwingerStepsTheLampWithThePosesClock()
    {
        (AnimationSkeleton rig, _, AnimationTracks _) = Blackguard();
        int hand = rig.Bones.Count - 1;
        AnimationSkeleton swinging = rig.Swinging([("swung:lamp", hand)])!;
        SkeletonPose pose = SkeletonPose.Of(swinging)!;
        int bone = rig.Bones.Count;
        Vector3 top = pose.BindModel[hand].Translation + new Vector3(3f, 0f, 0f);
        var swing = new BoneSwing(bone, hand, top, top + new Vector3(10f, 4f, -20f), "phys_tri_lamp_jntBnd", "staff.ao");
        var swinger = new BoneSwinger([swing]);
        AnimationTracks tracks = swinging.Played(swinging.Animations[0], (_, _) => null);
        float rate = swinging.Animations[0].Rate > 0 ? swinging.Animations[0].Rate : 30f;

        pose.Take(tracks, 0f);
        swinger.Swing(pose, tracks, 0f, rate, tracks.Frames, swinging: true);
        float length = Vector3.Distance(Vector3.Transform(swing.Rest, pose.Skin[hand]), Vector3.Transform(swing.Pivot, pose.Skin[hand]));
        Assert.True(Vector3.Distance(new Vector3(0f, 0f, length), swinger.OffsetOf(0)) < 1e-3f, $"it starts at {swinger.OffsetOf(0)}");

        float frame = 0f;
        for (var tick = 1; tick <= 300; tick++)
        {
            frame += (float)(Pendulum.Tick * rate);
            if (frame > tracks.Frames)
            {
                frame %= tracks.Frames;
            }

            swinger.Due(Pendulum.Tick);
            pose.Take(tracks, frame);
            swinger.Swing(pose, tracks, frame, rate, tracks.Frames, swinging: true);
            Matrix4x4 socket = pose.Skin[hand];
            Vector3 at = Vector3.Transform(swing.Pivot, socket);
            float rod = Vector3.Distance(Vector3.Transform(swing.Rest, socket), at);
            Assert.Equal(rod, swinger.OffsetOf(0).Length(), 2);
            Assert.True(
                Vector3.Distance(at + swinger.OffsetOf(0), Vector3.Transform(swing.Rest, pose.Skin[bone])) < 1e-2f,
                $"tick {tick}: the rod's end through the swung bone's skin stands off where the swing holds it");
        }

        Assert.Equal(300, swinger.Ticks);

        swinger.Swing(pose, tracks, frame, rate, tracks.Frames, swinging: false);
        Assert.Equal(pose.Skin[hand], pose.Skin[bone]);
    }

    /// <summary>
    /// A HELD STAFF WHOSE .ao CARRIES THE MEASURED SIMULATION gets its lamp's bone swung: a bone appended under the hand, the lamp's vertices and
    /// its light on it, its rod from its parent's rest to its own through the staff's place - and the capture writes the swing and plays it
    /// through his opening animation. Another simulation is left as animated and said so.
    /// </summary>
    [Fact]
    public void AHELDStaffsLampIsSwungWhereItsAoCarriesTheMeasuredSimulation()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["body.ao"] = Text(Body);

        // THE FIXTURE'S MESH IS WEIGHTED WHOLLY TO BONE 1 - the lamp's here.
        install.Files["staff/rig.ast"] = Packed.Skeleton(
            [("root_jntBnd", 255, 1, 0f), ("phys_tri_lamp_jntBnd", 255, 255, -50f)],
            [("idle_01", string.Empty, 2, 30, 0x6f, 0, 0)]);
        install.Files["staff/StaffHeld.ao"] = Text(Staff(Measured));

        MonsterModel model = MonsterModels.OfFiles(
            install.Read, ["body.ao"], true, null, [new WornPiece("R_wrist_jntBnd", "staff/StaffHeld.ao") { Held = true }]);
        BoneSwing swing = Assert.Single(model.Swings);
        AnimationSkeleton body = AnimationSkeleton.Read(install.Files["art/rig.ast"]);
        int wrist = Bone(body, "R_wrist_jntBnd");
        Assert.Equal(body.Bones.Count, swing.Bone);
        Assert.Equal(wrist, swing.Socket);
        Assert.Equal("phys_tri_lamp_jntBnd", swing.Name);
        Assert.Equal(1, model.Rig.Swung);
        Assert.Equal("swung:phys_tri_lamp_jntBnd", model.Rig.Bones[swing.Bone].Name);
        Matrix4x4 bind = SkeletonPose.Of(body)!.BindModel[wrist];
        Assert.True(Vector3.Distance(Vector3.Transform(Vector3.Zero, bind), swing.Pivot) < 1e-3f, $"the rod hangs from {swing.Pivot}");
        Assert.True(Vector3.Distance(Vector3.Transform(new Vector3(0f, 0f, -50f), bind), swing.Rest) < 1e-3f, $"and ends at {swing.Rest}");
        Assert.Contains("StaffHeld.ao's phys_tri_lamp_jntBnd swings as measured: a rod of 50 from its parent", model.SwingSaid, StringComparison.Ordinal);

        // THE STAFF'S FOUR VERTICES ON THE SWUNG BONE WHOLLY, its light with them.
        var swung = 0;
        for (var vertex = 0; vertex < model.Mesh.Positions.Length; vertex++)
        {
            if (model.Mesh.Bones[vertex * 4] == swing.Bone && model.Mesh.Weights[vertex * 4] == 255)
            {
                swung++;
            }
        }

        Assert.Equal(4, swung);
        Assert.Equal(swing.Bone, Assert.Single(model.Carried).Bone);
        Assert.NotNull(SkeletonPose.Of(model.Rig));

        // THE CAPTURE WRITES IT.
        string text = CaptureMonsterFiles.Write(install.Read, new CapturePane("monster", [], model) { Path = "Metadata/Characters/Test" }, null, null, out _);
        Assert.Contains("=== the bones he swings (BoneSwing, the light panel's \"swing\"): 1 ", text, StringComparison.Ordinal);
        Assert.Contains($"  {swing.Bone}\tphys_tri_lamp_jntBnd\tR_wrist_jntBnd\t", text, StringComparison.Ordinal);

        // ANOTHER SIMULATION - the player's cape's - stands as animated, said why.
        install.Files["staff/StaffHeld.ao"] = Text(Staff("\t\tlinear_allowed_contraction = 0.600\n\t\tanimation_force_factor = 10.000\n"));
        MonsterModel cape = MonsterModels.OfFiles(
            install.Read, ["body.ao"], true, null, [new WornPiece("R_wrist_jntBnd", "staff/StaffHeld.ao") { Held = true }]);
        Assert.Empty(cape.Swings);
        Assert.Equal(0, cape.Rig.Swung);
        Assert.Contains("StaffHeld.ao: its simulation is not the one measured (linear_allowed_contraction 0.600, no bend_stiffness", cape.SwingSaid, StringComparison.Ordinal);
        Assert.Equal(wrist, Assert.Single(cape.Carried).Bone);

        // AND A PIECE WRITING NO SIMULATION SAYS NOTHING OF ONE.
        install.Files["staff/StaffHeld.ao"] = Text(Staff(string.Empty));
        MonsterModel plain = MonsterModels.OfFiles(
            install.Read, ["body.ao"], true, null, [new WornPiece("R_wrist_jntBnd", "staff/StaffHeld.ao") { Held = true }]);
        Assert.Empty(plain.Swings);
        Assert.Equal(string.Empty, plain.SwingSaid);
    }

    /// <summary>A body on the fixture's rig, its client block closed by the last two characters.</summary>
    private const string Body =
        "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t}\n\tSkinMesh\n\t{\n\t\tskin = \"art/mesh.sm\"\n\t}\n}\n";

    /// <summary>WandererStaffHeld.ao's simulation lines as the capture of 03:27 wrote them.</summary>
    private const string Measured =
        "\t\tsocket = \"lamp_01\"\n\t\t\tparent = \"phys_tri_lamp_jntBnd\"\n\t\t\ttranslation = \"0 0 10\"\n"
        + "\t\tlinear_allowed_contraction = 0.000\n\t\tbend_stiffness = 800.000\n\t\tbend_allowed_angle = 0.000\n"
        + "\t\tnormal_viscosity_multiplier = 0.000\n\t\ttangent_viscosity_multiplier = 0.000\n\t\tenclosure_radius = 5.000\n"
        + "\t\tenclosure_angle = 5.000\n\t\tanimation_velocity_factor = 2.000\n\t\tanimation_force_factor = 2.000\n\t\tuse_shock_propagation = false\n";

    /// <summary>A staff on its own rig with these simulation lines, and the lamp's light.</summary>
    private static string Staff(string simulation)
        => "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"staff/rig.ast\"\n" + simulation + "\t}\n\tSkinMesh\n\t{\n\t\tskin = \"art/mesh.sm\"\n\t}\n"
            + "\tLights\n\t{\n\t\tlight = \"point_light\"\n\t\t\tbone_name = \"phys_tri_lamp_jntBnd\"\n\t\t\tstate = \"\"\n"
            + "\t\t\t\tcolour = \"2.4671 0.493089 0.0398032\"\n\t\t\t\tradius = 150\n\t\t\t\tpenumbra_dist = 0.5\n\t\t\t\tstyle = 1\n\t}\n}\n";

    /// <summary>The blackguard rig of the repository's fixtures, its one animation read.</summary>
    private static (AnimationSkeleton Rig, SkeletonPose Pose, AnimationTracks Tracks) Blackguard()
    {
        AnimationSkeleton rig = AnimationSkeleton.Read(File.ReadAllBytes(ComponentSweepTests.Fixture("blackguard-v6.one.ast")));
        Assert.True(rig.Ready, rig.Why);
        AnimationTracks tracks = AnimationTracks.Read(rig.Tracks(rig.Animations[0], (_, _) => null), rig.Animations[0].Tracks, rig.Version);
        Assert.True(tracks.Ready, tracks.Why);
        return (rig, SkeletonPose.Of(rig)!, tracks);
    }

    private static float Distance(Matrix4x4 one, Matrix4x4 other)
    {
        Matrix4x4 gap = one - other;
        return MathF.Max(
            MathF.Max(MathF.Max(MathF.Abs(gap.M11), MathF.Abs(gap.M12)), MathF.Max(MathF.Abs(gap.M13), MathF.Abs(gap.M21))),
            MathF.Max(
                MathF.Max(MathF.Max(MathF.Abs(gap.M22), MathF.Abs(gap.M23)), MathF.Max(MathF.Abs(gap.M31), MathF.Abs(gap.M32))),
                MathF.Max(MathF.Max(MathF.Abs(gap.M33), MathF.Abs(gap.M41)), MathF.Max(MathF.Abs(gap.M42), MathF.Abs(gap.M43)))));
    }

    private static int Bone(AnimationSkeleton rig, string name)
    {
        for (var at = 0; at < rig.Bones.Count; at++)
        {
            if (rig.Bones[at].Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            {
                return at;
            }
        }

        throw new InvalidOperationException($"the rig has no {name}");
    }

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);
}
