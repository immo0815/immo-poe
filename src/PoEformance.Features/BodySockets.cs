using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>One socket a body's .ao chain defines: its name, the bone it hangs off (and that bone's number in the body's rig), its turn, shift and scale.</summary>
/// <param name="Name">What an attached_object line or the game calls it - <c>R_Weapon</c>.</param>
/// <param name="Parent">The bone it hangs off - <c>aux_R_Weapon_attachment_jntBnd</c> - or empty where it names none.</param>
/// <param name="Bone">That bone's number in the body's rig, or -1 where the rig has no bone of the name.</param>
/// <param name="Rotation">Its turn as written, radians about x, y and z.</param>
/// <param name="Translation">Its shift as written.</param>
/// <param name="Scale">Its scale as written, one where it names none.</param>
public readonly record struct BodySocket(string Name, string Parent, int Bone, Vector3 Rotation, Vector3 Translation, Vector3 Scale);

/// <summary>
/// The sockets a body's .ao chain defines in its ClientAnimationController blocks - the player's <c>Metadata/Characters/Character.ao</c>
/// names some seventy, <c>R_Weapon</c> and <c>L_Weapon</c> among them - read child first, so a file's own socket wins over the one it inherits.
/// </summary>
/// <remarks>
/// WRITTEN AS A SOCKET AND ITS INDENTED LINES: <c>socket = "R_Weapon"</c> with <c>parent = "aux_R_Weapon_attachment_jntBnd"</c> under it,
/// and <c>rotation</c>, <c>translation</c> and <c>scale</c> where it turns, shifts or sizes the place. Which socket the game hangs a held
/// piece from is in none of these files - the player look part measures it (CapturePlayerLook.Sockets).
/// </remarks>
public static class BodySockets
{
    /// <summary>The sockets of a chain, the piece first - and each one's bone in the rig given, where it has one.</summary>
    public static IReadOnlyList<BodySocket> Of(IReadOnlyList<AnimatedObject> chain, AnimationSkeleton? rig)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var bones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var one = 0; one < (rig?.Bones.Count ?? 0); one++)
        {
            bones.TryAdd(rig!.Bones[one].Name, one);
        }

        var found = new List<BodySocket>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (AnimatedObject ao in chain)
        {
            foreach (AoStruct block in ao.Named("ClientAnimationController"))
            {
                foreach (AoEntry entry in block.Entries)
                {
                    if (!string.Equals(entry.Key, "socket", StringComparison.Ordinal) || entry.Value.Trim() is not { Length: > 0 } name || !seen.Add(name))
                    {
                        continue;
                    }

                    string parent = Child(entry, "parent");
                    found.Add(new BodySocket(
                        name,
                        parent,
                        parent.Length > 0 && bones.TryGetValue(parent, out int bone) ? bone : -1,
                        Three(Child(entry, "rotation")) ?? Vector3.Zero,
                        Three(Child(entry, "translation")) ?? Vector3.Zero,
                        Three(Child(entry, "scale")) ?? Vector3.One));
                }
            }
        }

        return found;
    }

    /// <summary>The value of a socket's indented line of one key, or empty.</summary>
    private static string Child(AoEntry entry, string key)
        => entry.Children.FirstOrDefault(one => string.Equals(one.Key, key, StringComparison.Ordinal))?.Value.Trim() ?? string.Empty;

    /// <summary>Three numbers written with spaces between them, or null where the text is not three numbers.</summary>
    internal static Vector3? Three(string said)
    {
        string[] parts = said.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 3
            && float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
            && float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float y)
            && float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
            ? new Vector3(x, y, z)
            : null;
    }
}
