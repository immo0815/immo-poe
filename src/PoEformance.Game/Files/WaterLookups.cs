using System.Globalization;
using System.Text;

namespace PoEformance.Game.Files;

/// <summary>
/// The engine's own water lookups, out of the install: its ripple texture and its swell's cross-section with that cross-section's
/// gradient - each taken only where it reads as the shaders read it, and every number the taking rests on said.
/// </summary>
/// <remarks>
/// FOUND BY THE CAPTURE'S WATER TEXTURES PART (v0.1.228 and v0.1.230, Port): the game's program names three lookups and no
/// more - <c>Art/2DArt/Lookup/surface_ripple.dds</c>, <c>gerstner_wave_profile.dds</c> and <c>wave_profile_gradient.dds</c> - and
/// the install's lookup folder holds two more profiles beside them, <c>wave_profile.dds</c> and <c>gerstner_positive_wave_profile.dds</c>.
/// The water pass's sources bind its textures by sampler name alone (internalonly.ffx: <c>surface_tex_sampler</c>, "ripple
/// heightfield and gradient"; <c>wave_data_sampler</c>, "wave cross-section"; <c>wave_gradient_sampler</c>, "wave cross-section
/// gradient"), so which file stands behind which name is settled by the files themselves, not by their names:
///
/// - THE RIPPLE TEXTURE's red is a height and its green and blue that height's slope a texel (GetWaterVolumeStateFieldOctave
///   takes <c>gb * amp * freq</c> as the slope of <c>r * amp</c> at <c>uv = q / 128 * freq</c>), so its green and blue must go
///   with the red's own differences along x and y. It is 128 square, as the engine's <c>textureSize</c> says.
/// - THE CROSS-SECTION's gradient is its red's rise along the phase (GetTidalWaterPlanarState adds <c>gradient.r * phaseGradient</c>
///   where the height is <c>profile.r - 0.5</c> read at the same place), so the gradient's red must go with the profile's own
///   differences along its width. Each profile is held against <c>wave_profile_gradient.dds</c> so, and the one that goes with it
///   is the swell's - the others are another wave's (the beach's <c>gerstner_profile</c> takes a precomputed shape of its own).
///
/// Going with is a correlation of at least <see cref="Agreement"/> either way round - the decoding is what is checked; a sign or a
/// scale the engine's file carries is the engine's, and it is read as the engine reads it. Where a lookup does not read or does
/// not go with its partner, the stand-in stays and the line says why.
/// </remarks>
public sealed class WaterLookups
{
    /// <summary>The engine's ripple texture - <c>surface_tex_sampler</c>.</summary>
    public const string RipplePath = "Art/2DArt/Lookup/surface_ripple.dds";

    /// <summary>The cross-section's gradient - <c>wave_gradient_sampler</c>.</summary>
    public const string GradientPath = "Art/2DArt/Lookup/wave_profile_gradient.dds";

    /// <summary>The profiles one of which is <c>wave_data_sampler</c> - the one the program names first.</summary>
    public static IReadOnlyList<string> ProfilePaths { get; } =
    [
        "Art/2DArt/Lookup/gerstner_wave_profile.dds",
        "Art/2DArt/Lookup/wave_profile.dds",
        "Art/2DArt/Lookup/gerstner_positive_wave_profile.dds",
    ];

    /// <summary>The least correlation, either way round, of a slope with its height's differences for a lookup to be taken.</summary>
    public const double Agreement = 0.8;

    /// <summary>
    /// The beach waves' wave shape - <c>wave_shape_precomp_tex</c> of BreakingWavesVertex and BreakingWavesFragment, as
    /// BreakingOceanWaves.fxgraph binds it (its InputTexture 6).
    /// </summary>
    /// <remarks>
    /// NAMED BY THE GRAPH, NOT TOLD APART BY ITS TEXELS as the swell's are: the graph writes the path into the input that feeds the
    /// node (the capture's water textures part, v0.1.241). Its red is the crest's sideways offset and its green its height
    /// (GetGerstnerPositiveProfile), read at the wave's coordinate and its break phase, held at the edges.
    /// </remarks>
    public const string BeachProfilePath = "Art/2DArt/Lookup/gerstner_positive_wave_profile.dds";

    /// <summary>The beach waves' noise - <c>wave_coord_noise_tex</c>, BreakingOceanWaves.fxgraph's InputTexture 18, R16G16_UNORM.</summary>
    public const string BeachNoisePath = "Art/particles/FXLib/tileables/16bit/flow_muddle_blur_01_R16G16.dds";

    private WaterLookups(
        WaterSheet? ripples, WaterSheet? swells, string rippleSaid, string swellSaid, IReadOnlyList<string> lines,
        WaterSheet? beachProfile, WaterSheet? beachNoise, string beachSaid, WaterSheet? fieldProfile = null)
    {
        FieldProfile = fieldProfile;
        Ripples = ripples;
        Swells = swells;
        RippleSaid = rippleSaid;
        SwellSaid = swellSaid;
        Lines = lines;
        BeachProfile = beachProfile;
        BeachNoise = beachNoise;
        BeachSaid = beachSaid;
    }

    /// <summary>The engine's beach wave shape, four floats a texel, or null where it is not taken.</summary>
    public WaterSheet? BeachProfile { get; }

    /// <summary>The engine's beach wave noise, four floats a texel - its red and green - or null where it is not taken.</summary>
    public WaterSheet? BeachNoise { get; }

    /// <summary>What the beach waves are drawn over: the engine's two lookups, or which stands in and why.</summary>
    public string BeachSaid { get; }

    /// <summary>The field waves' wave shape (ShadeProgram.FieldProfilePath), four floats a texel - read red and blue - or null where it is not taken.</summary>
    public WaterSheet? FieldProfile { get; }

    /// <summary>What the ripples are drawn over: the engine's texture, or the stand-in and why.</summary>
    public string RippleSaid { get; }

    /// <summary>What the swell's cross-section is: the engine's, or the stand-in and why.</summary>
    public string SwellSaid { get; }

    /// <summary>The engine's ripple texture, 128 square, four floats a texel - or null where it is not taken.</summary>
    public WaterSheet? Ripples { get; }

    /// <summary>The engine's swell cross-section: the profile's red, the gradient's at the profile's texels, the profile's green and alpha - four floats a texel - or null.</summary>
    public WaterSheet? Swells { get; }

    /// <summary>One line: what is taken, and what is not and why.</summary>
    public string Summary => RippleSaid + "; " + SwellSaid + "; " + BeachSaid;

    /// <summary>Every file read, its format, its channels and its checks - the capture's lines.</summary>
    public IReadOnlyList<string> Lines { get; }

    /// <summary>Reads the lookups out of an install. Never throws.</summary>
    public static WaterLookups Read(Func<string, byte[]?>? read)
    {
        if (read is null)
        {
            return new WaterLookups(
                null, null, "the ripples over a noise of this tool's - no install", "the swell's cross-section a trochoid of this tool's - no install",
                ["no install to read the engine's water lookups from"], null, null, "the beach waves stand flat - no install");
        }

        var lines = new List<string>();
        WaterSheet? ripples = RipplesOf(Picture(read, RipplePath, lines), lines, out string rippleSaid);
        FloatPicture? gradient = Picture(read, GradientPath, lines);
        var decoded = new Dictionary<string, FloatPicture>(StringComparer.Ordinal);
        WaterSheet? swells = SwellsOf(read, gradient, lines, decoded, out string swellSaid);
        WaterSheet? beachProfile = Whole(
            decoded.TryGetValue(BeachProfilePath, out FloatPicture? profile) ? profile : Picture(read, BeachProfilePath, lines), lines, "beach wave shape");
        WaterSheet? beachNoise = Whole(Picture(read, BeachNoisePath, lines), lines, "beach wave noise");
        string beachSaid = (beachProfile, beachNoise) switch
        {
            ({ }, { }) => "the beach waves over the engine's wave shape and noise (gerstner_positive_wave_profile.dds, flow_muddle_blur_01_R16G16.dds)",
            (null, { }) => "the beach waves stand flat - the engine's wave shape did not read",
            ({ }, null) => "the beach waves over the engine's wave shape, their noise a half - the engine's noise did not read",
            _ => "the beach waves stand flat, their noise a half - neither of the engine's lookups read",
        };
        lines.Add("beach waves: " + beachSaid);

        // THE FIELD WAVES' SHAPE, the profile the program names that neither the swell nor the beach takes - see ShadeProgram.Field.
        WaterSheet? fieldProfile = Whole(
            decoded.TryGetValue(ShadeProgram.FieldProfilePath, out FloatPicture? field) ? field : Picture(read, ShadeProgram.FieldProfilePath, lines),
            lines,
            "field waves' wave shape (gerstner_wave_profile.dds)");
        return new WaterLookups(ripples, swells, rippleSaid, swellSaid, lines, beachProfile, beachNoise, beachSaid, fieldProfile);
    }

    /// <summary>
    /// A lookup the graph binds by path, taken as it reads where every texel is a number - nothing to tell it apart from, so nothing else is checked.
    /// </summary>
    private static WaterSheet? Whole(FloatPicture? picture, List<string> lines, string what)
    {
        if (picture is null)
        {
            return null;
        }

        if (!Finite(picture.Texels))
        {
            lines.Add($"  not taken as the {what}: it holds a number that is none");
            return null;
        }

        lines.Add($"  TAKEN as the {what}");
        return new WaterSheet(picture.Width, picture.Height, 4, picture.Texels, true, what);
    }

    /// <summary>A lookup decoded, its format and channels said - or null, with why.</summary>
    private static FloatPicture? Picture(Func<string, byte[]?> read, string path, List<string> lines)
    {
        byte[]? raw;
        try
        {
            raw = GameArt.ReadRaw(read, path);
        }
        catch (Exception failed) when (failed is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            lines.Add($"{path}: not read - {failed.Message}");
            return null;
        }

        if (raw is null)
        {
            lines.Add($"{path}: not in the install");
            return null;
        }

        FloatPicture? picture = FloatArt.Decode(raw, out string why);
        if (picture is null)
        {
            lines.Add($"{path}: did not decode - {why}");
            return null;
        }

        lines.Add(Say($"{path}: {picture.Width} x {picture.Height}, {picture.Format}"));
        lines.AddRange(Channels(picture));
        return picture;
    }

    /// <summary>The ripple texture, where it is 128 square, finite, and its green and blue go with its red's differences.</summary>
    private static WaterSheet? RipplesOf(FloatPicture? picture, List<string> lines, out string said)
    {
        const string standIn = "the ripples over a noise of this tool's";
        if (picture is null)
        {
            said = standIn + " - the engine's ripple texture did not read";
            return null;
        }

        if (picture.Width != ShadeProgram.RippleSide || picture.Height != ShadeProgram.RippleSide)
        {
            said = Say($"{standIn} - the engine's ripple texture is {picture.Width} x {picture.Height}, not {ShadeProgram.RippleSide} square");
            lines.Add("  not taken: " + said);
            return null;
        }

        if (!Finite(picture.Texels))
        {
            said = standIn + " - the engine's ripple texture holds a number that is none";
            lines.Add("  not taken: " + said);
            return null;
        }

        int side = picture.Width;
        Correlation gx = default, gy = default, bx = default, by = default;
        for (var y = 0; y < side; y++)
        {
            for (var x = 0; x < side; x++)
            {
                float dx = (picture.At((x + 1) % side, y, 0) - picture.At((x + side - 1) % side, y, 0)) * 0.5f;
                float dy = (picture.At(x, (y + 1) % side, 0) - picture.At(x, (y + side - 1) % side, 0)) * 0.5f;
                float green = picture.At(x, y, 1), blue = picture.At(x, y, 2);
                gx.Add(green, dx);
                gy.Add(green, dy);
                bx.Add(blue, dx);
                by.Add(blue, dy);
            }
        }

        lines.Add(Say($"  green against the red's difference a texel along x: correlation {gx.Value:0.###}, green {gx.Ratio:0.###} of it; along y {gy.Value:0.###}"));
        lines.Add(Say($"  blue against the red's difference a texel along y: correlation {by.Value:0.###}, blue {by.Ratio:0.###} of it; along x {bx.Value:0.###}"));
        if (Math.Abs(gx.Value) < Agreement || Math.Abs(by.Value) < Agreement)
        {
            said = Say($"{standIn} - the engine's ripple texture's green and blue do not go with its red's slope ({gx.Value:0.##}, {by.Value:0.##})");
            lines.Add("  not taken: " + said);
            return null;
        }

        said = "the ripples over the engine's ripple texture (surface_ripple.dds)";
        lines.Add("  TAKEN as the ripple texture");
        return new WaterSheet(side, side, 4, picture.Texels, true, said);
    }

    /// <summary>The swell's cross-section: the profile whose red's differences along its width go with the gradient's red, and that gradient.</summary>
    private static WaterSheet? SwellsOf(
        Func<string, byte[]?> read, FloatPicture? gradient, List<string> lines, Dictionary<string, FloatPicture> decoded, out string said)
    {
        const string standIn = "the swell's cross-section a trochoid of this tool's";
        if (gradient is null || !Finite(gradient.Texels))
        {
            said = standIn + " - the engine's cross-section gradient did not read, and nothing else tells its profile apart";
            return null;
        }

        FloatPicture? best = null;
        string bestPath = string.Empty;
        double bestValue = 0;
        foreach (string path in ProfilePaths)
        {
            FloatPicture? profile = Picture(read, path, lines);
            if (profile is null)
            {
                continue;
            }

            decoded[path] = profile;

            if (!Finite(profile.Texels))
            {
                lines.Add("  holds a number that is none - not held against the gradient");
                continue;
            }

            Correlation along = default, across = default;
            int width = profile.Width, height = profile.Height;
            for (var y = 0; y < height; y++)
            {
                for (var x = 0; x < width; x++)
                {
                    // The differences a unit of the phase and of the shore's distance - the gradient's own units.
                    float du = (profile.At((x + 1) % width, y, 0) - profile.At((x + width - 1) % width, y, 0)) * width * 0.5f;
                    int below = Math.Max(y - 1, 0), above = Math.Min(y + 1, height - 1);
                    float dv = above == below ? 0f
                        : (profile.At(x, above, 0) - profile.At(x, below, 0)) * height / (above - below);
                    float u = (x + 0.5f) / width, v = (y + 0.5f) / height;
                    along.Add(Sampled(gradient, u, v, 0), du);
                    across.Add(Sampled(gradient, u, v, 1), dv);
                }
            }

            lines.Add(Say($"  against {Path.GetFileName(GradientPath)}: its red with this red's difference a unit of the phase {along.Value:0.###} (the gradient {along.Ratio:0.###} of it), its green with the difference a unit of the shore's distance {across.Value:0.###}"));
            if (Math.Abs(along.Value) >= Agreement && Math.Abs(along.Value) > bestValue)
            {
                best = profile;
                bestPath = path;
                bestValue = Math.Abs(along.Value);
            }
        }

        if (best is null)
        {
            said = standIn + " - no profile of the engine's goes with its cross-section gradient";
            lines.Add("  not taken: " + said);
            return null;
        }

        // THE PROFILE'S RED, THE GRADIENT'S RED AT ITS TEXELS, AND ITS GREEN AND ALPHA - the trail foam's strength and the front's
        // phase (GetTidalWaterPlanarState). Its blue, the orbit carrying the ripples to and fro, is not drawn.
        var texels = new float[best.Width * best.Height * 4];
        for (var y = 0; y < best.Height; y++)
        {
            for (var x = 0; x < best.Width; x++)
            {
                int at = ((y * best.Width) + x) * 4;
                texels[at] = best.At(x, y, 0);
                texels[at + 1] = Sampled(gradient, (x + 0.5f) / best.Width, (y + 0.5f) / best.Height, 0);
                texels[at + 2] = best.At(x, y, 1);
                texels[at + 3] = best.At(x, y, 3);
            }
        }

        string name = Path.GetFileName(bestPath);
        said = $"the swell's cross-section the engine's ({name}, its rise wave_profile_gradient.dds"
               + (best.Width == gradient.Width && best.Height == gradient.Height ? ")" : " read at its texels)");
        lines.Add($"TAKEN as the swell's cross-section: {name} with {Path.GetFileName(GradientPath)}");
        return new WaterSheet(best.Width, best.Height, 4, texels, true, said);
    }

    /// <summary>One channel at a place, wrapping along u and held along v, the four nearest texels' centres mixed - SamplerLinearWrapClampWrap's read.</summary>
    private static float Sampled(FloatPicture picture, float u, float v, int channel)
    {
        int width = picture.Width, height = picture.Height;
        float sx = (u * width) - 0.5f;
        float sy = Math.Clamp((v * height) - 0.5f, 0f, height - 1);
        float fx0 = MathF.Floor(sx);
        float fx = sx - fx0;
        int x0 = (((int)fx0 % width) + width) % width, x1 = (x0 + 1) % width;
        int y0 = (int)sy, y1 = Math.Min(y0 + 1, height - 1);
        float fy = sy - y0;
        float top = picture.At(x0, y0, channel) + ((picture.At(x1, y0, channel) - picture.At(x0, y0, channel)) * fx);
        float bottom = picture.At(x0, y1, channel) + ((picture.At(x1, y1, channel) - picture.At(x0, y1, channel)) * fx);
        return top + ((bottom - top) * fy);
    }

    private static bool Finite(float[] texels)
    {
        foreach (float one in texels)
        {
            if (!float.IsFinite(one))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Each channel's lowest, tenth, middle, ninetieth and highest value, and its mean - a line a channel.</summary>
    public static IEnumerable<string> Channels(FloatPicture picture)
    {
        int count = picture.Width * picture.Height;
        var values = new float[count];
        for (var channel = 0; channel < 4; channel++)
        {
            double sum = 0;
            for (var at = 0; at < count; at++)
            {
                values[at] = picture.Texels[(at * 4) + channel];
                sum += values[at];
            }

            Array.Sort(values);
            yield return Say(
                $"  {"rgba"[channel]}: {values[0]:0.####} {values[count / 10]:0.####} {values[count / 2]:0.####} {values[(count * 9) / 10]:0.####} {values[count - 1]:0.####} · mean {sum / count:0.####}");
        }
    }

    private static string Say(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    /// <summary>Pearson's correlation of two series, and the first over the second by least squares.</summary>
    private struct Correlation
    {
        private double _n, _a, _b, _aa, _bb, _ab;

        public void Add(double a, double b)
        {
            _n++;
            _a += a;
            _b += b;
            _aa += a * a;
            _bb += b * b;
            _ab += a * b;
        }

        public readonly double Value
        {
            get
            {
                double cov = (_n * _ab) - (_a * _b);
                double spread = Math.Sqrt(((_n * _aa) - (_a * _a)) * ((_n * _bb) - (_b * _b)));
                return spread > 0 ? cov / spread : 0;
            }
        }

        public readonly double Ratio => _bb > 0 ? _ab / _bb : 0;
    }

    /// <summary>The lines as one text.</summary>
    public override string ToString()
    {
        var text = new StringBuilder();
        text.AppendLine(Summary);
        foreach (string line in Lines)
        {
            text.AppendLine(line);
        }

        return text.ToString();
    }
}
