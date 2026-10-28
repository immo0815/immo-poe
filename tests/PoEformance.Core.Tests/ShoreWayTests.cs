using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The distance out to sea as the waves' way in (ShoreField, v0.1.295): the shore's along a plain coast, falling along a channel the
/// farther in - by the ground alone (v0.1.307), and on its twin bent round what stands in the water as well, as v0.1.295 to v0.1.306
/// drew it; the game's waves cross Port's channel from the ship's side.
/// </summary>
public class ShoreWayTests
{
    /// <summary>A mesh of flat quads, each its corners' x and y and one z - up is minus z - a shape a quad, or as many as asked.</summary>
    private static SkinnedMesh Quads(params (string Name, (float X0, float Y0, float X1, float Y1, float Z)[] Quads)[] shapes)
        => Cornered([.. shapes.Select(shape => (shape.Name, shape.Quads.Select(Flat).ToArray()))]);

    /// <summary>A flat quad's four corners.</summary>
    private static Vector3[] Flat((float X0, float Y0, float X1, float Y1, float Z) quad) =>
        [new(quad.X0, quad.Y0, quad.Z), new(quad.X1, quad.Y0, quad.Z), new(quad.X1, quad.Y1, quad.Z), new(quad.X0, quad.Y1, quad.Z)];

    /// <summary>A sheer quad from (x0, y0) to (x1, y1), standing from z0 to z1 - a wall, which covers no cell's middle from above.</summary>
    private static Vector3[] Sheer(float x0, float y0, float x1, float y1, float z0, float z1) =>
        [new(x0, y0, z0), new(x1, y1, z0), new(x1, y1, z1), new(x0, y0, z1)];

    /// <summary>A mesh of quads given by their four corners, a shape a list of them.</summary>
    private static SkinnedMesh Cornered(params (string Name, Vector3[][] Quads)[] shapes)
    {
        var places = new List<Vector3>();
        var indices = new List<int>();
        var named = new List<MeshShape>();
        foreach ((string name, Vector3[][] quads) in shapes)
        {
            int from = indices.Count;
            foreach (Vector3[] quad in quads)
            {
                int first = places.Count;
                places.AddRange(quad);
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

    /// <summary>
    /// A PIER INTO DEEP WATER (Port's, measured off a side-by-side video, v0.1.332): by the ground the way in wraps round its sea face, the
    /// waves running square onto it; past the walls (v0.1.333) the pier's own ground is in no wave's way, and they run on along it towards
    /// the coast it stands out from - while its root, nearer the beach than its walls, still counts.
    /// </summary>
    [Fact]
    public void PASTTHEWALLSThePiersOwnGroundTurnsNoWave()
    {
        // DEEP WATER (500 under) to x 700, a shallow shelf (100 under) to the land at x 1000, and a pier 400 wide out to x -1000.
        ShoreField byGround = ShoreField.Of(
            Quads(
                ("Water", [(-3000f, -3000f, 3000f, 3000f, 0f)]),
                ("Deep floor", [(-3000f, -3000f, 700f, 3000f, 500f)]),
                ("Shelf", [(700f, -3000f, 1000f, 3000f, 100f)]),
                ("Land", [(1000f, -3000f, 3000f, 3000f, -100f)]),
                ("Pier", [(-1000f, -200f, 700f, 200f, -100f)])),
            shape => shape == 0,
            shape => shape >= 1,
            null,
            shape => false)!;
        ShoreField past = byGround.Way(false, true);
        Assert.True(past.PassesWalls);
        Assert.False(byGround.PassesWalls);
        Assert.Same(past, byGround.Way(true, true));
        Assert.True(past.WallGround > 100, $"{past.WallGround} cells of a wall's own ground");

        // BY THE GROUND, 300 off the pier's face: the waves run down the slope onto it, down y.
        byGround.Fields(new Vector2(-500f, 500f), out _, out _, out Vector2 onto);
        Assert.True(onto.Y > 0f && MathF.Abs(onto.X) < 0.3f * onto.Y, $"by the ground {onto}");
        Assert.InRange(Ocean(byGround, -500f, 500f), 250f, 350f);

        // PAST THE WALLS, the same place: down the slope along the pier towards the coast, up x.
        past.Fields(new Vector2(-500f, 500f), out _, out _, out Vector2 along);
        Assert.True(along.X < 0f && MathF.Abs(along.Y) < 0.5f * -along.X, $"past the walls {along}");

        // THE KINDS: the pier's middle a wall's own ground, its root by the shelf and the land's ground counted.
        int Cell(float x, float y) => (((int)((y - byGround.Top) / byGround.CellSize)) * byGround.Width) + (int)((x - byGround.Left) / byGround.CellSize);
        Assert.Equal(ShoreField.LandBit | ShoreField.GroundBit, byGround.Kinds[Cell(-500f, 0f)]);
        Assert.Equal(ShoreField.LandBit | ShoreField.GroundBit | ShoreField.SteersBit, byGround.Kinds[Cell(650f, 0f)] & ~ShoreField.ShoreBit);
        Assert.Equal(ShoreField.LandBit | ShoreField.GroundBit | ShoreField.SteersBit, byGround.Kinds[Cell(2000f, 0f)]);
        Assert.NotEqual(0, byGround.Kinds[Cell(-500f, 190f)] & ShoreField.WallBit);

        // AND THE CAPTURE'S NUMBERS OF IT read back as they were made: the kinds and every way in, in world units.
        Features.ShoreNumbers numbers = Features.CapturePanes.ReadField(Features.CapturePanes.FieldBytes(byGround))!;
        Assert.Equal((byGround.Width, byGround.Height, byGround.Left, byGround.CellSize), (numbers.Width, numbers.Height, numbers.Left, numbers.Cell));
        Assert.Equal(byGround.Kinds.ToArray(), numbers.Kinds);
        int middle = Cell(-500f, 500f);
        Assert.Equal((1f - byGround.Texels[(middle * 2) + 1]) * ShoreField.Reach, numbers.ByGround[middle]);
        Assert.Equal((1f - past.Texels[(middle * 2) + 1]) * ShoreField.Reach, numbers.PastWalls[middle]);
        Assert.Null(Features.CapturePanes.ReadField("SHF2"u8));
    }

    /// <summary>The bay of WaterTests: water at nought over -3000..3000, a sea floor 300 under it to x 1000, the quay above it past - and whatever more is asked.</summary>
    private static SkinnedMesh Bay(params (string Name, Vector3[][] Quads)[] more) => Cornered(
    [
        ("Water", [Flat((-3000f, -3000f, 3000f, 3000f, 0f))]),
        ("Sea floor", [Flat((-3000f, -3000f, 1000f, 3000f, 300f))]),
        ("Quay", [Flat((1000f, -3000f, 3000f, 3000f, -100f))]),
        .. more,
    ]);

    [Fact]
    public void AHULLCROSSINGTheWaterTurnsTheWavesRoundItOnTheRoundWayAloneAndABridgeOverItNever()
    {
        // A HULL 100 WIDE AND 3,000 LONG between the sea and the quay: its deck 50 above the water, its keel 100 under, and its four
        // sides sheer between them - the sides cross the water, deck and keel do not.
        Vector3[][] hull =
        [
            Flat((100f, -1500f, 200f, 1500f, -50f)), Flat((100f, -1500f, 200f, 1500f, 100f)),
            Sheer(100f, -1500f, 100f, 1500f, -50f, 100f), Sheer(200f, -1500f, 200f, 1500f, -50f, 100f),
            Sheer(100f, -1500f, 200f, -1500f, -50f, 100f), Sheer(100f, 1500f, 200f, 1500f, -50f, 100f),
        ];
        Vector3[][] bridge = [Flat((100f, -1500f, 200f, 1500f, -50f))];
        ShoreField byGround = ShoreField.Of(Bay(("Hull", hull)), shape => shape == 0, shape => shape is 1 or 2, null, shape => shape == 3)!;
        ShoreField hulled = byGround.Way(true);
        ShoreField bridged = ShoreField.Of(Bay(("Bridge", bridge)), shape => shape == 0, shape => shape is 1 or 2, null, shape => shape == 3)!;
        Assert.False(byGround.RoundsWhatStands);
        Assert.True(hulled.RoundsWhatStands);
        Assert.Same(byGround, byGround.Way(false));

        // BY THE GROUND ALONE the waves run on under the hull, the way in the shore's own straight in.
        Assert.InRange(Ocean(byGround, 500f, 0f) - Shore(byGround, 500f, 0f), -30f, 30f);

        // ON THE ROUND WAY, BEHIND THE HULL, the way in goes round its ends, 1,500 off: far longer than the 500 straight from the shore -
        // its waterline, walked cell by cell, leaves no gap.
        Assert.InRange(Shore(hulled, 500f, 0f), 470f, 530f);
        Assert.True(Ocean(hulled, 500f, 0f) < -300f, $"behind the hull {Ocean(hulled, 500f, 0f)}");
        Assert.True(hulled.BlockedCells > 200, $"{hulled.BlockedCells} cells in the way");
        Assert.Equal(8, hulled.CrossingTriangles);

        // THE BRIDGE STANDS OVER THE WATER, NOT IN IT: the waves pass under, the way in the shore's own.
        Assert.InRange(Ocean(bridged, 500f, 0f) - Shore(bridged, 500f, 0f), -30f, 30f);
        Assert.Equal(0, bridged.BlockedCells);

        // IN FRONT OF THE HULL nothing changes.
        Assert.InRange(Ocean(hulled, -500f, 0f) - Shore(hulled, -500f, 0f), -30f, 30f);
    }

    /// <summary>
    /// A quay rising out of deep water is no shore, the same coast over shallow ground is - Port's channel by the quay stood white
    /// whatever the crests did, the engine's field-wave foam being whole within 150 of a shore (said from the live client, v0.1.298).
    /// </summary>
    [Fact]
    public void AWALLOUTOFDeepWaterIsNoShoreButTheWavesStillRunUpToIt()
    {
        // ONE COAST AT x 1000: south of y 0 a quay over a floor 500 under the water, north of it the same land over a floor 50 under.
        ShoreField shore = ShoreField.Of(
            Quads(
                ("Water", [(-3000f, -3000f, 3000f, 3000f, 0f)]),
                ("Deep floor", [(-3000f, -3000f, 1000f, 0f, 500f)]),
                ("Shallow floor", [(-3000f, 0f, 1000f, 3000f, 50f)]),
                ("Land", [(1000f, -3000f, 3000f, 3000f, -100f)])),
            shape => shape == 0,
            shape => shape is 1 or 2 or 3,
            null,
            shape => false)!;

        Assert.True(shore.WallCells > 50, $"{shore.WallCells} wall cells");

        // BY THE WALL the shore is the shallow coast's nearest corner, 1,500 away - but the way in is the wall's own 200.
        Assert.InRange(Shore(shore, 800f, -1500f), 1400f, 1650f);
        Assert.InRange(Ocean(shore, 800f, -1500f), 150f, 260f);

        // BY THE SHALLOW COAST both are its 200.
        Assert.InRange(Shore(shore, 800f, 1500f), 170f, 230f);
        Assert.InRange(Ocean(shore, 800f, 1500f) - Shore(shore, 800f, 1500f), -30f, 30f);
    }

    /// <summary>
    /// What stands in the sea takes the same test - Port's quay is faced with spraying stone standing out of water 375 deep, and v0.1.299
    /// kept it shore for that, the channel white (said from the live client, v0.1.300).
    /// </summary>
    [Fact]
    public void ASPRAYINGFacingOverDeepWaterIsAWallARockInTheShallowsIsShore()
    {
        // THE SAME COAST AT x 1000, its quay south of y 0 faced with stone standing out of the water; a rock in the shallows north.
        ShoreField shore = ShoreField.Of(
            Quads(
                ("Water", [(-3000f, -3000f, 3000f, 3000f, 0f)]),
                ("Deep floor", [(-3000f, -3000f, 1000f, 0f, 500f)]),
                ("Shallow floor", [(-3000f, 0f, 1000f, 3000f, 50f)]),
                ("Land", [(1000f, -3000f, 3000f, 3000f, -100f)]),
                ("Facing", [(975f, -3000f, 1000f, -100f, -50f)]),
                ("Rock", [(-1000f, 1000f, -900f, 1100f, -50f)])),
            shape => shape == 0,
            shape => shape is 1 or 2 or 3,
            shape => shape is 4 or 5,
            shape => false)!;

        Assert.True(shore.StandingWalls > 50, $"{shore.StandingWalls} standing walls");

        // BY THE FACED QUAY the shore is the shallow coast, 1,500 off; BESIDE THE ROCK it is the rock.
        Assert.InRange(Shore(shore, 800f, -1500f), 1400f, 1650f);
        Assert.InRange(Shore(shore, -950f, 1300f), 170f, 230f);

        // THE WAY IN by the ground alone knows no rock: beside it the sea is the coast's, 1,950 off; round what stands, the rock's.
        Assert.True(Ocean(shore, -950f, 1300f) > 1500f, $"by the ground {Ocean(shore, -950f, 1300f)}");
        Assert.True(Ocean(shore.Way(true), -950f, 1300f) < 600f, $"round the rock {Ocean(shore.Way(true), -950f, 1300f)}");
    }

    [Fact]
    public void AFLOORATTheBeachsDepthIsStillShore()
    {
        // THE BAY'S FLOOR LIES 300 UNDER, the beach's own depth: its quay stays a shore, as every water test has it.
        ShoreField shore = ShoreField.Of(Bay(), shape => shape == 0, shape => shape is 1 or 2, null, shape => false)!;
        Assert.InRange(Shore(shore, 500f, 0f), 470f, 530f);
        Assert.InRange(Shore(shore, -1000f, 0f), 1970f, 2000f);
    }

    /// <summary>
    /// Something over the water and something else under it, at one place, stand in nobody's way - Port's wobble core, an invisible plane
    /// over the ship, lay over the quay's sunken bricks and barred the channel's side 125 units wide (said from the live client, v0.1.297).
    /// </summary>
    [Fact]
    public void APLANEOVERTheWaterAndStonesUnderItAreNoObstacleTogether()
    {
        Vector3[][] plane = [Flat((-500f, -1500f, 900f, 1500f, -360f))];
        Vector3[][] bricks = [Flat((700f, -1500f, 1000f, 1500f, 240f))];
        ShoreField shore = ShoreField.Of(
            Bay(("Plane", plane), ("Bricks", bricks)), shape => shape == 0, shape => shape is 1 or 2, null, shape => shape is 3 or 4)!;

        Assert.Equal(0, shore.BlockedCells);
        Assert.Equal(0, shore.CrossingTriangles);
        Assert.Empty(shore.CrossingShapes);
        Assert.InRange(Ocean(shore, 800f, 0f) - Shore(shore, 800f, 0f), -30f, 30f);
    }
}
