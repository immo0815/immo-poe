using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// A held piece's simulated bones moving as the engine's cloth moves them (v0.1.422): the staff's lamp held against what the game's lamp
/// showed, the simulated bone in the rig, the model's build, and the cloths stepped with a pose's clock.
/// </summary>
public sealed class ClothSwingTests
{
    /// <summary>The Exile's Staff's lamp as its mesh carries it (the capture of 11:56): a pinned top and two corners, in the staff's frame.</summary>
    private static readonly Vector3[] LampRest = [new(-7.826f, 11.749f, -93.64f), new(-14.53f, -0.071f, -141.194f), new(-28.402f, 14.414f, -95.986f)];

    /// <summary>Its simulation: WandererStaffHeld.ao's keys, the rest the engine's defaults.</summary>
    private static ClothSettings LampSettings() => ClothSettings.Of(
        new Dictionary<string, string>
        {
            ["linear_allowed_contraction"] = "0", ["bend_stiffness"] = "800", ["bend_allowed_angle"] = "0", ["normal_viscosity_multiplier"] = "0",
            ["tangent_viscosity_multiplier"] = "0", ["enclosure_radius"] = "5", ["enclosure_angle"] = "5", ["animation_velocity_factor"] = "2",
            ["animation_force_factor"] = "2", ["use_shock_propagation"] = "false",
        },
        out _);

    /// <summary>The lamp's cloth, every particle carried by bone 0, its triangle bone at <paramref name="bind"/>.</summary>
    private static ClothRig Lamp(Matrix4x4 bind)
        => new(LampRest, [.. LampRest.Select(_ => new Int4(0, -1, -1, -1))], [.. LampRest.Select(_ => Vector4.UnitX)], [0, 1, 2], [1],
            [(1, "phys_tri_lamp_jntBnd", bind, [])], LampSettings(), "WandererStaffHeld.ao");

    /// <summary>
    /// THE GAME'S RESTING LAMP (MEASURED v0.1.414 in the bone watch of 09:29): standing, the lamp hung 2 to 7.5 degrees from straight down,
    /// tilted towards its rest's way, the rest 30 to 34 degrees from down. The engine's cloth gives exactly that: its pull towards the
    /// animation (velocity factor 2, force factor 2) against gravity's 980 settles the staff's lamp, its rest turned 32 degrees from down,
    /// about five degrees from down towards it - at 99 frames a second, the game's in that capture. And the game's lamp's "keep 0.98076 a
    /// tick" is the pull's own exp(-2 x 0.0101) = 0.980.
    /// </summary>
    [Fact]
    public void THEGAMESRestingLampTiltsAsTheClothSettlesIt()
    {
        Assert.Equal(0.98076f, MathF.Exp(-2f * 0.0101f), 2);
        Vector3 pivot = LampRest[1];
        Vector3 hang = (LampRest[0] + LampRest[2]) * 0.5f - pivot;
        Vector3 down = Vector3.UnitZ;

        // THE STAFF TURNED so the lamp's rest stands 32 degrees from straight down, about the pivot.
        Vector3 axis = Vector3.Normalize(Vector3.Cross(hang, down));
        float rest = MathF.Acos(Vector3.Dot(Vector3.Normalize(hang), down));
        Matrix4x4 staff = Matrix4x4.CreateTranslation(-pivot) * Matrix4x4.CreateFromAxisAngle(axis, rest - (32f * MathF.PI / 180f)) * Matrix4x4.CreateTranslation(pivot);
        Vector3 restWay = Vector3.Normalize(Vector3.TransformNormal(hang, staff));
        Assert.Equal(32f, MathF.Acos(restWay.Z) * 180f / MathF.PI, 1);

        var world = new ClothWorld([Lamp(Matrix4x4.CreateTranslation(pivot + (Vector3.Normalize(hang) * 24f)))], [], [], []);
        Matrix4x4[] skin = [staff];
        for (var frame = 0; frame < 99 * 20; frame++)
        {
            world.Frame(0.0101f, skin);
        }

        ReadOnlySpan<Vector3> at = world.Particles(0);
        Vector3 now = Vector3.Normalize(((at[0] + at[2]) * 0.5f) - at[1]);
        float fromDown = MathF.Acos(now.Z) * 180f / MathF.PI;
        Assert.InRange(fromDown, 2f, 7.5f);
        Assert.True(Vector3.Dot(Vector3.Cross(down, now), Vector3.Cross(down, restWay)) > 0f, "tilted towards the rest's way");

        // THE ROD STAYS RIGID: linear_stiffness -1 corrects every distance whole, ten times a step.
        Assert.Equal(Vector3.Distance(LampRest[0], LampRest[1]), Vector3.Distance(at[0], at[1]), 0);
        Assert.Equal(Vector3.Distance(LampRest[2], LampRest[1]), Vector3.Distance(at[2], at[1]), 0);
    }

    /// <summary>
    /// A LAMP SWUNG OUT DIES AWAY about where it settles: let go 40 degrees off, at 99 frames a second, its swing a second later is under half
    /// what it was and three seconds later within a few degrees of rest.
    /// </summary>
    [Fact]
    public void ALAMPSwungOutDiesAway()
    {
        Vector3 pivot = LampRest[1];
        Vector3 hang = (LampRest[0] + LampRest[2]) * 0.5f - pivot;
        var world = new ClothWorld([Lamp(Matrix4x4.CreateTranslation(pivot + (Vector3.Normalize(hang) * 24f)))], [], [], []);
        Matrix4x4[] still = [Matrix4x4.Identity];
        for (var frame = 0; frame < 99 * 10; frame++)
        {
            world.Frame(0.0101f, still);
        }

        Vector3 settled = Vector3.Normalize(((world.Particles(0)[0] + world.Particles(0)[2]) * 0.5f) - world.Particles(0)[1]);

        // A JERK OF THE STAFF sideways for one frame, then still: the lamp swings out and back.
        float Off()
        {
            ReadOnlySpan<Vector3> at = world.Particles(0);
            Vector3 way = Vector3.Normalize(((at[0] + at[2]) * 0.5f) - at[1]);
            return MathF.Acos(Math.Clamp(Vector3.Dot(way, settled), -1f, 1f)) * 180f / MathF.PI;
        }

        Matrix4x4[] jerked = [Matrix4x4.CreateTranslation(60f, 0f, 0f)];
        world.Frame(0.0101f, jerked);
        float first = 0f, later = 0f, last = 0f;
        for (var frame = 0; frame < 99 * 4; frame++)
        {
            world.Frame(0.0101f, jerked);
            float off = Off();
            if (frame < 99)
            {
                first = MathF.Max(first, off);
            }
            else if (frame < 198)
            {
                later = MathF.Max(later, off);
            }
            else if (frame >= 99 * 3)
            {
                last = MathF.Max(last, off);
            }
        }

        Assert.True(first > 10f, $"it swings out {first} degrees");
        Assert.True(later < first * 0.5f, $"a second later {later} of {first}");
        Assert.True(last < 3f, $"three seconds later {last}");
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
    /// The swinger stands the cloth on its targets at its first pose, keeps the lamp's pinned corner on the hand and its rod's lengths through
    /// a real animation at 99 frames a second, sets the simulated bone's skin so its bind lands where the cloth holds it, and with the switch
    /// off hangs it from its socket as authored.
    /// </summary>
    [Fact]
    public void THESwingerStepsTheClothWithThePosesClock()
    {
        (AnimationSkeleton rig, _, AnimationTracks _) = Blackguard();
        int hand = rig.Bones.Count - 1;
        AnimationSkeleton swinging = rig.Swinging([("swung:lamp", hand)])!;
        SkeletonPose pose = SkeletonPose.Of(swinging)!;
        int bone = rig.Bones.Count;
        Vector3 top = pose.BindModel[hand].Translation;
        Vector3[] rest = [top + new Vector3(10f, 0f, 40f), top, top + new Vector3(-10f, 0f, 40f)];
        Matrix4x4 bind = Matrix4x4.CreateTranslation(top + new Vector3(0f, 0f, 20f));
        var cloth = new ClothRig(rest, [.. rest.Select(_ => new Int4(hand, -1, -1, -1))], [.. rest.Select(_ => Vector4.UnitX)], [0, 1, 2], [1],
            [(bone, "phys_tri_lamp_jntBnd", bind, [])], LampSettings(), "staff.ao");
        var swinger = new ClothSwinger([new ClothSwing(cloth, hand, "staff.ao")]);
        AnimationTracks tracks = swinging.Played(swinging.Animations[0], (_, _) => null);
        float rate = swinging.Animations[0].Rate > 0 ? swinging.Animations[0].Rate : 30f;

        pose.Take(tracks, 0f);
        swinger.Swing(pose, tracks, 0f, rate, tracks.Frames, swinging: true);
        float frame = 0f;
        for (var at = 1; at <= 300; at++)
        {
            frame += 0.0101f * rate;
            if (frame > tracks.Frames)
            {
                frame %= tracks.Frames;
            }

            swinger.Due(0.0101);
            pose.Take(tracks, frame);
            swinger.Swing(pose, tracks, frame, rate, tracks.Frames, swinging: true);
            ReadOnlySpan<Vector3> particles = swinger.World.Particles(0);
            Assert.True(Vector3.Distance(Vector3.Transform(rest[1], pose.Skin[hand]), particles[1]) < 1e-2f, $"frame {at}: the pinned corner on the hand");
            Assert.Equal(Vector3.Distance(rest[0], rest[1]), Vector3.Distance(particles[0], particles[1]), 1);
            Vector3 held = swinger.World.Bone(0, cloth.Links[0]).Translation;
            Assert.True(Vector3.Distance(held, Vector3.Transform(bind.Translation, pose.Skin[bone])) < 1e-2f, $"frame {at}: the bone where the cloth holds it");
        }

        Assert.Equal(300, swinger.Steps);
        swinger.Swing(pose, tracks, frame, rate, tracks.Frames, swinging: false);
        Assert.Equal(pose.Skin[hand], pose.Skin[bone]);
    }

    /// <summary>
    /// A HELD STAFF WHOSE .ao SIMULATES AND WHOSE MESH CARRIES A SIMULATION MESH gets its lamp's bone simulated: a bone appended under the hand,
    /// the lamp's vertices and its light on it, its cloth from the mesh's simulation mesh, the bone on its triangle - and the capture writes the
    /// cloth and plays it through his opening animation. Any simulation is run, under its own keys; a piece writing none simulates nothing.
    /// </summary>
    [Fact]
    public void AHELDStaffsLampIsSimulatedWhereItsAoAndMeshCarryASimulation()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["body.ao"] = Text(Body);
        install.Files["art/rig.smd"] = MonsterModelTests.Smd(lamp: true);

        // THE FIXTURE'S MESH IS WEIGHTED WHOLLY TO BONE 1 - the lamp's here.
        install.Files["staff/rig.ast"] = Packed.Skeleton(
            [("root_jntBnd", 255, 1, 0f), ("phys_tri_lamp_jntBnd", 255, 255, -50f)],
            [("idle_01", string.Empty, 2, 30, 0x6f, 0, 0)]);
        install.Files["staff/StaffHeld.ao"] = Text(Staff(Measured));

        MonsterModel model = MonsterModels.OfFiles(
            install.Read, ["body.ao"], true, null, [new WornPiece("R_wrist_jntBnd", "staff/StaffHeld.ao") { Held = true }]);
        ClothSwing swing = Assert.Single(model.Swings);
        ClothBoneLink link = Assert.Single(swing.Cloth.Links);
        AnimationSkeleton body = AnimationSkeleton.Read(install.Files["art/rig.ast"]);
        int wrist = Bone(body, "R_wrist_jntBnd");
        Assert.Equal(body.Bones.Count, link.Bone);
        Assert.Equal(wrist, swing.Socket);
        Assert.Equal("phys_tri_lamp_jntBnd", link.Name);
        Assert.Equal(ClothBoneKind.Triangle, link.Kind);
        Assert.Equal(1, model.Rig.Swung);
        Assert.Equal("swung:phys_tri_lamp_jntBnd", model.Rig.Bones[link.Bone].Name);
        Assert.Equal(3, swing.Cloth.Count);
        Assert.True(swing.Cloth.Pinned[1]);
        Matrix4x4 bind = SkeletonPose.Of(body)!.BindModel[wrist];
        Assert.True(Vector3.Distance(Vector3.Transform(new Vector3(0f, 0f, -50f), bind), link.Bind.Translation) < 1e-3f, $"the lamp rests at {link.Bind.Translation}");
        Assert.True(Vector3.Distance(Vector3.Transform(Vector3.Zero, bind), swing.Cloth.Rest[1]) < 1e-3f, $"its pinned corner at {swing.Cloth.Rest[1]}");
        Assert.Contains("StaffHeld.ao simulates 1 bones (1 on a triangle) by the engine's cloth: 3 particles, 1 triangles, 1 pinned, held at R_wrist_jntBnd", model.SwingSaid, StringComparison.Ordinal);

        // THE STAFF'S FOUR VERTICES ON THE SIMULATED BONE WHOLLY, its light with them.
        var simulated = 0;
        for (var vertex = 0; vertex < model.Mesh.Positions.Length; vertex++)
        {
            if (model.Mesh.Bones[vertex * 4] == link.Bone && model.Mesh.Weights[vertex * 4] == 255)
            {
                simulated++;
            }
        }

        Assert.Equal(4, simulated);
        Assert.Equal(link.Bone, Assert.Single(model.Carried).Bone);
        Assert.NotNull(SkeletonPose.Of(model.Rig));

        // THE CAPTURE WRITES IT.
        string text = CaptureMonsterFiles.Write(install.Read, new CapturePane("monster", [], model) { Path = "Metadata/Characters/Test" }, null, null, out _);
        Assert.Contains("=== the bones he simulates (ClothSwing, the light panel's \"swing\"): 1 cloths, 1 bones", text, StringComparison.Ordinal);
        Assert.Contains($"  {link.Bone}\tphys_tri_lamp_jntBnd\tTriangle\ttriangle 0\tR_wrist_jntBnd\t", text, StringComparison.Ordinal);

        // ANOTHER SIMULATION - the player's cape's keys - is run too, under its own keys.
        install.Files["staff/StaffHeld.ao"] = Text(Staff("\t\tlinear_allowed_contraction = 0.600\n\t\tanimation_force_factor = 10.000\n"));
        MonsterModel cape = MonsterModels.OfFiles(
            install.Read, ["body.ao"], true, null, [new WornPiece("R_wrist_jntBnd", "staff/StaffHeld.ao") { Held = true }]);
        ClothSwing caped = Assert.Single(cape.Swings);
        Assert.Equal(0.6f, caped.Cloth.Settings.LinearAllowedContraction, 3);
        Assert.Equal(10f, caped.Cloth.Settings.AnimationForceFactor, 3);
        Assert.Equal(ClothSettings.Defaults.BendStiffness, caped.Cloth.Settings.BendStiffness);

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
