using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The ground's own scatter: a layer's points as a pattern every two tiles of the area's coordinates, a layer without points its number
/// a tile, each at its material's share of the group's weights and its ground's share of the corners; the master's doodad list one a
/// tile from the group its ground names; kept off ground nobody walks where a layer asks; the same every time.
/// </summary>
public sealed class GroundScatterTests
{
    private const string Master = "Metadata/Terrain/Maps/Test/master.tsi";
    private const string Template = "Art/Models/Terrain/Test/Ground.tgt";
    private const string Sand = "Metadata/Terrain/Test/sand.gt";
    private const string Rock = "Metadata/Terrain/Test/rock.gt";
    private const string Sword = "Art/Models/Test/sword.fmt";
    private const string Grass = "Art/Models/Test/grass.fmt";
    private const string Moss = "Art/Models/Test/moss.fmt";
    private const string Rust = "Metadata/Terrain/Doodads/Test/rust.ao";
    private const string Shell = "Metadata/Terrain/Doodads/Test/shell.ao";
    private const float Ground = -7f;

    /// <summary>A layer's points lie every 500 units of the AREA's coordinates: moving the room's frame in the area moves the pattern under it.</summary>
    [Fact]
    public void ALAYERSPointsAreAPatternEveryTwoTilesOfTheAreasCoordinates()
    {
        // The cell lies at 500..750, 250..500 of the room; of the pattern's points only (0.25, 0.75) falls in it with the room at the
        // area's origin - and only (0.75, 0.75) with the room 250 units further along the area.
        GroundScatter scatter = Scatter();
        scatter.Strew("unnamed.tdt", Flat(), [], 1, 1, Matrix4x4.CreateTranslation(500f, 250f, 0f), Vector2.Zero, blocked: null);
        (RoomDoodad one, Matrix4x4 place) = Assert.Single(scatter.Lines, line => line.One.Ao == Sword);
        Assert.Equal(new Vector3(625f, 375f, Ground), place.Translation);
        Assert.Equal((1f, 0f), (one.Scale, one.Turn));

        GroundScatter moved = Scatter();
        moved.Strew("unnamed.tdt", Flat(), [], 1, 1, Matrix4x4.CreateTranslation(500f, 250f, 0f), new Vector2(250f, 0f), blocked: null);
        Assert.Equal(new Vector3(625f, 375f, Ground), Assert.Single(moved.Lines, line => line.One.Ao == Sword).Place.Translation);
        Assert.Contains("1 of Swords", scatter.Said(), StringComparison.Ordinal);
    }

    /// <summary>A layer without points strews its number a tile - every one where its material is the group's only - and the same every time.</summary>
    [Fact]
    public void ALAYERWithoutPointsStrewsItsNumberATileTheSameEveryTime()
    {
        GroundScatter one = Scatter(), again = Scatter();
        for (var tile = 0; tile < 50; tile++)
        {
            Matrix4x4 place = Matrix4x4.CreateTranslation(tile * 250f, 1000f, 0f);
            one.Strew("rock.tdt", Flat(), [], 1, 1, place, Vector2.Zero, blocked: null);
            again.Strew("rock.tdt", Flat(), [], 1, 1, place, Vector2.Zero, blocked: null);
        }

        (RoomDoodad One, Matrix4x4 Place)[] moss = [.. one.Lines.Where(line => line.One.Ao == Moss)];
        Assert.Equal(50 * 4, moss.Length);
        Assert.All(moss, line => Assert.InRange(line.Place.Translation.Y, 1000f, 1250f));
        Assert.All(moss, line => Assert.Equal(Ground, line.Place.Translation.Z, 3));
        Assert.Equal(one.Lines.Select(line => line.Place), again.Lines.Select(line => line.Place));
    }

    /// <summary>A layer riding a material of a quarter of its group's weight strews a quarter of its number, near enough over many tiles.</summary>
    [Fact]
    public void ALAYERIsStrewnAtItsMaterialsShareOfTheGroupsWeights()
    {
        GroundScatter scatter = Scatter();
        for (var tile = 0; tile < 400; tile++)
        {
            scatter.Strew("sand.tdt", Flat(), [], 1, 1, Matrix4x4.CreateTranslation((tile % 20) * 250f, (tile / 20) * 250f, 0f), Vector2.Zero, blocked: null);
        }

        // 400 tiles of 4 candidates kept at a quarter: 400 expected, the spread about 17.
        Assert.InRange(scatter.Lines.Count(line => line.One.Ao == Grass), 340, 460);
    }

    /// <summary>
    /// A sub-tile of two grounds strews a ground's layer by that ground's share of its corners, bilinear, and shrinks it towards the layer's
    /// MinEdgeScale where the ground gives way.
    /// </summary>
    [Fact]
    public void ASUBTileOfTwoGroundsStrewsEachByItsShareOfTheCorners()
    {
        // The right sub-tile is sand on its left side and rock on its right; only rock strews moss, at a whole share, its edge half size.
        GroundScatter scatter = Scatter();
        for (var tile = 0; tile < 100; tile++)
        {
            scatter.Strew("gridded.tdt", TwoSubTiles(), [new GroundPiece(1, 1, 2), new GroundPiece(2, 1, 2)], 2, 1,
                Matrix4x4.CreateTranslation(tile * 500f, 0f, 0f), Vector2.Zero, blocked: null);
        }

        Assert.DoesNotContain(scatter.Lines, line => line.One.Ao == Moss && line.Place.Translation.X % 500f < 250f);
        (RoomDoodad One, Matrix4x4 Place)[] rock = [.. scatter.Lines.Where(line => line.One.Ao == Moss)];
        Assert.NotEmpty(rock);
        Assert.All(rock, line =>
        {
            float s = ((line.Place.Translation.X % 500f) - 250f) / 250f;
            Assert.Equal(float.Lerp(0.5f, 1f, s), line.One.Scale, 3);
        });

        // Weighed by s, the kept places lean to the rock side: their mean s near two thirds, not a half.
        float mean = rock.Average(line => ((line.Place.Translation.X % 500f) - 250f) / 250f);
        Assert.InRange(mean, 0.6f, 0.73f);
    }

    /// <summary>A layer not AllowOnBlocking strews nothing where nobody walks, one that is strews on; and the line under the picture says so.</summary>
    [Fact]
    public void ALAYERKeptOffBlockingGroundStrewsNothingWhereNobodyWalks()
    {
        GroundScatter scatter = Scatter();
        scatter.Strew("rock.tdt", Flat(), [], 1, 1, Matrix4x4.Identity, Vector2.Zero, blocked: _ => true);
        scatter.Strew("unnamed.tdt", Flat(), [], 1, 1, Matrix4x4.CreateTranslation(500f, 250f, 0f), Vector2.Zero, blocked: _ => true);

        Assert.DoesNotContain(scatter.Lines, line => line.One.Ao == Moss);
        Assert.Single(scatter.Lines, line => line.One.Ao == Sword);
        Assert.Contains("kept off ground nobody walks", scatter.Said(), StringComparison.Ordinal);
        Assert.DoesNotContain("walkable grid not known", scatter.Said(), StringComparison.Ordinal);
    }

    /// <summary>The master's doodad list strews one a tile, from the group named as the ground's group is, else "Default".</summary>
    [Fact]
    public void THEDOODADListStrewsOneATileFromItsGroundsGroupElseDefault()
    {
        GroundScatter scatter = Scatter();
        scatter.Strew("sand.tdt", Flat(), [], 1, 1, Matrix4x4.Identity, Vector2.Zero, blocked: null);
        scatter.Strew("rock.tdt", Flat(), [], 1, 1, Matrix4x4.CreateTranslation(250f, 0f, 0f), Vector2.Zero, blocked: null);

        (RoomDoodad One, Matrix4x4 Place) shell = Assert.Single(scatter.Lines, line => line.One.Ao == Shell);
        (RoomDoodad One, Matrix4x4 Place) rust = Assert.Single(scatter.Lines, line => line.One.Ao == Rust);
        Assert.InRange(shell.Place.Translation.X, 0f, 250f);
        Assert.InRange(rust.Place.Translation.X, 250f, 500f);
        Assert.Equal(GroundScatter.Stub, rust.One.Stub);
        Assert.Contains("2 from doodads.ddt", scatter.Said(), StringComparison.Ordinal);
    }

    private static GroundScatter Scatter()
    {
        (GroundPainter? painter, string why) = GroundPainter.For(Install, Master, new MonsterModels.Paints());
        Assert.True(painter is not null, why);
        return GroundScatter.For(Install, Master, painter!);
    }

    /// <summary>A tile of ground a tile across, flat at <see cref="Ground"/>.</summary>
    private static SkinnedMesh Flat()
    {
        Vector3[] positions = [new(0f, 0f, Ground), new(250f, 0f, Ground), new(0f, 250f, Ground), new(250f, 250f, Ground)];
        return SkinnedMesh.Of(
            positions, [.. positions.Select(_ => -Vector3.UnitZ)], [0, 1, 2, 1, 3, 2], new Vector3(0f, 0f, Ground), new Vector3(250f, 250f, Ground),
            [.. positions.Select(_ => Vector2.Zero)], [new MeshShape("0", 0, 6)]);
    }

    /// <summary>Two sub-tiles side by side, each a square of two triangles, flat at <see cref="Ground"/>.</summary>
    private static SkinnedMesh TwoSubTiles()
    {
        Vector3[] positions =
        [
            new(0f, 0f, Ground), new(250f, 0f, Ground), new(0f, 250f, Ground), new(250f, 0f, Ground), new(250f, 250f, Ground), new(0f, 250f, Ground),
            new(250f, 0f, Ground), new(500f, 0f, Ground), new(250f, 250f, Ground), new(500f, 0f, Ground), new(500f, 250f, Ground), new(250f, 250f, Ground),
        ];
        return SkinnedMesh.Of(
            positions, [.. positions.Select(_ => -Vector3.UnitZ)], [.. Enumerable.Range(0, 12)], new Vector3(0f, 0f, Ground), new Vector3(500f, 250f, Ground),
            [.. positions.Select(_ => Vector2.Zero)], [new MeshShape("0", 0, 6), new MeshShape("1", 6, 6)]);
    }

    private static byte[]? Install(string path) => path switch
    {
        Master => CaptureGroundTests.Text("version 3\nMaterialsList\t\"materials.mtd\"\nDoodads\t\"doodads.ddt\"\n"),

        // The unnamed group strews the swords at a whole share; sand the grass at a quarter, beside a bare material; rock the moss whole.
        "Metadata/Terrain/Maps/Test/materials.mtd" => CaptureGroundTests.Text(
            "version 5\n1 0 \"Art/swords.mat\" \"Metadata/Doodads/Swords.dlp\"\n"
            + "\"Sand\" 2 0\n\"Art/sand.mat\" \"Metadata/Doodads/Grass.dlp\"\n\"Art/bare.mat\"\n1 3 16\n"
            + "\"Rock\" 1 0 \"Art/rock.mat\" \"Metadata/Doodads/Moss.dlp\"\n"),
        "Metadata/Doodads/Swords.dlp" => CaptureGroundTests.Text(
            "version 3\n- RandomScale 1 1\n- AllowOnBlocking\n- MaxRotation 0\n\"" + Sword + "\" 0.75 (0.25,0.25) (0.75,0.75) (0.25,0.75)\n"),
        "Metadata/Doodads/Grass.dlp" => CaptureGroundTests.Text("version 3\n- RandomScale 1 1\n- MaxRotation 360\n\"" + Grass + "\" 4\n"),
        "Metadata/Doodads/Moss.dlp" => CaptureGroundTests.Text(
            "version 3\n- RandomScale 1 1\n- MaxRotation 360\n- MinEdgeScale 0.5\n\"" + Moss + "\" 4\n"),
        "Metadata/Terrain/Maps/Test/doodads.ddt" => CaptureGroundTests.Text(
            "version 3\n1\nDefault\n5 \"" + Rust + "\"\n\n\"Sand\"\n1 \"" + Shell + "\"\n"),
        "unnamed.tdt" => CaptureGroundTests.Tdt(Template, ["", "", "", ""]),
        "sand.tdt" => CaptureGroundTests.Tdt(Template, [Sand, Sand, Sand, Sand]),
        "rock.tdt" => CaptureGroundTests.Tdt(Template, [Rock, Rock, Rock, Rock]),

        // Two sub-tiles across: the left one's four corners sand, the right one's sand on its left side and rock on its right.
        "gridded.tdt" => TileFilesTests.Tdt(template: Template, grounds: true, grid: [5, 5, 6, 5, 5, 6]),
        Template => CaptureGroundTests.Text("version 3\nSize 1 1\nTileMeshRoot \"Art/Models/Terrain/Test/Ground\"\n"),
        Sand => CaptureGroundTests.Text("Sand\n1 1 0 0\n"),
        Rock => CaptureGroundTests.Text("Rock\n1 1 0 0\n"),
        _ => null,
    };
}
