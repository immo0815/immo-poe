using PoEformance.Core.Schema;
using PoEformance.Game.Entities;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The player as the Monster Book takes him (PlayerDress): his body read through Animated's model chain as the schema has it, and the model he
/// is drawn as - his class's body with the game's pieces hung at his root, each a skin on his rig alone, unscaled - under a key no monster has.
/// </summary>
public class PlayerDressTests
{
    /// <summary>Animated +0x358 to the model info, its +0x18 to the record, whose +0x08 names the .ao - nought anywhere is no body.</summary>
    [Fact]
    public void HISBodyIsTheModelHisAnimatedComponentLoaded()
    {
        const ulong animated = 0x2100_0000, info = 0x2200_0000, record = 0x2300_0000;
        var memory = new FakeMemoryReader();
        memory.Place(animated, new byte[0x380]);
        memory.Place(info, new byte[0x40]);
        memory.Place(record, new byte[0x40]);
        memory.Place(animated + 0x358, info);
        memory.Place(info + 0x18, record);
        memory.PlaceStdWString(record + 0x08, "Metadata/Characters/Int/IntFourB.ao", 0x2400_0000);
        OffsetSchema schema = Schema();

        Assert.Equal("Metadata/Characters/Int/IntFourB.ao", PlayerDress.ModelOf(memory, schema, animated));
        Assert.Equal(string.Empty, PlayerDress.ModelOf(memory, schema, 0));
        memory.Place(info + 0x18, 0UL);
        Assert.Equal(string.Empty, PlayerDress.ModelOf(memory, schema, animated));
    }

    /// <summary>
    /// The model: the class's name, his body as its one file, every piece at his root drawn as a skin alone, unscaled; the key changes with
    /// the pieces, so a take after a change of armour loads again; with no body there is nothing to draw.
    /// </summary>
    [Fact]
    public void HEIsDrawnAsHisBodyWithTheGamesPiecesAtHisRoot()
    {
        var dress = new PlayerDress(
            "Metadata/Characters/Int/IntFourb", "Sorceress", "Metadata/Characters/Int/IntFourB.ao",
            ["Metadata/Items/Armours/BodyArmours/BetaDoryaniArmour/BetaDoryaniArmourBodyInt.ao", "Metadata/Items/Armours/Boots/BetaBaron/BetaBaronBootsIntB.ao"],
            string.Empty);
        Assert.True(dress.Ready);
        MonsterVariety one = dress.Variety();
        Assert.Equal("Sorceress", one.Name);
        Assert.Equal(["Metadata/Characters/Int/IntFourB.ao"], one.AoFiles);
        Assert.Equal(0, one.ModelSize);
        Assert.Equal(2, one.Worn.Count);
        Assert.All(one.Worn, piece => Assert.True(piece.SkinOnly && piece.Socket == "<root>"));
        Assert.StartsWith("player:Metadata/Characters/Int/IntFourb|", dress.Key, StringComparison.Ordinal);
        Assert.NotEqual(dress.Key, (dress with { Pieces = [dress.Pieces[0]] }).Key);

        // NO CLASS NAME READ: the path's last word.
        Assert.Equal("IntFourb", (dress with { Class = string.Empty }).Variety().Name);
        Assert.False(PlayerDress.None.Ready);
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
}
