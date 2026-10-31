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
///
/// THREE SECTIONS MORE, each a need The Red Vale's capture of v0.1.372 MEASURED (v0.1.373):
/// <list type="bullet">
/// <item>ROOF SECTIONS APART. A tree trunk beside the player came out 11 times brighter in the game than in the pane (3,638 pixels): the
/// game hides the shapes whose .mat says <c>"roofsection": true</c> round the player - the engine keeps big objects from standing between
/// the camera and the player, said from the live client - and shows what stands behind them. Such a material is named "roof: " here as a
/// ground's is named "ground: ", its pixels kept out of every statistic and set out on their own by the distance to the player.</item>
/// <item>THE GROUND BLOCK BY BLOCK. Pixel by pixel the comparison is ruled by small things that cannot match - the ground's scatter is
/// strewn by the tool's own CHOSEN pattern - so the ground away from the player is also held in blocks, each one's light its pixels' median,
/// set out by how much of the block the pane's sun reaches. Worked offline on that capture: the pane's lit blocks about 13 times as bright
/// over their albedo as its shaded ones, the game's about 2.2 times - a sign the game darkens its sun over most of the ground the pane lights.</item>
/// <item>THE PLAYER'S LAMP. Within 400 units of the player the game in the pane's shade was 2.4 to 2.9 times the pane's: the lamp's share
/// of the pane's light and the gain the game asks of it, by the distance, so the lamp can be set from a measurement - fitted apart from
/// the scale the game asks of the rest of the light (v0.1.375; see <see cref="LampFit"/>).</item>
/// </list>
/// </remarks>
public static class CaptureLightCompare
{
    /// <summary>The pane's picture through the game's camera, beside the report.</summary>
    public const string PanePicture = "light-against-game-pane.png";

    /// <summary>The game's brightness over the pane's at each compared pixel, as a heat map.</summary>
    public const string RatioPicture = "light-against-game-ratio.png";

    /// <summary>The pane's sun and shade over the game's picture.</summary>
    public const string SunPicture = "light-against-game-sun.png";

    /// <summary>The ground's blocks, each by the game over the pane - see <see cref="GroundBlocks"/>.</summary>
    public const string BlocksPicture = "light-against-game-blocks.png";

    /// <summary>Every compared pixel's numbers, for the replay - see <see cref="BinaryFormat"/>.</summary>
    public const string Numbers = "light-against-game.bin";

    /// <summary>The replay's report, beside the part's.</summary>
    public const string ReplayFile = "light-against-game-replay.txt";

    /// <summary>How the numbers are laid out, written into the report.</summary>
    public const string BinaryFormat =
        "little-endian: \"PLAG\", int version 3, int gathered width, int gathered height, int step, int factor, float exposure, int left-out counts (9: no surface, water, "
        + "translucent over it, beach or lifted water over it, masked, past the far plane, a shape's edge, no game picture, the game's picture at its ceiling; "
        + "version 1 had the first 8), int materials, each a ushort "
        + "length and UTF-8 (a ground's name begins \"ground: \", a roof section's \"roof: \"), int samples, each: int gathered index, int shape (-1 none), int material, then halves: game rgb, game unexposed rgb, pane rgb, "
        + "albedo rgb, sun, facing, sun diffuse rgb, cube rgb, gi stand-in and flat rgb, room lights rgb, player lamp rgb, specular rgb, pane unexposed rgb; "
        + "then floats: depth, distance to the player across; version 3 (v0.1.380) after the left-out counts: floats the stage's corner in the area x y, the player's place "
        + "in the area x y, the sun's way x y z, a byte whether the clouds follow and 9 floats: clouds_scale, _intensity, _midpoint, _sharpness, _speed, _angle, "
        + "_fade_radius, _pre_fade, _post_fade; and each sample ends with floats its place in the stage's frame x y z";

    /// <summary>The word a ground's material is named under.</summary>
    internal const string GroundWord = "ground: ";

    /// <summary>The word a roof section's material is named under - one whose .mat says <c>"roofsection"</c> (<see cref="MaterialFile.RoofSection"/>).</summary>
    internal const string RoofWord = "roof: ";

    /// <summary>The least samples a material needs to be reported on its own.</summary>
    private const int FewestPerMaterial = 400;

    /// <summary>How many reasons a gathered pixel is left out for - see <see cref="BinaryFormat"/>.</summary>
    private const int Reasons = 9;

    /// <summary>
    /// How far across from the player a pixel is taken as under the player's lamp, units - the light panel's radius, CHOSEN: the left-out
    /// ceiling is counted within it, and the ground's blocks are taken beyond it.
    /// </summary>
    private const float NearPlayer = 500f;

    /// <summary>A ground block's side, gathered pixels.</summary>
    private const int BlockSide = 16;

    /// <summary>The least ground samples a block needs to be counted.</summary>
    private const int FewestPerBlock = 40;

    /// <summary>The width of the roof sections' bins by the distance to the player, units.</summary>
    private const float RoofBin = 250f;

    /// <summary>The width of the lamp's bins by the distance to the player, units.</summary>
    private const float LampBin = 100f;

    /// <summary>How many of the lamp's bins - out to 1,200 units.</summary>
    private const int LampBins = 12;

    /// <summary>The rows the ground's blocks are set out in, by the share of each block the pane's sun reaches.</summary>
    private static readonly (string Label, double From, double To)[] ShareRows =
        [("0-0.1", 0d, 0.1), ("0.1-0.3", 0.1, 0.3), ("0.3-0.5", 0.3, 0.5), ("0.5-0.7", 0.5, 0.7), ("0.7-1", 0.7, double.PositiveInfinity)];

    /// <summary>
    /// The bands of the lamp's share of a pixel's light each pixel's own ratio is set out by beside the lamp's fit - see <see cref="LampLines"/>;
    /// under a twentieth the ratio says nothing of the lamp at all.
    /// </summary>
    private static readonly (string Label, double From, double To)[] LampShareBands =
        [("0.05-0.1", 0.05, 0.1), ("0.1-0.2", 0.1, 0.2), ("0.2-0.3", 0.2, 0.3), ("0.3-0.5", 0.3, 0.5), ("0.5 and more", 0.5, double.PositiveInfinity)];

    /// <summary>
    /// The part's report and what goes beside it: the pictures and the numbers.
    /// </summary>
    /// <param name="read">The install's files - each material's .mat, read once, says whether it is a roof section - or null, every shape compared.</param>
    /// <param name="scene">The stage drawn through the game's camera.</param>
    /// <param name="pictures">The pictures to write beside the report, by file name.</param>
    /// <param name="numbers">The samples' numbers, <see cref="Numbers"/>.</param>
    /// <param name="search">Where it says how far it is, or null - the drawing's own, so the capture's window goes on from it.</param>
    /// <param name="environment">The area's own .env, whose clouds the clouds' noise is measured with - or null.</param>
    public static string Write(
        Func<string, byte[]?>? read, CaptureAtGame scene, out IReadOnlyList<(string File, GamePicture Picture)> pictures, out byte[] numbers,
        CaptureAtGame.Search? search = null, string? environment = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        search?.Begin(1);
        search?.Step("holding every pixel's light against the game's");
        SceneLight light = scene.Pane.Light!;
        MonsterModel model = scene.Pane.Model;
        PixelGather gather = scene.Gather;
        var roofs = new RoofSections(read);
        var materials = new List<string>();
        var materialOf = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var shapeMaterial = new int[model.Mesh.Shapes.Count];
        Array.Fill(shapeMaterial, -1);

        // EACH SHAPE'S MATERIAL NAMED WHEN A COMPARED PIXEL FIRST WEARS IT: naming it reads its .mat for the roof section's word, and a
        // stage wears hundreds of materials the game's camera never shows.
        int MaterialOf(int shape)
        {
            if (shape < 0 || shape >= shapeMaterial.Length)
            {
                return -1;
            }

            if (shapeMaterial[shape] < 0)
            {
                string name = MaterialName(model, shape, roofs);
                if (!materialOf.TryGetValue(name, out int at))
                {
                    at = materials.Count;
                    materials.Add(name);
                    materialOf[name] = at;
                }

                shapeMaterial[shape] = at;
            }

            return shapeMaterial[shape];
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
                at, shape, MaterialOf(shape), game, Ungraded(game, light), terms.Graded, terms.Albedo, terms.Sun, terms.SunFacing,
                terms.SunDiffuse, terms.Cube, terms.GiStandIn + terms.Flat, terms.RoomDiffuse, terms.PlayerDiffuse,
                terms.Lobes + terms.EnvironmentSpecular + terms.WavesReflection, terms.Unexposed, gather.Depth[at],
                float.IsNaN(player.X) ? float.NaN : Vector2.Distance(new Vector2(place.X, place.Y), player)) { Place = place });
        }

        string cloudsWhy = "no .env read";
        AreaClouds? clouds = read is not null && environment is { Length: > 0 }
            ? AreaClouds.Of(EnvironmentSettings.Read(environment, read(environment)), out cloudsWhy)
            : null;
        var header = new Header(gather.Width, (scene.DrawnHeight + gather.Step - 1) / gather.Step, gather.Step, scene.Factor, light.Exposure, left, materials)
        {
            Origin = scene.Origin,
            PlayerArea = scene.PlayerAcross,
            Sun = light.SunDirection,
            Clouds = clouds,
        };
        numbers = Bytes(header, samples);
        pictures = Pictures(scene, header, samples);

        var text = new StringBuilder();
        text.Append(Report(header, samples, "light against the game"));
        text.AppendLine().AppendLine("=== what was drawn");
        text.AppendLine(scene.LinedUp());
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"the game's picture at its ceiling (a channel at {CaptureAtGame.Ceiling} or more in the block): {left[8]:N0} pixels left out - {ceiling[(int)Lighting.Lit]:N0} in the pane's sun, {ceiling[(int)Lighting.Between]:N0} between, {ceiling[(int)Lighting.Shade]:N0} in its shade; {ceilingNear:N0} within {NearPlayer:0} units across of the player"));
        text.AppendLine(roofs.Said());
        foreach (string line in scene.Said)
        {
            text.AppendLine(line);
        }

        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"light: sun ({light.SunColour.X:0.###}, {light.SunColour.Y:0.###}, {light.SunColour.Z:0.###}) travelling ({light.SunDirection.X:0.####}, {light.SunDirection.Y:0.####}, {light.SunDirection.Z:0.####}), {(light.SunShadows ? "shadows" : "no shadows")} on a map of {light.ShadowSide}; ambient {light.Ambient}, flat {light.FlatAmbient:0.#####}, cube brightness {light.CubeBrightness:0.###}, direct_light_env_ratio {light.DirectLightEnvRatio:0.###}, gi_env_occlusion {light.GiEnvOcclusion:0.###}; exposure {light.Exposure:0.###}; grade {(light.Grade is null ? "none" : "the environment's table")}; {light.PointCount} point lights{(light.PlayerIndex >= 0 ? ", the player's lamp the last" : string.Empty)}"));
        text.AppendLine().AppendLine("=== the clouds' noise: which of the engine's noise candidates the game's light on the ground the pane lights follows (CloudNoise)");
        text.Append(CloudsMeasured(header, samples, clouds is null ? cloudsWhy : string.Empty,
            [.. CaptureLightExtras.NoiseCandidates.Select(path => (path, read?.Invoke(path) is { } bytes ? GameArt.Decode(bytes) : null))]));
        text.AppendLine().AppendLine("=== the numbers beside it");
        text.AppendLine(Numbers + ": " + BinaryFormat);
        text.AppendLine(PanePicture + ": the pane's picture through the game's camera, at the drawn size");
        text.AppendLine(RatioPicture + ": each compared pixel's log2 of the game's luminance over the pane's - blue the game darker, red brighter, white equal, -2 to 2; magenta a roof section, left out of the statistics; grey left out");
        text.AppendLine(SunPicture + ": the game's picture with the pane's sun over it - yellow lit, blue in shade, green between; dark left out");
        text.AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"{BlocksPicture}: each block of the ground the report counts ({BlockSide} x {BlockSide} gathered pixels, {FewestPerBlock} ground pixels or more beyond {NearPlayer:0} units across of the player) filled by the log2 of its pixels' median game over pane, coloured as the ratio picture is; the game's grey elsewhere"));
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
        var text = new StringBuilder(Report(header, samples, "light against the game replay"));

        // THE CLOUDS' NOISE AGAIN from the candidates light-extras wrote whole beside the capture.
        text.AppendLine().AppendLine("=== the clouds' noise");
        text.Append(CloudsMeasured(header, samples, header.Clouds is null ? "the numbers keep no clouds (before version 3, or the .env left a key out)" : string.Empty,
            [.. CaptureLightExtras.NoiseCandidates.Select(candidate =>
                (candidate, File.Exists(CaptureParts.Find(folder, CaptureLightExtras.WholeFile(candidate)))
                    ? GameArt.Decode(File.ReadAllBytes(CaptureParts.Find(folder, CaptureLightExtras.WholeFile(candidate))))
                    : null))]));
        return text.ToString();
    }

    /// <summary>
    /// The clouds' noise measured over the samples (CloudNoise.Measure) - the ground the pane lights, each with the game's unexposed light over
    /// the pane's - or why it is not.
    /// </summary>
    internal static string CloudsMeasured(Header header, IReadOnlyList<Sample> samples, string why, IReadOnlyList<(string Path, GamePicture? Picture)> candidates)
    {
        if (header.Clouds is not { } clouds)
        {
            return "no measurement: " + why + Environment.NewLine;
        }

        if (float.IsNaN(header.Origin.X) || float.IsNaN(header.Sun.X))
        {
            return "no measurement: the numbers keep no place in the area" + Environment.NewLine;
        }

        bool[] roof = Kinds(header, RoofWord);
        var points = new List<CloudNoise.Point>();
        foreach (Sample one in samples)
        {
            if (one.Class != Lighting.Lit || float.IsNaN(one.Place.X) || (one.Material >= 0 && one.Material < roof.Length && roof[one.Material]))
            {
                continue;
            }

            double pane = CaptureAtGame.Luminance(one.Unexposed), game = CaptureAtGame.Luminance(one.GameUnexposed);
            if (pane > 1e-5 && double.IsFinite(game))
            {
                points.Add(new CloudNoise.Point(new Vector2(one.Place.X, one.Place.Y) + header.Origin, (float)Math.Clamp(game / pane, 0d, 8d)));
            }
        }

        Vector2 player = float.IsNaN(header.PlayerArea.X) ? new Vector2(1e9f) : header.PlayerArea;
        return CloudNoise.Measure(clouds, header.Sun, player, points, candidates);
    }

    /// <summary>One compared pixel.</summary>
    internal readonly record struct Sample(
        int Index, int Shape, int Material, Vector3 Game, Vector3 GameUnexposed, Vector3 Pane, Vector3 Albedo, float Sun, float Facing, Vector3 SunDiffuse,
        Vector3 Cube, Vector3 GiFlat, Vector3 Room, Vector3 Player, Vector3 Specular, Vector3 Unexposed, float Depth, float PlayerDistance)
    {
        /// <summary>Where the pixel's surface stands in the stage's frame - NaN in numbers of version 1 and 2, which kept none.</summary>
        public Vector3 Place { get; init; } = new(float.NaN);

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
    internal sealed record Header(int Width, int Height, int Step, int Factor, float Exposure, int[] Left, IReadOnlyList<string> Materials)
    {
        /// <summary>The stage's corner in the area - a place in its frame plus this is the area's - NaN before version 3.</summary>
        public Vector2 Origin { get; init; } = new(float.NaN);

        /// <summary>Where the player stood in the area, x and y - NaN before version 3 or where no player was read.</summary>
        public Vector2 PlayerArea { get; init; } = new(float.NaN);

        /// <summary>The way the pane's sun travels - NaN before version 3.</summary>
        public Vector3 Sun { get; init; } = new(float.NaN);

        /// <summary>The area's clouds as its .env sets them, or null - before version 3, or where the .env leaves out a key (see AreaClouds.Of).</summary>
        public AreaClouds? Clouds { get; init; }
    }

    /// <summary>The material a shape wears, as the report groups it - a roof section's and a ground's each under its own word.</summary>
    /// <remarks>
    /// THE ROOF SECTION'S WORD FIRST: its pixels are kept out of everything, so a ground wearing one (none seen) is no measure of the ground's
    /// light either. And IN THE NAME, not a field of its own, so the numbers keep their layout and a file written before carries no roof.
    ///
    /// TWO GROUNDS MIXED ARE NAMED BY BOTH (v0.1.375): their name (GroundBlend) is no file, and taken as one its last "file" was the second
    /// ground's name with the mix's words for an extension, or the mask's - so a corner mix grouped under its second ground's own name and a
    /// mask mix under the mask's, one ground's light measured over another's pixels.
    /// </remarks>
    private static string MaterialName(MonsterModel model, int shape, RoofSections roofs)
    {
        string material = shape < model.ShapeMaterials.Count ? MaterialFile.Bare(model.ShapeMaterials[shape]) : string.Empty;
        string source = shape < model.ShapeSources.Count ? model.ShapeSources[shape] : string.Empty;
        string name = material.Length == 0 ? "(no material)"
            : GroundBlend.Parts(material, out string first, out string second, out _)
                ? Path.GetFileNameWithoutExtension(first) + " + " + Path.GetFileNameWithoutExtension(second)
                : Path.GetFileNameWithoutExtension(material);
        return roofs.Of(material) ? RoofWord + name
            : source.StartsWith(RoomModels.GroundSource, StringComparison.Ordinal) ? GroundWord + name
            : name;
    }

    /// <summary>Which materials say they are roof sections - each .mat read once - and how many were asked, said so, and did not read.</summary>
    internal sealed class RoofSections(Func<string, byte[]?>? read)
    {
        private readonly Dictionary<string, bool> _said = new(StringComparer.OrdinalIgnoreCase);
        private int _roofs;
        private int _unread;

        /// <summary>Whether a material, by its file, says it is a roof section - false where nothing can be read.</summary>
        /// <remarks>
        /// TWO GROUNDS MIXED ARE ASKED AS THE TWO MATERIALS THEY ARE (v0.1.375), the name they are worn under (GroundBlend) being no file:
        /// asked as one, every mix a stage wears was counted as a material not in the install, while each of its grounds read. The mix is a
        /// roof section where either of its grounds says so, as a ground wearing one is (MaterialName).
        /// </remarks>
        public bool Of(string material)
        {
            if (read is null || material.Length == 0)
            {
                return false;
            }

            if (GroundBlend.Parts(material, out string first, out string second, out _))
            {
                bool firstSays = Of(MaterialFile.Bare(first)), secondSays = Of(MaterialFile.Bare(second));
                return firstSays || secondSays;
            }

            if (_said.TryGetValue(material, out bool roof))
            {
                return roof;
            }

            // A FILE THAT DOES NOT READ costs its own answer, not the part: counted, and taken as no roof.
            byte[]? bytes;
            try
            {
                bytes = read(material);
            }
            catch (Exception failed) when (failed is IOException or InvalidDataException)
            {
                bytes = null;
            }

            _unread += bytes is { Length: > 0 } ? 0 : 1;
            roof = MaterialFile.Read(bytes).RoofSection;
            _roofs += roof ? 1 : 0;
            _said[material] = roof;
            return roof;
        }

        /// <summary>The line saying what was read.</summary>
        public string Said()
            => read is null
                ? "roof sections: not looked for - no install to read the materials' .mat from, so every shape is compared"
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"roof sections: {_roofs} of the {_said.Count} materials the compared pixels wear say \"roofsection\" in their .mat{(_unread > 0 ? $" ({_unread} not in the install or not read)" : string.Empty)} - named \"{RoofWord}...\", their pixels left out of every statistic and set out on their own");
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
    internal static string Report(Header header, IReadOnlyList<Sample> every, string head)
    {
        // ROOF SECTIONS APART: the game hides them round the player and shows what stands behind, so their pixels measure no light of the
        // game's - kept out of every statistic and set out on their own. Told by their names alone, so a replay keeps them out as the part
        // did, and numbers written before the word read as they were.
        bool[] roof = Kinds(header, RoofWord);
        var roofs = new List<Sample>();
        var samples = new List<Sample>(every.Count);
        foreach (Sample one in every)
        {
            (Of(roof, one) ? roofs : samples).Add(one);
        }

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
            $"left out: {header.Left[0]:N0} no surface, {header.Left[1]:N0} water, {header.Left[2]:N0} translucent over it, {header.Left[3]:N0} beach or lifted water over it, {header.Left[4]:N0} the game's interface or something moving, {header.Left[5]:N0} past the game's far plane, {header.Left[6]:N0} a shape's edge, {header.Left[7]:N0} no game picture, {header.Left[8]:N0} the game's picture at its ceiling, {roofs.Count:N0} roof sections the game hides round the player"));
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

        text.AppendLine().AppendLine("=== roof sections - the game hides some round the player (said from the live client), the game over the pane by the distance across to the player");
        foreach (string line in RoofLines(header, roofs))
        {
            text.AppendLine(line);
        }

        List<GroundBlock> blocks = GroundBlocks(header, samples, out int taken, out int sparse);
        text.AppendLine().AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"=== the ground block by block - beyond {NearPlayer:0} units across of the player (all of it where no player was read), in blocks of {BlockSide} x {BlockSide} gathered pixels with {FewestPerBlock} ground pixels or more"));
        foreach (string line in BlockRows(blocks, taken, sparse))
        {
            text.AppendLine(line);
        }

        text.AppendLine().AppendLine("=== the player's lamp - in the pane's shade, by the distance across to the player: the lamp's share of the pane's light, and the gains the game asks of the lamp (g) and of the rest of the light (a)");
        foreach (string line in LampLines(samples))
        {
            text.AppendLine(line);
        }

        text.AppendLine().AppendLine(string.Create(
            CultureInfo.InvariantCulture,
            $"=== the ground's blocks one by one - column and row of {BlockSide} gathered pixels from the top left, as {BlocksPicture} draws them"));
        if (blocks.Count > 0)
        {
            text.AppendLine("  column  row  pixels  sun share  game light  pane light  game over pane");
            foreach (GroundBlock block in blocks)
            {
                text.AppendLine(string.Create(
                    CultureInfo.InvariantCulture,
                    $"  {block.Column,6} {block.Row,4} {block.Count,7:N0} {block.SunShare,10:0.###} {block.GameLight,11:0.####} {block.PaneLight,11:0.####} {block.GameOverPane,15:0.###}"));
            }
        }
        else
        {
            text.AppendLine("  none");
        }

        return text.ToString();
    }

    /// <summary>Which of the header's materials are named under a word.</summary>
    private static bool[] Kinds(Header header, string word) => [.. header.Materials.Select(name => name.StartsWith(word, StringComparison.Ordinal))];

    /// <summary>Whether a sample wears a material of a kind <see cref="Kinds"/> told.</summary>
    private static bool Of(bool[] kind, Sample one) => one.Material >= 0 && one.Material < kind.Length && kind[one.Material];

    /// <summary>The median of each pixel's game luminance over the pane's, where the pane's is not nought.</summary>
    private static double MedianRatio(IEnumerable<Sample> samples)
    {
        var ratios = new List<double>();
        foreach (Sample one in samples)
        {
            double pane = CaptureAtGame.Luminance(one.Pane);
            if (pane > 1e-5)
            {
                ratios.Add(CaptureAtGame.Luminance(one.Game) / pane);
            }
        }

        return Median(ratios);
    }

    /// <summary>The roof sections' lines: each material's pixels and the game over the pane, by the distance across to the player.</summary>
    private static List<string> RoofLines(Header header, List<Sample> roofs)
    {
        if (roofs.Count == 0)
        {
            return ["  none: no material a compared pixel wears says \"roofsection\" - or none was read (the part's own line under \"what was drawn\" says which)"];
        }

        int kinds = roofs.Select(one => one.Material).Distinct().Count();
        var lines = new List<string>
        {
            string.Create(
                CultureInfo.InvariantCulture,
                $"  {roofs.Count:N0} pixels of {kinds} {(kinds == 1 ? "material" : "materials")}, left out of every statistic above and below; a ratio is the median of each pixel's game luminance over the pane's"),
        };
        foreach (IGrouping<int, Sample> group in roofs.GroupBy(one => one.Material).OrderByDescending(group => group.Count()))
        {
            List<Sample> those = [.. group];
            string name = group.Key < header.Materials.Count ? header.Materials[group.Key] : "?";
            lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {name}: {those.Count:N0} pixels, ratio {MedianRatio(those):0.###}"));
            List<IGrouping<int, Sample>> bins = [.. those.Where(one => float.IsFinite(one.PlayerDistance)).GroupBy(one => (int)(one.PlayerDistance / RoofBin)).OrderBy(bin => bin.Key)];
            if (bins.Count == 0)
            {
                lines.Add("    no player was read - no distance to set them out by");
            }

            foreach (IGrouping<int, Sample> bin in bins)
            {
                List<Sample> inBin = [.. bin];
                lines.Add(string.Create(
                    CultureInfo.InvariantCulture,
                    $"    {bin.Key * RoofBin,6:0}-{(bin.Key + 1) * RoofBin,-6:0} units: {inBin.Count,7:N0} pixels, ratio {MedianRatio(inBin):0.###}"));
            }
        }

        return lines;
    }

    /// <summary>One block of the ground the report counts.</summary>
    /// <param name="Column">Its column, from the left, in blocks.</param>
    /// <param name="Row">Its row, from the top, in blocks.</param>
    /// <param name="Count">Its ground pixels.</param>
    /// <param name="SunShare">The share of them in the pane's sun (<see cref="Lighting.Lit"/>).</param>
    /// <param name="GameLight">The median of each one's game luminance over its albedo's.</param>
    /// <param name="PaneLight">The median of each one's pane luminance over its albedo's.</param>
    /// <param name="GameOverPane">The median of each one's game luminance over the pane's.</param>
    internal readonly record struct GroundBlock(int Column, int Row, int Count, double SunShare, double GameLight, double PaneLight, double GameOverPane);

    /// <summary>
    /// The ground's samples beyond the player's lamp in blocks of <see cref="BlockSide"/> gathered pixels, those with
    /// <see cref="FewestPerBlock"/> or more - in rows from the top, each from the left.
    /// </summary>
    /// <remarks>
    /// A BLOCK'S LIGHT IS ITS PIXELS' MEDIAN, so a stone, a fern or a weapon the tool strews by its own pattern where the game strews by
    /// another moves nothing, while the light over the ground - the sun and its shadows, which are what is in question - stays. Beyond the
    /// player's lamp, whose radius in the pane is CHOSEN and whose strength in the game the lamp's own section measures, so the two
    /// questions do not mix; a pixel with no player read is beyond it.
    /// </remarks>
    /// <param name="header">The numbers' header - its width and its materials' names.</param>
    /// <param name="samples">The compared pixels.</param>
    /// <param name="taken">The ground pixels beyond the lamp.</param>
    /// <param name="sparse">Those of them in blocks too sparse to count.</param>
    internal static List<GroundBlock> GroundBlocks(Header header, IReadOnlyList<Sample> samples, out int taken, out int sparse)
    {
        bool[] ground = Kinds(header, GroundWord);
        int width = Math.Max(1, header.Width);
        int columns = (width + BlockSide - 1) / BlockSide;
        var members = new Dictionary<int, List<int>>();
        taken = 0;
        for (var at = 0; at < samples.Count; at++)
        {
            Sample one = samples[at];
            if (!Of(ground, one) || one.Index < 0 || one.PlayerDistance <= NearPlayer)
            {
                continue;
            }

            taken++;
            int key = (one.Index / width / BlockSide * columns) + (one.Index % width / BlockSide);
            if (!members.TryGetValue(key, out List<int>? those))
            {
                those = [];
                members[key] = those;
            }

            those.Add(at);
        }

        var blocks = new List<GroundBlock>();
        sparse = 0;
        var game = new List<double>();
        var pane = new List<double>();
        var ratio = new List<double>();
        foreach ((int key, List<int> those) in members.OrderBy(pair => pair.Key))
        {
            if (those.Count < FewestPerBlock)
            {
                sparse += those.Count;
                continue;
            }

            game.Clear();
            pane.Clear();
            ratio.Clear();
            var lit = 0;
            foreach (int at in those)
            {
                Sample one = samples[at];
                lit += one.Class == Lighting.Lit ? 1 : 0;
                double albedo = Math.Max(CaptureAtGame.Luminance(one.Albedo), 1e-4);
                double g = CaptureAtGame.Luminance(one.Game), p = CaptureAtGame.Luminance(one.Pane);
                game.Add(g / albedo);
                pane.Add(p / albedo);
                if (p > 1e-5)
                {
                    ratio.Add(g / p);
                }
            }

            blocks.Add(new GroundBlock(key % columns, key / columns, those.Count, (double)lit / those.Count, Median(game), Median(pane), Median(ratio)));
        }

        return blocks;
    }

    /// <summary>The ground's blocks set out by the share of each the pane's sun reaches, and the two pictures' contrast between lit and shaded.</summary>
    private static List<string> BlockRows(List<GroundBlock> blocks, int taken, int sparse)
    {
        var lines = new List<string>
        {
            "  pixel by pixel the comparison is ruled by small things that cannot match - the ground's scatter is strewn by the tool's own CHOSEN pattern - so the ground is held here in blocks, each one's light its pixels' median: the luminance over the albedo's",
            string.Create(CultureInfo.InvariantCulture, $"  {blocks.Count:N0} blocks of {taken - sparse:N0} ground pixels; {sparse:N0} more in blocks of fewer than {FewestPerBlock}"),
        };
        if (blocks.Count == 0)
        {
            return lines;
        }

        lines.Add("  sun share  blocks  game light  its quartiles         pane light  game over pane");
        foreach ((string label, double from, double to) in ShareRows)
        {
            List<GroundBlock> row = [.. blocks.Where(block => block.SunShare >= from && block.SunShare < to)];
            if (row.Count == 0)
            {
                lines.Add(string.Create(CultureInfo.InvariantCulture, $"  {label,-9} {0,7}"));
                continue;
            }

            List<double> game = [.. row.Select(block => block.GameLight)];
            game.Sort();
            int n = game.Count;
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  {label,-9} {n,7:N0} {game[n / 2],11:0.####} {game[n / 4],9:0.####}..{game[n * 3 / 4],-9:0.####} {Median([.. row.Select(block => block.PaneLight)]),10:0.####} {Median([.. row.Select(block => block.GameOverPane)]),15:0.###}"));
        }

        // THE CONTRAST, lit blocks over shaded ones in each picture: the pane's sun against its own shade beside the game's over the same
        // blocks - a game keeping less of it darkens its sun where the pane lights the ground (or lightens its shade).
        List<GroundBlock> lit = [.. blocks.Where(block => block.SunShare >= 0.7)];
        List<GroundBlock> shaded = [.. blocks.Where(block => block.SunShare < 0.1)];
        if (lit.Count == 0 || shaded.Count == 0)
        {
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  contrast: not measured - {lit.Count} blocks lit (a sun share of 0.7 or more), {shaded.Count} shaded (under 0.1)"));
            return lines;
        }

        double pane = Median([.. lit.Select(block => block.PaneLight)]) / Median([.. shaded.Select(block => block.PaneLight)]);
        double gameContrast = Median([.. lit.Select(block => block.GameLight)]) / Median([.. shaded.Select(block => block.GameLight)]);
        lines.Add(string.Create(
            CultureInfo.InvariantCulture,
            $"  contrast: the pane's light in its lit blocks (a sun share of 0.7 or more, {lit.Count:N0}) over its shaded ones' (under 0.1, {shaded.Count:N0}) {pane:0.##} to 1, the game's over the same blocks {gameContrast:0.##} to 1 - the game keeps {gameContrast / pane:0.###} of the pane's"));
        return lines;
    }

    /// <summary>
    /// The player's lamp in the pane's shade by the distance to the player: its share of the pane's light, and the game's light fitted as
    /// a times the rest of the pane's light plus g times its lamp - each pixel's luminance before the exposure, the game's taken back through
    /// the pane's grade and exposure - g the gain the game asks of the lamp and a the scale it asks of the rest; pooled first, then by bin.
    /// </summary>
    /// <remarks>
    /// IN THE SHADE ALONE, where the sun adds nothing to either picture. A FIT, NOT EACH PIXEL'S RATIO (v0.1.375): the ratio a pixel asks
    /// of its lamp, (game - rest) / lamp, is g + (a - 1) rest / lamp - the rest's mismatch divided by the lamp's share of the light, twentyfold
    /// where the lamp gives a twentieth - so wherever the game's shade is not the pane's, its median measures the shade and not the lamp. The
    /// Red Vale's v0.1.372 capture asked a pooled median of 1.462 (-1.038..5.511) so, the bins 500 to 600 and 700 to 800 units out asking
    /// negative gains of a lamp; its pixels the lamp gives 0.05 to 0.1 of their light asked -0.837, those it gives a half or more 2.874
    /// (MEASURED, its replay). The fit (<see cref="LampFit"/>, every shaded pixel the lamp reaches) gives the rest's mismatch to a and leaves
    /// g the lamp's - the same pixels ask g 2.725 and a 1.341, over 29,425 - and the line by the lamp's share sets the ratios out beside it,
    /// so a reader sees why its bands disagree. Least squares weighs what a pixel says of g by its lamp's light, so where the lamp gives
    /// next to nothing a bin's g says nothing (that capture's 1,100 to 1,200 units asked 1,439 of 57 pixels) while the pooled g stands. One
    /// says the game has it as the pane has it; it is a measurement for the lamp, which is not changed here.
    /// </remarks>
    private static List<string> LampLines(List<Sample> samples)
    {
        var shares = new List<double>[LampBins];
        var fits = new LampFit[LampBins];
        var counts = new int[LampBins];
        for (var bin = 0; bin < LampBins; bin++)
        {
            shares[bin] = [];
            fits[bin] = new LampFit();
        }

        var pooled = new LampFit();
        var ratios = new List<double>[LampShareBands.Length];
        for (var band = 0; band < ratios.Length; band++)
        {
            ratios[band] = [];
        }

        var placed = false;
        foreach (Sample one in samples)
        {
            if (one.Class != Lighting.Shade || !float.IsFinite(one.PlayerDistance))
            {
                continue;
            }

            placed = true;
            int bin = (int)(one.PlayerDistance / LampBin);
            if (bin < 0 || bin >= LampBins)
            {
                continue;
            }

            counts[bin]++;
            double light = CaptureAtGame.Luminance(one.Unexposed), lamp = CaptureAtGame.Luminance(one.Player);
            if (light > 1e-6)
            {
                shares[bin].Add(lamp / light);
            }

            if (!(lamp > 0d))
            {
                continue;
            }

            // THE REST NOUGHT AT LEAST: where the lamp is all of a pixel's light, the halves a replay reads can round it past the whole.
            double rest = Math.Max(0d, light - lamp), game = CaptureAtGame.Luminance(one.GameUnexposed);
            fits[bin].Add(rest, lamp, game);
            pooled.Add(rest, lamp, game);
            int band = light > 1e-6 ? BandOf(lamp / light) : -1;
            if (band >= 0)
            {
                ratios[band].Add((game - rest) / lamp);
            }
        }

        var lines = new List<string>
        {
            "  each pixel's luminance before the exposure: the lamp's share of the pane's light, the median over the bin; and the game's light taken back through the pane's grade and exposure fitted as a x the rest + g x the lamp - the rest the pane's light without its lamp - by least squares over the pixels the lamp reaches, a and g nought at least: g the gain the game asks of the lamp, a the scale it asks of the rest; one says the game has it as the pane has it, and where the lamp gives next to nothing of the light its g says nothing",
        };
        if (!placed)
        {
            lines.Add("  not measured - no player was read, so there is no distance to set the pixels out by");
            return lines;
        }

        lines.Add(pooled.Count == 0
            ? string.Create(
                CultureInfo.InvariantCulture,
                $"  all: the lamp reaches no pixel in the pane's shade within {LampBins * LampBin:N0} units - none is drawn, or it reaches no shade")
            : pooled.Solve() is { } all
                ? string.Create(
                    CultureInfo.InvariantCulture,
                    $"  all within {LampBins * LampBin:N0} units: the lamp's gain g {all.Lamp:0.###}, the rest's a {all.Others:0.###} - over {pooled.Count:N0} pixels in the pane's shade the lamp reaches")
                : string.Create(
                    CultureInfo.InvariantCulture,
                    $"  all within {LampBins * LampBin:N0} units: the lamp's gain not told from the rest's - each of the {pooled.Count:N0} pixels in the pane's shade it reaches takes the same share of its light from it"));

        for (var bin = 0; bin < LampBins; bin++)
        {
            if (counts[bin] == 0)
            {
                continue;
            }

            LampFit fit = fits[bin];
            string asked = fit.Count == 0 ? "the lamp reaches none of them"
                : fit.Solve() is { } one ? string.Create(CultureInfo.InvariantCulture, $"g {one.Lamp:0.###}, a {one.Others:0.###} over {fit.Count:N0} the lamp reaches")
                : string.Create(CultureInfo.InvariantCulture, $"g not told from a over {fit.Count:N0} the lamp reaches, each the same share of its light");
            lines.Add(string.Create(
                CultureInfo.InvariantCulture,
                $"  {bin * LampBin,5:0}-{(bin + 1) * LampBin,-5:0} units: {counts[bin],7:N0} pixels in shade, the lamp's share {Median(shares[bin]):0.###}; {asked}"));
        }

        // EACH PIXEL'S OWN RATIO BESIDE THE FIT, by the lamp's share: where the bands disagree, the rest is off and the thin shares say by how much.
        string banded = string.Join(", ", LampShareBands.Select((band, at) => ratios[at].Count == 0
            ? $"{band.Label} - (0)"
            : string.Create(CultureInfo.InvariantCulture, $"{band.Label} {Median(ratios[at]):0.###} ({ratios[at].Count:N0})")));
        lines.Add("  beside it each pixel's own ratio, (the game less the rest) over the lamp - g + (a - 1) rest / lamp, the rest's mismatch over the lamp's share - its median by that share: " + banded);
        return lines;
    }

    /// <summary>Which of <see cref="LampShareBands"/> a lamp's share falls in, or -1.</summary>
    private static int BandOf(double share)
    {
        for (var band = 0; band < LampShareBands.Length; band++)
        {
            if (share >= LampShareBands[band].From && share < LampShareBands[band].To)
            {
                return band;
            }
        }

        return -1;
    }

    /// <summary>
    /// The game's light fitted as a times the rest of the pane's light plus g times its lamp, by least squares with a and g nought at least -
    /// the two-by-two normal equations' sums, gathered a pixel at a time.
    /// </summary>
    /// <remarks>
    /// NOUGHT AT LEAST because neither light can be taken away. Where the normal equations' answer puts one below nought, the least squares
    /// over the quarter of the plane the two may stand in lies on its edge - that one nought and the other fitted alone - and of the two edges
    /// on the one whose own best leaves the less, which is how it is taken. Where every pixel's lamp gives the same share of its light - one
    /// pixel alone among them - the two cannot be told apart, and no fit is given rather than a nought.
    /// </remarks>
    internal sealed class LampFit
    {
        private double _restRest, _restLamp, _lampLamp, _restGame, _lampGame;

        /// <summary>The pixels added.</summary>
        public int Count { get; private set; }

        /// <summary>One pixel's luminances: the pane's light without its lamp, the lamp's, and the game's.</summary>
        public void Add(double rest, double lamp, double game)
        {
            _restRest += rest * rest;
            _restLamp += rest * lamp;
            _lampLamp += lamp * lamp;
            _restGame += rest * game;
            _lampGame += lamp * game;
            Count++;
        }

        /// <summary>
        /// The fit: a, the scale the game asks of the rest of the light (Others), and g, the gain it asks of the lamp - or null where nothing
        /// was added or the two cannot be told apart.
        /// </summary>
        public (double Others, double Lamp)? Solve()
        {
            double determinant = (_restRest * _lampLamp) - (_restLamp * _restLamp);
            if (Count == 0 || !(determinant > 1e-9 * _restRest * _lampLamp))
            {
                return null;
            }

            double rest = ((_lampLamp * _restGame) - (_restLamp * _lampGame)) / determinant;
            double lamp = ((_restRest * _lampGame) - (_restLamp * _restGame)) / determinant;
            if (rest >= 0d && lamp >= 0d)
            {
                return (rest, lamp);
            }

            (double Others, double Lamp) restAlone = (Math.Max(0d, _restGame / _restRest), 0d);
            (double Others, double Lamp) lampAlone = (0d, Math.Max(0d, _lampGame / _lampLamp));
            return Left(restAlone) <= Left(lampAlone) ? restAlone : lampAlone;
        }

        /// <summary>The squares a fit leaves, less the game's own sum of squares - the same for every fit, so enough to tell two apart.</summary>
        private double Left((double Others, double Lamp) fit)
            => (fit.Others * fit.Others * _restRest) + (2d * fit.Others * fit.Lamp * _restLamp) + (fit.Lamp * fit.Lamp * _lampLamp)
                - (2d * ((fit.Others * _restGame) + (fit.Lamp * _lampGame)));
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

    /// <summary>A ratio's colour in the pictures: its log2 from -2 to 2, blue the game darker, white equal, red brighter.</summary>
    private static (byte R, byte G, byte B) Heat(double ratio)
    {
        float t = Math.Clamp((float)(Math.Log2(ratio) / 2d), -1f, 1f);
        return ((byte)(t >= 0f ? 255 : (int)(255 * (1f + t))), (byte)(255 * (1f - MathF.Abs(t))), (byte)(t <= 0f ? 255 : (int)(255 * (1f - t))));
    }

    /// <summary>The pictures beside the report.</summary>
    private static List<(string File, GamePicture Picture)> Pictures(CaptureAtGame scene, Header header, IReadOnlyList<Sample> samples)
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

        // THE BLOCKS OVER THE GAME'S GREY, the ground the report counts filled block by block.
        byte[] tiles = (byte[])ratio.Clone();
        foreach (GroundBlock block in GroundBlocks(header, samples, out _, out _))
        {
            if (!(block.GameOverPane > 0d))
            {
                continue;
            }

            (byte red, byte green, byte blue) = Heat(block.GameOverPane);
            for (int y = block.Row * BlockSide, bottom = Math.Min(h, y + BlockSide); y < bottom; y++)
            {
                for (int x = block.Column * BlockSide, right = Math.Min(w, x + BlockSide); x < right; x++)
                {
                    int to = ((y * w) + x) * 4;
                    tiles[to] = red;
                    tiles[to + 1] = green;
                    tiles[to + 2] = blue;
                }
            }
        }

        bool[] roof = Kinds(header, RoofWord);
        foreach (Sample one in samples)
        {
            if (one.Index >= w * h)
            {
                continue;
            }

            double g = CaptureAtGame.Luminance(one.Game), p = CaptureAtGame.Luminance(one.Pane);
            if (Of(roof, one))
            {
                int at = one.Index * 4;
                ratio[at] = 255;
                ratio[at + 1] = 0;
                ratio[at + 2] = 255;
            }
            else if (g > 1e-5 && p > 1e-5)
            {
                int at = one.Index * 4;
                (ratio[at], ratio[at + 1], ratio[at + 2]) = Heat(g / p);
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
            (BlocksPicture, new GamePicture(w, h, tiles)),
        ];
    }

    /// <summary>The samples as bytes - <see cref="BinaryFormat"/> - of version 3, or 2 for a test that makes an older file.</summary>
    internal static byte[] Bytes(Header header, IReadOnlyList<Sample> samples, int version = 3)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write("PLAG"u8);
        writer.Write(version);
        writer.Write(header.Width);
        writer.Write(header.Height);
        writer.Write(header.Step);
        writer.Write(header.Factor);
        writer.Write(header.Exposure);
        foreach (int one in header.Left)
        {
            writer.Write(one);
        }

        // VERSION 3: where the stage stands in the area, the player, the sun and the clouds - what the clouds' noise is measured with.
        if (version >= 3)
        {
            ThreeOn(writer, header);
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
            if (version >= 3)
            {
                writer.Write(one.Place.X);
                writer.Write(one.Place.Y);
                writer.Write(one.Place.Z);
            }
        }

        writer.Flush();
        return stream.ToArray();
    }

    /// <summary>Version 3's header past the left-out counts - see <see cref="BinaryFormat"/>.</summary>
    private static void ThreeOn(BinaryWriter writer, Header header)
    {
        writer.Write(header.Origin.X);
        writer.Write(header.Origin.Y);
        writer.Write(header.PlayerArea.X);
        writer.Write(header.PlayerArea.Y);
        writer.Write(header.Sun.X);
        writer.Write(header.Sun.Y);
        writer.Write(header.Sun.Z);
        AreaClouds? clouds = header.Clouds;
        writer.Write(clouds is null ? (byte)0 : (byte)1);
        foreach (float one in clouds is null ? new float[9]
            : [clouds.Scale, clouds.Intensity, clouds.Midpoint, clouds.Sharpness, clouds.Speed, clouds.Angle, clouds.FadeRadius, clouds.PreFade, clouds.PostFade])
        {
            writer.Write(one);
        }
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
        if (version is not (1 or 2 or 3))
        {
            throw new InvalidDataException("not the light-against-the-game numbers of version 1, 2 or 3");
        }

        // VERSION 1 KEPT NO CEILING: its pixels at the game's ceiling are among the samples, and its ninth reason reads nought.
        int width = reader.ReadInt32(), height = reader.ReadInt32(), step = reader.ReadInt32(), factor = reader.ReadInt32();
        float exposure = reader.ReadSingle();
        var left = new int[Reasons];
        for (var at = 0; at < (version == 1 ? 8 : Reasons); at++)
        {
            left[at] = reader.ReadInt32();
        }

        Vector2 origin = new(float.NaN), playerArea = new(float.NaN);
        Vector3 sun = new(float.NaN);
        AreaClouds? clouds = null;
        if (version >= 3)
        {
            origin = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            playerArea = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            sun = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            bool any = reader.ReadByte() != 0;
            var values = new float[9];
            for (var at = 0; at < values.Length; at++)
            {
                values[at] = reader.ReadSingle();
            }

            clouds = any ? new AreaClouds(values[0], values[1], values[2], values[3], values[4], values[5], values[6], values[7], values[8]) : null;
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
            float sunShare = (float)reader.ReadHalf(), facing = (float)reader.ReadHalf();
            Vector3 sunDiffuse = Halves(reader), cube = Halves(reader), giFlat = Halves(reader), room = Halves(reader), player = Halves(reader), specular = Halves(reader), unexposed = Halves(reader);
            float depth = reader.ReadSingle(), distance = reader.ReadSingle();
            Vector3 place = version >= 3 ? new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()) : new Vector3(float.NaN);
            list.Add(new Sample(index, shape, material, game, gameUnexposed, pane, albedo, sunShare, facing, sunDiffuse, cube, giFlat, room, player, specular, unexposed, depth, distance) { Place = place });
        }

        return (new Header(width, height, step, factor, exposure, left, materials) { Origin = origin, PlayerArea = playerArea, Sun = sun, Clouds = clouds }, list);
    }

    private static Vector3 Halves(BinaryReader reader)
        => new((float)reader.ReadHalf(), (float)reader.ReadHalf(), (float)reader.ReadHalf());
}
