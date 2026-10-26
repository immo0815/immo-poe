using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// A wobble core's rig, played: the bone its WobblePoint follows, its matrix at any moment - what sways Port's ship.
/// </summary>
/// <remarks>
/// MEASURED BEFORE BUILT (v0.1.290's capture, Port). The core's type, <c>Metadata/Terrain/Gallows/Act4/4_6/Objects/BoatWobbleCore.ot</c>,
/// carries <c>Animated { animated_object = ".../BoatnautWobbleCore_small.ao" }</c> and <c>WobblePoint { sector = "caravan"; bone =
/// "caravan_jntBnd" }</c>; the .ao names a rig (<c>Boatnaut/BoatWobbleRig/rig.ast</c>), <c>default_animation = "idle_small_01"</c> and
/// <c>randomise_start_position = true</c>. Watched for twenty seconds, the WobblePoint held a 4x4 matrix, row vectors, that is a pure
/// turn about one fixed point - (4162, 12473, -328) in Port's world, the residual under half a unit against a swing of 140 - tilting
/// up to 0.37 degrees, the swings 1.9 to 2.4 s apart and their sizes repeating after about 12.9 s: an authored loop, not a sine.
/// So the matrix is read here as the bone's own: where the animation puts it against where it rests, <c>inverse(bindModel) *
/// world</c>, in the core's model frame - SkeletonPose's skin matrix for that bone. Whether that is the engine's exact reading is
/// what <c>wobble-files.txt</c> holds against the watch: this one's matrix over the loop beside the game's.
/// </remarks>
public sealed class WobbleRig
{
    /// <summary>The block of an object type that makes it a wobble core.</summary>
    public const string Block = "WobblePoint";

    /// <summary>The rate an animation whose header says none plays at - the portrait's.</summary>
    private const float UsualRate = 30f;

    /// <summary>
    /// The install's own Oodle decoder - its GameFiles.Unpack, handed over as the install opens - or null for the shipped managed one.
    /// </summary>
    /// <remarks>
    /// WHY HANDED OVER AND NOT PASSED DOWN: v0.1.291 unpacked the rig with <c>Oodle.Decompress</c>, the managed fallback, and it does not
    /// read BoatWobbleRig/rig.ast's keyframes ("its keyframes did not unpack", the next Port capture) - while the Monster Book plays
    /// every monster through the install's decoder. A room is built many calls down from anything that holds the install, as the
    /// water's lookups are drawn (ShadeProgram.Use), so the decoder comes the same way: once, as the install opens.
    /// </remarks>
    public static Func<ReadOnlyMemory<byte>, int, byte[]?>? Unpacker { get; set; }

    /// <summary>The decoder a rig is unpacked with: the install's where handed over, else the managed one.</summary>
    public static Func<ReadOnlyMemory<byte>, int, byte[]?> Unpacking => Unpacker ?? Oodle.Decompress;

    private readonly SkeletonPose _pose;
    private readonly AnimationTracks _tracks;
    private readonly object _gate = new();

    private WobbleRig(
        string type, string ao, string sector, string bone, string skeleton, SkeletonAnimation animation, int index, SkeletonPose pose, AnimationTracks tracks)
    {
        Type = type;
        Ao = ao;
        Sector = sector;
        Bone = bone;
        Skeleton = skeleton;
        Animation = animation;
        BoneIndex = index;
        _pose = pose;
        _tracks = tracks;
        Rate = animation.Rate > 0 ? animation.Rate : UsualRate;
        Seconds = tracks.Frames / Rate;
        Rest = pose.BindModel[index];
    }

    /// <summary>The core's object type, as a room line names it.</summary>
    public string Type { get; }

    /// <summary>The .ao its Animated block names.</summary>
    public string Ao { get; }

    /// <summary>The WobblePoint's sector.</summary>
    public string Sector { get; }

    /// <summary>The bone the WobblePoint follows.</summary>
    public string Bone { get; }

    /// <summary>The rig, as the .ao names it.</summary>
    public string Skeleton { get; }

    /// <summary>The animation played - the .ao's default one.</summary>
    public SkeletonAnimation Animation { get; }

    /// <summary>The bone's index in the rig.</summary>
    public int BoneIndex { get; }

    /// <summary>Frames a second.</summary>
    public float Rate { get; }

    /// <summary>The loop's length.</summary>
    public float Seconds { get; }

    /// <summary>The bone at rest, in the core's model frame - its translation is the point the sway turns about.</summary>
    public Matrix4x4 Rest { get; }

    /// <summary>
    /// The core's rig played, or null with why: the type's WobblePoint, its .ao's rig and default animation, the bone named.
    /// </summary>
    /// <param name="read">How to get a file out of the install.</param>
    /// <param name="unpack">How to undo Oodle, for a rig that keeps its keyframes in a bundle.</param>
    /// <param name="type">The core's object type, as a room line names it - without its extension.</param>
    public static WobbleRig? Of(Func<string, byte[]?> read, Func<ReadOnlyMemory<byte>, int, byte[]?>? unpack, string type, out string why)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(type);
        string path = type.Replace('\\', '/').Trim();
        if (Point(read, path) is not { } point)
        {
            why = $"{path}.ot does not read, or names no {Block} bone";
            return null;
        }

        (string bone, string sector, string ao) = point;

        if (ao.Length == 0 || read(ao) is not { Length: > 0 } aoBytes)
        {
            why = $"{path}.ot's animated object ({(ao.Length > 0 ? ao : "none named")}) does not read";
            return null;
        }

        List<AnimatedObject> chain = ModelDump.Whole(read, AnimatedObject.Read(aoBytes));
        string skeleton = ModelDump.Entryed(chain, "ClientAnimationController", "skeleton");
        string named = ModelDump.Entryed(chain, "AnimationController", "default_animation");
        AnimationSkeleton rig = AnimationSkeleton.Read(skeleton.Length > 0 ? read(skeleton) : null);
        if (!rig.Ready)
        {
            why = $"{ao}'s rig ({(skeleton.Length > 0 ? skeleton : "none named")}) does not read: {rig.Why}";
            return null;
        }

        int index = -1;
        for (var one = 0; one < rig.Bones.Count; one++)
        {
            if (string.Equals(rig.Bones[one].Name, bone, StringComparison.OrdinalIgnoreCase))
            {
                index = one;
                break;
            }
        }

        if (index < 0)
        {
            why = $"{skeleton} has no bone named {bone}";
            return null;
        }

        int which = -1;
        for (var one = 0; one < rig.Animations.Count; one++)
        {
            if (string.Equals(rig.Animations[one].Name, named, StringComparison.OrdinalIgnoreCase))
            {
                which = one;
                break;
            }
        }

        if (which < 0)
        {
            why = $"{skeleton} has no animation named {(named.Length > 0 ? named : "(none named)")}";
            return null;
        }

        SkeletonAnimation animation = rig.Animations[which];
        if (!rig.Loose && unpack is null)
        {
            why = $"{skeleton} keeps its keyframes in a bundle, and there is no Oodle here to unpack them";
            return null;
        }

        byte[]? frames = rig.Tracks(animation, unpack ?? ((_, _) => null));
        AnimationTracks tracks = frames is null ? AnimationTracks.None : AnimationTracks.Read(frames, animation.Tracks, rig.Version);
        if (!tracks.Ready || tracks.Frames <= 0)
        {
            // WHICH DECODER, AND WHAT IT SAID: v0.1.291's line said only that the keyframes did not unpack, with the managed decoder.
            string decoder = Unpacker is null ? "the shipped managed decoder" : "the install's decoder";
            string refused = Oodle.LastRefusal.Length > 0 ? $" - the managed decoder's last refusal: {Oodle.LastRefusal}" : string.Empty;
            why = $"{skeleton}'s {animation.Name} does not read: {(frames is null ? $"its keyframes did not unpack with {decoder}{refused}" : tracks.Why)}";
            return null;
        }

        if (SkeletonPose.Of(rig) is not { } pose)
        {
            why = $"{skeleton}'s bones do not hold together";
            return null;
        }

        why = string.Empty;
        return new WobbleRig(path, ao, sector, bone, skeleton, animation, index, pose, tracks);
    }

    /// <summary>
    /// What an object type's WobblePoint names - its bone, its sector and the .ao its Animated block names - or null where its <c>.ot</c> carries no WobblePoint with a bone.
    /// </summary>
    public static (string Bone, string Sector, string Ao)? Point(Func<string, byte[]?> read, string type)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(type);
        if (read(type.Replace('\\', '/').Trim() + ".ot") is not { Length: > 0 } bytes)
        {
            return null;
        }

        List<AnimatedObject> types = ModelDump.Whole(read, AnimatedObject.Read(bytes));
        string bone = ModelDump.Entryed(types, Block, "bone");
        return bone.Length == 0 ? null : (bone, ModelDump.Entryed(types, Block, "sector"), ModelDump.Entryed(types, "Animated", "animated_object"));
    }

    /// <summary>
    /// The bone's sway at a moment, in the core's model frame: where the animation puts it against where it rests, row vectors.
    /// </summary>
    /// <param name="seconds">Seconds into the loop; any value, wrapped.</param>
    public Matrix4x4 At(float seconds)
    {
        float frame = (seconds * Rate) % _tracks.Frames;
        if (frame < 0)
        {
            frame += _tracks.Frames;
        }

        // ONE POSE OBJECT, its arrays reused - so one caller at a time.
        lock (_gate)
        {
            _pose.Take(_tracks, frame);
            return _pose.Skin[BoneIndex];
        }
    }

    /// <summary>The rig's say for a capture: what was read, and why each thing.</summary>
    public string Said() => string.Create(
        CultureInfo.InvariantCulture,
        $"{Type}: sector {Sector}, bone {Bone} (#{BoneIndex}) of {Skeleton}, animation {Animation.Name} - {_tracks.Frames:0.##} frames at {Rate:0.##} a second, {Seconds:0.###} s; the bone at rest {Rest.Translation.X:0.###} {Rest.Translation.Y:0.###} {Rest.Translation.Z:0.###}");

    /// <summary>
    /// Whether an object type may be swayed: true unless its <c>.ot</c>, or a file it extends, sets <c>no_wobble</c> - as
    /// <c>DoodadNoWobble.ot</c> does in its construction event (<c>this.no_wobble = true;</c>), a line the game's program names.
    /// </summary>
    public static bool Sways(Func<string, byte[]?> read, string type)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(type);
        string path = type.Replace('\\', '/').Trim();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var hop = 0; hop < 12 && path.Length > 0 && seen.Add(path); hop++)
        {
            if (read(path + ".ot") is not { Length: > 0 } bytes)
            {
                return true;
            }

            string text = StatDescriptionFiles.Decode(bytes);
            if (text.Contains("no_wobble = true", StringComparison.OrdinalIgnoreCase) || text.Contains("no_wobble=true", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            path = Extends(text);
        }

        return true;
    }

    /// <summary>The file an object type's text extends, or empty for none.</summary>
    private static string Extends(string text)
    {
        foreach (string line in text.Split('\n'))
        {
            string trimmed = line.Trim();
            if (!trimmed.StartsWith("extends", StringComparison.Ordinal))
            {
                continue;
            }

            int open = trimmed.IndexOf('"', StringComparison.Ordinal);
            int close = open < 0 ? -1 : trimmed.IndexOf('"', open + 1);
            string parent = close > open ? trimmed[(open + 1)..close] : string.Empty;
            return parent.Equals("nothing", StringComparison.OrdinalIgnoreCase) ? string.Empty : parent;
        }

        return string.Empty;
    }
}
