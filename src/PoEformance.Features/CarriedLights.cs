using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// One point light a model carries on a bone of its rig: which bone moves it, where it stands at the bind pose, and what its .ao says of it.
/// </summary>
/// <param name="Bone">
/// The bone of the model's rig - the palette the card skins by, a rider's bones after his - whose skin matrix moves it as it moves the
/// vertices bound to it; -1 for none, where it stands at <paramref name="Rest"/> wherever the model stands.
/// </param>
/// <param name="Rest">Where it stands at the bind pose, in the model's own space.</param>
/// <param name="Colour">Its <c>colour</c> line.</param>
/// <param name="Radius">Its <c>radius</c> line.</param>
/// <param name="PenumbraDist">Its <c>penumbra_dist</c> line.</param>
/// <param name="Source">The .ao it came from and its state, for the line under the picture.</param>
/// <param name="BoneName">The bone as its own file names it, for the account - empty for none.</param>
public readonly record struct CarriedLight(int Bone, Vector3 Rest, Vector3 Colour, float Radius, float PenumbraDist, string Source, string BoneName);

/// <summary>
/// The point lights a monster and his pieces carry on their bones - read from each one's .ao as a room's are (RoomLights), put on the bone
/// of the rig that moves them, and stood where a pose puts that bone.
/// </summary>
/// <remarks>
/// THE EXILE'S STAFF'S LANTERN (v0.1.403, DECIDED from the live client, 2026-11-01: "lights carried by a bone, moving with the animation every
/// frame"): WandererStaffHeld.ao writes <c>Lights { light = "point_light" bone_name = "phys_tri_lamp_jntBnd" state = "" colour = "2.4671
/// 0.493089 0.0398032" radius = 150 penumbra_dist = 0.5 }</c>, and the tool drew a doodad's lights alone, fixed in their room. A light names its
/// bone in its OWN file's rig, so where it goes depends on how its piece hangs (MonsterModel's Worn):
/// <list type="bullet">
/// <item>THE BODY'S OWN, on its rig's bone of that name, its position line in that bone's frame.</item>
/// <item>A PIECE HUNG RIGIDLY (held in a hand, a prop, a piece sharing only the root): where its own rig rests the bone, put through the
/// piece's place, carried by the socket's bone - as its vertices are.</item>
/// <item>A PIECE RIDING HIS RIG (Malgor's wheel): on its own bone, numbered after his.</item>
/// <item>A SKIN ON HIS RIG: on his bone of the same name, its position line in his bone's frame - a READING: the piece's own rig rests the
/// bone elsewhere, and the light stands where his does. One his rig has not is left out, said so.</item>
/// </list>
/// The bone's own simulation (phys_ bones swing in the game) is not drawn: the lamp stands where the animation puts its bone.
/// </remarks>
public static class CarriedLights
{
    /// <summary>
    /// Where each light stands now, in the picture's frame: its rest through its bone's skin matrix where a pose is held, then the model's
    /// place (a staged monster's), written to <paramref name="into"/>; how many were written.
    /// </summary>
    /// <param name="lights">The lights the model carries.</param>
    /// <param name="skin">The pose's skin matrices (SkeletonPose.Skin), or empty for the bind pose.</param>
    /// <param name="place">Where the model stands in the picture - a stage's ActorPlace, else the identity.</param>
    /// <param name="into">Where to write them, as many as there is room for.</param>
    public static int At(IReadOnlyList<CarriedLight> lights, IReadOnlyList<Matrix4x4> skin, Matrix4x4 place, Span<PointLight> into)
    {
        ArgumentNullException.ThrowIfNull(lights);
        ArgumentNullException.ThrowIfNull(skin);
        int count = Math.Min(lights.Count, into.Length);
        for (var at = 0; at < count; at++)
        {
            CarriedLight one = lights[at];
            Vector3 position = one.Bone >= 0 && one.Bone < skin.Count ? Vector3.Transform(one.Rest, skin[one.Bone]) : one.Rest;
            into[at] = new PointLight(Vector3.Transform(position, place), one.Colour, one.Radius, one.PenumbraDist, one.Source);
        }

        return count;
    }

    /// <summary>
    /// The lights gathered as a monster is dressed, the body's first and each piece's as it is built; the bones' rests are taken from the
    /// rig as it finally stands, riders joined (<see cref="Made"/>).
    /// </summary>
    internal sealed class Gathering(Func<string, byte[]?> read)
    {
        private readonly Dictionary<string, RoomLights.Lit> _cache = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Found> _found = [];
        private int _spots, _off, _unboned, _rig, _strays;

        /// <summary>
        /// The body's own lights: the first of its files whose chain writes a Lights block - each on its rig's bone of the name it gives, else
        /// where its own file stands it.
        /// </summary>
        public void Body(IReadOnlyList<string> files, AnimationSkeleton rig)
        {
            foreach (string file in files)
            {
                AnimatedObject ao = AnimatedObject.Read(read(file.Replace('\\', '/').Trim()));
                if (!ao.Ready || !Writes(ModelDump.Whole(read, ao)))
                {
                    continue;
                }

                RoomLights.Lit lit = Of(file);
                Count(lit);
                for (var one = 0; one < lit.Lights.Count; one++)
                {
                    int bone = lit.Bones[one].Length > 0 ? IndexOf(rig, lit.Bones[one]) : -1;
                    _found.Add(bone >= 0
                        ? new Found(bone, lit.Offsets[one], Framed: true, lit.Lights[one], lit.Bones[one])
                        : new Found(-1, lit.Lights[one].Position, Framed: false, lit.Lights[one], string.Empty));
                }

                return;
            }
        }

        /// <summary>A piece hung rigidly at a bone: its lights where its own rig rests their bones, put through its place, carried by that bone.</summary>
        /// <param name="chain">The piece's .ao and what it extends, already read - a piece writing no light costs nothing more.</param>
        public void Rigid(IReadOnlyList<AnimatedObject> chain, string path, Matrix4x4 place, int bone)
        {
            if (!Writes(chain))
            {
                return;
            }

            RoomLights.Lit lit = Of(path);
            Count(lit);
            for (var one = 0; one < lit.Lights.Count; one++)
            {
                _found.Add(new Found(bone, Vector3.Transform(lit.Lights[one].Position, place), Framed: false, lit.Lights[one], lit.Bones[one]));
            }
        }

        /// <summary>A piece riding his rig on bones of its own from <paramref name="first"/>: each light on its own bone, numbered after his.</summary>
        public void Riding(IReadOnlyList<AnimatedObject> chain, string path, AnimationSkeleton own, int first)
        {
            if (!Writes(chain))
            {
                return;
            }

            RoomLights.Lit lit = Of(path);
            Count(lit);
            for (var one = 0; one < lit.Lights.Count; one++)
            {
                int bone = lit.Bones[one].Length > 0 ? IndexOf(own, lit.Bones[one]) : -1;
                _found.Add(bone >= 0
                    ? new Found(first + bone, lit.Offsets[one], Framed: true, lit.Lights[one], lit.Bones[one])
                    : new Found(-1, lit.Lights[one].Position, Framed: false, lit.Lights[one], string.Empty));
            }
        }

        /// <summary>A skin on his rig: each light on his bone of its bone's name, its position line in his bone's frame - one his rig has not left out.</summary>
        public void Skin(IReadOnlyList<AnimatedObject> chain, string path, IReadOnlyDictionary<string, int> his)
        {
            if (!Writes(chain))
            {
                return;
            }

            RoomLights.Lit lit = Of(path);
            Count(lit);
            for (var one = 0; one < lit.Lights.Count; one++)
            {
                if (lit.Bones[one].Length > 0 && his.TryGetValue(lit.Bones[one], out int bone))
                {
                    _found.Add(new Found(bone, lit.Offsets[one], Framed: true, lit.Lights[one], lit.Bones[one]));
                }
                else
                {
                    _strays++;
                }
            }
        }

        /// <summary>
        /// The lights as the model carries them, each framed one's rest taken from the rig as it finally stands - riders joined, whose own
        /// rests their bones keep (AnimationSkeleton.Carrying) - the first <see cref="SceneLight.MostCarried"/> kept; and what was left out, said.
        /// </summary>
        public (IReadOnlyList<CarriedLight> Lights, string Said) Made(AnimationSkeleton rig)
        {
            if (_found.Count == 0 && _off == 0 && _spots == 0)
            {
                return ([], string.Empty);
            }

            SkeletonPose? rest = _found.Any(one => one.Framed) ? SkeletonPose.Of(rig) : null;
            var lights = new List<CarriedLight>(Math.Min(_found.Count, SceneLight.MostCarried));
            int lost = 0, past = 0;
            foreach (Found one in _found)
            {
                Vector3 at = one.At;
                if (one.Framed)
                {
                    if (rest is null || one.Bone >= rest.BindModel.Count)
                    {
                        lost++;
                        continue;
                    }

                    at = Vector3.Transform(one.At, rest.BindModel[one.Bone]);
                }

                if (lights.Count == SceneLight.MostCarried)
                {
                    past++;
                    continue;
                }

                lights.Add(new CarriedLight(one.Bone, at, one.Light.Colour, one.Light.Radius, one.Light.PenumbraDist, one.Light.Source, one.Name));
            }

            var said = string.Create(CultureInfo.InvariantCulture, $"{lights.Count} point lights carried");
            if (lights.Count > 0)
            {
                said += " (" + string.Join(", ", lights.Select(one => one.BoneName.Length > 0 ? $"{Short(one.Source)} on {one.BoneName}" : $"{Short(one.Source)} on no bone")) + ")";
            }

            var left = new List<string>();
            Add(left, _off, "in a state with no colour (off)");
            Add(left, _spots, "spot lights not drawn");
            Add(left, _rig, "lights in rigs (.ast) not read");
            Add(left, _unboned, "on a bone their own rig has not, put at their piece's origin");
            Add(left, _strays, "on a skinned piece's bone his rig has not, left out");
            Add(left, lost, "on a bone the posed rig has not, left out");
            Add(left, past, $"past {SceneLight.MostCarried} left out");
            return (lights, left.Count == 0 ? said : said + "; " + string.Join(", ", left));
        }

        private RoomLights.Lit Of(string path) => RoomLights.Of(read, path, _cache, 0);

        /// <summary>
        /// Whether a chain writes any light - a Lights block saying something, or a fixed_ao piece that may - asked of what is read already, so a
        /// piece with none costs no read of its own: RoomLights.Of reads the chain again and the rig too, to place a light on a bone and count
        /// the rig's own lights.
        /// </summary>
        private static bool Writes(IReadOnlyList<AnimatedObject> chain)
        {
            foreach (AnimatedObject one in chain)
            {
                if (one.Structs.Any(block => block.Name.Equals("Lights", StringComparison.OrdinalIgnoreCase) && block.Entries.Count > 0)
                    || one.Entries().Any(line => line.Entry.Key.Equals("fixed_ao", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return false;
        }

        private void Count(RoomLights.Lit lit)
        {
            _spots += lit.Spots;
            _off += lit.Off;
            _unboned += lit.Unboned;
            _rig += lit.RigLights;
        }

        private static void Add(List<string> left, int count, string what)
        {
            if (count > 0)
            {
                left.Add(string.Create(CultureInfo.InvariantCulture, $"{count} {what}"));
            }
        }

        /// <summary>A source's file name without its folders, its state kept - RoomLights names a light "path (state)".</summary>
        private static string Short(string source)
        {
            int state = source.IndexOf(" (", StringComparison.Ordinal);
            string file = state >= 0 ? source[..state] : source;
            return file[(file.LastIndexOf('/') + 1)..] + (state >= 0 ? source[state..] : string.Empty);
        }

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

        /// <summary>One light found: its bone, and either its position line in that bone's frame (framed) or where it stands at rest.</summary>
        private readonly record struct Found(int Bone, Vector3 At, bool Framed, PointLight Light, string Name);
    }
}
