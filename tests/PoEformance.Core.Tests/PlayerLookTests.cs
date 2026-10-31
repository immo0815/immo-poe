using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using PoEformance.Core.Schema;
using PoEformance.Features;
using PoEformance.Game.Diagnostics;
using PoEformance.Game.Entities;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The player look part (PlayerLook, CapturePlayerLook) over a made memory and made files: the pointer web finds a game file a chain of
/// pointers reaches from a component, through a file record's std::wstring and a bare wide string, and says the chain; a component is
/// written whole with the list it begins and ends; the files side follows what a text file extends, and its offline reader says the
/// lines a piece attaches by.
/// </summary>
public class PlayerLookTests
{
    private const ulong Animated = 0x1000_0000, Info = 0x2000_0000, Record = 0x3000_0000, Name = 0x4000_0000, Wide = 0x5000_0000;

    /// <summary>
    /// The web walks Animated +0x358 to the model info and its +0x18 to the record, whose +0x08 is a std::wstring naming the .ao - the
    /// schema's own chain, found without being told it - and a bare wide string two hops off another word; a word past the depth is not
    /// followed, and a string that names no game file is not one.
    /// </summary>
    [Fact]
    public void THEPOINTERWebNamesAGameFileWithTheChainThatReachesIt()
    {
        var memory = new FakeMemoryReader();
        memory.Place(Animated, new byte[0x400]);
        memory.Place(Info, new byte[0x200]);
        memory.Place(Record, new byte[0x200]);
        memory.Place(Wide, new byte[0x200]);
        memory.Place(Animated + 0x358, Info);
        memory.Place(Animated + 0x100, Wide);
        memory.Place(Info + 0x18, Record);
        memory.PlaceStdWString(Record + 0x08, "Art/Models/Items/Armours/BodyArmours/Robe.ao", Name);
        memory.PlaceUtf16(Wide, "Metadata/Items/Armours/Helmets/Circlet.ao");
        memory.PlaceStdWString(Record + 0x40, "not a path at all, just words", Name + 0x400);

        PlayerLook.Webbed web = PlayerLook.Web(memory, [("Animated", Animated, 0x400)], depth: 3, block: 0x200, mostBlocks: 1000, TimeSpan.FromSeconds(30), null);
        Assert.Equal(["Art/Models/Items/Armours/BodyArmours/Robe.ao", "Metadata/Items/Armours/Helmets/Circlet.ao"], web.Paths.Keys.Order(StringComparer.Ordinal));
        Assert.Equal("Animated +0x358 -> +0x18 @ +0x8", Assert.Single(web.Paths["Art/Models/Items/Armours/BodyArmours/Robe.ao"]));
        Assert.Equal("Animated +0x100 (a wide string)", Assert.Single(web.Paths["Metadata/Items/Armours/Helmets/Circlet.ao"]));
        Assert.False(web.Stopped);

        // ONE HOP SHORT: the record is not read, so its name is not found.
        PlayerLook.Webbed shallow = PlayerLook.Web(memory, [("Animated", Animated, 0x400)], depth: 1, block: 0x200, mostBlocks: 1000, TimeSpan.FromSeconds(30), null);
        Assert.Equal(["Metadata/Items/Armours/Helmets/Circlet.ao"], shallow.Paths.Keys);
        Assert.Contains("  Metadata/Items/Armours/Helmets/Circlet.ao", web.Said());
    }

    /// <summary>A component is written word by word, the list it begins and ends whole, and one hop on from each pointer.</summary>
    [Fact]
    public void ACOMPONENTIsWrittenWholeWithTheListItHolds()
    {
        const ulong component = 0x6000_0000, list = 0x7000_0000, row = 0x7100_0000;
        var memory = new FakeMemoryReader();
        memory.Place(component, new byte[0x150]);
        memory.Place(list, new byte[0x88 * 2]);
        memory.Place(row, new byte[0x80]);
        memory.Place(component + 0x120, list);
        memory.Place(component + 0x128, list + (0x88 * 2));
        memory.Place(list + 0x10, row);
        memory.Place(list + 0x18, 1.5f);
        memory.PlaceUtf16(row, "Metadata/Items/Armours/Gloves/Wraps");

        List<string> lines = PlayerLook.Whole(
            memory, "Inventories", component, 0x150, address => address >= component && address < component + 0x150 ? "-> Inventories" : address == 0x3FC0_0000 ? "-> unreadable" : "-> heap");
        string text = string.Join('\n', lines);
        Assert.StartsWith("=== Inventories whole: 0x150 bytes at 0x60000000", lines[0], StringComparison.Ordinal);
        Assert.Contains("--- the list begun at +0x120 and ended at +0x128: 0x110 bytes at 0x70000000", text, StringComparison.Ordinal);
        Assert.Contains("--- one hop on: 0x80 bytes at 0x71000000", text, StringComparison.Ordinal);
        Assert.Contains("\"Metadata/Items/Armours/Gloves/Wraps\"", text, StringComparison.Ordinal);
        Assert.Contains("floats 1.5, 0", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// The pieces the game draws the player with, behind Render.Pieces as v0.1.393's captures found them: a slot's word points at an object whose
    /// +0x08 is the record of its .ao, the body armour's attachment one word deeper; a record naming no .ao, a float and a record read twice are
    /// told apart, and the slots' pieces are those at +0x08, each once.
    /// </summary>
    [Fact]
    public void THEPIECESAreReadFromWhatRenderPointsAt()
    {
        const ulong render = 0x1100_0000, composition = 0x1200_0000, wand = 0x1300_0000, holder = 0x1310_0000, sheet = 0x1320_0000;
        const ulong wandRecord = 0x1400_0000, skirtRecord = 0x1410_0000, sheetRecord = 0x1420_0000;
        var memory = new FakeMemoryReader();
        memory.Place(render, new byte[0x630]);
        memory.Place(composition, new byte[PlayerPieces.Bytes]);
        foreach (ulong one in new[] { wand, holder, sheet, wandRecord, skirtRecord, sheetRecord })
        {
            memory.Place(one, new byte[0x100]);
        }

        memory.Place(render + 0x1D0, composition);
        memory.Place(composition + 0x00, holder);
        memory.Place(composition + 0x30, wand);
        memory.Place(composition + 0x58, wand);
        memory.Place(composition + 0x60, sheet);
        memory.Place(composition + 0x68, 1.5f);
        memory.Place(wand + 0x08, wandRecord);
        memory.Place(holder + 0x98, skirtRecord);
        memory.Place(sheet + 0x08, sheetRecord);
        memory.PlaceStdWString(wandRecord + 0x08, "Metadata/Items/Weapons/Wands/WandHeld.ao", 0x1500_0000);
        memory.PlaceStdWString(skirtRecord + 0x08, "Metadata/Items/Armours/BodyArmours/Robe/SkirtHeld.ao", 0x1510_0000);
        memory.PlaceStdWString(sheetRecord + 0x08, "Art/Models/Items/Weapons/Wands/Wand.sm", 0x1520_0000);
        OffsetSchema schema = Schema();

        ulong found = PlayerPieces.CompositionOf(memory, schema, render);
        Assert.Equal(composition, found);
        IReadOnlyList<PlayerPieces.Piece> pieces = PlayerPieces.Read(memory, schema, found);
        Assert.Equal(
            [(0x00, 0x98, "Metadata/Items/Armours/BodyArmours/Robe/SkirtHeld.ao"), (0x30, 0x08, "Metadata/Items/Weapons/Wands/WandHeld.ao"), (0x58, 0x08, "Metadata/Items/Weapons/Wands/WandHeld.ao")],
            pieces.Select(one => (one.At, one.Inner, one.Path)));
        Assert.Equal(["Metadata/Items/Weapons/Wands/WandHeld.ao"], PlayerPieces.Slots(pieces).Select(one => one.Path));
        Assert.Equal(0UL, PlayerPieces.CompositionOf(memory, schema, 0));
        Assert.Empty(PlayerPieces.Read(memory, schema, 0));
    }

    /// <summary>The real schema, found up from the test's folder.</summary>
    private static OffsetSchema Schema()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "schema", "poe2.offsets.json")))
        {
            folder = folder.Parent;
        }

        return SchemaJson.Load(Path.Combine(folder!.FullName, "schema", "poe2.offsets.json"));
    }

    /// <summary>
    /// For each drawn piece, every visual identity row and string column naming it, a list's values included - so the capture says which
    /// per-body column the game took (an IntB beside an Int); a piece no row names says so.
    /// </summary>
    [Fact]
    public void EACHDrawnPieceIsSetBesideTheRowsAndColumnsNamingIt()
    {
        QuestTableLayouts layouts = Layouts();
        int size = layouts.RowSizeOf("ItemVisualIdentity");
        int idAt = layouts.OffsetOf("ItemVisualIdentity", "Id");
        int intAt = layouts.OffsetOf("ItemVisualIdentity", "column_50");
        int intBAt = layouts.OffsetOf("ItemVisualIdentity", "column_71");
        var variable = new List<byte>();
        int Text(string text)
        {
            int at = 8 + variable.Count;
            variable.AddRange(Encoding.Unicode.GetBytes(text));
            variable.AddRange([0, 0]);
            return at;
        }

        int bootsInt = Text("Metadata/Items/Armours/Boots/Baron/BaronBootsInt.ao"), bootsIntB = Text("Metadata/Items/Armours/Boots/Baron/BaronBootsIntB.ao");
        int[] ids = [Text("Baron"), Text("Other")];
        int Lists(int value)
        {
            int at = 8 + variable.Count;
            variable.AddRange(BitConverter.GetBytes((ulong)value));
            return at;
        }

        int intList = Lists(bootsInt), intBList = Lists(bootsIntB);
        DatFile art = Assert.IsType<DatFile>(DatFile.Parse(Dat(2, size, (bytes, at, row) =>
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + idAt), (ulong)ids[row]);
            if (row == 0)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + intAt), 1);
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + intAt + 8), (ulong)intList);
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + intBAt), 1);
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + intBAt + 8), (ulong)intBList);
            }
        }, [.. variable])));

        var named = CapturePlayerLook.Naming(art, layouts.Tables["ItemVisualIdentity"],
            ["Metadata/Items/Armours/Boots/Baron/BaronBootsIntB.ao", "Metadata/Items/Armours/Robe/SkirtHeld.ao", "Metadata/Items/Armours/Boots/Baron/BaronBootsIntB.ao"]).ToList();
        Assert.Equal(2, named.Count);
        Assert.Equal([(0, "column_71 (a list)")], named[0].Where);
        Assert.Empty(named[1].Where);
    }

    /// <summary>An .act names its parent on a "parent" line, behind the byte order mark - which the files side follows as an .ao's extends.</summary>
    [Fact]
    public void ANACTsParentIsFollowedAsAnAosExtends()
    {
        Assert.Equal(
            "Metadata/Characters/CharacterFemale.act",
            CapturePlayerLook.Extends("﻿parent \"Metadata/Characters/CharacterFemale.act\"\nanimated_object \"Metadata/Characters/Character.ao\"\n", "Metadata/Characters/Int/IntFour.act"));
        Assert.Null(CapturePlayerLook.Extends("﻿parent \"Metadata/Characters/CharacterFemale.act\"\n", "Metadata/Characters/Int/IntFour.ao"));
    }

    /// <summary>
    /// A body's sockets as Character.ao writes them - a socket and its indented parent, turn, shift and scale - each with its bone's number in
    /// the rig, the piece's own socket winning over the one it inherits, a parent the rig lacks said as no bone.
    /// </summary>
    [Fact]
    public void ABODYSSocketsAreReadWithTheirBones()
    {
        AnimationSkeleton rig = AnimationSkeleton.Read(File.ReadAllBytes(Fixture("basicskeleton-rig.headers.ast")));
        int chest = rig.Bones.Select(one => one.Name).ToList().IndexOf("chest_jntBnd");
        Assert.True(chest > 0, "the fixture rig names no chest_jntBnd");
        AnimatedObject child = AnimatedObject.Parse(
            "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tsocket = \"R_Weapon\"\n\t\t\tparent = \"chest_jntBnd\"\n"
            + "\t\t\ttranslation = \"0 -4.45 -12.85\"\n\t}\n}\n");
        AnimatedObject parent = AnimatedObject.Parse(
            "version 3\nclient\n{\n\tClientAnimationController\n\t{\n\t\tskeleton = \"Art/Models/CharactersFour/OneRig.ast\"\n"
            + "\t\tsocket = \"R_Weapon\"\n\t\t\tparent = \"root_jntBnd\"\n\t\tsocket = \"sheath_Staff\"\n\t\t\tparent = \"aux_Nowhere_attachment\"\n"
            + "\t\t\trotation = \"-0 0.532 0\"\n\t\t\ttranslation = \"23.564 7.315 62.935\"\n\t\tsocket = \"aux_STRIKE_POSITION\"\n\t\t\ttranslation = \"0 -65 0\"\n\t}\n}\n");

        IReadOnlyList<BodySocket> sockets = BodySockets.Of([child, parent], rig);
        Assert.Equal(["R_Weapon", "sheath_Staff", "aux_STRIKE_POSITION"], sockets.Select(one => one.Name));
        Assert.Equal(("chest_jntBnd", chest), (sockets[0].Parent, sockets[0].Bone));
        Assert.Equal(new System.Numerics.Vector3(0f, -4.45f, -12.85f), sockets[0].Translation);
        Assert.Equal(("aux_Nowhere_attachment", -1), (sockets[1].Parent, sockets[1].Bone));
        Assert.Equal(0.532f, sockets[1].Rotation.Y, 4);
        Assert.Equal(System.Numerics.Vector3.One, sockets[1].Scale);
        Assert.Equal((string.Empty, -1), (sockets[2].Parent, sockets[2].Bone));
    }

    /// <summary>
    /// Every word of the pieces' objects naming a socket: a socket's bone by its number, its name as narrow or wide text at a word's start -
    /// and the slots' objects side by side where they differ, a held piece's socket word standing out from a skin's.
    /// </summary>
    [Fact]
    public void AHELDPiecesSocketIsFoundInItsObjectAndSetBesideTheOthers()
    {
        BodySocket[] sockets = [new("R_Weapon", "aux_R_Weapon_attachment_jntBnd", 57, default, default, System.Numerics.Vector3.One),
            new("L_Weapon", "aux_L_Weapon_attachment_jntBnd", 42, default, default, System.Numerics.Vector3.One)];
        string[] bones = [.. Enumerable.Range(0, 60).Select(one => one switch { 57 => "aux_R_Weapon_attachment_jntBnd", 42 => "aux_L_Weapon_attachment_jntBnd", _ => "bone_" + one })];
        byte[] wand = new byte[0x40], focus = new byte[0x40], boots = new byte[0x40];
        BinaryPrimitives.WriteInt32LittleEndian(wand.AsSpan(0x18), 57);
        BinaryPrimitives.WriteInt32LittleEndian(focus.AsSpan(0x18), 42);
        Encoding.ASCII.GetBytes("L_Weapon").CopyTo(focus, 0x20);
        Encoding.Unicode.GetBytes("aux_R_Weapon_attachment_jntBnd").AsSpan(0, 32).CopyTo(wand.AsSpan(0x20));
        PlayerLook.PieceObject[] objects = [new("+0x030", 0x1000, wand), new("+0x058", 0x2000, focus), new("+0x130", 0x3000, boots)];

        string text = string.Join('\n', CapturePlayerLook.Matches(objects, sockets, bones));
        Assert.Contains("+0x030 +0x018\tint 57\tbone aux_R_Weapon_attachment_jntBnd - socket R_Weapon", text, StringComparison.Ordinal);
        Assert.Contains("+0x058 +0x018\tint 42\tbone aux_L_Weapon_attachment_jntBnd - socket L_Weapon", text, StringComparison.Ordinal);
        Assert.Contains("+0x058 +0x020\t\"L_Weapon\"\ta socket", text, StringComparison.Ordinal);
        Assert.DoesNotContain("+0x130", text, StringComparison.Ordinal);

        var seen = new PlayerLook.Seen("Metadata/Characters/Int/IntFourb", "Metadata/Characters/Int/IntFourB.ao", [], [])
        {
            Slots = [(0x30, 8, "Metadata/Items/Weapons/WandHeld.ao"), (0x58, 8, "Metadata/Items/Armours/Focus/FocusHeld.ao"), (0x130, 8, "Metadata/Items/Armours/Boots/BootsIntB.ao"), (0x0, 0x98, "Metadata/Items/Skirt.ao")],
            Objects = objects,
        };
        List<string> beside = [.. CapturePlayerLook.Beside(seen)];
        Assert.Equal("  the slots' objects side by side, every 32-bit word where they differ: +0x030 WandHeld.ao | +0x058 FocusHeld.ao | +0x130 BootsIntB.ao", beside[0]);
        Assert.Contains("    +0x018\t57 | 42 | 0", beside);
        Assert.DoesNotContain(beside, one => one.StartsWith("    +0x000", StringComparison.Ordinal));
    }

    /// <summary>The vendored layout of ItemVisualHeldBodyModel is dat-schema's PoE2 one: a visual identity and 28 strings, 240 bytes a row.</summary>
    [Fact]
    public void THEHELDBodyModelsLayoutIsPoE2s()
    {
        QuestTableLayouts layouts = Layouts();
        Assert.Equal(240, layouts.RowSizeOf("ItemVisualHeldBodyModel"));
        Assert.Equal(16 + (7 * 8), layouts.OffsetOf("ItemVisualHeldBodyModel", "MarauderBone"));
    }

    /// <summary>A fixture's path, found up from the test's folder.</summary>
    private static string Fixture(string name)
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !Directory.Exists(Path.Combine(folder.FullName, "tests", "fixtures")))
        {
            folder = folder.Parent;
        }

        return Path.Combine(folder!.FullName, "tests", "fixtures", name);
    }

    /// <summary>The part is started at the press, as every piece of work the app offers the capture is - a review found it offered and never started.</summary>
    [Fact]
    public void THEPARTRunsAtThePress()
    {
        Assert.True(CaptureParts.RunsAtPress(CaptureParts.PlayerLook));
        Assert.Contains(CaptureParts.All, one => one.Key == CaptureParts.PlayerLook && one.File == CaptureReport.PlayerLookFile);
    }

    /// <summary>
    /// The survey of ItemVisualIdentity's string columns over the whole table, by the vendored layout: a value counts for the longest body
    /// it holds (IntFourB, not IntFour inside it), a list's every value counts, a column holding nothing is left out; the bodies are the
    /// Characters rows' models under Metadata/Characters/ and his own.
    /// </summary>
    [Fact]
    public void THESURVEYCountsEachColumnsBodiesOverTheWholeTable()
    {
        QuestTableLayouts layouts = Layouts();
        int size = layouts.RowSizeOf("ItemVisualIdentity");
        int idAt = layouts.OffsetOf("ItemVisualIdentity", "Id");
        int oneAt = layouts.OffsetOf("ItemVisualIdentity", "column_31");
        int listAt = layouts.OffsetOf("ItemVisualIdentity", "column_48");
        Assert.True(size > 0 && idAt >= 0 && oneAt >= 0 && listAt >= 0, "the vendored layout lays out nothing");
        Assert.True(layouts.Tables["ItemVisualIdentity"].Single(column => column.Name == "column_48").Array);

        var variable = new List<byte>();
        int Text(string text)
        {
            int at = 8 + variable.Count;
            variable.AddRange(Encoding.Unicode.GetBytes(text));
            variable.AddRange([0, 0]);
            return at;
        }

        int robeB = Text("Art/Models/Items/Armours/IntFourB/Robe.sm"), robe = Text("Art/Models/Items/Armours/IntFour/Robe.sm");
        int plate = Text("Art/Models/Items/Armours/StrFour_Plate.ao"), glow = Text("Metadata/Items/Shared/Glow.ao");
        int[] ids = [Text("A"), Text("B"), Text("C"), Text("D")];
        int list = 8 + variable.Count;
        variable.AddRange(BitConverter.GetBytes((ulong)glow));
        variable.AddRange(BitConverter.GetBytes((ulong)plate));
        DatFile art = Assert.IsType<DatFile>(DatFile.Parse(Dat(4, size, (bytes, at, row) =>
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + idAt), (ulong)ids[row]);
            if (row < 3)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + oneAt), (ulong)(row == 1 ? robe : robeB));
            }

            if (row == 0)
            {
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + listAt), 2);
                BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + listAt + 8), (ulong)list);
            }
        }, [.. variable])));

        // THE BODIES: the Characters rows' models under Metadata/Characters/, with or without an extension, and his own.
        variable.Clear();
        int[] models = [Text("Metadata/Characters/Int/IntFour.ao"), Text("Metadata/Characters/Str/StrFour")];
        DatFile characters = Assert.IsType<DatFile>(DatFile.Parse(Dat(1, 16, (bytes, at, _) =>
        {
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at), (ulong)models[0]);
            BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(at + 8), (ulong)models[1]);
        }, [.. variable])));
        string[] bodies = CapturePlayerLook.Bodies(characters, "Metadata/Characters/Int/IntFourB.ao");
        Assert.Equal(["IntFour", "IntFourB", "StrFour"], bodies);

        List<string> lines = [.. CapturePlayerLook.Columns(art, layouts.Tables["ItemVisualIdentity"], bodies)];
        string text = string.Join('\n', lines);
        Assert.Contains("  Id\t4 of 4 rows, 4 values", lines);
        int one = lines.IndexOf("  column_31\t3 of 4 rows, 3 values");
        Assert.True(one >= 0, text);
        Assert.Equal("    extensions: .sm 3", lines[one + 1]);
        Assert.Equal("    bodies: IntFourB 2 | IntFour 1", lines[one + 2]);
        Assert.Equal("    folders (2): Art/Models/Items/Armours/IntFourB 2 | Art/Models/Items/Armours/IntFour 1", lines[one + 3]);
        int listed = lines.IndexOf("  column_48 (a list)\t1 of 4 rows, 2 values");
        Assert.True(listed >= 0, text);
        Assert.Equal("    bodies: StrFour 1", lines[listed + 2]);
        Assert.Equal("    first: \"Metadata/Items/Shared/Glow.ao\" | \"Art/Models/Items/Armours/StrFour_Plate.ao\"", lines[listed + 4]);
        Assert.DoesNotContain("DDSFile", text, StringComparison.Ordinal);
        int strings = layouts.Tables["ItemVisualIdentity"].Count(column => column.Type == "string");
        Assert.Equal(string.Create(CultureInfo.InvariantCulture, $"  3 of {strings} string columns and lists hold anything"), lines[^1]);
    }

    /// <summary>The vendored layouts, found up from the test's folder.</summary>
    private static QuestTableLayouts Layouts()
    {
        var folder = new DirectoryInfo(AppContext.BaseDirectory);
        while (folder is not null && !File.Exists(Path.Combine(folder.FullName, "data", "item-tables.json")))
        {
            folder = folder.Parent;
        }

        Assert.NotNull(folder);
        return Assert.IsType<QuestTableLayouts>(QuestTableLayouts.Load(Path.Combine(folder!.FullName, "data", "item-tables.json")));
    }

    /// <summary>A .datc64: the row count, the rows, the separator, then the variable section.</summary>
    private static byte[] Dat(int rows, int rowSize, Action<byte[], int, int> fill, byte[] variable)
    {
        var bytes = new byte[4 + (rows * rowSize) + 8 + variable.Length];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)rows);
        for (var row = 0; row < rows; row++)
        {
            fill(bytes, 4 + (row * rowSize), row);
        }

        bytes.AsSpan(4 + (rows * rowSize), 8).Fill(0xBB);
        variable.CopyTo(bytes, 4 + (rows * rowSize) + 8);
        return bytes;
    }

    /// <summary>What names a game file: under Metadata/ or Art/, or a folder and a file's extension - not words, not a bare name.</summary>
    [Fact]
    public void AGAMEPathIsAFolderAndAFile()
    {
        Assert.True(PlayerLook.GamePath("Metadata/Characters/Int/IntFourB.ao"));
        Assert.True(PlayerLook.GamePath("Art/Models/Items/Weapons/Focus.fmt"));
        Assert.True(PlayerLook.GamePath("Data/Balance/Inventories.datc64"));
        Assert.False(PlayerLook.GamePath("IntFourB.ao"));
        Assert.False(PlayerLook.GamePath("not a path at all, just words"));
        Assert.False(PlayerLook.GamePath("Metadata/é"));
    }

    /// <summary>The files side follows what a text file extends; its offline reader says each written file's lines a piece attaches by.</summary>
    [Fact]
    public void THEOFFLINEReaderSaysHowEachWrittenPieceAttaches()
    {
        Assert.Equal("Metadata/Characters/Int/Int.ao", CapturePlayerLook.Extends("version 2\nextends \"Metadata/Characters/Int/Int\"\n", "Metadata/Characters/Int/IntFourB.ao"));
        Assert.Null(CapturePlayerLook.Extends("version 2\nextends \"nothing\"\n", "x.ao"));

        string folder = Directory.CreateTempSubdirectory("player-look").FullName;
        try
        {
            string written = Path.Combine(folder, "mem", CapturePlayerLook.Folder, "Metadata", "Items", "Robe.ao");
            Directory.CreateDirectory(Path.GetDirectoryName(written)!);
            File.WriteAllText(written, "version 2\nextends \"Metadata/Parent\"\nAnimatedRender\n{\n\tskin = \"Art/Models/Robe.sm\"\n\tcolour = 1\n}\nAttachedAnimatedObject\n{\n\tbone = \"spine\"\n}\n", Encoding.UTF8);
            string replay = CapturePlayerLook.Replay(folder);
            Assert.StartsWith("player look replay: 1 files written whole", replay, StringComparison.Ordinal);
            Assert.Contains("Metadata/Items/Robe.ao\textends Metadata/Parent.ao", replay, StringComparison.Ordinal);
            Assert.Contains("  skin = \"Art/Models/Robe.sm\"", replay, StringComparison.Ordinal);
            Assert.Contains("  bone = \"spine\"", replay, StringComparison.Ordinal);
            Assert.DoesNotContain("colour", replay, StringComparison.Ordinal);
            Assert.StartsWith("player look replay: no player-look/", CapturePlayerLook.Replay(Directory.CreateTempSubdirectory("empty").FullName), StringComparison.Ordinal);
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }
    }
}
