using System.Numerics;
using PoEformance.Core.Tests;
using PoEformance.Game.Files;

namespace PoEformance.Gpu.Tests;

/// <summary>
/// A monster the card skins itself (CardSkin, v0.1.325) held against the processor's picture of the same pose - SkeletonPose.Move's
/// vertices, the sun's map laid over the same hull - on a real rig through its real animation.
/// </summary>
public class SkinnedGpuTests
{
    private const int Side = ModelGpuTests.Side;

    private static SceneLight Sunlit() => new([], SceneLight.PointShape.Zero, null)
    {
        SunColour = new Vector3(1.6f, 1.4f, 1.2f),
        SunDirection = Vector3.Normalize(new Vector3(1.1f, -1f, 0.6f)),
        SunShadows = true,
    };

    /// <summary>
    /// Standing alone, under the picture's lamp and under the sun, the card's skinned monster is the processor's on every frame - a pose
    /// that arrives, one held, and the next.
    /// </summary>
    [GpuFact]
    public void ASKINNEDMonsterIsTheProcessorsPoseFrameByFrame()
    {
        (SkeletonPose pose, AnimationTracks tracks, SkinnedMesh body) = RigBody.Ready();
        PoseHull? hull = PoseHull.Of(pose, body);
        Assert.NotNull(hull);
        var bones = new Matrix4x4[pose.Skin.Count];
        var posed = new Vector3[body.Positions.Length];
        var normals = new Vector3[body.Normals.Length];
        using ModelGpu? gpu = ModelGpu.Warp(out string why);
        Assert.True(gpu is not null, why);
        using ModelTarget target = gpu.Target(Side);
        foreach (SceneLight? light in (SceneLight?[])[null, Sunlit()])
        {
            long key = 0;
            float? last = null;
            foreach (float frame in (float[])[0f, tracks.Frames / 3f, tracks.Frames / 3f, tracks.Frames * 2f / 3f])
            {
                pose.Take(tracks, frame);
                key += last == frame ? 0 : 1;
                last = frame;
                pose.Move(body, posed, normals);
                pose.SkinAt(Matrix4x4.Identity, bones);
                ShadowBounds? bounds = light is null ? null : hull.Bounds(pose, light.SunDirection, Matrix4x4.Identity);
                var scene = new ModelScene(body, Turn: 0.6f, Tilt: 0.4f, Light: light, Skinned: new CardSkin(body, 0, bones, bones.Length, key, bounds));
                var canvas = new MeshPicture.Canvas(Side) { Light = light, Hull = bounds is { } held ? (0, held) : null };
                GamePicture expected = MeshPicture.Of(body, canvas, posed, normals, 0.6f, 0.4f);
                Assert.True(gpu.Can(scene, out why), why);
                Assert.True(gpu.Draw(target, scene, out why), why);
                ModelGpuTests.Compared(expected.Rgba, gpu.Read(target));
            }
        }
    }

    /// <summary>
    /// Placed on a stage, the card skins his slice of the joined mesh by the bones conjugated by his place (SkeletonPose.SkinAt) - the
    /// processor's picture of Move and then the place - and casts him over the stage's kept shadow on every new pose.
    /// </summary>
    [GpuFact]
    public void ASKINNEDMonsterOnAStageIsTheProcessorsAndIsCastOverTheKeptStage()
    {
        (SkeletonPose pose, AnimationTracks tracks, SkinnedMesh body) = RigBody.Ready();
        Matrix4x4 place = Matrix4x4.CreateScale(0.8f) * Matrix4x4.CreateRotationZ(0.7f) * Matrix4x4.CreateTranslation(40f, -30f, 0f);
        SkinnedMesh floor = Floor(body, place);
        SkinnedMesh stage = SkinnedMesh.Joined([new MeshJoin(floor), new MeshJoin(body, null, null, place)]);
        int first = floor.Positions.Length;
        PoseHull? hull = PoseHull.Of(pose, body);
        Assert.NotNull(hull);
        SceneLight light = Sunlit();
        var bones = new Matrix4x4[pose.Skin.Count];
        var posed = new Vector3[body.Positions.Length];
        var normals = new Vector3[body.Normals.Length];
        Vector3[] staged = [.. stage.Positions];
        Vector3[] stagedNormals = [.. stage.Normals];
        using ModelGpu? gpu = ModelGpu.Warp(out string why);
        Assert.True(gpu is not null, why);
        using ModelTarget target = gpu.Target(Side);
        long key = 0;
        float? last = null;
        foreach (float frame in (float[])[0f, tracks.Frames / 2f, tracks.Frames / 2f, tracks.Frames])
        {
            pose.Take(tracks, frame);
            key += last == frame ? 0 : 1;
            last = frame;

            // THE PROCESSOR'S PICTURE: MonsterPortrait's Posed - his own vertices skinned, then placed over his slice.
            pose.Move(body, posed, normals);
            for (var one = 0; one < posed.Length; one++)
            {
                staged[first + one] = Vector3.Transform(posed[one], place);
                Vector3 turned = Vector3.TransformNormal(normals[one], place);
                stagedNormals[first + one] = turned.Length() > 1e-6f ? Vector3.Normalize(turned) : normals[one];
            }

            pose.SkinAt(place, bones);
            ShadowBounds bounds = hull.Bounds(pose, light.SunDirection, place);
            var scene = new ModelScene(stage, Turn: 0.3f, Tilt: 0.5f, Light: light, Skinned: new CardSkin(body, first, bones, bones.Length, key, bounds));
            var canvas = new MeshPicture.Canvas(Side) { Light = light, Hull = (first, bounds) };
            GamePicture expected = MeshPicture.Of(stage, canvas, staged, stagedNormals, 0.3f, 0.5f);
            Assert.True(gpu.Can(scene, out why), why);
            Assert.True(gpu.Draw(target, scene, out why), why);
            ModelGpuTests.Compared(expected.Rgba, gpu.Read(target));
        }

        // CAST OVER THE KEPT STAGE on the three frames a new pose arrived; the held one read the kept map.
        Assert.Equal(3, gpu.ShadowsStaged);
    }

    /// <summary>
    /// A floor under where the stage puts him, and eight corners round all he may reach - so the stage's shadow frame holds him, as a
    /// room's holds its monster.
    /// </summary>
    private static SkinnedMesh Floor(SkinnedMesh body, Matrix4x4 place)
    {
        Vector3 least = new(float.MaxValue), most = new(float.MinValue);
        foreach (Vector3 one in body.Positions)
        {
            Vector3 at = Vector3.Transform(one, place);
            least = Vector3.Min(least, at);
            most = Vector3.Max(most, at);
        }

        Vector3 middle = (least + most) * 0.5f;
        float reach = (most - least).Length();
        float under = most.Z + 2f;
        Vector3[] positions =
        [
            new(middle.X - reach, middle.Y - reach, under), new(middle.X + reach, middle.Y - reach, under),
            new(middle.X + reach, middle.Y + reach, under), new(middle.X - reach, middle.Y + reach, under),
            .. Enumerable.Range(0, 8).Select(corner => middle + (new Vector3((corner & 1) == 0 ? -1f : 1f, (corner & 2) == 0 ? -1f : 1f, (corner & 4) == 0 ? -1f : 1f) * reach * 1.2f)),
        ];
        Vector3[] normals = [.. Enumerable.Repeat(-Vector3.UnitZ, positions.Length)];
        Vector3 low = positions.Aggregate(new Vector3(float.MaxValue), Vector3.Min), high = positions.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        return SkinnedMesh.Of(positions, normals, [0, 1, 2, 0, 2, 3], low, high);
    }
}
