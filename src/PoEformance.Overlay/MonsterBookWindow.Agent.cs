using System.Globalization;
using PoEformance.Features;
using PoEformance.Game.Entities;

namespace PoEformance.Overlay;

/// <summary>The Monster Book as the agent link drives it: a monster by name or path, a stage by its name in the list, the player taken.</summary>
/// <remarks>Render thread only, as everything the book's draw touches.</remarks>
public sealed partial class MonsterBookWindow
{
    /// <summary>
    /// A monster by its path, else by its name - exactly, then the first whose name or path holds the words - and the
    /// others that matched, for the agent to choose among.
    /// </summary>
    internal (string Path, IReadOnlyList<string> Others) AgentFind(string asked)
    {
        asked = asked.Trim();
        IReadOnlyDictionary<string, MonsterVariety> all = _of.All;
        if (asked.Length == 0)
        {
            return (string.Empty, []);
        }

        if (_of.Find(asked) is not null)
        {
            return (asked, []);
        }

        var exact = new List<string>();
        var near = new List<string>();
        foreach ((string path, MonsterVariety one) in all)
        {
            string name = one.Name ?? string.Empty;
            if (string.Equals(name, asked, StringComparison.OrdinalIgnoreCase))
            {
                exact.Add(path);
            }
            else if (name.Contains(asked, StringComparison.OrdinalIgnoreCase) || path.Contains(asked, StringComparison.OrdinalIgnoreCase))
            {
                near.Add(path);
            }
        }

        exact.Sort(StringComparer.Ordinal);
        near.Sort(StringComparer.Ordinal);
        List<string> found = exact.Count > 0 ? exact : near;
        return found.Count == 0
            ? (string.Empty, [])
            : (found[0], [.. found.Skip(1).Take(30).Select(path => $"{all[path].Name} - {path}")]);
    }

    /// <summary>Shows a monster in the pane, as a click on its row does; the player gives the pane back.</summary>
    internal void AgentChoose(string path)
    {
        Choose(path);
        _playerShown = false;
    }

    /// <summary>Every stage the list offers for the monster shown, as the combo names them, with the key each loads under.</summary>
    internal List<(string Label, string Key)> AgentStages()
    {
        var stages = new List<(string Label, string Key)> { ("none", string.Empty) };
        if (Model is not { Possible: true } model)
        {
            return stages;
        }

        foreach (string tile in Arenas(model, Chosen, opening: true))
        {
            stages.Add(("arena: " + Tail(tile), Key(tile)));
        }

        stages.AddRange(_laid.Select(laid => ("laid in play: " + Tail(laid.Room), laid.Key)));
        stages.AddRange(_built.Select(built => ("built · " + RoomKey.Read(built.Key).BuiltIn + ": " + Tail(built.Room), built.Key)));
        if (TileChoice?.Invoke() is { Length: > 0 } pick)
        {
            stages.Add(("the Tile Book's: " + Tail(pick), Key(pick)));
        }

        if (Around is { } around && around.Here() is { Key.Length: > 0 })
        {
            string side = around.Size.ToString(CultureInfo.InvariantCulture);
            stages.Add(($"around you: {side} x {side} tiles", "\u0001around"));
        }

        return stages;
    }

    /// <summary>
    /// Stands the monster on a stage: "none", a label from <see cref="AgentStages"/> (exact, then the first containing the
    /// words), "around you" with the block's side, or a key or tile path written out; what it did.
    /// </summary>
    internal string AgentStage(string asked, int around)
    {
        if (Model is not { Possible: true } model)
        {
            return "the book has no model pane in this session";
        }

        asked = asked.Trim();
        if (around > 0 && Around is { } block && block.Size != around)
        {
            block.Size = around;
            block.Changed?.Invoke();
        }

        if (asked.Contains('|', StringComparison.Ordinal) || asked.EndsWith(".tdt", StringComparison.OrdinalIgnoreCase)
            || asked.EndsWith(".arm", StringComparison.OrdinalIgnoreCase))
        {
            model.Stage = asked.EndsWith(".tdt", StringComparison.OrdinalIgnoreCase) ? Key(asked) : asked;
            return "the stage is " + model.Stage;
        }

        List<(string Label, string Key)> stages = AgentStages();
        int at = stages.FindIndex(stage => string.Equals(stage.Label, asked, StringComparison.OrdinalIgnoreCase));
        if (at < 0)
        {
            at = stages.FindIndex(stage => stage.Label.Contains(asked, StringComparison.OrdinalIgnoreCase));
        }

        if (at < 0)
        {
            return $"no stage '{asked}' - the list offers: " + string.Join("; ", stages.Select(stage => stage.Label));
        }

        string key = stages[at].Key;
        if (key == "\u0001around")
        {
            if (Around?.Taken() is not { Key.Length: > 0 } taken)
            {
                return "the area round you could not be taken";
            }

            key = taken.Key;
        }

        model.Stage = key;
        return "stands on " + stages[at].Label;
    }

    /// <summary>Takes the player as the game draws him, as the button does; false where the book cannot.</summary>
    internal bool AgentTakePlayer()
    {
        if (PlayerTaken is not { } take || Model is not { Possible: true } || _taking is not null)
        {
            return false;
        }

        _taking = Task.Run(take);
        return true;
    }

    /// <summary>Whether the player taken last has landed, and what it said - read after <see cref="AgentTakePlayer"/>.</summary>
    internal (bool Landed, bool Shown, string Said) AgentPlayer()
        => (_taking is null, _playerShown, _player.Ready ? $"{_player.Path}, weapon set {_playerSet + 1} of {_player.Sets.Count}" : _player.Why);

    /// <summary>Switches the player's weapon set (one-based), as the radio buttons do.</summary>
    internal string AgentWeaponSet(int set)
    {
        if (!_player.Ready || set < 1 || set > _player.Sets.Count)
        {
            return _player.Ready ? $"he has {_player.Sets.Count} weapon set(s)" : "the player has not been taken";
        }

        _playerSet = set - 1;
        _playerVariety = _player.Variety(_playerSet);
        _playerShown = true;
        return $"weapon set {set}";
    }
}
