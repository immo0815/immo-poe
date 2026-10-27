using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The distance out to sea as the waves' way in (ShoreField, v0.1.295): the shore's along a plain coast, falling along a channel the
/// farther in, and bent round what crosses the water - Port's crests run along the channel between the ship and the quay in the game.
/// </summary>
public class ShoreWayTests
{
    /// <summary>A mesh of flat quads, each its corners' x and y and one z - up is minus z - a shape a quad, or as many as asked.</summary>
    private static SkinnedMesh Quads(params (string Name, (float X0, float Y0, float X1, float Y1, float Z)[] Quads)[] shapes)
    {
        var places = new List<Vector3>();
        var indices = new List<int>();
        var named = new List<MeshShape>();
        foreach ((string name, (float X0, float Y0, float X1, float Y1, float Z)[] quads) in shapes)
        {
            int from = indices.Count;
            foreach ((float x0, float y0, float x1, float y1, float z) in quads)
            {
                int first = places.Count;
                places.AddRange([new(x0, y0, z), new(x1, y0, z), new(x1, y1, z), new(x0, y1, z)]);
                indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
            }

            named.Add(new MeshShape(name, from, indices.Count - from));
        }

        var normals = new Vector3[places.Count];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        Vector3 least = places.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        Vector3 most = places.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        return SkinnedMesh.Of([.. places], normals, [.. indices], least, most, [.. places.Select(one => new Vector2(one.X, one.Y))], [.. named]);
    }

    /// <summary>The bay of WaterTests: water at nought over -3000..3000, a sea floor 300 under it to x 1000, the quay above it past - and whatever more is asked.</summary>
    private static SkinnedMesh Bay(params (string Name, (float X0, float Y0, float X1, float Y1, float Z)[] Quads)[] more) => Quads(
    [
        ("Water", [(-3000f, -3000f, 3000f, 3000f, 0f)]),
        ("Sea floor", [(-3000f, -3000f, 1000f, 3000f, 300f)]),
        ("Quay", [(1000f, -3000f, 3000f, 3000f, -100f)]),
        .. more,
    ]);

    /// <summary>The field's distance out to sea at a place, world units.</summary>
    private static float Ocean(ShoreField shore, float x, float y)
    {
        shore.At(new Vector2(x, y), out _, out float ocean);
        return (1f - ocean) * ShoreField.Reach;
    }

    /// <summary>The field's shore distance at a place, world units.</summary>
    private static float Shore(ShoreField shore, float x, float y)
    {
        shore.At(new Vector2(x, y), out float near, out _);
        return near * ShoreField.Reach;
    }

    [Fact]
    public void ALONGAPLAINCOASTTheWayInIsTheShoresDistance()
    {
        ShoreField shore = ShoreField.Of(Bay(), shape => shape == 0, shape => shape is 1 or 2, null, shape => false)!;
        foreach (float x in (float[])[-1500f, -500f, 0f, 500f, 900f])
        {
            Assert.InRange(Ocean(shore, x, 0f) - Shore(shore, x, 0f), -30f, 30f);
        }

        Assert.Equal(0, shore.BlockedCells);
    }

    [Fact]
    public void INACHANNELTheWayInFallsTheFartherInWhileTheShoreStaysNear()
    {
        // A CHANNEL 300 WIDE cut 1,500 into the quay, its floor under the water like the bay's: the sea comes in at x 1000.
        ShoreField shore = ShoreField.Of(
            Quads(
                ("Water", [(-3000f, -3000f, 3000f, 3000f, 0f)]),
                ("Sea floor", [(-3000f, -3000f, 1000f, 3000f, 300f), (1000f, -150f, 2500f, 150f, 300f)]),
                ("Quay", [(1000f, -3000f, 3000f, -150f, -100f), (1000f, 150f, 3000f, 3000f, -100f), (2500f, -150f, 3000f, 150f, -100f)])),
            shape => shape == 0,
            shape => shape is 1 or 2,
            null,
            shape => false)!;

        // THE SHORE IS THE CHANNEL'S SIDES, 150 AWAY ALL ALONG IT - but the way in grows: the crests run along the channel, not across.
        float shoreNear = Shore(shore, 1300f, 0f), shoreFar = Shore(shore, 2100f, 0f);
        Assert.InRange(shoreNear, 100f, 180f);
        Assert.InRange(shoreFar, 100f, 180f);
        float oceanNear = Ocean(shore, 1300f, 0f), oceanFar = Ocean(shore, 2100f, 0f);
        Assert.InRange(oceanNear - oceanFar, 700f, 900f);

        // THE SLOPE THE ENGINE SENDS ITS WAVES DOWN points into the channel, along it.
        shore.Fields(new Vector2(1700f, 0f), out _, out _, out Vector2 slope);
        Assert.True(slope.X < 0f && MathF.Abs(slope.Y) < 0.2f * MathF.Abs(slope.X), $"slope {slope}");
        Assert.True(shore.Travelled > 0, "no cell took its way in");
    }

    [Fact]
    public void AHULLCROSSINGTheWaterTurnsTheWavesRoundItAndABridgeOverItDoesNot()
    {
        // A HULL 100 WIDE AND 3,000 LONG between the sea and the quay, its deck 50 above the water and its keel 100 under.
        (float, float, float, float, float)[] hull = [(100f, -1500f, 200f, 1500f, -50f), (100f, -1500f, 200f, 1500f, 100f)];
        (float, float, float, float, float)[] bridge = [(100f, -1500f, 200f, 1500f, -50f)];
        ShoreField hulled = ShoreField.Of(Bay(("Hull", hull)), shape => shape == 0, shape => shape is 1 or 2, null, shape => shape == 3)!;
        ShoreField bridged = ShoreField.Of(Bay(("Bridge", bridge)), shape => shape == 0, shape => shape is 1 or 2, null, shape => shape == 3)!;

        // BEHIND THE HULL the way in goes round its ends, 1,500 off: far longer than the 500 straight from the shore.
        Assert.InRange(Shore(hulled, 500f, 0f), 470f, 530f);
        Assert.True(Ocean(hulled, 500f, 0f) < -300f, $"behind the hull {Ocean(hulled, 500f, 0f)}");
        Assert.True(hulled.BlockedCells > 400, $"{hulled.BlockedCells} cells in the way");

        // THE BRIDGE STANDS OVER THE WATER, NOT IN IT: the waves pass under, the way in the shore's own.
        Assert.InRange(Ocean(bridged, 500f, 0f) - Shore(bridged, 500f, 0f), -30f, 30f);
        Assert.Equal(0, bridged.BlockedCells);

        // IN FRONT OF THE HULL nothing changes.
        Assert.InRange(Ocean(hulled, -500f, 0f) - Shore(hulled, -500f, 0f), -30f, 30f);
    }
}
