using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// A doodad set's line draws one of its members, by its weight, chosen by where the line stands - the same every build; a file that is no
/// set draws itself.
/// </summary>
public class DoodadSetTests
{
    /// <summary>WallSand_Cc_set.ao as the stage sweep's capture carried it (v0.1.348).</summary>
    private const string WallSand = """
        version 3
        extends "Metadata/AOSetParent"

        AOSet
        {
        	ao = "100 Metadata/Terrain/Desert/Maraketh/WallSand_Cc_01.ao"
        	ao = "100 Metadata/Terrain/Desert/Maraketh/WallSand_Cc_02.ao"
        	ao = "100 Metadata/Terrain/Desert/Maraketh/WallSand_Cc_03.ao"
        }
        """;

    private static byte[] Text(string text) => [.. Encoding.Unicode.GetPreamble(), .. Encoding.Unicode.GetBytes(text)];

    [Fact]
    public void ASETSLineDrawsOneMemberByItsWeightAndItsPlace()
    {
        const string set = "Metadata/Terrain/Desert/Maraketh/ThickWall/WallSand_Cc_set.ao";
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [set] = Text(WallSand),
            ["Metadata/Heavy_set.ao"] = Text("version 3\nAOSet\n{\n\tao = \"3 Metadata/A.ao\"\n\tao = \"1 Metadata/B.ao\"\n}\n"),
            ["Metadata/Outer_set.ao"] = Text("version 3\nAOSet\n{\n\tao = \"1 Metadata/Heavy_set.ao\"\n}\n"),
        };

        Assert.Equal(
            [(100, "Metadata/Terrain/Desert/Maraketh/WallSand_Cc_01.ao"), (100, "Metadata/Terrain/Desert/Maraketh/WallSand_Cc_02.ao"), (100, "Metadata/Terrain/Desert/Maraketh/WallSand_Cc_03.ao")],
            RoomModels.Members(AnimatedObject.Read(files[set])));

        var sets = new Dictionary<string, IReadOnlyList<(int Weight, string Ao)>>(StringComparer.OrdinalIgnoreCase);
        RoomDoodad line = new(0, 0, 0f, 1f, set, string.Empty);
        var counts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var x = 0; x < 30; x++)
        {
            for (var y = 0; y < 30; y++)
            {
                string drawn = RoomModels.Member(line, new Vector3(x * 23.5f, y * 23.5f, 0f), files.GetValueOrDefault, sets).Ao;
                counts[drawn] = counts.GetValueOrDefault(drawn) + 1;
            }
        }

        // EVERY MEMBER, NEAR ITS SHARE, and the same choice again at the same place.
        Assert.Equal(3, counts.Count);
        Assert.All(counts.Values, count => Assert.InRange(count, 240, 360));
        Assert.Equal(
            RoomModels.Member(line, new Vector3(47f, 94f, 0f), files.GetValueOrDefault, sets).Ao,
            RoomModels.Member(line, new Vector3(47f, 94f, 0f), files.GetValueOrDefault, sets).Ao);

        // BY WEIGHT, through a set naming a set.
        int heavy = 0;
        for (var at = 0; at < 1000; at++)
        {
            heavy += RoomModels.Member(line with { Ao = "Metadata/Outer_set.ao" }, new Vector3(at * 7f, at * 3f, 0f), files.GetValueOrDefault, sets).Ao == "Metadata/A.ao" ? 1 : 0;
        }

        Assert.InRange(heavy, 690, 810);

        // NO SET, ITSELF - and a file named so but holding no AOSet, itself too.
        Assert.Equal("Metadata/Rock.ao", RoomModels.Member(line with { Ao = "Metadata/Rock.ao" }, Vector3.Zero, files.GetValueOrDefault, sets).Ao);
        Assert.Equal("Metadata/Missing_set.ao", RoomModels.Member(line with { Ao = "Metadata/Missing_set.ao" }, Vector3.Zero, files.GetValueOrDefault, sets).Ao);
    }
}
