using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// What the processor hands the graphics card so it can skin a monster itself (v0.1.325): the hull the sun's map is laid over, the
/// lowest point without the posed vertices, and the bones' matrices for a monster placed on a stage - each held against
/// SkeletonPose.Move, on a real rig through its real animation.
/// </summary>
public class CardSkinTests
{
    /// <summary>Where the monster stands on a stage - a scale, a turn and a move, as StagedModels places one.</summary>
    private static readonly Matrix4x4 OnAStage = Matrix4x4.CreateScale(0.7f) * Matrix4x4.CreateRotationZ(0.9f) * Matrix4x4.CreateTranslation(300f, -120f, 40f);

    private static readonly Vector3[] Suns =
    [
        Vector3.Normalize(new Vector3(1.1f, -1f, 0.6f)), Vector3.Normalize(new Vector3(-0.2f, 0.3f, 1f)), Vector3.Normalize(new Vector3(0.7f, 0.7f, -0.1f)),
    ];

    /// <summary>
    /// The hull holds every posed vertex, whatever the frame, the sun or the place - and is no more than a little looser than they are.
    /// </summary>
    /// <remarks>
    /// THE MAP'S FRAME FROM THE HULL MUST NOT CUT A LIMB OFF, which a hull short of a vertex would: the card's map is laid over it alone.
    /// The looseness is bounded too, so a hull grown to the whole rig's reach every frame - a frame that holds, and a shadow drawn on a
    /// texel far coarser than it needs - would not pass.
    /// </remarks>
    [Fact]
    public void THEHULLHoldsEveryPosedVertexThroughTheWholeAnimation()
    {
        (SkeletonPose pose, AnimationTracks tracks, SkinnedMesh body) = RigBody.Ready();
        PoseHull? hull = PoseHull.Of(pose, body);
        Assert.NotNull(hull);
        var posed = new Vector3[body.Positions.Length];
        var normals = new Vector3[body.Normals.Length];
        var placed = new Vector3[posed.Length];
        float loosest = 0f;
        foreach (Matrix4x4 place in (Matrix4x4[])[Matrix4x4.Identity, OnAStage])
        {
            for (var frame = 0f; frame <= tracks.Frames; frame += 0.5f)
            {
                pose.Take(tracks, frame);
                pose.Move(body, posed, normals);
                for (var one = 0; one < posed.Length; one++)
                {
                    placed[one] = Vector3.Transform(posed[one], place);
                }

                foreach (Vector3 sun in Suns)
                {
                    ShadowBounds own = ShadowMap.Bounded(placed, sun);
                    ShadowBounds held = hull.Bounds(pose, sun, place);
                    const float Rounding = 1e-2f;
                    Assert.True(
                        held.Least.X <= own.Least.X + Rounding && held.Least.Y <= own.Least.Y + Rounding
                        && held.Most.X >= own.Most.X - Rounding && held.Most.Y >= own.Most.Y - Rounding && held.Nearest <= own.Nearest + Rounding,
                        $"frame {frame}: the hull {held} does not hold the pose {own}");
                    // THE LONGER SIDE, which sets the map's texel (ShadowMap.Framed).
                    Vector2 span = own.Most - own.Least, wide = held.Most - held.Least;
                    loosest = MathF.Max(loosest, MathF.Max(wide.X, wide.Y) / MathF.Max(span.X, span.Y));
                }
            }
        }

        // MEASURED on this body: 1.24 at the median, 1.54 at most - boxes turned to each bone's own spread came to 1.20 and 1.53.
        Assert.True(loosest < 1.6f, $"the hull's map reaches {loosest:0.00} times as far as the pose's");
    }

    /// <summary>The lowest point worked out without moving anything is the lowest point Move leaves, to the bit, frame by frame.</summary>
    [Fact]
    public void THELOWESTPointIsTheOneMoveLeaves()
    {
        (SkeletonPose pose, AnimationTracks tracks, SkinnedMesh body) = RigBody.Ready();
        var posed = new Vector3[body.Positions.Length];
        var normals = new Vector3[body.Normals.Length];
        for (var frame = 0f; frame <= tracks.Frames; frame += 0.5f)
        {
            pose.Take(tracks, frame);
            pose.Move(body, posed, normals);
            Assert.Equal(posed.Max(one => one.Z), pose.Lowest(body));
        }
    }

    /// <summary>
    /// The card's arithmetic on a stage - the placed vertex skinned by the skin conjugated by the place (SkinAt), weight by weight as its
    /// vertex shader does - puts every vertex where Move and then the place put it, and turns its normal the same way.
    /// </summary>
    [Fact]
    public void ASKINAtAPlaceMovesAPlacedVertexWhereMoveAndThenThePlacePutIt()
    {
        (SkeletonPose pose, AnimationTracks tracks, SkinnedMesh body) = RigBody.Ready();
        var posed = new Vector3[body.Positions.Length];
        var normals = new Vector3[body.Normals.Length];
        var bones = new Matrix4x4[pose.Skin.Count];
        Vector3[] placed = [.. body.Positions.Select(one => Vector3.Transform(one, OnAStage))];
        Vector3[] placedNormals = [.. body.Normals.Select(one => Vector3.Normalize(Vector3.TransformNormal(one, OnAStage)))];
        float worst = 0f, worstTurn = 1f;
        for (var frame = 0f; frame <= tracks.Frames; frame += 1f)
        {
            pose.Take(tracks, frame);
            pose.Move(body, posed, normals);
            pose.SkinAt(OnAStage, bones);
            for (var one = 0; one < posed.Length; one++)
            {
                (Vector3 at, Vector3 facing) = Shaded(body, one, placed[one], placedNormals[one], bones);
                Vector3 expected = Vector3.Transform(posed[one], OnAStage);
                Vector3 turned = Vector3.Normalize(Vector3.TransformNormal(normals[one], OnAStage));
                worst = MathF.Max(worst, Vector3.Distance(at, expected));
                worstTurn = MathF.Min(worstTurn, Vector3.Dot(facing, turned));
            }
        }

        // A HUNDREDTH OF A UNIT where the stage puts him hundreds of units out, a monster a hundred-odd tall.
        Assert.True(worst < 1e-2f, $"a vertex lands {worst} from where Move and the place put it");
        Assert.True(worstTurn > 0.99999f, $"a normal turns {MathF.Acos(MathF.Min(1f, worstTurn))} radians off");
    }

    /// <summary>The identity place hands the skin matrices over as they are.</summary>
    [Fact]
    public void ASKINAtNoPlaceIsTheSkinItself()
    {
        (SkeletonPose pose, AnimationTracks tracks, _) = RigBody.Ready();
        pose.Take(tracks, tracks.Frames / 2f);
        var bones = new Matrix4x4[pose.Skin.Count];
        pose.SkinAt(Matrix4x4.Identity, bones);
        Assert.Equal(pose.Skin, bones);
    }

    /// <summary>
    /// The processor lays the sun's map over a hull it is handed exactly as over the vertices the hull stands for: handed the pose's own
    /// bounds, alone and with a stage before it, the picture is the one drawn without a hull.
    /// </summary>
    [Fact]
    public void AHULLThatIsThePosesOwnBoundsLaysTheMapAsThePoseDoes()
    {
        (SkeletonPose pose, AnimationTracks tracks, SkinnedMesh body) = RigBody.Ready();
        var light = new SceneLight([], SceneLight.PointShape.Zero, null)
        {
            SunColour = new Vector3(1.6f, 1.4f, 1.2f),
            SunDirection = Suns[0],
            SunShadows = true,
        };
        pose.Take(tracks, tracks.Frames / 3f);
        var posed = new Vector3[body.Positions.Length];
        var normals = new Vector3[body.Normals.Length];
        pose.Move(body, posed, normals);

        GamePicture plain = MeshPicture.Of(body, new MeshPicture.Canvas(160) { Light = light }, posed, normals, 0.6f, 0.4f);
        GamePicture hulled = MeshPicture.Of(
            body, new MeshPicture.Canvas(160) { Light = light, Hull = (0, ShadowMap.Bounded(posed, light.SunDirection)) }, posed, normals, 0.6f, 0.4f);
        Assert.Equal(plain.Rgba, hulled.Rgba);

        // A STAGE BEFORE HIM: the floor's own vertices bounded where the mesh puts them, his by the hull.
        SkinnedMesh floor = SkinnedMesh.Of(
            [new(-400f, -400f, 200f), new(400f, -400f, 200f), new(400f, 400f, 200f), new(-400f, 400f, 200f)],
            [-Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ, -Vector3.UnitZ], [0, 1, 2, 0, 2, 3], new Vector3(-400f, -400f, 200f), new Vector3(400f, 400f, 200f));
        SkinnedMesh stage = SkinnedMesh.Joined([new MeshJoin(floor), new MeshJoin(body)]);
        int first = floor.Positions.Length;
        Vector3[] staged = [.. floor.Positions, .. posed];
        Vector3[] stagedNormals = [.. floor.Normals, .. normals];
        GamePicture whole = MeshPicture.Of(stage, new MeshPicture.Canvas(160) { Light = light }, staged, stagedNormals, 0.6f, 0.4f);
        GamePicture parted = MeshPicture.Of(
            stage, new MeshPicture.Canvas(160) { Light = light, Hull = (first, ShadowMap.Bounded(posed, light.SunDirection)) }, staged, stagedNormals, 0.6f, 0.4f);
        Assert.Equal(whole.Rgba, parted.Rgba);
    }

    /// <summary>ModelShaders' Skinned, in C#: the weighted mean of where each weight's bone puts the vertex, the normal made unit length again.</summary>
    private static (Vector3 At, Vector3 Facing) Shaded(SkinnedMesh mesh, int one, Vector3 at, Vector3 facing, Matrix4x4[] bones)
    {
        Vector3 moved = Vector3.Zero, turned = Vector3.Zero;
        var weight = 0f;
        for (var part = 0; part < 4; part++)
        {
            byte heavy = mesh.Weights[(one * 4) + part];
            int bone = mesh.Bones[(one * 4) + part];
            if (heavy == 0 || bone >= bones.Length)
            {
                continue;
            }

            float share = heavy / 255f;
            moved += Vector3.Transform(at, bones[bone]) * share;
            turned += Vector3.TransformNormal(facing, bones[bone]) * share;
            weight += share;
        }

        if (weight <= 0f)
        {
            return (at, facing);
        }

        float length = turned.Length();
        return (moved / weight, length > 1e-6f ? turned / length : facing);
    }
}
