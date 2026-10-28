using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// A piece on a rig of its own whose animations carry its carrier's names rides the carrier's rig: its bones after his, its own
/// animation of each name played in step - Malgor's ship's wheel, which every other answer left at his origin (v0.1.329).
/// </summary>
public sealed class RiderTests
{
    /// <summary>
    /// Carried, a rig poses its own bones as it does alone and the rider's as the rider does alone - the real blackguard rig carrying
    /// itself, its one animation of the same name and rate.
    /// </summary>
    [Fact]
    public void ARIGCarryingAnotherPosesBothAsEachPosesAlone()
    {
        (SkeletonPose alone, AnimationTracks tracks, _) = RigBody.Ready();
        AnimationSkeleton rig = RigBody.Rig();
        AnimationSkeleton carrying = rig.Carrying([rig])!;
        Assert.Equal(rig.Bones.Count * 2, carrying.Bones.Count);
        Assert.Equal([(rig, rig.Bones.Count)], carrying.Riders);

        AnimationTracks joined = carrying.Played(carrying.Animations[0], (_, _) => null);
        Assert.True(joined.Ready, joined.Why);
        Assert.Equal(tracks.Frames, joined.Frames);
        SkeletonPose both = SkeletonPose.Of(carrying)!;

        foreach (float frame in (float[])[0f, tracks.Frames * 0.37f, tracks.Frames])
        {
            alone.Take(tracks, frame);
            both.Take(joined, frame);
            for (var bone = 0; bone < rig.Bones.Count; bone++)
            {
                Same(alone.World[bone], both.World[bone]);
                Same(alone.World[bone], both.World[rig.Bones.Count + bone]);
            }
        }
    }

    /// <summary>
    /// A rider's keys are timed in this animation's frames: at half the rate, its frame t is the carrier's 2t.
    /// </summary>
    [Fact]
    public void ARIDERSKeysAreTimedInTheCarriersFrames()
    {
        (SkeletonPose alone, AnimationTracks tracks, _) = RigBody.Ready();
        AnimationSkeleton rig = RigBody.Rig();
        AnimationSkeleton carrying = rig.Carrying([rig])!;
        AnimationTracks joined = tracks.Mine(rig.Bones.Count).With(tracks, rig.Bones.Count, rig.Bones.Count, 2f);
        SkeletonPose both = SkeletonPose.Of(carrying)!;

        float frame = tracks.Frames * 0.21f;
        alone.Take(tracks, frame);
        both.Take(joined, frame * 2f);
        for (var bone = 0; bone < rig.Bones.Count; bone++)
        {
            Same(alone.World[bone], both.World[rig.Bones.Count + bone]);
        }
    }

    /// <summary>A track past the rig's own bones - a light's - is not handed to the first rider's bone.</summary>
    [Fact]
    public void ATRACKPastTheRigsBonesIsLeftOut()
    {
        (_, AnimationTracks tracks, _) = RigBody.Ready();
        AnimationTracks mine = tracks.Mine(10);
        Assert.NotEmpty(mine.Tracks);
        Assert.All(mine.Tracks, one => Assert.InRange(one.Bone, 0, 9));
        Assert.Same(tracks, tracks.Mine(RigBody.Rig().Bones.Count));
    }

    /// <summary>
    /// A "&lt;root&gt;" piece whose rig shares only the root with him, and whose animations carry his names, rides his rig: its bones
    /// numbered after his, its part told as played. A rig whose animations carry none of his names does not ride.
    /// </summary>
    [Theory]
    [InlineData("idle_01", true)]
    [InlineData("spin", false)]
    public void APIECEWhoseAnimationsCarryHisNamesRidesHisRig(string animation, bool rides)
    {
        var install = MonsterModelTests.Install();
        install.Files["art/rig.ast"] = Packed.Skeleton(
            [("root_jntBnd", 255, 1, 0f), ("chest_jntBnd", 255, 255, -90f)],
            [("idle_01", string.Empty, 2, 30, 0x6c, 0, 0)]);
        install.Files["body.ao"] = MonsterModelTests.Ao(skin: "art/mesh.sm", attach: "art/wheel.ao", socket: "<root>", skeleton: "art/rig.ast");
        install.Files["art/wheel.ao"] = MonsterModelTests.Ao(skin: "art/wheel.sm", skeleton: "art/wheel.ast");
        install.Files["art/wheel.ast"] = Packed.Skeleton(
            [("root_jntBnd", 255, 1, 0f), ("helm_jntBnd", 255, 255, -200f)],
            [(animation, string.Empty, 2, 60, 0x6c, 0, 0)]);
        install.Files["art/wheel.sm"] = MonsterModelTests.Sm("art/wheel.smd", "art/paint.mat");
        install.Files["art/wheel.smd"] = MonsterModelTests.Smd();

        MonsterModel said = MonsterModels.Of(install.Read, MonsterModelTests.Named("body.ao"));

        Assert.Equal(1, said.Parts);
        PartFit wheel = Assert.Single(said.Fitted);
        if (rides)
        {
            Assert.Equal(PartKind.Played, wheel.Kind);
            Assert.Equal(4, said.Rig.Bones.Count);
            Assert.Equal(2, Assert.Single(said.Rig.Riders).First);

            // THE FIXTURE BINDS EVERY VERTEX TO ITS OWN BONE 1, the helm - his bone 3 now.
            Assert.Equal(3, said.Mesh.Bones[16]);
            Assert.Equal<byte>([255, 0, 0, 0], said.Mesh.Weights[16..20]);

            // AND HE STILL MOVES: v0.1.329 said the riding in Move, which is why a model does NOT animate, and stopped him whole.
            Assert.Equal(string.Empty, said.Move);
            Assert.True(said.Moves);
        }
        else
        {
            Assert.Equal(PartKind.Rigid, wheel.Kind);
            Assert.Equal(2, said.Rig.Bones.Count);
            Assert.Empty(said.Rig.Riders);
        }
    }

    private static void Same(Matrix4x4 expected, Matrix4x4 actual)
    {
        for (var row = 0; row < 4; row++)
        {
            for (var column = 0; column < 4; column++)
            {
                Assert.Equal(expected[row, column], actual[row, column], 3);
            }
        }
    }
}
