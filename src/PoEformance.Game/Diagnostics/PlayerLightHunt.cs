using System.Buffers.Binary;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using PoEformance.Core.Diagnostics;
using PoEformance.Game.Files;

namespace PoEformance.Game.Diagnostics;

/// <summary>
/// Looks for the engine's player light in the game's memory - where it stands over the player, its colour, how far it reaches and which
/// way it points - beside every place its colour or the player's own place is kept, inside the light hunt's own sweep.
/// </summary>
/// <remarks>
/// WHAT THE ENGINE'S SHADERS SAY (lighting.ffx, whole in light-extras.txt): every dynamic light is lit by its type
/// (ComputeDynamicLightParams) - a spot (ComputeSpotLightParams: its colour times <c>1 - saturate(d / colour.w)</c> times
/// <c>saturate((rho - phi) / (theta - phi))</c>, theta its position's w and phi its direction's) or a point (ComputePointLightParamsNew:
/// its radius multiplier <c>lerp(1/sqrt(0.02), 100, saturate(position.w))</c>, "params of player light for backwards compatibility" at
/// nought). WHAT THEY DO NOT SAY: which of the two the player's light is, where it stands over him, how far it reaches, its w, its cone -
/// the processor sets them, from a .env that names only its colour, intensity, penumbra and shadows_enabled. The tool's lamp
/// (SceneLight.PlayerLamp) stands at a CHOSEN height and radius until this is read.
///
/// WHAT IS LOOKED FOR, in one pass with the light hunt's own needles (FloatHunt.Part hands each its own): the light's colour as the area's
/// file makes it - its colour times its intensity, the engine's default of one where the file sets none (the schema's
/// ParsedEnvironment.PlayerLightIntensity), and the colour alone too where the intensity is not one - and the player's own place, Render's
/// x and y. Round each place found <see cref="Around"/> bytes are read either way and what stands there is listed: three floats within
/// <see cref="Reach"/> of the player (how far off along x and y, how high over the ground under him), three in the colour's proportions (at
/// what scale of the file's), a unit vector (a spot's way, how far it leans from straight down), and single values matching the file's own
/// player light numbers - each with the float after it, a four-wide layout's w. A window holding both a place by the player and the colour
/// comes first.
///
/// A DIAGNOSTIC: nothing is concluded here. Every window is written whole as bytes, so the replay (<see cref="Replay"/>) lists the same
/// things from the file alone, and a better rule for what stands beside a place is held against the capture without the game.
/// </remarks>
public sealed class PlayerLightHunt
{
    /// <summary>The file the replay writes beside a capture's light-hunt.txt.</summary>
    public const string ReplayFile = "player-light-replay.txt";

    /// <summary>How many bytes either side of a place found are read and written.</summary>
    public const int Around = 0x180;

    /// <summary>How many places of each needle have their surroundings read.</summary>
    public const int MostWindows = 32;

    /// <summary>How far from the player - along x and y, and up or down from the ground under him - three floats count as a place by him, units.</summary>
    public const float Reach = 2000f;

    /// <summary>How near the player's x and y two floats must be to be taken for his place, units - a light that follows him may lag a little.</summary>
    public const float PlaceTolerance = 8f;

    /// <summary>How near each of the colour's floats must be.</summary>
    public const float ColourTolerance = 2e-4f;

    /// <summary>The engine's own player_light.penumbra where a file sets none, BY THE PARSED ENVIRONMENT'S ORDER - a reading, see the schema.</summary>
    /// <remarks>
    /// The value with a flag of nought at ParsedEnvironment +0x60, between player_light.intensity (+0x58) and post_processing's
    /// hdr_bloom_cutoff (+0x68, which the Canal Hideout's file sets) - the one float key of player_light besides the intensity. Pi over five.
    /// </remarks>
    public const float DefaultPenumbra = 0.62832f;

    /// <summary>How near two directions must be, per component of the unit vectors, for three floats to be a colour of the light's.</summary>
    private const float SameProportions = 1e-3f;

    /// <summary>The bytes a line of a window's dump holds.</summary>
    private const int BytesALine = 32;

    /// <summary>The most things listed for one window, the nearest the place first.</summary>
    private const int MostSights = 48;

    private static readonly Regex Heading = new(
        @"^=== player light window (\d+) of \d+: (.+) at 0x([0-9A-F]+) \(read from 0x([0-9A-F]+), (\d+) bytes\)$", RegexOptions.CultureInvariant);

    private static readonly Regex Head = new(
        @"^player light hunt: player (\S+) (\S+) (\S+), ground (\S+), colour (\S+) (\S+) (\S+), intensity (\S+), penumbra (\S+)", RegexOptions.CultureInvariant);

    private PlayerLightHunt(Inputs inputs, string from, IReadOnlyList<FloatNeedle> needles)
    {
        Of = inputs;
        From = from;
        Needles = needles;
    }

    /// <summary>What a hunt is worked out from: the player's place, the ground under him, and the area file's player light.</summary>
    /// <param name="Player">Render's x, y and z of the player.</param>
    /// <param name="Ground">The terrain's height under him - up is minus z, so a light over him has a z less than this.</param>
    /// <param name="Colour">The light's colour times its intensity, as the area's file makes it.</param>
    /// <param name="Intensity">The intensity that made it - the file's, or the engine's default one.</param>
    /// <param name="Penumbra">The file's player_light.penumbra, NaN where it sets none.</param>
    public readonly record struct Inputs(Vector3 Player, float Ground, Vector3 Colour, float Intensity, float Penumbra);

    /// <summary>One thing found beside a place: where, of which kind, and how it is said.</summary>
    /// <param name="Offset">Bytes from the place found.</param>
    /// <param name="Kind">near, colour, unit or value.</param>
    /// <param name="Said">Its line.</param>
    public sealed record Sight(int Offset, string Kind, string Said);

    /// <summary>One window read back from a report: which needle, where, and its bytes.</summary>
    /// <param name="Number">Its number in the report.</param>
    /// <param name="Needle">The needle's name as written.</param>
    /// <param name="At">Where the needle was found.</param>
    /// <param name="From">The window's first byte.</param>
    /// <param name="Bytes">The window.</param>
    public sealed record Window(int Number, string Needle, ulong At, ulong From, byte[] Bytes);

    /// <summary>What the hunt is worked out from.</summary>
    public Inputs Of { get; }

    /// <summary>Where each input came from, in words.</summary>
    public string From { get; }

    /// <summary>What to look for, to be put after the light hunt's own needles in one sweep.</summary>
    public IReadOnlyList<FloatNeedle> Needles { get; }

    /// <summary>A hunt for the player standing at <paramref name="player"/> over <paramref name="ground"/>, in an area whose .env is <paramref name="environment"/>.</summary>
    public static PlayerLightHunt For(EnvironmentSettings environment, Vector3 player, float ground)
    {
        ArgumentNullException.ThrowIfNull(environment);
        Vector3 colour = environment.PlayerColour ?? Vector3.One;
        float intensity = environment.PlayerIntensity ?? 1f;
        float penumbra = environment.All.FirstOrDefault(one => string.Equals(one.Key, "player_light.penumbra", StringComparison.OrdinalIgnoreCase)) is { Values.Count: 1 } set
            ? set.Values[0]
            : float.NaN;
        var inputs = new Inputs(player, ground, colour * intensity, intensity, penumbra);
        string from = string.Create(CultureInfo.InvariantCulture,
            $"  the colour {(environment.PlayerColour is null ? "absent from the area's file, white taken" : "the area's file's")} ({environment.Path}), times the intensity {(environment.PlayerIntensity is null ? "the engine's default one (absent from the file; ParsedEnvironment.PlayerLightIntensity, +0x58, holds 1 with a nought where no file sets it)" : "the file's")}; the penumbra {(float.IsNaN(penumbra) ? $"absent from the file - the engine's {Say(DefaultPenumbra)} at ParsedEnvironment +0x60 by the order of its keys, a reading" : "the file's")}");

        var needles = new List<FloatNeedle>
        {
            new($"the player light's colour as the file makes it ({Say(inputs.Colour)})", [inputs.Colour.X, inputs.Colour.Y, inputs.Colour.Z], ColourTolerance, Around, MostWindows),
        };

        if (MathF.Abs(intensity - 1f) > 1e-6f)
        {
            needles.Add(new($"its colour alone ({Say(colour)})", [colour.X, colour.Y, colour.Z], ColourTolerance, Around, MostWindows));
        }

        needles.Add(new($"the player's place, x and y ({Say(player.X)}, {Say(player.Y)})", [player.X, player.Y], PlaceTolerance, Around, MostWindows));
        return new PlayerLightHunt(inputs, from, needles);
    }

    /// <summary>The hunt's part of a finished sweep said: what was looked for, where it was found, the windows ranked, and each whole.</summary>
    /// <param name="result">The sweep's result for these needles alone - FloatHunt.Part.</param>
    public string Read(FloatHuntResult result)
    {
        ArgumentNullException.ThrowIfNull(result);
        var report = new StringBuilder();
        report.AppendLine("=== the player light, sought in the light hunt's own sweep (PlayerLightHunt)");
        report.AppendLine(HeadLine(Of));
        report.AppendLine(From);
        report.AppendLine("  why: lighting.ffx lights a dynamic light as a spot (its colour times 1 - saturate(d / colour.w), times saturate((rho - phi) / (theta - phi)),");
        report.AppendLine("  theta its position's w, phi its direction's) or a point (ComputePointLightParamsNew, its radius multiplier lerp(7.07, 100, position.w));");
        report.AppendLine("  where the player's light stands, how far it reaches and which it is, no file says. Beside every place its colour or the player's place is kept,");
        report.AppendLine("  what stands there is listed - a window holding a place by the player and the colour first. The tool's lamp is CHOSEN until this is read.");
        var places = new List<ulong>[Needles.Count];
        for (var at = 0; at < places.Length; at++)
        {
            places[at] = [];
        }

        foreach (FloatSighting one in result.Sightings)
        {
            if (one.Needle >= 0 && one.Needle < places.Length)
            {
                places[one.Needle].Add(one.At);
            }
        }

        var capped = new HashSet<int>(result.Capped);
        for (var at = 0; at < Needles.Count; at++)
        {
            report.Append(CultureInfo.InvariantCulture,
                $"  {Needles[at].Name}: {places[at].Count}{(capped.Contains(at) ? " places, more not kept" : " places")}{(places[at].Count > 0 ? " - " + string.Join(", ", places[at].Take(8).Select(place => $"0x{place:X}")) : string.Empty)}").AppendLine();
        }

        IReadOnlyList<FloatDump> dumps = result.Dumps ?? [];
        var windows = new List<Window>(dumps.Count);
        for (var at = 0; at < dumps.Count; at++)
        {
            FloatDump dump = dumps[at];
            windows.Add(new Window(at + 1, dump.Needle >= 0 && dump.Needle < Needles.Count ? Needles[dump.Needle].Name : "?", dump.At, dump.From, dump.Bytes));
        }

        List<(Window Window, List<Sight> Sights)> seen = [.. windows.Select(window => (window, Beside(window.Bytes, (int)(window.At - window.From), Of)))];
        Listed(report, seen);
        foreach ((Window window, List<Sight> sights) in seen)
        {
            report.AppendLine();
            report.Append(CultureInfo.InvariantCulture,
                $"=== player light window {window.Number} of {windows.Count}: {window.Needle} at 0x{window.At:X} (read from 0x{window.From:X}, {window.Bytes.Length} bytes)").AppendLine();
            report.Append(CultureInfo.InvariantCulture, $"  beside it, bytes from the place found - {sights.Count} things:").AppendLine();
            foreach (Sight sight in sights)
            {
                report.Append("    ").AppendLine(sight.Said);
            }

            report.AppendLine("  as floats, eight a row, each row's first offset from the place found:");
            Floats(report, window);
            report.Append(CultureInfo.InvariantCulture, $"  the window's bytes, {BytesALine} a line from its first:").AppendLine();
            for (var offset = 0; offset < window.Bytes.Length; offset += BytesALine)
            {
                report.Append(CultureInfo.InvariantCulture,
                    $"    {offset:X4}  {Convert.ToHexStringLower(window.Bytes.AsSpan(offset, Math.Min(BytesALine, window.Bytes.Length - offset)))}").AppendLine();
            }
        }

        return report.ToString();
    }

    /// <summary>
    /// What stands beside one place: three floats by the player, three in the colour's proportions, unit vectors, and the file's own values -
    /// the nearest the place first.
    /// </summary>
    /// <param name="window">The bytes read round the place.</param>
    /// <param name="place">Where in them the place is.</param>
    /// <param name="inputs">The player, the ground and the colour.</param>
    public static List<Sight> Beside(ReadOnlySpan<byte> window, int place, Inputs inputs)
    {
        var sights = new List<Sight>();
        Vector3 colour = inputs.Colour;
        float colourLength = colour.Length();
        Vector3 colourWay = colourLength > 0f ? colour / colourLength : Vector3.Zero;
        bool grey = MathF.Abs(colour.X - colour.Y) < 1e-6f && MathF.Abs(colour.Y - colour.Z) < 1e-6f;
        (float Value, string Name)[] values = Values(inputs);
        int floats = window.Length / sizeof(float);
        for (var at = 0; at < floats; at++)
        {
            int offset = (at * sizeof(float)) - place;
            float a = Float(window, at);
            if (at + 2 < floats)
            {
                float b = Float(window, at + 1), c = Float(window, at + 2);
                var triple = new Vector3(a, b, c);
                if (float.IsFinite(a) && float.IsFinite(b) && float.IsFinite(c))
                {
                    if (MathF.Abs(a - inputs.Player.X) <= Reach && MathF.Abs(b - inputs.Player.Y) <= Reach && MathF.Abs(c - inputs.Ground) <= Reach)
                    {
                        sights.Add(new Sight(offset, "near", string.Create(CultureInfo.InvariantCulture,
                            $"{Offset(offset)} near: x {Signed(a - inputs.Player.X)}, y {Signed(b - inputs.Player.Y)} off the player, {Say(inputs.Ground - c)} over the ground under him (z {Say(c)}){After(window, at, floats)}")));
                    }

                    float length = triple.Length();
                    if (!grey && colourLength > 0f && a > 0f && b > 0f && c > 0f && length > 1e-6f)
                    {
                        Vector3 way = triple / length;
                        float scale = length / colourLength;
                        if (MathF.Abs(way.X - colourWay.X) <= SameProportions && MathF.Abs(way.Y - colourWay.Y) <= SameProportions
                            && MathF.Abs(way.Z - colourWay.Z) <= SameProportions && scale is >= 1e-3f and <= 1e4f)
                        {
                            sights.Add(new Sight(offset, "colour", string.Create(CultureInfo.InvariantCulture,
                                $"{Offset(offset)} colour: {Say(triple)}, {Say(scale)} times the file's colour and intensity{After(window, at, floats)}")));
                        }
                    }

                    if (MathF.Abs(length - 1f) <= 1e-3f && Axes(triple) > 1)
                    {
                        float lean = MathF.Acos(Math.Clamp(c / length, -1f, 1f)) * 180f / MathF.PI;
                        sights.Add(new Sight(offset, "unit", string.Create(CultureInfo.InvariantCulture,
                            $"{Offset(offset)} unit: {Say(triple)}, {lean:0.##} degrees from straight down (+z){After(window, at, floats)}")));
                    }
                }
            }

            if (float.IsFinite(a) && a != 0f)
            {
                foreach ((float value, string name) in values)
                {
                    if (MathF.Abs(a - value) <= 1e-4f * MathF.Max(1f, MathF.Abs(value)))
                    {
                        sights.Add(new Sight(offset, "value", string.Create(CultureInfo.InvariantCulture,
                            $"{Offset(offset)} value: {Say(a)} = {name}{(at + 1 < floats ? "; then " + Said(window, at + 1) : string.Empty)}")));
                        break;
                    }
                }
            }
        }

        return [.. sights.OrderBy(one => Math.Abs(one.Offset)).ThenBy(one => one.Offset).Take(MostSights).OrderBy(one => one.Offset)];
    }

    /// <summary>The windows of a report read back, with the inputs its head line names - or none where it names none.</summary>
    public static (Inputs? Inputs, IReadOnlyList<Window> Windows) Read(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        Inputs? inputs = null;
        var windows = new List<Window>();
        Window? open = null;
        var bytes = new List<byte>();
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (inputs is null && Head.Match(line) is { Success: true } head)
            {
                inputs = new Inputs(
                    new Vector3(Parse(head.Groups[1].Value), Parse(head.Groups[2].Value), Parse(head.Groups[3].Value)),
                    Parse(head.Groups[4].Value),
                    new Vector3(Parse(head.Groups[5].Value), Parse(head.Groups[6].Value), Parse(head.Groups[7].Value)),
                    Parse(head.Groups[8].Value),
                    Parse(head.Groups[9].Value));
                continue;
            }

            if (line.StartsWith("=== ", StringComparison.Ordinal))
            {
                Close();
                if (Heading.Match(line) is { Success: true } heading)
                {
                    open = new Window(
                        int.Parse(heading.Groups[1].Value, CultureInfo.InvariantCulture),
                        heading.Groups[2].Value,
                        ulong.Parse(heading.Groups[3].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        ulong.Parse(heading.Groups[4].Value, NumberStyles.HexNumber, CultureInfo.InvariantCulture),
                        new byte[int.Parse(heading.Groups[5].Value, CultureInfo.InvariantCulture)]);
                }

                continue;
            }

            // A DUMP LINE: four spaces, the offset in hex, two spaces, the bytes in hex.
            if (open is not null && line.Length > 10 && line.StartsWith("    ", StringComparison.Ordinal) && line[8..10] == "  "
                && int.TryParse(line.AsSpan(4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int offset) && offset == bytes.Count)
            {
                try
                {
                    bytes.AddRange(Convert.FromHexString(line.AsSpan(10)));
                }
                catch (FormatException)
                {
                    // Not a dump line after all - a sight's line that happened to start alike.
                }
            }
        }

        Close();
        return (inputs, windows);

        void Close()
        {
            if (open is not null && bytes.Count == open.Bytes.Length)
            {
                bytes.CopyTo(open.Bytes);
                windows.Add(open);
            }

            open = null;
            bytes.Clear();
        }
    }

    /// <summary>A report's player light windows read back and ranked again by today's rules - the player light's part of light-hunt-replay.txt.</summary>
    public static string Replay(string text)
    {
        (Inputs? inputs, IReadOnlyList<Window> windows) = Read(text);
        var said = new StringBuilder();
        if (inputs is not { } known)
        {
            said.AppendLine("player light hunt replay: the report holds no player light hunt - an older capture, or one with no player read");
            return said.ToString();
        }

        said.Append(CultureInfo.InvariantCulture, $"player light hunt replay: {windows.Count} windows read back").AppendLine();
        said.AppendLine(HeadLine(known));
        Listed(said, [.. windows.Select(window => (window, Beside(window.Bytes, (int)(window.At - window.From), known)))]);
        return said.ToString();
    }

    /// <summary>The head line the replay reads the inputs back from.</summary>
    private static string HeadLine(Inputs inputs) => string.Create(CultureInfo.InvariantCulture,
        $"player light hunt: player {Say(inputs.Player.X)} {Say(inputs.Player.Y)} {Say(inputs.Player.Z)}, ground {Say(inputs.Ground)}, colour {Say(inputs.Colour.X)} {Say(inputs.Colour.Y)} {Say(inputs.Colour.Z)}, intensity {Say(inputs.Intensity)}, penumbra {(float.IsNaN(inputs.Penumbra) ? "absent" : Say(inputs.Penumbra))}");

    /// <summary>The windows ranked: those holding a place by the player and the colour first, each with what it holds.</summary>
    private static void Listed(StringBuilder report, IReadOnlyList<(Window Window, List<Sight> Sights)> windows)
    {
        var ranked = windows
            .Select(one => (one.Window, one.Sights, Rank: Rank(one.Sights)))
            .OrderByDescending(one => one.Rank)
            .ThenBy(one => one.Window.Number)
            .ToList();
        int both = ranked.Count(one => one.Rank >= 6);
        report.Append(CultureInfo.InvariantCulture,
            $"  {windows.Count} windows read, {both} holding both a place by the player and the light's colour - the likeliest first:").AppendLine();
        foreach ((Window window, List<Sight> sights, int rank) in ranked)
        {
            report.Append(CultureInfo.InvariantCulture,
                $"  window {window.Number}: {window.Needle} at 0x{window.At:X} - {sights.Count(one => one.Kind == "near")} near, {sights.Count(one => one.Kind == "colour")} colour, {sights.Count(one => one.Kind == "unit")} unit, {sights.Count(one => one.Kind == "value")} value").AppendLine();
            if (rank >= 6)
            {
                foreach (Sight sight in sights)
                {
                    report.Append("      ").AppendLine(sight.Said);
                }
            }
        }
    }

    /// <summary>How likely a window is to hold the light: a place by the player counts four, the colour two, a unit vector one.</summary>
    private static int Rank(List<Sight> sights)
        => (sights.Exists(one => one.Kind == "near") ? 4 : 0) + (sights.Exists(one => one.Kind == "colour") ? 2 : 0) + (sights.Exists(one => one.Kind == "unit") ? 1 : 0);

    /// <summary>The single values worth naming where they stand: the file's penumbra or the engine's, as an angle and as the cosines a cone is kept by, and the intensity.</summary>
    private static (float Value, string Name)[] Values(Inputs inputs)
    {
        var values = new List<(float, string)>();
        foreach ((float penumbra, string whose) in (ReadOnlySpan<(float, string)>)[(inputs.Penumbra, "the file's"), (DefaultPenumbra, "the engine's (a reading)")])
        {
            if (!float.IsFinite(penumbra) || penumbra <= 0f)
            {
                continue;
            }

            values.Add((penumbra, $"{whose} penumbra"));
            values.Add((MathF.Cos(penumbra), $"the cosine of {whose} penumbra"));
            values.Add((MathF.Cos(penumbra * 0.5f), $"the cosine of half {whose} penumbra"));
        }

        if (MathF.Abs(inputs.Intensity - 1f) > 1e-6f)
        {
            values.Add((inputs.Intensity, "the file's intensity"));
        }

        return [.. values];
    }

    /// <summary>A window as rows of eight floats, each row's first offset from the place found, the needle's place marked.</summary>
    private static void Floats(StringBuilder report, Window window)
    {
        int count = window.Bytes.Length / sizeof(float);
        long origin = (long)(window.At - window.From);
        for (var at = 0; at < count; at += 8)
        {
            long offset = (at * sizeof(float)) - origin;
            report.Append(CultureInfo.InvariantCulture, $"  {(offset < 0 ? "-" : "+")}0x{Math.Abs(offset):X3}:");
            for (int one = at; one < Math.Min(at + 8, count); one++)
            {
                bool own = (one * sizeof(float)) - origin == 0;
                report.Append(own ? " [" : "  ").Append(Said(window.Bytes, one)).Append(own ? "]" : string.Empty);
            }

            report.AppendLine();
        }
    }

    /// <summary>The float after three - a four-wide layout's w - said, or nothing at the window's end.</summary>
    private static string After(ReadOnlySpan<byte> window, int at, int floats) => at + 3 < floats ? "; then " + Said(window, at + 3) : string.Empty;

    /// <summary>How many of three floats are not nought - one is an axis, which every matrix and box holds.</summary>
    private static int Axes(Vector3 value) => (MathF.Abs(value.X) > 1e-6f ? 1 : 0) + (MathF.Abs(value.Y) > 1e-6f ? 1 : 0) + (MathF.Abs(value.Z) > 1e-6f ? 1 : 0);

    private static float Float(ReadOnlySpan<byte> bytes, int index) => BinaryPrimitives.ReadSingleLittleEndian(bytes[(index * sizeof(float))..]);

    /// <summary>One word as a float where it reads as one, else its bits.</summary>
    private static string Said(ReadOnlySpan<byte> bytes, int index)
    {
        float value = Float(bytes, index);
        return float.IsFinite(value) && (value == 0f || MathF.Abs(value) is >= 1e-6f and < 1e7f)
            ? value.ToString("0.#####", CultureInfo.InvariantCulture)
            : string.Create(CultureInfo.InvariantCulture, $"#{BinaryPrimitives.ReadUInt32LittleEndian(bytes[(index * sizeof(float))..]):X8}");
    }

    private static float Parse(string text) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ? value : float.NaN;

    private static string Offset(int offset) => offset >= 0
        ? string.Create(CultureInfo.InvariantCulture, $"+0x{offset:X3}")
        : string.Create(CultureInfo.InvariantCulture, $"-0x{-offset:X3}");

    private static string Signed(float value) => (value >= 0f ? "+" : string.Empty) + Say(value);

    private static string Say(float value) => value.ToString("G7", CultureInfo.InvariantCulture);

    private static string Say(Vector3 value) => string.Create(CultureInfo.InvariantCulture, $"{Say(value.X)} {Say(value.Y)} {Say(value.Z)}");
}
