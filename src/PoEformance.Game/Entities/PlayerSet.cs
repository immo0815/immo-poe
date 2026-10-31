namespace PoEformance.Game.Entities;

/// <summary>
/// One of the player's weapon sets: the items in its hands, read from his inventories, and what the Monster Book draws it with - the pieces in
/// its hands, the animation pack and the idle of its stance - first from the composition alone (PlayerDress.Read), then from the files
/// (PlayerStance in Features).
/// </summary>
/// <param name="MainItem">The main hand's item's metadata path, or empty.</param>
/// <param name="OffItem">The off hand's item's metadata path, or empty.</param>
public sealed record PlayerSet(string MainItem, string OffItem)
{
    /// <summary>The .ao hung in his main hand (at PlayerDress.MainSocket), or empty.</summary>
    public string MainPiece { get; init; } = string.Empty;

    /// <summary>The .ao hung in his off hand (at PlayerDress.OffSocket), or empty.</summary>
    public string OffPiece { get; init; } = string.Empty;

    /// <summary>Its stance's name - its hands' item stances, main then off (<c>WandSpiritShield</c>) - or empty where not read.</summary>
    public string Stance { get; init; } = string.Empty;

    /// <summary>The .ast of its animation pack - the Actor's for the set in hand, its stance's timeline's for the other - or empty.</summary>
    public string Moves { get; init; } = string.Empty;

    /// <summary>The idle its stance plays, or empty.</summary>
    public string Opening { get; init; } = string.Empty;

    /// <summary>Where each of its pieces, its pack and its idle came from, a line each - for the Monster Book and the capture.</summary>
    public IReadOnlyList<string> Said { get; init; } = [];

    /// <summary>Whether neither hand carries an item.</summary>
    public bool Empty => MainItem.Length == 0 && OffItem.Length == 0;
}
