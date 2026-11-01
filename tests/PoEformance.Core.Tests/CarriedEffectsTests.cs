using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The particle effects a model carries (CarriedEffects): read from an .ao's ParticleEffects and BoneGroups, its bones put on the rig as a
/// rigid piece's lights are, and every frame each emitter's frame and its bones in that frame, w the distance along them - as the engine's
/// emitter record and emitter_bones_data hold the Exile's Staff's lamp (measured, v0.1.434-436).
/// </summary>
public sealed class CarriedEffectsTests
{
    [Fact]
    public void ANEFFECTSBonesStandInItsFrameAndWCountsTheWayAlongThem()
    {
        Matrix4x4 place = Matrix4x4.CreateRotationZ(0.5f) * Matrix4x4.CreateTranslation(10f, 0f, 0f);
        var effect = new CarriedEffect("a.pet", "lamp", [new CarriedPoint(0, Vector3.Transform(Vector3.Zero, place), "a"), new CarriedPoint(0, Vector3.Transform(new Vector3(3f, 4f, 0f), place), "b")], 0, place, "piece.ao");
        Matrix4x4[] skin = [Matrix4x4.CreateTranslation(0f, 0f, 7f)];
        Matrix4x4 stage = Matrix4x4.CreateTranslation(100f, 0f, 0f);
        Span<Vector4> bones = stackalloc Vector4[4];

        (Matrix4x4 world, int count) = CarriedEffects.At(effect, skin, stage, bones);

        // THE FRAME IS THE PIECE'S PLACE THROUGH ITS BONE, THEN THE STAGE - and the bones come back into the piece's own frame.
        Assert.Equal(2, count);
        AssertNear(Vector3.Transform(Vector3.Zero, place * skin[0] * stage), world.Translation);
        AssertNear(Vector3.Zero, new Vector3(bones[0].X, bones[0].Y, bones[0].Z));
        AssertNear(new Vector3(3f, 4f, 0f), new Vector3(bones[1].X, bones[1].Y, bones[1].Z));
        Assert.Equal(0f, bones[0].W);
        Assert.Equal(5f, bones[1].W, 4);
    }

    [Fact]
    public void ARIGIDPiecesGroupIsReadFromItsFileItsSocketAlongAndBothMoveToTheSwungBone()
    {
        AnimationSkeleton rig = RigBody.Rig();
        string first = rig.Bones[1].Name, second = rig.Bones[2].Name;
        string ao = $$"""
            version 3
            extends "nothing"

            ClientAnimationController
            {
            	skeleton = "Art/rig.ast"
            	socket = "tip_01"
            		parent = "{{second}}"
            		translation = "0 10 0"
            }

            BoneGroups
            {
            	bone_group = "lamp false {{first}} tip_01"
            	bone_group = "other false {{second}}"
            }

            ParticleEffects
            {
            	continuous_effect = "lamp Metadata/x/fx/lamp.pet"
            }
            """;
        byte[]? Read(string path) => path switch
        {
            "piece.ao" => Encoding.UTF8.GetBytes(ao),
            "Art/rig.ast" => RigBody.Bytes(),
            _ => null,
        };

        var gathering = new CarriedEffects.Gathering(Read);
        List<AnimatedObject> chain = ModelDump.Whole(Read, AnimatedObject.Read(Read("piece.ao")));
        Matrix4x4 place = Matrix4x4.CreateTranslation(1f, 2f, 3f);
        (int from, int count) = gathering.Rigid(chain, "piece.ao", place, 5);
        Assert.Equal((0, 2), (from, count));

        // THE SWUNG BONE TAKES THE GROUP'S BONE BY ITS NAME AND THE SOCKET BY ITS PARENT'S.
        gathering.Rebone((from, count), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { first }, 9);
        gathering.Rebone((from, count), new HashSet<string>(StringComparer.OrdinalIgnoreCase) { second }, 11);
        (IReadOnlyList<CarriedEffect> effects, string said) = gathering.Made(rig);

        CarriedEffect lamp = Assert.Single(effects);
        Assert.Equal(("Metadata/x/fx/lamp.pet", "lamp", 5), (lamp.Effect, lamp.Group, lamp.Frame));
        SkeletonPose rest = SkeletonPose.Of(rig)!;
        Assert.Equal([9, 11], lamp.Points.Select(one => one.Bone));
        AssertNear(Vector3.Transform(rest.BindModel[1].Translation, place), lamp.Points[0].Rest);
        AssertNear(Vector3.Transform((Matrix4x4.CreateTranslation(0f, 10f, 0f) * rest.BindModel[2]).Translation, place), lamp.Points[1].Rest);
        Assert.Contains("1 particle effects carried (lamp.pet on lamp of piece.ao, 2 bones)", said, StringComparison.Ordinal);
    }

    [Fact]
    public void THEWRAPPEDPixelEntryCallsTheLinkedOneWithItsOwnInputsAndHandsOnItsColour()
    {
        const string pixel = """
            struct PixelOut { float4 target_PIXEL_RETURN_SEMANTIC_COLOR : SV_Target0; };
            PixelOut PixelMain(Interstage pin, bool sv_IsFrontFace : SV_IsFrontFace)
            {
                PixelOut pout = (PixelOut)0;
                return pout;
            }
            """;
        string? wrapped = ParticleLinker.Wrapped(pixel, "PixelFinished", "static float Extra = 1.0f;", "    return colour * Extra;");

        Assert.NotNull(wrapped);
        Assert.Contains("float4 PixelFinished(Interstage pin, bool sv_IsFrontFace : SV_IsFrontFace) : SV_Target0", wrapped, StringComparison.Ordinal);
        Assert.Contains("PixelOut held = PixelMain(pin, sv_IsFrontFace);", wrapped, StringComparison.Ordinal);
        Assert.Contains("float4 colour = held.target_PIXEL_RETURN_SEMANTIC_COLOR;", wrapped, StringComparison.Ordinal);
        Assert.True(wrapped.IndexOf("static float Extra", StringComparison.Ordinal) < wrapped.IndexOf("float4 PixelFinished", StringComparison.Ordinal));
        Assert.Null(ParticleLinker.Wrapped("float4 Other() : SV_Target { return 0; }", "PixelFinished", string.Empty, string.Empty));
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
        => Assert.True(Vector3.Distance(expected, actual) < 1e-3f, $"expected {expected}, was {actual}");
}
