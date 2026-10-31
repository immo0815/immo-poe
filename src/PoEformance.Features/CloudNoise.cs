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

    /// <summary>
    /// Every candidate held against the points: for each, the way and the slide its cloud factor goes best with the game's light at, how
    /// well, and how far that stands over its other slides - then the one taken, or that none stands clear.
    /// </summary>
    /// <param name="clouds">The area's clouds.</param>
    /// <param name="sun">The way the sun's light travels - its sign does not matter (see the remarks).</param>
    /// <param name="player">Where the player stood, area x and y.</param>
    /// <param name="points">The ground the pane lights, each with the game's light over the pane's.</param>
    /// <param name="candidates">Each candidate's path and its picture, or null where it did not read.</param>
    public static string Measure(AreaClouds clouds, Vector3 sun, Vector2 player, IReadOnlyList<Point> points, IReadOnlyList<(string Path, GamePicture? Picture)> candidates)
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
        Vector2 up = new Vector2(sun.X, sun.Y) / sun.Z * AreaClouds.Height;
        for (var at = 0; at < count; at++)
        {
            Point one = points[at * step];
            reference[at] = one.Area - up;
            fade[at] = AreaClouds.Fade(Vector2.Distance(player, one.Area), clouds.FadeRadius);
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

            float[] alpha = AreaClouds.AlphaOf(picture);
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
                        float c = clouds.Factor(AreaClouds.Sample(alpha, picture.Width, picture.Height, u[at] + shift, v[at]), fade[at]);
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

    private static float Correlation(int n, double sumX, double sumXX, double sumY, double sumYY, double sumXY)
    {
        double cov = sumXY - (sumX * sumY / n);
        double varX = sumXX - (sumX * sumX / n), varY = sumYY - (sumY * sumY / n);
        return varX <= 1e-12 || varY <= 1e-12 ? 0f : (float)(cov / Math.Sqrt(varX * varY));
    }

}
