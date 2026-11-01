using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The point lights a monster and his pieces carry on their bones (v0.1.403): the Exile's Staff's lamp on its phys_tri_lamp_jntBnd, read from
/// the piece's .ao as a room's lights are, carried by the bone that carries the piece, and stood where each pose puts that bone.
/// </summary>
public sealed class CarriedLightsTests
{
    /// <summary>A body on the fixture's rig, its client block closed by the last two characters.</summary>
    private const string Body =
        "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t}\n\tSkinMesh\n\t{\n\t\tskin = \"art/mesh.sm\"\n\t}\n}\n";

    /// <summary>WandererStaffHeld.ao's Lights block as the capture of 00:19 wrote it.</summary>
    private const string Lamp =
        "\tLights\n\t{\n\t\tlight = \"point_light\"\n\t\t\tbone_name = \"phys_tri_lamp_jntBnd\"\n\t\t\tstate = \"\"\n"
        + "\t\t\t\tcolour = \"2.4671 0.493089 0.0398032\"\n\t\t\t\tradius = 150\n\t\t\t\tpenumbra_dist = 0.5\n\t\t\t\tpenumbra_radius = 0.01\n\t\t\t\tstyle = 1\n\t}\n";

    /// <summary>
    /// A held piece's light stands where its own rig rests the bone it names, put through the piece's place, and is carried by the bone the
    /// piece hangs at - as the piece's vertices are; a pose moves it through that bone's skin matrix, then the model's place.
    /// </summary>
    [Fact]
    public void ALIGHTOnAHeldPiecesBoneIsCarriedByTheBoneItHangsAt()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["body.ao"] = Text(Body);
        install.Files["staff/rig.ast"] = Packed.Skeleton(
            [("root_jntBnd", 255, 1, 0f), ("staff_jntBnd", 255, 2, -50f), ("phys_tri_lamp_jntBnd", 255, 255, -100f)],
            [("idle_01", string.Empty, 3, 30, 0x6f, 0, 0)]);
        install.Files["staff/StaffHeld.ao"] = Text(
            "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"staff/rig.ast\"\n\t}\n\tSkinMesh\n\t{\n\t\tskin = \"art/mesh.sm\"\n\t}\n"
            + Lamp + "}\n");

        MonsterModel model = MonsterModels.OfFiles(
            install.Read, ["body.ao"], true, null, [new WornPiece("R_wrist_jntBnd", "staff/StaffHeld.ao") { Held = true }]);
        CarriedLight lamp = Assert.Single(model.Carried);

        // ON THE WRIST, where the staff hangs, at the lamp's bone as the staff's rig rests it (0, 0, -150) put through the wrist's bind.
        AnimationSkeleton rig = AnimationSkeleton.Read(install.Files["art/rig.ast"]);
        int wrist = Bone(rig, "R_wrist_jntBnd");
        Assert.Equal(wrist, lamp.Bone);
        Vector3 rest = Vector3.Transform(new Vector3(0f, 0f, -150f), SkeletonPose.Of(rig)!.BindModel[wrist]);
        Assert.True(Vector3.Distance(rest, lamp.Rest) < 1e-3f, $"the lamp rests at {lamp.Rest}, the wrist's bind puts its bone at {rest}");
        Assert.Equal(new Vector3(2.4671f, 0.493089f, 0.0398032f), lamp.Colour);
        Assert.Equal(150f, lamp.Radius);
        Assert.Equal(0.5f, lamp.PenumbraDist);
        Assert.Equal("phys_tri_lamp_jntBnd", lamp.BoneName);
        Assert.Equal(1, lamp.Style);
        Assert.Equal("1 point lights carried (StaffHeld.ao (unnamed state) on phys_tri_lamp_jntBnd)", model.CarriedSaid);

        // THE POSE MOVES IT AS IT MOVES THE VERTICES BOUND TO THE WRIST: the bone's skin matrix, then where the model stands.
        var skin = new Matrix4x4[rig.Bones.Count];
        Array.Fill(skin, Matrix4x4.Identity);
        skin[wrist] = Matrix4x4.CreateRotationZ(0.7f) * Matrix4x4.CreateTranslation(3f, -4f, 5f);
        Matrix4x4 place = Matrix4x4.CreateRotationZ(1.1f) * Matrix4x4.CreateTranslation(100f, 200f, -40f);
        var at = new PointLight[1];
        Assert.Equal(1, CarriedLights.At(model.Carried, skin, place, at));
        Assert.Equal(Vector3.Transform(Vector3.Transform(lamp.Rest, skin[wrist]), place), at[0].Position);
        Assert.Equal(lamp.Colour, at[0].Colour);

        // NO POSE, the bind's: where it rests.
        Assert.Equal(1, CarriedLights.At(model.Carried, [], Matrix4x4.Identity, at));
        Assert.Equal(lamp.Rest, at[0].Position);

        // THE CAPTURE WRITES BOTH HALVES: the bone of his that moves it, the bone its own file names, where it rests and what it is.
        string text = CaptureMonsterFiles.Write(install.Read, new CapturePane("monster", [], model) { Path = "Metadata/Characters/Test" }, null, null, out _);
        Assert.Contains("=== the lights he carries on his bones (CarriedLights, the light panel's \"carried\"): 1 ", text, StringComparison.Ordinal);
        Assert.Contains($"  {wrist}\t{rig.Bones[wrist].Name}\tphys_tri_lamp_jntBnd\t", text, StringComparison.Ordinal);
        Assert.Contains("\t150\t0.5\t1 (flickers, measured)\tstaff/StaffHeld.ao (unnamed state)", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The body's own light stands on its rig's bone, its position line in that bone's frame; a light whose default state has no colour and a
    /// spot light are left out and said so.
    /// </summary>
    [Fact]
    public void THEBODYSOwnLightStandsOnItsBoneAndWhatIsLeftOutIsSaid()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["body.ao"] = Text(
            Body[..^2]
            + "\tLights\n\t{\n"
            + "\t\tlight = \"point_light\"\n\t\t\tbone_name = \"R_wrist_jntBnd\"\n\t\t\tposition = \"0 0 10\"\n\t\t\tstate = \"on\"\n\t\t\t\tcolour = \"1 0.5 0.25\"\n\t\t\t\tradius = 80\n"
            + "\t\tlight = \"spot_light\"\n\t\t\tstate = \"\"\n\t\t\t\tcolour = \"1 1 1\"\n"
            + "\t\tlight = \"point_light\"\n\t\t\tstate = \"spear_off\"\n\t\t\t\tdefault_state = true\n"
            + "\t}\n}\n");

        MonsterModel model = MonsterModels.OfFiles(install.Read, ["body.ao"], true, null);
        CarriedLight own = Assert.Single(model.Carried);
        AnimationSkeleton rig = AnimationSkeleton.Read(install.Files["art/rig.ast"]);
        int wrist = Bone(rig, "R_wrist_jntBnd");
        Assert.Equal(wrist, own.Bone);
        Vector3 rest = Vector3.Transform(new Vector3(0f, 0f, 10f), SkeletonPose.Of(rig)!.BindModel[wrist]);
        Assert.True(Vector3.Distance(rest, own.Rest) < 1e-3f, $"the light rests at {own.Rest}, ten along the wrist is {rest}");
        Assert.Equal(new Vector3(1f, 0.5f, 0.25f), own.Colour);
        Assert.Contains("1 in a state with no colour (off)", model.CarriedSaid, StringComparison.Ordinal);
        Assert.Contains("1 spot lights not drawn", model.CarriedSaid, StringComparison.Ordinal);

        // A MONSTER CARRYING NONE says nothing.
        install.Files["body.ao"] = Text(Body);
        MonsterModel bare = MonsterModels.OfFiles(install.Read, ["body.ao"], true, null);
        Assert.Empty(bare.Carried);
        Assert.Equal(string.Empty, bare.CarriedSaid);
    }

    /// <summary>
    /// A carried light lights a surface exactly as the same light in the room's grid would - the shared fall-off, no grid asked - on a copy that
    /// shares the room's lights; the light handed out is left as it was, the panel's switch off hands the light back carrying nothing, and the
    /// first eight are kept.
    /// </summary>
    [Fact]
    public void ACARRIEDLightLightsAsTheSameLightInTheRoomWould()
    {
        var lamp = new PointLight(new Vector3(30f, -20f, -40f), new Vector3(2.4671f, 0.493089f, 0.0398032f), 150f, 0.5f, "lamp");
        var room = new SceneLight([lamp], SceneLight.PointShape.Penumbra, null) { Ambient = SceneAmbient.None };
        var bare = new SceneLight([], SceneLight.PointShape.Penumbra, null) { Ambient = SceneAmbient.None };
        SceneLight carrying = bare.Carrying([lamp]);
        Assert.Equal(1, carrying.Carried.Length);
        Assert.Equal(0, bare.Carried.Length);
        Assert.Same(bare.PointsOwner, carrying.PointsOwner);

        Vector3 toEye = Vector3.Normalize(new Vector3(0.3f, -1f, -0.6f));
        foreach (Vector3 place in (Vector3[])[Vector3.Zero, new(60f, 10f, -40f), new(30f, -20f, 20f), new(120f, -100f, 30f), new(900f, 0f, 0f)])
        {
            Vector3 normal = Vector3.Normalize(new Vector3(0.2f, -0.4f, -1f));
            Assert.Equal(
                room.Shade(new Vector3(0.5f), place, normal, toEye, 1f, false, default, 0f, default),
                carrying.Shade(new Vector3(0.5f), place, normal, toEye, 1f, false, default, 0f, default));
            Assert.Equal(
                room.Shade(new Vector3(0.5f), place, normal, toEye, 1f, true, new Vector3(0.04f), 0.7f, default),
                carrying.Shade(new Vector3(0.5f), place, normal, toEye, 1f, true, new Vector3(0.04f), 0.7f, default));
        }

        // IT LIGHTS WHAT IT REACHES: the lamp's own glow on the floor under it.
        Assert.NotEqual(Vector3.Zero, carrying.Shade(new Vector3(0.5f), new Vector3(30f, -20f, 0f), -Vector3.UnitZ, -Vector3.UnitZ, 1f, false, default, 0f, default));

        var off = new SceneLight([], SceneLight.PointShape.Penumbra, null) { Carries = false };
        Assert.Same(off, off.Carrying([lamp]));
        Assert.Equal(SceneLight.MostCarried, bare.Carrying(Enumerable.Repeat(lamp, SceneLight.MostCarried + 4).ToArray()).Carried.Length);
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
