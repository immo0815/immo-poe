using System.Globalization;
using PoEformance.Game.Files;

namespace PoEformance.Overlay;

/// <summary>The light panel's switches as the agent link sets them: by the label the panel shows beside each.</summary>
/// <remarks>
/// THE PANEL'S OWN LABELS, so what the agent asks for is what a person reading the panel sees. Each change moves the
/// panel's version as a click does (<see cref="Switch"/>), and the pane builds its light again the next frame.
/// Render thread only, as every control of the panel.
/// </remarks>
public sealed partial class SceneLightPanel
{
    /// <summary>Every switch by its label, and what reads and writes it.</summary>
    private (string Label, Func<bool> Read, Action<bool> Write)[] AgentSwitches() =>
    [
        ("game light", () => _on, value => _on = value),
        ("room", () => _points, value => _points = value),
        ("carried", () => _carried, value => _carried = value),
        ("sun", () => _sun, value => _sun = value),
        ("shadows", () => _shadows, value => _shadows = value),
        ("player", () => _playerLight, value => _playerLight = value),
        ("exposure", () => _exposure, value => _exposure = value),
        ("colour grade", () => _grade, value => _grade = value),
        ("emissive", () => _emissive, value => _emissive = value),
        ("glints", () => _glints, value => _glints = value),
        ("reflection", () => _reflection, value => _reflection = value),
        ("scene", () => _mirroring, value => _mirroring = value),
        ("refraction", () => _refraction, value => _refraction = value),
        ("caustics", () => _caustics, value => _caustics = value),
        ("crash", () => _crash, value => _crash = value),
        ("way round", () => _wayRound, value => _wayRound = value),
        ("past walls", () => _pastWalls, value => _pastWalls = value),
        ("break by ground", () => _breakByGround, value => _breakByGround = value),
        ("wetness", () => _wetness, value => _wetness = value),
        ("spray from occlusion", () => _sprayOccluded, value => _sprayOccluded = value),
        ("fog", () => _fog, value => _fog = value),
        ("clouds", () => _clouds, value => _clouds = value),
        ("roof fade", () => _roofFade, value => _roofFade = value),
        ("free sun", () => _freeSun, value => _freeSun = value),
    ];

    /// <summary>Sets switches and sliders by label; one line for each, saying what it is now or why it was not set.</summary>
    /// <param name="switches">Label to on or off.</param>
    /// <param name="values">Label to number: "lift", "wind" (degrees), "flat" (the flat ambient), "sun round" and "sun height" (degrees), "ambient" (0 none, 1 flat, 2 cube).</param>
    internal List<string> AgentSet(IReadOnlyDictionary<string, bool> switches, IReadOnlyDictionary<string, double> values)
    {
        var said = new List<string>();
        (string Label, Func<bool> Read, Action<bool> Write)[] known = AgentSwitches();
        foreach ((string label, bool value) in switches)
        {
            int at = Array.FindIndex(known, one => string.Equals(one.Label, label.Trim(), StringComparison.OrdinalIgnoreCase));
            if (at < 0)
            {
                said.Add($"no switch '{label}' - the switches are {string.Join(", ", known.Select(one => one.Label))}");
                continue;
            }

            known[at].Write(value);
            said.Add($"{known[at].Label}: {(value ? "on" : "off")}");
        }

        foreach ((string label, double value) in values)
        {
            float number = (float)value;
            switch (label.Trim().ToLowerInvariant())
            {
                case "lift":
                    _lift = Math.Clamp(number, 1f, 5f);
                    said.Add(string.Create(CultureInfo.InvariantCulture, $"lift: {_lift:F2}"));
                    break;
                case "wind":
                    _windTurn = Math.Clamp(number, -180f, 180f);
                    said.Add(string.Create(CultureInfo.InvariantCulture, $"wind: {_windTurn:F0} degrees"));
                    break;
                case "flat":
                    _flat = Math.Clamp(number, 0f, 4f);
                    said.Add(string.Create(CultureInfo.InvariantCulture, $"flat ambient: {_flat:F3}"));
                    break;
                case "ambient":
                    _ambient = Math.Clamp((int)MathF.Round(number), 0, 2);
                    said.Add($"ambient: {(SceneAmbient)_ambient}");
                    break;
                case "sun round":
                    _freeRound = number * MathF.PI / 180f;
                    said.Add(string.Create(CultureInfo.InvariantCulture, $"free sun round: {number:F1} degrees"));
                    break;
                case "sun height":
                    _freeHeight = Math.Clamp(number, 0f, 90f) * MathF.PI / 180f;
                    said.Add(string.Create(CultureInfo.InvariantCulture, $"free sun height: {_freeHeight * 180f / MathF.PI:F1} degrees"));
                    break;
                default:
                    said.Add($"no value '{label}' - the values are lift, wind, flat, ambient, sun round, sun height");
                    continue;
            }
        }

        _version++;
        return said;
    }

    /// <summary>Every switch and value as it stands.</summary>
    internal string AgentState()
        => string.Join(", ", AgentSwitches().Select(one => $"{one.Label} {(one.Read() ? "on" : "off")}"))
            + string.Create(
                CultureInfo.InvariantCulture,
                $"; lift {_lift:F2}, wind {_windTurn:F0}, ambient {(SceneAmbient)_ambient}, flat {_flat:F3}, environment {(_picked.Length > 0 ? _picked : "the area's own")}");
}
