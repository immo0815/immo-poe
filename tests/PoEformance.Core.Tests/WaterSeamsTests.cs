using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// Where water shapes meet (WaterSeams): two squares of water edge to edge, their shared edge carrying each other's corners, or one of
/// them carrying a corner in the middle of the other's edge - the T-junction a lifted sea cracks open at.
/// </summary>
public class WaterSeamsTests
{
    /// <summary>Corner to corner: the shared edge matched both ways, the rest the water's own border, no junction.</summary>
    [Fact]
    public void SQUARESCornerToCornerMeetWithoutAJunction()
    {
        WaterSeams seams = WaterSeams.Of(Squares(split: false), [true, true]);
        Assert.Equal(8, seams.Edges);
        Assert.Equal(2, seams.Matched);
        Assert.Equal(0, seams.Junctions);
        Assert.Equal(6, seams.Outer);
        Assert.Empty(seams.Samples);
    }

    /// <summary>
    /// The right square's left edge halved: its middle corner lies in the middle of the left square's right edge - one junction, one
    /// corner, at (10, 5); the right square's two halves each meet the left one at a corner and its middle.
    /// </summary>
    [Fact]
    public void ACORNERInTheMiddleOfAnEdgeIsAJunction()
    {
        WaterSeams seams = WaterSeams.Of(Squares(split: true), [true, true]);
        Assert.Equal(1, seams.Junctions);
        Assert.Equal(1, seams.Corners);
        Assert.Equal(new Vector3(10f, 5f, 0f), Assert.Single(seams.Samples));
    }

    /// <summary>One water shape alone has no seams to count, nor has a shape that is not water.</summary>
    [Fact]
    public void ONEWaterShapeHasNoSeams()
    {
        Assert.Equal(0, WaterSeams.Of(Squares(split: true), [true, false]).Edges);
    }

    /// <summary>Two squares of ten side by side, the left two triangles, the right three where its left edge is halved.</summary>
    private static SkinnedMesh Squares(bool split)
    {
        Vector3[] places =
        [
            new(0f, 0f, 0f), new(10f, 0f, 0f), new(10f, 10f, 0f), new(0f, 10f, 0f),
            new(10f, 0f, 0f), new(20f, 0f, 0f), new(20f, 10f, 0f), new(10f, 10f, 0f), new(10f, 5f, 0f),
        ];
        int[] left = [0, 1, 2, 0, 2, 3];
        int[] right = split ? [4, 5, 8, 8, 5, 6, 8, 6, 7] : [4, 5, 6, 4, 6, 7];
        var normals = new Vector3[places.Length];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        return SkinnedMesh.Of(
            places, normals, [.. left, .. right], Vector3.Zero, new Vector3(20f, 10f, 0f), new Vector2[places.Length],
            [new MeshShape("Left", 0, left.Length), new MeshShape("Right", left.Length, right.Length)]);
    }
}
