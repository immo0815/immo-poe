using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The capture's model-panes part: each pane's lines, every doodad file of its room with what became of its lines, and each file that did not draw whole with what it extends.
/// </summary>
public sealed class CapturePanesTests
{
    private const string Water = "Metadata/Terrain/Doodads/Water/WaterPlane3x3.ao";
    private const string Parent = "Metadata/Terrain/Doodads/Water/WaterBase";

    [Fact]
    public void APANESLinesAndItsRoomsDoodadFilesAreWrittenAndWhatDidNotDrawIsWrittenWhole()
    {
        DoodadFile rock = new("Metadata/Doodads/Rock.ao", 3, 2, 0, 1, 0, string.Empty, new Vector3(10f, 20f, 0f))
        {
            Triangles = 2,
            Least = new Vector3(-1f, -1f, 0f),
            Most = new Vector3(1f, 1f, 0f),
            Materials = ["art/rock.mat", "art/rock.mat"],
            Modes = ["AlphaTest", "AlphaTest"],
        };
        DoodadFile water = new(Water, 31, 0, 31, 0, 0, "no SkinMesh or FixedMesh anywhere in the .ao files or what they extend", new Vector3(500f, 250f, 125f));
        var model = new MonsterModel(Square(), null, "Metadata/Monsters/Malgor/Malgor.smd", string.Empty, string.Empty)
        {
            Stage = "Metadata/Terrain/Maps/Port/Rooms/Unique/Boss_01.arm",
            StageMove = "tile doodads: 31 from the doodad lists of 1 tile files; 31 doodads did not draw",
            DoodadFiles = [rock, water],
        };

        string text = CapturePanes.Write(Install, [new CapturePane("monster", ["read 1 MB in 2 files", "stage: Boss_01.arm"], model)]);

        Assert.StartsWith("1 model panes showing a model, 2 doodad files of their rooms with what became of each one's lines, 1 that did not draw written whole", text, StringComparison.Ordinal);
        Assert.Contains("=== the monster book's pane", text, StringComparison.Ordinal);
        Assert.Contains("  info\tstage: Boss_01.arm", text, StringComparison.Ordinal);
        Assert.Contains("  stage's own lines\ttile doodads: 31", text, StringComparison.Ordinal);
        Assert.Contains("  doodad files: 2, 34 lines - 2 laid, 31 not drawn, 1 left out by the cap, 0 unseen", text, StringComparison.Ordinal);

        // BOTH HALVES: the box and where the first line puts it, and the materials with their blend once each.
        Assert.Contains("    3\t2\t0\t1\t0\t2\t-1 -1 0 .. 1 1 0\t10 20 0\tMetadata/Doodads/Rock.ao\tart/rock.mat (AlphaTest)\t", text, StringComparison.Ordinal);
        Assert.Contains("    31\t0\t31\t0\t0\t0\t-\t500 250 125\t" + Water + "\t-\tno SkinMesh", text, StringComparison.Ordinal);

        // THE FILE THAT DID NOT DRAW, WHOLE, and what it extends - read with the extension added, as the model walk reads it.
        Assert.Contains("##### " + Water + "  (did not draw)", text, StringComparison.Ordinal);
        Assert.Contains("  | \tWaterPlane", text, StringComparison.Ordinal);
        Assert.Contains("##### " + Parent + "  (extended by " + Water + ")", text, StringComparison.Ordinal);
        Assert.Contains("  read as " + Parent + ".ao", text, StringComparison.Ordinal);
        Assert.Contains("  | \tWaterBody", text, StringComparison.Ordinal);
        Assert.DoesNotContain("##### Metadata/Doodads/Rock.ao", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WITHOUTAnInstallTheFilesAreNamedAndSaidSo()
    {
        DoodadFile water = new(Water, 1, 0, 1, 0, 0, "gone", Vector3.Zero);
        var model = new MonsterModel(Square(), null, "room.arm", string.Empty, string.Empty) { DoodadFiles = [water] };

        string text = CapturePanes.Write(null, [new CapturePane("tile", [], model)]);

        Assert.Contains("##### " + Water + "  (did not draw)\n  (no install to read it from)", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    private static byte[]? Install(string path) => path switch
    {
        Water => Encoding.UTF8.GetBytes("version 3\nextends \"" + Parent + "\"\nclient\n{\n\tWaterPlane\n\t{\n\t\tsize = 3\n\t}\n}\n"),
        Parent + ".ao" => Encoding.UTF8.GetBytes("version 3\nextends \"nothing\"\nclient\n{\n\tWaterBody\n\t{\n\t}\n}\n"),
        _ => null,
    };

    private static SkinnedMesh Square()
    {
        Vector3[] positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 1f, 0f)];
        return SkinnedMesh.Of(positions, [.. positions.Select(_ => -Vector3.UnitZ)], [0, 1, 2], Vector3.Zero, Vector3.One, null, [new MeshShape("0", 0, 3)]);
    }
}
