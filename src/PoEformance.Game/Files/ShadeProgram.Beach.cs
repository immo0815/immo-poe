using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>The engine's beach waves - water.ffx's beach_waves, BreakingWavesVertex and BreakingWavesFragment.</summary>
public sealed partial class ShadeProgram
{
    /// <summary>The graph the beach waves are drawn with: the engine's own, which the game's program names (CaptureWaves).</summary>
    public const string BeachGraph = "Metadata/Effects/Graphs/Environment/BreakingOceanWaves.fxgraph";

    /// <summary>What the beach program is named in a picture's lines.</summary>
    public const string BeachName = "the engine's beach waves (BreakingOceanWaves.fxgraph: BreakingWavesVertex and BreakingWavesFragment)";

    /// <summary>What the beach's wetness program is named by - see <see cref="Wetness"/>.</summary>
    public const string BeachWetnessName = "the wetness the beach waves cast (BreakingOceanWavesWetness.fxgraph: BreakingWavesFragment's wetness)";

    /// <summary>beach_waves' <c>beach_wave_speed</c>, in units a second.</summary>
    public const float BeachWaveSpeed = 200f;

    /// <summary>beach_waves' <c>beach_wave_length</c>, in units.</summary>
    public const float BeachWaveLength = 500f;

    /// <summary>beach_waves' <c>beach_foam_spray_width</c>.</summary>
    private const float BeachSprayWidth = 0.2f;

    /// <summary>beach_waves' <c>beach_wave_shoreline_offset</c>.</summary>
    private const float BeachShorelineOffset = 0.5f;

    /// <summary>beach_waves' <c>beach_nodes_per_wave</c>.</summary>
    private const float BeachNodesPerWave = 4f;

    /// <summary>
    /// How far out to sea of the room's waterline the shore's travel distance is nought, in world units - half a wave, CHOSEN.
    /// </summary>
    /// <remarks>
    /// THE ENGINE'S WAVES STAND TALLEST AND BREAK WHERE <c>shore_coord</c> IS NOUGHT, half a wave in from where its own
    /// <c>shore_travel_dist</c> is (<c>beach_wave_shoreline_offset</c>); where that distance is nought in the engine's field is read
    /// nowhere. MEASURED on a beach rising a tenth a unit (BeachTests): with the room's waterline as nought the crest stood tallest
    /// 240 units up the dry sand and the foam ran from 300 to 600 in - while the game's foam line lies on the shore (said from the
    /// live client). So the distance is taken this much less: the phase shoreline falls on the waterline, the foam runs up past it.
    /// </remarks>
    public const float BeachTravelShift = BeachWaveLength * BeachShorelineOffset;

    /// <summary>
    /// How fast the water film under the foam swallows what lies behind it, a unit of path - the mean of the graph's own absorption.
    /// </summary>
    /// <remarks>
    /// THE GRAPH'S, AVERAGED. BreakingOceanWaves.fxgraph hands GetWaterSubsurfaceColor a <c>scatter_intensity</c> of its constant
    /// <c>(1.5, 1, 0.4) * 0.005</c> - its numbers as written, as the compile reads every constant, the graph's sRGB flag on it unread -
    /// plus <c>0.1 * saturate(subsurface * 0)</c>, a MultiplyConst left at its declared nought. The engine lets through
    /// <c>exp(-path * absorption)</c> channel by channel; a picture that mixes by one alpha takes the three's mean, CHOSEN.
    /// </remarks>
    public const float BeachAbsorption = (1.5f + 1f + 0.4f) * 0.005f / 3f;

    /// <summary>The graph's foam share of the colour's alpha: its MultiplyConst 3, <c>0.7</c>.</summary>
    private const float BeachFoamShare = 0.7f;

    /// <summary>The graph's specular colour, its ConstantFloat 2: <c>0.05</c>.</summary>
    public const float BeachSpecular = 0.05f;

    /// <summary>The beach wave shape read: the stand-in until <see cref="Use"/> hands over the engine's.</summary>
    private static WaterSheet s_beachProfile = new(2, 2, 4, [0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f], false, BeachProfileStandIn);

    /// <summary>The beach wave noise read: the stand-in until <see cref="Use"/> hands over the engine's.</summary>
    private static WaterSheet s_beachNoise = new(2, 2, 4, [0.5f, 0.5f, 0f, 1f, 0.5f, 0.5f, 0f, 1f, 0.5f, 0.5f, 0f, 1f, 0.5f, 0.5f, 0f, 1f], false, BeachNoiseStandIn);

    private const string BeachProfileStandIn = "the beach waves stand flat - the engine's wave shape not read";

    private const string BeachNoiseStandIn = "the beach waves' noise a half - the engine's not read";

    /// <summary>
    /// The beach waves' wave shape, row by row, four floats a texel - the crest's sideways offset and its height: the engine's
    /// (WaterLookups.BeachProfilePath) once handed over, nought until then, which stands the waves flat.
    /// </summary>
    public static WaterSheet BeachProfile => Volatile.Read(ref s_beachProfile);

    /// <summary>
    /// The beach waves' noise, row by row, four floats a texel - its red and green: the engine's (WaterLookups.BeachNoisePath) once
    /// handed over, a half everywhere until then, which leaves every wave unshifted.
    /// </summary>
    public static WaterSheet BeachNoise => Volatile.Read(ref s_beachNoise);

    /// <summary>
    /// The beach waves' own two lookups taken where read - see <see cref="Use"/>.
    /// </summary>
    private static void UseBeach(WaterLookups lookups)
    {
        Volatile.Write(ref s_beachProfile, lookups.BeachProfile is { Channels: 4, Width: > 0, Height: > 0 } profile
            ? profile
            : BeachProfile with { Said = BeachProfileStandIn });
        Volatile.Write(ref s_beachNoise, lookups.BeachNoise is { Channels: 4, Width: > 0, Height: > 0 } noise
            ? noise
            : BeachNoise with { Said = BeachNoiseStandIn });
    }

    /// <summary>
    /// What the beach waves hand their graph: the colour and how much it covers, linear, the surface's normal and its gloss - and, for
    /// the probe, the foam's share of the cover and the film's thickness under it; and the wetness BreakingWavesFragment casts.
    /// </summary>
    public readonly record struct BeachLook(Vector4 Colour, Vector3 Normal, float Gloss, float Foam = 0f, float Thickness = 0f, float Wetness = 0f);

    /// <summary>How the beach waves read their three textures: a sheet's texel at coordinates at its top level, and its average.</summary>
    public interface IBeachTextures : ISprayTextures
    {
        /// <summary>The sheet's colour at its smallest level - the engine's read at level fifteen.</summary>
        Vector4 Average(int sheet);
    }

    /// <summary>A program's own sheets, read at their top level, the average at their smallest.</summary>
    private readonly struct BeachLevels(ShadeProgram program) : IBeachTextures
    {
        public Vector4 Read(int sheet, Vector2 uv) => program.Read(sheet, uv, 0f);

        public Vector4 Average(int sheet) => program.Read(sheet, new Vector2(0.5f, 0.5f), 15f);
    }

    /// <summary>What one <see cref="Op.Beach"/> step was run on - its two outputs are two steps over one sum, worked out once.</summary>
    private readonly record struct BeachKey(ShadeProgram? Program, Vector4 Place, Vector4 Packed, Vector4 Water, int Sheets, ShoreField? Shore);

    [ThreadStatic]
    private static BeachKey s_beachKey;

    [ThreadStatic]
    private static BeachLook s_beach;

    /// <summary>The last beach step this thread ran - the probed pixel's, read straight after its program ran there (MeshPicture's probe).</summary>
    internal static BeachLook LastBeach => s_beach;

    /// <summary>The beach step's outputs, in its Extra's top byte: the colour and its cover.</summary>
    internal const int BeachColour = 0;

    /// <summary>The surface's normal in xyz, its gloss in w.</summary>
    internal const int BeachNormal = 1;

    /// <summary>The wetness the fragment casts, in all four - see <see cref="Wetness"/>.</summary>
    internal const int BeachWetness = 2;

    /// <summary>
    /// One output of the beach step: A the place, B the clock and the depth the view crosses behind (x and y), C the shore's register,
    /// D the area's water colour; Extra the three sheets a byte each and in its top byte which output.
    /// </summary>
    private Vector4 Beached(Span<Vector4> r, Step step)
    {
        ShoreField? shore = r[step.C].W > 0.5f ? s_shore : null;
        int sheets = step.Extra & 0xFFFFFF;
        var key = new BeachKey(this, r[step.A], r[step.B], r[step.D], sheets, shore);
        if (!key.Equals(s_beachKey))
        {
            Vector4 place = r[step.A], packed = r[step.B], water = r[step.D];
            s_beach = Beaching(
                new Vector2(place.X, place.Y), packed.X, packed.Y, new Vector3(water.X, water.Y, water.Z), shore,
                new BeachLevels(this), sheets & 0xFF, (sheets >> 8) & 0xFF, (sheets >> 16) & 0xFF);
            s_beachKey = key;
        }

        BeachLook beach = s_beach;
        return (step.Extra >> 24) switch
        {
            BeachColour => beach.Colour,
            BeachNormal => new Vector4(beach.Normal, beach.Gloss),
            _ => new Vector4(beach.Wetness),
        };
    }

    /// <summary>
    /// The beach waves as a program: BreakingWavesFragment's surface laid on the room's beach sheet, as the graph mixes it - see
    /// <see cref="Beaching"/>; the glossy twin hands the game light the surface's normal, the specular the graph's 0.05 and its gloss.
    /// </summary>
    /// <param name="foamColour">The foam's colour - the texture the graph binds to <c>foam_color_tex</c>.</param>
    /// <param name="foamNormal">The foam's normal, ao and roughness - <c>foam_normal_tex</c>.</param>
    /// <param name="rippleNormal">The ripples' normal - <c>ripple_normal_tex</c>.</param>
    public static ShadeProgram Beach(ShadeTexture foamColour, ShadeTexture foamNormal, ShadeTexture rippleNormal)
    {
        var build = new Builder();
        int sheets = build.Texture(foamColour) | (build.Texture(foamNormal) << 8) | (build.Texture(rippleNormal) << 16);

        // HOW MUCH THE VIEW CROSSES TO THE SOLID BEHIND, the film's path, beside the clock in one register.
        int crossed = build.Emit(Op.Max, build.Emit(Op.Depth, Position), build.Constant(Vector4.Zero));
        int packed = build.Emit(Op.Swizzle, Clock, extra: AllFrom(0));
        build.Into(packed, crossed, Only(1, 0));
        int colour = build.Emit(Op.Beach, Position, packed, WaterShore, WaterColour, extra: sheets | (BeachColour << 24));
        ShadeProgram beach = Finish(build, colour, -1, -1, [BeachName], hasAlpha: true, plainable: false)
            ?? throw new InvalidOperationException("the beach program does not fit its registers");

        // THE GAME LIGHT'S: the surface's normal for all its light, the graph's specular and the surface's gloss.
        int normal = build.Emit(Op.Beach, Position, packed, WaterShore, WaterColour, extra: sheets | (BeachNormal << 24));
        int gloss = build.Emit(Op.Swizzle, normal, extra: AllFrom(3));
        int specular = build.Constant(new Vector4(BeachSpecular));
        beach.Glossy = Finish(build, colour, specular, gloss, [BeachName], hasAlpha: true, plainable: false, normal)
            ?? throw new InvalidOperationException("the beach's light does not fit its registers");

        // AND THE WETNESS IT CASTS, a program of its own whose colour's alpha it is - BreakingOceanWavesWetness.fxgraph writes
        // BreakingWavesFragment's wetness into the albedo's w under the blend mode Wetness, over the same moved vertices. Both
        // programs carry it, the flat and the glossy, so whichever a picture draws casts.
        int wet = build.Emit(Op.Beach, Position, packed, WaterShore, WaterColour, extra: sheets | (BeachWetness << 24));
        ShadeProgram wetness = Finish(build, wet, -1, -1, [BeachWetnessName], hasAlpha: true, plainable: false)
            ?? throw new InvalidOperationException("the beach's wetness does not fit its registers");
        beach.Wetness = wetness;
        beach.Glossy.Wetness = wetness;
        return beach;
    }

    /// <summary>
    /// The beach program for the engine's graph: <see cref="Beach(ShadeTexture, ShadeTexture, ShadeTexture)"/> over the three textures the
    /// graph binds to BreakingWavesFragment - or none, and why, where it binds one of them to nothing.
    /// </summary>
    /// <remarks>
    /// THE GRAPH'S OWN TEXTURES, by the links into the fragment's ports and the InputTexture each comes from - its path and its sRGB
    /// flag as written (BreakingOceanWaves.fxgraph: SeaFoam_colour_Uncompressed.dds in sRGB, SeaFoam_normal_Uncompressed.dds and
    /// backwatertile_normal_DXT1.dds as they are). Its wave shape and noise are fixed lookups (WaterLookups), not the program's.
    /// </remarks>
    public static ShadeCompile BeachOf(ShaderGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);
        ShadeTexture? Bound(string port)
        {
            foreach (ShaderLink link in graph.Links)
            {
                if (link.Target.Type != "BreakingWavesFragment" || link.Target.Variable != port || link.Source.Type != "InputTexture")
                {
                    continue;
                }

                foreach (ShaderNode node in graph.Nodes)
                {
                    if (node.Type == "InputTexture" && node.Index == link.Source.Index && node.Parameters.Length > 0 && node.Parameters[0].Path.Length > 0)
                    {
                        return new ShadeTexture(node.Parameters[0].Path, node.Parameters[0].Srgb ?? false);
                    }
                }
            }

            return null;
        }

        string[] ports = ["foam_color_tex", "foam_normal_tex", "ripple_normal_tex"];
        var bound = new ShadeTexture[ports.Length];
        for (var one = 0; one < ports.Length; one++)
        {
            if (Bound(ports[one]) is not { } texture)
            {
                return new ShadeCompile(null, [$"{BeachGraph}: no texture bound to BreakingWavesFragment's {ports[one]}"]);
            }

            bound[one] = texture;
        }

        return new ShadeCompile(
            Beach(bound[0], bound[1], bound[2]),
            [$"{BeachGraph}: drawn as the engine's beach waves - its sheet, the film's colour and the shore's distances CHOSEN (ShadeProgram.Beaching)"]);
    }

    /// <summary>
    /// How far a beach vertex moves along z at a place: BreakingWavesVertex's <c>-normal_field.x + water_height_offset</c>, up being minus z.
    /// </summary>
    /// <param name="place">Where, in world units.</param>
    /// <param name="clock">The clock, in seconds.</param>
    /// <param name="heightOffset">The area's <c>water_height_offset</c> - WaterLook.FlowRegister's z.</param>
    /// <param name="shore">The room's shore, or null - the offset alone without one.</param>
    public static float BeachLift(Vector2 place, float clock, float heightOffset, ShoreField? shore)
        => BeachLift(BeachProfile, BeachNoise, place, clock, heightOffset, shore);

    /// <summary>The same, over a given wave shape and noise.</summary>
    public static float BeachLift(WaterSheet profile, WaterSheet noise, Vector2 place, float clock, float heightOffset, ShoreField? shore)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(noise);
        if (shore is null)
        {
            return heightOffset;
        }

        Front front = FrontAt(place, clock, shore, noise, foam: false);
        return -BeachNormalField(front.Geom0, front.Geom1, profile).X + heightOffset;
    }

    /// <summary>
    /// The beach waves' surface at a place, as BreakingWavesFragment hands it to BreakingOceanWaves.fxgraph and the graph mixes it.
    /// </summary>
    /// <param name="place">Where, in world units.</param>
    /// <param name="clock">The clock, in seconds.</param>
    /// <param name="crossed">How far the view crosses from the surface to the solid behind it.</param>
    /// <param name="water">The area's open water colour, linear - WaterLook.ColourRegister's xyz.</param>
    /// <param name="shore">The room's shore, or null - nothing drawn without one.</param>
    /// <param name="read">How the three textures are read.</param>
    /// <param name="colourSheet">The foam's colour - <c>foam_color_tex</c>.</param>
    /// <param name="normalSheet">The foam's normal, ao and roughness - <c>foam_normal_tex</c>.</param>
    /// <param name="rippleSheet">The ripples' normal - <c>ripple_normal_tex</c>.</param>
    /// <remarks>
    /// AS WRITTEN (water.ffx's beach_waves): GetBeachWaveFront - two waves 500 units long rolling in at 200 a second off the
    /// distance out to sea, each its own hash's intensity, a noise shifting its front, a break phase from the shore's distance, and
    /// past the shoreline slowed to a stop (GetShorePhaseOffsetField); GetBeachFoamSurface - the foam of each laid as two scrolling
    /// layers of the foam texture a quarter wave apart, its profile spiking at the hump, its spray at the front, fading past the
    /// shoreline by a noise's dissipation, the nearer wave laid over the farther; GetBeachWaveNormalField - the crest's height off
    /// the wave shape, fading out past the shoreline; and the surface's ripples, three octaves of the ripple texture turned and
    /// drifting on the noise. The normal is GradToNormal of the three gradients, turned to the frame's up.
    ///
    /// THE GRAPH'S MIX (BreakingOceanWaves.fxgraph): the foam covers <c>f = 0.7 * a</c>, the water film under it
    /// <c>thickness</c>; the albedo is the foam's colour over <c>1 - (1 - f)(1 - thickness)</c>, the specular 0.05, the gloss
    /// <c>1 - roughness</c>, and the light that reaches the film from behind is let through by <c>exp(-path * absorption)</c>,
    /// the path the depth to the solid behind times the thickness (GetWaterSubsurfaceColor, ReadDispersedGBufferColor). One
    /// alpha stands for both here: <c>f + (1 - f) * thickness * (1 - through)</c>, the absorbed share drawn in the area's water
    /// colour, as the sea beside it - CHOSEN, the engine's film scattering its own dark colour (<c>(0.05, 0.6, 0.9) * 0.1</c>) by
    /// a pass of subsurface light the picture does not have. NOT DRAWN: the refraction's bend, its caustics
    /// (water_caustics_2_BC1.dds), the wet sand the wetness graph darkens, and the foam's spray particles.
    ///
    /// THE SHORE'S TWO DISTANCES ARE ONE. GetShoreFieldData hands the waves <c>ocean_dist</c> (the field's blue) and
    /// <c>shore_travel_dist</c> (its green), each over <c>dist_to_color</c>; the room's field (ShoreField) holds one distance to the
    /// shore, so both are it, in world units (<see cref="ShoreField.Reach"/> a colour unit, which the waves' world units cancel) -
    /// CHOSEN, which puts each wave's shoreline square to the nearest shore - the travel distance less <see cref="BeachTravelShift"/>. The turbulence the scrolling layers read (<c>noise_sampler</c>, a pass texture named
    /// nowhere) stands at a half, which moves nothing. On the processor every texture is read at its top level.
    /// </remarks>
    public static BeachLook Beaching<TRead>(
        Vector2 place, float clock, float crossed, Vector3 water, ShoreField? shore, TRead read, int colourSheet, int normalSheet, int rippleSheet)
        where TRead : struct, IBeachTextures
        => Beaching(BeachProfile, BeachNoise, place, clock, crossed, water, shore, read, colourSheet, normalSheet, rippleSheet);

    /// <summary>The same, over a given wave shape and noise.</summary>
    public static BeachLook Beaching<TRead>(
        WaterSheet profile, WaterSheet noise, Vector2 place, float clock, float crossed, Vector3 water, ShoreField? shore,
        TRead read, int colourSheet, int normalSheet, int rippleSheet)
        where TRead : struct, IBeachTextures
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(noise);
        if (shore is null)
        {
            return new BeachLook(Vector4.Zero, new Vector3(0f, 0f, -1f), 0f);
        }

        Front front = FrontAt(place, clock, shore, noise, foam: true);
        Surface surface = BeachFoamSurface(place, front.Foam0, front.Foam1, clock, read, colourSheet, normalSheet, noise);
        Vector3 normalField = BeachNormalField(front.Geom0, front.Geom1, profile);
        Vector2 ripples = BeachRipples(place, clock, read, rippleSheet, noise);

        // GradToNormal of the three gradients, times (1, 1, -1): up is minus z.
        Vector2 grad = new Vector2(normalField.Y, normalField.Z) + new Vector2(surface.Normal.Y, surface.Normal.Z) + ripples;
        Vector3 normal = Vector3.Normalize(new Vector3(-grad.X, -grad.Y, -1f));

        // THE GRAPH'S MIX, the film's absorbed share in the area's water colour - see the remarks.
        float foam = BeachFoamShare * surface.Colour.W;
        float thickness = Saturated(surface.Subsurface);
        float through = MathF.Exp(-crossed * thickness * BeachAbsorption);
        float film = (1f - foam) * thickness * (1f - through);
        float cover = foam + film;
        Vector3 albedo = cover > 0f
            ? ((new Vector3(surface.Colour.X, surface.Colour.Y, surface.Colour.Z) * foam) + (water * film)) / cover
            : Vector3.Zero;
        return new BeachLook(new Vector4(albedo, cover), normal, Saturated(1f - surface.Rough), foam, thickness, BeachWetted(place, front, surface, noise));
    }

    /// <summary>
    /// BreakingWavesFragment's wetness as written: the foam's subsurface past a tenth, at least the shore's own band - eight tenths
    /// wherever the shore's coordinate stands within 1.4 of the waterline, frayed by the noise's red at a thousandth of the place.
    /// </summary>
    private static float BeachWetted(Vector2 place, in Front front, in Surface surface, WaterSheet noise)
    {
        float fromFoam = MathSmoothStep(surface.Subsurface, 0.1f, 0.5f);
        float band = 0.8f * Saturated((front.ShoreX + 1.4f + ((NoiseAt(noise, place * 1e-3f).X - 0.5f) * 0.5f)) * 3.1f);
        return MathF.Max(fromFoam, band);
    }

    /// <summary>GetBeachWaveFront: the two foam waves and the two geometry waves at a place - the foam's only where asked for - and its shore coordinate's x.</summary>
    private readonly record struct Front(WavePhase Foam0, WavePhase Foam1, WavePhase Geom0, WavePhase Geom1, float ShoreX);

    /// <summary>One wave of the front: beach_waves' WavePhase.</summary>
    private struct WavePhase
    {
        public Vector3 WaveCoord;
        public Vector3 BreakPhase;
        public Vector3 ShorePhase;
        public Vector2 Dir;
        public float PhaseSpeed;
        public int Index;
        public float IntensityMult;
        public float InterpRatio;
    }

    /// <summary>GetBeachWaveFront as written, the shore's two distances the field's one - see <see cref="Beaching"/>.</summary>
    private static Front FrontAt(Vector2 place, float clock, ShoreField shore, WaterSheet noise, bool foam)
    {
        // GetShoreFieldData: the distance and its slope by central differences a texel apart, in world units.
        float eps = shore.CellSize;
        shore.At(place, out float centre, out _);
        shore.At(place + new Vector2(eps, 0f), out float xp, out _);
        shore.At(place - new Vector2(eps, 0f), out float xn, out _);
        shore.At(place + new Vector2(0f, eps), out float yp, out _);
        shore.At(place - new Vector2(0f, eps), out float yn, out _);
        float inv = 0.5f / eps;
        var ocean = new Vector3(centre * ShoreField.Reach, (xp - xn) * inv * ShoreField.Reach, (yp - yn) * inv * ShoreField.Reach);
        Vector3 travel = ocean - new Vector3(BeachTravelShift, 0f, 0f);

        Vector3 globalFront = (-ocean / BeachWaveLength) - new Vector3(clock * (BeachWaveSpeed / BeachWaveLength), 0f, 0f);
        Vector3 shoreCoord = (travel / BeachWaveLength) + new Vector3(1f - BeachShorelineOffset, 0f, 0f);

        // normalize, guarded: where the field stands flat - a ridge between two shores, or past its reach - the waves run nowhere.
        Vector2 dir = -Direction(new Vector2(ocean.Y, ocean.Z));
        float inland = MathF.Min(0f, shoreCoord.X);
        WavePhase geom0, geom1, foam0 = default, foam1 = default;
        (int g0, int g1) = InterpIndices(globalFront.X - 0.5f + inland);
        geom0 = BuildWavePhase(place, dir, g0, shoreCoord, globalFront - new Vector3(g0, 0f, 0f), clock, noise);
        geom1 = BuildWavePhase(place, dir, g1, shoreCoord, globalFront - new Vector3(g1, 0f, 0f), clock, noise);
        if (foam)
        {
            (int f0, int f1) = InterpIndices(globalFront.X + inland);
            foam0 = BuildWavePhase(place, dir, f0, shoreCoord, globalFront - new Vector3(f0, 0f, 0f), clock, noise);
            foam1 = BuildWavePhase(place, dir, f1, shoreCoord, globalFront - new Vector3(f1, 0f, 0f), clock, noise);
        }

        return new Front(foam0, foam1, geom0, geom1, shoreCoord.X);
    }

    /// <summary>BuildWavePhase as written.</summary>
    private static WavePhase BuildWavePhase(Vector2 place, Vector2 dir, int index, Vector3 shoreCoord, Vector3 frontCoord, float clock, WaterSheet noise)
    {
        Vector3 waveHash = HashStable(new Vector3(index, 3f, 0f));
        float shoreNoise = NoiseAt(noise, (place * 2e-3f) + new Vector2(waveHash.X, waveHash.Y)).X;

        // MakeUniformUvField(place) less float2x3(front * dir.x, front * dir.y) * beach_wave_length, a row each.
        Vector3 row0 = new Vector3(place.X, 1f, 0f) - (frontCoord * (dir.X * BeachWaveLength));
        Vector3 row1 = new Vector3(place.Y, 0f, 1f) - (frontCoord * (dir.Y * BeachWaveLength));

        WavePhase phase = default;
        phase.WaveCoord = frontCoord;
        phase.ShorePhase = -phase.WaveCoord - shoreCoord;
        float phaseSpeed = 1f;
        if (phase.ShorePhase.X > 0f)
        {
            // GetShorePhaseOffsetField, v0 nought: straight on at first, slowed to a stop.
            float shorePhase = phase.ShorePhase.X;
            float a = -1.5f * (0.5f + shoreNoise);
            phase.WaveCoord.X -= a * shorePhase * shorePhase / 2f;
            phaseSpeed += a * shorePhase;
        }

        Vector3 hash = HashStable(new Vector3(index, 0f, 0f));
        Vector3 offsetNoise = FrontAdvectedNoise(row0, row1, dir * BeachWaveSpeed, hash, clock, noise);
        phase.InterpRatio = (frontCoord.X + 1f) / 2f;
        phase.WaveCoord += new Vector3(0.5f, 0f, 0f) - ((1f - BeachSprayWidth) * offsetNoise);
        phase.IntensityMult = MathSmoothStep(waveHash.X, 0.9f, 0.95f) * 0.3f;
        float breakLength = (phase.IntensityMult * 0.5f) + 2.5f;
        float breakOffset = (phase.IntensityMult - 0.3f) * 5f;
        phase.BreakPhase = new Vector3(1f, 0f, 0f) - ((shoreCoord + new Vector3(-breakOffset, 0f, 0f)) / breakLength);

        // GetBreakNoiseField over MakeUniformUvField(place): the noise's own value and slope.
        Vector3 breakNoise = StationaryNoise((place * 5e-4f) + new Vector2(hash.X, hash.Y), noise);
        breakNoise = new Vector3(breakNoise.X, breakNoise.Y * 5e-4f, breakNoise.Z * 5e-4f);
        phase.BreakPhase += (breakNoise - new Vector3(0.5f, 0f, 0f)) * 5f * Saturated(shoreCoord.X);
        phase.Dir = dir;
        phase.PhaseSpeed = phaseSpeed;
        phase.Index = index;
        return phase;
    }

    /// <summary>GetFrontAdvectedNoiseField: the noise drifting with the front, its slope carried through the front's own field.</summary>
    private static Vector3 FrontAdvectedNoise(Vector3 row0, Vector3 row1, Vector2 velocity, Vector3 hash, float clock, WaterSheet noise)
    {
        const float uvMult = 6e-4f, timeMult = 0.5f;
        var pos = new Vector2(row0.X, row1.X);
        Vector2 uv = (pos * uvMult) + new Vector2(hash.X, hash.Y);
        Vector2 drift = velocity * uvMult / timeMult;

        // GetAdvectedNoiseField3: two layers crossfading, each the stationary field where the drift has taken it - its hash unused.
        (Vector2 phases, _, Vector2 weights) = LinearInterpNodes((clock * timeMult) + hash.Z);
        Vector3 field = (StationaryNoise(uv - (drift * ((phases.X - 0.5f) * 2f)), noise) * weights.X)
            + (StationaryNoise(uv - (drift * ((phases.Y - 0.5f) * 2f)), noise) * weights.Y);
        float dy = field.Y * uvMult, dz = field.Z * uvMult;
        return new Vector3(field.X, (dy * row0.Y) + (dz * row1.Y), (dy * row0.Z) + (dz * row1.Z));
    }

    /// <summary>GetStationaryNoiseField: the noise's green and its slope by forward differences a hundredth apart.</summary>
    private static Vector3 StationaryNoise(Vector2 uv, WaterSheet noise)
    {
        const float eps = 1e-2f;
        float x = NoiseAt(noise, uv + new Vector2(eps, 0f)).Y;
        float y = NoiseAt(noise, uv + new Vector2(0f, eps)).Y;
        float centre = NoiseAt(noise, uv).Y;
        return new Vector3(centre, (x - centre) / eps, (y - centre) / eps);
    }

    /// <summary>GetBeachWaveNormalField: the two geometry waves' crests, each faded out past the shoreline.</summary>
    private static Vector3 BeachNormalField(in WavePhase one, in WavePhase other, WaterSheet profile)
        => SingleWaveNormal(one, profile) + SingleWaveNormal(other, profile);

    /// <summary>GetSingleWaveNormalField with its shore and intensity fades - the crest's height and its slope.</summary>
    /// <remarks>
    /// The break phase's slope is multiplied by nought as written ("had to ignore ... z"), so its read of the shape is left out.
    /// </remarks>
    private static Vector3 SingleWaveNormal(in WavePhase phase, WaterSheet profile)
    {
        float shoreFade = Saturated(-phase.ShorePhase.X / 0.4f);
        float intensityFade = 0.7f + (phase.IntensityMult * 0.3f);
        float coord = phase.WaveCoord.X, breaking = phase.BreakPhase.X;

        // GetGerstnerPositiveProfile: the shape's green and its rise along the wave, a texel of 256 apart.
        const float eps = 1f / 256f;
        float height = ProfileAt(profile, coord, breaking).Y;
        float rise = (ProfileAt(profile, coord + eps, breaking).Y - height) / eps;
        const float fadeWidth = 0.01f;
        float fade = Saturated(coord / fadeWidth) * Saturated(1f - ((coord - (1f - fadeWidth)) / fadeWidth));
        height *= fade;
        rise *= fade;
        float scale = BeachWaveLength * 0.5f * shoreFade * intensityFade;
        return new Vector3(height, phase.WaveCoord.Y * rise, phase.WaveCoord.Z * rise) * scale;
    }

    /// <summary>The foam's surface as beach_waves' Surface carries it.</summary>
    private struct Surface
    {
        public Vector3 Normal;
        public Vector4 Colour;
        public float Subsurface;
        public float Ao;
        public float Rough;
        public float SqrWeight;
    }

    /// <summary>GetBeachFoamSurface: each foam wave's surface, the later wave's laid over the earlier's.</summary>
    private static Surface BeachFoamSurface<TRead>(
        Vector2 place, in WavePhase one, in WavePhase other, float clock, TRead read, int colourSheet, int normalSheet, WaterSheet noise)
        where TRead : struct, IBeachTextures
    {
        Vector4 average = read.Average(colourSheet);
        Surface first = SingleWaveFoam(place, one, clock, read, colourSheet, normalSheet, noise, average);
        Surface second = SingleWaveFoam(place, other, clock, read, colourSheet, normalSheet, noise, average);
        return one.Index < other.Index ? OverlayLayered(second, first) : OverlayLayered(first, second);
    }

    /// <summary>GetSingleWaveFoamSurface as written.</summary>
    private static Surface SingleWaveFoam<TRead>(
        Vector2 place, in WavePhase phase, float clock, TRead read, int colourSheet, int normalSheet, WaterSheet noise, Vector4 average)
        where TRead : struct, IBeachTextures
    {
        float foamCoord = phase.WaveCoord.X + 0.5f;
        float shorePhase = phase.ShorePhase.X, breakPhase = phase.BreakPhase.X;
        FoamProfile profile = BeachFoamProfile(foamCoord, shorePhase, breakPhase, phase.IntensityMult);

        // GetFoamInterpNodes: four nodes a wave.
        (_, Vector2 seeds, Vector2 weights) = LinearInterpNodes(foamCoord * BeachNodesPerWave);
        seeds /= BeachNodesPerWave;
        Vector2 flowDir = Direction((phase.Dir * 0.7f) + (Direction(new Vector2(phase.WaveCoord.Y, phase.WaveCoord.Z)) * 0.3f));
        Vector2 flowVelocity = ((phase.Dir * (phase.PhaseSpeed * profile.Inherited)) + (flowDir * 0f)) * BeachWaveSpeed;

        Surface layer0 = SpaceLayer(place, seeds.X, phase.Index, flowVelocity, clock, read, colourSheet, normalSheet, average);
        Surface layer1 = SpaceLayer(place, seeds.Y, phase.Index, flowVelocity, clock, read, colourSheet, normalSheet, average);
        Surface surface = Blend(layer0, layer1, weights.Y);

        float intensity = Saturated(0f + profile.Intensity) * Saturated(5f * phase.InterpRatio);
        float sprayFade = Saturated(1f - ((phase.WaveCoord.X - (0.5f + profile.SprayStart)) / (profile.SprayEnd - profile.SprayStart)));
        Vector3 waveHash = Hash33(new Vector3(phase.Index, 1f, 0f));
        float beachNoise = NoiseAt(noise, (place * 3e-3f) + new Vector2(waveHash.X, waveHash.Y)).X;
        float dissipation = Lerp(-5f, 1f, MathSmoothStep(beachNoise, 0.5f, 0.5f));
        float opacity = RampPow(sprayFade, 0.8f) * RampPow(Saturated(1f - shorePhase), dissipation);

        // DissolveLayeredSurface.
        float threshold = 1f - surface.Normal.X;
        float surfaceFade = MathSmoothStep(intensity * opacity, threshold, profile.Sharpness);
        float subsurfaceFade = MathSmoothStep(opacity, threshold, profile.Sharpness);
        surface.Colour.W *= surfaceFade;
        surface.Normal *= 1f - Saturated(1f - (intensity * opacity));
        surface.Rough *= surfaceFade;
        surface.Subsurface *= subsurfaceFade;
        return surface;
    }

    /// <summary>One space layer of a wave's foam: GetFoamScrollingLayer at the layer's own scale and pace, its variance kept.</summary>
    private static Surface SpaceLayer<TRead>(
        Vector2 place, float seed, int index, Vector2 flowVelocity, float clock, TRead read, int colourSheet, int normalSheet, Vector4 average)
        where TRead : struct, IBeachTextures
    {
        float scaleMult = MathF.Pow(Saturated(seed), 3f);
        float timeFreq = Lerp(0.5f, 3f, scaleMult);
        float spaceFreq = 1.8e-3f * Lerp(0.75f, 1.2f, scaleMult);
        float layerSeed = (index * BeachNodesPerWave) + seed;
        Surface layer = ScrollingLayer(place * spaceFreq, flowVelocity * spaceFreq / timeFreq, clock * timeFreq, layerSeed, read, colourSheet, normalSheet);
        layer.Subsurface = 1f;

        // PreserveVariance: two layers crossfading are flatter than one; their spread from the mean is scaled back up.
        layer.Colour = ((layer.Colour - average) / MathF.Sqrt(layer.SqrWeight)) + average;
        return layer;
    }

    /// <summary>
    /// GetFoamScrollingLayer: two layers crossfading over the phase, each turned by its hash and slid along the flow - the
    /// turbulence's offset nought (see <see cref="Beaching"/>).
    /// </summary>
    private static Surface ScrollingLayer<TRead>(
        Vector2 uv, Vector2 velocity, float phase, float layerSeed, TRead read, int colourSheet, int normalSheet)
        where TRead : struct, IBeachTextures
    {
        (Vector2 phases, Vector2 seeds, Vector2 weights) = LinearInterpNodes(phase);
        Surface surface = default;
        for (var layer = 0; layer < 2; layer++)
        {
            float local = layer == 0 ? phases.X : phases.Y, weight = layer == 0 ? weights.X : weights.Y;
            Vector3 hash = Hash33(new Vector3(layer == 0 ? seeds.X : seeds.Y, layerSeed, 0f));
            float angle = 3.1415f * 2f * hash.Z;
            float s = MathF.Sin(angle), c = MathF.Cos(angle);
            Vector2 flow = uv - (velocity * ((local * 2f) - 1f)) + new Vector2(hash.X, hash.Y);

            // mul(v, float2x2(c, s, -s, c)), and the gradient back through its transpose.
            var turned = new Vector2((flow.X * c) - (flow.Y * s), (flow.X * s) + (flow.Y * c));
            Vector4 colour = read.Read(colourSheet, turned);
            Vector4 bumps = read.Read(normalSheet, turned);
            Vector2 back = RgToGradYFix(bumps.X, bumps.Y);
            var world = new Vector2((back.X * c) + (back.Y * s), (back.Y * c) - (back.X * s));
            surface.Colour += colour * weight;
            surface.Normal += new Vector3(colour.W, world.X, world.Y) * weight;
            surface.Ao += bumps.Z * weight;
            surface.Rough += bumps.W * weight;
            surface.Subsurface += ((colour.W * 0.5f) + 0.5f) * weight;
            surface.SqrWeight += weight * weight;
        }

        return surface;
    }

    /// <summary>BlendSurfaces.</summary>
    private static Surface Blend(in Surface one, in Surface other, float share)
    {
        Surface blended = default;
        blended.Colour = Vector4.Lerp(one.Colour, other.Colour, share);
        blended.Normal = Vector3.Lerp(one.Normal, other.Normal, share);
        blended.Ao = Lerp(one.Ao, other.Ao, share);
        blended.Rough = Lerp(one.Rough, other.Rough, share);
        blended.Subsurface = Lerp(one.Subsurface, other.Subsurface, share);
        blended.SqrWeight = (one.SqrWeight * (1f - share) * (1f - share)) + (other.SqrWeight * share * share);
        return blended;
    }

    /// <summary>OverlayLayeredSurfaces: the top over the bottom by its alpha, their normals' fields added.</summary>
    private static Surface OverlayLayered(in Surface bottom, in Surface top)
    {
        float a0 = bottom.Colour.W, a1 = top.Colour.W;
        float alpha = Saturated(1f - ((1f - a0) * (1f - a1)));
        float inv = 1f / (alpha + 1e-5f);
        Vector3 rgb = Vector3.Lerp(new Vector3(bottom.Colour.X, bottom.Colour.Y, bottom.Colour.Z) * a0, new Vector3(top.Colour.X, top.Colour.Y, top.Colour.Z), a1) * inv;
        Surface laid = default;
        laid.Colour = new Vector4(rgb, alpha);
        laid.Normal = bottom.Normal + top.Normal;
        laid.Ao = Lerp(bottom.Ao * a0, top.Ao, a1) * inv;
        laid.Rough = Lerp(bottom.Rough * a0, top.Rough, a1) * inv;
        laid.Subsurface = 1f - ((1f - bottom.Subsurface) * (1f - top.Subsurface));
        return laid;
    }

    /// <summary>GetBeachFoamProfile's: how the foam inherits the wave's speed, how much of it there is, how sharp, and where its spray runs.</summary>
    private readonly record struct FoamProfile(float Inherited, float Intensity, float Sharpness, float SprayStart, float SprayEnd);

    /// <summary>GetBeachFoamProfile as written.</summary>
    private static FoamProfile BeachFoamProfile(float foamCoord, float shorePhase, float breakPhase, float intensityMult)
    {
        // GetBeachFoamVelocity: the spray thrown ahead at the break, the shore's own pace once over the line.
        float sprayVelocity = 1.5f * MathF.Pow(Saturated(foamCoord / 0.95f), 20f);
        float over = Saturated((foamCoord - 1f) * 10f);
        sprayVelocity *= 0.7f + (0.3f * (1f - (over * over)));
        sprayVelocity *= PolyStep(breakPhase, 0.5f, 0.9f);
        float shoreVelocity = Lerp(1.2f * MathF.Pow(Saturated(foamCoord / 0.95f), 3f), 1f, Saturated((shorePhase - 0.2f) / 0.8f));
        float inherited = Lerp(sprayVelocity, shoreVelocity, Saturated((shorePhase + 0.25f) * 5f));

        float sprayPhase = Saturated((breakPhase - 0.5f) * 2f);
        float spraySharpness = Lerp(0.6f, 0.9f, sprayPhase);
        float sharpness = Lerp(0.6f, spraySharpness, Saturated((foamCoord - 0.9f) * 5f));
        sharpness = Lerp(sharpness, 0.4f, Saturated(shorePhase));

        float sprayWidthScale = (sprayPhase * Lerp(0.4f, 0.6f, intensityMult)) + 0.1f;
        float maxOffset = Lerp(0.5f, 1f, intensityMult);
        float sprayOffsetScale = Lerp(0f, maxOffset, sprayPhase);
        float slide = Saturated((shorePhase - -0.2f) * 5f);
        sprayOffsetScale *= Lerp(1f, 0.75f, slide);
        sprayWidthScale = Lerp(sprayWidthScale, 0.1f, slide);
        sprayWidthScale = Lerp(sprayWidthScale, 0.3f, PolyStep(Saturated(shorePhase), 0.6f, 0.8f));
        float sprayStart = BeachSprayWidth * (1f - sprayWidthScale) * sprayOffsetScale;
        float sprayEnd = sprayStart + (sprayWidthScale * BeachSprayWidth);
        float intensity = BeachFoamIntensity(foamCoord, foamCoord - sprayEnd, shorePhase, breakPhase, intensityMult);
        return new FoamProfile(inherited, intensity, sharpness, sprayStart, sprayEnd);
    }

    /// <summary>GetBeachFoamIntensity as written - SmoothStepSimple's <c>3x² - 2x²</c> too.</summary>
    private static float BeachFoamIntensity(float foamCoord, float sprayCoord, float shorePhase, float breakPhase, float intensityMult)
    {
        const float hump = 0.9f;
        float breakFoam = PolyStep(breakPhase, 0.53f, 0.95f);
        float residualFoam = Saturated((shorePhase - -0.4f) * 5f);
        float shoreFade = RampPow(Saturated(1f - shorePhase), 4f);
        float spike = 0.2f * breakFoam * shoreFade;
        float tail = 0.1f * breakFoam * shoreFade * (0f + (1f * intensityMult));
        float residual = 0.8f * residualFoam;
        float constant = 0.3f * breakFoam * (0.7f + (0.3f * intensityMult));
        float humped = MathF.Pow(Saturated(foamCoord / hump), 4f);
        float intensity = ((3f * humped * humped) - (2f * humped * humped)) * spike;
        intensity += MathF.Pow(Saturated(foamCoord / hump), 3f) * tail;
        intensity += constant;
        intensity += residual * MathF.Pow(Saturated(sprayCoord), 20f);
        return Lerp(intensity, (intensity * 0.8f) + 0.2f, Saturated(breakPhase));
    }

    /// <summary>
    /// BreakingWavesFragment's ripples: GetSurfaceRipplesField's three octaves of the ripple texture, drifting on the noise - their slope.
    /// </summary>
    private static Vector2 BeachRipples<TRead>(Vector2 place, float clock, TRead read, int rippleSheet, WaterSheet noise)
        where TRead : struct, IBeachTextures
    {
        const float worldToUv = 0.002f, worldToNoise = 4e-3f, noiseTimeMult = 0.15f, timeFreq = 0.25f;
        float phaseNoise = NoiseAt(noise, place * 1e-3f).X;

        // SampleFlowingTexture, its flow nought, a period of five and seed nought: two reads of the noise crossfading.
        (_, Vector2 seeds, Vector2 weights) = LinearInterpNodes(clock * noiseTimeMult * (1f / 5f));
        Vector3 h0 = Hash33(new Vector3(seeds.X, 0f, 0f)), h1 = Hash33(new Vector3(seeds.Y, 0f, 0f));
        Vector2 at = place * worldToNoise;
        Vector2 following = (NoiseAt(noise, at + new Vector2(h0.X, h0.Y)) * weights.X) + (NoiseAt(noise, at + new Vector2(h1.X, h1.Y)) * weights.Y);
        Vector2 turbulent = (following - new Vector2(0.5f)) * 60f;
        const float velocityMult = 50f * worldToUv / timeFreq;
        var velocityCoeffs = new Vector2(0.5f * velocityMult * velocityMult, 0.02f * velocityMult * velocityMult);
        Vector2 flow = turbulent * worldToUv / timeFreq;
        float phase = (clock * timeFreq) + phaseNoise;
        const float amp = 0.5f;

        Vector2 uv = place * worldToUv;
        Vector2 grad = Vector2.Zero;
        for (var octave = 0; octave < 3; octave++)
        {
            Vector3 octaveSeed = HashStable(new Vector3(0f, 0f, octave));
            float octaveScale = MathF.Pow(2f, octave);
            (Vector2 phases, Vector2 nodeSeeds, Vector2 nodeWeights) = LinearInterpNodes(phase + octaveSeed.Z);
            float speed = MathF.Sqrt((velocityCoeffs.X / octaveScale) + (velocityCoeffs.Y * octaveScale));
            float ampMult = 1f / (1f + (0.5f * octaveScale));
            for (var node = 0; node < 2; node++)
            {
                Vector3 nodeSeed = HashStable(new Vector3(node == 0 ? nodeSeeds.X : nodeSeeds.Y, octave, 0f));
                float velocityAngle = nodeSeed.Z * 2f * 3.1415f;
                Vector2 rippleVelocity = new Vector2(MathF.Cos(velocityAngle), MathF.Sin(velocityAngle)) * speed;
                float turn = nodeSeed.Y * 2f * 3.1415f;
                float s = MathF.Sin(turn), c = MathF.Cos(turn);
                float local = node == 0 ? phases.X : phases.Y;
                Vector2 nodeUv = (uv * octaveScale) + new Vector2(nodeSeed.X, nodeSeed.Y) - ((flow + rippleVelocity) * octaveScale * ((local * 2f) - 1f));

                // mul(nodeUv, GetRotationMat2(turn)), and the gradient back through its transpose.
                var turned = new Vector2((nodeUv.X * c) - (nodeUv.Y * s), (nodeUv.X * s) + (nodeUv.Y * c));
                Vector4 sample = read.Read(rippleSheet, turned);
                Vector2 back = RgToGradYFix(sample.X, sample.Y);
                grad += new Vector2((back.X * c) + (back.Y * s), (back.Y * c) - (back.X * s)) * ampMult * (node == 0 ? nodeWeights.X : nodeWeights.Y);
            }
        }

        return grad * amp;
    }

    /// <summary>GetInterpIndices: the two nodes' indices about a phase.</summary>
    private static (int First, int Second) InterpIndices(float phase)
    {
        float x = 0.5f + (phase * 0.5f), y = phase * 0.5f;
        return ((int)MathF.Floor((MathF.Floor(x) * 2f) + 0.5f), (int)MathF.Floor((MathF.Floor(y) * 2f) + 1f + 0.5f));
    }

    /// <summary>
    /// The wave shape's red and green at a place, held at its edges, the four nearest texels' centres mixed - SamplerLinearClamp at
    /// level nought, as the card's four loads read it.
    /// </summary>
    internal static Vector2 ProfileAt(WaterSheet sheet, float u, float v)
    {
        int width = sheet.Width, height = sheet.Height;
        float sx = (u * width) - 0.5f, sy = (v * height) - 0.5f;
        if (!float.IsFinite(sx) || !float.IsFinite(sy))
        {
            return Vector2.Zero;
        }

        sx = Math.Clamp(sx, 0f, width - 1);
        sy = Math.Clamp(sy, 0f, height - 1);
        int x0 = (int)sx, y0 = (int)sy;
        int x1 = Math.Min(x0 + 1, width - 1), y1 = Math.Min(y0 + 1, height - 1);
        float fx = sx - x0, fy = sy - y0;
        float[] texels = sheet.Texels;
        int a = ((y0 * width) + x0) * 4, b = ((y0 * width) + x1) * 4;
        int c = ((y1 * width) + x0) * 4, d = ((y1 * width) + x1) * 4;
        var ta = new Vector2(texels[a], texels[a + 1]);
        var tb = new Vector2(texels[b], texels[b + 1]);
        var tc = new Vector2(texels[c], texels[c + 1]);
        var td = new Vector2(texels[d], texels[d + 1]);
        Vector2 top = ta + ((tb - ta) * fx);
        Vector2 bottom = tc + ((td - tc) * fx);
        return top + ((bottom - top) * fy);
    }

    /// <summary>
    /// The noise's red and green at coordinates, wrapping, the four nearest texels' centres mixed - SamplerLinearWrap at level nought,
    /// as the card's four loads read it.
    /// </summary>
    internal static Vector2 NoiseAt(WaterSheet sheet, Vector2 uv)
    {
        int width = sheet.Width, height = sheet.Height;
        float sx = (uv.X * width) - 0.5f, sy = (uv.Y * height) - 0.5f;
        float fx0 = MathF.Floor(sx), fy0 = MathF.Floor(sy);
        if (!float.IsFinite(fx0) || !float.IsFinite(fy0))
        {
            return new Vector2(0.5f);
        }

        float fx = sx - fx0, fy = sy - fy0;
        int x0 = ((((int)fx0) % width) + width) % width, y0 = ((((int)fy0) % height) + height) % height;
        int x1 = (x0 + 1) % width, y1 = (y0 + 1) % height;
        float[] texels = sheet.Texels;
        int a = ((y0 * width) + x0) * 4, b = ((y0 * width) + x1) * 4;
        int c = ((y1 * width) + x0) * 4, d = ((y1 * width) + x1) * 4;
        var ta = new Vector2(texels[a], texels[a + 1]);
        var tb = new Vector2(texels[b], texels[b + 1]);
        var tc = new Vector2(texels[c], texels[c + 1]);
        var td = new Vector2(texels[d], texels[d + 1]);
        Vector2 top = ta + ((tb - ta) * fx);
        Vector2 bottom = tc + ((td - tc) * fx);
        return top + ((bottom - top) * fy);
    }

    /// <summary>A direction at length one, or nought where it has none - normalize's, guarded where HLSL's would not be.</summary>
    private static Vector2 Direction(Vector2 v)
    {
        float length = v.Length();
        return length > 1e-12f ? v / length : Vector2.Zero;
    }

    /// <summary>The engine's hash33Stable: the three numbers' bits through PCG's rounds (hash33UintPcg), each as HashUToF makes it a fraction.</summary>
    public static Vector3 HashStable(Vector3 value)
    {
        uint x = BitConverter.SingleToUInt32Bits(value.X), y = BitConverter.SingleToUInt32Bits(value.Y), z = BitConverter.SingleToUInt32Bits(value.Z);
        unchecked
        {
            x = (x * 1664525u) + 1013904223u;
            y = (y * 1664525u) + 1013904223u;
            z = (z * 1664525u) + 1013904223u;
            x += y * z;
            y += z * x;
            z += x * y;
            x ^= x >> 16;
            y ^= y >> 16;
            z ^= z >> 16;
            x += y * z;
            y += z * x;
            z += x * y;
        }

        return new Vector3(HashUnit(x), HashUnit(y), HashUnit(z));
    }

    /// <summary>math_utils' RampPow.</summary>
    internal static float RampPow(float value, float power)
        => power > 0f ? MathF.Pow(Saturated(value), 1f + power) : 1f - MathF.Pow(Saturated(1f - value), 1f - power);

    /// <summary>math_utils' PolyStep.</summary>
    internal static float PolyStep(float value, float center, float steepness)
    {
        center = Math.Clamp(center, 1e-2f, 1f - 1e-2f);
        const float most = 100f;
        float power = Math.Clamp(1f / (1f - steepness), -most, most);
        float k = Math.Clamp(0.5f * power / MathF.Min(center, 1f - center), 0f, most);
        float a = MathF.Min(2f * k * center, most);
        float b = MathF.Min(2f * k * (1f - center), most);
        return 0.5f - (0.5f * MathF.Pow(Saturated(1f - ((value - center) / (1f - center))), b)) + (0.5f * MathF.Pow(Saturated(value / center), a));
    }
}
