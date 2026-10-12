using System.Globalization;
using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// The open water an area is drawn with: its colour, how thick it is, and how it moves - what the engine's water pass reads.
/// </summary>
/// <param name="OpenColour">water.open_water_color, as the file writes it - in gamma, the engine raising it to 2.2 (water.ffx).</param>
/// <param name="Murkiness">water.open_water_murkiness: how much of the light a unit of water takes - the density.</param>
/// <param name="FlowIntensity">water.flow_intensity: how fast the surface's pattern moves, a factor.</param>
/// <param name="HeightOffset">
/// water.height_offset: how far the surface is moved off its planes, in world units along z (up is minus) - by the beach, crashing and
/// flood waves alone (water.ffx's <c>water_height_offset</c>: BreakingWavesVertex and its kin), never an ocean plane, so it is carried and not drawn.
/// </param>
/// <param name="Said">Where each value came from, for the line under a picture.</param>
/// <param name="Reflectiveness">water.reflectiveness: the surface's specular colour, the share of light it reflects head on - InitWaterSurfaceData's <c>specular_color</c>.</param>
/// <param name="WindAngle">water.wind_direction_angle, radians: which way the ocean's ripples are blown - <c>water_wind_direction</c>, taken as (cos, sin).</param>
/// <param name="WindIntensity">water.wind_intensity: how high the ocean's ripples stand - <c>water_wind_intensity</c>.</param>
/// <param name="WindSpeed">water.wind_speed: how fast they roll - <c>water_wind_speed</c>.</param>
/// <param name="Directionness">water.directionness: how much they line up with the wind, nought to one - <c>water_directionness</c>.</param>
/// <remarks>
/// AN AREA'S FILE OR THE ENGINE'S OWN DEFAULT, per value. Most areas set no water colour - Port sets
/// only its flow and a height offset - and what the engine draws them with is written in no file: it
/// was read out of the game's parsed copy of the environment, where every value the file leaves unset
/// stands as the engine's default with a flag of nought (ParsedEnvironment in the offset schema). The
/// colour's slot was named by Clearfell Encampment's file, the murkiness's by The Red Vale's.
///
/// THE MURKINESS IS THE DENSITY: water.ffx takes the medium's density from <c>water_color_open.a</c> and
/// lights the medium with <c>water_color_open.rgb</c>, the file's two keys beside each other are
/// <c>open_water_color</c> and <c>open_water_murkiness</c>, and the shaders' own water materials say so
/// in their defaults - legacy.ffx declares <c>medium_color</c>, <c>water_uv_color_open</c> and
/// <c>refalpha_medium_color</c> as <c>"R G B Density"</c> defaulting to <c>0.1 0.19 0.22 0.01</c>, the very
/// colour and murkiness measured as the engine's in the parsed environment, and hands them to the water's
/// <c>water_color_open</c> (water.txt, v0.1.217). Those are material uniforms; the pass's own is still not
/// read itself, but its default and its alpha's meaning no longer rest on names alone.
/// </remarks>
public readonly record struct WaterLook(
    Vector3 OpenColour,
    float Murkiness,
    float FlowIntensity,
    float HeightOffset,
    string Said,
    float Reflectiveness = WaterLook.DefaultReflectiveness,
    float WindAngle = WaterLook.DefaultWindAngle,
    float WindIntensity = WaterLook.DefaultWindIntensity,
    float WindSpeed = WaterLook.DefaultWindSpeed,
    float Directionness = WaterLook.DefaultDirectionness)
{
    /// <summary>The engine's open water colour where a file sets none - ParsedEnvironment.WaterOpenColor, measured in Port, Clearfell and The Red Vale.</summary>
    public static readonly Vector3 DefaultOpenColour = new(0.1f, 0.19f, 0.22f);

    /// <summary>The engine's murkiness where a file sets none - ParsedEnvironment.WaterOpenMurkiness, the same three areas.</summary>
    public const float DefaultMurkiness = 0.01f;

    /// <summary>The engine's height offset where a file sets none - ParsedEnvironment.WaterHeightOffset, Clearfell's.</summary>
    public const float DefaultHeightOffset = 0f;

    /// <summary>The engine's reflectiveness where a file sets none - ParsedEnvironment.WaterReflectiveness, measured in Port, Clearfell and The Red Vale.</summary>
    public const float DefaultReflectiveness = 0.03f;

    /// <summary>The wind's way where a file sets none: along x - CHOSEN, see <see cref="DefaultWindIntensity"/>.</summary>
    public const float DefaultWindAngle = 0f;

    /// <summary>
    /// The ripples' height where a file sets none - CHOSEN: the engine's former constant, <c>0.1 * 4 * 4 / 1.5</c>.
    /// </summary>
    /// <remarks>
    /// NOT YET MEASURED. Port's file sets none of the ocean's keys. The ocean ripples' uniforms
    /// (GetWindRippleSurfaceField3: <c>water_wind_intensity</c>, <c>water_wind_speed</c>,
    /// <c>water_directionness</c>, <c>water_wind_direction</c>) replaced constants the old full-screen tracer
    /// still writes (screenspacetracer.hlsl's GetOceanWaterHeight3: an amplitude of 0.1 * 4 * 4 / 1.5, a
    /// scroll of 1.25, a stretch of 0.225, a turn of 0.2 between octaves and no wind matrix) - the very
    /// numbers the formula gives at a directionness of one, a speed of one and a wind along x. And the
    /// parsed environment's water floats that no file read so far names hold 1.06 and 1 side by side
    /// (light-hunt.txt, v0.1.208). So the former constants stand until a capture in an area whose file
    /// sets these keys (the Blank Sea and Yaochi hideouts set all four) names their slots.
    /// </remarks>
    public const float DefaultWindIntensity = 0.1f * 4f * 4f / 1.5f;

    /// <summary>The ripples' speed where a file sets none - CHOSEN, the former constants' own; see <see cref="DefaultWindIntensity"/>.</summary>
    public const float DefaultWindSpeed = 1f;

    /// <summary>How much the ripples line up with the wind where a file sets none - CHOSEN, the former constants' own; see <see cref="DefaultWindIntensity"/>.</summary>
    public const float DefaultDirectionness = 1f;

    /// <summary>
    /// The flow where a file sets none: none.
    /// </summary>
    /// <remarks>
    /// NOT MEASURED - every area read so far sets its own (Port 1.5, Clearfell 1.5, The Red Vale 0.15),
    /// so the engine's default never stood in a capture. Still water says that; a number made up would not.
    /// </remarks>
    public const float NoFlow = 0f;

    /// <summary>The engine's water, every value its default.</summary>
    public static WaterLook EngineDefault { get; } = new(DefaultOpenColour, DefaultMurkiness, NoFlow, DefaultHeightOffset, "every value the engine's default");

    /// <summary>The colour the program reads: linear, as water.ffx makes it (<c>pow(saturate(rgb), 2.2)</c>), the density in w.</summary>
    public Vector4 ColourRegister => new(
        MathF.Pow(Math.Clamp(OpenColour.X, 0f, 1f), 2.2f),
        MathF.Pow(Math.Clamp(OpenColour.Y, 0f, 1f), 2.2f),
        MathF.Pow(Math.Clamp(OpenColour.Z, 0f, 1f), 2.2f),
        MathF.Max(0f, Murkiness));

    /// <summary>The flow the program reads, in x, the reflectiveness in y and the height offset in z.</summary>
    public Vector4 FlowRegister => new(FlowIntensity, MathF.Max(0f, Reflectiveness), HeightOffset, 0f);

    /// <summary>The wind the ocean's ripples read: its way's angle in x, their height in y, their speed in z and how much they line up in w - see ShadeProgram.WindRipples.</summary>
    public Vector4 WindRegister => new(WindAngle, WindIntensity, WindSpeed, Directionness);

    /// <summary>An area's water: each value its file sets, the engine's default for the rest.</summary>
    public static WaterLook Of(EnvironmentSettings? environment)
    {
        if (environment is not { Water.Count: > 0 } env)
        {
            return EngineDefault;
        }

        Vector3 colour = DefaultOpenColour;
        float murkiness = DefaultMurkiness, flow = NoFlow, height = DefaultHeightOffset, reflectiveness = DefaultReflectiveness;
        float windAngle = DefaultWindAngle, windIntensity = DefaultWindIntensity, windSpeed = DefaultWindSpeed, directionness = DefaultDirectionness;
        var set = new List<string>(9);
        foreach (EnvironmentValue value in env.Water)
        {
            switch (value.Key)
            {
                case "open_water_color" when value.Values.Count >= 3:
                    colour = new Vector3(value.Values[0], value.Values[1], value.Values[2]);
                    set.Add("colour");
                    break;
                case "open_water_murkiness" when value.Values.Count >= 1:
                    murkiness = value.Values[0];
                    set.Add("murkiness");
                    break;
                case "flow_intensity" when value.Values.Count >= 1:
                    flow = value.Values[0];
                    set.Add("flow");
                    break;
                case "height_offset" when value.Values.Count >= 1:
                    height = value.Values[0];
                    set.Add("height offset");
                    break;
                case "reflectiveness" when value.Values.Count >= 1:
                    reflectiveness = value.Values[0];
                    set.Add("reflectiveness");
                    break;
                case "wind_direction_angle" when value.Values.Count >= 1:
                    windAngle = value.Values[0];
                    set.Add("wind's way");
                    break;
                case "wind_intensity" when value.Values.Count >= 1:
                    windIntensity = value.Values[0];
                    set.Add("wind's strength");
                    break;
                case "wind_speed" when value.Values.Count >= 1:
                    windSpeed = value.Values[0];
                    set.Add("wind's speed");
                    break;
                case "directionness" when value.Values.Count >= 1:
                    directionness = value.Values[0];
                    set.Add("directionness");
                    break;
            }
        }

        return new WaterLook(
            colour, murkiness, flow, height,
            set.Count == 0 ? "every value the engine's default" : $"{string.Join(", ", set)} from the file, the rest the engine's default",
            reflectiveness, windAngle, windIntensity, windSpeed, directionness);
    }

    /// <summary>The numbers and where they came from, for the line under a picture.</summary>
    public string Line() => string.Create(
        CultureInfo.InvariantCulture,
        $"water ({OpenColour.X:0.###} {OpenColour.Y:0.###} {OpenColour.Z:0.###}) murkiness {Murkiness:0.####} flow {FlowIntensity:0.##} reflectiveness {Reflectiveness:0.###} wind {WindAngle * 180f / MathF.PI:0}° x{WindIntensity:0.###} speed {WindSpeed:0.##} directionness {Directionness:0.##} - {Said}");
}
