using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The pane's roof fade (RoofFade): a model's roof sections of tile pieces, each one hidden whole (MaterialBlend.Hidden) where its box across
/// the ground reaches into the region between the camera and a place - and the roof sections themselves read from each material's .mat
/// where the shapes are compiled (MonsterModels.Shaded); and the region's extents the game's verdicts allow (CaptureRoofFade.Extents).
/// </summary>
public class RoofFadeTests
{
    private const string A = "Metadata/Terrain/Woods/A.tdt at tile 0, 0";
    private const string B = "Metadata/Terrain/Woods/B.tdt at tile 2, 0";
    private const string C = "Metadata/Terrain/Woods/C.tdt at tile 0, 2";

    /// <summary>
    /// A section is hidden where its box reaches into the region round the place - towards the camera, not behind it, not far aside - whatever
    /// piece it belongs to; turning the camera turns the region with it; an object's own roof section stays drawn; a place with nothing
    /// round it hides nothing and hands the blends back as they were.
    /// </summary>
    [Fact]
    public void THEREGIONBetweenTheCameraAndThePlaceHidesWhatReachesIntoIt()
    {
        MonsterModel model = Stage();
        RoofFade roofs = RoofFade.Of(model)!;
        Assert.Equal(3, roofs.Sections);
        Assert.Equal(1, roofs.Loose);

        // LOOKING ALONG +y (a turn of half a lap): A's tree 300 to 400 towards the camera and 50 either side is hidden; B's, 450 aside, and C's,
        // 100 to 200 behind, are not.
        Vector2 alongY = RoofFade.AheadOf(MathF.PI);
        Assert.Equal(0f, alongY.X, 5);
        Assert.Equal(1f, alongY.Y, 5);
        var hidden = new bool[roofs.Sections];
        Assert.Equal(1, roofs.Hide(new Vector3(150f, 500f, 0f), alongY, hidden));
        Assert.Equal([true, false, false], hidden);

        // TURNED ROUND: C's tree now stands between the camera and the place, A's behind it.
        Assert.Equal(1, roofs.Hide(new Vector3(150f, 500f, 0f), RoofFade.AheadOf(0f), hidden));
        Assert.Equal([false, false, true], hidden);

        // THE GAME'S OWN WAY, (0.707, 0.707), is a turn of three eighths of a lap.
        Vector2 game = RoofFade.AheadOf(CameraPose.GameTurn);
        Assert.Equal(MathF.Sqrt(0.5f), game.X, 4);
        Assert.Equal(MathF.Sqrt(0.5f), game.Y, 4);

        MaterialBlend[] own = [.. Enumerable.Repeat(MaterialBlend.Opaque, model.Mesh.Shapes.Count)];
        IReadOnlyList<MaterialBlend> faded = roofs.Faded(own, model.Mesh.Shapes.Count, [true, false, false])!;
        Assert.Equal(
            [MaterialBlend.Opaque, MaterialBlend.Hidden, MaterialBlend.Opaque, MaterialBlend.Opaque, MaterialBlend.Opaque, MaterialBlend.Opaque, MaterialBlend.Opaque],
            faded);
        Assert.Equal(MaterialBlend.Hidden, roofs.Faded(null, model.Mesh.Shapes.Count, [false, false, true])![6]);
        Assert.Same(own, roofs.Faded(own, model.Mesh.Shapes.Count, [false, false, false]));
        Assert.Null(roofs.Faded(null, model.Mesh.Shapes.Count, [false, false, false]));

        Assert.Equal(0, roofs.Hide(new Vector3(2000f, 2000f, 0f), alongY, hidden));
        Assert.StartsWith("roof fade: none of the 3 roof sections of tile pieces reaches into the region round the focus - none hidden; 1 roof sections of objects, not tiles, drawn - a section is hidden where", roofs.Said(hidden), StringComparison.Ordinal);
        Assert.StartsWith("roof fade: 1 of the 3 roof sections of tile pieces hidden round the focus (A at tile 0, 0); 1 roof sections of objects", roofs.Said([true, false, false]), StringComparison.Ordinal);
        Assert.Null(RoofFade.Of(model with { RoofSections = [] }));
    }

    /// <summary>The test is the spreads' meeting, edges in: a box touching the region's far edge towards the camera or its side reaches it; one past them does not.</summary>
    [Fact]
    public void ABOXReachesTheRegionWhereItsSpreadsMeetItsEdgesIncluded()
    {
        Assert.True(RoofFade.Reaches((-RoofFade.Toward - 50f, -RoofFade.Toward), (-10f, 10f)));
        Assert.False(RoofFade.Reaches((-RoofFade.Toward - 50f, -RoofFade.Toward - 1f), (-10f, 10f)));
        Assert.True(RoofFade.Reaches((-100f, -50f), (RoofFade.Aside, RoofFade.Aside + 80f)));
        Assert.False(RoofFade.Reaches((-100f, -50f), (RoofFade.Aside + 1f, RoofFade.Aside + 80f)));
        Assert.True(RoofFade.Reaches((RoofFade.Behind, RoofFade.Behind + 40f), (-10f, 10f)));
        Assert.False(RoofFade.Reaches((RoofFade.Behind + 1f, RoofFade.Behind + 40f), (-10f, 10f)));

        // A BOX'S SPREAD along a way: its four corners laid on it.
        (float least, float most) = RoofFade.Spread(Vector2.Normalize(Vector2.One), new Vector2(0f, 0f), new Vector2(100f, 50f), new Vector2(10f, 10f));
        Assert.Equal(-20f * MathF.Sqrt(0.5f), least, 3);
        Assert.Equal(130f * MathF.Sqrt(0.5f), most, 3);
    }

    /// <summary>
    /// The extents three captures of The Red Vale allow, from their verdicts on curve5's tree and its neighbours (09:12, 13:36 and 13:46,
    /// placed along the game camera's ways from the player as their roof-fade.txt places them): at least 467 towards the camera, 134 to under
    /// 372 aside, -126 to under 47 behind - the pane's CHOSEN 600, 250 and nought within; a shown section with two sides open sets nothing.
    /// </summary>
    [Fact]
    public void THEVERDICTSOfThreeCapturesBoundTheRegion()
    {
        CaptureRoofFade.Placed[] placed =
        [
            new("0912 1581", 1581, true, (-596f, -467f), (-260f, -131f)),
            new("0912 1578", 1578, true, (-770f, -329f), (-419f, 22f)),
            new("1346 1581", 1581, true, (-588f, -460f), (-263f, -134f)),
            new("1346 1578", 1578, true, (-762f, -322f), (-422f, 18f)),
            new("1336 1578", 1578, true, (-126f, 314f), (-149f, 292f)),
            new("1336 1581", 1581, false, (47f, 176f), (10f, 139f)),
            new("1336 1372", 1372, false, (-516f, -388f), (-501f, -372f)),
            new("1336 2289", 2289, false, (-207f, 14f), (-1328f, -1107f)),
            new("1346 2367", 2367, false, (135f, 301f), (-1105f, -939f)),
        ];
        string text = CaptureRoofFade.Extents(placed).ReplaceLineEndings("\n");
        Assert.Contains("  towards the camera: at least 467 (0912 1581), no most - the pane's 600 within\n", text, StringComparison.Ordinal);
        Assert.Contains("  aside: at least 134 (1346 1581), under 372 (1336 1372) - the pane's 250 within\n", text, StringComparison.Ordinal);
        Assert.Contains("  behind: at least -126 (1336 1578), under 47 (1336 1581) - the pane's 0 within\n", text, StringComparison.Ordinal);
        Assert.Contains("  over 5 hidden and 4 shown; 1 shown with two sides or more open set nothing: 1346 2367\n", text, StringComparison.Ordinal);

        // A SHOWN SECTION THE HIDDEN ONES SAY MUST REACH THE REGION: the verdicts fit no region of this shape.
        string clash = CaptureRoofFade.Extents([.. placed, new("clash", 1, false, (-500f, -400f), (-20f, 20f))]);
        Assert.Contains("THE VERDICTS DO NOT FIT A REGION OF THIS SHAPE", clash, StringComparison.Ordinal);
        Assert.StartsWith(
            "=== the region's extents the verdicts allow",
            CaptureRoofFade.Extents([]),
            StringComparison.Ordinal);
    }

    /// <summary>A shape is a roof section where its material's .mat says "roofsection": true - read where the shapes are compiled.</summary>
    [Fact]
    public void ASHAPESRoofSectionIsReadFromItsMaterialsFile()
    {
        MonsterModel model = Stage() with
        {
            RoofSections = [],
            ShapeMaterials = ["Art/ground.mat", "Art/tree.mat", "Art/ground.mat", "Art/tree.mat:1", "Art/rock.mat", "Art/tree.mat", "Art/tree.mat"],
        };
        static byte[]? Read(string path) => path switch
        {
            "Art/tree.mat" => Encoding.UTF8.GetBytes("""{"version":4,"graphinstances":[{"parent":"Metadata/Materials/DielectricSpecGlossBN.fxgraph"}],"roofsection":true}"""),
            "Art/ground.mat" or "Art/rock.mat" => Encoding.UTF8.GetBytes("""{"version":4,"graphinstances":[{"parent":"Metadata/Materials/DielectricSpecGlossBN.fxgraph"}]}"""),
            _ => null,
        };

        MonsterModel shaded = MonsterModels.Shaded(Read, model, new MonsterModels.Paints());
        Assert.Equal([false, true, false, true, false, true, true], shaded.RoofSections);
        Assert.Empty(MonsterModels.Shaded(_ => null, model, new MonsterModels.Paints()).RoofSections);
    }

    /// <summary>
    /// A stage of three tile pieces and an object: A's ground over (0, 0) to (500, 500) and a tree on it; B's ground beside it and a tree
    /// and a rock on it; an object's own tree; C's tree with no ground of its piece. The trees are roof sections.
    /// </summary>
    private static MonsterModel Stage()
    {
        var places = new List<Vector3>();
        var indices = new List<int>();
        var shapes = new List<MeshShape>();
        void Quad(string name, float x0, float y0, float x1, float y1, float z)
        {
            int at = places.Count;
            places.AddRange([new(x0, y0, z), new(x1, y0, z), new(x1, y1, z), new(x0, y1, z)]);
            shapes.Add(new MeshShape(name, indices.Count, 6));
            indices.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
        }

        Quad("a ground", 0f, 0f, 500f, 500f, 0f);
        Quad("a tree", 100f, 100f, 200f, 200f, -300f);
        Quad("b ground", 500f, 0f, 1000f, 500f, 0f);
        Quad("b tree", 600f, 100f, 700f, 200f, -300f);
        Quad("b rock", 700f, 100f, 800f, 200f, -50f);
        Quad("object tree", 300f, 300f, 400f, 400f, -300f);
        Quad("c tree", 0f, 600f, 100f, 700f, -300f);
        SkinnedMesh mesh = SkinnedMesh.Of(
            [.. places], [.. places.Select(_ => -Vector3.UnitZ)], [.. indices], new Vector3(0f, 0f, -300f), new Vector3(1000f, 700f, 0f),
            [.. places.Select(_ => Vector2.Zero)], shapes);
        return new MonsterModel(mesh, null, "stage", string.Empty, string.Empty)
        {
            ShapeSources = ["ground of " + A, A, "ground of " + B, B, B, "Metadata/Terrain/Doodads/tree.ao", C],
            RoofSections = [false, true, false, true, false, true, true],
        };
    }
}
