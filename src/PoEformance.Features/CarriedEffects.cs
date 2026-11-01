using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>One bone of an effect's bone group: the bone of the model's rig that carries it and where it stands at the bind pose, model space.</summary>
/// <param name="Bone">The rig's bone whose skin matrix moves it - a rider's after his, a swung bone appended; -1 for none.</param>
/// <param name="Rest">Where it stands at the bind pose, in the model's own space.</param>
/// <param name="Name">The bone or socket as the group names it.</param>
public readonly record struct CarriedPoint(int Bone, Vector3 Rest, string Name);

/// <summary>
/// One particle effect a model carries for as long as it stands - an .ao's <c>continuous_effect</c> - on one of its bone groups.
/// </summary>
/// <param name="Effect">The .pet it names.</param>
/// <param name="Group">The bone group it is emitted along.</param>
/// <param name="Points">The group's bones in its own order - the engine's emitter_bones_data, each w the distance along them.</param>
/// <param name="Frame">The bone carrying the piece the effect is written in - its socket's - whose frame is the emitter's; -1 for the model's own.</param>
/// <param name="Place">The piece's place in the model's space at the bind pose (its frame there); the identity for the model's own effects.</param>
/// <param name="Source">The .ao it came from.</param>
public sealed record CarriedEffect(string Effect, string Group, IReadOnlyList<CarriedPoint> Points, int Frame, Matrix4x4 Place, string Source);

/// <summary>
/// The particle effects a monster and his pieces carry on their bones: each .ao's ParticleEffects <c>continuous_effect = "&lt;group&gt; &lt;.pet&gt;"</c>
/// on its BoneGroups' <c>bone_group = "&lt;group&gt; &lt;bool&gt; &lt;bones…&gt;"</c>, the bones put on the rig that moves them as CarriedLights puts a light.
/// </summary>
/// <remarks>
/// THE EXILE'S STAFF'S LANTERN (v0.1.441, DECIDED from the live client: the engine's GPU particles built, update and render in one go):
/// WandererStaffHeld.ao writes <c>continuous_effect = "lamp Metadata/…/WandererStaff/fx/lamp.pet"</c> on its group <c>lamp</c> -
/// phys_tri_lamp_jntBnd and lamp_01. MEASURED in the game (particle-emitters.txt, v0.1.434-436): the emitter's frame is the held piece's World,
/// its bones each one's place in the piece's MODEL frame, unscaled, w the distance along the group from its first bone (lamp_01 at 9.99).
/// So a rigid piece's effect is carried in its socket bone's frame, and each bone where the pose puts it, brought back into that frame -
/// the swung lamp's bone (MonsterModel's Swaying) carrying it and the socket on it. A name is the piece's own rig's bone, else one of its
/// chain's sockets (BodySockets), carried by the bone it hangs off.
/// NOT READ: an animation's effect events (ParticleEffects' <c>animations</c>), which fire once at a time of a clip.
/// </remarks>
public static class CarriedEffects
{
    /// <summary>The block naming an .ao's effects, and the line naming one that runs as long as the object stands.</summary>
    private const string EffectsBlock = "ParticleEffects", Continuous = "continuous_effect";

    /// <summary>The block naming an .ao's bone groups, and its line.</summary>
    private const string GroupsBlock = "BoneGroups", GroupLine = "bone_group";

    /// <summary>The block naming a rig, and its line - MonsterModel's RigBlock and RigEntry.</summary>
    private const string RigBlock = "ClientAnimationController", RigLine = "skeleton";

    /// <summary>
    /// Where an effect's emitter stands now and where its bones are in its frame: its frame - the piece's place through its socket bone's skin
    /// matrix, then the model's place - and each bone where the pose puts it, brought into that frame, w the distance along the group so far.
    /// </summary>
    /// <param name="effect">The effect.</param>
    /// <param name="skin">The pose's skin matrices (SkeletonPose.Skin), or empty for the bind pose.</param>
    /// <param name="place">Where the model stands in the picture - a stage's ActorPlace, else the identity.</param>
    /// <param name="bones">Where to write the bones, as many as there is room for.</param>
    /// <returns>The emitter's frame in the picture, and how many bones were written.</returns>
    public static (Matrix4x4 World, int Count) At(CarriedEffect effect, IReadOnlyList<Matrix4x4> skin, Matrix4x4 place, Span<Vector4> bones)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(skin);
        Matrix4x4 frame = effect.Frame >= 0 && effect.Frame < skin.Count ? effect.Place * skin[effect.Frame] : effect.Place;
        Matrix4x4 world = frame * place;
        if (!Matrix4x4.Invert(world, out Matrix4x4 into))
        {
            into = Matrix4x4.Identity;
        }

        int count = Math.Min(effect.Points.Count, bones.Length);
        float along = 0f;
        Vector3 last = default;
        for (var at = 0; at < count; at++)
        {
            CarriedPoint one = effect.Points[at];
            Vector3 posed = one.Bone >= 0 && one.Bone < skin.Count ? Vector3.Transform(one.Rest, skin[one.Bone]) : one.Rest;
            Vector3 local = Vector3.Transform(Vector3.Transform(posed, place), into);
            if (at > 0)
            {
                along += Vector3.Distance(local, last);
            }

            bones[at] = new Vector4(local, along);
            last = local;
        }

        return (world, count);
    }

    /// <summary>
    /// The effects gathered as a monster is dressed, the body's first and each piece's as it is hung (CarriedLights.Gathering calls each step);
    /// the framed bones' rests taken from the rig as it finally stands (<see cref="Made"/>).
    /// </summary>
    internal sealed class Gathering(Func<string, byte[]?> read)
    {
        private readonly List<Found> _found = [];
        private int _events, _unboned, _strays, _lost;

        /// <summary>The body's own effects: every one its files' chains carry, each bone on his rig's bone of the name or a socket of his.</summary>
        public void Body(IReadOnlyList<string> files, AnimationSkeleton rig)
        {
            foreach (string file in files)
            {
                AnimatedObject ao = AnimatedObject.Read(read(file.Replace('\\', '/').Trim()));
                if (!ao.Ready)
                {
                    continue;
                }

                List<AnimatedObject> chain = ModelDump.Whole(read, ao);
                foreach ((string group, string effect, string[] names) in Effects(chain))
                {
                    var points = new List<Point>(names.Length);
                    foreach (string name in names)
                    {
                        if (Named(rig, name, chain) is { } framed)
                        {
                            points.Add(framed);
                        }
                        else
                        {
                            _unboned++;
                        }
                    }

                    Add(group, effect, points, -1, Matrix4x4.Identity, file);
                }
            }
        }

        /// <summary>
        /// A piece hung rigidly at a bone: each bone where its own rig rests it (or a socket of its chain), put through its place, carried by
        /// that bone; which of the points found they are, for <see cref="Rebone"/>.
        /// </summary>
        public (int From, int Count) Rigid(IReadOnlyList<AnimatedObject> chain, string path, Matrix4x4 place, int bone)
        {
            int from = Points;
            List<(string Group, string Effect, string[] Names)> effects = Effects(chain);
            if (effects.Count == 0)
            {
                return (from, 0);
            }

            AnimationSkeleton? own = Rig(chain);
            SkeletonPose? rest = SkeletonPose.Of(own);
            IReadOnlyList<BodySocket> sockets = BodySockets.Of(chain, own);
            foreach ((string group, string effect, string[] names) in effects)
            {
                var points = new List<Point>(names.Length);
                foreach (string name in names)
                {
                    if (Rested(own, rest, sockets, name) is { } at)
                    {
                        points.Add(new Point(bone, Vector3.Transform(at.Position, place), Framed: false, name, at.Parent));
                    }
                    else
                    {
                        _unboned++;
                    }
                }

                Add(group, effect, points, bone, place, path);
            }

            return (from, Points - from);
        }

        /// <summary>
        /// The points of a rigid piece (<see cref="Rigid"/>'s range) on any of the bones named - or a socket hanging off one - moved onto another
        /// bone of the rig, their rest kept: the lamp's group onto the bone that swings it (MonsterModel's Swaying).
        /// </summary>
        public void Rebone((int From, int Count) range, IReadOnlySet<string> bones, int bone)
        {
            ArgumentNullException.ThrowIfNull(bones);
            var at = 0;
            foreach (Found one in _found)
            {
                for (var p = 0; p < one.Points.Count; p++, at++)
                {
                    Point point = one.Points[p];
                    if (at >= range.From && at < range.From + range.Count
                        && (bones.Contains(point.Name) || (point.Parent.Length > 0 && bones.Contains(point.Parent))))
                    {
                        one.Points[p] = point with { Bone = bone };
                    }
                }
            }
        }

        /// <summary>A piece riding his rig on bones of its own from <paramref name="first"/>: each bone its own, numbered after his.</summary>
        public void Riding(IReadOnlyList<AnimatedObject> chain, string path, AnimationSkeleton own, int first)
        {
            foreach ((string group, string effect, string[] names) in Effects(chain))
            {
                var points = new List<Point>(names.Length);
                foreach (string name in names)
                {
                    if (Named(own, name, chain) is { } framed)
                    {
                        points.Add(framed with { Bone = first + framed.Bone });
                    }
                    else
                    {
                        _unboned++;
                    }
                }

                Add(group, effect, points, -1, Matrix4x4.Identity, path);
            }
        }

        /// <summary>A skin on his rig: each bone his of the same name (a socket's parent likewise) - one his rig has not left out, said.</summary>
        public void Skin(IReadOnlyList<AnimatedObject> chain, string path, IReadOnlyDictionary<string, int> his)
        {
            foreach ((string group, string effect, string[] names) in Effects(chain))
            {
                IReadOnlyList<BodySocket> sockets = BodySockets.Of(chain, null);
                var points = new List<Point>(names.Length);
                foreach (string name in names)
                {
                    if (his.TryGetValue(name, out int bone))
                    {
                        points.Add(new Point(bone, Vector3.Zero, Framed: true, name, string.Empty));
                    }
                    else if (sockets.FirstOrDefault(one => one.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { Parent.Length: > 0 } socket
                        && his.TryGetValue(socket.Parent, out int parent))
                    {
                        points.Add(new Point(parent, Local(socket).Translation, Framed: true, name, socket.Parent));
                    }
                    else
                    {
                        _strays++;
                    }
                }

                Add(group, effect, points, -1, Matrix4x4.Identity, path);
            }
        }

        /// <summary>
        /// The effects as the model carries them, each framed bone's rest taken from the rig as it finally stands - riders joined, swung bones
        /// appended - and what was left out, said.
        /// </summary>
        public (IReadOnlyList<CarriedEffect> Effects, string Said) Made(AnimationSkeleton rig)
        {
            if (_found.Count == 0 && _events == 0)
            {
                return ([], string.Empty);
            }

            SkeletonPose? rest = _found.Any(one => one.Points.Any(point => point.Framed)) ? SkeletonPose.Of(rig) : null;
            var made = new List<CarriedEffect>(_found.Count);
            foreach (Found one in _found)
            {
                var points = new List<CarriedPoint>(one.Points.Count);
                foreach (Point point in one.Points)
                {
                    Vector3 at = point.At;
                    if (point.Framed)
                    {
                        if (rest is null || point.Bone < 0 || point.Bone >= rest.BindModel.Count)
                        {
                            _lost++;
                            continue;
                        }

                        at = Vector3.Transform(point.At, rest.BindModel[point.Bone]);
                    }

                    points.Add(new CarriedPoint(point.Bone, at, point.Name));
                }

                if (points.Count > 0)
                {
                    made.Add(new CarriedEffect(one.Effect, one.Group, points, one.Frame, one.Place, one.Source));
                }
            }

            var said = string.Create(CultureInfo.InvariantCulture, $"{made.Count} particle effects carried");
            if (made.Count > 0)
            {
                said += " (" + string.Join(", ", made.Select(one => $"{Short(one.Effect)} on {one.Group} of {Short(one.Source)}, {one.Points.Count} bones")) + ")";
            }

            var left = new List<string>();
            Add(left, _events, "effects an animation fires at a time, not drawn");
            Add(left, _unboned, "group bones their own rig and sockets have not, left out");
            Add(left, _strays, "group bones of a skinned piece his rig has not, left out");
            Add(left, _lost, "group bones the posed rig has not, left out");
            return (made, left.Count == 0 ? said : said + "; " + string.Join(", ", left));
        }

        /// <summary>How many points have been found so far, the order <see cref="Rebone"/>'s ranges count in.</summary>
        private int Points => _found.Sum(one => one.Points.Count);

        private void Add(string group, string effect, List<Point> points, int frame, Matrix4x4 place, string source)
            => _found.Add(new Found(effect, group, points, frame, place, source));

        /// <summary>
        /// A chain's continuous effects with their groups' bones, nearest file first, each effect and group once; an animation's events counted.
        /// </summary>
        private List<(string Group, string Effect, string[] Names)> Effects(IReadOnlyList<AnimatedObject> chain)
        {
            var groups = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
            var effects = new List<(string Group, string Effect)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (AnimatedObject one in chain)
            {
                foreach (AoStruct block in one.Named(GroupsBlock))
                {
                    foreach (AoEntry line in block.Entries.Where(entry => entry.Key.Equals(GroupLine, StringComparison.OrdinalIgnoreCase)))
                    {
                        string[] words = line.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        if (words.Length >= 3)
                        {
                            groups.TryAdd(words[0], words[2..]);
                        }
                    }
                }

                foreach (AoStruct block in one.Named(EffectsBlock))
                {
                    foreach (AoEntry line in block.Entries)
                    {
                        if (line.Key.Equals(Continuous, StringComparison.OrdinalIgnoreCase))
                        {
                            string[] words = line.Value.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                            if (words.Length == 2 && words[1].EndsWith(".pet", StringComparison.OrdinalIgnoreCase) && seen.Add(words[0] + "|" + words[1]))
                            {
                                effects.Add((words[0], words[1]));
                            }
                        }
                        else if (line.Key.Equals("animations", StringComparison.OrdinalIgnoreCase))
                        {
                            _events += CountOf(line.Value, "ParticleEffectEventType");
                        }
                    }
                }
            }

            return [.. effects.Select(one => (one.Group, one.Effect, groups.GetValueOrDefault(one.Group) ?? []))];
        }

        /// <summary>The rig a chain names, or null.</summary>
        private AnimationSkeleton? Rig(IReadOnlyList<AnimatedObject> chain)
        {
            foreach (AnimatedObject one in chain)
            {
                foreach (AoStruct block in one.Named(RigBlock))
                {
                    if (block.Entries.FirstOrDefault(entry => entry.Key.Equals(RigLine, StringComparison.OrdinalIgnoreCase)) is { Value.Length: > 0 } line)
                    {
                        AnimationSkeleton rig = AnimationSkeleton.Read(read(line.Value.Replace('\\', '/').Trim()));
                        return rig.Ready ? rig : null;
                    }
                }
            }

            return null;
        }

        /// <summary>A group bone on a rig the model's rest will place (framed): the rig's bone of the name, else a socket of the chain off one.</summary>
        private static Point? Named(AnimationSkeleton rig, string name, IReadOnlyList<AnimatedObject> chain)
        {
            int bone = IndexOf(rig, name);
            if (bone >= 0)
            {
                return new Point(bone, Vector3.Zero, Framed: true, name, string.Empty);
            }

            foreach (BodySocket socket in BodySockets.Of(chain, rig))
            {
                if (socket.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && socket.Bone >= 0)
                {
                    return new Point(socket.Bone, Local(socket).Translation, Framed: true, name, socket.Parent);
                }
            }

            return null;
        }

        /// <summary>Where a piece's own rig rests a group bone - the bone of the name, else a socket off one - in the piece's model space.</summary>
        private static (Vector3 Position, string Parent)? Rested(AnimationSkeleton? rig, SkeletonPose? rest, IReadOnlyList<BodySocket> sockets, string name)
        {
            if (rig is null || rest is null)
            {
                return null;
            }

            int bone = IndexOf(rig, name);
            if (bone >= 0 && bone < rest.BindModel.Count)
            {
                return (rest.BindModel[bone].Translation, string.Empty);
            }

            foreach (BodySocket socket in sockets)
            {
                if (socket.Name.Equals(name, StringComparison.OrdinalIgnoreCase) && socket.Bone >= 0 && socket.Bone < rest.BindModel.Count)
                {
                    return ((Local(socket) * rest.BindModel[socket.Bone]).Translation, socket.Parent);
                }
            }

            return null;
        }

        /// <summary>A socket's transform off its bone - MonsterModel.SocketsOf's: its size, its turn about x, y and z, its shift.</summary>
        private static Matrix4x4 Local(BodySocket socket)
            => Matrix4x4.CreateScale(socket.Scale)
                * Matrix4x4.CreateRotationX(socket.Rotation.X)
                * Matrix4x4.CreateRotationY(socket.Rotation.Y)
                * Matrix4x4.CreateRotationZ(socket.Rotation.Z)
                * Matrix4x4.CreateTranslation(socket.Translation);

        private static int IndexOf(AnimationSkeleton rig, string bone)
        {
            for (var at = 0; at < rig.Bones.Count; at++)
            {
                if (rig.Bones[at].Name.Equals(bone, StringComparison.OrdinalIgnoreCase))
                {
                    return at;
                }
            }

            return -1;
        }

        private static int CountOf(string text, string word)
        {
            int count = 0;
            for (int at = text.IndexOf(word, StringComparison.Ordinal); at >= 0; at = text.IndexOf(word, at + word.Length, StringComparison.Ordinal))
            {
                count++;
            }

            return count;
        }

        private static void Add(List<string> left, int count, string what)
        {
            if (count > 0)
            {
                left.Add(string.Create(CultureInfo.InvariantCulture, $"{count} {what}"));
            }
        }

        private static string Short(string path) => path[(path.Replace('\\', '/').LastIndexOf('/') + 1)..];

        /// <summary>One bone found: its rig bone, and either its offset in that bone's frame (framed) or where it stands at rest; its socket's parent.</summary>
        private readonly record struct Point(int Bone, Vector3 At, bool Framed, string Name, string Parent);

        /// <summary>One effect found, with its bones.</summary>
        private sealed record Found(string Effect, string Group, List<Point> Points, int Frame, Matrix4x4 Place, string Source);
    }
}
