using System.Globalization;
using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// An area's clouds as its .env sets them, and shadows.ffx's GetCloudsIntensity over them: the factor the clouds' shadow lays on the sun's
/// light at a place. The card draws it (ModelShaders' Clouded, SceneLight.Clouds); this is its reference and the capture's measurement's.
/// </summary>
/// <remarks>
/// GetCloudsIntensity AS WRITTEN (shadows.ffx's shadow_map_sampling, whole in light-extras.txt): the point 1000 units up along the light -
/// <c>position - light_dir / light_dir.z * 1000</c>, the same whichever way the light's axis points - its x and y along and across
/// <c>clouds_velocity</c> (x where the clouds move slower than 0.1), slid along x by <c>|velocity| * time</c>, times
/// <c>0.001 * clouds_scale</c>; the noise's ALPHA read there at level nought, wrapping and linear; then
/// <c>saturate(alpha + fade * pre_fade)</c>, math_utils' SmoothStep about the midpoint with the sharpness, <c>+ fade * post_fade</c>,
/// saturated and lerped from one by the intensity - fade HLSL's smoothstep of one less the distance to the player over
/// <c>clouds_fade_radius</c>. The engine multiplies the directional light's shadow by it, on a surface (ShadowMap) and through the air
/// (IntegrateShadowMap, at the layer's first point).
/// TWO THINGS ARE NOT READ: the noise - <c>noise_sampler</c>, a pass texture no file names - and how the engine makes clouds_velocity
/// from the .env's speed and angle, its C++. CHOSEN from the live client (2026-10-31) on the best of the candidates held against The Red
/// Vale's picture (v0.1.381's light-against-game.bin, measured offline by CloudNoise over every way and slide): <see cref="NoisePath"/>'s
/// alpha - r 0.48 against flowmap_04's 0.35 over all the lit ground, 0.39 against 0.33 beyond 900 units of the player, 0.34 against 0.31
/// beyond 1200, none of it clear by CloudNoise's own rule - and the velocity <c>speed (cos a, sin a)</c>, the way muddle_double went best
/// with at every distance. MEASURED by a second capture in The Red Vale (09:12, another spot, the processor's picture drawn without clouds
/// so the game's own cloud shadow stands in its light over the pane's): muddle_double r 0.531 by <c>speed (cos a, sin a)</c>, 2.5 spreads
/// over its other slides, flowmap_04 next at 0.378 - clear by CloudNoise's own rule. One area's evidence; another area's capture is to
/// confirm it.
/// </remarks>
/// <param name="Scale">clouds_scale.</param>
/// <param name="Intensity">clouds_intensity.</param>
/// <param name="Midpoint">clouds_midpoint.</param>
/// <param name="Sharpness">clouds_sharpness.</param>
/// <param name="Speed">clouds_speed.</param>
/// <param name="Angle">clouds_angle, radians.</param>
/// <param name="FadeRadius">clouds_fade_radius - <see cref="DefaultFadeRadius"/> where the file sets none.</param>
/// <param name="PreFade">clouds_pre_fade.</param>
/// <param name="PostFade">clouds_post_fade.</param>
public sealed record AreaClouds(
    float Scale, float Intensity, float Midpoint, float Sharpness, float Speed, float Angle, float FadeRadius, float PreFade, float PostFade)
{
    /// <summary>The engine's own clouds' height: GetCloudsIntensity's <c>clouds_height</c>, a constant in shadows.ffx.</summary>
    public const float Height = 1000f;

    /// <summary>
    /// The fade radius where an environment sets none: 800 - MEASURED in The Red Vale's parsed environment (light-hunt.txt, v0.1.372), whose
    /// file sets every other key of its clouds.
    /// </summary>
    public const float DefaultFadeRadius = 800f;

    /// <summary>The noise the clouds read - MEASURED in The Red Vale, see the remarks: the engine's distortion texture, which the caustics and the river read too.</summary>
    public const string NoisePath = ShadeProgram.DistortionPath;

    /// <summary>
    /// Below this the sun's light is taken to run along the ground and the clouds lay nothing (CHOSEN: the engine divides by the light's z
    /// unguarded, and its sun never stands at the horizon; a free sun here may).
    /// </summary>
    public const float LeastRise = 1e-3f;

    /// <summary>clouds_velocity: <c>speed (cos a, sin a)</c> - MEASURED in The Red Vale, see the remarks.</summary>
    public Vector2 Velocity => new Vector2(MathF.Cos(Angle), MathF.Sin(Angle)) * Speed;

    /// <summary>
    /// The noise's x axis - clouds_velocity's way, x where the clouds move slower than 0.1 - and how fast it slides along it, the velocity's
    /// length: GetCloudsIntensity's <c>x_vec</c> and <c>cloud_speed</c>.
    /// </summary>
    public (Vector2 Along, float Speed) Drift
    {
        get
        {
            Vector2 velocity = Velocity;
            float speed = velocity.Length();
            return (speed > 0.1f ? velocity / speed : Vector2.UnitX, speed);
        }
    }

    /// <summary>The clouds an environment sets, or null and why where it leaves out a key whose default is not measured.</summary>
    public static AreaClouds? Of(EnvironmentSettings environment, out string why)
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
        return new AreaClouds(
            values["clouds_scale"], values["clouds_intensity"], values["clouds_midpoint"], values["clouds_sharpness"], values["clouds_speed"],
            values["clouds_angle"], values.GetValueOrDefault("clouds_fade_radius", DefaultFadeRadius), values["clouds_pre_fade"], values["clouds_post_fade"]);
    }

    /// <summary>GetCloudsIntensity's factor on the sun for a noise alpha and the player's fade there.</summary>
    public float Factor(float alpha, float fade)
    {
        float faded = Saturate(alpha + (fade * PreFade));
        float shaped = Saturate(MathSmoothStep(faded, Midpoint, Sharpness) + (fade * PostFade));
        return 1f + ((shaped - 1f) * Intensity);
    }

    /// <summary>The player's fade at a distance: HLSL's smoothstep(0, 1, saturate(1 - distance / radius)).</summary>
    public static float Fade(float distance, float radius)
    {
        float t = Saturate(1f - (distance / MathF.Max(1e-4f, radius)));
        return t * t * (3f - (2f * t));
    }

    /// <summary>
    /// Where the noise is read for a place of the area: GetCloudsIntensity's texture coordinates, the light's way given as the sun's light
    /// travels (its sign does not matter); null where the sun stands at the horizon (<see cref="LeastRise"/>).
    /// </summary>
    /// <param name="area">The place, area x and y.</param>
    /// <param name="travel">The way the sun's light travels.</param>
    /// <param name="time">The engine's <c>time</c> - the picture's clock.</param>
    public Vector2? NoiseAt(Vector2 area, Vector3 travel, float time)
    {
        if (MathF.Abs(travel.Z) < LeastRise)
        {
            return null;
        }

        Vector2 reference = area - (new Vector2(travel.X, travel.Y) / travel.Z * Height);
        (Vector2 x, float speed) = Drift;
        var y = new Vector2(-x.Y, x.X);
        float frequency = 0.001f * Scale;
        return new Vector2((Vector2.Dot(x, reference) + (speed * time)) * frequency, Vector2.Dot(y, reference) * frequency);
    }

    /// <summary>
    /// GetCloudsIntensity at a place of the area: the noise's alpha read at <see cref="NoiseAt"/> as the card reads it (<see cref="Sample"/>),
    /// faded by the distance to the player. One where the sun stands at the horizon.
    /// </summary>
    /// <param name="area">The place, area x and y.</param>
    /// <param name="travel">The way the sun's light travels.</param>
    /// <param name="player">Where the player stands, area x and y - on a stage the camera's focus.</param>
    /// <param name="time">The picture's clock.</param>
    /// <param name="alpha">The noise's alpha, row by row, as fractions.</param>
    /// <param name="width">The noise's width.</param>
    /// <param name="height">The noise's height.</param>
    public float At(Vector2 area, Vector3 travel, Vector2 player, float time, float[] alpha, int width, int height)
    {
        ArgumentNullException.ThrowIfNull(alpha);
        return NoiseAt(area, travel, time) is not { } at
            ? 1f
            : Factor(Sample(alpha, width, height, at.X, at.Y), Fade(Vector2.Distance(player, area), FadeRadius));
    }

    /// <summary>A picture's alpha as fractions, for <see cref="At"/>.</summary>
    public static float[] AlphaOf(GamePicture picture)
    {
        var alpha = new float[picture.Width * picture.Height];
        for (var at = 0; at < alpha.Length; at++)
        {
            alpha[at] = picture.Rgba[(at * 4) + 3] / 255f;
        }

        return alpha;
    }

    /// <summary>
    /// math_utils' SmoothStep(val, center, curve) - the DECLARATIONS block shadow_map_sampling includes, saturated outside. (util.hlsl, the
    /// engine's .hlsl files' own, holds the value a hair inside nought and one first instead and leaves the sum unsaturated; for a value in
    /// nought to one the two agree to the float.)
    /// </summary>
    public static float MathSmoothStep(float value, float center, float curve)
    {
        float power = (2f / MathF.Max(1e-3f, 1f - curve)) - 1f;
        return Saturate(1f - center
            + (-MathF.Pow(Saturate(1f - MathF.Max(center, value)), power) * MathF.Pow(Saturate(1f - center), -(power - 1f)))
            + (MathF.Pow(Saturate(MathF.Min(center, value)), power) * MathF.Pow(Saturate(center), -(power - 1f))));
    }

    /// <summary>A wrapping linear read at level nought, texel centres at halves - SamplerLinearWrapNoBias's, and the card's WrappedRead.</summary>
    public static float Sample(float[] texels, int width, int height, float u, float v)
    {
        ArgumentNullException.ThrowIfNull(texels);
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

    private static float Saturate(float value) => Math.Clamp(value, 0f, 1f);
}
