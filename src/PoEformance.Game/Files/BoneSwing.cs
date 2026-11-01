using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// A bone the tool swings as the engine's own simulation swings it: a rigid rod hung at its parent's rest point, its end at its own rest,
/// carried by the bone its piece hangs from - the Exile's Staff's lamp.
/// </summary>
/// <param name="Bone">The bone appended to the rig for it (AnimationSkeleton.Swinging): at the identity, its skin matrix set by the swing.</param>
/// <param name="Socket">The rig's bone the piece hangs from rigidly - whose skin matrix carries the rod's top.</param>
/// <param name="Pivot">Where the rod hangs from at the bind pose, in the model's space: the simulated bone's parent's rest through the piece's place.</param>
/// <param name="Rest">Where the rod's end stands at the bind pose: the simulated bone's own rest through the piece's place.</param>
/// <param name="Name">The simulated bone as its own rig names it.</param>
/// <param name="Source">The piece's .ao, for the line under the picture.</param>
public sealed record BoneSwing(int Bone, int Socket, Vector3 Pivot, Vector3 Rest, string Name, string Source)
{
    /// <summary>
    /// The skin matrix that swings the rod's vertices from where the socket's skin puts them to hang along <paramref name="offset"/>: the
    /// socket's skin, then the shortest turn about the rod's top that takes the rod as the pose holds it onto the swung one.
    /// </summary>
    /// <param name="socketSkin">The socket's skin matrix in the pose.</param>
    /// <param name="offset">The rod's end from its top, as the swing holds it.</param>
    /// <remarks>
    /// THE SHORTEST TURN, CHOSEN: the rod's twist about its own length is in no measurement - the game's light list holds the lamp's place
    /// alone - and a lantern is nearly round about its chain.
    /// </remarks>
    public Matrix4x4 Swung(Matrix4x4 socketSkin, Vector3 offset)
    {
        Vector3 top = Vector3.Transform(Pivot, socketSkin);
        Vector3 rod = Vector3.Transform(Rest, socketSkin) - top;
        Quaternion turn = Pendulum.Between(rod, offset);
        return socketSkin * Matrix4x4.CreateTranslation(-top) * Matrix4x4.CreateFromQuaternion(turn) * Matrix4x4.CreateTranslation(top);
    }
}

/// <summary>
/// The engine's pendulum for a lamp on a staff, stepped tick by tick as MEASURED off the game's own light list.
/// </summary>
/// <remarks>
/// MEASURED (2026-11-01 09:29, Hunting Grounds, v0.1.414's bone watch; the Exile's Staff, WandererStaffHeld.ao): the lamp's light stands in the
/// renderer's list every tick, 10.1 ms apart, and its bone's place beside the staff's World read the same tick. The lamp is a RIGID ROD from a
/// point 0.06 units off base_lamp_jntBnd's rest, 24.148 of the staff's units long (its rest's 24, 0.6% short). Where the staff stood still,
/// 81 ticks of its free swing are a Verlet step in the world - the last tick's motion kept at <see cref="Keep"/>, gravity <see cref="Fall"/>
/// down (plus z), the rod's length held - to R² 0.9995. Where the staff moves, the rod's top carries the lamp along whole, and the lamp is
/// kicked against the top's acceleration A TICK LATE by <see cref="Inertia"/> of it: over the ticks on which he did not turn, a tick's miss
/// 0.076 units at the median, and runs of thirty ticks ending 6.2 degrees off (none of the kick 33, all of it 43).
/// NOT MODELLED: his turning (the lamp follows his facing more than a carried rod would, by no one fraction), the bend stiffness that tilts
/// the resting lamp 2 to 7.5 degrees towards its rest's way, the twist about the rod.
/// </remarks>
public sealed class Pendulum
{
    /// <summary>The seconds between two steps - the game's tick in the capture measured (10.1 ms, 99 a second).</summary>
    public const double Tick = 0.0101;

    /// <summary>The share of the last tick's motion a step keeps - MEASURED.</summary>
    public const float Keep = 0.98076f;

    /// <summary>Gravity a tick squared over the rod's length - MEASURED: 0.0625 world units on a rod of 22.3368 (611 units a second squared).</summary>
    public const float Fall = 0.0625f / 22.3368f;

    /// <summary>The share of the rod's top's acceleration, a tick late, the lamp is kicked against - MEASURED.</summary>
    public const float Inertia = 0.45f;

    private Vector3 _now, _before;
    private Vector3 _top, _topBefore, _topBefore2;

    /// <summary>The rod's end from its top, as of the last step.</summary>
    public Vector3 Offset => _now;

    /// <summary>Hangs it at rest straight down from <paramref name="top"/>, its top's history there - the start of a picture, CHOSEN.</summary>
    public void Hang(Vector3 top, float length)
    {
        _now = _before = new Vector3(0f, 0f, length);
        _top = _topBefore = _topBefore2 = top;
    }

    /// <summary>
    /// Its top's history set where the top stands now, its swing kept: a jump of the animation - another one chosen, the clock moved by hand,
    /// one starting over - is no motion of the hand that carries it.
    /// </summary>
    public void Settle(Vector3 top) => _top = _topBefore = _topBefore2 = top;

    /// <summary>
    /// Stands it where reads put it - the rod now and a tick before, its top's last three places - to step on from: the game's own lamp held
    /// against the steps (BoneSwingTests).
    /// </summary>
    public void Seed(Vector3 now, Vector3 before, Vector3 top, Vector3 topBefore, Vector3 topBefore2)
    {
        (_now, _before) = (now, before);
        (_top, _topBefore, _topBefore2) = (top, topBefore, topBefore2);
    }

    /// <summary>One tick: the rod's top where it stands at the new tick, and the rod's length as the pose holds it.</summary>
    public void Step(Vector3 top, float length)
    {
        Vector3 kick = (_top - _topBefore) - (_topBefore - _topBefore2);
        Vector3 next = _now + (Keep * (_now - _before)) - (Inertia * kick) + new Vector3(0f, 0f, Fall * length);
        float reach = next.Length();
        next = reach > 1e-6f ? next * (length / reach) : new Vector3(0f, 0f, length);
        _before = _now;
        _now = next;
        _topBefore2 = _topBefore;
        _topBefore = _top;
        _top = top;
    }

    /// <summary>The shortest turn taking the way of <paramref name="from"/> onto the way of <paramref name="to"/>; none where either is nothing.</summary>
    public static Quaternion Between(Vector3 from, Vector3 to)
    {
        float lengths = from.Length() * to.Length();
        if (lengths < 1e-12f)
        {
            return Quaternion.Identity;
        }

        float dot = Vector3.Dot(from, to) / lengths;
        if (dot > 1f - 1e-7f)
        {
            return Quaternion.Identity;
        }

        if (dot < -1f + 1e-6f)
        {
            // HALF A TURN about any axis across the rod.
            Vector3 across = Vector3.Cross(from, Vector3.UnitX);
            if (across.LengthSquared() < 1e-6f * from.LengthSquared())
            {
                across = Vector3.Cross(from, Vector3.UnitY);
            }

            across = Vector3.Normalize(across);
            return new Quaternion(across, 0f);
        }

        Vector3 axis = Vector3.Cross(from, to) / lengths;
        return Quaternion.Normalize(new Quaternion(axis, 1f + dot));
    }
}

/// <summary>
/// The swings of one model, stepped with its animation's clock: each tick's hand worked out at that tick's own moment (SkeletonPose.SkinAt),
/// each lamp hung on it, and the swung bones' skin matrices set into the pose the frame took.
/// </summary>
public sealed class BoneSwinger
{
    /// <summary>The most seconds stepped at once - a stalled frame does not loose a hundred ticks on the next.</summary>
    public const double MostDue = 0.5;

    /// <summary>How far from where the steps reached an animation's frame may stand before it is a jump, in frames.</summary>
    private const float JumpFrames = 0.25f;

    private readonly Pendulum[] _bobs;
    private AnimationTracks? _tracks;
    private float _frame = float.NaN;
    private double _due;
    private bool _hung;

    /// <summary>A swinger for a model's swings, every lamp hung straight down at its first pose.</summary>
    public BoneSwinger(IReadOnlyList<BoneSwing> swings)
    {
        ArgumentNullException.ThrowIfNull(swings);
        Swings = swings;
        _bobs = new Pendulum[swings.Count];
        for (var one = 0; one < _bobs.Length; one++)
        {
            _bobs[one] = new Pendulum();
        }
    }

    /// <summary>The model's swings.</summary>
    public IReadOnlyList<BoneSwing> Swings { get; }

    /// <summary>How many ticks have been stepped, for the line under the picture.</summary>
    public long Ticks { get; private set; }

    /// <summary>Each lamp's rod as the swing holds it, in the model's space, for the line and the tests.</summary>
    public Vector3 OffsetOf(int swing) => _bobs[swing].Offset;

    /// <summary>The animation's clock ran on by <paramref name="seconds"/> while it played.</summary>
    public void Due(double seconds)
    {
        if (seconds > 0 && double.IsFinite(seconds))
        {
            _due = Math.Min(_due + seconds, MostDue);
        }
    }

    /// <summary>
    /// Steps every whole tick the clock ran on, each at its own frame of the animation, and sets each swung bone's skin matrix into the pose
    /// taken at <paramref name="frame"/>; with <paramref name="swinging"/> off, each swung bone hangs rigidly from its socket as authored.
    /// </summary>
    /// <param name="pose">The pose, just taken at <paramref name="frame"/> of <paramref name="tracks"/>.</param>
    /// <param name="tracks">The animation's keyframes.</param>
    /// <param name="frame">Where the pose was taken, in frames.</param>
    /// <param name="rate">The animation's frames a second.</param>
    /// <param name="frames">Its length in frames, where it starts over.</param>
    /// <param name="swinging">Whether the lamps swing, the light panel's switch.</param>
    public void Swing(SkeletonPose pose, AnimationTracks tracks, float frame, float rate, float frames, bool swinging)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(tracks);
        IReadOnlyList<Matrix4x4> skin = pose.Skin;
        if (!swinging)
        {
            foreach (BoneSwing one in Swings)
            {
                pose.Override(one.Bone, Socketed(skin, one.Socket));
            }

            // OFF STARTS OVER: turned on again, the lamps hang down from the pose they find.
            _frame = float.NaN;
            _due = 0;
            _hung = false;
            return;
        }

        float step = (float)(Pendulum.Tick * Math.Max(rate, 0f));
        float reached = Wrapped(_frame + (float)(_due * Math.Max(rate, 0f)), frames);
        bool jumped = !ReferenceEquals(tracks, _tracks) || float.IsNaN(_frame) || !_hung || Apart(reached, frame, frames) > JumpFrames;
        if (jumped)
        {
            for (var one = 0; one < _bobs.Length; one++)
            {
                (Vector3 top, float length) = Rod(Swings[one], Socketed(skin, Swings[one].Socket));
                if (_hung)
                {
                    _bobs[one].Settle(top);
                }
                else
                {
                    _bobs[one].Hang(top, length);
                }
            }

            _hung = true;
            _tracks = tracks;
            _frame = frame;
            _due = 0;
        }
        else
        {
            for (; _due >= Pendulum.Tick; _due -= Pendulum.Tick)
            {
                float at = _frame + step;
                bool over = frames > 0f && at > frames;
                at = Wrapped(at, frames);
                for (var one = 0; one < _bobs.Length; one++)
                {
                    BoneSwing swing = Swings[one];
                    (Vector3 top, float length) = Rod(swing, pose.SkinAt(tracks, at, swing.Socket));

                    // AN ANIMATION STARTING OVER IS NO MOTION of the hand, CHOSEN: the book plays every animation round, one-shots too.
                    if (over)
                    {
                        _bobs[one].Settle(top);
                    }

                    _bobs[one].Step(top, length);
                }

                _frame = at;
                Ticks++;
            }
        }

        for (var one = 0; one < _bobs.Length; one++)
        {
            BoneSwing swing = Swings[one];
            pose.Override(swing.Bone, swing.Swung(Socketed(skin, swing.Socket), _bobs[one].Offset));
        }
    }

    /// <summary>The rod's top and length through a socket's skin matrix.</summary>
    private static (Vector3 Top, float Length) Rod(BoneSwing swing, Matrix4x4 skin)
    {
        Vector3 top = Vector3.Transform(swing.Pivot, skin);
        return (top, (Vector3.Transform(swing.Rest, skin) - top).Length());
    }

    private static Matrix4x4 Socketed(IReadOnlyList<Matrix4x4> skin, int socket)
        => socket >= 0 && socket < skin.Count ? skin[socket] : Matrix4x4.Identity;

    /// <summary>A frame wrapped into the animation as the book's clock wraps it - past the end, modulo its length.</summary>
    private static float Wrapped(float frame, float frames) => frames > 0f && frame > frames ? frame % frames : frame;

    /// <summary>How far apart two frames of a looping animation stand, the shorter way round.</summary>
    private static float Apart(float one, float other, float frames)
    {
        float gap = MathF.Abs(one - other);
        return frames > 0f ? MathF.Min(gap, MathF.Abs(frames - gap)) : gap;
    }
}
