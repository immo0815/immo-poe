using System.Globalization;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Overlay;

/// <summary>
/// What the agent link may do with a model pane: the buttons a hand presses, and the picture and lines it reads.
/// </summary>
/// <remarks>
/// ON THE RENDER THREAD, every member: the pane's state is the frame's (see <see cref="AgentFrames"/>, which
/// runs these between frames). Nothing here draws - the pane draws as it always does, the next frame its book is
/// on screen, and the agent link waits for that drawing before it reads the picture back (<see cref="AgentSettled"/>).
/// </remarks>
public sealed partial class MonsterPortrait
{
    /// <summary>The processor's last picture, its pixels the canvas's own until the next drawing - see <see cref="AgentPicture"/>.</summary>
    private GamePicture _agentCpu;

    /// <summary>The window is to be unfolded the next time it is drawn.</summary>
    private bool _agentUnfold;

    /// <summary>The last ticket handed out by <see cref="AgentOpen"/>.</summary>
    private int _agentTicket;

    /// <summary>The ticket that stood when the pane last drew a picture.</summary>
    private int _agentDrawn;

    /// <summary>The ticket that stood when the book last named its subject (<see cref="Show"/>).</summary>
    private int _agentSeen;

    /// <summary>
    /// Opens and unfolds the pane's window and makes its next frame draw the picture again; the ticket a wait for
    /// that picture holds (<see cref="AgentSettled"/>).
    /// </summary>
    /// <remarks>
    /// A TICKET, NOT A COUNT OF PICTURES: the book names its subject (Show) before the window draws it in the same frame,
    /// so a picture drawn with the ticket standing is of what was asked after it - never the model shown before a new
    /// choice, which a count taken at the asking would let through on the very next frame.
    /// </remarks>
    internal int AgentOpen()
    {
        if (!WindowOpen)
        {
            WindowOpen = true;
            WindowChanged?.Invoke(true);
        }

        _agentUnfold = true;
        _shown = string.Empty;
        return ++_agentTicket;
    }

    /// <summary>
    /// Whether the pane has answered the ticket: a picture drawn since it with everything asked in - the model built,
    /// the view drawn, the card's shaders and the processor's textures in, an animation's keyframes landed - or a build
    /// that ended with no model.
    /// </summary>
    internal bool AgentSettled(int ticket)
        => (_agentDrawn >= ticket
                && _loading is not { IsCompleted: false }
                && _loadingTracks is not { IsCompleted: false }
                && _drawnView == _view
                && !_probeAsked
                && (!_model.Ready || (!_cardWaiting && !_unpackWaiting)))
            || (_agentSeen >= ticket && _loading is null && !_model.Ready && Why.Length > 0);

    /// <summary>Why the pane has not drawn what was asked, in a few words - for a wait that ran out.</summary>
    internal string AgentWaiting()
        => !Possible ? "the book has no install to read"
            : !WindowOpen ? "the model window is closed"
            : _loading is { IsCompleted: false } ? _progress?.Said() ?? "building the model"
            : !_model.Ready ? "no model: " + Why
            : _loadingTracks is { IsCompleted: false } ? "the animation's keyframes are loading"
            : _cardWaiting ? "the card's shaders are still compiling: " + _cardWhy
            : _unpackWaiting ? Why
            : "the pane has not drawn again - its book's tab is not on screen, the tools window is folded or covered, or the game is not in front";

    /// <summary>The picture shown, as straight RGBA rows top to bottom, and which of the two drew it; null when there is none.</summary>
    internal (byte[] Rgba, int Size, bool Card)? AgentPicture()
    {
        if (_onCard && _cardTarget is { } target && Card?.Gpu is { } gpu)
        {
            return (gpu.Read(target), target.Size, true);
        }

        if (!_onCard && _agentCpu.Ready && _agentCpu.Width == _agentCpu.Height)
        {
            return (_agentCpu.Rgba.AsSpan(0, _agentCpu.Width * _agentCpu.Height * 4).ToArray(), _agentCpu.Width, false);
        }

        return null;
    }

    /// <summary>Presses one of the edge buttons that move the view, by its name; what it did, or why not.</summary>
    internal string AgentPress(string button)
    {
        switch (button)
        {
            case "reset" when _model.Ready:
                Pressed(ResetSide);
                return "the view fitted to the model again";
            case "game" when _view.Valid:
                Pressed(GameSide);
                return _model.Standing is null ? "the game's angle and distance about the focus" : "the game's view of the staged monster";
            case "perspective":
                if (_parallel)
                {
                    Pressed(PerspectiveSide);
                }

                return "perspective";
            case "parallel":
                if (!_parallel)
                {
                    Pressed(PerspectiveSide);
                }

                return "parallel";
            case "reset" or "game":
                return "no model is shown yet";
            default:
                return $"'{button}' is not a view button (reset, game, perspective, parallel)";
        }
    }

    /// <summary>The view, as the pane holds it now.</summary>
    internal CameraPose AgentView => _view;

    /// <summary>
    /// Sets the view, each part left out kept as it is; false where no model is shown (a model that lands fits its own
    /// view) or the pose comes out invalid.
    /// </summary>
    internal bool AgentLook(Vector3? focus, float? turn, float? tilt, float? distance, bool? parallel)
    {
        if (!_model.Ready)
        {
            return false;
        }

        CameraPose now = _view;
        var pose = new CameraPose(
            focus ?? now.Focus, turn ?? now.Turn, tilt ?? now.Tilt, distance ?? now.Distance, parallel ?? now.Parallel);
        if (!pose.Valid)
        {
            return false;
        }

        _parallel = pose.Parallel;
        _view = pose;
        return true;
    }

    /// <summary>Plays an animation by its name (exact, then the first containing it), and starts or stops the clock.</summary>
    internal string AgentAnimate(string name, bool? playing)
    {
        var said = new List<string>();
        if (name.Length > 0)
        {
            IReadOnlyList<SkeletonAnimation> moves = _model.Rig.Animations;
            int found = -1;
            for (int i = 0; i < moves.Count && found < 0; i++)
            {
                if (string.Equals(moves[i].Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    found = i;
                }
            }

            for (int i = 0; i < moves.Count && found < 0; i++)
            {
                if (moves[i].Name.Contains(name, StringComparison.OrdinalIgnoreCase))
                {
                    found = i;
                }
            }

            if (found < 0)
            {
                said.Add(_pose is null ? "the model has no skeleton to play" : $"no animation named '{name}' among {moves.Count}");
            }
            else if (_pose is null)
            {
                said.Add("the model has no skeleton to play");
            }
            else
            {
                Choose(found);
                said.Add("playing " + moves[found].Name);
            }
        }

        if (playing is { } play)
        {
            _playing = play;
            said.Add(play ? "the clock runs" : "the clock stands");
        }

        return string.Join("; ", said);
    }

    /// <summary>Probes the pixel at a share of the picture across and down (0 to 1), as a click with the probe on does.</summary>
    internal void AgentProbe(float across, float down)
    {
        if (!_probing)
        {
            ProbeToggled();
        }

        _probeAt = new Vector2(Math.Clamp(across, 0f, 1f), Math.Clamp(down, 0f, 1f));
        _probeLines = [];
        _layerLines = [];
        _probeAsked = true;
    }

    /// <summary>Switches the probe off, as its button does.</summary>
    internal void AgentProbeOff()
    {
        if (_probing)
        {
            ProbeToggled();
        }
    }

    /// <summary>Everything the pane says of itself: what it shows, how, from where, and the lines under its picture.</summary>
    internal List<string> AgentLines(bool built)
    {
        var lines = new List<string>
        {
            $"book: {Book} - window {(WindowOpen ? "open" : "closed")}",
            "shows: " + (_wanted.Length > 0 ? _wanted : "nothing"),
        };
        if (Stage.Length > 0)
        {
            lines.Add("stage: " + Stage);
        }

        lines.Add("state: " + (_loading is { IsCompleted: false }
            ? _progress?.Said() ?? "building"
            : _model.Ready
                ? string.Create(CultureInfo.InvariantCulture, $"ready, {_model.Files} file(s)")
                : Why.Length > 0 ? "no model: " + Why : "no model"));
        lines.Add("drawn by: " + (_onCard ? "the card" : "the processor" + (_cardWhy.Length > 0 ? " - " + _cardWhy : string.Empty)));
        CameraPose view = _view;
        if (view.Valid)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"view: focus {view.Focus.X:F1} {view.Focus.Y:F1} {view.Focus.Z:F1} · turn {view.Turn:F4} · tilt {view.Tilt:F4} · distance {view.Distance:F1} · {(view.Parallel ? "parallel" : "perspective")}"));
        }

        if (_model.Ready && _model.Rig.Animations.Count > 0)
        {
            IReadOnlyList<SkeletonAnimation> moves = _model.Rig.Animations;
            string current = _chosen >= 0 && _chosen < moves.Count ? moves[_chosen].Name : "none";
            lines.Add($"animation: {current} ({(_playing ? "playing" : "standing")}), {moves.Count} in all: "
                + string.Join(", ", moves.Take(80).Select(move => move.Name)) + (moves.Count > 80 ? ", ..." : string.Empty));
        }

        lines.AddRange(_status.Select(line => line.Replace("%%", "%", StringComparison.Ordinal)));
        if (HasProbe)
        {
            lines.Add("probe:");
            lines.AddRange(_probeLines);
            lines.AddRange(_layerLines);
        }

        if (built)
        {
            lines.Add("built:");
            lines.AddRange(_built);
        }

        return lines;
    }
}
