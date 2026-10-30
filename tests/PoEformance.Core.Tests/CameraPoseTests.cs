using System.Numerics;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// The model pane's camera as a pose - a focus, two angles and a distance, in perspective or parallel - and the pictures drawn from it.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT (2026-10-28): navigating the Monster Book's stage was poor and zooming close impossible. Every rule
/// here is a sum the pane leans on: the parallel pose IS the old camera, the gestures keep what they promise on the picture, and the
/// perspective drawing puts a texel and a depth where the camera says.
/// </remarks>
public class CameraPoseTests
{
    private const int Size = 128;

    /// <summary>The fitted parallel pose is the camera every pane was drawn from before, turned, tilted, zoomed and panned.</summary>
    [Theory]
    [InlineData(0f, 0f, 1f, 0f, 0f)]
    [InlineData(0.6f, 0.4f, 1f, 0f, 0f)]
    [InlineData(-2.1f, -0.9f, 2.5f, 0.1f, -0.2f)]
    [InlineData(3f, 1.2f, 0.4f, -0.3f, 0.05f)]
    public void APARALLELPoseFittedToTheBoxIsTheOldCamera(float turn, float tilt, float zoom, float panX, float panY)
    {
        SkinnedMesh box = Box(new Vector3(-3f, -1f, -7f), new Vector3(5f, 2f, 1f));
        var pan = new Vector2(panX, panY);
        MeshPicture.Camera old = MeshPicture.Camera.Of(box, turn, tilt, zoom, pan);
        MeshPicture.Camera posed = MeshPicture.Camera.Of(box, CameraPose.Fitted(box, turn, tilt, parallel: true, zoom, pan));
        Assert.False(posed.Perspective);
        foreach (Vector3 corner in Corners(box))
        {
            Vector3 a = old.Place(corner), b = posed.Place(corner);
            Assert.True(Vector2.Distance(new Vector2(a.X, a.Y), new Vector2(b.X, b.Y)) < 1e-5f, $"{corner}: {a} against {b}");
        }
    }

    /// <summary>And its picture is the old picture, pixel for pixel but for the last bit of a float.</summary>
    [Fact]
    public void THEPICTUREOfAParallelPoseIsTheOldPicture()
    {
        SkinnedMesh box = Box(new Vector3(-3f, -1f, -7f), new Vector3(5f, 2f, 1f));
        byte[] old = MeshPicture.Of(box, Size, 0.6f, 0.4f).Rgba;
        var canvas = new MeshPicture.Canvas(Size) { Pose = CameraPose.Fitted(box, 0.6f, 0.4f, parallel: true) };
        byte[] posed = MeshPicture.Of(box, canvas).Rgba;
        int differ = 0, drawn = 0;
        for (var at = 0; at < old.Length; at += 4)
        {
            drawn += old[at + 3] > 0 ? 1 : 0;
            differ += old.AsSpan(at, 4).SequenceEqual(posed.AsSpan(at, 4)) ? 0 : 1;
        }

        Assert.True(drawn > Size * Size / 8, $"{drawn} drawn");
        Assert.True(differ * 200 < drawn, $"{differ} of {drawn} pixels differ");
    }

    /// <summary>The game's angle looks as the game's camera matrix does: down 48.7 degrees along +x +y, with +x -y to the right.</summary>
    [Fact]
    public void THEGAMESAngleLooksAsTheGamesCameraDoes()
    {
        var pose = new CameraPose(Vector3.Zero, CameraPose.GameTurn, CameraPose.GameTilt, 100f);
        Vector3 forward = pose.Forward, right = pose.Right, down = pose.Down;
        Assert.True(Vector3.Distance(forward, new Vector3(0.467f, 0.467f, 0.751f)) < 2e-3f, $"{forward}");
        Assert.True(Vector3.Distance(right, Vector3.Normalize(new Vector3(1f, -1f, 0f))) < 1e-4f, $"{right}");

        // THE PICTURE'S DOWN IS THE GAME'S CLIP Y TURNED OVER: (1.983, 1.983, -2.462) over its length, the other way.
        Assert.True(Vector3.Distance(down, -Vector3.Normalize(new Vector3(1.983f, 1.983f, -2.462f))) < 2e-3f, $"{down}");
    }

    /// <summary>
    /// The game's pose about a player's feet puts the eye where the game's own camera stood over him, within a hundredth of its distance -
    /// The Red Vale's, and every committed fixture's taken under this camera - where the game view about his feet themselves did not.
    /// </summary>
    /// <remarks>
    /// THE RED VALE'S EYE IS ITS CAPTURE'S, taken from the camera matrix beside the player line (GameAhead's measurement); the fixtures'
    /// eyes are taken apart here from the camera lines their capture.txt wrote (tests/fixtures/captures), each over the player's place at
    /// his terrain height.
    /// </remarks>
    [Fact]
    public void THEGAMESPoseAboutThePlayersFeetPutsTheEyeWhereTheGamesCameraStood()
    {
        var cases = new List<(string Area, Vector3 Feet, Vector3 Eye)> { ("The Red Vale", new Vector3(4081.842f, 7314.178f, 0f), new Vector3(3040.1f, 6271.5f, -1614.8f)) };
        foreach ((string area, float[] matrix, Vector3 feet) in UnderThisCamera)
        {
            GameCamera camera = Assert.IsType<GameCamera>(GameCamera.Of(matrix, 3440, 1440));
            cases.Add((area, feet, camera.Eye));
        }

        foreach ((string area, Vector3 feet, Vector3 eye) in cases)
        {
            CameraPose pose = new CameraPose(new Vector3(1f, 2f, 3f), 0.3f, 0.2f, 50f, Parallel: true).AsTheGameAt(feet);
            Assert.Equal(feet + new Vector3(CameraPose.GameAhead, 0f), pose.Focus);
            Assert.Equal((CameraPose.GameTurn, CameraPose.GameTilt, CameraPose.GameDistance, true), (pose.Turn, pose.Tilt, pose.Distance, pose.Parallel));
            Assert.True(Vector3.Distance(pose.Eye, eye) < CameraPose.GameDistance * 0.01f, $"{area}: the eye at {pose.Eye}, the game's at {eye}");

            // ABOUT THE FEET THEMSELVES, as the game view stood before, the eye is out by more than that.
            Vector3 before = new CameraPose(feet, 0f, 0f, 1f).AsTheGame().Eye;
            Assert.True(Vector3.Distance(before, eye) > CameraPose.GameDistance * 0.01f, $"{area}: about the feet the eye at {before}, the game's at {eye}");
        }
    }

    /// <summary>
    /// The committed fixtures taken under the game's camera of 48.67 degrees down, each capture.txt's camera line and the player's place at his
    /// terrain height - Oasis's and Steppe's are an older camera's, 46.8 degrees down from 1,917 units.
    /// </summary>
    private static readonly (string Area, float[] Matrix, Vector3 Feet)[] UnderThisCamera =
    [
        ("Augury", [1.105f, 1.983f, 0.477f, 0.467f, -1.105f, 1.983f, 0.477f, 0.467f, 0f, -2.462f, 0.769f, 0.751f, 4178.573f, -33445.98f, -5932.682f, -5724.86f], new Vector3(6581.521f, 10364.13f, 0f)),
        ("Steaming Springs", [1.105f, 1.983f, 0.487f, 0.467f, -1.105f, 1.983f, 0.487f, 0.467f, 0f, -2.462f, 0.784f, 0.751f, 176.982f, -15722.91f, -1658.924f, -1389.074f], new Vector3(3846.552f, 4006.763f, -125f)),
        ("Sulphuric Caverns", [1.105f, 1.983f, 0.488f, 0.467f, -1.105f, 1.983f, 0.488f, 0.467f, 0f, -2.462f, 0.786f, 0.751f, -327.547f, -21119.06f, -3162.382f, -2824.887f], new Vector3(5513.06f, 5216.551f, 0f)),
        ("the Titan Grotto", [1.105f, 1.983f, 0.478f, 0.467f, -1.105f, 1.983f, 0.478f, 0.467f, 0f, -2.462f, 0.771f, 0.751f, 10029.18f, -81187.95f, -16376.43f, -15792.02f], new Vector3(15427.51f, 24506.32f, -875f)),
    ];

    /// <summary>In perspective the focus lands in the middle, and the picture spans thirty degrees from its top to its bottom.</summary>
    [Fact]
    public void INPERSPECTIVEThePictureSeesThirtyDegrees()
    {
        SkinnedMesh box = Box(new Vector3(-10f), new Vector3(10f));
        var pose = new CameraPose(new Vector3(1f, 2f, -3f), 0.7f, 0.5f, 40f);
        MeshPicture.Camera camera = MeshPicture.Camera.Of(box, pose);
        Assert.True(camera.Perspective);
        Vector3 middle = camera.Place(pose.Focus);
        Assert.True(Vector2.Distance(new Vector2(middle.X, middle.Y), new Vector2(0.5f)) < 1e-5f, $"{middle}");
        Assert.Equal(40f, middle.Z, 3);

        // A point at the focus's depth, as far down as the tangent of fifteen degrees takes it, lies on the picture's bottom edge.
        Vector3 edge = camera.Place(pose.Focus + (pose.Down * (40f * MathF.Tan(CameraPose.FieldOfView / 2f))));
        Assert.Equal(1f, edge.Y, 4);
        Assert.Equal(0.5f, edge.X, 4);
        Assert.True(camera.InFront(edge));
        Assert.False(camera.InFront(camera.Place(pose.Eye - pose.Forward)));
    }

    /// <summary>The wheel's zoom keeps the point it zooms towards where it is on the picture, in perspective and parallel, nearer or further.</summary>
    [Theory]
    [InlineData(false, 0.5f)]
    [InlineData(false, 1.7f)]
    [InlineData(true, 0.5f)]
    [InlineData(true, 1.7f)]
    public void ZOOMINGKeepsThePointWhereItIsOnThePicture(bool parallel, float factor)
    {
        SkinnedMesh box = Box(new Vector3(-10f), new Vector3(10f));
        var pose = new CameraPose(new Vector3(0f, 0f, -2f), 0.4f, 0.6f, 60f, parallel);
        var about = new Vector3(4f, -3f, -5f);
        Vector3 before = MeshPicture.Camera.Of(box, pose).Place(about);
        CameraPose zoomed = pose.Zoomed(about, factor, 1000f);
        Vector3 after = MeshPicture.Camera.Of(box, zoomed).Place(about);
        Assert.Equal(60f * factor, zoomed.Distance, 3);
        Assert.True(Vector2.Distance(new Vector2(before.X, before.Y), new Vector2(after.X, after.Y)) < 1e-4f, $"{before} then {after}");
    }

    /// <summary>A barrel fills the picture: the wheel brings the eye as close as a couple of units, held there however far it is turned.</summary>
    [Fact]
    public void ABARRELFillsThePicture()
    {
        SkinnedMesh barrel = Box(new Vector3(-30f, -30f, -80f), new Vector3(30f, 30f, 0f));
        var pose = CameraPose.Fitted(barrel, 0.3f, 0.4f, parallel: false);
        for (var notch = 0; notch < 200; notch++)
        {
            pose = pose.Zoomed(pose.Focus, 1f / 1.18f, 1000f);
        }

        Assert.Equal(CameraPose.Closest, pose.Distance, 3);
        MeshPicture.Camera camera = MeshPicture.Camera.Of(barrel, pose);
        Vector3 a = camera.Place(pose.Focus - (pose.Right * 30f)), b = camera.Place(pose.Focus + (pose.Right * 30f));
        Assert.True(b.X - a.X > 10f, $"the barrel spans {b.X - a.X} of the picture");
    }

    /// <summary>A right drag pans: the focus's plane follows the hand by as many shares of the picture.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PANNINGMovesTheSceneWithTheHand(bool parallel)
    {
        SkinnedMesh box = Box(new Vector3(-10f), new Vector3(10f));
        var pose = new CameraPose(new Vector3(1f, 1f, -1f), -0.8f, 0.3f, 50f, parallel);
        var drag = new Vector2(0.12f, -0.07f);
        Vector3 placed = MeshPicture.Camera.Of(box, pose.Panned(drag)).Place(pose.Focus);
        Assert.True(Vector2.Distance(new Vector2(placed.X, placed.Y), new Vector2(0.5f) + drag) < 1e-4f, $"{placed}");
    }

    /// <summary>A left drag turns about the focus: it stays in the middle, the eye at its distance, the tilt held between straight down and straight up.</summary>
    [Fact]
    public void ORBITINGTurnsAboutTheFocus()
    {
        SkinnedMesh box = Box(new Vector3(-10f), new Vector3(10f));
        var pose = new CameraPose(new Vector3(3f, -2f, -4f), 0.2f, 0.3f, 25f);
        CameraPose turned = pose.Orbited(1.1f, 0.4f);
        Vector3 middle = MeshPicture.Camera.Of(box, turned).Place(pose.Focus);
        Assert.True(Vector2.Distance(new Vector2(middle.X, middle.Y), new Vector2(0.5f)) < 1e-5f);
        Assert.Equal(25f, Vector3.Distance(turned.Eye, pose.Focus), 3);
        Assert.Equal(CameraPose.Steepest, pose.Orbited(0f, 10f).Tilt);
        Assert.Equal(-CameraPose.Steepest, pose.Orbited(0f, -10f).Tilt);
        Assert.True(MathF.Abs(pose.Orbited(100f, 0f).Turn) <= MathF.PI);

        // STRAIGHT DOWN looks along +z, up being minus z.
        Assert.True(Vector3.Distance(pose.Orbited(0f, 10f).Forward, Vector3.UnitZ) < 1e-5f);
    }

    /// <summary>A double click looks at the point under it from where the eye stands: the point comes to the middle, the eye stays.</summary>
    [Fact]
    public void LOOKINGAtAPointBringsItToTheMiddleFromWhereTheEyeStands()
    {
        SkinnedMesh box = Box(new Vector3(-10f), new Vector3(10f));
        var pose = new CameraPose(Vector3.Zero, 0.5f, 0.6f, 30f);
        var point = new Vector3(6f, 2f, -3f);
        CameraPose looking = pose.LookingAt(point);
        Vector3 placed = MeshPicture.Camera.Of(box, looking).Place(point);
        Assert.True(Vector2.Distance(new Vector2(placed.X, placed.Y), new Vector2(0.5f)) < 1e-4f, $"{placed}");
        Assert.True(Vector3.Distance(looking.Eye, pose.Eye) < 1e-3f, $"{looking.Eye} against {pose.Eye}");
        Assert.Equal(point, looking.Focus);
    }

    /// <summary>The keys fly eye and focus together, along the view, across it and up the world, four times as fast with shift.</summary>
    [Fact]
    public void THEKEYSFlyAlongTheViewAcrossItAndUpTheWorld()
    {
        var pose = new CameraPose(Vector3.Zero, 0.5f, 0.6f, 100f);
        Vector3 ahead = pose.Flight(1f, 0f, 0f, 0.1f, fast: false);
        Assert.True(Vector3.Dot(Vector3.Normalize(ahead), pose.Forward) > 0.9999f);
        Assert.Equal(8f, ahead.Length(), 3);
        Assert.Equal(32f, pose.Flight(1f, 0f, 0f, 0.1f, fast: true).Length(), 3);
        Assert.True(Vector3.Dot(Vector3.Normalize(pose.Flight(0f, 1f, 0f, 0.1f, fast: false)), pose.Right) > 0.9999f);
        Assert.True(Vector3.Dot(Vector3.Normalize(pose.Flight(0f, 0f, 1f, 0.1f, fast: false)), -Vector3.UnitZ) > 0.9999f);
        Assert.Equal(Vector3.Zero, pose.Flight(0f, 0f, 0f, 0.1f, fast: true));
        CameraPose flown = pose.Flown(ahead);
        Assert.Equal(pose.Distance, flown.Distance);
        Assert.True(Vector3.Distance(flown.Eye - pose.Eye, ahead) < 1e-4f);
    }

    /// <summary>The pick finds the nearest surface along a ray through the picture - what a double click and the wheel go towards.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void APICKFindsTheNearestSurfaceUnderThePointer(bool parallel)
    {
        SkinnedMesh floor = Floor(20f, 0f, Checker());
        var pose = new CameraPose(new Vector3(0f, 0f, 0f), 0.3f, 0.8f, 40f, parallel);
        var pointer = new Vector2(0.62f, 0.41f);
        (Vector3 from, Vector3 way) = pose.Ray(pointer);
        Assert.True(MeshPicture.Pick(floor, floor.Positions, from, way, out float away));
        Vector3 hit = from + (way * away);
        Assert.Equal(0f, hit.Z, 3);
        Vector3 placed = MeshPicture.Camera.Of(floor, pose).Place(hit);
        Assert.True(Vector2.Distance(new Vector2(placed.X, placed.Y), pointer) < 1e-4f, $"{placed}");
        Assert.False(MeshPicture.Pick(floor, floor.Positions, from, -way, out _));
    }

    /// <summary>
    /// A floor seen at a slant in perspective is textured where the camera puts it: its texture's halves part at the point the floor's
    /// middle line lands on, not half way up its picture, as an affine read would put them.
    /// </summary>
    [Fact]
    public void APERSPECTIVEFloorIsTexturedWhereTheCameraPutsIt()
    {
        SkinnedMesh floor = Floor(20f, 0f, Halves());
        var pose = new CameraPose(Vector3.Zero, 0f, 0.35f, 18f);
        var canvas = new MeshPicture.Canvas(Size) { Pose = pose };
        GamePicture picture = MeshPicture.Of(floor, canvas, skin: Halves());
        MeshPicture.Camera camera = MeshPicture.Camera.Of(floor, pose);

        // THE TEXTURE'S HALVES PART ALONG THE FLOOR'S y = 0: red where y is less, blue where more.
        Vector3 seam = camera.Place(Vector3.Zero);
        Vector3 near = camera.Place(new Vector3(0f, -1f, 0f));
        Vector3 far = camera.Place(new Vector3(0f, 1f, 0f));
        (byte r1, byte b1) = At(picture, near);
        (byte r2, byte b2) = At(picture, far);
        Assert.True(r1 > b1 * 2, $"red before the seam at {near}: {r1}, {b1}");
        Assert.True(b2 > r2 * 2, $"blue past it at {far}: {r2}, {b2}");

        // AN AFFINE READ WOULD PUT THE SEAM WHERE THE FLOOR'S PICTURE IS HALF WAY UP - which is not where the camera puts y = 0.
        Vector3 nearEdge = camera.Place(new Vector3(0f, -10f, 0f)), farEdge = camera.Place(new Vector3(0f, 10f, 0f));
        float halfway = (nearEdge.Y + farEdge.Y) * 0.5f;
        Assert.True(MathF.Abs(halfway - seam.Y) * Size > 3f, $"the slant is too small to tell: {halfway} against {seam.Y}");
    }

    /// <summary>
    /// A floor running under the eye crosses the near plane: the part in front is drawn to the picture's foot without tearing, every
    /// pixel at the depth its own place has - and nothing behind the eye is drawn.
    /// </summary>
    [Fact]
    public void AFLOORThroughTheNearPlaneIsDrawnInFrontOfItAlone()
    {
        SkinnedMesh floor = Floor(200f, 0f, null);
        var pose = new CameraPose(new Vector3(0f, 0f, -5f), 0.2f, 0.25f, 10f);
        MeshPicture.Camera camera = MeshPicture.Camera.Of(floor, pose);
        Assert.True(pose.Eye.Z < 0f && pose.Eye.Z > -10f, $"the eye at {pose.Eye} should stand just over the floor");

        var canvas = new MeshPicture.Canvas(Size) { Pose = pose };
        GamePicture picture = MeshPicture.Of(floor, canvas);
        int bottom = 0;
        for (var x = 0; x < Size; x++)
        {
            bottom += picture.Rgba[(((Size - 1) * Size) + x) * 4 + 3] > 0 ? 1 : 0;
        }

        Assert.Equal(Size, bottom);

        // THE HORIZON: rows over it see nothing.
        Assert.Equal(0, picture.Rgba[(Size / 2) * 4 + 3]);

        // EVERY DRAWN PIXEL'S DEPTH IS ITS RAY'S MEETING WITH THE FLOOR.
        var probe = new PixelProbe(Size / 3, Size - 6);
        canvas.Probe = probe;
        MeshPicture.Of(floor, canvas);
        (Vector3 from, Vector3 way) = pose.Ray(new Vector2((probe.X + 0.5f) / Size, (probe.Y + 0.5f) / Size));
        float along = -from.Z / way.Z;
        float depth = Vector3.Transform(from + (way * along), camera.View).Z;
        Assert.True(probe.Drawn);
        Assert.Equal(depth, probe.Final, 2);
    }

    /// <summary>The floor's lines in perspective come from the real eye: finite, on the picture, and on the eye's side of the floor.</summary>
    [Fact]
    public void THEFLOORSLinesInPerspectiveAreDrawnThroughTheEye()
    {
        SkinnedMesh box = Box(new Vector3(-20f, -20f, -40f), new Vector3(20f, 20f, 0f));
        var pose = CameraPose.Fitted(box, 0.4f, 0.5f, parallel: false);
        MeshPicture.Camera camera = MeshPicture.Camera.Of(box, pose);
        var lines = new List<ModelFloor.Line>();
        ModelFloor.Of(lines, camera, 400f);
        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.True(
            float.IsFinite(line.From.X) && float.IsFinite(line.To.Y) && line.From.X >= -1e-3f && line.From.X <= 1.001f && line.To.Y >= -1e-3f && line.To.Y <= 1.001f));
        Assert.True(ModelFloor.Under(camera));
        Assert.False(ModelFloor.Under(MeshPicture.Camera.Of(box, pose.Orbited(0f, -2f) with { Focus = new Vector3(0f, 0f, 30f) })));
    }

    /// <summary>
    /// The water's march in perspective is the engine's: a wall standing across the ray's way is hit at the step the projected depths
    /// say, and nothing is hit over an empty picture.
    /// </summary>
    [Fact]
    public void THEPERSPECTIVEMarchMeetsAWallAcrossTheRay()
    {
        const int side = 64;
        const float focal = side * 1.8660254f;
        var depth = new float[side * side];
        Array.Fill(depth, float.MaxValue);
        for (var y = 0; y < side; y++)
        {
            for (int x = 40; x < side; x++)
            {
                depth[(y * side) + x] = 92f;
            }
        }

        // FROM A PIXEL AT DEPTH 90 IN THE MIDDLE ROW, THE RAY RUNS RIGHT AND AWAY: seven units along it is past the wall's depth of 92,
        // nine pixels right - where the wall begins.
        var start = new Vector3(0f, 0f, 90f);
        Vector3 way = Vector3.Normalize(new Vector3(1f, 0f, 0.3f));
        int step = ScreenReflection.MarchSeen(side / 2, side / 2, start, way, focal, side, depth, out int hitX, out int hitY);
        Assert.True(step >= 0, "the ray should meet the wall");
        Assert.True(hitX >= 40, $"{hitX}");
        Assert.Equal(side / 2, hitY);

        Array.Fill(depth, float.MaxValue);
        Assert.True(ScreenReflection.MarchSeen(side / 2, side / 2, start, way, focal, side, depth, out _, out _) < 0);
    }

    private static (byte Red, byte Blue) At(GamePicture picture, Vector3 placed)
    {
        int x = Math.Clamp((int)(placed.X * Size), 0, Size - 1);
        int y = Math.Clamp((int)(placed.Y * Size), 0, Size - 1);
        int at = ((y * Size) + x) * 4;
        Assert.True(picture.Rgba[at + 3] > 0, $"nothing drawn at {placed}");
        return (picture.Rgba[at], picture.Rgba[at + 2]);
    }

    private static IEnumerable<Vector3> Corners(SkinnedMesh mesh)
    {
        for (var corner = 0; corner < 8; corner++)
        {
            yield return new Vector3(
                (corner & 1) == 0 ? mesh.Least.X : mesh.Most.X,
                (corner & 2) == 0 ? mesh.Least.Y : mesh.Most.Y,
                (corner & 4) == 0 ? mesh.Least.Z : mesh.Most.Z);
        }
    }

    /// <summary>A box between two corners, two triangles a face, a normal per face.</summary>
    private static SkinnedMesh Box(Vector3 least, Vector3 most)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();
        var indices = new List<int>();
        Vector3 middle = (least + most) * 0.5f, half = (most - least) * 0.5f;
        foreach (Vector3 axis in new[] { Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ })
        {
            Vector3 u = axis == Vector3.UnitX ? Vector3.UnitY : Vector3.UnitX;
            Vector3 v = Vector3.Cross(axis, u);
            foreach (float sign in new[] { -1f, 1f })
            {
                int at = positions.Count;
                Vector3 face = middle + (axis * sign * Vector3.Dot(half, axis));
                Vector3 du = u * Vector3.Dot(half, u), dv = v * MathF.Abs(Vector3.Dot(half, Vector3.Abs(v)));
                positions.AddRange([face - du - dv, face + du - dv, face + du + dv, face - du + dv]);
                normals.AddRange(Enumerable.Repeat(axis * sign, 4));
                indices.AddRange([at, at + 1, at + 2, at, at + 2, at + 3]);
            }
        }

        return SkinnedMesh.Of([.. positions], [.. normals], [.. indices], least, most);
    }

    /// <summary>A square floor so wide across at height <paramref name="z"/>, its texture once over it: u along x, v along y.</summary>
    private static SkinnedMesh Floor(float wide, float z, Mipmaps? skin)
    {
        _ = skin;
        float half = wide * 0.5f;
        Vector3[] places = [new(-half, -half, z), new(half, -half, z), new(half, half, z), new(-half, half, z)];
        var normals = new Vector3[4];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        Vector2[] spots = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)];
        return SkinnedMesh.Of(places, normals, [0, 1, 2, 0, 2, 3], new Vector3(-half, -half, z - 1f), new Vector3(half, half, z), spots, [new MeshShape("Floor", 0, 6)]);
    }

    /// <summary>A texture red where v is under a half and blue where over.</summary>
    private static Mipmaps Halves()
    {
        var pixels = new byte[16 * 16 * 4];
        for (var one = 0; one < 16 * 16; one++)
        {
            bool low = one / 16 < 8;
            pixels[one * 4] = low ? (byte)220 : (byte)20;
            pixels[(one * 4) + 2] = low ? (byte)20 : (byte)220;
            pixels[(one * 4) + 3] = 255;
        }

        Mipmaps? levels = Mipmaps.Of(new GamePicture(16, 16, pixels));
        Assert.NotNull(levels);
        return levels;
    }

    private static Mipmaps Checker()
    {
        var pixels = new byte[8 * 8 * 4];
        for (var one = 0; one < 64; one++)
        {
            byte shade = ((one % 8) + (one / 8)) % 2 == 0 ? (byte)200 : (byte)60;
            pixels[one * 4] = shade;
            pixels[(one * 4) + 1] = shade;
            pixels[(one * 4) + 2] = shade;
            pixels[(one * 4) + 3] = 255;
        }

        Mipmaps? levels = Mipmaps.Of(new GamePicture(8, 8, pixels));
        Assert.NotNull(levels);
        return levels;
    }
}
