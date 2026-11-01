using PoEformance.Features;

namespace PoEformance.Overlay;

/// <summary>The Tile Book as the agent link drives it: a tile or room by path or by words in its path, and the area round you.</summary>
/// <remarks>Render thread only, as everything the book's draw touches.</remarks>
public sealed partial class TileBookWindow
{
    /// <summary>A row by its path, else the first whose path holds the words, and the others that did.</summary>
    internal (string Path, IReadOnlyList<string> Others) AgentFind(string asked)
    {
        asked = asked.Trim();
        if (asked.Length == 0)
        {
            return (string.Empty, []);
        }

        if (Page.Row(asked) >= 0)
        {
            return (asked, []);
        }

        var found = new List<string>();
        foreach (string path in Page.Paths)
        {
            if (path.Contains(asked, StringComparison.OrdinalIgnoreCase))
            {
                found.Add(path);
            }
        }

        found.Sort(StringComparer.Ordinal);
        return found.Count == 0 ? (string.Empty, []) : (found[0], [.. found.Skip(1).Take(30)]);
    }

    /// <summary>Shows a row, as a click on it does - the area round you gives the pane back.</summary>
    internal void AgentChoose(string path)
    {
        Choose(path);
        _blockKey = string.Empty;
    }

    /// <summary>Shows the area round you in place of the row, as the button does; why not where it cannot.</summary>
    internal string AgentAround(int side)
    {
        if (AroundBlock is not { } around)
        {
            return "the book has no area round you in this session";
        }

        if (side > 0 && around.Size != side)
        {
            around.Size = side;
            around.Changed?.Invoke();
        }

        Take(around);
        return _blockKey.Length > 0 ? $"the area round you, {around.Size} x {around.Size} tiles" : _blockWhy;
    }

    /// <summary>How the room rows are drawn: the doodads' cap, the tool shapes, laid as the area laid it, fitted to the ground.</summary>
    internal string AgentRoom(int? doodads, bool? tools, bool? asLaid, bool? fitted)
    {
        var said = new List<string>();
        if (doodads is { } cap)
        {
            Doodads = cap;
            said.Add($"doodads {Doodads}");
        }

        if (tools is { } shown)
        {
            Tools = shown;
            said.Add(shown ? "tool shapes shown" : "tool shapes hidden");
        }

        if (asLaid is { } laid)
        {
            _asLaid = laid;
            said.Add(laid ? "as laid" : "as written");
        }

        if (fitted is { } fit)
        {
            _fitted = fit;
            said.Add(fit ? "fitted to the ground" : "at the tiles' level");
        }

        return string.Join(", ", said);
    }
}
