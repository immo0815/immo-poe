using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// A tileset's TileMaterialOverrides on a tile's own doodads, laid in a room - see MonsterModels.Swapped.
/// </summary>
public sealed class TileSwapTests
{
    private const string Corruption = "Art/Textures/Environment/Corruption/CorruptionGradientc.mat";
    private const string Hidden = "Art/Textures/masks/transparentobjectsc.mat";
    private const string Rock = "Art/Textures/Rock.mat";

    /// <summary>
    /// A swapped shape wears the material the area draws, with that material's blend; a shape the swaps do not name, and a selector on
    /// it, keep what they wore - and a model none of whose materials is swapped comes back as it was.
    /// </summary>
    /// <remarks>
    /// Crimson Shores' master swaps <c>CorruptionGradientc.mat</c>, the tendrils its stromatolite tiles bring in their <c>.tmd</c>, for
    /// the mask nothing draws; the Monster Book's stage drew the tendrils. A swap as a tile's mesh takes it: the whole material.
    /// </remarks>
    [Fact]
    public void ATILESDoodadWearsTheTilesetsSwapAndTheRestAsItWas()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            [Hidden] = Encoding.UTF8.GetBytes("""{"version":4,"defaultgraph":{"version":3,"overriden_blend_mode":"AlphaTest"}}"""),
        };
        Vector3[] positions = [new(0f, 0f, 0f), new(1f, 0f, 0f), new(0f, 1f, 0f), new(2f, 0f, 0f), new(3f, 0f, 0f), new(2f, 1f, 0f)];
        SkinnedMesh mesh = SkinnedMesh.Of(
            positions, [.. positions.Select(_ => -Vector3.UnitZ)], [0, 1, 2, 3, 4, 5], Vector3.Zero, new Vector3(3f, 1f, 0f), null,
            [new MeshShape("0", 0, 3), new MeshShape("1", 3, 3)]);
        var model = new MonsterModel(mesh, null, "tendrils.ao", string.Empty, string.Empty)
        {
            ShapeMaterials = [Corruption + ":0", Rock + ":1"],
            Modes = ["", "Opaque"],
            ShapeTextures = ["Art/Textures/Corruption.dds", "Art/Textures/Rock.dds"],
        };
        var swaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { [Corruption] = Hidden };

        MonsterModel swapped = MonsterModels.Swapped(files.GetValueOrDefault, model, swaps, new MonsterModels.Paints());

        Assert.Equal([Hidden, Rock + ":1"], swapped.ShapeMaterials);
        Assert.Equal(["AlphaTest", "Opaque"], swapped.Modes);
        Assert.Equal([string.Empty, "Art/Textures/Rock.dds"], swapped.ShapeTextures);
        Assert.Same(model, MonsterModels.Swapped(files.GetValueOrDefault, model, new Dictionary<string, string> { [Rock + "x"] = Hidden }, new MonsterModels.Paints()));
    }
}
