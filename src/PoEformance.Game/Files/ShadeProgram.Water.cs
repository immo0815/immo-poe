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
    /// surface the engine lights - GetFlowSurfaceField's eight octaves (<see cref="Ripples"/>) over a noise
    /// standing for its ripple texture, drifting with the pattern - handed to the game light's glints and
    /// reflection, its specular the area's reflectiveness and its gloss the engine's 0.99. Under the
    /// picture's lamp the twin draws as this program does. Not drawn: the waves lifting the surface (the
    /// engine finds where the view meets the raised surface), foam, the refraction's bend, screen-space
    /// reflections.
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
        // engine lights - drifting with the pattern, its specular the area's reflectiveness, its gloss
        // the engine's (InitWaterSurfaceData) - see HasWaves.
        int drift = build.Emit(Op.Multiply, flow, build.Constant(new Vector4(WaveDrift, 0f, 0f)));
        int waves = build.Emit(Op.Waves, Position, drift, Clock);
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

    /// <summary>
    /// How far the waves drift a second for each unit of the area's flow, in world units along x and y - CHOSEN, the shimmer's own drift.
    /// </summary>
    /// <remarks>
    /// THE ENGINE DRIFTS THEM BY ITS FLOW MAP (GetFlowSample: <c>flowmap_tex_sampler</c> over
    /// <c>river_flowmap_planar_aabb</c>, times <c>2 * water_flow_intensity</c>), which it makes at run time and no
    /// file holds; for Port's harbour it is not known. This drifts them the way the colour's pattern
    /// drifts - 0.15 and 0.1 of a 120-unit cell a second per unit of flow - so the two move together, and
    /// the line under the picture says the way is chosen.
    /// </remarks>
    public static readonly Vector2 WaveDrift = new(18f, 12f);

    /// <summary>The ripple texture's side in texels - the engine's <c>textureSize</c> (GetWaterVolumeStateFieldOctave).</summary>
    public const int RippleSide = 128;

    /// <summary>
    /// How many texels of the stand-in ripple texture one noise cell spans - CHOSEN; the engine's texture is not read.
    /// </summary>
    /// <remarks>
    /// THE ENGINE'S SURFACE IS ONE TEXTURE READ EIGHT TIMES: <c>surface_tex_sampler</c>, "ripple heightfield and
    /// gradient", a pass texture the engine binds itself, 128 texels a tile (GetWaterVolumeStateFieldOctave),
    /// its red the height and its green and blue the height's slope a texel. No shader source names its file
    /// and no capture's loaded files list it, so it stands as a texture of gradient noise read the same way - the
    /// height <c>(r - 0.4) * 3</c>, the slope three times the red's a texel, four texels a unit of the octave's
    /// coordinates - a cell every sixteen texels, eight across the tile and wrapping with it. The texture, once
    /// found, replaces <see cref="RippleSheet"/>'s texels alone.
    /// </remarks>
    public const float RippleCell = 16f;

    /// <summary>The largest a two-dimensional gradient noise of unit gradients reaches, <c>sqrt(2) / 2</c> - what takes it to nought to one.</summary>
    private const float RippleRange = 0.70710677f;

    /// <summary>The stand-in ripple texture: 128 by 128 texels, row by row, four floats each - the height's red, its slope a texel along x and y, and one.</summary>
    private static readonly float[] RippleTexels = Rippled();

    /// <summary>
    /// The stand-in ripple texture's texels, row by row, four floats each - what the card is handed to read, as <see cref="Ripple"/> reads them here.
    /// </summary>
    public static ReadOnlySpan<float> RippleSheet => RippleTexels;

    /// <summary>
    /// The engine's flowing water surface at a place: its height, and its slope along x and y - GetFlowSurfaceField, eight octaves, on <see cref="Ripple"/>.
    /// </summary>
    /// <param name="place">Where on the water, in world units.</param>
    /// <param name="drift">How it drifts, in world units a second - the engine's <c>flow_intensity</c>.</param>
    /// <param name="clock">The clock, in seconds.</param>
    /// <param name="slope">The height's rise along x and y.</param>
    /// <remarks>
    /// THE ENGINE'S OCTAVES AS WRITTEN (water.ffx): four pairs, each octave turned half a radian from the
    /// last and every pair twice as fine, the first a hundredth of a world unit; each octave fading in and
    /// out over three seconds, the pair's two half a period apart, and slid along the drift - turned by
    /// up to 0.3 radians either way - as it fades, so a slide never shows its jump back; the first pair's
    /// height <c>0.3 * (|drift| + 3)</c>, each finer pair's less by between 0.2 and 0.9 as the drift grows
    /// to 300, and quicker by 1.3. Its surface is <c>z = -height</c>, so its normal is
    /// <c>normalize(-slope.x, -slope.y, -1)</c>, up being minus z - GetFlowingSurface's.
    /// </remarks>
    public static float Ripples(Vector2 place, Vector2 drift, float clock, out Vector2 slope)
    {
        float speed = drift.Length();
        float amplitude = 0.3f * (speed + 3f);
        float drop = 0.2f + (0.7f * Math.Clamp(speed / 300f, 0f, 1f));
        float m00 = 0.01f, m01 = 0f, m10 = 0f, m11 = 0.01f;
        float quickness = 1f;
        float height = 0f;
        slope = Vector2.Zero;
        for (var octave = 0; octave < 8; octave++)
        {
            float period = 3f / quickness;
            int pair = octave / 2;
            float phase = (clock / period) + OctaveRatio[pair] + ((octave % 2) * 0.5f);
            phase -= MathF.Floor(phase);
            float opacity = phase * (1f - phase) * 4f;

            // mul(-drift, float2x2(c, s, -s, c)) * phase * quickness * period.
            float c = OctaveCos[pair], s = OctaveSin[pair], along = phase * quickness * period;
            float px = place.X + (((-drift.X * c) + (drift.Y * s)) * along);
            float py = place.Y + (((-drift.X * s) - (drift.Y * c)) * along);

            float value = Ripple((px * m00) + (py * m10), (px * m01) + (py * m11), out float gx, out float gy);
            float weight = amplitude * opacity;
            height += value * weight;
            slope += new Vector2((gx * m00) + (gy * m01), (gx * m10) + (gy * m11)) * weight;

            // mul(octaveMatrix, deltaRotation): every octave turned half a radian, every second one twice as fine too.
            float grow = octave % 2 == 1 ? 2f : 1f;
            (m00, m01, m10, m11) = (
                ((m00 * TurnCos) - (m01 * TurnSin)) * grow, ((m00 * TurnSin) + (m01 * TurnCos)) * grow,
                ((m10 * TurnCos) - (m11 * TurnSin)) * grow, ((m10 * TurnSin) + (m11 * TurnCos)) * grow);
            if (octave % 2 == 1)
            {
                amplitude *= drop;
                quickness *= 1.3f;
            }
        }

        return height;
    }

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
        float[] texels = RippleTexels;
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
    /// The stand-in ripple texture, made once: gradient noise wrapping every eight cells, its red <c>0.5 + 0.5 * noise / range</c> at each texel's centre, its green and blue the red's slope a texel, worked out exactly.
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

    /// <summary>Each pair's share of the octaves, <c>float(i / 2) / float(octavesCount - 1 + 1e-3)</c>.</summary>
    private static readonly float[] OctaveRatio = [0f, 1f / 3.001f, 2f / 3.001f, 3f / 3.001f];

    /// <summary>The cosine of each pair's turn of the drift, <c>lerp(-0.3, 0.3, ratio)</c> radians - worked out once, and written into the card's shader as numbers.</summary>
    internal static readonly float[] OctaveCos = [.. OctaveRatio.Select(one => MathF.Cos(-0.3f + (0.6f * one)))];

    /// <summary>The sine of each pair's turn of the drift.</summary>
    internal static readonly float[] OctaveSin = [.. OctaveRatio.Select(one => MathF.Sin(-0.3f + (0.6f * one)))];

    /// <summary>The cosine of the half radian each octave is turned from the last.</summary>
    internal static readonly float TurnCos = MathF.Cos(0.5f);

    /// <summary>The sine of the half radian each octave is turned from the last.</summary>
    internal static readonly float TurnSin = MathF.Sin(0.5f);

    /// <summary>A swizzle putting one component of its source in all four.</summary>
    private static int AllFrom(int component) => (0xF << 8) | component | (component << 2) | (component << 4) | (component << 6);

    /// <summary>A swizzle writing one component from one of its source's.</summary>
    private static int Only(int into, int from) => (1 << (8 + into)) | (from << (into * 2));
}
