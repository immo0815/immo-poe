using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// The capture's light-against-the-game part: the stage drawn through the game's own camera (CaptureAtGame), every gathered pixel's light held
/// against the game's pixel under it - by sun and shade, by material, term by term - so a picture too bright or too green says which part of
/// the light makes it so.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT (v0.1.368): "build the capture part now, it comes anyway - so we need not trade crumbs of information". The
/// Red Vale's pane drew everything too bright and too saturated against the game's picture, the shade measured 2.6 times the game's - by hand,
/// from crops of two pictures from two cameras. Here one press measures it, and the numbers are kept (the .bin) so the statistics can be
/// worked out again offline (Replay) when a rule changes.
///
/// WHAT IS COMPARED: the solid surface at each gathered pixel - nothing translucent over it, not water, not by a shape's edge, not where the
/// game's picture shows its interface or anything that moves, nearer than the game's far plane. Both sides in linear light: the game's picture
/// decoded by the piecewise sRGB curve (its ApplyOETF unread, CHOSEN), averaged over the block a drawn pixel stands for.
///
/// WHAT EACH STATISTIC IS FOR, said in the report: the medians by sun and shade say where the brightness is wrong; the chromaticity and the
/// neutral fit (game = s pane + n) say whether a neutral or blue share is missing; the term fit says which term of the light is too strong
/// (the game's picture taken back through the pane's own grade and exposure - the engine's grade being the same table, CHOSEN); the game's own
/// sun and shade, split per material on its brightness over the pane's albedo, says whether the game's shadows lie where the pane's do; and
/// the ratio by the distance to the player and by depth tests the player's lamp (CHOSEN 500 / 100) and a haze.
/// </remarks>
public static class CaptureLightCompare
{
    /// <summary>The pane's picture through the game's camera, beside the report.</summary>
    public const string PanePicture = "light-against-game-pane.png";

    /// <summary>The game's brightness over the pane's at each compared pixel, as a heat map.</summary>
    public const string RatioPicture = "light-against-game-ratio.png";

    /// <summary>The pane's sun and shade over the game's picture.</summary>
    public const string SunPicture = "light-against-game-sun.png";

    /// <summary>Every compared pixel's numbers, for the replay - see <see cref="BinaryFormat"/>.</summary>
    public const string Numbers = "light-against-game.bin";

    /// <summary>The replay's report, beside the part's.</summary>
    public const string ReplayFile = "light-against-game-replay.txt";

    /// <summary>How the numbers are laid out, written into the report.</summary>
    public const string BinaryFormat =
        "little-endian: \"PLAG\", int version 2, int gathered width, int gathered height, int step, int factor, float exposure, int left-out counts (9: no surface, water, "
        + "translucent over it, beach or lifted water over it, masked, past the far plane, a shape's edge, no game picture, the game's picture at its ceiling; "
        + "version 1 had the first 8), int materials, each a ushort "
        + "length and UTF-8, int samples, each: int gathered index, int shape (-1 none), int material, then halves: game rgb, game unexposed rgb, pane rgb, "
        + "albedo rgb, sun, facing, sun diffuse rgb, cube rgb, gi stand-in and flat rgb, room lights rgb, player lamp rgb, specular rgb, pane unexposed rgb; "
        + "then floats: depth, distance to the player across";

    /// <summary>The least samples a material needs to be reported on its own.</summary>
    private const int FewestPerMaterial = 400;

    /// <summary>How many reasons a gathered pixel is left out for - see <see cref="BinaryFormat"/>.</summary>
    private const int Reasons = 9;

    /// <summary>How far across from the player the left-out ceiling is counted as under the player's lamp, units.</summary>
    private const float NearPlayer = 500f;

    /// <summary>
    /// The part's report and what goes beside it: the pictures and the numbers.
    /// </summary>
    /// <param name="scene">The stage drawn through the game's camera.</param>
    /// <param name="pictures">The pictures to write beside the report, by file name.</param>
    /// <param name="numbers">The samples' numbers, <see cref="Numbers"/>.</param>
    /// <param name="search">Where it says how far it is, or null - the drawing's own, so the capture's window goes on from it.</param>
    public static string Write(
        CaptureAtGame scene, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers, CaptureAtGame.Search? search = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        search?.Begin(1);
        search?.Step("holding every pixel's light against the game's");
        SceneLight light = scene.Pane.Light!;
        MonsterModel model = scene.Pane.Model;
        PixelGather gather = scene.Gather;
        var materials = new List<string>();
        var materialOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var shapeMaterial = new int[model.Mesh.Shapes.Count];
        for (var shape = 0; shape < shapeMaterial.Length; shape++)
        {
            string name = MaterialName(model, shape);
            if (!materialOf.TryGetValue(name, out int at))
            {
                at = materials.Count;
                materials.Add(name);
                materialOf[name] = at;
            }

            shapeMaterial[shape] = at;
        }

        Vector2 player = scene.PlayerAcross - scene.Origin;
        var left = new int[Reasons];
        var ceiling = new int[3];
        var ceilingNear = 0;
        var samples = new List<Sample>(gather.Triangle.Length / 2);
        int width = gather.Width;
        for (var at = 0; at < gather.Triangle.Length; at++)
        {
            int drawn = scene.DrawnAt(at);
            if (drawn < 0)
            {
                continue;
            }

            int triangle = gather.Triangle[at];
            if (triangle < 0)
            {
                left[0]++;
                continue;
            }

            if (gather.Water[at])
            {
                left[1]++;
                continue;
            }

            if (scene.Layer[drawn] >= 0)
            {
                left[2]++;
                continue;
            }

            if (scene.Depth[drawn] < gather.Depth[at] * 0.999f)
            {
                left[3]++;
                continue;
            }

            if (scene.Masked[drawn])
            {
                left[4]++;
                continue;
            }

            if (gather.Depth[at] > scene.Camera.Far)
            {
                left[5]++;
                continue;
            }

            int shape = triangle < scene.ShapeOf.Length ? scene.ShapeOf[triangle] : -1;
            if (Edge(scene, gather, at, width, shape))
            {
                left[6]++;
                continue;
            }

            Vector3 game = scene.GameAt(drawn);
            if (!float.IsFinite(game.X))
            {
                left[7]++;
                continue;
            }

            SceneLight.LightTerms terms = gather.Terms[at];
            Vector3 place = gather.Place[at];

            // THE GAME'S PICTURE AT ITS CEILING says only that its light is at least this much - left out, and said by the pane's sun and
            // by the player's lamp, where it gathers.
            if (scene.Clipped[drawn])
            {
                left[8]++;
                ceiling[(int)ClassOf(terms.Sun, terms.SunFacing)]++;
                ceilingNear += !float.IsNaN(player.X) && Vector2.Distance(new Vector2(place.X, place.Y), player) < NearPlayer ? 1 : 0;
                continue;
            }
            samples.Add(new Sample(
                at, shape, shape >= 0 ? shapeMaterial[shape] : -1, game, Ungraded(game, light), terms.Graded, terms.Albedo, terms.Sun, terms.SunFacing,
                terms.SunDiffuse, terms.Cube, terms.GiStandIn + terms.Flat, terms.RoomDiffuse, terms.PlayerDiffuse,
                terms.Lobes + terms.EnvironmentSpecular + terms.WavesReflection, terms.Unexposed, gather.Depth[at],
                float.IsNaN(player.X) ? float.NaN : Vector2.Distance(new Vector2(place.X, place.Y), player)));
        }

        var header = new Header(gather.Width, (scene.DrawnHeight + gather.Step - 1) / gather.Step, gather.Step, scene.Factor, light.Exposure, left, materials);
        numbers = Bytes(header, samples);
        pictures = Pictures(scene, samples);

        var text = new StringBuilder();
        text.Append(Report(header, samples, "light against the game"));
        text.AppendLine().AppendLine("=== what was drawn");
        text.AppendLine(scene.LinedUp());
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"the game's picture at its ceiling (a channel at {CaptureAtGame.Ceiling} or more in the block): {left[8]:N0} pixels left out - {ceiling[(int)Lighting.Lit]:N0} in the pane's sun, {ceiling[(int)Lighting.Between]:N0} between, {ceiling[(int)Lighting.Shade]:N0} in its shade; {ceilingNear:N0} within {NearPlayer:0} units across of the player"));
        foreach (string line in scene.Said)
        {
            text.AppendLine(line);
        }

        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"light: sun ({light.SunColour.X:0.###}, {light.SunColour.Y:0.###}, {light.SunColour.Z:0.###}) travelling ({light.SunDirection.X:0.####}, {light.SunDirection.Y:0.####}, {light.SunDirection.Z:0.####}), {(light.SunShadows ? "shadows" : "no shadows")} on a map of {light.ShadowSide}; ambient {light.Ambient}, flat {light.FlatAmbient:0.#####}, cube brightness {light.CubeBrightness:0.###}, direct_light_env_ratio {light.DirectLightEnvRatio:0.###}, gi_env_occlusion {light.GiEnvOcclusion:0.###}; exposure {light.Exposure:0.###}; grade {(light.Grade is null ? "none" : "the environment's table")}; {light.PointCount} point lights{(light.PlayerIndex >= 0 ? ", the player's lamp the last" : string.Empty)}"));
        text.AppendLine().AppendLine("=== the numbers beside it");
        text.AppendLine(Numbers + ": " + BinaryFormat);
        text.AppendLine(PanePicture + ": the pane's picture through the game's camera, at the drawn size");
        text.AppendLine(RatioPicture + ": each compared pixel's log2 of the game's luminance over the pane's - blue the game darker, red brighter, white equal, -2 to 2; grey left out");
        text.AppendLine(SunPicture + ": the game's picture with the pane's sun over it - yellow lit, blue in shade, green between; dark left out");
        return text.ToString();
    }

    /// <summary>The report again from the numbers a capture kept - see <see cref="Numbers"/>.</summary>
    /// <param name="folder">The capture's folder, unpacked.</param>
    public static string Replay(string folder)
    {
        string path = CaptureParts.Find(folder, Numbers);
        if (!File.Exists(path))
        {
            return "light against the game replay: no " + Numbers + " in " + folder;
        }

        (Header header, List<Sample> samples) = Read(File.ReadAllBytes(path));
        return Report(header, samples, "light against the game replay");
    }

    /// <summary>One compared pixel.</summary>
    internal readonly record struct Sample(
        int Index, int Shape, int Material, Vector3 Game, Vector3 GameUnexposed, Vector3 Pane, Vector3 Albedo, float Sun, float Facing, Vector3 SunDiffuse,
        Vector3 Cube, Vector3 GiFlat, Vector3 Room, Vector3 Player, Vector3 Specular, Vector3 Unexposed, float Depth, float PlayerDistance)
    {
        /// <summary>The pane's sun at the pixel: lit, between, or in shade.</summary>
        public Lighting Class => ClassOf(Sun, Facing);
    }

    /// <summary>The pane's sun at a pixel from how much of it reached and how squarely: lit, between, or in shade.</summary>
    internal static Lighting ClassOf(float sun, float facing)
        => sun >= 0.9f && facing > 0.1f ? Lighting.Lit : sun <= 0.1f || facing <= 0.02f ? Lighting.Shade : Lighting.Between;

    /// <summary>How the pane's sun reaches a pixel.</summary>
    internal enum Lighting
    {
        /// <summary>Most of the sun, facing it.</summary>
        Lit,

        /// <summary>A shadow's edge or a grazing face.</summary>
        Between,

        /// <summary>In shadow or facing away.</summary>
        Shade,
    }

    /// <summary>What the numbers carry besides the samples.</summary>
    internal sealed record Header(int Width, int Height, int Step, int Factor, float Exposure, int[] Left, IReadOnlyList<string> Materials);

    /// <summary>The material a shape wears, as the report groups it - a ground's under its own word.</summary>
    private static string MaterialName(MonsterModel model, int shape)
    {
        string material = shape < model.ShapeMaterials.Count ? MaterialFile.Bare(model.ShapeMaterials[shape]) : string.Empty;
        string source = shape < model.ShapeSources.Count ? model.ShapeSources[shape] : string.Empty;
        string name = material.Length > 0 ? Path.GetFileNameWithoutExtension(material) : "(no material)";
        return source.StartsWith(RoomModels.GroundSource, StringComparison.Ordinal) ? "ground: " + name : name;
    }

    /// <summary>Whether a gathered pixel lies on a shape's edge or a step in depth - its colour there a mix of two surfaces in the game's picture.</summary>
    private static bool Edge(CaptureAtGame scene, PixelGather gather, int at, int width, int shape)
    {
        int x = at % width, y = at / width;
        float depth = gather.Depth[at];
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx, ny = y + dy;
                if ((dx == 0 && dy == 0) || nx < 0 || ny < 0 || nx >= width || ny >= width)
                {
                    continue;
                }

                int other = (ny * width) + nx;
                int triangle = gather.Triangle[other];
                if (triangle < 0 || (triangle < scene.ShapeOf.Length ? scene.ShapeOf[triangle] : -1) != shape || MathF.Abs(gather.Depth[other] - depth) > depth * 0.03f)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The game's colour taken back through the pane's grade and exposure: what the light came to before them, if the engine's grade is the
    /// pane's table (CHOSEN) - the grade inverted by twelve rounds of <c>x += target - grade(x)</c>, the table being near neutral.
    /// </summary>
    private static Vector3 Ungraded(Vector3 game, SceneLight light)
    {
        Vector3 x = game;
        if (light.Grade is { } grade)
        {
            for (var round = 0; round < 12; round++)
            {
                x = Vector3.Max(Vector3.Zero, x + (game - grade.Apply(x)));
            }
        }

        return x / MathF.Max(light.Exposure, 1e-6f);
    }

    /// <summary>The report, from the samples alone - the part's and the replay's.</summary>
    internal static string Report(Header header, IReadOnlyList<Sample> samples, string head)
    {
        var text = new StringBuilder();
        Lighting[] classes = [Lighting.Lit, Lighting.Between, Lighting.Shade];
        int total = samples.Count;
        int lit = samples.Count(one => one.Class == Lighting.Lit), shade = samples.Count(one => one.Class == Lighting.Shade);
        Measure all = Measure.Of(samples);
        Measure inSun = Measure.Of(samples.Where(one => one.Class == Lighting.Lit));
        Measure inShade = Measure.Of(samples.Where(one => one.Class == Lighting.Shade));

        // NO SUN AT ALL is said as such: every pixel then counts as shade, which a ratio "in the pane's sun" of nothing would hide.
        bool sunless = lit == 0 && total > 0 && samples.All(one => one.Sun == 0f);
        string byTheSun = sunless
            ? string.Create(CultureInfo.InvariantCulture, $"the pane's sun reaches none of them, in its shade {inShade.Ratio:0.###} ({shade:N0})")
            : string.Create(CultureInfo.InvariantCulture, $"in the pane's sun {inSun.Ratio:0.###} ({lit:N0}), in its shade {inShade.Ratio:0.###} ({shade:N0})");
        text.Append(CultureInfo.InvariantCulture,
            $"{head}: {total:N0} pixels compared - the game's luminance over the pane's, median {all.Ratio:0.###}; {byTheSun}; chroma kept {all.Chroma:0.###}")
            .AppendLine();
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"left out: {header.Left[0]:N0} no surface, {header.Left[1]:N0} water, {header.Left[2]:N0} translucent over it, {header.Left[3]:N0} beach or lifted water over it, {header.Left[4]:N0} the game's interface or something moving, {header.Left[5]:N0} past the game's far plane, {header.Left[6]:N0} a shape's edge, {header.Left[7]:N0} no game picture, {header.Left[8]:N0} the game's picture at its ceiling"));
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"gathered every {header.Step} drawn pixels each way, a drawn pixel {header.Factor} of the game's each way: {header.Width} x {header.Height}; exposure {header.Exposure:0.###}"));

        text.AppendLine().AppendLine("=== by the pane's sun - linear light, both sides; ratio the game over the pane");
        text.AppendLine("  class      pixels   game Y   pane Y   ratio  log2 IQR        game r g b              pane r g b    game sat  pane sat  chroma kept");
        foreach (Lighting one in classes)
        {
            Measure m = Measure.Of(samples.Where(sample => sample.Class == one));
            text.AppendLine(m.Row(one.ToString().ToLowerInvariant()));
        }

        text.AppendLine(all.Row("all"));
        text.AppendLine(sunless
            ? "  the sun's share: the pane's sun reaches none of the compared pixels - the light has no sun, or every one of them lies in its shadow"
            : string.Create(
                CultureInfo.InvariantCulture,
                $"  the sun's share: the pane lights {(double)lit / Math.Max(1, total):0.###} of the compared pixels, in shade {(double)shade / Math.Max(1, total):0.###}"));

        text.AppendLine().AppendLine("=== the game as a multiple of the pane plus a grey: game = s pane + n, over the three channels (least squares), and each channel's gain");
        foreach (Lighting one in classes)
        {
            text.AppendLine(Neutral(one.ToString().ToLowerInvariant(), samples.Where(sample => sample.Class == one).ToList()));
        }

        text.AppendLine(Neutral("all", samples));

        text.AppendLine().AppendLine("=== which term of the light: the game taken back through the pane's grade and exposure, as a non-negative sum of the pane's terms and a grey (either sign)");
        text.AppendLine("  a term's weight one says the game has it as the pane has it; nought that the game has none of it; the grey is what no term explains");
        foreach (Lighting one in classes)
        {
            text.AppendLine(Terms(one.ToString().ToLowerInvariant(), samples.Where(sample => sample.Class == one).ToList()));
        }

        text.AppendLine(Terms("all", samples));

        text.AppendLine().AppendLine("=== by material - the most pixels first; ratio in the pane's sun and in its shade, and the game's own sun and shade split on its brightness over the pane's albedo");
        text.AppendLine("  material                                   pixels  sun ratio  shade ratio  game sun/shade  pane sun/shade  agree (pane lit: game lit / shade; pane shade: game lit / shade)");
        foreach (IGrouping<int, Sample> group in samples.Where(one => one.Material >= 0).GroupBy(one => one.Material).OrderByDescending(group => group.Count()).Take(20))
        {
            List<Sample> those = [.. group];
            if (those.Count < FewestPerMaterial)
            {
                break;
            }

            string name = group.Key < header.Materials.Count ? header.Materials[group.Key] : "?";
            text.AppendLine(MaterialRow(name, those));
        }

        text.AppendLine().AppendLine("=== the game over the pane by the distance across to the player (the player's lamp, CHOSEN - its radius and height in the light panel's switches line)");
        foreach (string line in Binned(samples.Where(one => float.IsFinite(one.PlayerDistance)), one => one.PlayerDistance, 100f, 12, "units"))
        {
            text.AppendLine(line);
        }

        text.AppendLine().AppendLine("=== the game over the pane by depth along the view (a haze or fog grows with it)");
        foreach (string line in Binned(samples, one => one.Depth, 250f, 16, "deep"))
        {
            text.AppendLine(line);
        }

        return text.ToString();
    }

    /// <summary>Medians, chromaticities and saturations of a set of samples.</summary>
    private readonly record struct Measure(int Count, double GameY, double PaneY, double Ratio, double Low, double High, Vector3 GameRgb, Vector3 PaneRgb, double GameSat, double PaneSat)
    {
        /// <summary>The share of the pane's chroma the game keeps - its saturation over the pane's.</summary>
        public double Chroma => PaneSat > 1e-6 ? GameSat / PaneSat : double.NaN;

        public static Measure Of(IEnumerable<Sample> samples)
        {
            var gameY = new List<double>();
            var paneY = new List<double>();
            var logs = new List<double>();
            Vector3 game = Vector3.Zero, pane = Vector3.Zero;
            foreach (Sample one in samples)
            {
                double g = CaptureAtGame.Luminance(one.Game), p = CaptureAtGame.Luminance(one.Pane);
                gameY.Add(g);
                paneY.Add(p);
                if (g > 1e-5 && p > 1e-5)
                {
                    logs.Add(Math.Log2(g / p));
                }

                game += one.Game;
                pane += one.Pane;
            }

            if (gameY.Count == 0)
            {
                return new Measure(0, double.NaN, double.NaN, double.NaN, double.NaN, double.NaN, new Vector3(float.NaN), new Vector3(float.NaN), double.NaN, double.NaN);
            }

            double gm = Median(gameY), pm = Median(paneY);
            logs.Sort();
            return new Measure(
                gameY.Count, gm, pm, pm > 0 ? gm / pm : double.NaN, logs.Count > 0 ? logs[logs.Count / 4] : double.NaN, logs.Count > 0 ? logs[logs.Count * 3 / 4] : double.NaN,
                Chromaticity(game), Chromaticity(pane), Saturation(game), Saturation(pane));
        }

        public string Row(string name)
            => string.Create(
                CultureInfo.InvariantCulture,
                $"  {name,-8} {Count,8:N0} {GameY,8:0.####} {PaneY,8:0.####} {Ratio,7:0.###} {Low,6:0.##}..{High,-6:0.##} {GameRgb.X,6:0.###} {GameRgb.Y,6:0.###} {GameRgb.Z,6:0.###}  {PaneRgb.X,6:0.###} {PaneRgb.Y,6:0.###} {PaneRgb.Z,6:0.###}  {GameSat,8:0.###} {PaneSat,8:0.###} {Chroma,8:0.###}");

        private static Vector3 Chromaticity(Vector3 sum)
        {
            float all = sum.X + sum.Y + sum.Z;
            return all > 0f ? sum / all : new Vector3(float.NaN);
        }

        private static double Saturation(Vector3 sum)
        {
            float most = MathF.Max(sum.X, MathF.Max(sum.Y, sum.Z)), least = MathF.Min(sum.X, MathF.Min(sum.Y, sum.Z));
            return most > 0f ? 1d - (least / most) : double.NaN;
        }
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            return double.NaN;
        }

        values.Sort();
        return values[values.Count / 2];
    }

    /// <summary>game = s pane + n over the three channels pooled, and each channel's own gain.</summary>
    private static string Neutral(string name, IReadOnlyList<Sample> samples)
    {
        if (samples.Count < 50)
        {
            return $"  {name}: too few pixels ({samples.Count})";
        }

        double sx = 0, sy = 0, sxx = 0, sxy = 0, syy = 0;
        long n = 0;
        Vector3 game = Vector3.Zero, pane = Vector3.Zero;
        foreach (Sample one in samples)
        {
            for (var c = 0; c < 3; c++)
            {
                double x = one.Pane[c], y = one.Game[c];
                sx += x;
                sy += y;
                sxx += x * x;
                sxy += x * y;
                syy += y * y;
                n++;
            }

            game += one.Game;
            pane += one.Pane;
        }

        double vx = sxx - (sx * sx / n), vy = syy - (sy * sy / n), cxy = sxy - (sx * sy / n);
        double s = vx > 0 ? cxy / vx : double.NaN;
        double offset = (sy - (s * sx)) / n;
        double r2 = vx > 0 && vy > 0 ? cxy * cxy / (vx * vy) : double.NaN;
        double meanGame = sy / n;
        return string.Create(
            CultureInfo.InvariantCulture,
            $"  {name,-8} s {s:0.###}  n {offset:0.#####} ({offset / meanGame:0.###} of the game's mean)  R2 {r2:0.###}   gains r {game.X / pane.X:0.###} g {game.Y / pane.Y:0.###} b {game.Z / pane.Z:0.###}");
    }

    /// <summary>The non-negative weights of the pane's terms (and a grey of either sign) that make the game's unexposed light.</summary>
    private static string Terms(string name, IReadOnlyList<Sample> samples)
    {
        if (samples.Count < 50)
        {
            return $"  {name}: too few pixels ({samples.Count})";
        }

        const int terms = 8;
        var gram = new double[terms, terms];
        var rhs = new double[terms];
        Span<double> row = stackalloc double[terms];
        Vector3 sums0 = Vector3.Zero, sums1 = Vector3.Zero, sums2 = Vector3.Zero, sums3 = Vector3.Zero, sums4 = Vector3.Zero, sums5 = Vector3.Zero, own = Vector3.Zero;
        double total = 0, totalSquares = 0;
        long n = 0;
        foreach (Sample one in samples)
        {
            sums0 += one.SunDiffuse;
            sums1 += one.Cube;
            sums2 += one.GiFlat;
            sums3 += one.Room;
            sums4 += one.Player;
            sums5 += one.Specular;
            own += one.Unexposed;
            for (var c = 0; c < 3; c++)
            {
                row[0] = one.SunDiffuse[c];
                row[1] = one.Cube[c];
                row[2] = one.GiFlat[c];
                row[3] = one.Room[c];
                row[4] = one.Player[c];
                row[5] = one.Specular[c];
                row[6] = 1d;
                row[7] = -1d;
                double y = one.GameUnexposed[c];
                for (var i = 0; i < terms; i++)
                {
                    rhs[i] += row[i] * y;
                    for (var j = 0; j < terms; j++)
                    {
                        gram[i, j] += row[i] * row[j];
                    }
                }

                total += y;
                totalSquares += y * y;
                n++;
            }
        }

        double[] weights = NonNegative(gram, rhs);
        double residual = totalSquares;
        for (var i = 0; i < terms; i++)
        {
            residual -= 2 * weights[i] * rhs[i];
            for (var j = 0; j < terms; j++)
            {
                residual += weights[i] * gram[i, j] * weights[j];
            }
        }

        double variance = totalSquares - (total * total / n);
        double r2 = variance > 0 ? 1d - (residual / variance) : double.NaN;
        float paneAll = own.X + own.Y + own.Z;
        string Share(Vector3 sum) => paneAll > 0f ? ((sum.X + sum.Y + sum.Z) / paneAll).ToString("0.##", CultureInfo.InvariantCulture) : "-";
        return string.Create(
            CultureInfo.InvariantCulture,
            $"  {name,-8} sun {weights[0]:0.###} (its share of the pane's light {Share(sums0)})  cube {weights[1]:0.###} ({Share(sums1)})  gi stand-in and flat {weights[2]:0.###} ({Share(sums2)})  room lights {weights[3]:0.###} ({Share(sums3)})  player's lamp {weights[4]:0.###} ({Share(sums4)})  specular {weights[5]:0.###} ({Share(sums5)})  grey {weights[6] - weights[7]:0.#####}  R2 {r2:0.###}");
    }

    /// <summary>Non-negative least squares on the normal equations, by coordinate descent - a handful of unknowns.</summary>
    internal static double[] NonNegative(double[,] gram, double[] rhs, int rounds = 2000)
    {
        int count = rhs.Length;
        var x = new double[count];
        for (var round = 0; round < rounds; round++)
        {
            double moved = 0;
            for (var i = 0; i < count; i++)
            {
                if (!(gram[i, i] > 1e-18))
                {
                    continue;
                }

                double gradient = rhs[i];
                for (var j = 0; j < count; j++)
                {
                    gradient -= gram[i, j] * x[j];
                }

                double next = Math.Max(0d, x[i] + (gradient / gram[i, i]));
                moved = Math.Max(moved, Math.Abs(next - x[i]));
                x[i] = next;
            }

            if (moved < 1e-12)
            {
                break;
            }
        }

        return x;
    }

    /// <summary>One material's row: its ratios in sun and shade and how the game's own sun and shade fall on it.</summary>
    private static string MaterialRow(string name, List<Sample> samples)
    {
        Measure sun = Measure.Of(samples.Where(one => one.Class == Lighting.Lit));
        Measure shade = Measure.Of(samples.Where(one => one.Class == Lighting.Shade));

        // THE GAME'S OWN SUN AND SHADE: its brightness over the pane's albedo, split in two where the split leaves the two halves most apart (Otsu).
        var light = new List<(double Game, double Pane, Lighting Pane_)>(samples.Count);
        foreach (Sample one in samples)
        {
            double albedo = Math.Max(CaptureAtGame.Luminance(one.Albedo), 1e-4);
            double g = CaptureAtGame.Luminance(one.Game) / albedo, p = CaptureAtGame.Luminance(one.Pane) / albedo;
            if (g > 1e-6 && p > 1e-6)
            {
                light.Add((Math.Log(g), Math.Log(p), one.Class));
            }
        }

        string agree = "-", gameRatio = "-", paneRatio = "-";
        if (light.Count >= 100 && Otsu(light.Select(one => one.Game).ToList()) is { } split)
        {
            int litLit = 0, litShade = 0, shadeLit = 0, shadeShade = 0;
            double gameHigh = 0, gameLow = 0, paneHigh = 0, paneLow = 0;
            int high = 0, low = 0, paneLitCount = 0, paneShadeCount = 0;
            foreach ((double game, double pane, Lighting cls) in light)
            {
                bool gameLit = game >= split;
                if (gameLit)
                {
                    gameHigh += game;
                    high++;
                }
                else
                {
                    gameLow += game;
                    low++;
                }

                if (cls == Lighting.Lit)
                {
                    paneHigh += pane;
                    paneLitCount++;
                    if (gameLit)
                    {
                        litLit++;
                    }
                    else
                    {
                        litShade++;
                    }
                }
                else if (cls == Lighting.Shade)
                {
                    paneLow += pane;
                    paneShadeCount++;
                    if (gameLit)
                    {
                        shadeLit++;
                    }
                    else
                    {
                        shadeShade++;
                    }
                }
            }

            agree = string.Create(CultureInfo.InvariantCulture, $"{litLit} / {litShade}; {shadeLit} / {shadeShade}");
            gameRatio = high > 0 && low > 0 ? Math.Exp((gameHigh / high) - (gameLow / low)).ToString("0.##", CultureInfo.InvariantCulture) : "-";
            paneRatio = paneLitCount > 0 && paneShadeCount > 0 ? Math.Exp((paneHigh / paneLitCount) - (paneLow / paneShadeCount)).ToString("0.##", CultureInfo.InvariantCulture) : "-";
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"  {Shorten(name, 42),-42} {samples.Count,7:N0} {sun.Ratio,10:0.###} {shade.Ratio,12:0.###} {gameRatio,15} {paneRatio,15}  {agree}");
    }

    /// <summary>The split of a set of values in two that leaves the halves most apart - Otsu's, on 64 bins between the 1st and the 99th percentile.</summary>
    private static double? Otsu(List<double> values)
    {
        values.Sort();
        double low = values[values.Count / 100], high = values[values.Count * 99 / 100];
        if (!(high > low))
        {
            return null;
        }

        const int bins = 64;
        var counts = new double[bins];
        foreach (double value in values)
        {
            counts[Math.Clamp((int)((value - low) / (high - low) * bins), 0, bins - 1)]++;
        }

        double all = values.Count, sumAll = 0;
        for (var at = 0; at < bins; at++)
        {
            sumAll += at * counts[at];
        }

        double below = 0, sumBelow = 0, best = -1;
        int bestAt = -1;
        for (var at = 0; at < bins - 1; at++)
        {
            below += counts[at];
            sumBelow += at * counts[at];
            double above = all - below;
            if (below == 0 || above == 0)
            {
                continue;
            }

            double meanBelow = sumBelow / below, meanAbove = (sumAll - sumBelow) / above;
            double between = below * above * (meanBelow - meanAbove) * (meanBelow - meanAbove);
            if (between > best)
            {
                best = between;
                bestAt = at;
            }
        }

        return bestAt < 0 ? null : low + ((bestAt + 1) * (high - low) / bins);
    }

    /// <summary>The median ratio in bins of a measure.</summary>
    private static IEnumerable<string> Binned(IEnumerable<Sample> samples, Func<Sample, float> measure, float width, int bins, string unit)
    {
        var groups = new List<Sample>[bins];
        for (var at = 0; at < bins; at++)
        {
            groups[at] = [];
        }

        foreach (Sample one in samples)
        {
            int at = (int)(measure(one) / width);
            if (at >= 0 && at < bins)
            {
                groups[at].Add(one);
            }
        }

        for (var at = 0; at < bins; at++)
        {
            if (groups[at].Count < 50)
            {
                continue;
            }

            Measure sun = Measure.Of(groups[at].Where(one => one.Class == Lighting.Lit));
            Measure shade = Measure.Of(groups[at].Where(one => one.Class == Lighting.Shade));
            Measure all = Measure.Of(groups[at]);
            yield return string.Create(
                CultureInfo.InvariantCulture,
                $"  {at * width,6:0}-{(at + 1) * width,-6:0} {unit}: {groups[at].Count,7:N0} pixels, ratio {all.Ratio:0.###} (sun {sun.Ratio:0.###} of {sun.Count:N0}, shade {shade.Ratio:0.###} of {shade.Count:N0})");
        }
    }

    private static string Shorten(string text, int most) => text.Length <= most ? text : "..." + text[^(most - 3)..];

    /// <summary>The pictures beside the report.</summary>
    private static List<(string File, GamePicture Picture)> Pictures(CaptureAtGame scene, IReadOnlyList<Sample> samples)
    {
        int w = scene.Gather.Width, h = Math.Min(scene.Gather.Width, (scene.DrawnHeight + scene.Gather.Step - 1) / scene.Gather.Step);
        var ratio = new byte[w * h * 4];
        var sun = new byte[w * h * 4];
        for (var at = 0; at < w * h; at++)
        {
            int drawn = scene.DrawnAt(at);
            Vector3 game = drawn >= 0 ? scene.GameAt(drawn) : new Vector3(float.NaN);
            byte grey = float.IsFinite(game.X) ? CaptureAtGame.Encoded(CaptureAtGame.Luminance(game) * 0.35f) : (byte)0;
            ratio[(at * 4) + 0] = ratio[(at * 4) + 1] = ratio[(at * 4) + 2] = grey;
            ratio[(at * 4) + 3] = 255;
            byte r = float.IsFinite(game.X) ? CaptureAtGame.Encoded(game.X * 0.4f) : (byte)0;
            byte g = float.IsFinite(game.X) ? CaptureAtGame.Encoded(game.Y * 0.4f) : (byte)0;
            byte b = float.IsFinite(game.X) ? CaptureAtGame.Encoded(game.Z * 0.4f) : (byte)0;
            sun[(at * 4) + 0] = r;
            sun[(at * 4) + 1] = g;
            sun[(at * 4) + 2] = b;
            sun[(at * 4) + 3] = 255;
        }

        foreach (Sample one in samples)
        {
            if (one.Index >= w * h)
            {
                continue;
            }

            double g = CaptureAtGame.Luminance(one.Game), p = CaptureAtGame.Luminance(one.Pane);
            if (g > 1e-5 && p > 1e-5)
            {
                float t = Math.Clamp((float)(Math.Log2(g / p) / 2d), -1f, 1f);
                int at = one.Index * 4;
                ratio[at] = (byte)(t >= 0f ? 255 : (int)(255 * (1f + t)));
                ratio[at + 1] = (byte)(255 * (1f - MathF.Abs(t)));
                ratio[at + 2] = (byte)(t <= 0f ? 255 : (int)(255 * (1f - t)));
            }

            Vector3 tint = one.Class switch
            {
                Lighting.Lit => new Vector3(1f, 0.85f, 0.1f),
                Lighting.Shade => new Vector3(0.15f, 0.35f, 1f),
                _ => new Vector3(0.2f, 0.9f, 0.3f),
            };
            Vector3 shown = (one.Game * 0.6f) + (tint * 0.25f);
            int to = one.Index * 4;
            sun[to] = CaptureAtGame.Encoded(shown.X);
            sun[to + 1] = CaptureAtGame.Encoded(shown.Y);
            sun[to + 2] = CaptureAtGame.Encoded(shown.Z);
        }

        return
        [
            (PanePicture, new GamePicture(scene.DrawnWidth, scene.DrawnHeight, scene.Picture)),
            (RatioPicture, new GamePicture(w, h, ratio)),
            (SunPicture, new GamePicture(w, h, sun)),
        ];
    }

    /// <summary>The samples as bytes - <see cref="BinaryFormat"/>.</summary>
    private static byte[] Bytes(Header header, IReadOnlyList<Sample> samples)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write("PLAG"u8);
        writer.Write(2);
        writer.Write(header.Width);
        writer.Write(header.Height);
        writer.Write(header.Step);
        writer.Write(header.Factor);
        writer.Write(header.Exposure);
        foreach (int one in header.Left)
        {
            writer.Write(one);
        }

        writer.Write(header.Materials.Count);
        foreach (string material in header.Materials)
        {
            byte[] name = Encoding.UTF8.GetBytes(material);
            writer.Write((ushort)Math.Min(name.Length, ushort.MaxValue));
            writer.Write(name, 0, Math.Min(name.Length, ushort.MaxValue));
        }

        writer.Write(samples.Count);
        foreach (Sample one in samples)
        {
            writer.Write(one.Index);
            writer.Write(one.Shape);
            writer.Write(one.Material);
            Halves(writer, one.Game);
            Halves(writer, one.GameUnexposed);
            Halves(writer, one.Pane);
            Halves(writer, one.Albedo);
            writer.Write((Half)one.Sun);
            writer.Write((Half)one.Facing);
            Halves(writer, one.SunDiffuse);
            Halves(writer, one.Cube);
            Halves(writer, one.GiFlat);
            Halves(writer, one.Room);
            Halves(writer, one.Player);
            Halves(writer, one.Specular);
            Halves(writer, one.Unexposed);
            writer.Write(one.Depth);
            writer.Write(one.PlayerDistance);
        }

        writer.Flush();
        return stream.ToArray();
    }

    private static void Halves(BinaryWriter writer, Vector3 value)
    {
        writer.Write((Half)value.X);
        writer.Write((Half)value.Y);
        writer.Write((Half)value.Z);
    }

    /// <summary>The samples read back - <see cref="BinaryFormat"/>.</summary>
    internal static (Header Header, List<Sample> Samples) Read(byte[] bytes)
    {
        using var reader = new BinaryReader(new MemoryStream(bytes), Encoding.UTF8);
        int version = reader.ReadBytes(4).AsSpan().SequenceEqual("PLAG"u8) ? reader.ReadInt32() : -1;
        if (version is not (1 or 2))
        {
            throw new InvalidDataException("not the light-against-the-game numbers of version 1 or 2");
        }

        // VERSION 1 KEPT NO CEILING: its pixels at the game's ceiling are among the samples, and its ninth reason reads nought.
        int width = reader.ReadInt32(), height = reader.ReadInt32(), step = reader.ReadInt32(), factor = reader.ReadInt32();
        float exposure = reader.ReadSingle();
        var left = new int[Reasons];
        for (var at = 0; at < (version == 1 ? 8 : Reasons); at++)
        {
            left[at] = reader.ReadInt32();
        }

        int count = reader.ReadInt32();
        var materials = new List<string>(count);
        for (var at = 0; at < count; at++)
        {
            ushort length = reader.ReadUInt16();
            materials.Add(Encoding.UTF8.GetString(reader.ReadBytes(length)));
        }

        int samples = reader.ReadInt32();
        var list = new List<Sample>(samples);
        for (var at = 0; at < samples; at++)
        {
            int index = reader.ReadInt32(), shape = reader.ReadInt32(), material = reader.ReadInt32();
            Vector3 game = Halves(reader), gameUnexposed = Halves(reader), pane = Halves(reader), albedo = Halves(reader);
            float sun = (float)reader.ReadHalf(), facing = (float)reader.ReadHalf();
            Vector3 sunDiffuse = Halves(reader), cube = Halves(reader), giFlat = Halves(reader), room = Halves(reader), player = Halves(reader), specular = Halves(reader), unexposed = Halves(reader);
            float depth = reader.ReadSingle(), distance = reader.ReadSingle();
            list.Add(new Sample(index, shape, material, game, gameUnexposed, pane, albedo, sun, facing, sunDiffuse, cube, giFlat, room, player, specular, unexposed, depth, distance));
        }

        return (new Header(width, height, step, factor, exposure, left, materials), list);
    }

    private static Vector3 Halves(BinaryReader reader)
        => new((float)reader.ReadHalf(), (float)reader.ReadHalf(), (float)reader.ReadHalf());
}
