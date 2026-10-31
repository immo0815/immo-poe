using System.Numerics;
using System.Text;
using PoEformance.Core.Schema;
using PoEformance.Features;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The player held and played as the game does (v0.1.397): the stance his .act gives the weapons in hand and its idle, the pack and timeline
/// his Actor holds, his hands' pieces rigid at R_Weapon and L_Weapon, his cape at Back, a piece's own attach_bone_override, a socket only his
/// files define, and the stance's pack played in place of his rig's own animations.
/// </summary>
public class PlayerStanceTests
{
    /// <summary>
    /// The Sorceress's own files (the 16:16 capture's): IntFour.act names CharacterFemale.act names Character.act; a wand and a focus are the
    /// stance WandSpiritShield, whose Base timeline is the Wand stance's base_wand.atl - the very timeline her Actor held - and whose idle is
    /// Character.act's idle_*_01 with the nearest wildcard, wand_shield. Her first set's staff is the stance Staff, on base_staff.atl.
    /// </summary>
    [Fact]
    public void AWANDAndAFocusAreTheStanceWandSpiritShieldWhoseIdleIsIdleWandShield01()
    {
        (ActStance root, string wildcard) = PlayerStance.Merge([Act("Int/IntFour.act"), Act("CharacterFemale.act"), Act("Character.act")]);
        Assert.Equal("unarmed", wildcard);

        ActStance stance = Assert.IsType<ActStance>(root.Find("WandSpiritShield"));
        Assert.Equal("Wand", stance.Up?.Name);
        Assert.Equal("wand_shield", stance.WildcardOf(wildcard));
        Assert.Equal("Metadata/Characters/Timelines/base_wand/base_wand.atl", PlayerStance.TimelineOf(stance));
        (ActAction? idle, ActStance? from) = stance.ActionOf("Idle");
        Assert.NotNull(idle);
        Assert.Equal("idle_*_01", idle.Animation);
        Assert.Equal(string.Empty, from?.Name);
        Assert.Equal("idle_wand_shield_01", PlayerStance.Named(stance, idle, wildcard));

        // HER FIRST SET: a two-handed staff alone.
        ActStance staff = Assert.IsType<ActStance>(root.Find("Staff"));
        Assert.Equal("Metadata/Characters/Timelines/base_staff/base_staff.atl", PlayerStance.TimelineOf(staff));
        Assert.Equal("idle_staff_01", PlayerStance.Named(staff, Assert.IsType<ActAction>(staff.ActionOf("Idle").Action), wildcard));

        // A WAND WITH A REAL SHIELD plays the DaggerShield animations its stance is nested in - the wildcard is the dagger's.
        ActStance wandShield = Assert.IsType<ActStance>(root.Find("WandShield"));
        Assert.Equal("DaggerShield", wandShield.Up?.Name);
        Assert.Equal("dagger_shield", wandShield.WildcardOf(wildcard));

        // NOTHING IN HAND: the file's own wildcard.
        ActStance unarmed = Assert.IsType<ActStance>(root.Find("Unarmed"));
        Assert.Equal("idle_unarmed_01", PlayerStance.Named(unarmed, Assert.IsType<ActAction>(unarmed.ActionOf("Idle").Action), wildcard));

        // IntFour.act's ClassSpecific rule reached the merged block.
        Assert.Contains(("ClassSpecific", "_strdex_", "_int_"), root.Replaces);
    }

    /// <summary>
    /// The grammar's corners: a parent line, a wildcard before the block, a stance with and without a wildcard, a value over several lines, a
    /// comment, a statement ending at its block's end, a tag's replace rule applied to the actions carrying the tag.
    /// </summary>
    [Fact]
    public void ANACTIsReadIntoItsStancesActionsTimelinesAndRules()
    {
        ActFile file = ActFile.Parse(
            "﻿parent \"Metadata/Characters/Base.act\"\nanimated_object \"Metadata/Characters/Character.ao\"\n\"plain\"  // default\n{\n"
            + "\tRun =\n\t\"\n\t\twalk_*_01\n\t\t\t: speed = 222,\n\t\" timeline \"T/run.atl\" tag Base;\n"
            + "\tIdle = \"idle_*_01\" timeline \"T/plain.atl\" tag Base tag Classy;\n"
            + "\twith_tag Classy replace \"_sword_\" \"_blade_\";\n"
            + "\tstance Sword \"sword\"\n\t{\n\t\twith_tag Base timeline \"T/sword.atl\";\n\t\tstance SwordTown\n\t\t{\n\t\t\twith_tag Idle::town;\n\t\t}\n\t}\n"
            + "\tappend_persistent Layer\n}\nmovement_speed 333\n");
        Assert.True(file.Ready);
        Assert.Empty(file.Faults);
        Assert.Equal("Metadata/Characters/Base.act", file.Parent);
        Assert.Equal("plain", file.Wildcard);
        Assert.Equal("walk_*_01", file.Root.Actions["Run"].Animation.Trim().Split('\n')[0].Trim());
        Assert.Equal("T/run.atl", file.Root.Actions["Run"].Timeline);

        ActStance town = Assert.IsType<ActStance>(file.Root.Find("SwordTown"));
        Assert.Equal("sword", town.WildcardOf(file.Wildcard));
        Assert.Equal("T/sword.atl", PlayerStance.TimelineOf(town));
        Assert.Equal("idle_blade_01", PlayerStance.Named(town, file.Root.Actions["Idle"], file.Wildcard));
        Assert.Equal("T/plain.atl", PlayerStance.TimelineOf(file.Root));
    }

    /// <summary>
    /// Her Actor's chain as the schema has it: +0x228 to the animation set, its +0x180 to the pack, the pack's +0xF0 to an object whose +0x10
    /// is the .ast's record; the set's +0x188 the timeline's record - each record naming itself at +0x08. A record of another kind is not taken.
    /// </summary>
    [Fact]
    public void THESTANCEsPackAndTimelineAreReadThroughTheActor()
    {
        const ulong actor = 0x2100_0000, set = 0x2200_0000, pack = 0x2300_0000, keyframes = 0x2400_0000, ast = 0x2500_0000, atl = 0x2600_0000;
        var memory = new FakeMemoryReader();
        foreach (ulong one in new[] { actor, set, pack, keyframes, ast, atl })
        {
            memory.Place(one, new byte[0x400]);
        }

        memory.Place(actor + 0x228, set);
        memory.Place(set + 0x180, pack);
        memory.Place(set + 0x188, atl);
        memory.Place(pack + 0xF0, keyframes);
        memory.Place(keyframes + 0x10, ast);
        memory.PlaceStdWString(ast + 0x08, "Art/Models/CharactersFour/Animations/BaseWand/OneRig.ast", 0x2700_0000);
        memory.PlaceStdWString(atl + 0x08, "Metadata/Characters/Timelines/base_wand/base_wand.atl", 0x2800_0000);
        OffsetSchema schema = Schema();

        (string moves, string timeline) = PlayerDress.StanceOf(memory, schema, actor);
        Assert.Equal("Art/Models/CharactersFour/Animations/BaseWand/OneRig.ast", moves);
        Assert.Equal("Metadata/Characters/Timelines/base_wand/base_wand.atl", timeline);
        Assert.Equal((string.Empty, string.Empty), PlayerDress.StanceOf(memory, schema, 0));

        memory.PlaceStdWString(ast + 0x08, "Art/Models/CharactersFour/Animations/BaseWand/OneRig.amd", 0x2700_0000);
        Assert.Equal(string.Empty, PlayerDress.StanceOf(memory, schema, actor).Moves);
    }

    /// <summary>
    /// The composition's slots by the schema: the piece at +0x30 is the main hand's, at +0x58 the off hand's; the model hangs them at R_Weapon
    /// and L_Weapon, rigidly, the rest as skins at his root, and carries the stance's pack and idle - which change the key.
    /// </summary>
    [Fact]
    public void HISHandsPiecesHangRigidlyAtRWeaponAndLWeapon()
    {
        OffsetSchema schema = Schema();
        PlayerPieces.Piece[] slots =
        [
            new(0x30, 0x08, 1, 2, "Metadata/Items/Weapons/Wand.ao"),
            new(0x58, 0x08, 3, 4, "Metadata/Items/Armours/Focus.ao"),
            new(0x90, 0x08, 5, 6, "Metadata/Items/Armours/Body.ao"),
        ];
        Assert.Equal("Metadata/Items/Weapons/Wand.ao", PlayerPieces.AtSlot(slots, schema, "MainHand"));
        Assert.Equal("Metadata/Items/Armours/Focus.ao", PlayerPieces.AtSlot(slots, schema, "OffHand"));
        Assert.Equal(string.Empty, PlayerPieces.AtSlot(slots, schema, "Boots"));

        var dress = new PlayerDress("Metadata/Characters/Int/IntFourb", "Sorceress", "Metadata/Characters/Int/IntFourB.ao", [.. slots.Select(one => one.Path)], string.Empty)
        {
            MainHand = slots[0].Path,
            OffHand = slots[1].Path,
            Moves = "Art/Models/CharactersFour/Animations/BaseWand/OneRig.ast",
            Opening = "idle_wand_shield_01",
        };
        MonsterVariety one = dress.Variety();
        Assert.Equal(new WornPiece("R_Weapon", slots[0].Path) { Held = true }, one.Worn[0]);
        Assert.Equal(new WornPiece("L_Weapon", slots[1].Path) { Held = true }, one.Worn[1]);
        Assert.Equal(new WornPiece("<root>", slots[2].Path) { SkinOnly = true }, one.Worn[2]);
        Assert.Equal(dress.Moves, one.Moves);
        Assert.Equal("idle_wand_shield_01", one.Opening);
        Assert.NotEqual(dress.Key, (dress with { Moves = string.Empty }).Key);
    }

    /// <summary>
    /// A piece's own attach_bone_override hangs it at that bone of its carrier, where its line says "&lt;root&gt;" (the glove feathers' elbows);
    /// one that pairs its bones with its carrier's by attachment_bones is placed by the pairing and the override left alone (the robe's skirt).
    /// </summary>
    [Fact]
    public void APIECESOwnAttachBoneOverrideHangsItAtThatBone()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["body.ao"] = Text(
            "version 3\nAttachedAnimatedObject\n{\n\tattached_object = \"<root> art/feather.ao\"\n}\nclient\n{\n"
            + "\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t}\n\tSkinMesh\n\t{\n\t\tskin = \"art/mesh.sm\"\n\t}\n}\n");
        install.Files["art/feather.sm"] = MonsterModelTests.Sm("art/rig.smd", "art/paint.mat");
        install.Files["art/feather.ao"] = Text(
            "version 3\nBaseAnimationEvents\n{\n\tattach_bone_override = \"R_elbow_jntBnd\"\n}\nclient\n{\n"
            + "\tSkinMesh\n\t{\n\t\tskin = \"art/feather.sm\"\n\t}\n}\n");

        PartFit feather = Assert.Single(MonsterModels.OfFiles(install.Read, ["body.ao"], true, null).Fitted);
        Assert.Equal("R_elbow_jntBnd", feather.Socket);
        Assert.Equal(PartPlace.Bone, feather.Place);

        // PAIRED BY ITS OWN attachment_bones: the line's "<root>" stands.
        install.Files["art/feather.ao"] = Text(
            "version 3\nBaseAnimationEvents\n{\n\tattach_bone_override = \"R_elbow_jntBnd\"\n}\nclient\n{\n"
            + "\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t\tattachment_bones = \"hip_jntBnd child_attach\"\n\t}\n"
            + "\tBoneGroups\n\t{\n\t\tbone_group = \"child_attach false root_jntBnd\"\n\t}\n"
            + "\tSkinMesh\n\t{\n\t\tskin = \"art/feather.sm\"\n\t}\n}\n");
        PartFit paired = Assert.Single(MonsterModels.OfFiles(install.Read, ["body.ao"], true, null).Fitted);
        Assert.Equal("<root>", paired.Socket);
        Assert.Equal(PartPlace.Root, paired.Place);
    }

    /// <summary>
    /// A held piece goes rigidly at its socket - a socket only the body's files define (its parent bone, its own shift) as well as a bone's
    /// name - its own rig never matched against the body's: the same rig as his, which as a skin would follow his bones, stays one rigid piece.
    /// </summary>
    [Fact]
    public void AHELDPieceGoesRigidlyAtASocketItsBodysFilesDefine()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["body.ao"] = Body(shift: "0 0 0");
        install.Files["art/wand.ao"] = Text(
            "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t}\n\tSkinMesh\n\t{\n\t\tskin = \"art/mesh.sm\"\n\t}\n}\n");

        MonsterModel atWrist = MonsterModels.OfFiles(install.Read, ["body.ao"], true, null, [new WornPiece("R_wrist_jntBnd", "art/wand.ao") { Held = true }]);
        MonsterModel atGrip = MonsterModels.OfFiles(install.Read, ["body.ao"], true, null, [new WornPiece("Grip", "art/wand.ao") { Held = true }]);
        PartFit wrist = Assert.Single(atWrist.Fitted), grip = Assert.Single(atGrip.Fitted);
        Assert.Equal(PartKind.Rigid, grip.Kind);
        Assert.Equal(PartPlace.Bone, grip.Place);
        Assert.Equal("Grip", grip.Socket);
        Assert.Equal(wrist.Least, grip.Least);
        Assert.Equal(wrist.Most, grip.Most);

        // THE SOCKET'S OWN SHIFT moves the piece by its length, whichever way the wrist turns it.
        install.Files["body.ao"] = Body(shift: "0 0 -10");
        PartFit shifted = Assert.Single(MonsterModels.OfFiles(install.Read, ["body.ao"], true, null, [new WornPiece("Grip", "art/wand.ao") { Held = true }]).Fitted);
        Vector3 moved = ((shifted.Least + shifted.Most) / 2) - ((grip.Least + grip.Most) / 2);
        Assert.Equal(10f, moved.Length(), 2);

        // NOT HELD: the same piece is a skin on his rig, bone for bone.
        PartFit skinned = Assert.Single(MonsterModels.OfFiles(install.Read, ["body.ao"], true, null, [new WornPiece("R_wrist_jntBnd", "art/wand.ao")]).Fitted);
        Assert.Equal(PartKind.Skin, skinned.Kind);
    }

    /// <summary>A worn piece that extends Metadata/ParentBackAttachment - his cape - hangs rigidly at Back (the rig's aux_back_attachment).</summary>
    [Fact]
    public void ABACKAttachmentHangsAtBack()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["body.ao"] = Body(shift: "0 0 0");
        install.Files["Metadata/ParentBackAttachment.ao"] = Text("version 3\nabstract\nextends \"Metadata/Parent\"\n");
        install.Files["Metadata/Parent.ao"] = Text("version 3\nabstract\nextends \"nothing\"\n");
        install.Files["art/cape.ao"] = Text(
            "version 3\nextends \"Metadata/ParentBackAttachment\"\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t}\n"
            + "\tSkinMesh\n\t{\n\t\tskin = \"art/mesh.sm\"\n\t}\n}\n");

        PartFit cape = Assert.Single(MonsterModels.OfFiles(install.Read, ["body.ao"], true, null, [new WornPiece("<root>", "art/cape.ao") { SkinOnly = true }]).Fitted);
        Assert.Equal("Back", cape.Socket);
        Assert.Equal(PartPlace.Bone, cape.Place);
        Assert.Equal(PartKind.Rigid, cape.Kind);
    }

    /// <summary>
    /// The stance's pack is played in place of his rig's own animations where its bones are his name for name - the rig's path then the pack's,
    /// the opening his stance's idle where the pack has it - and refused, said why, where they are not.
    /// </summary>
    [Fact]
    public void THESTANCESPackIsPlayedInPlaceOfHisRigsAnimations()
    {
        MonsterModelTests.Fake install = MonsterModelTests.Install();
        install.Files["body.ao"] = Body(shift: "0 0 0");
        install.Files["art/pack.ast"] = install.Files["art/rig.ast"];
        AnimationSkeleton rig = AnimationSkeleton.Read(install.Files["art/rig.ast"]);
        string first = rig.Animations.Count > 0 ? rig.Animations[0].Name : string.Empty;

        MonsterModel played = MonsterModels.OfFiles(install.Read, ["body.ao"], true, null, null, "art/pack.ast", first);
        Assert.Equal("art/pack.ast", played.Rig_);
        Assert.Contains("of his stance, from art/pack.ast", played.Pack, StringComparison.Ordinal);
        Assert.Equal(first, played.Opening);
        Assert.Equal(string.Empty, MonsterModels.OfFiles(install.Read, ["body.ao"], true, null, null, "art/pack.ast", "no_such_animation").Opening);

        install.Files["art/other.ast"] = File.ReadAllBytes(Fixture("blackguard-v6.one.ast"));
        MonsterModel refused = MonsterModels.OfFiles(install.Read, ["body.ao"], true, null, null, "art/other.ast", first);
        Assert.Equal("art/rig.ast", refused.Rig_);
        Assert.Contains("are not his", refused.Pack, StringComparison.Ordinal);
    }

    /// <summary>The body of the socket test: his rig, his skin, and a socket "Grip" on his right wrist with the shift given.</summary>
    private static byte[] Body(string shift) => Text(
        "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"art/rig.ast\"\n\t\tsocket = \"Grip\"\n\t\t\tparent = \"R_wrist_jntBnd\"\n"
        + $"\t\t\ttranslation = \"{shift}\"\n\t}}\n\tSkinMesh\n\t{{\n\t\tskin = \"art/mesh.sm\"\n\t}}\n}}\n");

    /// <summary>One of the player's .act files, as the capture wrote it.</summary>
    private static ActFile Act(string under) => ActFile.Parse(File.ReadAllText(Fixture(Path.Combine("player-act", "Metadata", "Characters", under))));

    private static string Fixture(string name)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "tests", "fixtures")))
        {
            folder = folder.Parent;
        }

        return Path.Combine(folder!.FullName, "tests", "fixtures", name);
    }

    private static OffsetSchema Schema()
        => SchemaJson.Load(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Fixture("x"))!)!)!, "schema", "poe2.offsets.json"));

    private static byte[] Text(string text) => Encoding.UTF8.GetBytes(text);
}
