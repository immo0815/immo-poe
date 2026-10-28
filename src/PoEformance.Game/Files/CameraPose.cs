using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// Where a model pane is looked at from: the point it looks at and turns about, two angles, a distance, and whether it is drawn in perspective.
/// </summary>
/// <param name="Focus">The point the camera looks at and turns about, in the model's own space.</param>
/// <param name="Turn">Rotation about the model's up axis, in radians - MeshPicture.Camera's turn.</param>
/// <param name="Tilt">Rotation towards the viewer, in radians - positive looks down, a quarter turn straight down.</param>
/// <param name="Distance">How far the eye stands from the focus, in model units. On a parallel view it sets the scale alone: the focus's plane is drawn as the perspective view draws it, so the switch between the two keeps what is at the focus the size it was.</param>
/// <param name="Parallel">Whether the picture is drawn without perspective, as every pane was before v0.1.324.</param>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT (2026-10-28): navigating the Monster Book's stage was poor, and zooming close to an object was not
/// possible. The old camera was fitted to the model's box and turned about the box's middle, zoomed at most six times the fit - which on
/// Port's stage still showed a thousand units - and panned only by zooming to the pointer. A pose with a focus of its own turns about
/// whatever is looked at, zooms towards whatever is under the pointer without a limit but the eye's own, and flies.
///
/// IN PERSPECTIVE AS THE GAME SEES IT: thirty degrees from the top of the picture to the bottom, MEASURED off the game's camera matrix
/// (Port's capture of v0.1.319: the clip's y column is 3.732 long, one over tan 15 degrees). The game's own angle is the same matrix's:
/// the way it looks, (0.467, 0.467, 0.751), is down 48.67 degrees along +x +y, with +x -y to the right - <see cref="GameTurn"/> and
/// <see cref="GameTilt"/> - from about 2,165 units off the player.
///
/// THE ANGLES ARE MeshPicture.Camera's OWN, turned and tilted the same way (see <see cref="Turning"/>), so the parallel pose at the
/// box's middle and the fitted distance is the camera every pane was drawn from before - the regression tests hold the two together.
/// </remarks>
public readonly record struct CameraPose(Vector3 Focus, float Turn, float Tilt, float Distance, bool Parallel = false)
{
    /// <summary>How much the picture sees from its top edge to its bottom, in radians - MEASURED, the game's.</summary>
    public static readonly float FieldOfView = 30f * MathF.PI / 180f;

    /// <summary>Shares of the picture's side per unit across, at a unit's distance from the eye: one over twice the tangent of half the field of view.</summary>
    public static readonly float Focal = 0.5f / MathF.Tan(FieldOfView / 2f);

    /// <summary>The game camera's tilt: asin 0.751, its way's drop - MEASURED.</summary>
    public static readonly float GameTilt = MathF.Asin(0.751f);

    /// <summary>
    /// The game camera's turn in this camera's terms: looking along +x +y with +x -y to the right - MEASURED.
    /// </summary>
    /// <remarks>
    /// Worked from <see cref="Turning"/>: the way into the picture is (sin t cos l, -cos t cos l, sin l) for a turn t and tilt l, which is
    /// the game's (0.467, 0.467, 0.751) at t = 135 degrees; the picture's x is then (0.707, -0.707, 0), the game's clip x (1.105, -1.105, 0).
    /// </remarks>
    public const float GameTurn = 3f * MathF.PI / 4f;

    /// <summary>How far the game's camera stands off the player, in world units - MEASURED, Port's capture of v0.1.319.</summary>
    public const float GameDistance = 2165f;

    /// <summary>The closest the eye may come to the focus, in model units: close enough for a barrel to fill the picture several times over.</summary>
    public const float Closest = 2f;

    /// <summary>How far the eye may stand from the focus, in lengths of the model's box.</summary>
    public const float Farthest = 60f;

    /// <summary>The steepest tilt either way: straight down, or straight up.</summary>
    public const float Steepest = MathF.PI / 2f;

    /// <summary>Whether the pose can draw anything: a finite focus and angles, and a distance past nought.</summary>
    public bool Valid
        => float.IsFinite(Focus.X) && float.IsFinite(Focus.Y) && float.IsFinite(Focus.Z) && float.IsFinite(Turn) && float.IsFinite(Tilt)
           && Distance > 0f && float.IsFinite(Distance);

    /// <summary>The way into the picture in the model's space.</summary>
    public Vector3 Forward
    {
        get
        {
            Matrix4x4 turning = Turning(Turn, Tilt);
            return new Vector3(turning.M13, turning.M23, turning.M33);
        }
    }

    /// <summary>The picture's rightward way in the model's space.</summary>
    public Vector3 Right
    {
        get
        {
            Matrix4x4 turning = Turning(Turn, Tilt);
            return new Vector3(turning.M11, turning.M21, turning.M31);
        }
    }

    /// <summary>The picture's downward way in the model's space.</summary>
    public Vector3 Down
    {
        get
        {
            Matrix4x4 turning = Turning(Turn, Tilt);
            return new Vector3(turning.M12, turning.M22, turning.M32);
        }
    }

    /// <summary>Where the eye stands, in the model's space.</summary>
    public Vector3 Eye => Focus - (Forward * Distance);

    /// <summary>The depth from the eye under which nothing is drawn: a hundredth of the distance, kept between a twentieth and five units.</summary>
    /// <remarks>
    /// CHOSEN. The card's depth is reversed and a float, so its precision is the same at every depth and the near plane can sit close; it is
    /// kept from nought only so a triangle through the eye cannot divide by it, and from growing past five so an eye standing far from its
    /// focus can still fly up to a wall.
    /// </remarks>
    public float Near => Math.Clamp(Distance * 0.01f, 0.05f, 5f);

    /// <summary>Model units per share of the picture's side at the focus's depth - how far a drag across the whole picture moves the focus.</summary>
    public float Across => Distance / Focal;

    /// <summary>
    /// The camera's turn without its place: the model's space turned over in x (the game's frame is left-handed), turned, tilted and stood up - MeshPicture.Camera's.
    /// </summary>
    public static Matrix4x4 Turning(float turn, float tilt)
        => Matrix4x4.CreateScale(-1f, 1f, 1f)
            * Matrix4x4.CreateRotationZ(turn)
            * Matrix4x4.CreateRotationX(tilt)
            * Matrix4x4.CreateRotationX(-MathF.PI / 2f);

    /// <summary>
    /// The pose a model starts at: its box's middle looked at from the distance that fills the picture as the fitted view did, turned and tilted so.
    /// </summary>
    /// <param name="mesh">The model. Nothing, or a mesh with no box, gives a pose that is not <see cref="Valid"/>.</param>
    /// <param name="turn">The turn.</param>
    /// <param name="tilt">The tilt.</param>
    /// <param name="parallel">Whether it is drawn without perspective.</param>
    /// <param name="zoom">How much closer than the fitted view - the old camera's zoom.</param>
    /// <param name="pan">Where the box's middle sits, as a share of the picture off its middle - the old camera's pan.</param>
    public static CameraPose Fitted(SkinnedMesh? mesh, float turn, float tilt, bool parallel, float zoom = 1f, Vector2 pan = default)
    {
        if (mesh is not { Ready: true })
        {
            return default;
        }

        Vector3 middle = (mesh.Least + mesh.Most) * 0.5f;
        float reach = Reach(mesh);
        if (!(reach > 0f) || !(zoom > 0f))
        {
            return default;
        }

        // THE FITTED SCALE, Fill times the zoom over the reach, is the focus plane's Focal over the distance.
        float distance = Focal * reach / (MeshPicture.Fill * zoom);
        var pose = new CameraPose(middle, turn, Math.Clamp(tilt, -Steepest, Steepest), distance, parallel);

        // A PAN MOVED THE MIDDLE OFF THE PICTURE'S: the focus moves the other way by as much.
        return pan == default || !float.IsFinite(pan.X) || !float.IsFinite(pan.Y)
            ? pose
            : pose with { Focus = middle - (((pose.Right * pan.X) + (pose.Down * pan.Y)) * pose.Across) };
    }

    /// <summary>The longest side of a mesh's box - what the fitted view fills the picture with.</summary>
    public static float Reach(SkinnedMesh mesh)
    {
        ArgumentNullException.ThrowIfNull(mesh);
        Vector3 span = Vector3.Abs(mesh.Most - mesh.Least);
        return MathF.Max(span.X, MathF.Max(span.Y, span.Z));
    }

    /// <summary>The pose turned and tilted by so much about its focus, the tilt held between straight down and straight up and the turn within one lap.</summary>
    public CameraPose Orbited(float turn, float tilt)
    {
        float turned = Turn + turn;
        if (turned > MathF.PI || turned < -MathF.PI)
        {
            // Kept within one lap, so an orbit left running for an evening does not walk the angle out to where a float can no longer
            // tell one frame from the next.
            turned -= MathF.Tau * MathF.Floor((turned + MathF.PI) / MathF.Tau);
        }

        return this with { Turn = turned, Tilt = Math.Clamp(Tilt + tilt, -Steepest, Steepest) };
    }

    /// <summary>The pose moved across the picture by a drag of so many shares of its side, the scene following the hand at the focus's depth.</summary>
    public CameraPose Panned(Vector2 shares)
        => !float.IsFinite(shares.X) || !float.IsFinite(shares.Y)
            ? this
            : this with { Focus = Focus - (((Right * shares.X) + (Down * shares.Y)) * Across) };

    /// <summary>
    /// The pose brought nearer to a point by so much, everything about it scaled towards that point - which stays where it is on the picture.
    /// </summary>
    /// <param name="about">The point - the surface under the pointer, or where the pointer meets the focus's plane.</param>
    /// <param name="factor">How much of the distance is left: under one nearer, over one further.</param>
    /// <param name="farthest">The furthest the eye may then stand from the focus.</param>
    /// <remarks>
    /// A STRETCH ABOUT THE POINT: the eye, the focus and the distance all scaled towards it by the one factor, the angles kept - so the
    /// point stays on the ray it was on, in perspective, and at its place on the picture, in parallel. The focus comes towards the point
    /// with the eye, so after zooming in on a barrel the camera turns about something by the barrel and not about the room's middle. Held
    /// between <see cref="Closest"/> and <paramref name="farthest"/> by shrinking the factor, never by moving off the point.
    /// </remarks>
    public CameraPose Zoomed(Vector3 about, float factor, float farthest)
    {
        if (!(factor > 0f) || !float.IsFinite(factor) || !float.IsFinite(about.X) || !float.IsFinite(about.Y) || !float.IsFinite(about.Z))
        {
            return this;
        }

        float distance = Math.Clamp(Distance * factor, Closest, MathF.Max(Closest, farthest));
        factor = distance / Distance;
        return this with { Focus = about + ((Focus - about) * factor), Distance = distance };
    }

    /// <summary>The pose flown by so much in the model's space, eye and focus together.</summary>
    public CameraPose Flown(Vector3 by)
        => !float.IsFinite(by.X) || !float.IsFinite(by.Y) || !float.IsFinite(by.Z) ? this : this with { Focus = Focus + by };

    /// <summary>
    /// The pose looking at a point from where its eye stands: the point becomes the focus and the camera turns to it - the double click.
    /// </summary>
    /// <remarks>
    /// THE EYE STAYS AND TURNS, rather than flying sideways to put the point in the middle: what was looked at stays in sight while the
    /// picture swings to it. The new tilt and turn are the way from the eye to the point read back through <see cref="Turning"/>'s way
    /// into the picture, (sin t cos l, -cos t cos l, sin l); a point straight below or above keeps the turn it had.
    /// </remarks>
    public CameraPose LookingAt(Vector3 point)
    {
        Vector3 eye = Eye;
        Vector3 way = point - eye;
        float distance = way.Length();
        if (!(distance >= Closest) || !float.IsFinite(distance))
        {
            return this with { Focus = point };
        }

        way /= distance;
        float tilt = MathF.Asin(Math.Clamp(way.Z, -1f, 1f));
        float level = MathF.Sqrt((way.X * way.X) + (way.Y * way.Y));
        float turn = level > 1e-4f ? MathF.Atan2(way.X, -way.Y) : Turn;
        return new CameraPose(point, turn, Math.Clamp(tilt, -Steepest, Steepest), distance, Parallel);
    }

    /// <summary>The pose at the game's own angle and distance, about its focus.</summary>
    public CameraPose AsTheGame() => this with { Turn = GameTurn, Tilt = GameTilt, Distance = GameDistance };

    /// <summary>
    /// Where the pointer meets the focus's plane - the plane through the focus facing the eye - in the model's space.
    /// </summary>
    /// <param name="pointer">The pointer as a share of the picture's side from its top left corner.</param>
    public Vector3 OnFocusPlane(Vector2 pointer)
        => Focus + (((Right * (pointer.X - 0.5f)) + (Down * (pointer.Y - 0.5f))) * Across);

    /// <summary>
    /// The ray through a point of the picture: where it starts and the way it runs, in the model's space.
    /// </summary>
    /// <param name="pointer">The point as a share of the picture's side from its top left corner.</param>
    /// <remarks>
    /// In perspective from the eye through the point; in parallel along the way into the picture, from the focus's plane drawn back past
    /// anything a box the size of the distance can hold in front of it.
    /// </remarks>
    public (Vector3 From, Vector3 Way) Ray(Vector2 pointer)
    {
        Vector3 forward = Forward;
        if (Parallel)
        {
            return (OnFocusPlane(pointer) - (forward * (Distance * Farthest)), forward);
        }

        Vector3 way = forward + (((Right * (pointer.X - 0.5f)) + (Down * (pointer.Y - 0.5f))) / Focal);
        return (Eye, Vector3.Normalize(way));
    }

    /// <summary>
    /// How far the eye is flown in a frame by the keys held: W and S along the view, A and D across it, Q and E down and up the world.
    /// </summary>
    /// <param name="forward">One for W, minus one for S, nought for neither or both.</param>
    /// <param name="across">One for D, minus one for A.</param>
    /// <param name="up">One for E, minus one for Q.</param>
    /// <param name="seconds">The frame's time.</param>
    /// <param name="fast">Whether shift is held: four times as fast.</param>
    /// <remarks>
    /// AT A PACE THAT FOLLOWS THE DISTANCE, CHOSEN: four fifths of the focus's distance a second, so a camera standing over the whole
    /// harbour crosses it in a few seconds and one zoomed onto a barrel creeps along it. Up is minus z in the game's frame.
    /// </remarks>
    public Vector3 Flight(float forward, float across, float up, float seconds, bool fast)
    {
        if (forward == 0f && across == 0f && up == 0f)
        {
            return Vector3.Zero;
        }

        float pace = MathF.Max(Distance, 25f) * 0.8f * (fast ? 4f : 1f) * Math.Clamp(seconds, 0f, 0.25f);
        Vector3 way = (Forward * forward) + (Right * across) - (Vector3.UnitZ * up);
        return way * pace;
    }
}
