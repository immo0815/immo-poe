using System.Text;
using PoEformance.Features;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// How worn pieces cover what they are worn over (PieceHiding, MonsterModels' dressing): an armour skin's ObjectHiding read through what it
/// extends, a plain key replacing and an add_ key adding; a name a group of the target's skin or a shape itself; the default outfit an
/// attached_object line names by its alias left off under armour and its body's segments hidden; the armour skinned on its carrier's rig
/// though it brings none; and a shape's material written beside the skin rather than under it.
/// </summary>
public class PieceHidingTests
{
    /// <summary>
    /// The robe's case: SkinBodyArmour hides the body's Body_Seg and its Body_Attach and its siblings' groups; the robe empties the first
    /// with a plain key and adds a shape to the third - read from the furthest file down.
    /// </summary>
    [Fact]
    public void APIECESHidingIsReadThroughItsChainAPlainKeyReplacingAndAnAddKeyAdding()
    {
        AnimatedObject parent = AnimatedObject.Parse(
            "version 3\nextends \"Metadata/Skin\"\nclient\n{\n\tObjectHiding\n\t{\n"
            + "\t\thide_parent_attachments = \"Body_Attach\"\n\t\thide_parent_segments = \"Body_Seg\"\n"
            + "\t\thide_sibling_segments = \"AP_Body_Hides, AP_Helmet_Hides\"\n"
            + "\t\thide_sibling_attachments = \"AP_Helmet_Attach,AP_Boots_Attach\"\n"
            + "\t\tshow_sibling_segments = \"AP_Helmet_Show\"\n\t}\n}\n");
        AnimatedObject robe = AnimatedObject.Parse(
            "version 3\nextends \"Metadata/SkinBodyArmour\"\nclient\n{\n\tObjectHiding\n\t{\n"
            + "\t\thide_parent_segments = \"\"\n\t\tadd_hide_sibling_segments = \"BD_skirt_meshShape\"\n\t}\n}\n");

        PieceHiding hiding = PieceHiding.Of([robe, parent]);
        Assert.Empty(hiding.ParentSegments);
        Assert.Equal(["Body_Attach"], hiding.ParentAttachments);
        Assert.Equal(["AP_Body_Hides", "AP_Helmet_Hides", "BD_skirt_meshShape"], hiding.SiblingSegments);
        Assert.Equal(["AP_Helmet_Attach", "AP_Boots_Attach"], hiding.SiblingAttachments);
        Assert.Equal(["AP_Helmet_Show"], hiding.Shown);
        Assert.True(hiding.Any);

        // THE PARENT ALONE hides the body's segments; nothing at all hides nothing.
        Assert.Equal(["Body_Seg"], PieceHiding.Of([parent]).ParentSegments);
        Assert.Same(PieceHiding.None, PieceHiding.Of([AnimatedObject.Parse("version 3\nextends \"nothing\"\n")]));
    }

    /// <summary>A name is a group of the target's skin where it names one, its shapes by commas, else a shape itself.</summary>
    [Fact]
    public void ANAMEIsAGroupOfTheSkinOrAShapeItself()
    {
        AnimatedObject body = AnimatedObject.Parse(
            "version 3\nclient\n{\n\tSkinMesh\n\t{\n\t\talias = \"Body_Seg L_chest_meshShape, torso_1_meshShape\"\n"
            + "\t\talias = \"Gloves_Seg L_palm_meshShape\"\n\t\tskin = \"Art/Models/CharactersFour/Witch.sm\"\n\t}\n}\n");
        IReadOnlyDictionary<string, string[]> groups = PieceHiding.Aliases([body]);
        Assert.Equal(["L_chest_meshShape", "torso_1_meshShape"], groups["Body_Seg"]);
        Assert.Equal(
            ["L_chest_meshShape", "torso_1_meshShape", "R_palm_meshShape"],
            PieceHiding.Shapes(["Body_Seg", "R_palm_meshShape"], groups));
    }

    /// <summary>
    /// The player's case in small: his body names a default outfit by the alias Body_Attach and its skin's shape as the group Body_Seg; the
    /// armour worn beside his files' own lines extends a parent whose ObjectHiding hides both, and brings no rig of its own - so the outfit
    /// is left off, the body's shape hidden, and the armour skinned on his rig by its own numbers.
    /// </summary>
    [Fact]
    public void ARMOURWornOverTheBodyLeavesItsDefaultOutfitOffAndHidesItsSegments()
    {
        MonsterModelTests.Fake install = Body();
        install.Files["art/armour.ao"] = Text("version 3\nextends \"art/skinbody\"\nclient\n{\n\tSkinMesh\n\t{\n\t\tskin = \"art/armour.sm\"\n\t}\n}\n");
        install.Files["art/skinbody.ao"] = Text(
            "version 3\nextends \"nothing\"\nclient\n{\n\tObjectHiding\n\t{\n\t\thide_parent_attachments = \"Body_Attach\"\n"
            + "\t\thide_parent_segments = \"Body_Seg\"\n\t}\n}\n");
        install.Files["art/armour.sm"] = MonsterModelTests.Sm("art/rig.smd", "art/paint.mat");

        MonsterModel bare = MonsterModels.OfFiles(install.Read, ["body.ao"], true, null);
        Assert.Equal(1, bare.Parts);
        Assert.Equal(4, bare.Mesh.Triangles);
        Assert.Empty(bare.Covered);

        MonsterModel dressed = MonsterModels.OfFiles(install.Read, ["body.ao"], true, null, [new WornPiece("<root>", "art/armour.ao")]);
        string said = string.Join('\n', dressed.Covered);
        Assert.Contains("outfit.ao (Body_Attach) left off: armour.ao hides it", said, StringComparison.Ordinal);
        Assert.Contains("armour.ao hides the body's Body_Seg", said, StringComparison.Ordinal);
        Assert.Contains("1 of the body's shapes hidden", said, StringComparison.Ordinal);
        Assert.Equal(1, dressed.Parts);
        Assert.Equal(2, dressed.Mesh.Triangles);
        PartFit armour = Assert.Single(dressed.Fitted);
        Assert.Equal("art/armour.ao", armour.File);
        Assert.Equal(PartKind.Skin, armour.Kind);
        Assert.True(armour.Past);
        Assert.Equal(0, armour.Bones);
    }

    /// <summary>A shape's material written beside the skin, not under it, is that shape's material - as the player's IntFourB.ao writes 56 of 57.</summary>
    [Fact]
    public void ASHAPESMaterialBesideTheSkinIsItsMaterial()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["body.ao"] = Text(
            "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t}\n"
            + "\tSkinMesh\n\t{\n\t\tskin = \"art/mesh.sm\"\n\t\tHipsShape = \"art/painted.mat:0\"\n\t}\n}\n");
        MonsterModel model = MonsterModels.OfFiles(install.Read, ["body.ao"], true, null);
        Assert.Equal(["art/painted.mat:0"], model.ShapeMaterials);
    }

    /// <summary>
    /// The body of the cases above: a skin of one shape on a rig, grouped as Body_Seg, wearing an outfit at its root by the alias Body_Attach
    /// - a line read whole once, whose path ended in the alias and was dropped.
    /// </summary>
    private static MonsterModelTests.Fake Body()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["body.ao"] = Text(
            "version 3\nAttachedAnimatedObject\n{\n\tclear_inherited_attached_objects = true\n"
            + "\tattached_object = \"<root> art/outfit.ao alias Body_Attach\"\n}\nclient\n{\n"
            + "\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t}\n"
            + "\tSkinMesh\n\t{\n\t\talias = \"Body_Seg HipsShape\"\n\t\tskin = \"art/mesh.sm\"\n\t}\n}\n");
        install.Files["art/outfit.ao"] = Text("version 3\nclient\n{\n\tSkinMesh\n\t{\n\t\tskin = \"art/outfit.sm\"\n\t}\n}\n");
        install.Files["art/outfit.sm"] = MonsterModelTests.Sm("art/rig.smd", "art/paint.mat");
        return install;
    }

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);
}
