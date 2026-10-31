using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Features;

/// <summary>
/// How much of each roof section the game hides round the player, measured on the game's own picture - beside where the section stands to
/// the player and to the camera, and the roof fade's words its object, its area's master and its environment set.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT (2026-10-31, the third of the four, "measure first"): the shader side is settled - a section wears the
/// engine's roof_fade.fxgraph, an AlphaTest of the object uniform <c>roof_fade</c> against world-space noise, so it dissolves through a
/// speckle as the value falls from one to nought - but the value is the program's (ClientTileMap::CalculateRoofFadeRanges, RoofFadeMode,
/// the cylinders, boxes and circles some files set; light-extras.txt section 10). Before a rule is given, this part measures it.
///
/// THE STAGE IS DRAWN THREE TIMES through the game's camera (CaptureAtGame): as the light parts draw it, with every roof section; with the
/// sections drawn as shadow casters alone (MaterialBlend.ShadowOnly) - what lies behind them, their shadows kept; and with the sections left
/// out of the mesh - what lies behind them, their shadows gone too. Where the game hides a section its picture shows what lies behind it.
/// Over each section's gathered pixels where the drawing with it and the one without it differ (CHOSEN: their luminance a fifth of the
/// brighter apart at least), the game's light is fitted as <c>a with + b without</c> (non-negative least squares, the three channels), once
/// for each way without: the HIDDEN SHARE <c>b / (a + b)</c> - nought shown whole, one hidden whole - and <c>a + b</c> the game's light over
/// the pane's, let free since the pane's light is not the game's (the clouds, the fog, the lamp). Which way without fits the better says
/// whether the game keeps a hidden section's shadow. The noise the engine dissolves through is finer than a gathered pixel, so a share
/// between nought and one is a section part hidden, not a blend.
///
/// BESIDE EACH SECTION: its box in the area; how far across and in space it stands from the player; how near the line from the game's eye to
/// the player passes it, and where along that line (nought the eye, one the player); its nearest pixel to the player's on the screen; its
/// nearest depth against the player's; and its object's roof fade words (the shape's source, an .ao, and what it extends). Then the area's:
/// its master's and environment's roof fade lines.
/// </remarks>
public static class CaptureRoofFade
{
    /// <summary>The numbers the report is worked out from, beside it.</summary>
    public const string Numbers = "roof-fade.bin";

    /// <summary>The game's picture with each section's pixels tinted by how hidden they are, beside it.</summary>
    public const string Tinted = "roof-fade.png";

    /// <summary>The offline reader's file, beside the capture.</summary>
    public const string ReplayFile = "roof-fade-replay.txt";

    /// <summary>How far apart the two drawings' luminance must stand at a pixel, over the brighter, for it to tell shown from hidden - CHOSEN.</summary>
    internal const float Telling = 0.2f;

    /// <summary>The fewest telling pixels a section is fitted over.</summary>
    internal const int FewestTelling = 12;

    /// <summary>How many points along the line from the eye to the player its nearness to a section's box is sought at.</summary>
    private const int LineSteps = 256;

    /// <summary>The most extends hops an object's words are followed through.</summary>
    private const int MostHops = 4;

    private const int Magic = 0x44465250; // "PRFD"

    private const int Version = 1;

    /// <summary>One gathered pixel of a roof section: the game's light and the three drawings', linear, where it stands and how deep.</summary>
    /// <param name="Shape">The section's shape.</param>
    /// <param name="Drawn">The drawn pixel.</param>
    /// <param name="Game">The game's light there.</param>
    /// <param name="With">The drawing with the sections.</param>
    /// <param name="Kept">The drawing without them, their shadows kept.</param>
    /// <param name="Gone">The drawing without them, their shadows gone.</param>
    /// <param name="Place">Its place, the stage's space.</param>
    /// <param name="Depth">Its depth along the view.</param>
    public readonly record struct Sample(int Shape, int Drawn, Vector3 Game, Vector3 With, Vector3 Kept, Vector3 Gone, Vector3 Place, float Depth);

    /// <summary>A section of the stage, as the numbers keep it.</summary>
    /// <param name="Shape">Its shape.</param>
    /// <param name="Material">The material it wears.</param>
    /// <param name="Source">What it came from - an .ao, a tile's file.</param>
    /// <param name="Least">Its box's least corner, the stage's space.</param>
    /// <param name="Most">Its box's most.</param>
    public sealed record Section(int Shape, string Material, string Source, Vector3 Least, Vector3 Most);

    /// <summary>What the numbers hold beside the samples: the drawing's size, the stage's corner, the player, the game's eye and its way.</summary>
    public sealed record Header(
        int Width, int Height, int Factor, Vector2 Origin, Vector3 Player, Vector3 Eye, Vector3 Forward, Vector2 PlayerPixel, IReadOnlyList<Section> Sections);

    /// <summary>A fit of the game's light as <c>a with + b without</c>.</summary>
    /// <param name="A">The share the drawing with the section stands for.</param>
    /// <param name="B">The share the drawing without it stands for.</param>
    /// <param name="Error">What is left, its root mean square over the game's.</param>
    /// <param name="Count">How many pixels it was fitted over.</param>
    public readonly record struct Fit(float A, float B, float Error, int Count)
    {
        /// <summary>How much of the section the game hides: <c>b / (a + b)</c>, NaN where nothing was fitted.</summary>
        public float Hidden => A + B > 0f ? B / (A + B) : float.NaN;
    }

    /// <summary>
    /// The part: every roof section the chosen stage draws, measured against the game's picture - its text; the tinted picture and the numbers
    /// handed back.
    /// </summary>
    /// <param name="read">The install's files - the materials' .mat, the objects' .ao, the master and the environment.</param>
    /// <param name="scene">The stage drawn through the game's camera with the game's picture (CaptureAtGame.Draw).</param>
    /// <param name="environment">The area's environment, or empty.</param>
    /// <param name="search">Where it says how far it is, or null.</param>
    /// <param name="pictures">The tinted picture, or none.</param>
    /// <param name="numbers">The numbers, or none.</param>
    public static string Write(
        Func<string, byte[]?>? read, CaptureAtGame scene, string environment, CaptureAtGame.Search? search,
        out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers)
    {
        ArgumentNullException.ThrowIfNull(scene);
        pictures = [];
        numbers = [];
        MonsterModel model = scene.Pane.Model;
        SkinnedMesh mesh = model.Mesh;
        var roofs = new CaptureLightCompare.RoofSections(read);
        var roof = new bool[mesh.Shapes.Count];
        var sections = new List<Section>();
        for (var shape = 0; shape < roof.Length; shape++)
        {
            string name = CaptureLightCompare.MaterialName(model, shape, roofs);
            if (!name.StartsWith(CaptureLightCompare.RoofWord, StringComparison.Ordinal) || mesh.Shapes[shape].Count == 0)
            {
                continue;
            }

            roof[shape] = true;
            (Vector3 least, Vector3 most) = BoxOf(mesh, shape);
            string source = shape < model.ShapeSources.Count ? model.ShapeSources[shape] : string.Empty;
            sections.Add(new Section(shape, name[CaptureLightCompare.RoofWord.Length..], source, least, most));
        }

        var text = new StringBuilder();
        if (sections.Count == 0)
        {
            text.Append("roof fade: no roof sections on the stage - ").AppendLine(roofs.Said());
            text.AppendLine(string.Join(Environment.NewLine, scene.Said));
            return text.ToString();
        }

        // THE TWO DRAWINGS WITHOUT THE SECTIONS: drawn as shadow casters alone, and left out of the mesh.
        search?.Begin(2);
        search?.Step("drawing the stage again without its roof sections, their shadows kept");
        MaterialBlend[] blends = new MaterialBlend[mesh.Shapes.Count];
        for (var shape = 0; shape < blends.Length; shape++)
        {
            blends[shape] = roof[shape] ? MaterialBlend.ShadowOnly
                : scene.Pane.Blends is { } given && shape < given.Count ? given[shape] : MaterialBlend.Opaque;
        }

        byte[] kept = scene.Redrawn(mesh, blends);
        search?.Step("drawing the stage again without its roof sections, their shadows gone");
        int[] shapeOf = scene.ShapeOf;
        var keep = new bool[shapeOf.Length];
        for (var triangle = 0; triangle < keep.Length; triangle++)
        {
            keep[triangle] = shapeOf[triangle] < 0 || !roof[shapeOf[triangle]];
        }

        MaterialBlend[] own = [.. Enumerable.Range(0, blends.Length).Select(shape => roof[shape] ? MaterialBlend.Opaque : blends[shape])];
        byte[] gone = scene.Redrawn(mesh.Keeping(keep), own);

        // EVERY GATHERED PIXEL OF A SECTION the game's picture shows plainly: not masked, not at its ceiling, something behind it on the stage.
        var samples = new List<Sample>();
        var behindNothing = 0;
        PixelGather gather = scene.Gather;
        for (var at = 0; at < gather.Triangle.Length; at++)
        {
            int triangle = gather.Triangle[at];
            int drawn = triangle < 0 ? -1 : scene.DrawnAt(at);
            if (drawn < 0 || triangle >= shapeOf.Length || shapeOf[triangle] < 0 || !roof[shapeOf[triangle]]
                || scene.Masked[drawn] || (scene.Clipped.Length > drawn && scene.Clipped[drawn]))
            {
                continue;
            }

            Vector3 game = scene.GameAt(drawn);
            if (float.IsNaN(game.X))
            {
                continue;
            }

            if (kept[(drawn * 4) + 3] == 0 || gone[(drawn * 4) + 3] == 0)
            {
                behindNothing++;
                continue;
            }

            samples.Add(new Sample(
                shapeOf[triangle], drawn, game, LinearOf(scene.Picture, drawn), LinearOf(kept, drawn), LinearOf(gone, drawn), gather.Place[at], gather.Depth[at]));
        }

        GameCamera camera = scene.Camera;
        Vector2 playerPixel = float.IsNaN(scene.PlayerWorld.X) ? new Vector2(float.NaN) : camera.Pixel(scene.PlayerWorld) / scene.Factor;
        var header = new Header(
            scene.DrawnWidth, scene.DrawnHeight, scene.Factor, scene.Origin, scene.PlayerWorld, camera.Eye, camera.Forward, playerPixel, sections);
        numbers = Write(header, samples);

        text.Append(Report(header, samples, "roof fade"));
        text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{behindNothing:N0} section pixels left out: nothing of the stage behind them, the game's picture there showing what lies past it"));
        text.AppendLine(roofs.Said());

        // EACH SECTION'S OBJECT'S WORDS, then the area's.
        text.AppendLine().AppendLine("=== the sections' objects' roof fade words (the shape's source and what it extends, any case)");
        foreach (string source in sections.Select(one => one.Source).Where(one => one.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.Ordinal))
        {
            text.Append("  ").Append(source).Append(": ").AppendLine(read is null ? "no install to read" : Words(read, source));
        }

        text.AppendLine().AppendLine("=== the area's roof fade words");
        text.Append("  master ").Append(model.MasterFile.Length > 0 ? model.MasterFile : "(none named by the stage)").Append(": ")
            .AppendLine(read is null || model.MasterFile.Length == 0 ? "not read" : Words(read, model.MasterFile));
        text.Append("  environment ").Append(environment.Length > 0 ? environment : "(none)").Append(": ")
            .AppendLine(read is null || environment.Length == 0 ? "not read" : Words(read, environment));

        text.AppendLine().AppendLine(string.Join(Environment.NewLine, scene.Said));
        text.AppendLine(Tinted + ": the game's picture dimmed, each section's telling pixels tinted from green, shown as the pane draws it, to red, hidden - by the share each pixel's own light asks under its section's fit with the shadows kept; grey a section pixel that tells nothing");
        text.AppendLine(Format);
        pictures = [(Tinted, Tint(scene, header, samples))];
        return text.ToString();
    }

    /// <summary>What the numbers file holds, for the report's last line.</summary>
    private const string Format = Numbers + ": little-endian: \"PRFD\", int version 1, int drawn width, int drawn height, int factor, floats the stage's corner x y, the player in the world x y z (NaN none), the game's eye x y z, its forward x y z, the player's drawn pixel x y; int sections, each: int shape, two ushort-length UTF-8 strings (material, source), floats its box's least x y z and most x y z (the stage's space); int samples, each: int shape, int drawn pixel, floats the game's linear rgb, the drawing with the sections, without them their shadows kept, without them their shadows gone, its place x y z, its depth";

    /// <summary>The report over the numbers - the part's and the replay's alike.</summary>
    internal static string Report(Header header, IReadOnlyList<Sample> samples, string title)
    {
        ArgumentNullException.ThrowIfNull(header);
        ArgumentNullException.ThrowIfNull(samples);
        var text = new StringBuilder();
        ILookup<int, Sample> byShape = samples.ToLookup(one => one.Shape);
        var fitted = header.Sections
            .Select(section => (Section: section, Kept: Fitted(byShape[section.Shape], kept: true), Gone: Fitted(byShape[section.Shape], kept: false), Pixels: byShape[section.Shape].Count()))
            .ToList();
        int telling = fitted.Sum(one => one.Kept.Count);
        int hidden = fitted.Count(one => one.Kept.Count >= FewestTelling && one.Kept.Hidden >= 0.5f);
        int shown = fitted.Count(one => one.Kept.Count >= FewestTelling && one.Kept.Hidden < 0.5f);
        text.Append(CultureInfo.InvariantCulture,
            $"{title}: {header.Sections.Count} roof sections on the stage, {fitted.Count(one => one.Pixels > 0)} on the screen, {samples.Count:N0} of their pixels compared, {telling:N0} telling shown from hidden; {hidden} mostly hidden by the game, {shown} mostly shown, {fitted.Count(one => one.Kept.Count < FewestTelling)} too few telling pixels").AppendLine();
        text.AppendLine(Player(header));
        text.AppendLine();
        text.AppendLine("=== each section: hidden share b / (a + b) of the game's light fitted as a with + b without, with the shadows kept and gone; where it stands");
        text.AppendLine("  shape\tmaterial\tpixels\ttelling\thidden (shadows kept)\ta+b\terror\thidden (shadows gone)\ta+b\terror\tshadow\tacross to the player\tin space\teye-player line passes\tat\ton the screen\tdepth less the player's\tbox in the area\tsource");
        foreach ((Section section, Fit fitKept, Fit fitGone, int pixels) in fitted.OrderByDescending(one => one.Pixels))
        {
            Sample[] own = [.. byShape[section.Shape]];
            text.Append(CultureInfo.InvariantCulture, $"  {section.Shape}\t{section.Material}\t{pixels:N0}\t{fitKept.Count:N0}\t");
            text.Append(FitSaid(fitKept)).Append('\t').Append(FitSaid(fitGone)).Append('\t');
            text.Append(fitKept.Count < FewestTelling || fitGone.Count < FewestTelling ? "-"
                : MathF.Abs(fitKept.Error - fitGone.Error) < 0.005f ? "alike" : fitKept.Error < fitGone.Error ? "kept" : "gone").Append('\t');
            text.Append(Where(header, section, own)).Append('\t').AppendLine(section.Source);
        }

        return text.ToString();
    }

    /// <summary>A fit in the report: hidden share, scale and error, or a dash.</summary>
    private static string FitSaid(Fit fit)
        => fit.Count < FewestTelling ? "-\t-\t-"
            : string.Create(CultureInfo.InvariantCulture, $"{fit.Hidden:0.00}\t{fit.A + fit.B:0.###}\t{fit.Error:0.000}");

    /// <summary>The player's line: where he stands and how the game's eye looks at him.</summary>
    private static string Player(Header header)
    {
        if (float.IsNaN(header.Player.X))
        {
            return "the player: none read - the sections' places to him are not measured";
        }

        Vector3 local = header.Player - new Vector3(header.Origin, 0f);
        float depth = Vector3.Dot(header.Player - header.Eye, header.Forward);
        return string.Create(CultureInfo.InvariantCulture,
            $"the player at ({header.Player.X:0.#}, {header.Player.Y:0.#}, {header.Player.Z:0.#}) in the area, ({local.X:0.#}, {local.Y:0.#}, {local.Z:0.#}) on the stage, at drawn pixel ({header.PlayerPixel.X:0.#}, {header.PlayerPixel.Y:0.#}) of {header.Width} x {header.Height} (1/{header.Factor} of the screen), {depth:0.#} units along the view from the game's eye at ({header.Eye.X:0.#}, {header.Eye.Y:0.#}, {header.Eye.Z:0.#})");
    }

    /// <summary>Where a section stands to the player and the game's eye - the report's columns.</summary>
    private static string Where(Header header, Section section, IReadOnlyList<Sample> own)
    {
        Vector3 origin = new(header.Origin, 0f);
        Vector3 least = section.Least + origin, most = section.Most + origin;
        string box = string.Create(CultureInfo.InvariantCulture, $"({least.X:0}, {least.Y:0}, {least.Z:0}) to ({most.X:0}, {most.Y:0}, {most.Z:0})");
        if (float.IsNaN(header.Player.X))
        {
            return "-\t-\t-\t-\t-\t-\t" + box;
        }

        Vector3 player = header.Player;
        float across = Vector2.Distance(new Vector2(player.X, player.Y), Vector2.Clamp(new Vector2(player.X, player.Y), new Vector2(least.X, least.Y), new Vector2(most.X, most.Y)));
        float space = Vector3.Distance(player, Vector3.Clamp(player, least, most));

        // THE LINE FROM THE EYE TO THE PLAYER: its nearest pass by the box, and where along it.
        float nearest = float.MaxValue, along = 0f;
        for (var step = 0; step <= LineSteps; step++)
        {
            float share = step / (float)LineSteps;
            Vector3 point = Vector3.Lerp(header.Eye, player, share);
            float distance = Vector3.Distance(point, Vector3.Clamp(point, least, most));
            if (distance < nearest)
            {
                (nearest, along) = (distance, share);
            }
        }

        float screen = float.NaN, nearestDepth = float.NaN;
        foreach (Sample one in own)
        {
            int x = one.Drawn % header.Width, y = one.Drawn / header.Width;
            float distance = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), header.PlayerPixel);
            screen = float.IsNaN(screen) ? distance : MathF.Min(screen, distance);
            nearestDepth = float.IsNaN(nearestDepth) ? one.Depth : MathF.Min(nearestDepth, one.Depth);
        }

        float playerDepth = Vector3.Dot(player - header.Eye, header.Forward);
        return string.Create(CultureInfo.InvariantCulture,
            $"{across:0}\t{space:0}\t{nearest:0}\t{along:0.00}\t{(float.IsNaN(screen) ? "-" : screen.ToString("0", CultureInfo.InvariantCulture))}\t{(float.IsNaN(nearestDepth) ? "-" : (nearestDepth - playerDepth).ToString("0", CultureInfo.InvariantCulture))}\t{box}");
    }

    /// <summary>
    /// The game's light fitted as <c>a with + b without</c> over a section's telling pixels - non-negative least squares on the three channels,
    /// one way without or the other.
    /// </summary>
    internal static Fit Fitted(IEnumerable<Sample> samples, bool kept)
    {
        double ww = 0, wb = 0, bb = 0, gw = 0, gb = 0, gg = 0;
        var count = 0;
        foreach (Sample one in samples)
        {
            Vector3 without = kept ? one.Kept : one.Gone;
            float lit = Luminance(one.With), dark = Luminance(without);
            if (MathF.Abs(lit - dark) < Telling * MathF.Max(lit, dark) || MathF.Max(lit, dark) <= 0f)
            {
                continue;
            }

            count++;
            ww += Vector3.Dot(one.With, one.With);
            wb += Vector3.Dot(one.With, without);
            bb += Vector3.Dot(without, without);
            gw += Vector3.Dot(one.Game, one.With);
            gb += Vector3.Dot(one.Game, without);
            gg += Vector3.Dot(one.Game, one.Game);
        }

        if (count == 0 || gg <= 0)
        {
            return new Fit(0f, 0f, float.NaN, count);
        }

        // THE NORMAL EQUATIONS, then the faces of the quarter they may not leave.
        double a, b;
        double det = (ww * bb) - (wb * wb);
        a = det > 1e-12 ? ((gw * bb) - (gb * wb)) / det : -1;
        b = det > 1e-12 ? ((gb * ww) - (gw * wb)) / det : -1;
        if (a < 0 || b < 0)
        {
            double onlyA = ww > 0 ? Math.Max(0, gw / ww) : 0, onlyB = bb > 0 ? Math.Max(0, gb / bb) : 0;
            double leftA = gg - (2 * onlyA * gw) + (onlyA * onlyA * ww), leftB = gg - (2 * onlyB * gb) + (onlyB * onlyB * bb);
            (a, b) = leftA <= leftB ? (onlyA, 0d) : (0d, onlyB);
        }

        double left = gg - (2 * ((a * gw) + (b * gb))) + (a * a * ww) + (2 * a * b * wb) + (b * b * bb);
        return new Fit((float)a, (float)b, (float)Math.Sqrt(Math.Max(0, left) / gg), count);
    }

    /// <summary>The game's picture dimmed, the sections' telling pixels tinted from green, shown, to red, hidden; grey a section pixel that tells nothing.</summary>
    private static GamePicture Tint(CaptureAtGame scene, Header header, IReadOnlyList<Sample> samples)
    {
        int width = header.Width, height = header.Height;
        var rgba = new byte[width * height * 4];
        for (var at = 0; at < width * height; at++)
        {
            Vector3 game = scene.GameAt(at);
            Vector3 shown = float.IsNaN(game.X) ? Vector3.Zero : game * 0.4f;
            rgba[at * 4] = CaptureAtGame.Encoded(shown.X);
            rgba[(at * 4) + 1] = CaptureAtGame.Encoded(shown.Y);
            rgba[(at * 4) + 2] = CaptureAtGame.Encoded(shown.Z);
            rgba[(at * 4) + 3] = 255;
        }

        Dictionary<int, Fit> fits = samples.GroupBy(one => one.Shape).ToDictionary(one => one.Key, one => Fitted(one, kept: true));
        foreach (Sample one in samples)
        {
            Fit fit = fits[one.Shape];
            float lit = Luminance(one.With), dark = Luminance(one.Kept);
            int to = one.Drawn * 4;
            if (fit.Count < FewestTelling || MathF.Abs(lit - dark) < Telling * MathF.Max(lit, dark))
            {
                (rgba[to], rgba[to + 1], rgba[to + 2]) = ((byte)128, (byte)128, (byte)128);
                continue;
            }

            // THE PIXEL'S OWN SHARE under its section's scale: the game's light over a + b, between the two drawings.
            float scale = MathF.Max(1e-6f, fit.A + fit.B);
            Vector3 span = one.Kept - one.With;
            float hidden = Math.Clamp(Vector3.Dot((one.Game / scale) - one.With, span) / MathF.Max(1e-9f, Vector3.Dot(span, span)), 0f, 1f);
            (rgba[to], rgba[to + 1], rgba[to + 2]) = ((byte)(255 * hidden), (byte)(255 * (1f - hidden)), (byte)40);
        }

        return new GamePicture(width, height, rgba);
    }

    /// <summary>A file's lines naming a roof fade word, followed through what it extends - or that none does.</summary>
    private static string Words(Func<string, byte[]?> read, string path)
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string? next = path;
        for (var hop = 0; hop < MostHops && next is not null && seen.Add(next); hop++)
        {
            byte[]? bytes = read(next);
            if (bytes is not { Length: > 0 })
            {
                lines.Add(hop == 0 ? "not in the install" : next + " not in the install");
                break;
            }

            string content = StatDescriptionFiles.Decode(bytes);
            string? extends = null;
            foreach (string raw in content.Split('\n'))
            {
                string line = raw.Trim();
                if (CaptureLightExtras.RoofFadeWords.Any(word => line.Contains(word, StringComparison.OrdinalIgnoreCase)))
                {
                    lines.Add((hop == 0 ? string.Empty : next + ": ") + line);
                }

                if (extends is null && line.StartsWith("extends", StringComparison.Ordinal) && line.IndexOf('"', StringComparison.Ordinal) is var open and >= 0
                    && line.IndexOf('"', open + 1) is var close and > 0)
                {
                    string parent = line[(open + 1)..close];
                    extends = parent.Length == 0 || parent.Contains("nothing", StringComparison.OrdinalIgnoreCase) ? null
                        : Path.HasExtension(parent) ? parent : parent + Path.GetExtension(path);
                }
            }

            next = extends;
        }

        return lines.Count == 0 ? "names no roof fade word" : string.Join(" | ", lines);
    }

    /// <summary>A shape's box over the vertices its triangles use, the stage's space.</summary>
    private static (Vector3 Least, Vector3 Most) BoxOf(SkinnedMesh mesh, int shape)
    {
        MeshShape part = mesh.Shapes[shape];
        var least = new Vector3(float.MaxValue);
        var most = new Vector3(float.MinValue);
        for (int at = part.From; at < part.From + part.Count && at < mesh.Indices.Length; at++)
        {
            Vector3 place = mesh.Positions[mesh.Indices[at]];
            least = Vector3.Min(least, place);
            most = Vector3.Max(most, place);
        }

        return (least, most);
    }

    private static Vector3 LinearOf(byte[] rgba, int drawn)
    {
        float[] linear = CaptureAtGame.Linear;
        int at = drawn * 4;
        return new Vector3(linear[rgba[at]], linear[rgba[at + 1]], linear[rgba[at + 2]]);
    }

    private static float Luminance(Vector3 colour) => (0.2126f * colour.X) + (0.7152f * colour.Y) + (0.0722f * colour.Z);

    /// <summary>The numbers, as <see cref="Format"/> says.</summary>
    internal static byte[] Write(Header header, IReadOnlyList<Sample> samples)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            writer.Write(Magic);
            writer.Write(Version);
            writer.Write(header.Width);
            writer.Write(header.Height);
            writer.Write(header.Factor);
            Floats(writer, header.Origin.X, header.Origin.Y, header.Player.X, header.Player.Y, header.Player.Z, header.Eye.X, header.Eye.Y, header.Eye.Z,
                header.Forward.X, header.Forward.Y, header.Forward.Z, header.PlayerPixel.X, header.PlayerPixel.Y);
            writer.Write(header.Sections.Count);
            foreach (Section section in header.Sections)
            {
                writer.Write(section.Shape);
                Text(writer, section.Material);
                Text(writer, section.Source);
                Floats(writer, section.Least.X, section.Least.Y, section.Least.Z, section.Most.X, section.Most.Y, section.Most.Z);
            }

            writer.Write(samples.Count);
            foreach (Sample one in samples)
            {
                writer.Write(one.Shape);
                writer.Write(one.Drawn);
                Floats(writer, one.Game.X, one.Game.Y, one.Game.Z, one.With.X, one.With.Y, one.With.Z, one.Kept.X, one.Kept.Y, one.Kept.Z,
                    one.Gone.X, one.Gone.Y, one.Gone.Z, one.Place.X, one.Place.Y, one.Place.Z, one.Depth);
            }
        }

        return stream.ToArray();
    }

    /// <summary>The numbers read back - see <see cref="Format"/>.</summary>
    internal static (Header Header, List<Sample> Samples) Read(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        if (reader.ReadInt32() != Magic || reader.ReadInt32() is not Version)
        {
            throw new InvalidDataException("not roof-fade numbers of version " + Version);
        }

        int width = reader.ReadInt32(), height = reader.ReadInt32(), factor = reader.ReadInt32();
        var origin = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        Vector3 player = Vector(reader), eye = Vector(reader), forward = Vector(reader);
        var pixel = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        int count = reader.ReadInt32();
        var sections = new List<Section>(count);
        for (var at = 0; at < count; at++)
        {
            int shape = reader.ReadInt32();
            string material = Text(reader), source = Text(reader);
            sections.Add(new Section(shape, material, source, Vector(reader), Vector(reader)));
        }

        int many = reader.ReadInt32();
        var samples = new List<Sample>(many);
        for (var at = 0; at < many; at++)
        {
            int shape = reader.ReadInt32(), drawn = reader.ReadInt32();
            samples.Add(new Sample(shape, drawn, Vector(reader), Vector(reader), Vector(reader), Vector(reader), Vector(reader), reader.ReadSingle()));
        }

        return (new Header(width, height, factor, origin, player, eye, forward, pixel, sections), samples);
    }

    /// <summary>The offline reader: the numbers beside a capture fitted again.</summary>
    public static string Replay(string folder)
    {
        string path = CaptureParts.Find(folder, Numbers);
        if (!File.Exists(path))
        {
            return "roof fade replay: no " + Numbers + " in " + folder;
        }

        (Header header, List<Sample> samples) = Read(File.ReadAllBytes(path));
        return Report(header, samples, "roof fade replay");
    }

    private static void Floats(BinaryWriter writer, params ReadOnlySpan<float> values)
    {
        foreach (float value in values)
        {
            writer.Write(value);
        }
    }

    private static void Text(BinaryWriter writer, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        int length = Math.Min(bytes.Length, ushort.MaxValue);
        writer.Write((ushort)length);
        writer.Write(bytes, 0, length);
    }

    private static string Text(BinaryReader reader) => Encoding.UTF8.GetString(reader.ReadBytes(reader.ReadUInt16()));

    private static Vector3 Vector(BinaryReader reader) => new(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
}
