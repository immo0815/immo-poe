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
    /// The composition's held slots by the schema, as the two captures of 2026-10-31 hold them: with the wand and focus in hand +0x08 empty,
    /// +0x30 the wand and +0x58 the focus; after the swap to the staff +0x08 the staff and +0x30 the wand again, which the game does not draw.
    /// The composition alone puts the first set in hand where +0x08 is filled; the model hangs the set's hands at R_Weapon and L_Weapon,
    /// rigidly, never a piece of the other set's hands as a skin - v0.1.397 hung the wand in her hand with the staff in the game's - and
    /// carries the set's pack and idle, which change the key.
    /// </summary>
    [Fact]
    public void THESETInHandHangsItsOwnHandsAndTheOtherSetsPiecesNowhere()
    {
        const string staff = "Metadata/Items/Weapons/TwoHandWeapons/Staves/mtx/WandererStaff/WandererStaffHeld.ao";
        const string wand = "Metadata/Items/Weapons/OneHandWeapons/Wands/Microtransactions/WandOfTheThaumaturge/WandOfTheThaumaturgeHeld.ao";
        const string focus = "Metadata/Items/Armours/Focus/Microtransactions/BetaDoryaniFocus/BetaDoryaniFocusHeld.ao";
        const string body = "Metadata/Items/Armours/BodyArmours/BetaDoryaniArmour/BetaDoryaniArmourBodyInt.ao";
        OffsetSchema schema = Schema();
        PlayerPieces.Piece[] after = [new(0x08, 0x08, 1, 2, staff), new(0x30, 0x08, 3, 4, wand), new(0x90, 0x08, 5, 6, body)];
        Assert.Equal(staff, PlayerPieces.AtSlot(after, schema, "Weapon1"));
        Assert.Equal(wand, PlayerPieces.AtSlot(after, schema, "Weapon2"));
        Assert.Equal(string.Empty, PlayerPieces.AtSlot(after, schema, "Offhand2"));
        Assert.Equal(string.Empty, PlayerPieces.AtSlot(after, schema, "Boots"));

        (string, string)[] items = [("Metadata/Items/Weapons/TwoHandWeapons/Staves/FourStaff6", string.Empty),
            ("Metadata/Items/Weapons/OneHandWeapons/Wands/FourWand13", "Metadata/Items/Armours/Focii/FourFocus6Endgame")];
        var taken = new PlayerDress("Metadata/Characters/Int/IntFourb", "Sorceress", "Metadata/Characters/Int/IntFourB.ao", [staff, wand, body], string.Empty)
        {
            Moves = "Art/Models/CharactersFour/Animations/BaseStaff/OneRig.ast",
        };

        // THE 22:17 CAPTURE: the staff in hand.
        PlayerDress swapped = PlayerDress.Composed(taken, items, staff, wand, string.Empty);
        Assert.Equal(0, swapped.InHand);
        Assert.Equal(0, swapped.Shown);
        Assert.Equal([staff, wand], swapped.Held);
        MonsterVariety held = swapped.Variety(0);
        Assert.Equal(new WornPiece("R_Weapon", staff) { Held = true }, held.Worn[0]);
        Assert.Equal(new WornPiece("<root>", body) { SkinOnly = true }, Assert.Single(held.Worn, one => !one.Held));
        Assert.DoesNotContain(held.Worn, one => one.Path == wand);
        Assert.Equal(swapped.Moves, held.Moves);

        // THE OTHER SET, switched to: the wand from its slot, no pack of its own read yet - so none, not the staff's.
        MonsterVariety other = swapped.Variety(1);
        Assert.Equal(new WornPiece("R_Weapon", wand) { Held = true }, Assert.Single(other.Worn, one => one.Held));
        Assert.DoesNotContain(other.Worn, one => one.Path == staff);
        Assert.Equal(string.Empty, other.Moves);
        Assert.NotEqual(swapped.Key(0), swapped.Key(1));

        // THE 16:16 CAPTURE: the wand and the focus in hand, +0x08 empty.
        PlayerDress before = PlayerDress.Composed(taken with { Pieces = [wand, focus, body] }, items, string.Empty, wand, focus);
        Assert.Equal(1, before.InHand);
        MonsterVariety wandAndFocus = before.Variety(1);
        Assert.Equal(new WornPiece("R_Weapon", wand) { Held = true }, wandAndFocus.Worn[0]);
        Assert.Equal(new WornPiece("L_Weapon", focus) { Held = true }, wandAndFocus.Worn[1]);
        Assert.Equal(new WornPiece("<root>", body) { SkinOnly = true }, wandAndFocus.Worn[2]);

        // A SET'S OWN PACK AND IDLE are its model's and its key's.
        PlayerDress resolved = before with { Sets = [before.Sets[0], before.Sets[1] with { Moves = "Art/Models/CharactersFour/Animations/BaseWand/OneRig.ast", Opening = "idle_wand_shield_01" }] };
        Assert.Equal("idle_wand_shield_01", resolved.Variety(1).Opening);
        Assert.Equal("Art/Models/CharactersFour/Animations/BaseWand/OneRig.ast", resolved.Variety(1).Moves);
        Assert.NotEqual(before.Key(1), resolved.Key(1));
    }

    /// <summary>
    /// His equipment looks as the captures hold them: entries of 0x88 behind Inventories +0x120, the slot in the first byte, the class at +0x08 -
    /// its ItemClasses row plus one (the staff's 14 held as 15), an item stance's row at +0x10 on a held item alone, the visual identity's row at +0x20 naming its Id at its own +0x00 in UTF-16 -
    /// and the slots 0 to 3 standing for the two sets' hands, main then off.
    /// </summary>
    [Fact]
    public void HISEquipmentLooksAreReadWithBothSetsCosmetics()
    {
        const ulong inventories = 0x3100_0000, list = 0x3200_0000, rows = 0x3300_0000, ids = 0x3400_0000, stance = 0x3500_0000;
        var memory = new FakeMemoryReader();
        memory.Place(inventories, new byte[0x150]);
        (int Slot, int Class, bool Held, string Id)[] looks =
        [
            (0, 15, true, "AddedEffectWandererStaffSkin"),
            (2, 9, true, "AddedEffectWandoftheThaumaturgeSkin"),
            (3, 82, true, "AddedEffectDoryanisIdolFocusandShieldSkin"),
            (6, 25, false, "AddedEffectThaumaturgeoftheVaalBodyArmour"),
        ];
        byte[] block = new byte[looks.Length * 0x88];
        for (var one = 0; one < looks.Length; one++)
        {
            ulong row = rows + (ulong)(one * 0x3C8), id = ids + (ulong)(one * 0x100);
            block[one * 0x88] = (byte)looks[one].Slot;
            BitConverter.GetBytes(looks[one].Class).CopyTo(block, (one * 0x88) + 0x08);
            BitConverter.GetBytes(looks[one].Held ? stance + (ulong)one : 0UL).CopyTo(block, (one * 0x88) + 0x10);
            BitConverter.GetBytes(row).CopyTo(block, (one * 0x88) + 0x20);
            memory.Place(row, new byte[0x3C8]);
            memory.Place(row, id);
            memory.PlaceUtf16(id, looks[one].Id);
        }

        memory.Place(list, block);
        memory.Place(inventories + 0x120, list);
        memory.Place(inventories + 0x128, list + (ulong)block.Length);
        OffsetSchema schema = Schema();

        IReadOnlyList<EquipmentLook> read = EquipmentLook.Read(memory, schema, inventories);
        Assert.Equal(looks.Select(one => new EquipmentLook(one.Slot, one.Class, one.Held, one.Id)), read);
        Assert.Equal([14, 8, 81, 24], read.Select(one => one.ClassRow));
        Assert.Equal(-1, new EquipmentLook(0, 0, true, string.Empty).ClassRow);
        Assert.Equal((0, 0), EquipmentLook.SetOf(0, 2));
        Assert.Equal((0, 1), EquipmentLook.SetOf(1, 2));
        Assert.Equal((1, 0), EquipmentLook.SetOf(2, 2));
        Assert.Equal((1, 1), EquipmentLook.SetOf(3, 2));
        Assert.Equal((-1, -1), EquipmentLook.SetOf(6, 2));

        // A LIST THAT IS NOT WHOLE ENTRIES, or none, reads nothing.
        memory.Place(inventories + 0x128, list + 0x40);
        Assert.Empty(EquipmentLook.Read(memory, schema, inventories));
        Assert.Empty(EquipmentLook.Read(memory, schema, 0));
    }

    /// <summary>
    /// A set's pack is the .ast its stance's timeline names: an .atl is JSON whose "skeleton" names it (base_staff.atl names BaseStaff/OneRig.ast,
    /// the 22:17 capture); a value that is no .ast, or no key at all, names none.
    /// </summary>
    [Fact]
    public void ATIMELINENamesItsPackAsItsSkeleton()
    {
        Assert.Equal("Art/Models/CharactersFour/Animations/BaseStaff/OneRig.ast", PlayerStance.SkeletonOf(
            "{\n\t\"metadata\": \"Art/Models/CharactersFour/Animations/BaseStaff/OneRig.amd\",\n\t\"skeleton\": \"Art/Models/CharactersFour/Animations/BaseStaff/OneRig.ast\",\n\t\"animations\": []\n}"));
        Assert.Equal("Art/X/OneRig.ast", PlayerStance.SkeletonOf("{\"skeleton\" :\"Art\\/X\\/OneRig.ast\"}"));
        Assert.Equal(string.Empty, PlayerStance.SkeletonOf("{\"skeleton\": \"Art/X/OneRig.amd\"}"));
        Assert.Equal(string.Empty, PlayerStance.SkeletonOf("{\"name\": \"skeleton\", \"metadata\": \"Art/X/OneRig.amd\"}"));
    }

    /// <summary>
    /// The held model hung for a hand: the one the composition draws among those its look names, else the only one, else the one sharing the
    /// most folders with the item's own model - a focus-and-shield skin naming both is hung as the focus.
    /// </summary>
    [Fact]
    public void AHANDHangsTheModelItsLookNamesThatTheGameDraws()
    {
        string[] both = ["Metadata/Items/Armours/Shields/Microtransactions/Doryani/DoryaniShieldHeld.ao", "Metadata/Items/Armours/Focus/Microtransactions/Doryani/DoryaniFocusHeld.ao"];
        Assert.Equal(both[1], PlayerStance.Pick(both, [both[1]], string.Empty));
        Assert.Equal(both[1], PlayerStance.Pick(both, [], "Metadata/Items/Armours/Focus/CrystalFocus/CrystalFocusHeld.ao"));
        Assert.Equal(both[0], PlayerStance.Pick(both, [], string.Empty));
        Assert.Equal(both[0], PlayerStance.Pick([both[0]], [], "Metadata/Items/Armours/Focus/CrystalFocus/CrystalFocusHeld.ao"));
        Assert.Equal(string.Empty, PlayerStance.Pick([], [both[1]], string.Empty));
    }

    /// <summary>
    /// The 22:17 capture whole, through an install holding his tables, his .act chain (the fixtures), both stances' timelines and packs: the
    /// staff's set is in hand (its stance's timeline is his Actor's) and holds the staff the game draws; the other set holds the wand the
    /// composition kept and the focus its look's cosmetic names - the focus-and-shield skin's focus, by the folder of the focus's own model -
    /// and plays the pack its stance's timeline names from its idle. A hand whose slot's class row is not its item's takes the one held look
    /// of its class; an item with no look draws its own model, said so.
    /// </summary>
    [Fact]
    public void THEOTHERSetsHandsComeFromHisLooksAndItsPackFromItsTimeline()
    {
        const string staffItem = "Metadata/Items/Weapons/TwoHandWeapons/Staves/FourStaff6";
        const string wandItem = "Metadata/Items/Weapons/OneHandWeapons/Wands/FourWand13";
        const string focusItem = "Metadata/Items/Armours/Focii/FourFocus6Endgame";
        const string staff = "Metadata/Items/Weapons/TwoHandWeapons/Staves/mtx/WandererStaff/WandererStaffHeld.ao";
        const string wand = "Metadata/Items/Weapons/OneHandWeapons/Wands/Microtransactions/WandOfTheThaumaturge/WandOfTheThaumaturgeHeld.ao";
        const string focus = "Metadata/Items/Armours/Focus/Microtransactions/BetaDoryaniFocus/BetaDoryaniFocusHeld.ao";
        const string shield = "Metadata/Items/Armours/Shields/Microtransactions/BetaDoryaniShield/BetaDoryaniShieldHeld.ao";
        const string ownFocus = "Metadata/Items/Armours/Focus/CrystalFocus/CrystalFocusHeld.ao";
        const string mace = "Metadata/Items/Weapons/OneHandWeapons/OneHandMaces/Microtransactions/WandererMace/WandererMaceHeld.ao";
        const string wandLine = "Metadata/Characters/Timelines/base_wand/base_wand.atl";
        const string body = "Metadata/Items/Armours/BodyArmours/BetaDoryaniArmour/BetaDoryaniArmourBodyInt.ao";
        const string staffPack = "Art/Models/CharactersFour/Animations/BaseStaff/OneRig.ast";
        const string wandPack = "Art/Models/CharactersFour/Animations/BaseWand/OneRig.ast";
        const string staffLine = "Metadata/Characters/Timelines/base_staff/base_staff.atl";
        QuestTableLayouts layouts = Layouts();
        var files = new List<(string, byte[])>
        {
            Table(layouts, "Characters", [new() { ["Id"] = "Metadata/Characters/Int/IntFourb", ["ACTFile"] = "Metadata/Characters/Int/IntFour.act" }]),
            Table(layouts, "BaseItemTypes",
            [
                new() { ["Id"] = staffItem, ["ItemClass"] = 0, ["ItemStance"] = 0, ["ItemVisualIdentity"] = 0 },
                new() { ["Id"] = wandItem, ["ItemClass"] = 1, ["ItemStance"] = 1, ["ItemVisualIdentity"] = 1 },
                new() { ["Id"] = focusItem, ["ItemClass"] = 2, ["ItemStance"] = 2, ["ItemVisualIdentity"] = 2 },
            ]),
            Table(layouts, "ItemClasses", [new() { ["Id"] = "Staff" }, new() { ["Id"] = "Wand" }, new() { ["Id"] = "Focus" }]),
            Table(layouts, "ItemStances", [new() { ["Id"] = "Staff" }, new() { ["Id"] = "Wand" }, new() { ["Id"] = "SpiritShield" }]),
            Table(layouts, "ItemVisualIdentity",
            [
                new() { ["Id"] = "FourStaff6", ["AOFile"] = "Metadata/Items/Weapons/TwoHandWeapons/Staves/ChimingStaff/ChimingStaffDrop.ao", ["AOFile2"] = "Metadata/Items/Weapons/TwoHandWeapons/Staves/ChimingStaff/ChimingStaffHeld.ao" },
                new() { ["Id"] = "FourWand13", ["AOFile2"] = "Metadata/Items/Weapons/OneHandWeapons/Wands/DuelingWand/DuelingWandHeld.ao" },
                new() { ["Id"] = "FourFocus6", ["AOFile2"] = ownFocus },
                new() { ["Id"] = "AddedEffectWandererStaffSkin", ["AOFile"] = "Metadata/Items/Currency/CurrencyOrb.ao", ["column_38"] = staff, ["column_41"] = mace },
                new() { ["Id"] = "AddedEffectWandoftheThaumaturgeSkin", ["AOFile"] = "Metadata/Items/Currency/CurrencyOrb.ao", ["column_33"] = wand },
                new() { ["Id"] = "AddedEffectDoryanisIdolFocusandShieldSkin", ["AOFile"] = "Metadata/Items/Currency/CurrencyOrb.ao", ["column_81"] = shield, ["column_82"] = focus },
            ]),
            ("Metadata/Characters/Int/IntFour.act", File.ReadAllBytes(Fixture(Path.Combine("player-act", "Metadata", "Characters", "Int", "IntFour.act")))),
            ("Metadata/Characters/CharacterFemale.act", File.ReadAllBytes(Fixture(Path.Combine("player-act", "Metadata", "Characters", "CharacterFemale.act")))),
            ("Metadata/Characters/Character.act", File.ReadAllBytes(Fixture(Path.Combine("player-act", "Metadata", "Characters", "Character.act")))),
            (staffLine, Utf16("{\n\t\"metadata\": \"Art/Models/CharactersFour/Animations/BaseStaff/OneRig.amd\",\n\t\"skeleton\": \"" + staffPack + "\"\n}")),
            (wandLine, Utf16("{\n\t\"skeleton\": \"" + wandPack + "\",\n\t\"animations\": []\n}")),
            (staffPack, Packed.Skeleton([("root_jntBnd", 255, 255, 0f)], [("idle_staff_01", string.Empty, 1, 60, 0x6f, 0, 0)])),
            (wandPack, Packed.Skeleton([("root_jntBnd", 255, 255, 0f)], [("idle_wand_shield_01", string.Empty, 1, 60, 0x6f, 0, 0)])),
        };
        GameFiles install = Assert.IsType<GameFiles>(FakeInstall.Of([.. files]));
        PlayerDress taken = PlayerDress.Composed(
            new PlayerDress("Metadata/Characters/Int/IntFourb", "Sorceress", "Metadata/Characters/Int/IntFourB.ao", [staff, wand, body], string.Empty)
            {
                Moves = staffPack,
                Timeline = staffLine,
                // MEMORY HOLDS EACH CLASS AS ITS ROW PLUS ONE (EquipmentLook.ClassRow): the staff's row 0 as 1, the wand's 1 as 2, the focus's 2 as 3.
                Looks = Looks,
            },
            [(staffItem, string.Empty), (wandItem, focusItem)], staff, wand, string.Empty);

        PlayerDress read = PlayerStance.Resolve(install, layouts, taken);
        Assert.Equal(0, read.InHand);
        Assert.Equal((staff, string.Empty, "Staff", staffPack, "idle_staff_01"),
            (read.Sets[0].MainPiece, read.Sets[0].OffPiece, read.Sets[0].Stance, read.Sets[0].Moves, read.Sets[0].Opening));
        Assert.Equal((wand, focus, "WandSpiritShield", wandPack, "idle_wand_shield_01"),
            (read.Sets[1].MainPiece, read.Sets[1].OffPiece, read.Sets[1].Stance, read.Sets[1].Moves, read.Sets[1].Opening));
        Assert.Contains(read.Sets[1].Said, line => line.StartsWith("off hand: " + focus + " - its cosmetic", StringComparison.Ordinal));
        Assert.Contains(read.Sets[0].Said, line => line.StartsWith("main hand: " + staff + " - as the game draws it", StringComparison.Ordinal));

        MonsterVariety inHand = read.Variety(read.InHand);
        Assert.Equal([staff, body], inHand.Worn.Select(one => one.Path));
        MonsterVariety switched = read.Variety(1);
        Assert.Equal([wand, focus, body], switched.Worn.Select(one => one.Path));
        Assert.Equal((wandPack, "idle_wand_shield_01"), (switched.Moves, switched.Opening));

        // A SLOT WHOSE CLASS ROW IS NOT ITS ITEM'S gives way to the one held look of the item's class.
        PlayerDress moved = PlayerStance.Resolve(install, layouts, taken with { Looks = [new(0, 1, true, "AddedEffectWandererStaffSkin"), new(5, 3, true, "AddedEffectDoryanisIdolFocusandShieldSkin"), new(3, 2, true, "AddedEffectWandoftheThaumaturgeSkin")] });
        Assert.Equal((wand, focus), (moved.Sets[1].MainPiece, moved.Sets[1].OffPiece));

        // THE 00:23 CAPTURE: the second set in hand, the composition holding the wand and the focus alone - the staff's set, not in hand,
        // still wears its cosmetic, the staff of the two held models its look names (the staff's column_38, the mace's column_41) by its
        // folder. v0.1.398 read memory's number as the row, turned the look away and hung the staff's own ChimingStaffHeld.ao.
        PlayerDress second = PlayerStance.Resolve(install, layouts, PlayerDress.Composed(
            new PlayerDress("Metadata/Characters/Int/IntFourb", "Sorceress", "Metadata/Characters/Int/IntFourB.ao", [wand, focus, body], string.Empty)
            {
                Moves = wandPack,
                Timeline = wandLine,
                Looks = Looks,
            },
            [(staffItem, string.Empty), (wandItem, focusItem)], string.Empty, wand, focus));
        Assert.Equal(1, second.InHand);
        Assert.Equal((staff, wand, focus), (second.Sets[0].MainPiece, second.Sets[1].MainPiece, second.Sets[1].OffPiece));
        Assert.Contains(second.Sets[0].Said, line => line.StartsWith("main hand: " + staff + " - its cosmetic", StringComparison.Ordinal));
        Assert.Contains(second.Sets[0].Said, line => line.Contains("one of 2 held models it names", StringComparison.Ordinal));

        // MEMORY'S NUMBER TAKEN FOR THE ROW turns the other set's look away, as v0.1.398 did: a look one off is not the item's class.
        PlayerDress unshifted = PlayerStance.Resolve(install, layouts, taken with { Looks = [new(0, 0, true, "AddedEffectWandererStaffSkin"), new(2, 1, true, "AddedEffectWandoftheThaumaturgeSkin"), new(3, 2, true, "AddedEffectDoryanisIdolFocusandShieldSkin")] });
        Assert.Equal(ownFocus, unshifted.Sets[1].OffPiece);

        // NO LOOKS READ: the composition's held slots, else the item's own model, said so.
        PlayerDress bare = PlayerStance.Resolve(install, layouts, taken with { Looks = [] });
        Assert.Equal((staff, wand, ownFocus), (bare.Sets[0].MainPiece, bare.Sets[1].MainPiece, bare.Sets[1].OffPiece));
        Assert.Contains(bare.Sets[1].Said, line => line.StartsWith("off hand: " + ownFocus + " - the item's own model", StringComparison.Ordinal));
    }

    /// <summary>The 00:19 and 00:23 captures' held looks (and one worn), each class as memory holds it - the test install's row plus one.</summary>
    private static readonly EquipmentLook[] Looks =
    [
        new(0, 1, true, "AddedEffectWandererStaffSkin"),
        new(2, 2, true, "AddedEffectWandoftheThaumaturgeSkin"),
        new(3, 3, true, "AddedEffectDoryanisIdolFocusandShieldSkin"),
        new(6, 26, false, "AddedEffectThaumaturgeoftheVaalBodyArmour"),
    ];

    /// <summary>A .datc64 of one table by the vendored layout: each row's strings and foreign rows by column name, the rest nought.</summary>
    private static (string Path, byte[] Bytes) Table(QuestTableLayouts layouts, string table, List<Dictionary<string, object>> rows)
    {
        int size = layouts.RowSizeOf(table);
        var variable = new List<byte>(Encoding.Unicode.GetBytes("\0"));
        var bytes = new byte[size * rows.Count];
        for (var row = 0; row < rows.Count; row++)
        {
            foreach ((string column, object value) in rows[row])
            {
                int at = (row * size) + layouts.OffsetOf(table, column);
                if (value is string text)
                {
                    BitConverter.GetBytes((ulong)(8 + variable.Count)).CopyTo(bytes, at);
                    variable.AddRange(Encoding.Unicode.GetBytes(text + "\0"));
                }
                else
                {
                    BitConverter.GetBytes((ulong)(int)value).CopyTo(bytes, at);
                    BitConverter.GetBytes(0UL).CopyTo(bytes, at + 8);
                }
            }
        }

        // EVERY FOREIGN ROW NOT WRITTEN NAMES NOTHING, as the file writes it - a nought would be row nought.
        DatColumns.Layout(layouts.Tables[table]);
        foreach (DatColumn column in layouts.Tables[table].Where(one => one.Type == "foreignrow" && !one.Array))
        {
            for (var row = 0; row < rows.Count; row++)
            {
                if (!rows[row].ContainsKey(column.Name))
                {
                    BitConverter.GetBytes(0xFEFE_FEFE_FEFE_FEFEUL).CopyTo(bytes, (row * size) + column.Offset);
                }
            }
        }

        var file = new List<byte>(BitConverter.GetBytes(rows.Count));
        file.AddRange(bytes);
        file.AddRange(Enumerable.Repeat((byte)0xBB, 8));
        file.AddRange(variable);
        return ($"data/balance/{table.ToLowerInvariant()}.datc64", [.. file]);
    }

    /// <summary>UTF-16 with its mark, as the game writes its text files.</summary>
    private static byte[] Utf16(string text) => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)];

    /// <summary>The vendored item layouts, found up from the test's folder.</summary>
    private static QuestTableLayouts Layouts()
        => Assert.IsType<QuestTableLayouts>(QuestTableLayouts.Load(Path.Combine(
            Path.GetDirectoryName(Path.GetDirectoryName(Path.GetDirectoryName(Fixture("x"))!)!)!, "data", "item-tables.json")));

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
