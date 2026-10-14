using System.Numerics;
using PoEformance.Core.Memory;
using PoEformance.Core.Schema;

namespace PoEformance.Game.Ui;

/// <summary>
/// The rectangles of the ground item labels the game is drawing this frame, in window pixels.
/// </summary>
/// <remarks>
/// WHAT THEY ARE. A dropped item's name is an ordinary UI element whose StringId is the item's
/// own path - <c>Metadata/MiscellaneousObjects/WorldItem</c> for a ground item - and whose text is
/// the name. Its rectangle is its own, the same as an interface part's, so the map can be drawn
/// under it at the game's own place (measured on the large map, 2026-10-14).
///
/// WHEN IT IS READ. Only while the large map is on screen: a label is only ever drawn under the
/// map's fade, and the search costs a walk of the interface that the minimap does not need. The
/// caller decides; this only answers.
///
/// ONLY WHAT IS SHOWN. A hidden element keeps its rectangle, so a label the game has stopped
/// drawing would otherwise still fade the map. Visibility and the window are both checked.
///
/// SEARCHED, NOT POSITIONED. The labels hang under the interface's own tree at a depth nobody
/// has pinned down, so they are found by their id under the UI root rather than by an index that
/// a patch would move. The budget bounds the walk; an interrupted search returns what it found.
/// </remarks>
public sealed class GroundLabelReader
{
    /// <summary>The StringId every ground item label carries.</summary>
    public const string Id = "Metadata/MiscellaneousObjects/WorldItem";

    private const int MaxLabels = 300;
    private const int Budget = 20_000;

    private readonly UiTreeReader _tree;

    public GroundLabelReader(IMemoryReader reader, OffsetSchema schema)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);

        _tree = new UiTreeReader(reader, schema);
    }

    /// <summary>The visible ground labels that lie on the window, or none when the root is unknown.</summary>
    public List<ScreenRect> Read(ulong uiRoot, UiScale scale, ScreenRect window)
    {
        var labels = new List<ScreenRect>();
        if (uiRoot == 0)
        {
            return labels;
        }

        foreach (UiNode node in _tree.Search(uiRoot, Id, scale, MaxLabels, Budget))
        {
            if (node.StringId != Id || !node.Visible || node.Size.X <= 0f || node.Size.Y <= 0f)
            {
                continue;
            }

            Vector2 at = node.Position;
            var rect = new ScreenRect(at.X, at.Y, at.X + node.Size.X, at.Y + node.Size.Y);
            if (rect.IsSane && rect.Overlaps(window))
            {
                labels.Add(rect.ClippedTo(window));
            }
        }

        return labels;
    }
}
