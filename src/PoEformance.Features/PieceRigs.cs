using PoEformance.Game.Diagnostics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The rigs a piece the player is drawn with brings: its own, and those of the pieces it hangs off itself - for the bone watch's search
/// for their poses by their rest (<see cref="PoseHunt"/>).
/// </summary>
/// <remarks>
/// THE PIECES IT HANGS OFF ITSELF TOO, because those are where the engine simulates most: the robe's skirt and shawl, the gloves' feathers
/// and the helmet's dangle are attached_object lines of the armour's .ao, not pieces of the composition (v0.1.401's capture), and each has a
/// rig of its own full of phys_ bones.
/// </remarks>
public static class PieceRigs
{
    /// <summary>How deep the pieces a piece hangs off itself are followed - two levels, a robe's skirt on the robe on the body.</summary>
    public const int Depth = 2;

    /// <summary>The rigs of a piece and of what it hangs off itself, its own first - each rig once, a piece without one left out.</summary>
    /// <param name="read">How to get a file out of the install, by path.</param>
    /// <param name="path">The piece's .ao, or an entity's path naming one without its suffix.</param>
    public static IReadOnlyList<PoseRig> Of(Func<string, byte[]?> read, string path)
    {
        ArgumentNullException.ThrowIfNull(read);
        var rigs = new List<PoseRig>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var queue = new Queue<(string Path, int Depth)>();
        queue.Enqueue((path, 0));
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (queue.Count > 0)
        {
            (string one, int depth) = queue.Dequeue();
            if (string.IsNullOrWhiteSpace(one) || !visited.Add(one))
            {
                continue;
            }

            AnimatedObject ao = MonsterModels.ObjectAt(read, one);
            if (!ao.Ready)
            {
                continue;
            }

            List<AnimatedObject> chain = ModelDump.Whole(read, ao);
            string skeleton = ModelDump.Entryed(chain, "ClientAnimationController", "skeleton");
            if (skeleton.Length > 0 && seen.Add(skeleton) && PoseRig.Of(one, skeleton, AnimationSkeleton.Read(read(skeleton))) is { } rig)
            {
                rigs.Add(rig);
            }

            if (depth >= Depth)
            {
                continue;
            }

            foreach (AnimatedObject link in chain)
            {
                foreach (string hung in MonsterModels.HungPaths(link))
                {
                    queue.Enqueue((hung, depth + 1));
                }
            }
        }

        return rigs;
    }
}
