using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// The pane's roof fade (RoofFade): a model's roof sections by tile piece, each piece's footprint its ground's, and the sections of the
/// piece under a place left out whole (MaterialBlend.Hidden) - and the roof sections themselves read from each material's .mat where the
/// shapes are compiled (MonsterModels.Shaded).
/// </summary>
public class RoofFadeTests
{
    private const string A = "Metadata/Terrain/Woods/A.tdt at tile 0, 0";
    private const string B = "Metadata/Terrain/Woods/B.tdt at tile 2, 0";
    private const string C = "Metadata/Terrain/Woods/C.tdt at tile 0, 2";

    /// <summary>
    /// Every roof section of the piece a place stands on is hidden whole, its shadow with it, and nothing else: a piece's footprint is its ground's
    /// box, its own shapes' where it lays no ground; a place on two pieces' shared edge hides both; an object's own roof section is no
    /// piece's and stays drawn; a place on no piece hides nothing and hands the blends back as they were.
    /// </summary>
    [Fact]
    public void THEPIECEUnderAPlaceHidesItsRoofSectionsAndNothingElse()
    {
        MonsterModel model = Stage();
        RoofFade roofs = RoofFade.Of(model)!;
        Assert.Equal(3, roofs.Pieces);
        Assert.Equal(3, roofs.Sections);
        Assert.Equal(1, roofs.Loose);

        Assert.Equal([0], roofs.Holding(new Vector3(150f, 150f, 0f)));
        Assert.Equal([0, 1], roofs.Holding(new Vector3(500f, 250f, 0f)));
        Assert.Equal([2], roofs.Holding(new Vector3(50f, 650f, 0f)));
        Assert.Empty(roofs.Holding(new Vector3(2000f, 2000f, 0f)));
        Assert.True(roofs.HeldBy(new Vector3(500f, 250f, 0f), [0, 1]));
        Assert.False(roofs.HeldBy(new Vector3(500f, 250f, 0f), [0]));
        Assert.False(roofs.HeldBy(new Vector3(150f, 150f, 0f), []));
        Assert.True(roofs.HeldBy(new Vector3(2000f, 2000f, 0f), []));

        MaterialBlend[] own = [.. Enumerable.Repeat(MaterialBlend.Opaque, model.Mesh.Shapes.Count)];
        IReadOnlyList<MaterialBlend> faded = roofs.Faded(own, model.Mesh.Shapes.Count, [1])!;
        Assert.Equal(
            [MaterialBlend.Opaque, MaterialBlend.Opaque, MaterialBlend.Opaque, MaterialBlend.Hidden, MaterialBlend.Opaque, MaterialBlend.Opaque, MaterialBlend.Opaque],
            faded);
        Assert.Equal(MaterialBlend.Hidden, roofs.Faded(null, model.Mesh.Shapes.Count, [0])![1]);
        Assert.Same(own, roofs.Faded(own, model.Mesh.Shapes.Count, []));
        Assert.Null(roofs.Faded(null, model.Mesh.Shapes.Count, []));

        string said = roofs.Said([0]);
        Assert.StartsWith("roof fade: 1 roof sections of A at tile 0, 0 hidden, the piece under the focus; 1 roof sections of objects, not tiles, drawn - CHOSEN", said, StringComparison.Ordinal);
        Assert.StartsWith("roof fade: the focus stands on none of the 3 tile pieces carrying roof sections - none hidden", roofs.Said([]), StringComparison.Ordinal);
        Assert.Null(RoofFade.Of(model with { RoofSections = [] }));
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
