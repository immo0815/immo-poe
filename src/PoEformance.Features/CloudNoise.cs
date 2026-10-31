using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;

namespace PoEformance.Features;

/// <summary>
/// Which of the engine's noise candidates is its <c>noise_sampler</c>, held against the game's picture: the clouds' shadow each candidate
/// would throw on the ground the pane lights - shadows.ffx's GetCloudsIntensity as written - set beside how much darker the game draws it.
/// </summary>
/// <remarks>
/// ASKED FOR FROM THE LIVE CLIENT (v0.1.380): measure the noise on the game's picture rather than assume one. GetCloudsIntensity reads the
/// noise's ALPHA at the point 1000 units up along the light - <c>(xy - light.xy / light.z * 1000)</c>, the same whichever way the light's
/// axis points - turned so its x runs along <c>clouds_velocity</c>, times <c>0.001 * clouds_scale</c>, slid along x by
/// <c>speed * time</c>; then <c>saturate(alpha + fade * pre_fade)</c>, math_utils' SmoothStep about the midpoint with the sharpness,
/// <c>+ fade * post_fade</c>, saturated, and lerped from one by the intensity - fade the smoothstep of one less the distance to the player
/// over <c>clouds_fade_radius</c>. TWO THINGS ARE NOT READ AND ARE SEARCHED: how the engine makes clouds_velocity from the .env's speed
/// and angle (its C++ - eight ways are tried, every sign and order of the angle's cosine and sine), and the engine's <c>time</c> at the
/// press (every slide along x across one period of the texture, half a texel apart). For each candidate and way the correlation of the
/// cloud factor with the game's light over the pane's, over the ground the pane puts in its sun; a candidate is the noise where its best
/// stands clear of every other's (CHOSEN: r 0.25 at least, and 0.1 over the next).
/// </remarks>
public static class CloudNoise
{
    /// <summary>The engine's own clouds' height: GetCloudsIntensity's <c>clouds_height</c>, a constant in shadows.ffx.</summary>
    public const float Height = 1000f;

    /// <summary>
    /// The fade radius where an environment sets none: 800 - MEASURED in The Red Vale's parsed environment (light-hunt.txt, v0.1.372), whose
    /// file sets every other key of its clouds.
    /// </summary>
    public const float DefaultFadeRadius = 800f;

    /// <summary>The most ground points the measurement holds - every core runs every slide of every way over each.</summary>
    public const int MostPoints = 12_000;

    /// <summary>The fewest ground points a measurement is made on.</summary>
    public const int FewestPoints = 400;

    /// <summary>
    /// How far the winner's best stands over the next candidate's, and the least it must reach - CHOSEN. Not its slides' spread: a smooth
    /// noise's correlation rises and falls smoothly with the slide, so even the true texture's best stands only some three spreads over its
    /// slides' mean (the made-up test's 2.7 at r 0.997).
    /// </summary>
    private const float ClearOver = 0.1f;

    private const float ClearLeast = 0.25f;

    /// <summary>An area's clouds, as its .env sets them.</summary>
    /// <param name="Scale">clouds_scale.</param>
    /// <param name="Intensity">clouds_intensity.</param>
    /// <param name="Midpoint">clouds_midpoint.</param>
    /// <param name="Sharpness">clouds_sharpness.</param>
    /// <param name="Speed">clouds_speed.</param>
    /// <param name="Angle">clouds_angle, radians.</param>
    /// <param name="FadeRadius">clouds_fade_radius - <see cref="DefaultFadeRadius"/> where the file sets none.</param>
    /// <param name="PreFade">clouds_pre_fade.</param>
    /// <param name="PostFade">clouds_post_fade.</param>
    public sealed record Clouds(
        float Scale, float Intensity, float Midpoint, float Sharpness, float Speed, float Angle, float FadeRadius, float PreFade, float PostFade);

    /// <summary>One point of the ground the pane lights: where it stands in the area, and the game's light there over the pane's.</summary>
    public readonly record struct Point(Vector2 Area, float Game);

    /// <summary>The ways clouds_velocity may be made from the .env's angle - the engine's C++, not read.</summary>
    private static readonly (string Name, Func<float, Vector2> Way)[] Ways =
    [
        ("(cos a, sin a)", a => new Vector2(MathF.Cos(a), MathF.Sin(a))),
        ("(cos a, -sin a)", a => new Vector2(MathF.Cos(a), -MathF.Sin(a))),
        ("(-cos a, sin a)", a => new Vector2(-MathF.Cos(a), MathF.Sin(a))),
        ("(-cos a, -sin a)", a => new Vector2(-MathF.Cos(a), -MathF.Sin(a))),
        ("(sin a, cos a)", a => new Vector2(MathF.Sin(a), MathF.Cos(a))),
        ("(sin a, -cos a)", a => new Vector2(MathF.Sin(a), -MathF.Cos(a))),
        ("(-sin a, cos a)", a => new Vector2(-MathF.Sin(a), MathF.Cos(a))),
        ("(-sin a, -cos a)", a => new Vector2(-MathF.Sin(a), -MathF.Cos(a))),
    ];

    /// <summary>The clouds an environment sets, or null and why where it leaves out a key whose default is not measured.</summary>
    public static Clouds? Of(EnvironmentSettings environment, out string why)
    {
        ArgumentNullException.ThrowIfNull(environment);
        var values = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        foreach (EnvironmentValue value in environment.All)
        {
            if (value.Key.StartsWith("clouds.", StringComparison.OrdinalIgnoreCase) && value.Values.Count > 0)
            {
                values[value.Key["clouds.".Length..]] = value.Values[0];
            }
        }

        string[] needed = ["clouds_scale", "clouds_intensity", "clouds_midpoint", "clouds_sharpness", "clouds_speed", "clouds_angle", "clouds_pre_fade", "clouds_post_fade"];
        string[] missing = [.. needed.Where(key => !values.ContainsKey(key))];
        if (missing.Length > 0)
        {
            why = "the area's .env leaves out " + string.Join(", ", missing) + " - their defaults are not measured";
            return null;
        }

        why = values.ContainsKey("clouds_fade_radius") ? string.Empty
            : string.Create(CultureInfo.InvariantCulture, $"clouds_fade_radius not set - the engine's {DefaultFadeRadius:0} (measured)");
        return new Clouds(
            values["clouds_scale"], values["clouds_intensity"], values["clouds_midpoint"], values["clouds_sharpness"], values["clouds_speed"],
            values["clouds_angle"], values.GetValueOrDefault("clouds_fade_radius", DefaultFadeRadius), values["clouds_pre_fade"], values["clouds_post_fade"]);
    }

    /// <summary>GetCloudsIntensity's factor on the sun for a noise alpha and the player's fade there.</summary>
    public static float Factor(Clouds clouds, float alpha, float fade)
    {
        ArgumentNullException.ThrowIfNull(clouds);
        float faded = Math.Clamp(alpha + (fade * clouds.PreFade), 0f, 1f);
        float shaped = Math.Clamp(MathSmoothStep(faded, clouds.Midpoint, clouds.Sharpness) + (fade * clouds.PostFade), 0f, 1f);
        return 1f + ((shaped - 1f) * clouds.Intensity);
    }

    /// <summary>The player's fade at a distance: HLSL's smoothstep(0, 1, saturate(1 - distance / radius)).</summary>
    public static float Fade(float distance, float radius)
    {
        float t = Math.Clamp(1f - (distance / MathF.Max(1e-4f, radius)), 0f, 1f);
        return t * t * (3f - (2f * t));
    }

    /// <summary>
    /// math_utils.ffx's SmoothStep(val, center, curve), as the engine writes it - the value held a hair inside nought and one first (the
    /// spray's copy, ShadeProgram.MathSmoothStep, leaves that to its callers).
    /// </summary>
    public static float MathSmoothStep(float value, float center, float curve)
    {
        const float eps = 1e-7f;
        value = MathF.Max(MathF.Min(value, 1f - eps), eps);
        float power = (2f / MathF.Max(1e-3f, 1f - curve)) - 1f;
        return 1f - center
            + (-MathF.Pow(Saturate(1f - MathF.Max(center, value)), power) * MathF.Pow(Saturate(1f - center), -(power - 1f)))
            + (MathF.Pow(Saturate(MathF.Min(center, value)), power) * MathF.Pow(Saturate(center), -(power - 1f)));
    }

    /// <summary>
    /// Every candidate held against the points: for each, the way and the slide its cloud factor goes best with the game's light at, how
    /// well, and how far that stands over its other slides - then the one taken, or that none stands clear.
    /// </summary>
    /// <param name="clouds">The area's clouds.</param>
    /// <param name="sun">The way the sun's light travels - its sign does not matter (see the remarks).</param>
    /// <param name="player">Where the player stood, area x and y.</param>
    /// <param name="points">The ground the pane lights, each with the game's light over the pane's.</param>
    /// <param name="candidates">Each candidate's path and its picture, or null where it did not read.</param>
    public static string Measure(Clouds clouds, Vector3 sun, Vector2 player, IReadOnlyList<Point> points, IReadOnlyList<(string Path, GamePicture? Picture)> candidates)
    {
        ArgumentNullException.ThrowIfNull(clouds);
        ArgumentNullException.ThrowIfNull(points);
        ArgumentNullException.ThrowIfNull(candidates);
        var said = new StringBuilder();
        said.Append(CultureInfo.InvariantCulture,
            $"clouds: scale {clouds.Scale:0.###}, intensity {clouds.Intensity:0.###}, midpoint {clouds.Midpoint:0.###}, sharpness {clouds.Sharpness:0.###}, speed {clouds.Speed:0.###}, angle {clouds.Angle:0.####}, fade radius {clouds.FadeRadius:0.#}, pre {clouds.PreFade:0.###}, post {clouds.PostFade:0.###}; the sun travelling ({sun.X:0.####}, {sun.Y:0.####}, {sun.Z:0.####}); the player at ({player.X:0.#}, {player.Y:0.#})").AppendLine();
        if (MathF.Abs(sun.Z) < 1e-3f)
        {
            return said.AppendLine("no measurement: the sun stands at the horizon").ToString();
        }

        if (points.Count < FewestPoints)
        {
            return said.Append(CultureInfo.InvariantCulture, $"no measurement: {points.Count} points of lit ground, fewer than {FewestPoints}").AppendLine().ToString();
        }

        // THE POINTS EVENLY TAKEN to the most held, each moved up the light to the clouds and its fade worked out once.
        int step = Math.Max(1, (points.Count + MostPoints - 1) / MostPoints);
        int count = (points.Count + step - 1) / step;
        var reference = new Vector2[count];
        var fade = new float[count];
        var game = new float[count];
        Vector2 up = new Vector2(sun.X, sun.Y) / sun.Z * Height;
        for (var at = 0; at < count; at++)
        {
            Point one = points[at * step];
            reference[at] = one.Area - up;
            fade[at] = Fade(Vector2.Distance(player, one.Area), clouds.FadeRadius);
            game[at] = one.Game;
        }

        double sumY = 0, sumYY = 0;
        foreach (float y in game)
        {
            sumY += y;
            sumYY += (double)y * y;
        }

        float frequency = 0.001f * clouds.Scale;
        said.Append(CultureInfo.InvariantCulture, $"{count:N0} points of lit ground held (every {step}. of {points.Count:N0}); each candidate's alpha read as a linear wrapping sampler reads it, at level nought").AppendLine();
        var results = new List<(string Path, string Way, float Best, float Slide, float Mean, float Spread, int Slides)>();
        foreach ((string path, GamePicture? read) in candidates)
        {
            if (read is not { Width: > 0, Height: > 0 } picture)
            {
                said.Append("  candidate\t").Append(path).AppendLine("\tdid not read");
                continue;
            }

            float[] alpha = Alpha(picture);
            int slides = picture.Width * 2;
            (string Way, float Best, float Slide, float Mean, float Spread) best = (string.Empty, float.NegativeInfinity, 0f, 0f, 0f);
            foreach ((string name, Func<float, Vector2> wayOf) in Ways)
            {
                Vector2 velocity = wayOf(clouds.Angle) * clouds.Speed;
                Vector2 x = clouds.Speed > 0.1f ? Vector2.Normalize(velocity) : Vector2.UnitX;
                var y = new Vector2(-x.Y, x.X);
                var u = new float[count];
                var v = new float[count];
                for (var at = 0; at < count; at++)
                {
                    u[at] = Vector2.Dot(x, reference[at]) * frequency;
                    v[at] = Vector2.Dot(y, reference[at]) * frequency;
                }

                var r = new float[slides];
                Parallel.For(0, slides, slide =>
                {
                    float shift = slide / (float)slides;
                    double sumC = 0, sumCC = 0, sumCY = 0;
                    for (var at = 0; at < count; at++)
                    {
                        float c = Factor(clouds, Sample(alpha, picture.Width, picture.Height, u[at] + shift, v[at]), fade[at]);
                        sumC += c;
                        sumCC += (double)c * c;
                        sumCY += (double)c * game[at];
                    }

                    r[slide] = Correlation(count, sumC, sumCC, sumY, sumYY, sumCY);
                });

                int top = 0;
                double mean = 0;
                for (var slide = 0; slide < slides; slide++)
                {
                    top = r[slide] > r[top] ? slide : top;
                    mean += r[slide];
                }

                mean /= slides;
                double spread = Math.Sqrt(r.Sum(one => (one - mean) * (one - mean)) / slides);
                if (r[top] > best.Best)
                {
                    best = (name, r[top], top / (float)slides, (float)mean, (float)spread);
                }
            }

            results.Add((path, best.Way, best.Best, best.Slide, best.Mean, best.Spread, slides));
            said.Append(CultureInfo.InvariantCulture,
                $"  candidate\t{path}\t{picture.Width}x{picture.Height}\tbest r {best.Best:0.000} by clouds_velocity = speed {best.Way}, slid {best.Slide:0.0000} of the texture ({best.Slide / frequency:0} units) along it; its slides' mean {best.Mean:0.000}, spread {best.Spread:0.000}, so the best {(best.Spread > 0f ? (best.Best - best.Mean) / best.Spread : 0f):0.0} spreads over").AppendLine();
        }

        if (results.Count == 0)
        {
            return said.AppendLine("no candidate read").ToString();
        }

        (string Path, string Way, float Best, float Slide, float Mean, float Spread, int Slides)[] ranked = [.. results.OrderByDescending(one => one.Best)];
        var winner = ranked[0];
        float next = ranked.Length > 1 ? ranked[1].Best : float.NegativeInfinity;
        float spreads = winner.Spread > 0f ? (winner.Best - winner.Mean) / winner.Spread : 0f;
        bool clear = winner.Best - next >= ClearOver && winner.Best >= ClearLeast;
        said.Append(CultureInfo.InvariantCulture,
            $"taken: {(clear ? winner.Path + " - clear" : "none clear")} (the best {winner.Path} r {winner.Best:0.000}, {spreads:0.0} spreads over its slides; the next {(ranked.Length > 1 ? $"{ranked[1].Path} r {next:0.000}" : "none")}; clear at r {ClearLeast:0.00} and {ClearOver:0.0} over the next, CHOSEN)").AppendLine();
        return said.ToString();
    }

    /// <summary>A picture's alpha as fractions.</summary>
    private static float[] Alpha(GamePicture picture)
    {
        var alpha = new float[picture.Width * picture.Height];
        for (var at = 0; at < alpha.Length; at++)
        {
            alpha[at] = picture.Rgba[(at * 4) + 3] / 255f;
        }

        return alpha;
    }

    /// <summary>A wrapping linear read at level nought, texel centres at halves - SamplerLinearWrapNoBias's.</summary>
    internal static float Sample(float[] texels, int width, int height, float u, float v)
    {
        float x = (u * width) - 0.5f, y = (v * height) - 0.5f;
        float fx = MathF.Floor(x), fy = MathF.Floor(y);
        float tx = x - fx, ty = y - fy;
        int x0 = Wrap((int)fx, width), y0 = Wrap((int)fy, height);
        int x1 = x0 + 1 == width ? 0 : x0 + 1, y1 = y0 + 1 == height ? 0 : y0 + 1;
        float top = texels[(y0 * width) + x0] + ((texels[(y0 * width) + x1] - texels[(y0 * width) + x0]) * tx);
        float bottom = texels[(y1 * width) + x0] + ((texels[(y1 * width) + x1] - texels[(y1 * width) + x0]) * tx);
        return top + ((bottom - top) * ty);
    }

    private static int Wrap(int at, int size)
    {
        int wrapped = at % size;
        return wrapped < 0 ? wrapped + size : wrapped;
    }

    private static float Correlation(int n, double sumX, double sumXX, double sumY, double sumYY, double sumXY)
    {
        double cov = sumXY - (sumX * sumY / n);
        double varX = sumXX - (sumX * sumX / n), varY = sumYY - (sumY * sumY / n);
        return varX <= 1e-12 || varY <= 1e-12 ? 0f : (float)(cov / Math.Sqrt(varX * varY));
    }

    private static float Saturate(float value) => Math.Clamp(value, 0f, 1f);
}
