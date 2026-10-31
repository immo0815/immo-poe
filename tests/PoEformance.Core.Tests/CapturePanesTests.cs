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
        // A FILE THAT DREW is not written as one that did not - only among the largest (ADOODADWithARig...).
        Assert.DoesNotContain("##### Metadata/Doodads/Rock.ao  (did not draw)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ADOODADWithARigAndTheLargestAreWrittenWholeWithTheirAnimations()
    {
        // PORT'S SHIP rocks with the waves in the game and stands still here: what moves it is in its .ao, or the engine's.
        DoodadFile ship = new("Metadata/Terrain/Maps/Port/Tiles/boss_boat.ao", 1, 1, 0, 0, 0, string.Empty, Vector3.Zero)
        {
            Triangles = 355726,
            Rig = "Art/Models/Terrain/Port/boss_boat.ast",
            Animations = ["idle (30 fps, 4 tracks, kind 110)"],
        };
        DoodadFile rock = new("Metadata/Doodads/Rock.ao", 3, 3, 0, 0, 0, string.Empty, Vector3.Zero) { Triangles = 900 };
        DoodadFile pebble = new("Metadata/Doodads/Pebble.ao", 3, 3, 0, 0, 0, string.Empty, Vector3.Zero) { Triangles = 10 };
        var model = new MonsterModel(Square(), null, "room.arm", string.Empty, string.Empty) { DoodadFiles = [pebble, ship, rock] };

        string text = CapturePanes.Write(Install, [new CapturePane("monster", [], model)]).ReplaceLineEndings("\n");

        Assert.Contains("=== the doodad files with a rig (1) and the 2 largest without one, whole, with their animations", text, StringComparison.Ordinal);
        Assert.Contains("  Metadata/Terrain/Maps/Port/Tiles/boss_boat.ao  355726 triangles, 1 lines, 1 laid  rig Art/Models/Terrain/Port/boss_boat.ast\n    animation\tidle (30 fps, 4 tracks, kind 110)\n", text, StringComparison.Ordinal);
        Assert.Contains("##### Metadata/Terrain/Maps/Port/Tiles/boss_boat.ao  (has a rig)", text, StringComparison.Ordinal);
        Assert.Contains("##### Metadata/Doodads/Rock.ao  (among the largest)", text, StringComparison.Ordinal);
        Assert.Contains("##### Metadata/Doodads/Pebble.ao  (among the largest)", text, StringComparison.Ordinal);
    }

    [Fact]
    public void WITHOUTAnInstallTheFilesAreNamedAndSaidSo()
    {
        DoodadFile water = new(Water, 1, 0, 1, 0, 0, "gone", Vector3.Zero);
        var model = new MonsterModel(Square(), null, "room.arm", string.Empty, string.Empty) { DoodadFiles = [water] };

        string text = CapturePanes.Write(null, [new CapturePane("tile", [], model)]);

        Assert.Contains("##### " + Water + "  (did not draw)\n  (no install to read it from)", text.ReplaceLineEndings("\n"), StringComparison.Ordinal);
    }

    /// <summary>
    /// The materials under the probe first and those drawn plain after, each whole with what it compiles to and how it is drawn - and their graphs once, whole.
    /// </summary>
    [Fact]
    public void THEMATERIALSUnderTheProbeAndThoseDrawnPlainAreWrittenWhole()
    {
        const string Sand = "Art/Textures/Environment/desert/Shore/Ground/G_VST_Sand01.mat";
        const string Gone = "Art/Textures/Test/gone.mat";
        Vector3[] positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 1f, 0f), new(1f, 1f, 0f), new(2f, 1f, 0f), new(1f, 2f, 0f)];
        SkinnedMesh mesh = SkinnedMesh.Of(
            positions, [.. positions.Select(_ => -Vector3.UnitZ)], [0, 1, 2, 3, 4, 5], Vector3.Zero, new Vector3(2f, 2f, 0f), null,
            [new MeshShape("0", 0, 3), new MeshShape("1", 3, 3)]);
        var model = new MonsterModel(mesh, null, "room.arm", string.Empty, string.Empty)
        {
            ShapeMaterials = [Sand + ":0", Gone],
            ShapeSources = ["Metadata/Terrain/Doodads/Water/WaterPlane3x3.ao", "Metadata/Doodads/Gone.ao"],
        };

        string text = CapturePanes.Write(CaptureGroundTests.Fixture, [new CapturePane("monster", [], model) { Probed = [0] }]).ReplaceLineEndings("\n");

        Assert.Contains("=== the materials under the probe and those drawn plain, whole (2 of 2; 2 drawn plain)", text, StringComparison.Ordinal);

        // THE PROBED ONE FIRST, its graph selector taken off, then the plain one the probe did not name.
        int probed = text.IndexOf("##### " + Sand + "  (under the probe in the monster book's pane · plain · from Metadata/Terrain/Doodads/Water/WaterPlane3x3.ao)", StringComparison.Ordinal);
        int plain = text.IndexOf("##### " + Gone + "  (drawn plain in the monster book's pane · 1 triangles · from Metadata/Doodads/Gone.ao)\n(not in the install)", StringComparison.Ordinal);
        Assert.True(probed >= 0 && plain > probed, text);
        Assert.Matches("graphs name: [^\\n]*InputUV", text);
        Assert.Contains("\nprogram: ", text, StringComparison.Ordinal);
        Assert.Contains("\ndrawn: ", text, StringComparison.Ordinal);
        Assert.Contains("##### Metadata/Materials/Ground/PBRGroundBN.fxgraph  (a graph these materials are instances of)", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A stage made under a master whose swaps it wears says so with no probe click: the pair, the shapes and triangles wearing the swap's
    /// material, the two materials as compiled and what the swap loses - Port's quay stone (MSHD_GraveBrickTile01c.mat, from v0.1.336's
    /// capture) swapped for a desert rock carrying no spray loses the spray, its twin and the wetness it read - and both materials whole
    /// with their graphs.
    /// </summary>
    /// <remarks>
    /// AS PORT'S QUAY WENT DARK: from v0.1.344 a laid or built room puts its master's TileMaterialOverrides on its tiles, Port's swaps the
    /// quay's stone for another, and the spray from occlusion built for it was gone - which no capture said for thirty versions.
    /// </remarks>
    [Fact]
    public void THEMASTERSSwapsAStageWearsAreHeldAgainstWhatEachDoesToItsMaterial()
    {
        const string Quay = "Art/Models/Terrain/Maps/Stronghold/Textures/Architecture/MSHD_GraveBrickTile01c.mat";
        const string Rock = "Art/Textures/Environment/desert/DesertCliffs/DESERT_Rock01c.mat";
        const string Master = "Metadata/Terrain/Maps/Port/master.tsi";
        Vector3[] positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 1f, 0f), new(1f, 1f, 0f), new(2f, 1f, 0f), new(1f, 2f, 0f), new(3f, 3f, 0f), new(4f, 3f, 0f), new(3f, 4f, 0f)];
        SkinnedMesh mesh = SkinnedMesh.Of(
            positions, [.. positions.Select(_ => -Vector3.UnitZ)], [0, 1, 2, 3, 4, 5, 6, 7, 8], Vector3.Zero, new Vector3(4f, 4f, 0f), null,
            [new MeshShape("0", 0, 6), new MeshShape("1", 6, 3)]);
        var model = new MonsterModel(mesh, null, "Metadata/Monsters/Malgor/Malgor.smd", string.Empty, string.Empty)
        {
            Stage = "Metadata/Terrain/Maps/Port/Rooms/Unique/Boss_01.arm",
            ShapeMaterials = [Rock, "Art/Textures/Test/gone.mat"],
            MasterSwaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [Quay] = Rock,
                ["Art/Textures/Test/unworn.mat"] = "Art/Textures/Test/other.mat",
            },
            MasterFile = Master,
        };

        string text = CapturePanes.Write(CaptureGroundTests.Fixture, [new CapturePane("monster", [], model)]).ReplaceLineEndings("\n");

        Assert.EndsWith(", 1 pairs of the masters' material swaps worn, 1 of them changing what their material can do", text[..text.IndexOf('\n', StringComparison.Ordinal)], StringComparison.Ordinal);
        Assert.Contains("=== the master's material swaps each stage wears (1 of 1 rooms and stages shown made under a master that swaps materials)\n", text, StringComparison.Ordinal);
        Assert.Contains("  the monster book's pane\tmade under " + Master + ", whose TileMaterialOverrides swap 2 materials: 1 of them worn here, 1 not\n", text, StringComparison.Ordinal);

        // THE PAIR AND WHAT WEARS IT - two triangles of one shape - then the two compiles side by side: the quay's stone sprays, the
        // rock does not, and the swap takes the spray and its twin with it.
        Assert.Contains("    1 shapes, 2 triangles\twearing the swap's material " + Rock + " - the master swaps " + Quay + " for it\n", text, StringComparison.Ordinal);
        string compiled = text.Split('\n').Single(line => line.StartsWith("      compiled\t", StringComparison.Ordinal));
        Assert.StartsWith("      compiled\tMSHD_GraveBrickTile01c.mat: drawn from its program, sprays, spray from occlusion, reads wetness", compiled, StringComparison.Ordinal);
        Assert.Contains(" · DESERT_Rock01c.mat: drawn from its program", compiled, StringComparison.Ordinal);
        Assert.Contains(" · LOST: sprays, spray from occlusion, reads wetness", compiled, StringComparison.Ordinal);

        // BOTH WHOLE, the swap's material saying what wears it, and their graphs once each - the spray's among them.
        Assert.Contains("=== the two materials of each swap that changes what its material can do, whole (1 of 1 pairs - at most 12, CHOSEN", text, StringComparison.Ordinal);
        Assert.Contains("##### " + Quay + "  (the master " + Master + " swaps it for DESERT_Rock01c.mat · LOST: sprays, spray from occlusion, reads wetness", text, StringComparison.Ordinal);
        Assert.Contains("##### " + Rock + "  (the swap's material in place of MSHD_GraveBrickTile01c.mat, worn by 1 shapes, 2 triangles in the monster book's pane)\n  | ", text, StringComparison.Ordinal);
        Assert.Contains("##### Metadata/Materials/OceanSprayFoam.fxgraph  (a graph these swapped materials are instances of)", text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A room or stage made under no master says so, and one made under a master that swaps nothing it wears says that - each a line, no
    /// pair; a monster standing on nothing is no stage and the section is not written - nor where his stage was asked for and did not
    /// build, which would have said "made under no master" of a stage nobody drew.
    /// </summary>
    [Fact]
    public void ASTAGEUnderNoMasterOrWearingNoSwapSaysSo()
    {
        var swapless = new MonsterModel(Square(), null, "room.arm", string.Empty, string.Empty)
        {
            Kind = ModelKind.Room,
            ShapeMaterials = ["Art/Textures/Test/own.mat"],
            MasterSwaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Art/Textures/Test/a.mat"] = "Art/Textures/Test/b.mat" },
            MasterFile = "Metadata/Terrain/Test/master.tsi",
        };
        var bare = new MonsterModel(Square(), null, "room.arm", string.Empty, string.Empty) { Kind = ModelKind.Room };

        string text = CapturePanes.Write(null, [new CapturePane("tile", [], swapless), new CapturePane("monster", [], bare)]).ReplaceLineEndings("\n");

        Assert.Contains("(1 of 2 rooms and stages shown made under a master that swaps materials)", text, StringComparison.Ordinal);
        Assert.Contains("  the tile book's pane\tmade under Metadata/Terrain/Test/master.tsi, whose TileMaterialOverrides swap 1 materials: 0 of them worn here, 1 not\n", text, StringComparison.Ordinal);
        Assert.Contains("  the monster book's pane\tmade under no master - every material its own\n", text, StringComparison.Ordinal);
        Assert.DoesNotContain("wearing the swap's material", text, StringComparison.Ordinal);

        var monster = new MonsterModel(Square(), null, "monster.smd", string.Empty, string.Empty);
        Assert.DoesNotContain("=== the master's material swaps", CapturePanes.Write(null, [new CapturePane("monster", [], monster)]), StringComparison.Ordinal);

        // A MONSTER STANDING ON A STAGE no master made is listed as a room is; one whose stage did not build names the stage asked for
        // (StagedModels.Of) and stands on nothing.
        MonsterModel standing = monster with { Actor = monster, Stage = "Metadata/Terrain/Test/Arena.tdt" };
        string staged = CapturePanes.Write(null, [new CapturePane("monster", [], standing)]).ReplaceLineEndings("\n");
        Assert.Contains("(0 of 1 rooms and stages shown made under a master that swaps materials)", staged, StringComparison.Ordinal);
        Assert.Contains("  the monster book's pane\tmade under no master - every material its own\n", staged, StringComparison.Ordinal);

        MonsterModel unbuilt = monster with
        {
            Stage = "Metadata/Terrain/Maps/Port/Rooms/Unique/Boss_01.arm",
            StageSaid = "no stage: the area this room was laid in is gone - it is a stage while the player stands in that area",
        };
        Assert.DoesNotContain("=== the master's material swaps", CapturePanes.Write(null, [new CapturePane("monster", [], unbuilt)]), StringComparison.Ordinal);
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
