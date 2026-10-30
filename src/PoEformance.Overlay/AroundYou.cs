using System.Globalization;
using ImGuiNET;
using PoEformance.Features;

namespace PoEformance.Overlay;

/// <summary>
/// The "around you" stage the Tile Book and the Monster Book both offer: the block of the area round the player's tile, its side one slider
/// shared by both books - see AreaBlock.
/// </summary>
/// <remarks>
/// ONE SIDE FOR BOTH BOOKS, kept in the settings (<c>aroundSize</c>, nought for the usual): the block is the same place whichever book draws
/// it, and the side is a choice about the machine as much as the place.
/// </remarks>
public sealed class AroundYou
{
    private int _size = AreaBlock.UsualSize;
    private int _dragging = AreaBlock.UsualSize;

    /// <summary>The block's side in tiles, held to the slider's ends; nought or less is the usual.</summary>
    public int Size
    {
        get => _size;
        set
        {
            _size = value <= 0 ? AreaBlock.UsualSize : Math.Clamp(value, AreaBlock.LeastSize, AreaBlock.MostSize);
            _dragging = _size;
        }
    }

    /// <summary>The key of the block of a side round the player's tile now, or empty with why not - see AreaBlock.Around.</summary>
    public Func<int, (string Key, string Why)>? Key { get; init; }

    /// <summary>Told when the side changes, so the settings keep it.</summary>
    public Action? Changed { get; init; }

    /// <summary>The block round the player now at the slider's side, or empty with why not.</summary>
    public (string Key, string Why) Here() => Key?.Invoke(_size) ?? (string.Empty, "nothing reads the area's tiles");

    /// <summary>
    /// The side's slider; true once it is let go at a new side - the block is taken again then, round wherever the player stands.
    /// </summary>
    public bool Slider(string id, float width)
    {
        ImGui.SetNextItemWidth(width);
        ImGui.SliderInt(id, ref _dragging, AreaBlock.LeastSize, AreaBlock.MostSize,
            string.Create(CultureInfo.InvariantCulture, $"{_dragging} x {_dragging} tiles"));
        if (ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("How many tiles a side the area round you is laid over - a tile is 250 units, the game's camera stands about 2,165"
                + " from the player. The block is laid again round wherever you stand when the slider is let go; Ctrl+click types a number.");
        }

        if (!ImGui.IsItemDeactivatedAfterEdit())
        {
            return false;
        }

        int side = Math.Clamp(_dragging, AreaBlock.LeastSize, AreaBlock.MostSize);
        if (side == _size)
        {
            return false;
        }

        _size = side;
        Changed?.Invoke();
        return true;
    }

    /// <summary>What the tooltip of an "around you" choice says.</summary>
    public const string Tip = "The area round you as the game laid it: every tile of the block round the tile you stand on, laid where and"
        + " how the area laid it, with its ground and its own doodads - no room needed, so it works in any area. What only a room adds, its"
        + " own doodads, is not there. The picture's origin being a place in the area, the river's flow map read live is drawn on it.";
}
