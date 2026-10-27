using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The ground painted from the area's MaterialsList: one ground all round in its material, two mixed per pixel by the template
/// mask's green at the block's own coordinates - the MaterialsList's first ground at green nought - or split by triangle where
/// they cannot be mixed, and three left plain.
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
        Assert.Contains("1 sub-tiles of one ground, 0 of two", painter.Said(), StringComparison.Ordinal);
        Assert.Contains("(0 pieces; 1 from the tile's four corners, their definition carrying no grid; 0 sub-tiles", painter.Said(), StringComparison.Ordinal);
    }

    [Fact]
    public void ATILEOfTwoGroundsIsMixedPerPixelTheListsFirstAtNought()
    {
        GroundPainter painter = Painter(Mixable);
        Matrix4x4 place = Matrix4x4.CreateTranslation(500f, 250f, 0f);

        MonsterModel? painted = painter.Paint("two.tdt", Ground(), place, "ground of two.tdt");

        // ONE SHAPE WEARING BOTH, Rock - listed before Sand - at green nought; the block keeping its own coordinates for the mask.
        Assert.NotNull(painted);
        Assert.Equal([GroundBlend.Name("Art/rock.mat", "Art/sand2.mat", Mask)], painted!.ShapeMaterials);
        Assert.Single(painted.Mesh.Shapes);
        Assert.Equal(Ground().Coordinates, painted.Mesh.Coordinates);
        Assert.Contains("0 sub-tiles of one ground, 1 of two mixed per pixel", painter.Said(), StringComparison.Ordinal);
        Assert.DoesNotContain("split by triangle", painter.Said(), StringComparison.Ordinal);
    }

    [Fact]
    public void ATILEOfTwoGroundsThatCannotBeMixedIsSplitByItsMasksGreenAndSaysWhy()
    {
        GroundPainter painter = Painter();

        MonsterModel? painted = painter.Paint("two.tdt", Ground(), Matrix4x4.Identity, "ground of two.tdt");

        // Rock is listed before Sand, so it wears green nought - the first triangle; Sand the second.
        Assert.NotNull(painted);
        Assert.Equal(["Art/rock.mat", "Art/sand2.mat"], painted!.ShapeMaterials);
        Assert.Equal([0, 1, 2], painted.Mesh.Indices[..3]);
        Assert.Equal([3, 4, 5], painted.Mesh.Indices[3..6]);
        Assert.Contains("0 sub-tiles of one ground, 0 of two mixed per pixel", painter.Said(), StringComparison.Ordinal);
        Assert.Contains("1 of two split by triangle where the green averages a half - mixing them could not be: 1 a ground whose graphs did not compile: Art/rock.mat", painter.Said(), StringComparison.Ordinal);
    }

    /// <summary>
    /// Each sub-tile takes its own corners from the definition's ground grid: one all sand is drawn in sand with the world's coordinates,
    /// one of sand and rock mixed by the mask with the block's own - though the tile's four corners name sand and rock both.
    /// </summary>
    [Fact]
    public void EACHSUBTileIsPaintedFromItsOwnCornersInTheGrid()
    {
        const string Gridded = "gridded.tdt", GridSand = "Metadata/Terrain/Test/sand.gt", GridRock = "Metadata/Terrain/Test/rock.gt";
        byte[]? Read(string path) => path switch
        {
            // Two sub-tiles across: the left one's four corners sand, the right one's two sand and two rock.
            Gridded => TileFilesTests.Tdt(template: Template, grounds: true, grid: [5, 5, 6, 5, 5, 6]),
            GridSand => CaptureGroundTests.Text("Sand\n1 1 0 0\n"),
            GridRock => CaptureGroundTests.Text("Rock\n1 1 0 0\n"),
            _ => Mixable(path),
        };

        GroundPainter painter = Painter(Read);
        SkinnedMesh ground = TwoSubTiles();
        Matrix4x4 place = Matrix4x4.CreateTranslation(500f, 250f, 0f);

        MonsterModel? painted = painter.Paint(Gridded, ground, [new GroundPiece(1, 1, 2), new GroundPiece(2, 1, 2)], place, "ground of gridded.tdt");

        Assert.NotNull(painted);
        Assert.Equal(["Art/sand2.mat", GroundBlend.Name("Art/rock.mat", "Art/sand2.mat", Mask)], painted!.ShapeMaterials);
        Assert.Equal(new Vector2(500f, 250f) * GroundPainter.GroundScale, painted.Mesh.Coordinates[0]);
        Assert.Equal(ground.Coordinates[6], painted.Mesh.Coordinates[6]);
        string said = painter.Said();
        Assert.Contains("(1 pieces; 0 from the tile's four corners", said, StringComparison.Ordinal);
        Assert.Contains("1 sub-tiles of one ground, 1 of two mixed per pixel", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// A shape of a two-ground sub-tile whose block coordinates reach a cell of the mask's atlas or more across is mixed by the second
    /// ground's share of its sub-tile's corners instead, on copies of its vertices - and kept on the mask's cells by the switch.
    /// </summary>
    [Fact]
    public void ASHAPEAcrossTheMasksAtlasIsMixedByItsSubTilesCorners()
    {
        const string Gridded = "gridded.tdt", GridSand = "Metadata/Terrain/Test/sand.gt", GridRock = "Metadata/Terrain/Test/rock.gt";
        byte[]? Read(string path) => path switch
        {
            // The left sub-tile's four corners sand; the right one's sand on its left side and rock on its right.
            Gridded => TileFilesTests.Tdt(template: Template, grounds: true, grid: [5, 5, 6, 5, 5, 6]),
            GridSand => CaptureGroundTests.Text("Sand\n1 1 0 0\n"),
            GridRock => CaptureGroundTests.Text("Rock\n1 1 0 0\n"),
            _ => Mixable(path),
        };

        // THE RIGHT SUB-TILE'S COORDINATES RUN 40 ACROSS the mask two texels wide: 80 texels, past a cell of the atlas.
        SkinnedMesh narrow = TwoSubTiles();
        Vector2[] coordinates = [.. narrow.Positions.Select((place, at) => at < 6 ? narrow.Coordinates[at] : new Vector2((place.X - 250f) / 250f * 40f, 0.25f))];
        SkinnedMesh ground = SkinnedMesh.Of(narrow.Positions, narrow.Normals, narrow.Indices, narrow.Least, narrow.Most, coordinates, narrow.Shapes);
        GroundPiece[] pieces = [new GroundPiece(1, 1, 2), new GroundPiece(2, 1, 2)];

        GroundPainter painter = Painter(Read);
        MonsterModel? painted = painter.Paint(Gridded, ground, pieces, Matrix4x4.CreateTranslation(500f, 250f, 0f), "ground of gridded.tdt");

        // Rock is listed first, so the share is sand's: whole at the sub-tile's left edge (x 250), nought at its right (x 500).
        Assert.NotNull(painted);
        Assert.Equal(["Art/sand2.mat", GroundBlend.Cornered("Art/rock.mat", "Art/sand2.mat")], painted!.ShapeMaterials);
        Assert.Equal(18, painted.Mesh.Positions.Length);
        Assert.Equal([12, 13, 14, 15, 16, 17], painted.Mesh.Indices[6..12]);
        for (var copy = 0; copy < 6; copy++)
        {
            Vector3 place = painted.Mesh.Positions[12 + copy];
            Assert.Equal(ground.Positions[6 + copy], place);
            Assert.Equal(1f - ((place.X - 250f) / 250f), painted.Mesh.Coordinates[12 + copy].X, 5);
        }

        Assert.Contains("of them 1 with shapes across the mask's atlas (1 shapes) mixed instead by the second ground's share of their sub-tile's corners", painter.Said(), StringComparison.Ordinal);

        // THE SWITCH keeps it on the mask's cells, as before: no copies, the block's coordinates.
        GroundPainter onCells = Painter(Read, maskCells: true);
        MonsterModel? kept = onCells.Paint(Gridded, ground, pieces, Matrix4x4.Identity, "ground of gridded.tdt");
        Assert.NotNull(kept);
        Assert.Equal(["Art/sand2.mat", GroundBlend.Name("Art/rock.mat", "Art/sand2.mat", Mask)], kept!.ShapeMaterials);
        Assert.Equal(12, kept.Mesh.Positions.Length);
        Assert.Equal(coordinates[6], kept.Mesh.Coordinates[6]);
        Assert.Contains("1 of them with shapes across the mask's atlas kept on its cells by the switch", onCells.Said(), StringComparison.Ordinal);
    }

    /// <summary>
    /// A corner the grid leaves unnamed takes the MaterialsList's group with no name - the game holds its null slot there: a sub-tile
    /// of sand and unnamed corners mixes the two, the unnamed group listed first, and one with no corner named wears the group alone.
    /// </summary>
    [Fact]
    public void ACORNERTheGridLeavesUnnamedTakesTheGroupWithNoName()
    {
        const string Gridded = "unnamed.tdt", GridSand = "Metadata/Terrain/Test/sand.gt";
        byte[]? Read(string path) => path switch
        {
            Gridded => TileFilesTests.Tdt(template: Template, grounds: true, grid: [5, -1, -1, 5, -1, -1]),
            GridSand => CaptureGroundTests.Text("Sand\n1 1 0 0\n"),
            _ => Mixable(path),
        };

        GroundPainter painter = Painter(Read);
        MonsterModel? painted = painter.Paint(Gridded, TwoSubTiles(), [new GroundPiece(1, 1, 2), new GroundPiece(2, 1, 2)], Matrix4x4.Identity, "ground of unnamed.tdt");

        Assert.NotNull(painted);
        Assert.Equal([GroundBlend.Name("Art/cobble.mat", "Art/sand2.mat", Mask), "Art/cobble.mat"], painted!.ShapeMaterials);
        string said = painter.Said();
        Assert.Contains("2 sub-tiles with a corner the grid leaves unnamed, taken as the MaterialsList's group with no name", said, StringComparison.Ordinal);
        Assert.Contains("1 sub-tiles of one ground, 1 of two mixed per pixel", said, StringComparison.Ordinal);
    }

    /// <summary>
    /// The sub-tiles' grounds as the painter works them out, for the capture's picture of the ground on the screen: each sub-tile's grid
    /// corners and the groups they come to in the list's order, and the template's mask with its picture.
    /// </summary>
    [Fact]
    public void EACHSUBTilesGroundsAreGivenAsThePainterWorksThemOut()
    {
        const string Gridded = "unnamed.tdt", GridSand = "Metadata/Terrain/Test/sand.gt";
        byte[]? Read(string path) => path switch
        {
            Gridded => TileFilesTests.Tdt(template: Template, grounds: true, grid: [5, -1, -1, 5, -1, -1]),
            GridSand => CaptureGroundTests.Text("Sand\n1 1 0 0\n"),
            _ => Mixable(path),
        };

        (IReadOnlyList<SubTileGrounds> subTiles, string mask, GamePicture? picture) =
            Painter(Read).SubTilesOf(Gridded, [new GroundPiece(1, 1, 2), new GroundPiece(2, 1, 2)], 4);

        Assert.Equal(2, subTiles.Count);
        Assert.Equal(["", "Sand"], subTiles[0].Grounds);
        Assert.Equal([GridSand, string.Empty, GridSand, string.Empty], subTiles[0].Corners);

        // The second ground's corners, in the corners' order: the sand ones, the unnamed being the first ground's group.
        Assert.Equal([true, false, true, false], subTiles[0].Seconds);
        Assert.Equal([""], subTiles[1].Grounds);
        Assert.Empty(subTiles[1].Seconds);
        Assert.Equal(Mask, mask);
        Assert.Equal(2, picture?.Width);
    }

    /// <summary>
    /// A tile of three grounds is drawn as the two most of its corners name, mixed as two are: Moss at two corners, then Rock over Sand
    /// at one each - Rock listed first - with Rock, listed before Moss, at green nought; and it is said, as a stand-in.
    /// </summary>
    [Fact]
    public void ATILEOfThreeGroundsIsDrawnAsTheTwoMostOfItsCornersName()
    {
        GroundPainter painter = Painter(Mixable);

        MonsterModel? painted = painter.Paint("three.tdt", Ground(), Matrix4x4.Identity, "ground of three.tdt");

        Assert.NotNull(painted);
        Assert.Equal([GroundBlend.Name("Art/rock.mat", "Art/moss.mat", Mask)], painted!.ShapeMaterials);
        string said = painter.Said();
        Assert.Contains("0 sub-tiles of one ground, 1 of two mixed per pixel", said, StringComparison.Ordinal);
        Assert.Contains(", 1 of three or four grounds drawn as the two most of their corners name, ties to the list's first - CHOSEN", said, StringComparison.Ordinal);
        Assert.Contains("plain: none", said, StringComparison.Ordinal);

        // THE CAPTURE'S VIEW keeps them the engine's own: three grounds, said why the tool cannot mix them as it does.
        (IReadOnlyList<SubTileGrounds> subTiles, _, _) = Painter(Mixable).SubTilesOf("three.tdt", [new GroundPiece(1, 1, 2)], 2);
        Assert.Equal("of three or four grounds (the engine's atlas, placed by transforms no file holds)", Assert.Single(subTiles).Why);
    }

    [Fact]
    public void NOMASTERIsSaidRatherThanPainted()
    {
        (GroundPainter? painter, string why) = GroundPainter.For(Install, string.Empty, new MonsterModels.Paints());

        Assert.Null(painter);
        Assert.Equal("no master to name a MaterialsList", why);
    }

    private static GroundPainter Painter(Func<string, byte[]?>? read = null, bool maskCells = false)
    {
        (GroundPainter? painter, string why) = GroundPainter.For(read ?? Install, Master, new MonsterModels.Paints(), maskCells);
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

    /// <summary>Two sub-tiles side by side, each a square of two triangles with its own vertices, the block's coordinates on every vertex.</summary>
    private static SkinnedMesh TwoSubTiles()
    {
        Vector3[] positions =
        [
            new(0f, 0f, 0f), new(250f, 0f, 0f), new(0f, 250f, 0f), new(250f, 0f, 0f), new(250f, 250f, 0f), new(0f, 250f, 0f),
            new(250f, 0f, 0f), new(500f, 0f, 0f), new(250f, 250f, 0f), new(500f, 0f, 0f), new(500f, 250f, 0f), new(250f, 250f, 0f),
        ];
        Vector2[] coordinates = [.. positions.Select((_, at) => new Vector2(0.1f + (at * 0.05f), 0.25f))];
        return SkinnedMesh.Of(
            positions, [.. positions.Select(_ => -Vector3.UnitZ)], [.. Enumerable.Range(0, 12)], Vector3.Zero, new Vector3(500f, 250f, 0f), coordinates,
            [new MeshShape("0", 0, 6), new MeshShape("1", 6, 6)]);
    }

    /// <summary>The install with the rock's and the sand's materials in it, each a constant colour - so the two grounds can be mixed.</summary>
    private static byte[]? Mixable(string path) => path switch
    {
        "Art/rock.mat" => CaptureGroundTests.Text("""{"version":4,"graphinstances":[{"parent":"Metadata/Grey.fxgraph"}]}"""),
        "Art/sand2.mat" => CaptureGroundTests.Text("""{"version":4,"graphinstances":[{"parent":"Metadata/Grey.fxgraph"}]}"""),
        "Art/cobble.mat" => CaptureGroundTests.Text("""{"version":4,"graphinstances":[{"parent":"Metadata/Grey.fxgraph"}]}"""),
        "Art/moss.mat" => CaptureGroundTests.Text("""{"version":4,"graphinstances":[{"parent":"Metadata/Grey.fxgraph"}]}"""),
        "Metadata/Grey.fxgraph" => CaptureGroundTests.Text(
            """
            {"nodes":[
              {"type":"ConstantPixel3","index":0,"parameters":[{"value":[0.5,0.5,0.5]}]},
              {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
             "links":[
              {"src":{"type":"ConstantPixel3","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}}]}
            """),
        _ => Install(path),
    };

    private static byte[]? Install(string path) => path switch
    {
        Master => CaptureGroundTests.Text("version 3\nMaterialsList\t\"materials.mtd\"\n"),
        "Metadata/Terrain/Maps/Test/materials.mtd" => CaptureGroundTests.Text(
            "version 5\n1 0 \"Art/cobble.mat\"\n\"Rock\" 1 0 \"Art/rock.mat\"\n\"Sand\" 2 0\n\"Art/sand1.mat\"\n\"Art/sand2.mat\"\n30 70 16\n\"Moss\" 1 0 \"Art/moss.mat\"\n"),
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
