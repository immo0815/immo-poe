using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The ground painted from the area's MaterialsList: one ground all round in its material, two split by the template mask's
/// green at the block's own coordinates - the MaterialsList's first ground at green nought - and three left plain.
/// </summary>
public sealed class GroundPainterTests
{
    private const string Master = "Metadata/Terrain/Maps/Test/master.tsi";
    private const string Room = "Metadata/Terrain/Maps/Test/Rooms/arena.arm";
    private const string Template = "Art/Models/Terrain/Test/Ground.tgt";
    private const string Mask = "Art/Textures/masks/test.dds";
    private const string Sand = "Metadata/Terrain/Maps/Test/sand.gt";
    private const string Rock = "Metadata/Terrain/Maps/Test/rock.gt";
    private const string Moss = "Metadata/Terrain/Maps/Test/moss.gt";

    [Fact]
    public void THEMASTERBesideARoomsRoomsFolderIsFoundAndNothingElse()
    {
        Assert.Equal(Master, GroundPainter.MasterBeside(Install, Room));
        Assert.Equal(string.Empty, GroundPainter.MasterBeside(Install, "Metadata/Terrain/Maps/Elsewhere/Rooms/arena.arm"));
        Assert.Equal(string.Empty, GroundPainter.MasterBeside(Install, "Metadata/Terrain/Maps/Test/arena.arm"));
    }

    [Fact]
    public void ATILEOfOneGroundWearsItsGroupsHeaviestChoiceWithTheWorldsCoordinates()
    {
        GroundPainter painter = Painter();
        Matrix4x4 place = Matrix4x4.CreateTranslation(500f, 250f, 0f);

        MonsterModel? painted = painter.Paint("one.tdt", Ground(), place, "ground of one.tdt at tile 2, 1");

        Assert.NotNull(painted);
        Assert.Equal(["Art/sand2.mat"], painted!.ShapeMaterials);

        // The engine's coordinates: where the vertex lies, times the ground scale.
        Assert.Equal(new Vector2(500f, 250f) * GroundPainter.GroundScale, painted.Mesh.Coordinates[0]);
        Assert.Equal(new Vector2(750f, 500f) * GroundPainter.GroundScale, painted.Mesh.Coordinates[4]);
        Assert.Contains("1 pieces of one ground, 0 of two", painter.Said(), StringComparison.Ordinal);
    }

    [Fact]
    public void ATILEOfTwoGroundsIsSplitByItsMasksGreenTheListsFirstAtNought()
    {
        GroundPainter painter = Painter();

        MonsterModel? painted = painter.Paint("two.tdt", Ground(), Matrix4x4.Identity, "ground of two.tdt");

        // Rock is listed before Sand, so it wears green nought - the first triangle; Sand the second.
        Assert.NotNull(painted);
        Assert.Equal(["Art/rock.mat", "Art/sand2.mat"], painted!.ShapeMaterials);
        Assert.Equal([0, 1, 2], painted.Mesh.Indices[..3]);
        Assert.Equal([3, 4, 5], painted.Mesh.Indices[3..6]);
        Assert.Contains("0 pieces of one ground, 1 of two", painter.Said(), StringComparison.Ordinal);
    }

    [Fact]
    public void ATILEOfThreeGroundsStaysPlainAndIsSaid()
    {
        GroundPainter painter = Painter();

        Assert.Null(painter.Paint("three.tdt", Ground(), Matrix4x4.Identity, "ground of three.tdt"));
        Assert.Null(painter.Paint("three.tdt", Ground(), Matrix4x4.Identity, "ground of three.tdt"));
        Assert.Contains("plain: 2 of three or four grounds", painter.Said(), StringComparison.Ordinal);
    }

    [Fact]
    public void NOMASTERIsSaidRatherThanPainted()
    {
        (GroundPainter? painter, string why) = GroundPainter.For(Install, string.Empty, new MonsterModels.Paints());

        Assert.Null(painter);
        Assert.Equal("no master to name a MaterialsList", why);
    }

    private static GroundPainter Painter()
    {
        (GroundPainter? painter, string why) = GroundPainter.For(Install, Master, new MonsterModels.Paints());
        Assert.True(painter is not null, why);
        Assert.Equal("Metadata/Terrain/Maps/Test/materials.mtd", painter!.From);
        return painter;
    }

    /// <summary>
    /// A ground of two triangles a tile across, apart: the first's coordinates point at the mask's green nought, the second's at its green whole.
    /// </summary>
    private static SkinnedMesh Ground()
    {
        Vector3[] positions =
        [
            new(0f, 0f, 0f), new(250f, 0f, 0f), new(0f, 250f, 0f),
            new(250f, 0f, 0f), new(250f, 250f, 0f), new(0f, 250f, 0f),
        ];
        Vector2[] coordinates = [new(0.25f, 0.25f), new(0.25f, 0.25f), new(0.25f, 0.25f), new(0.75f, 0.75f), new(0.75f, 0.75f), new(0.75f, 0.75f)];
        return SkinnedMesh.Of(
            positions, [.. positions.Select(_ => -Vector3.UnitZ)], [0, 1, 2, 3, 4, 5], Vector3.Zero, new Vector3(250f, 250f, 0f), coordinates,
            [new MeshShape("0", 0, 6)]);
    }

    private static byte[]? Install(string path) => path switch
    {
        Master => CaptureGroundTests.Text("version 3\nMaterialsList\t\"materials.mtd\"\n"),
        "Metadata/Terrain/Maps/Test/materials.mtd" => CaptureGroundTests.Text(
            "version 5\n\"Rock\" 1 0 \"Art/rock.mat\"\n\"Sand\" 2 0\n\"Art/sand1.mat\"\n\"Art/sand2.mat\"\n30 70 16\n\"Moss\" 1 0 \"Art/moss.mat\"\n"),
        "one.tdt" => CaptureGroundTests.Tdt(Template, [Sand, Sand, Sand, Sand]),
        "two.tdt" => CaptureGroundTests.Tdt(Template, [Sand, Sand, Rock, Rock]),
        "three.tdt" => CaptureGroundTests.Tdt(Template, [Sand, Rock, Moss, Moss]),
        Template => CaptureGroundTests.Text("version 3\nSize 1 1\nTileMeshRoot \"Art/Models/Terrain/Test/Ground\"\nGroundMask \"" + Mask + "\"\n"),

        // Green nought at the top left texel, whole at the bottom right.
        Mask => GameArtTests.Dds(2, 2, [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 0, 255, 0, 255, 0, 255]),
        Sand => CaptureGroundTests.Text("Sand\n1 1 0 0\n"),
        Rock => CaptureGroundTests.Text("Rock\n1 1 0 0\n"),
        Moss => CaptureGroundTests.Text("Moss\n1 1 0 0\n"),
        _ => null,
    };
}
