using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>A piece's cloth as a model swings it: the cloth, its bone links numbered in the model's rig, the bone the piece hangs from.</summary>
/// <param name="Cloth">The cloth.</param>
/// <param name="Socket">The model's bone the piece hangs from rigidly - its simulated bones' stand-in where nothing swings.</param>
/// <param name="Source">The piece's .ao, for the lines.</param>
public sealed record ClothSwing(ClothRig Cloth, int Socket, string Source);

/// <summary>
/// The cloths of one model stepped with its clock: once a frame, as the engine steps them (ClothWorld), each simulated bone's skin matrix
/// then set into the pose the frame took.
/// </summary>
/// <remarks>
/// ONE STEP A FRAME, AS THE GAME TAKES IT (module+0x132F6C0, read v0.1.421): the time the book's clock ran on since the last pose is the
/// frame's time, smoothed by the world as the system smooths it. The targets are the pose's own - the engine skins them from the pose of
/// its frame. CHOSEN, as the measured lamp's swinger chose before: another animation, the clock moved by hand or an animation starting over
/// is no motion of the bones that carry the cloth - every particle stands on its target again.
/// </remarks>
public sealed class ClothSwinger
{
    /// <summary>The most seconds a frame steps - a stalled frame does not take a step of seconds.</summary>
    public const double MostDue = 0.5;

    /// <summary>How far from where the clock reached an animation's frame may stand before it is a jump, in frames.</summary>
    private const float JumpFrames = 0.25f;

    private readonly Matrix4x4[][] _unbind;
    private ClothWorld _world;
    private Matrix4x4[] _skin = [];
    private AnimationTracks? _tracks;
    private float _frame = float.NaN;
    private double _due;

    /// <summary>A swinger of a model's cloths, every particle on its target at its first pose.</summary>
    public ClothSwinger(IReadOnlyList<ClothSwing> swings)
    {
        ArgumentNullException.ThrowIfNull(swings);
        Swings = swings;
        _unbind = [.. swings.Select(one => one.Cloth.Links.Select(link => Matrix4x4.Invert(link.Bind, out Matrix4x4 back) ? back : Matrix4x4.Identity).ToArray())];
        _world = Made();
    }

    /// <summary>The model's cloths.</summary>
    public IReadOnlyList<ClothSwing> Swings { get; }

    /// <summary>The steps taken since the last start, for the line under the picture.</summary>
    public long Steps => _world.Steps;

    /// <summary>The world, for the lines and the tests.</summary>
    public ClothWorld World => _world;

    /// <summary>The animation's clock ran on by <paramref name="seconds"/> while it played.</summary>
    public void Due(double seconds)
    {
        if (seconds > 0 && double.IsFinite(seconds))
        {
            _due = Math.Min(_due + seconds, MostDue);
        }
    }

    /// <summary>
    /// Steps the cloths by the time the clock ran on and sets every simulated bone's skin matrix into the pose taken at <paramref name="frame"/>;
    /// with <paramref name="swinging"/> off, every simulated bone hangs rigidly from its piece's socket as authored.
    /// </summary>
    public void Swing(SkeletonPose pose, AnimationTracks tracks, float frame, float rate, float frames, bool swinging)
    {
        ArgumentNullException.ThrowIfNull(pose);
        ArgumentNullException.ThrowIfNull(tracks);
        IReadOnlyList<Matrix4x4> skin = pose.Skin;
        if (!swinging)
        {
            foreach (ClothSwing swing in Swings)
            {
                Matrix4x4 socket = swing.Socket >= 0 && swing.Socket < skin.Count ? skin[swing.Socket] : Matrix4x4.Identity;
                foreach (ClothBoneLink link in swing.Cloth.Links)
                {
                    pose.Override(link.Bone, socket);
                }
            }

            // OFF STARTS OVER: turned on again, every particle stands on its target.
            Restart();
            return;
        }

        float reached = Wrapped(_frame + (float)(_due * Math.Max(rate, 0f)), frames);
        if (!ReferenceEquals(tracks, _tracks) || float.IsNaN(_frame) || Apart(reached, frame, frames) > JumpFrames)
        {
            Restart();
        }

        if (_skin.Length != skin.Count)
        {
            _skin = new Matrix4x4[skin.Count];
        }

        for (var bone = 0; bone < _skin.Length; bone++)
        {
            _skin[bone] = skin[bone];
        }

        _world.Frame((float)_due, _skin);
        _due = 0;
        _tracks = tracks;
        _frame = frame;
        for (var one = 0; one < Swings.Count; one++)
        {
            IReadOnlyList<ClothBoneLink> links = Swings[one].Cloth.Links;
            for (var each = 0; each < links.Count; each++)
            {
                ClothBoneLink link = links[each];
                pose.Override(link.Bone, _unbind[one][each] * _world.Bone(one, link));
            }
        }
    }

    private void Restart()
    {
        _world = Made();
        _frame = float.NaN;
        _due = 0;
    }

    private ClothWorld Made() => new([.. Swings.Select(one => one.Cloth)], [], [], []);

    /// <summary>A frame wrapped into the animation as the book's clock wraps it - past the end, modulo its length.</summary>
    private static float Wrapped(float frame, float frames) => frames > 0f && frame > frames ? frame % frames : frame;

    /// <summary>How far apart two frames of a looping animation stand, the shorter way round.</summary>
    private static float Apart(float one, float other, float frames)
    {
        float gap = MathF.Abs(one - other);
        return frames > 0f ? MathF.Min(gap, MathF.Abs(frames - gap)) : gap;
    }
}
