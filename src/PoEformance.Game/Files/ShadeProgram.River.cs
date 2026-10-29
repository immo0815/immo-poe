using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>Which of the engine's water surfaces a material is drawn with - see <see cref="ShadeProgram.KindOf"/>.</summary>
public enum WaterKind
{
    /// <summary>No water: the material's own graphs.</summary>
    None,

    /// <summary>The engine's ocean - GetOceanWaterPlanarState, its swell over the room's shore and the wind's ripples (ShadeProgram.Ocean).</summary>
    Ocean,

    /// <summary>The engine's river - GetFlowingWaterPlanarStateWorld at no flow (ShadeProgram.River).</summary>
    River,

    /// <summary>Standing water - the ocean's surface with no shore, the wind's ripples alone (ShadeProgram.Water's remarks).</summary>
    Still,
}

/// <summary>The engine's river surface - see <see cref="River"/> and <see cref="RiverFoam"/>.</summary>
/// <remarks>
/// THE STAGE SWEEP'S FIRST WATER CAUSE (v0.1.346 and v0.1.348): 1,120 rooms wear a material of the shader type <c>FlowingWater</c>
/// (<c>nvidian_FlowingWater.mat</c>, 511 of them, and the sunken city's, the chaos temple's and the marsh's), 188 <c>NonsplashyFlowingWater</c>,
/// 254 <c>NonsplashyWater</c> and 5 <c>Swamp_Water</c>, all drawn through their graphs as an ordinary material where the engine draws
/// them in its water pass. The pass has two surfaces (internalonly.ffx): WaterComputeSurface, the ocean's, and WaterComputeFlowingSurface -
/// GetFlowingSurface over GetFlowingWaterPlanarStateWorld (water.ffx, whole in water.txt). Which shader type takes which is in the program,
/// not a file; the names say the flowing ones take the second, and DECIDED from the live client (2026-10-29) so are they drawn. The two
/// still ones are drawn as the ocean's surface with no shore - its wind's ripples alone, no swell, no beach and no field waves (DECIDED
/// from the live client, a reading: <c>NonsplashyOcean</c> is a name the program holds, and nothing splashes on a pond).
///
/// THE RIVER'S FLOW IS THE ENGINE'S, MADE AT RUN TIME AND READ NOWHERE: GetFlowSample reads <c>flowmap_tex_sampler</c> over
/// <c>river_flowmap_planar_aabb</c>, pass uniforms the processor sets, and the program makes the texture with
/// <c>RiverMarkupField(IDevice*, const PointLine*, const MarkupFlowConstraints*, vector2_int, vector4, float, bool, float)</c> or its twin
/// over a grid of bools - inputs no file of the 118,851 water-shore.txt searched names (v0.1.302's capture: SetWaterFlow, SetWaterSource and
/// SetWaterBlocking are script commands no object script calls). So THE RIVER IS DRAWN AT NO FLOW, DECIDED from the live client: the flow
/// sample nought throughout, its velocity and its distance alike, and the markup hunt in ocean-markup.txt looks for the map's box and grid
/// (ShoreFieldHunt, OceanMarkupHunt). At no flow the surface does not slide: its eight octaves fade in and out in place, each pair half
/// a period apart, every 3 seconds the first and 1.3 times quicker each pair after - a shimmer, not a current.
/// </remarks>
public sealed partial class ShadeProgram
{
    /// <summary>The shader type the engine draws as its river - GetFlowingWaterPlanarStateWorld; see the remarks.</summary>
    public const string RiverShader = "FlowingWater";

    /// <summary>The river's twin that throws no splashes - drawn as the river.</summary>
    public const string NonsplashyRiverShader = "NonsplashyFlowingWater";

    /// <summary>Standing water that throws no splashes - drawn as the ocean's surface with no shore.</summary>
    public const string StillShader = "NonsplashyWater";

    /// <summary>A swamp's standing water - drawn as <see cref="StillShader"/> is.</summary>
    public const string SwampShader = "Swamp_Water";

    /// <summary>What the river program is named in a picture's lines.</summary>
    public const string RiverGraph = "the engine's water pass, its river surface at no flow (the flow map, made at run time, unread)";

    /// <summary>What the still water program is named in a picture's lines.</summary>
    public const string StillGraph = "the engine's water pass, the ocean's surface with no shore - the wind's ripples alone";

    /// <summary>The river step's output: the surface's normal and its height.</summary>
    internal const int RiverNormal = 0;

    /// <summary>The river step's output: the foam's colour and how much it covers.</summary>
    internal const int RiverFoamOut = 1;

    /// <summary>
    /// The foam's strength at no flow before the area's flow_foam: GetFlowingWaterPlanarStateWorld's march - its first step's
    /// <c>(1 - (a - 100) * 0.02) * (|v| * 0.1 + 0.05) * exp(0) * 0.1</c> with the sample's distance <c>a</c> and velocity <c>v</c> nought.
    /// </summary>
    public const float RiverStillFoam = 0.015f;

    /// <summary>The foam's strength every river carries whatever its flow - the march's <c>+ 0.25</c>.</summary>
    public const float RiverFoamFloor = 0.25f;

    /// <summary>
    /// The water a material is drawn as, by its shader type: the ocean, the river, standing water, or none.
    /// </summary>
    public static WaterKind KindOf(string? shaderType)
    {
        if (IsOcean(shaderType))
        {
            return WaterKind.Ocean;
        }

        if (string.Equals(shaderType, RiverShader, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shaderType, NonsplashyRiverShader, StringComparison.OrdinalIgnoreCase))
        {
            return WaterKind.River;
        }

        return string.Equals(shaderType, StillShader, StringComparison.OrdinalIgnoreCase)
            || string.Equals(shaderType, SwampShader, StringComparison.OrdinalIgnoreCase)
            ? WaterKind.Still
            : WaterKind.None;
    }

    /// <summary>
    /// Whether the program is the river's - its foam reads the engine's distortion texture (<see cref="Distortion"/>), which the card binds then.
    /// </summary>
    public bool Rivers { get; private set; }

    /// <summary>
    /// Whether the program's water is the ocean's, lifted by its waves where the light asks (SceneLight.WaterLift, the field waves) and drawn
    /// as one surface then - not the river's nor standing water's, whose engine surfaces are flat meshes lit per pixel only.
    /// </summary>
    public bool Lifts { get; private set; }

    /// <summary>
    /// GetFlowSurfaceField at no flow, as written: its height at a place, and its slope along x and y.
    /// </summary>
    /// <param name="place">Where on the water, in the picture's units.</param>
    /// <param name="clock">The clock, in seconds.</param>
    /// <param name="slope">The height's rise along x and y.</param>
    /// <remarks>
    /// EIGHT OCTAVES ON THE RIPPLE TEXTURE (<see cref="Ripple"/>, GetWaterVolumeStateFieldOctave), the first through a matrix of 0.01, each
    /// turned half a radian from the last and every second one twice as fine, its height <c>0.3 * (|flow| + 3)</c> - 0.9 at no flow - and
    /// every second one's less by <c>lerp(0.2, 0.9, |flow| / 300)</c>, 0.2. Each fades in and out over <c>3 / speed</c> seconds,
    /// <c>phase * (1 - phase) * 4</c>, the pair half a period apart and the pairs a third of one, the speed 1.3 times more each pair. The
    /// scroll along the flow, <c>-flow * phase * speed * period</c> turned by -0.3 to 0.3 radians, is nought at no flow.
    /// </remarks>
    public static float River(Vector2 place, float clock, out Vector2 slope)
    {
        const int octaves = 4;
        float turnCos = MathF.Cos(0.5f), turnSin = MathF.Sin(0.5f);
        float amplitude = 0.3f * 3f;
        float speed = 1f;
        const float drop = 0.2f;
        float m00 = 0.01f, m01 = 0f, m10 = 0f, m11 = 0.01f;
        float height = 0f;
        slope = Vector2.Zero;
        for (var octave = 0; octave < octaves * 2; octave++)
        {
            float period = 3f / speed;
            float ratio = (octave / 2) / (octaves - 1 + 1e-3f);
            float phase = Fractional((clock / period) + ratio + ((octave % 2) * 0.5f));
            float opacity = phase * (1f - phase) * 4f;

            // mul(place, octaveMatrix), and the slope through its transpose.
            float qx = (place.X * m00) + (place.Y * m10);
            float qy = (place.X * m01) + (place.Y * m11);
            float value = Ripple(qx, qy, out float gx, out float gy);
            float weight = amplitude * opacity;
            height += value * weight;
            slope += new Vector2((gx * m00) + (gy * m01), (gx * m10) + (gy * m11)) * weight;

            // mul(octaveMatrix, deltaRotation), every second one twice as fine, lower and quicker.
            float finer = (octave % 2) == 1 ? 2f : 1f;
            (m00, m01, m10, m11) = (
                ((m00 * turnCos) - (m01 * turnSin)) * finer, ((m00 * turnSin) + (m01 * turnCos)) * finer,
                ((m10 * turnCos) - (m11 * turnSin)) * finer, ((m10 * turnSin) + (m11 * turnCos)) * finer);
            if ((octave % 2) == 1)
            {
                amplitude *= drop;
                speed *= 1.3f;
            }
        }

        return height;
    }

    /// <summary>
    /// The river's foam at no flow: GetFlowingWaterPlanarStateWorld's strength and GetFlowingFoamColor as written - its colour in xyz
    /// (white), how much it covers in w; nought where the distortion texture is not read.
    /// </summary>
    /// <param name="place">Where on the water, in the picture's units.</param>
    /// <param name="clock">The clock, in seconds.</param>
    /// <param name="flowFoam">The area's water.flow_foam - WaterLook.FlowRegister's w.</param>
    /// <remarks>
    /// THE STRENGTH: <c>saturate(0.015 * flow_foam + 0.25)</c> (<see cref="RiverStillFoam"/>, <see cref="RiverFoamFloor"/>) - the curl, the
    /// speed and the shallows' terms are nought at no flow. THE COLOUR: GetFlowingFoamColor's two interpolation nodes come to one at
    /// no flow (<c>GetLinearInterpNodes(0)</c>: the seeds 0 and 1, the weights 1 and 0), so one GetFlowingFoamColorFixedFreq at 0.2 a
    /// second: four layers, two harmonics of two half a period apart, each with its phase scrambled by the distortion's red, its place
    /// moved by the hashed layer, the distortion turned 0.5 radians (turbulence, by <c>10 * saturate(strength - 0.2)</c>, nought at the floor),
    /// turned 2 radians (a hundred units' distortion) and turned -1 radian (the mask); the foam a product of two sines, its share
    /// thresholded by the mask's alpha and then by <c>lerp(1, 0.3, strength)</c>. Every read is SamplerLinearWrap's at level nought, as the
    /// engine's <c>lod -0.5</c> clamps - <see cref="DistortionRead"/>.
    /// </remarks>
    public static Vector4 RiverFoam(Vector2 place, float clock, float flowFoam)
        => Distortion is { } distortion ? RiverFoam(distortion.Top, place, clock, flowFoam) : Vector4.Zero;

    /// <summary>The same over a distortion texture handed in - see <see cref="RiverFoam(Vector2, float, float)"/>.</summary>
    public static Vector4 RiverFoam(GamePicture sheet, Vector2 place, float clock, float flowFoam)
    {
        float strength = Saturated((RiverStillFoam * flowFoam) + RiverFoamFloor);
        const float phaseFrequency = 0.2f;
        float foam = 0f, mask = 0f, weights = 0f;
        for (var layer = 0; layer < 4; layer++)
        {
            int harmonic = layer / 2;
            float scramble = DistortionRead(sheet, place * (0.0005f * (1 << harmonic))).X * 0.2f;
            float phase = Fractional((clock * phaseFrequency) + ((layer % 2) * 0.5f) + (0.5f * harmonic / 2f) + scramble);
            float opacity = MathF.Min(phase, 1f - phase) * 2f;
            // pow(1.8, harmonic) of the first two harmonics, written out so the card comes to the same bits.
            float frequency = harmonic == 0 ? 1f : 1.8f;

            // The scroll along the flow, (phase - 0.5) * 1.5 * flow / frequency, is nought at no flow.
            Vector3 hash = Hash33(new Vector3(layer, 0f, 0f));
            Vector2 at = (place * (0.01f * frequency)) + new Vector2(hash.X, hash.Y);
            Vector4 turbulence = DistortionRead(sheet, Turned(0.5f, at / 7f));
            at += new Vector2(turbulence.X - 0.5f, turbulence.Y - 0.5f) * 2f * phase / phaseFrequency * 0.2f * (10f * Saturated(strength - 0.2f));
            Vector4 moved = DistortionRead(sheet, Turned(2f, at / 10f));
            at += new Vector2(moved.X - 0.5f, moved.Y - 0.5f) * 100f;
            Vector4 masked = DistortionRead(sheet, Turned(-1f, at) * 0.03f);
            float noise = (1f - (((MathF.Sin(at.X * 2.7f) * 0.5f) + 0.5f) * ((MathF.Sin(at.Y * 2.3f) * 0.5f) + 0.5f))) * 0.3f;
            weights += opacity;
            foam += Saturated(1f - noise) * opacity;
            mask += masked.W * opacity;
        }

        foam /= weights;
        mask /= weights;
        float maskThreshold = Saturated((1f - mask - 0.4f) * 3f);
        foam = Saturated((foam - maskThreshold) / (1.3f - maskThreshold) * 1.3f);
        float threshold = Lerp(1f, 0.3f, strength);
        foam = Saturated((foam - threshold) / (1.4f - threshold) * 1.4f);
        return new Vector4(1f, 1f, 1f, foam);
    }

    /// <summary><c>mul(RotationMatrix(angle), v)</c>: <c>float2x2(cos, sin, -sin, cos)</c> times the column v.</summary>
    private static Vector2 Turned(float angle, Vector2 v)
    {
        float c = MathF.Cos(angle), s = MathF.Sin(angle);
        return new Vector2((c * v.X) + (s * v.Y), (-s * v.X) + (c * v.Y));
    }

    /// <summary>
    /// The distortion texture as SamplerLinearWrap reads it at level nought: the four nearest texels' centres mixed by hand, wrapping, each
    /// channel a fraction of 255 - the card's ShadeRiverRead, the caustics' WrappedRead arithmetic.
    /// </summary>
    internal static Vector4 DistortionRead(GamePicture sheet, Vector2 spot)
    {
        int width = sheet.Width, height = sheet.Height;
        float sx = (spot.X * width) - 0.5f, sy = (spot.Y * height) - 0.5f;
        float lx = MathF.Floor(sx), ly = MathF.Floor(sy);
        float fx = sx - lx, fy = sy - ly;
        int x0 = (((int)lx % width) + width) % width, y0 = (((int)ly % height) + height) % height;
        int x1 = (x0 + 1) % width, y1 = (y0 + 1) % height;
        byte[] rgba = sheet.Rgba;
        Vector4 Texel(int x, int y)
        {
            int at = ((y * width) + x) * 4;
            return new Vector4(rgba[at], rgba[at + 1], rgba[at + 2], rgba[at + 3]) / 255f;
        }

        Vector4 top = Vector4.Lerp(Texel(x0, y0), Texel(x1, y0), fx);
        Vector4 bottom = Vector4.Lerp(Texel(x0, y1), Texel(x1, y1), fx);
        return Vector4.Lerp(top, bottom, fy);
    }
}
