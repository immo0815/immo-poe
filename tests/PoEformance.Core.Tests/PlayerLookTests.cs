using System.Text;
using PoEformance.Features;
using PoEformance.Game.Diagnostics;
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
