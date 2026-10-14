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
/// the name. Its rectangle is its own, the same as an interface part's.
///
/// WHERE THEY ARE. Under one element, reached by the index path <see cref="ContainerPath"/> from the
/// UI root (measured 2026-10-14: the labels are that element's direct children).
///
/// WHAT IT COSTS. The element's address is found once per AREA - it is kept while the area instance
/// stays the same and looked up again when the area changes - and every read after that only walks
/// the element's children. A search of the whole tree was the first version and cost about half a
/// second a read; the path costs a few node reads.
///
/// ONLY THE ITEMS, AND ONLY THE ONES SHOWN. Of the children, only WorldItem elements count, and of
/// those only the visible ones: a hidden element keeps its rectangle, so a label the game has
/// stopped drawing would otherwise still fade the map.
/// </remarks>
public sealed class GroundLabelReader
{
    /// <summary>The StringId every ground item label carries.</summary>
    public const string Id = "Metadata/MiscellaneousObjects/WorldItem";

    /// <summary>The child indices from the UI root down to the element the labels hang under.</summary>
    public static readonly int[] ContainerPath = [8, 0, 0];

    private readonly UiTreeReader _tree;

    private ulong _area;
    private ulong _container;

    public GroundLabelReader(IMemoryReader reader, OffsetSchema schema)
    {
        ArgumentNullException.ThrowIfNull(reader);
        ArgumentNullException.ThrowIfNull(schema);

        _tree = new UiTreeReader(reader, schema);
    }

    /// <summary>
    /// The visible ground labels that lie on the window. Empty when the path does not lead to the
    /// element, which is the honest answer rather than a search for it.
    /// </summary>
    /// <param name="uiRoot">The UI root this frame.</param>
    /// <param name="scale">The window's scale, for the positions.</param>
    /// <param name="areaInstance">The area instance this frame; a new one means the element is looked up again.</param>
    /// <param name="window">The window the labels must lie on.</param>
    public List<ScreenRect> Read(ulong uiRoot, UiScale scale, ulong areaInstance, ScreenRect window)
    {
        var labels = new List<ScreenRect>();
        if (uiRoot == 0 || areaInstance == 0)
        {
            return labels;
        }

        if (areaInstance != _area || _container == 0)
        {
            _area = areaInstance;
            _container = ResolveContainer(uiRoot, scale);
        }

        if (_container == 0)
        {
            return labels;
        }

        UiNode? container = _tree.ReadOne(_container, scale);
        if (container is null)
        {
            // Gone since the last read - looked up again on the next one.
            _container = 0;
            return labels;
        }

        foreach (ulong child in container.Children)
        {
            UiNode? node = _tree.ReadOne(child, scale);
            if (node is null || node.StringId != Id || !node.Visible || node.Size.X <= 0f || node.Size.Y <= 0f)
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

    /// <summary>The address of the element at the end of <see cref="ContainerPath"/>, or zero.</summary>
    private ulong ResolveContainer(ulong root, UiScale scale)
    {
        ulong at = root;
        foreach (int index in ContainerPath)
        {
            UiNode? node = _tree.ReadOne(at, scale);
            if (node is null || index < 0 || index >= node.Children.Count)
            {
                return 0;
            }

            at = node.Children[index];
        }

        return _tree.ReadOne(at, scale) is null ? 0 : at;
    }
}
