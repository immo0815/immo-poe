using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>The engine's water, as a program - see <see cref="Water"/>.</summary>
public sealed partial class ShadeProgram
{
    /// <summary>What the water program is named in a picture's lines.</summary>
    public const string WaterGraph = "the engine's water pass, drawn as the area's open water (WaterLook)";

    /// <summary>
    /// The shader type the engine draws in its own water pass rather than through the material's graphs.
    /// </summary>
    /// <remarks>
    /// <c>nvidian_Ocean.mat</c> - Port's harbour's 31 WaterPlane3x3 doodads - says <c>"shadertype":"Ocean"</c>
    /// beside <c>"splashywater":true</c>, and its graphs write no colour of their own the picture can draw:
    /// the colour is the pass's (common.ffx: PASS_TYPE_WATER, water_color_open, water_color_terrain).
    /// </remarks>
    public const string OceanShader = "Ocean";

    /// <summary>
    /// How fast the water thins out towards the floor behind it, a unit of water at a time: the engine's water pass's own number.
    /// </summary>
    /// <remarks>
    /// internalonly.ffx's WaterComputeRefraction hands water.ffx's BuildWaterRefraction <c>0.03</c> as its
    /// <c>water_to_terrain_mult</c>, and that makes <c>water_to_terrain_ratio = saturate(1 - gap * 0.03 * lerp(1, 30, ground_water_ratio))</c>,
    /// the gap the distance along the view from the surface to the solid behind (DepthDistance's). The density the medium
    /// absorbs with is multiplied by <c>1 - water_to_terrain_ratio</c>, and a fragment whose ratio reaches one is discarded:
    /// the water fades to nothing within 33 units of the floor. The ratio's other term is nought on water drawn this way:
    /// GetFlowingSurface's planar state (GetFlowingWaterPlanarStateWorld) leaves both its ground_water_ratio and its own
    /// water_to_terrain_ratio at nought, so the shore's thirtyfold is not taken.
    /// </remarks>
    public const float ShoreFade = 0.03f;

    /// <summary>
    /// Open water as a medium: the area's colour, covering by how much water the view crosses before the solid surface behind it, its pattern moving with the flow.
    /// </summary>
    /// <remarks>
    /// THE ENGINE'S MEDIUM, cut down to what the picture can say. water.ffx's GetRefractedColor lights the
    /// medium with <c>water_color_open.rgb</c> and lets through <c>exp(-distance * density)</c> of what lies
    /// behind, the density being <c>water_color_open.a</c>; here the distance is the depth from the water's
    /// surface to the solid one behind it along the view (DepthDistance's, see <see cref="Op.Depth"/>), the
    /// colour and density the area's (<see cref="WaterColour"/>), lit as any colour is, and what gets
    /// through is the picture behind it showing through the mix. Where nothing solid lies behind, the
    /// water covers whole. Near the floor the density thins as the engine thins it (<see cref="ShoreFade"/>),
    /// so a shore's edge fades out rather than standing as a band of colour.
    ///
    /// THE PASS'S OWN FORMULA. The water pass (internalonly.ffx's WaterComputeSurface and WaterComputeRefraction,
    /// water.ffx's BuildWaterRefraction and GetRefractedWaterLight) mixes <c>opaque * r + absorbed * (1 - r)</c>,
    /// <c>r = exp(-travel * density)</c>; what it adds beyond this program is a Fresnel reflection weighted by the
    /// area's reflectiveness, the sun's highlight, the environment's cube and screen-space reflections, the
    /// refraction (index 1.33) bending the ray it measures the travel along, foam, and the waves' normals.
    ///
    /// THE COLOUR'S PATTERN IS CHOSEN: value noise on the surface, a cell every 120 units, its brightness
    /// from 0.85 to 1.15 of the colour, drifting at the area's flow - a moving surface that reads as water,
    /// not measured, and still where an area's flow is nought.
    ///
    /// THE WAVES ARE THE GLOSSY TWIN'S (<see cref="Glossy"/>, <see cref="HasWaves"/>): the same colour, and the
    /// surface the engine lights - the ocean's own (<see cref="Ocean"/>: GetWindRippleSurfaceField3's three
    /// octaves over its ripple texture, <see cref="Ripples"/>, in the area's wind) - handed to the game light's
    /// glints and reflection, its specular the area's reflectiveness and its gloss the engine's 0.99. Under the
    /// picture's lamp the twin draws as this program does. Not drawn: foam, the refraction's bend.
    /// </remarks>
    public static ShadeProgram Water()
    {
        var build = new Builder();

        // HOW MUCH WATER THE VIEW CROSSES: the solid surface behind less the water's own depth, never less than nought.
        int crossed = build.Emit(Op.Max, build.Emit(Op.Depth, Position), build.Constant(Vector4.Zero));

        // THE SHORE: the density thinned within 33 units of the floor, 1 - water_to_terrain_ratio.
        int shore = build.Emit(Op.Saturate, build.Emit(Op.Multiply, crossed, build.Constant(new Vector4(ShoreFade))));

        // WHAT GETS THROUGH, exp(-distance * density), and the cover is the rest.
        int density = build.Emit(Op.Multiply, build.Emit(Op.Swizzle, WaterColour, extra: AllFrom(3)), shore);
        int travelled = build.Emit(Op.Negate, build.Emit(Op.Multiply, crossed, density));
        int through = build.Emit(Op.Power, build.Constant(new Vector4(MathF.E)), travelled);
        int cover = build.Emit(Op.OneMinus, through);

        // THE PATTERN, drifting at the flow: x and y off the surface, z the time - all of it still at a flow of nought.
        int flow = build.Emit(Op.Swizzle, WaterFlow, extra: AllFrom(0));
        int rate = build.Emit(Op.Multiply, flow, build.Constant(new Vector4(0.15f, 0.1f, 0.2f, 0f)));
        int surface = build.Emit(Op.Multiply, Position, build.Constant(new Vector4(1f / 120f, 1f / 120f, 0f, 0f)));
        int spot = build.Emit(Op.MultiplyAdd, Clock, rate, surface);
        int pattern = build.Emit(Op.Noise, spot, extra: 0);
        int shimmer = build.Emit(Op.MultiplyAdd, pattern, build.Constant(new Vector4(0.3f)), build.Constant(new Vector4(0.85f)));

        // THE COLOUR, its w the cover.
        int colour = build.Emit(Op.Multiply, WaterColour, shimmer);
        build.Into(colour, cover, Only(3, 0));
        ShadeProgram water = Finish(build, colour, -1, -1, [WaterGraph], hasAlpha: true, plainable: false)
            ?? throw new InvalidOperationException("the water program does not fit its registers");

        // THE WAVES, for the game light's glints and reflection: the same colour, and the surface the
        // engine lights - the ocean's ripples in the area's wind, its specular the area's reflectiveness,
        // its gloss the engine's (InitWaterSurfaceData) - see HasWaves.
        int waves = build.Emit(Op.Waves, Position, WaterWind, Clock, WaterShore, WaterSwell);
        int specular = build.Emit(Op.Swizzle, WaterFlow, extra: AllFrom(1));
        int gloss = build.Constant(new Vector4(WaterGloss));
        water.Glossy = Finish(build, colour, specular, gloss, [WaterGraph], hasAlpha: true, plainable: false, waves)
            ?? throw new InvalidOperationException("the water's waves do not fit their registers");
        return water;
    }

    /// <summary>
    /// The water's glossiness, the engine's: InitWaterSurfaceData's <c>lerp(0.99, 0, foam)</c> where no foam lies.
    /// </summary>
    public const float WaterGloss = 0.99f;

    /// <summary>The ripple texture's side in texels - the engine's <c>textureSize</c> (GetWaterVolumeStateFieldOctave).</summary>
    public const int RippleSide = 128;

    /// <summary>
    /// How many texels of the stand-in ripple texture one noise cell spans - CHOSEN, for where the engine's does not read.
    /// </summary>
    /// <remarks>
    /// THE ENGINE'S SURFACE IS ONE TEXTURE READ OCTAVE AFTER OCTAVE: <c>surface_tex_sampler</c>, "ripple heightfield and
    /// gradient", a pass texture the engine binds itself, 128 texels a tile (GetWaterVolumeStateFieldOctave), its red the
    /// height and its green and blue the height's slope a texel. No shader source names its file; the game's program does -
    /// <c>Art/2DArt/Lookup/surface_ripple.dds</c>, 128 square, four floats a texel (the capture's water textures part,
    /// v0.1.228) - and <see cref="Use"/> hands it over as the install opens (WaterLookups). Where it does not read, it stands
    /// as a texture of gradient noise read the same way - the height <c>(r - 0.4) * 3</c>, the slope three times the red's a
    /// texel, four texels a unit of the octave's coordinates - a cell every sixteen texels, eight across the tile and wrapping
    /// with it.
    /// </remarks>
    public const float RippleCell = 16f;

    /// <summary>The largest a two-dimensional gradient noise of unit gradients reaches, <c>sqrt(2) / 2</c> - what takes it to nought to one.</summary>
    private const float RippleRange = 0.70710677f;

    /// <summary>What the stand-in ripple texture is said to be under a picture.</summary>
    private const string RippleStandIn = "the ripples over a noise of this tool's - the engine's ripple texture not read";

    /// <summary>The stand-in swell cross-section's line.</summary>
    private const string SwellStandIn = "the swell's cross-section a trochoid of this tool's - the engine's not read";

    /// <summary>The ripple texture read: the stand-in until <see cref="Use"/> hands over the engine's.</summary>
    private static WaterSheet s_ripples = new(RippleSide, RippleSide, 4, Rippled(), false, RippleStandIn);

    /// <summary>The swell's cross-section read: the stand-in until <see cref="Use"/> hands over the engine's.</summary>
    private static WaterSheet s_swells = new(SwellPhases, SwellShores, 2, Swelled(), false, SwellStandIn);

    /// <summary>
    /// The ripple texture the water reads, 128 by 128 texels, row by row, four floats each - the height's red, its slope a texel
    /// along x and y, and a fourth: the engine's (WaterLookups) once handed over, this tool's stand-in until then.
    /// </summary>
    public static WaterSheet Ripples => Volatile.Read(ref s_ripples);

    /// <summary>
    /// The swell's cross-section the water reads, row by row from the open sea to the shore, two floats a texel - its height and that
    /// height's rise a unit of phase: the engine's (WaterLookups) once handed over, this tool's stand-in until then.
    /// </summary>
    public static WaterSheet Swells => Volatile.Read(ref s_swells);

    /// <summary>The ripple texture's texels, as <see cref="Ripples"/> holds them.</summary>
    public static ReadOnlySpan<float> RippleSheet => Ripples.Texels;

    /// <summary>
    /// Hands the water the engine's own lookups where they were taken - once, as the install opens, before anything is drawn; the
    /// card makes its textures again when it sees them change.
    /// </summary>
    public static void Use(WaterLookups lookups)
    {
        ArgumentNullException.ThrowIfNull(lookups);

        // WHERE THE ENGINE'S IS NOT TAKEN the stand-in stays, saying why.
        Volatile.Write(ref s_ripples, lookups.Ripples is { Width: RippleSide, Height: RippleSide, Channels: 4 } ripples
            ? ripples
            : Ripples with { Said = lookups.RippleSaid });
        Volatile.Write(ref s_swells, lookups.Swells is { Channels: 2, Width: > 0, Height: > 0 } swells
            ? swells
            : Swells with { Said = lookups.SwellSaid });
    }

    /// <summary>
    /// The engine's ocean surface at a place: its height, and its slope along x and y - GetOceanWaterPlanarState's, its swell over the room's shore and the wind's ripples on top.
    /// </summary>
    /// <param name="place">Where on the water, in world units.</param>
    /// <param name="wind">The area's wind - WaterLook.WindRegister.</param>
    /// <param name="swell">The area's swell - WaterLook.SwellRegister.</param>
    /// <param name="clock">The clock, in seconds.</param>
    /// <param name="shore">The room's shore, or null where there is none - the swell then stands still and the ripples run whole.</param>
    /// <param name="slope">The height's rise along x and y.</param>
    /// <remarks>
    /// THE OCEAN'S PATH, NOT THE RIVER'S. The engine draws its water through two surfaces (internalonly.ffx):
    /// WaterComputeSurface, GetTotalWaveSurfaceNumerical over GetOceanWaterPlanarState, and
    /// WaterComputeFlowingSurface, GetFlowingSurface over GetFlowSurfaceField - eight octaves that slide
    /// along the flow and fade back to their start every 1.4 to 3 seconds. The second was drawn first
    /// (v0.1.221), and at Port's flow it read as a two-second film looped (said from the live client,
    /// v0.1.228) - while the game draws crests rolling in to the harbour. The ocean's own reads no flow at
    /// all. Which surface an "Ocean" material takes is in no file read; the names and the game's picture say this one.
    ///
    /// THE SWELL AS WRITTEN (GetTidalWaterPlanarState, a beach): the tide's phase from the distance out to sea, 25
    /// periods over 2 pi a colour unit, rolling in at 1.1 a second; a smooth noise of the clock wobbling it; slowed and
    /// hurried within a quarter of a colour unit of the shore; its shape read from the cross-section at that phase and
    /// the shore's distance (<see cref="Swell"/>), times the swell's height, less over ground under water; its slope the
    /// cross-section's own along the phase's (<see cref="Swells"/>: the engine's profile and gradient once handed over). The
    /// ripples on top thinned to nothing at the shoreline, <c>1 - (1 - |shoreDist|)^10</c>, and twice the ripples' own. NOT
    /// DRAWN: the swell's foam and the orbit that carries the ripples to and fro with it (the cross-section's green, blue and
    /// alpha, and the distortion texture), and the rocky shore's clashing state - the shore is all beach.
    /// </remarks>
    public static float Ocean(Vector2 place, Vector4 wind, Vector4 swell, float clock, ShoreField? shore, out Vector2 slope)
    {
        float height = 0f;
        slope = Vector2.Zero;
        float ripples = 2f;
        var groundWater = false;
        if (shore is not null)
        {
            shore.Fields(place, out float shoreDist, out float oceanDist, out Vector2 oceanSlope);
            const float pi = 3.1415f;
            float swellHeight = swell.X, period = swell.Y, intensity = swell.Z;
            float periods = 1f / period;
            float underGround = 1f - Saturated(1f + ((shoreDist + 0.05f) * 10f));
            float tide = 1f - Fractional((((1f - oceanDist) * periods) + (clock * 1.1f / (period * 25f))) / (2f * pi));

            // THE SHORE'S LENGTH IS NOUGHT here, so the noise runs on the clock alone and the swell's angle adds nothing.
            float noise = (SmoothNoise(clock * 0.1f, clock * 0.15f) * 0.5f) + 0.5f;
            float reduction = Saturated(noise);
            float slowdown = 1f - (shoreDist * 4f), hurry = 1f - (shoreDist * 5.5f);
            float offset = (MathF.Pow(Saturated(slowdown), 1.1f) * 0.5f) - (MathF.Pow(Saturated(hurry), 1.1f) * 0.75f);
            tide = Fractional((noise * 0.2f) + tide + (offset * 0.8f) + 10f);
            float effective = Saturated(1f - shoreDist);
            effective = MathF.Pow(effective, 1f / ((Saturated(1f - (reduction * 0.4f)) * intensity * 0.8f) + 0.2f));
            float cross = Swell(tide, effective, out float rise);

            // (-oceanDist.gradient) / periodColorLength, the field's slope being given negated.
            Vector2 phaseSlope = -oceanSlope * (periods / (2f * pi));
            float kept = 1f - underGround;
            height = (cross - 0.5f) * swellHeight * kept;
            slope = rise * phaseSlope * swellHeight * kept;
            groundWater = underGround > 0.5f;
            ripples = (1f - MathF.Pow(Saturated(1f - MathF.Abs(shoreDist)), 10f)) * 2f;
        }

        height += WindRipples(place, wind, clock, groundWater, out Vector2 rippleSlope) * ripples;
        slope += rippleSlope * ripples;
        return height;
    }

    /// <summary>How many phases the stand-in swell cross-section is drawn at - its width.</summary>
    public const int SwellPhases = 256;

    /// <summary>How many distances from the shore the stand-in is drawn at - its height.</summary>
    public const int SwellShores = 16;

    /// <summary>
    /// The swell's cross-section at a phase and a distance from the shore: its height, nought to one, and that height's rise a unit of phase - the engine's <c>wave_data_sampler</c> and <c>wave_gradient_sampler</c>.
    /// </summary>
    /// <remarks>
    /// READ AS THE ENGINE READS ITS OWN (SamplerLinearWrapClampWrap at level nought): wrapping along the phase, held at the ends
    /// along the shore's distance, the four nearest texels' centres mixed, the sheet's own size whatever it is - the card's
    /// ShadeSwellAt the same arithmetic. The engine's two lookups (WaterLookups: a profile's red and wave_profile_gradient.dds's)
    /// once taken; until then, or where they do not read, a trochoid CHOSEN: its crest sharpening from a steepness of 0.2 out at
    /// sea to 0.8 at the shore and its height growing from half to whole, crest at the half phase.
    /// </remarks>
    internal static float Swell(float phase, float shore, out float rise) => Swell(Swells, phase, shore, out rise);

    /// <summary>The same, of a given cross-section.</summary>
    public static float Swell(WaterSheet sheet, float phase, float shore, out float rise)
    {
        ArgumentNullException.ThrowIfNull(sheet);
        int width = sheet.Width, height = sheet.Height;
        float sx = (phase * width) - 0.5f;
        float sy = Math.Clamp((shore * height) - 0.5f, 0f, height - 1);
        float fx0 = MathF.Floor(sx);
        float fx = sx - fx0;
        int x0 = (((int)fx0 % width) + width) % width, x1 = (x0 + 1) % width;
        int y0 = (int)sy, y1 = Math.Min(y0 + 1, height - 1);
        float fy = sy - y0;
        float[] texels = sheet.Texels;
        int a = ((y0 * width) + x0) * 2, b = ((y0 * width) + x1) * 2;
        int c = ((y1 * width) + x0) * 2, d = ((y1 * width) + x1) * 2;
        float Mixed(int part)
        {
            float top = texels[a + part] + ((texels[b + part] - texels[a + part]) * fx);
            float bottom = texels[c + part] + ((texels[d + part] - texels[c + part]) * fx);
            return top + ((bottom - top) * fy);
        }

        rise = Mixed(1);
        return Mixed(0);
    }

    /// <summary>The cross-section made: at each phase the trochoid's angle found by Newton's method, its height and that height's rise along the phase.</summary>
    private static float[] Swelled()
    {
        var texels = new float[SwellPhases * SwellShores * 2];
        for (var row = 0; row < SwellShores; row++)
        {
            float near = (row + 0.5f) / SwellShores;
            float steep = 0.2f + (0.6f * near), tall = 0.5f + (0.5f * near);
            for (var column = 0; column < SwellPhases; column++)
            {
                // phase = (angle - steep * sin(angle)) / 2 pi, the crest at the half.
                double phase = (column + 0.5) / SwellPhases, angle = phase * 2.0 * Math.PI;
                for (var step = 0; step < 12; step++)
                {
                    angle -= (angle - (steep * Math.Sin(angle)) - (phase * 2.0 * Math.PI)) / (1.0 - (steep * Math.Cos(angle)));
                }

                int at = ((row * SwellPhases) + column) * 2;
                texels[at] = (float)(0.5 - (tall * 0.5 * Math.Cos(angle)));
                texels[at + 1] = (float)(tall * Math.PI * Math.Sin(angle) / (1.0 - (steep * Math.Cos(angle))));
            }
        }

        return texels;
    }

    /// <summary>GetSmoothNoise's value: a smooth step between four of Hash's corners, from minus one to one as the engine returns it.</summary>
    internal static float SmoothNoise(float x, float y)
    {
        float ix = MathF.Floor(x), iy = MathF.Floor(y);
        float fx = x - ix, fy = y - iy;
        float seed = ix + (iy * 57f);
        float v00 = Hash(seed), v01 = Hash(seed + 1f), v10 = Hash(seed + 57f), v11 = Hash(seed + 58f);
        float u = (3f * fx * fx) - (2f * fx * fx * fx), v = (3f * fy * fy) - (2f * fy * fy * fy);
        return ((v00 + ((v01 - v00) * u) + ((v10 - v00) * v) + ((v00 - v01 + v11 - v10) * u * v)) * 2f) - 1f;
    }

    /// <summary>The engine's Hash (Dave Hoskins'), as water.ffx writes it.</summary>
    private static float Hash(float p)
    {
        float px = Fractional(p * 4.438975f), py = Fractional(p * 3.972973f);
        float dot = (py * (px + 19.19f)) + (px * (py + 19.19f));
        px += dot;
        py += dot;
        return Fractional(px * py);
    }

    private static float Fractional(float x) => x - MathF.Floor(x);

    /// <summary>
    /// The wind's ripples on the ocean at a place: their height, and their slope along x and y - GetWindRippleSurfaceField3, three octaves on <see cref="Ripple"/>.
    /// </summary>
    /// <param name="place">Where on the water, in world units.</param>
    /// <param name="wind">The wind's way (radians), the ripples' height, their speed and how much they line up with the wind - WaterLook.WindRegister.</param>
    /// <param name="clock">The clock, in seconds.</param>
    /// <param name="overGround">Whether the water stands over ground (the swell's ground water ratio past a half) - <c>ground_params_ratio</c>.</param>
    /// <param name="slope">The height's rise along x and y.</param>
    /// <remarks>
    /// THE ENGINE'S OCTAVES AS WRITTEN (water.ffx), on open water: three octaves, the first 0.16 of a frequency through a phase
    /// matrix of 0.12 turned to the wind, each stretched along one axis, turned from the last and finer by
    /// <c>lerp(1.5, 3.04, directionness)</c>, its height less by <c>lerp(0.9, 0.22, directionness)</c>; each
    /// SCROLLED STEADILY, <c>(1 + 2.5t) * (0.5, 1.5) * speed * frequency</c> along its own coordinates, and
    /// slower by the root of the step each octave - no fading and no starting over, so nothing loops. The
    /// whole is divided by 0.4, as the engine does. The wind's way is the angle's (cos, sin), the
    /// conversion being in no file read.
    /// </remarks>
    public static float WindRipples(Vector2 place, Vector4 wind, float clock, bool overGround, out Vector2 slope)
    {
        // OVER GROUND UNDER WATER (ground_params_ratio one): twice as fine, no direction, far lower and half as quick.
        float ground = overGround ? 1f : 0f;
        float directionness = Lerp(wind.W, 0f, ground);
        float frequency = 0.16f * Lerp(1f, 2f, ground);
        float amplitude = wind.Y * Lerp(0.2f, 1f, directionness) * Lerp(1f, 0.15f, ground);
        float time = 1f + (clock * 2.5f);
        float step = Lerp(1.5f, 1.9f * 1.6f, directionness);
        float turn = Lerp(2f, 0.2f, directionness);
        float turnCos = MathF.Cos(turn), turnSin = MathF.Sin(turn);
        float windX = MathF.Cos(wind.X), windY = MathF.Sin(wind.X);

        // mul(I * 0.12, float2x2(w.x, w.y, -w.y, w.x)).
        float p00 = 0.12f * windX, p01 = 0.12f * windY, p10 = -0.12f * windY, p11 = 0.12f * windX;
        float scroll = Lerp(2.5f, 1.25f, directionness) * wind.Z * Lerp(1f, 0.5f, ground);
        float stretch = Lerp(0.7f, 0.225f, directionness);
        float falloff = Lerp(0.9f, 0.22f, directionness);
        float height = 0f;
        slope = Vector2.Zero;
        for (var octave = 0; octave < 3; octave++)
        {
            // mul(phaseMatrix, float2x2(stretch, 0, 0, 1) * frequency).
            float m00 = p00 * stretch * frequency, m01 = p01 * frequency;
            float m10 = p10 * stretch * frequency, m11 = p11 * frequency;
            float shift = scroll * frequency;
            float qx = (place.X * m00) + (place.Y * m10) - (time * 0.5f * shift);
            float qy = (place.X * m01) + (place.Y * m11) - (time * 1.5f * shift);
            float value = Ripple(qx, qy, out float gx, out float gy);
            height += value * amplitude;
            slope += new Vector2((gx * m00) + (gy * m01), (gx * m10) + (gy * m11)) * amplitude;

            // mul(phaseMatrix, octaveMatrix): turned, then finer, slower and lower.
            (p00, p01, p10, p11) = (
                (p00 * turnCos) - (p01 * turnSin), (p00 * turnSin) + (p01 * turnCos),
                (p10 * turnCos) - (p11 * turnSin), (p10 * turnSin) + (p11 * turnCos));
            frequency *= step;
            scroll /= MathF.Sqrt(step);
            amplitude *= falloff;
        }

        slope /= 0.4f;
        return height / 0.4f;
    }

    private static float Lerp(float from, float to, float share) => from + ((to - from) * share);

    /// <summary>
    /// One octave's read of the ripple texture at the octave's own coordinates: the height, and its slope along them - GetWaterVolumeStateFieldOctave's.
    /// </summary>
    /// <remarks>
    /// A LINEAR, WRAPPING READ AT LEVEL NOUGHT, as the engine's SAMPLE_TEX2DLOD through SamplerLinearWrap is - the
    /// four nearest texels' centres, mixed by hand, so the card's ShadeRipple (four loads, the same arithmetic)
    /// comes out the same to the bit rather than by its filter's own fixed-point weights. Four texels a unit of the
    /// octave's coordinates: <c>uv = q / 128 * 4</c>. The slope is the texels' slope mixed, as the engine's is -
    /// not the slope of the mixed height.
    /// </remarks>
    internal static float Ripple(float qx, float qy, out float gx, out float gy)
    {
        float sx = (qx * 4f) - 0.5f, sy = (qy * 4f) - 0.5f;
        float fx0 = MathF.Floor(sx), fy0 = MathF.Floor(sy);
        float fx = sx - fx0, fy = sy - fy0;
        int x0 = (int)fx0 & (RippleSide - 1), y0 = (int)fy0 & (RippleSide - 1);
        int x1 = (x0 + 1) & (RippleSide - 1), y1 = (y0 + 1) & (RippleSide - 1);
        float[] texels = Volatile.Read(ref s_ripples).Texels;
        int a = ((y0 * RippleSide) + x0) * 4, b = ((y0 * RippleSide) + x1) * 4;
        int c = ((y1 * RippleSide) + x0) * 4, d = ((y1 * RippleSide) + x1) * 4;
        float Mixed(int part)
        {
            float top = texels[a + part] + ((texels[b + part] - texels[a + part]) * fx);
            float bottom = texels[c + part] + ((texels[d + part] - texels[c + part]) * fx);
            return top + ((bottom - top) * fy);
        }

        gx = Mixed(1) * 12f;
        gy = Mixed(2) * 12f;
        return (Mixed(0) - 0.4f) * 3f;
    }

    /// <summary>
    /// The stand-in ripple texture, made once for where the engine's does not read: gradient noise wrapping every eight cells, its red <c>0.5 + 0.5 * noise / range</c> at each texel's centre, its green and blue the red's slope a texel, worked out exactly.
    /// </summary>
    private static float[] Rippled()
    {
        const int cells = (int)(RippleSide / RippleCell);
        var texels = new float[RippleSide * RippleSide * 4];
        for (var y = 0; y < RippleSide; y++)
        {
            for (var x = 0; x < RippleSide; x++)
            {
                float sx = (x + 0.5f) / RippleCell, sy = (y + 0.5f) / RippleCell;
                int ix = (int)MathF.Floor(sx), iy = (int)MathF.Floor(sy);
                float fx = sx - ix, fy = sy - iy;
                Vector2 ga = RippleGradient(ix % cells, iy % cells), gb = RippleGradient((ix + 1) % cells, iy % cells);
                Vector2 gc = RippleGradient(ix % cells, (iy + 1) % cells), gd = RippleGradient((ix + 1) % cells, (iy + 1) % cells);
                float a = (ga.X * fx) + (ga.Y * fy);
                float b = (gb.X * (fx - 1f)) + (gb.Y * fy);
                float c = (gc.X * fx) + (gc.Y * (fy - 1f));
                float d = (gd.X * (fx - 1f)) + (gd.Y * (fy - 1f));
                float u = fx * fx * (3f - (2f * fx)), v = fy * fy * (3f - (2f * fy));
                float du = 6f * fx * (1f - fx), dv = 6f * fy * (1f - fy);
                float k1 = b - a, k2 = c - a, k3 = a - b - c + d;
                float noise = a + (u * k1) + (v * k2) + (u * v * k3);
                float nx = ga.X + (u * (gb.X - ga.X)) + (v * (gc.X - ga.X)) + (u * v * (ga.X - gb.X - gc.X + gd.X)) + (du * (k1 + (v * k3)));
                float ny = ga.Y + (u * (gb.Y - ga.Y)) + (v * (gc.Y - ga.Y)) + (u * v * (ga.Y - gb.Y - gc.Y + gd.Y)) + (dv * (k2 + (u * k3)));
                int at = ((y * RippleSide) + x) * 4;
                texels[at] = 0.5f + (0.5f * noise / RippleRange);
                texels[at + 1] = 0.5f / RippleRange * nx / RippleCell;
                texels[at + 2] = 0.5f / RippleRange * ny / RippleCell;
                texels[at + 3] = 1f;
            }
        }

        return texels;
    }

    /// <summary>A cell's gradient: two of PerlinNoise31's hashes, normalised.</summary>
    private static Vector2 RippleGradient(int cx, int cy)
    {
        (uint hx, uint hy, _) = Pcg(unchecked((uint)cx), unchecked((uint)cy), 0u);
        var grad = new Vector2(HashUnit(hx) - 0.5f, HashUnit(hy) - 0.5f);
        return grad / MathF.Sqrt(MathF.Max(Vector2.Dot(grad, grad), 1e-12f));
    }

    /// <summary>A swizzle putting one component of its source in all four.</summary>
    private static int AllFrom(int component) => (0xF << 8) | component | (component << 2) | (component << 4) | (component << 6);

    /// <summary>A swizzle writing one component from one of its source's.</summary>
    private static int Only(int into, int from) => (1 << (8 + into)) | (from << (into * 2));
}
