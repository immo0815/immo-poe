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
/// <param name="SwellHeight">water.swell_height: how high the ocean's swell stands, crest to trough - <c>water_swell_height</c>.</param>
/// <param name="SwellPeriod">water.swell_period: one over the swell's periods a colour unit - <c>water_swell_period</c>.</param>
/// <param name="SwellIntensity">water.swell_intensity: how soon the swell takes its shore's shape - <c>water_swell_intensity</c>.</param>
/// <param name="SubsurfaceScattering">water.subsurface_scattering: how far a crest's colour is lifted towards the medium's green and a trough's
/// darkened - <c>water_subsurface_scattering</c>, GetWaterMediumColor's (SceneLight.ShadeWater).</param>
/// <param name="RefractionIndex">water.refraction_index: how far the view bends as it enters the water - <c>water_refraction_index</c>,
/// GetRefractedOpaqueSurface's (SceneLight.WaterRefraction).</param>
/// <param name="CausticsMult">water.caustics_mult: how strongly the light seen through the water is patterned by the caustics -
/// <c>water_caustics_mult</c>, GetRefractedOpaqueSurface's (SceneLight.WaterCaustics).</param>
/// <param name="FlowFoam">water.flow_foam: how much the river's foam grows with its flow - <c>water_flow_foam</c>, GetFlowingWaterPlanarStateWorld's
/// (ShadeProgram.RiverFoam).</param>
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
    float Directionness = WaterLook.DefaultDirectionness,
    float SwellHeight = WaterLook.DefaultSwellHeight,
    float SwellPeriod = WaterLook.DefaultSwellPeriod,
    float SwellIntensity = WaterLook.DefaultSwellIntensity,
    float SubsurfaceScattering = WaterLook.DefaultSubsurfaceScattering,
    float RefractionIndex = WaterLook.DefaultRefractionIndex,
    float CausticsMult = WaterLook.DefaultCausticsMult,
    float FlowFoam = WaterLook.DefaultFlowFoam)
{
    /// <summary>The engine's open water colour where a file sets none - ParsedEnvironment.WaterOpenColor, measured in Port, Clearfell and The Red Vale.</summary>
    public static readonly Vector3 DefaultOpenColour = new(0.1f, 0.19f, 0.22f);

    /// <summary>The engine's murkiness where a file sets none - ParsedEnvironment.WaterOpenMurkiness, the same three areas.</summary>
    public const float DefaultMurkiness = 0.01f;

    /// <summary>The engine's height offset where a file sets none - ParsedEnvironment.WaterHeightOffset, Clearfell's.</summary>
    public const float DefaultHeightOffset = 0f;

    /// <summary>The engine's reflectiveness where a file sets none - ParsedEnvironment.WaterReflectiveness, measured in Port, Clearfell and The Red Vale.</summary>
    public const float DefaultReflectiveness = 0.03f;

    /// <summary>The wind's way where a file sets none: along x - ParsedEnvironment.WaterWindDirectionAngle, see <see cref="DefaultWindIntensity"/>.</summary>
    public const float DefaultWindAngle = 0f;

    /// <summary>
    /// The ripples' height where a file sets none - ParsedEnvironment.WaterWindIntensity, MEASURED (v0.1.343): Port's 1.06 with a flag of nought.
    /// </summary>
    /// <remarks>
    /// THE OCEAN'S DEFAULTS NAMED BY CRIMSON SHORES (v0.1.341's light-hunt.txt), whose file sets the wind's way, height and speed, the
    /// directionness, the swell's height and intensity and the caustics: each found at its slot of the parsed environment with a flag of
    /// one, where Port's copies hold the engine's defaults with a nought - the wind's way 0 (+0x250), its height 1.06 (+0x258), its speed 1
    /// (+0x260), the directionness 1 (+0x208), the swell's height 37.5 (+0x270) and intensity 0 (+0x278), its period 0.04 at +0x280 (no file
    /// read sets it; the swell's four keys lie in their names' order, +0x268 to +0x280). Until then these stood CHOSEN as the old
    /// full-screen tracer's constants (screenspacetracer.hlsl: 0.1 * 4 * 4 / 1.5, a speed and directionness of one, no wind matrix, a swell
    /// of 37.5 and 0.04) - all of them the engine's but the wind's height, 1.0667 against 1.06, and the swell's intensity, 1 against 0.
    /// </remarks>
    public const float DefaultWindIntensity = 1.06f;

    /// <summary>The ripples' speed where a file sets none - ParsedEnvironment.WaterWindSpeed, MEASURED; see <see cref="DefaultWindIntensity"/>.</summary>
    public const float DefaultWindSpeed = 1f;

    /// <summary>How much the ripples line up with the wind where a file sets none - ParsedEnvironment.WaterDirectionness, MEASURED; see <see cref="DefaultWindIntensity"/>.</summary>
    public const float DefaultDirectionness = 1f;

    /// <summary>The swell's height where a file sets none - ParsedEnvironment.WaterSwellHeight, MEASURED; see <see cref="DefaultWindIntensity"/>.</summary>
    public const float DefaultSwellHeight = 37.5f;

    /// <summary>
    /// The swell's period where a file sets none - the 0.04 at ParsedEnvironment +0x280, named by its place after the swell's height and
    /// intensity, no file read setting it; see <see cref="DefaultWindIntensity"/>.
    /// </summary>
    public const float DefaultSwellPeriod = 1f / 25f;

    /// <summary>
    /// How soon the swell takes its shore's shape where a file sets none - ParsedEnvironment.WaterSwellIntensity, MEASURED: nought (CHOSEN as
    /// one until v0.1.343); see <see cref="DefaultWindIntensity"/>.
    /// </summary>
    public const float DefaultSwellIntensity = 0f;

    /// <summary>
    /// How strongly the caustics pattern the light seen through the water where a file sets none - ParsedEnvironment.WaterCausticsMult,
    /// MEASURED (v0.1.343): Port's 1 with a flag of nought, the slot named by Crimson Shores' file, which sets 3. legacy.ffx's water materials
    /// declare their own <c>water_uv_caustics_mult</c> with a default of one too.
    /// </summary>
    public const float DefaultCausticsMult = 1f;

    /// <summary>
    /// How much the river's foam grows with its flow where a file sets none - ParsedEnvironment.WaterFlowFoam, MEASURED: Port's 1 with a
    /// flag of nought, the slot named by Clearfell's and The Red Vale's files (0.5 and 0.3).
    /// </summary>
    public const float DefaultFlowFoam = 1f;

    /// <summary>
    /// The medium's scattering where a file sets none - ParsedEnvironment.WaterSubsurfaceScattering: Port's default one with a flag of
    /// nought, the slot named by The Red Vale's file (light-hunt.txt, v0.1.212).
    /// </summary>
    public const float DefaultSubsurfaceScattering = 1f;

    /// <summary>
    /// The refraction index where a file sets none - ParsedEnvironment.WaterRefractionIndex: Port's default 1.33 with a flag of nought, the slot
    /// named by The Red Vale's file, which sets 1.30095 (light-hunt.txt, v0.1.212).
    /// </summary>
    public const float DefaultRefractionIndex = 1.33f;

    /// <summary>
    /// The room's shore, for the ocean's swell - see ShoreField; null where the room has none, or nothing made one. Not the area's: it is the drawing's.
    /// </summary>
    public ShoreField? Shore { get; init; }

    /// <summary>No flow: still water.</summary>
    public const float NoFlow = 0f;

    /// <summary>
    /// The flow where a file sets none - ParsedEnvironment.WaterFlowIntensity, MEASURED (v0.1.343): Crimson Shores' file sets none and its
    /// copies hold 1 with a flag of nought. Until then still water stood for it, every area read before setting its own (Port 1.5, Clearfell
    /// 1.5, The Red Vale 0.15).
    /// </summary>
    public const float DefaultFlowIntensity = 1f;

    /// <summary>The engine's water, every value its default.</summary>
    public static WaterLook EngineDefault { get; } = new(DefaultOpenColour, DefaultMurkiness, DefaultFlowIntensity, DefaultHeightOffset, "every value the engine's default");

    /// <summary>The colour the program reads: linear, as water.ffx makes it (<c>pow(saturate(rgb), 2.2)</c>), the density in w.</summary>
    public Vector4 ColourRegister => new(
        MathF.Pow(Math.Clamp(OpenColour.X, 0f, 1f), 2.2f),
        MathF.Pow(Math.Clamp(OpenColour.Y, 0f, 1f), 2.2f),
        MathF.Pow(Math.Clamp(OpenColour.Z, 0f, 1f), 2.2f),
        MathF.Max(0f, Murkiness));

    /// <summary>The flow the program reads, in x, the reflectiveness in y, the height offset in z and the river's flow_foam in w.</summary>
    public Vector4 FlowRegister => new(FlowIntensity, MathF.Max(0f, Reflectiveness), HeightOffset, FlowFoam);

    /// <summary>The wind the ocean's ripples read: its way's angle in x, their height in y, their speed in z and how much they line up in w - see ShadeProgram.WindRipples.</summary>
    public Vector4 WindRegister => new(WindAngle, WindIntensity, WindSpeed, Directionness);

    /// <summary>The swell the ocean reads: its height in x, its period in y, its intensity in z - see ShadeProgram.Ocean.</summary>
    public Vector4 SwellRegister => new(SwellHeight, SwellPeriod, SwellIntensity, 0f);

    /// <summary>The shore's box the ocean reads - ShoreField.Register, nought where there is no shore.</summary>
    public Vector4 ShoreRegister => Shore?.Register ?? Vector4.Zero;

    /// <summary>An area's water: each value its file sets, the engine's default for the rest.</summary>
    public static WaterLook Of(EnvironmentSettings? environment)
    {
        if (environment is not { Water.Count: > 0 } env)
        {
            return EngineDefault;
        }

        Vector3 colour = DefaultOpenColour;
        float murkiness = DefaultMurkiness, flow = DefaultFlowIntensity, height = DefaultHeightOffset, reflectiveness = DefaultReflectiveness;
        float windAngle = DefaultWindAngle, windIntensity = DefaultWindIntensity, windSpeed = DefaultWindSpeed, directionness = DefaultDirectionness;
        float swellHeight = DefaultSwellHeight, swellPeriod = DefaultSwellPeriod, swellIntensity = DefaultSwellIntensity;
        float scattering = DefaultSubsurfaceScattering, index = DefaultRefractionIndex, caustics = DefaultCausticsMult, flowFoam = DefaultFlowFoam;
        var set = new List<string>(12);
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
                // TWO SPELLINGS, NO FILE SETTING BOTH (v0.1.341's water.txt: 22 files, the maps, write water_wind_*, and 30, the
                // hideouts, wind_*): the first's slots are MEASURED as the ripples' (ParsedEnvironment.WaterWindIntensity, Port's default
                // the old tracer's constant), the second taken for the same by its name alone.
                case "water_wind_direction_angle" or "wind_direction_angle" when value.Values.Count >= 1:
                    windAngle = value.Values[0];
                    set.Add("wind's way");
                    break;
                case "water_wind_intensity" or "wind_intensity" when value.Values.Count >= 1:
                    windIntensity = value.Values[0];
                    set.Add("wind's strength");
                    break;
                case "water_wind_speed" or "wind_speed" when value.Values.Count >= 1:
                    windSpeed = value.Values[0];
                    set.Add("wind's speed");
                    break;
                case "directionness" when value.Values.Count >= 1:
                    directionness = value.Values[0];
                    set.Add("directionness");
                    break;
                case "swell_height" when value.Values.Count >= 1:
                    swellHeight = value.Values[0];
                    set.Add("swell's height");
                    break;
                case "swell_period" when value.Values.Count >= 1 && value.Values[0] > 0f:
                    swellPeriod = value.Values[0];
                    set.Add("swell's period");
                    break;
                case "swell_intensity" when value.Values.Count >= 1:
                    swellIntensity = value.Values[0];
                    set.Add("swell's intensity");
                    break;
                case "subsurface_scattering" when value.Values.Count >= 1:
                    scattering = value.Values[0];
                    set.Add("scattering");
                    break;
                case "refraction_index" when value.Values.Count >= 1:
                    index = value.Values[0];
                    set.Add("refraction index");
                    break;
                case "caustics_mult" when value.Values.Count >= 1:
                    caustics = value.Values[0];
                    set.Add("caustics");
                    break;
                case "flow_foam" when value.Values.Count >= 1:
                    flowFoam = value.Values[0];
                    set.Add("flow foam");
                    break;
            }
        }

        return new WaterLook(
            colour, murkiness, flow, height,
            set.Count == 0 ? "every value the engine's default" : $"{string.Join(", ", set)} from the file, the rest the engine's default",
            reflectiveness, windAngle, windIntensity, windSpeed, directionness, swellHeight, swellPeriod, swellIntensity, scattering, index, caustics, flowFoam);
    }

    /// <summary>The numbers and where they came from, for the line under a picture.</summary>
    public string Line() => string.Create(
        CultureInfo.InvariantCulture,
        $"water ({OpenColour.X:0.###} {OpenColour.Y:0.###} {OpenColour.Z:0.###}) murkiness {Murkiness:0.####} flow {FlowIntensity:0.##} reflectiveness {Reflectiveness:0.###} wind {WindAngle * 180f / MathF.PI:0}° x{WindIntensity:0.###} speed {WindSpeed:0.##} directionness {Directionness:0.##} swell {SwellHeight:0.#} period {SwellPeriod:0.###} intensity {SwellIntensity:0.##} scattering {SubsurfaceScattering:0.##} refraction index {RefractionIndex:0.###} caustics x{CausticsMult:0.###} flow foam {FlowFoam:0.###} - {Said}");
}
