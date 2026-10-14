using System.Numerics;
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
/// WHAT IT COSTS. The element's address is found once per AREA - kept while the area instance is
/// the same, looked up again when it changes. Every read after that places the element's children
/// in one pass (<see cref="UiElementReader.ReadSiblings"/>, one walk up the ancestors for all of
/// them) and reads a child's StringId only where the child is shown and has a size. Reading each
/// child as a whole node (<c>UiTreeReader.ReadOne</c>) would also read its text, its own children
/// and a walk up for every child, none of which this needs.
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

    private const int MaxChildren = 512;

    /// <summary>
    /// Whether labels are read at all. Off means no read and no fade - the switch on the overlay's
    /// hiding group. Set from the overlay's thread; a bool write is atomic, and a frame that sees
    /// the old value reads one more time, which is harmless.
    /// </summary>
    public static volatile bool Enabled = true;

    private readonly UiElementReader _elements;

    private ulong _area;
    private ulong _container;

    public GroundLabelReader(UiElementReader elements)
    {
        ArgumentNullException.ThrowIfNull(elements);

        _elements = elements;
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
        if (!Enabled || uiRoot == 0 || areaInstance == 0)
        {
            return labels;
        }

        if (areaInstance != _area || _container == 0)
        {
            _area = areaInstance;
            _container = ResolveContainer(uiRoot);
        }

        if (_container == 0)
        {
            return labels;
        }

        if (!_elements.IsUiElement(_container))
        {
            // Gone since the last read - looked up again on the next one.
            _container = 0;
            return labels;
        }

        List<ulong> children = _elements.Children(_container, MaxChildren);
        if (children.Count == 0)
        {
            return labels;
        }

        Dictionary<ulong, Placed> placed = _elements.ReadSiblings(_container, children, scale);
        foreach (ulong child in children)
        {
            if (!placed.TryGetValue(child, out Placed where) || !where.Shown
                || where.Size.X <= 0f || where.Size.Y <= 0f)
            {
                continue;
            }

            if (_elements.StringIdOf(child) != Id)
            {
                continue;
            }

            Vector2 at = where.Position;
            var rect = new ScreenRect(at.X, at.Y, at.X + where.Size.X, at.Y + where.Size.Y);
            if (rect.IsSane && rect.Overlaps(window))
            {
                labels.Add(rect.ClippedTo(window));
            }
        }

        return labels;
    }

    /// <summary>The address of the element at the end of <see cref="ContainerPath"/>, or zero.</summary>
    private ulong ResolveContainer(ulong root)
    {
        ulong at = root;
        foreach (int index in ContainerPath)
        {
            if (!_elements.IsUiElement(at))
            {
                return 0;
            }

            at = _elements.Child(at, index);
            if (at == 0)
            {
                return 0;
            }
        }

        return _elements.IsUiElement(at) ? at : 0;
    }
}
