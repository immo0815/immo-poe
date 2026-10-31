using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// What a worn piece's <c>ObjectHiding</c> block says to hide - its carrier's shapes and attachments, its siblings' - read through
/// everything it extends; and a skin's shape groups (<see cref="Aliases"/>) the names resolve through.
/// </summary>
/// <remarks>
/// THE GAME'S OWN RULE FOR WHAT ARMOUR COVERS, read from the files the player look part wrote whole (v0.1.393's captures):
/// <c>Metadata/SkinBodyArmour.ao</c>, which every body armour skin extends, says
///
///     ObjectHiding
///     {
///         hide_parent_attachments = "Body_Attach"
///         hide_parent_segments = "Body_Seg"
///         hide_sibling_segments = "AP_Body_Hides, AP_Helmet_Hides, …"
///         hide_sibling_attachments = "AP_Helmet_Attach, AP_L_Glove_Attach, …"
///         show_sibling_segments = "AP_Helmet_Show, …"
///     }
///
/// and the body names those groups itself: its <c>attached_object = "&lt;root&gt; …SorceressBody.ao alias Body_Attach"</c> (the class's default
/// outfit) and its skin's <c>alias = "Body_Seg L_chest_meshShape, …"</c>. A piece's own file overrides what it extends - the Beta Doryani
/// robe writes <c>hide_parent_segments = ""</c>, keeping the body's torso under it - or adds to it: <c>add_hide_sibling_segments =
/// "BD_skirt_meshShape"</c>, <c>add_hide_parent_attachments = "Face_Attach"</c>. A name is a group of the target's skin where it names
/// one, else a shape itself (the gloves' <c>"L_fingers_meshShape, L_palm_meshShape, …"</c>).
///
/// READ, NOT MEASURED against the game's picture yet: that a plain key replaces what the chain above set and an <c>add_</c> key adds to it
/// (the robe's empty line means nothing otherwise), and that "parent" is the piece's carrier and "sibling" the carrier's other pieces.
/// <c>show_sibling_segments</c> is read and NOT acted on - what a shown group is hidden by until then is in no file read so far - and the
/// account says so (<see cref="Shown"/>).
/// </remarks>
/// <param name="ParentSegments">The carrier's shapes or shape groups to hide.</param>
/// <param name="ParentAttachments">The carrier's attachments to leave off, by the alias their lines give them.</param>
/// <param name="SiblingSegments">The other pieces' shapes or shape groups to hide.</param>
/// <param name="SiblingAttachments">The other pieces' own attachments to leave off, by alias.</param>
/// <param name="Shown">What it says to show of its siblings - read, not acted on.</param>
public sealed record PieceHiding(
    IReadOnlyList<string> ParentSegments,
    IReadOnlyList<string> ParentAttachments,
    IReadOnlyList<string> SiblingSegments,
    IReadOnlyList<string> SiblingAttachments,
    IReadOnlyList<string> Shown)
{
    /// <summary>Nothing hidden.</summary>
    public static PieceHiding None { get; } = new([], [], [], [], []);

    /// <summary>Whether it hides anything at all.</summary>
    public bool Any => ParentSegments.Count + ParentAttachments.Count + SiblingSegments.Count + SiblingAttachments.Count > 0;

    /// <summary>The block a piece's hiding is written in.</summary>
    public const string Block = "ObjectHiding";

    /// <summary>The prefix that adds to what the chain above set rather than replacing it.</summary>
    private const string Add = "add_";

    private const string HideParentSegments = "hide_parent_segments";
    private const string HideParentAttachments = "hide_parent_attachments";
    private const string HideSiblingSegments = "hide_sibling_segments";
    private const string HideSiblingAttachments = "hide_sibling_attachments";
    private const string ShowSiblingSegments = "show_sibling_segments";

    /// <summary>
    /// A piece's hiding through its chain - the piece first, then what it extends, as <c>MonsterModel</c>'s walk lists them: read from the
    /// furthest ancestor down, each plain key replacing what was set above it and each <c>add_</c> key adding to it.
    /// </summary>
    public static PieceHiding Of(IReadOnlyList<AnimatedObject> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var said = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        for (int one = chain.Count - 1; one >= 0; one--)
        {
            foreach (AoStruct block in chain[one].Named(Block))
            {
                foreach (AoEntry entry in block.Entries)
                {
                    bool adds = entry.Key.StartsWith(Add, StringComparison.Ordinal);
                    string key = adds ? entry.Key[Add.Length..] : entry.Key;
                    if (key is not (HideParentSegments or HideParentAttachments or HideSiblingSegments or HideSiblingAttachments or ShowSiblingSegments))
                    {
                        continue;
                    }

                    if (!adds || !said.TryGetValue(key, out List<string>? names))
                    {
                        names = [];
                        if (adds && said.TryGetValue(key, out List<string>? before))
                        {
                            names.AddRange(before);
                        }

                        said[key] = names;
                    }

                    names.AddRange(Names(entry.Value));
                }
            }
        }

        if (said.Count == 0)
        {
            return None;
        }

        IReadOnlyList<string> Listed(string key) => said.TryGetValue(key, out List<string>? names) ? [.. names.Distinct(StringComparer.OrdinalIgnoreCase)] : [];
        return new PieceHiding(
            Listed(HideParentSegments), Listed(HideParentAttachments), Listed(HideSiblingSegments), Listed(HideSiblingAttachments), Listed(ShowSiblingSegments));
    }

    /// <summary>
    /// A chain's skin shape groups, the piece's own first: <c>alias = "Body_Seg L_chest_meshShape, L_shoulder_1_meshShape"</c> in a
    /// SkinMesh block - the group's name, then its shapes by commas.
    /// </summary>
    public static IReadOnlyDictionary<string, string[]> Aliases(IReadOnlyList<AnimatedObject> chain)
    {
        ArgumentNullException.ThrowIfNull(chain);
        var groups = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        foreach (AnimatedObject one in chain)
        {
            foreach (AoStruct block in one.Named("SkinMesh"))
            {
                foreach (AoEntry entry in block.Entries)
                {
                    if (!string.Equals(entry.Key, "alias", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    string value = entry.Value.Trim();
                    int space = value.IndexOf(' ', StringComparison.Ordinal);
                    if (space > 0)
                    {
                        groups.TryAdd(value[..space], [.. Names(value[(space + 1)..])]);
                    }
                }
            }
        }

        return groups;
    }

    /// <summary>The shape names a list of names stands for: each group's shapes where a group of that name is known, else the name itself.</summary>
    public static IEnumerable<string> Shapes(IEnumerable<string> names, IReadOnlyDictionary<string, string[]> groups)
    {
        ArgumentNullException.ThrowIfNull(names);
        ArgumentNullException.ThrowIfNull(groups);
        foreach (string name in names)
        {
            if (groups.TryGetValue(name, out string[]? shapes))
            {
                foreach (string shape in shapes)
                {
                    yield return shape;
                }
            }
            else
            {
                yield return name;
            }
        }
    }

    /// <summary>A line's names: split at commas, trimmed, the empty ones dropped.</summary>
    internal static IEnumerable<string> Names(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
