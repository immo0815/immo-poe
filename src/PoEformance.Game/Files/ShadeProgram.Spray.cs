using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>The sea's spray on what stands in it - effects.ffx's CrashingFoamSurface, a node of a material's own graph.</summary>
public sealed partial class ShadeProgram
{
    /// <summary>What CrashingFoamSurface hands its graph: the foam's colour and how much it covers, its normal, ao and gloss, and how wet the surface is.</summary>
    public readonly record struct Spray(Vector4 Colour, Vector3 Normal, float Ao, float Gloss, float Wetness);

    /// <summary>How the spray reads its three textures: a sheet's texel at coordinates, at its top level - a struct, so no call goes through a delegate.</summary>
    public interface ISprayTextures
    {
        /// <summary>A sheet's texel at coordinates, wrapping, linear where the sheet is sRGB.</summary>
        Vector4 Read(int sheet, Vector2 uv);
    }

    /// <summary>A program's own sheets, read at their top level.</summary>
    private readonly struct TopLevels(ShadeProgram program) : ISprayTextures
    {
        public Vector4 Read(int sheet, Vector2 uv) => program.Read(sheet, uv, 0f);
    }

    /// <summary>What one <see cref="Op.Spray"/> step was run on - its three outputs are three steps over one sum, worked out once.</summary>
    private readonly record struct SprayKey(ShadeProgram? Program, Vector4 Place, Vector4 Normal, Vector4 Packed, int Sheets, ShoreField? Shore);

    [ThreadStatic]
    private static SprayKey s_sprayKey;

    [ThreadStatic]
    private static Spray s_spray;

    /// <summary>The probe a drawing hands the spray for the probed pixel's fragment, or null - see <see cref="ProbeSprays"/>.</summary>
    [ThreadStatic]
    private static PixelProbe? s_sprayProbe;

    [ThreadStatic]
    private static int s_sprayTriangle;

    [ThreadStatic]
    private static float s_sprayDepth;

    /// <summary>
    /// Has the spray step of the program run next on this thread write its terms to a probe (ProbeSpray) - once, the first step of the three
    /// outputs; null takes it back. A drawing sets it round the probed pixel's fragment alone, so every other pixel pays one compare.
    /// </summary>
    internal static void ProbeSprays(PixelProbe? probe, int triangle, float depth)
    {
        s_sprayProbe = probe;
        s_sprayTriangle = triangle;
        s_sprayDepth = depth;
    }

    /// <summary>
    /// One output of the spray step: A the place, B the normal, C the curvature, the spray's height and the clock in x, y and z, D the
    /// shore's register; Extra the three sheets a byte each and in its top byte which output - the colour, the normal and ao, or the
    /// wetness and gloss.
    /// </summary>
    private Vector4 Sprayed(Span<Vector4> r, Step step)
    {
        ShoreField? shore = r[step.D].W > 0.5f ? s_shore : null;
        int sheets = step.Extra & 0xFFFFFF;
        var key = new SprayKey(this, r[step.A], r[step.B], r[step.C], sheets, shore);
        PixelProbe? probe = s_sprayProbe;
        if (!key.Equals(s_sprayKey) || probe is not null)
        {
            Vector4 place = r[step.A], normal = r[step.B], packed = r[step.C];
            ProbeSpray terms = default;
            s_spray = Spraying(
                new Vector3(place.X, place.Y, place.Z), new Vector3(normal.X, normal.Y, normal.Z), packed.X, packed.Y, packed.Z, shore,
                new TopLevels(this), sheets & 0xFF, (sheets >> 8) & 0xFF, (sheets >> 16) & 0xFF, probe is not null, ref terms);
            s_sprayKey = key;
            if (probe is not null)
            {
                SprayRise[]? rise = terms.Shore
                    ? Rising(
                        new Vector3(place.X, place.Y, place.Z), new Vector3(normal.X, normal.Y, normal.Z), packed.X, packed.Y, packed.Z, shore!,
                        new TopLevels(this), sheets & 0xFF, (sheets >> 8) & 0xFF, (sheets >> 16) & 0xFF)
                    : null;
                probe.Add(terms with { Triangle = s_sprayTriangle, Depth = s_sprayDepth, Rise = rise });
                s_sprayProbe = null;
            }
        }

        Spray spray = s_spray;
        return (step.Extra >> 24) switch
        {
            SprayColour => spray.Colour,
            SprayNormal => new Vector4(spray.Normal, spray.Ao),
            _ => new Vector4(spray.Wetness, spray.Gloss, 0f, 0f),
        };
    }

    /// <summary>How far above the shore field's water level the probe's sweep runs the spray - from under it to past where the game's quay streaks.</summary>
    private static readonly float[] RiseHeights = [-25f, 0f, 50f, 100f, 150f, 200f, 300f];

    /// <summary>The sweep's steps over one crashing wave.</summary>
    private const int RiseSteps = 50;

    /// <summary>A cover the sweep counts as shown - a twentieth.</summary>
    private const float RiseShown = 0.05f;

    /// <summary>
    /// The spray at the probed place moved up its face to each of <see cref="RiseHeights"/>, run over one crashing wave from the clock -
    /// see <see cref="SprayRise"/>. Up the face is the way within the surface that rises most (up is minus z); a surface within some
    /// twelve degrees of level, whose way up would carry the place far across it, is moved straight up.
    /// </summary>
    public static SprayRise[] Rising<TRead>(
        Vector3 place, Vector3 normal, float curvature, float sprayHeight, float clock, ShoreField shore,
        TRead read, int colourSheet, int normalSheet, int noiseSheet)
        where TRead : struct, ISprayTextures
    {
        ArgumentNullException.ThrowIfNull(shore);
        float length = normal.LengthSquared();
        Vector3 facing = length > 1e-12f ? normal / MathF.Sqrt(length) : Vector3.UnitZ;
        var up = new Vector3(0f, 0f, -1f);
        Vector3 along = up - (facing * Vector3.Dot(facing, up));
        along = along.LengthSquared() > 1e-6f ? Vector3.Normalize(along) : up;
        along = along.Z < -0.2f ? along : up;

        const float step = FieldWaveLength / FieldWaveSpeed / RiseSteps;
        var rise = new SprayRise[RiseHeights.Length];
        for (var at = 0; at < rise.Length; at++)
        {
            float above = RiseHeights[at];
            Vector3 point = place + (along * (((shore.Level - above) - place.Z) / along.Z));
            float peak = -1f, peakAt = 0f, peakPhase = 0f, foamMost = 0f, alphaMost = 0f;
            var shown = 0;
            for (var k = 0; k < RiseSteps; k++)
            {
                ProbeSpray terms = default;
                Spraying(point, facing, curvature, sprayHeight, clock + (k * step), shore, read, colourSheet, normalSheet, noiseSheet, true, ref terms);
                if (terms.Cover > peak)
                {
                    (peak, peakAt, peakPhase) = (terms.Cover, k * step, terms.Phase);
                }

                shown += terms.Cover > RiseShown ? 1 : 0;
                foamMost = MathF.Max(foamMost, terms.Foam);
                alphaMost = MathF.Max(alphaMost, terms.FoamAlpha);
            }

            rise[at] = new SprayRise(above, peak, peakAt, peakPhase, shown * step, foamMost, alphaMost);
        }

        return rise;
    }

    /// <summary>The spray step's outputs, in its Extra's top byte: the foam's colour and cover.</summary>
    internal const int SprayColour = 0;

    /// <summary>The foam's normal in xyz, the ao in w.</summary>
    internal const int SprayNormal = 1;

    /// <summary>The wetness in x, the gloss in y.</summary>
    internal const int SprayWet = 2;

    /// <summary>field_waves' wave speed, in units a second - the crashing waves roll in at it.</summary>
    public const float FieldWaveSpeed = 200f;

    /// <summary>field_waves' wave length, in units - one wave crashes every <see cref="FieldWaveLength"/> / <see cref="FieldWaveSpeed"/> seconds.</summary>
    public const float FieldWaveLength = 1000f;

    /// <summary>
    /// The spray at a place on a surface standing in the sea: CrashingFoamSurface as effects.ffx writes it.
    /// </summary>
    /// <param name="place">The surface's place in the world - the graph's own, lifted as its graph lifts it.</param>
    /// <param name="normal">The surface's normal.</param>
    /// <param name="curvature">The graph's curvature: how sharply the fade ramps.</param>
    /// <param name="sprayHeight">How far above the water the spray reaches - the graph's.</param>
    /// <param name="clock">The clock, in seconds.</param>
    /// <param name="shore">The room's shore, or null - no spray without one.</param>
    /// <param name="read">How a texture is read: sheet and coordinates, at its top level.</param>
    /// <param name="colourSheet">The foam's colour and height - <c>foam_color_tex</c>.</param>
    /// <param name="normalSheet">The foam's normal, ao and roughness - <c>foam_normal_tex</c>.</param>
    /// <param name="noiseSheet">The wave coordinate's noise - <c>wave_coord_noise_tex</c>, its green read.</param>
    /// <remarks>
    /// THE ROCKS' OWN MATERIAL DRAWS THE SEA RUNNING DOWN THEM: Port's shore rocks (ISL_Rock*.mat) are instances of
    /// OceanSprayFoam.fxgraph beside their stone, whose CrashingFoamSurface the compile left out until v0.1.234. As written: the
    /// crashing wave's coordinate from the distance out to sea, a 1,000-unit wave rolling in at 200 a second (GetFieldWaveGeom's
    /// <c>wave_coord</c>, its advected and stationary noise read off the noise texture's green); the spray's phase and strength from
    /// it; a band of height - <c>saturate(0.8 + z / spray_height) * saturate((150 - z) / 100)</c>, the shore's distance within 200 -
    /// ramped by the spray and the curvature; the foam's own texture read three ways (GetTriplanarScrollingFoam, two layers each,
    /// crossfading every two seconds), scrolling down the face at rest and towards the shore as the spray peaks. The shore's
    /// distances are the room's shore field's in world units (2,000 a colour unit - <see cref="ShoreField.Reach"/>, CHOSEN for the
    /// engine's <c>dist_to_color</c>). Left as nought, being multiplied by nought where the engine reads them: the scrolling layer's
    /// turbulence (its <c>noise_sampler</c>, a pass texture named nowhere). On the processor every texture is read at its top level.
    /// </remarks>
    public static Spray Spraying<TRead>(
        Vector3 place, Vector3 normal, float curvature, float sprayHeight, float clock, ShoreField? shore,
        TRead read, int colourSheet, int normalSheet, int noiseSheet)
        where TRead : struct, ISprayTextures
    {
        ProbeSpray unused = default;
        return Spraying(place, normal, curvature, sprayHeight, clock, shore, read, colourSheet, normalSheet, noiseSheet, false, ref unused);
    }

    /// <summary>
    /// <see cref="Spraying{TRead}(Vector3, Vector3, float, float, float, ShoreField, TRead, int, int, int)"/>, its terms written to
    /// <paramref name="terms"/> where <paramref name="probed"/> - the probe's line (ProbeSpray), worked out by the arithmetic that draws.
    /// </summary>
    public static Spray Spraying<TRead>(
        Vector3 place, Vector3 normal, float curvature, float sprayHeight, float clock, ShoreField? shore,
        TRead read, int colourSheet, int normalSheet, int noiseSheet, bool probed, ref ProbeSpray terms)
        where TRead : struct, ISprayTextures
    {
        // THE NORMAL AT ITS OWN LENGTH ONE, as the program's own normal is (dot > 1e-12): the graph hands the TBN basis's, whose
        // length the mesh does not say. The weights and the velocity's projection do not change with it; Grad3ToNormal does.
        float length = normal.LengthSquared();
        normal = length > 1e-12f ? normal / MathF.Sqrt(length) : normal;
        if (shore is null)
        {
            if (probed)
            {
                terms = new ProbeSpray(
                    -1, 0f, place, normal, curvature, sprayHeight, clock, false, float.NaN, -1, -1, "no shore field", 0f, 0f, Vector2.Zero, 0f, 0f,
                    0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, Vector3.Zero, 0f, 0f, 0f, Vector3.Zero);
            }

            return new Spray(Vector4.Zero, normal, 1f, 0f, 0f);
        }

        // GetShoreFieldData: the distances in world units, the one out to sea's slope by central differences a texel apart.
        var at = new Vector2(place.X, place.Y);
        float eps = shore.CellSize;
        float shoreDist = ShoreWorld(shore, at, out float oceanDist);
        ShoreWorld(shore, at + new Vector2(eps, 0f), out float xp);
        ShoreWorld(shore, at - new Vector2(eps, 0f), out float xn);
        ShoreWorld(shore, at + new Vector2(0f, eps), out float yp);
        ShoreWorld(shore, at - new Vector2(0f, eps), out float yn);
        var oceanSlope = new Vector2(xp - xn, yp - yn) * (0.5f / eps);

        // GetFieldWaveGeom's wave_coord: the distance out to sea and the clock, and the noise advected along the waves and standing.
        Vector2 uv = at * 0.00012f;
        float stationary = read.Read(noiseSheet, uv).Y;
        Vector2 velocity = -oceanSlope * (FieldWaveSpeed * 0.00012f / 0.1f);
        (Vector2 phases, Vector2 seeds, Vector2 weights) = LinearInterpNodes(clock * 0.1f);
        float advected = 0f;
        for (var layer = 0; layer < 2; layer++)
        {
            float phase = layer == 0 ? phases.X : phases.Y;
            Vector3 hash = HashStable(layer == 0 ? seeds.X : seeds.Y);
            Vector2 layerUv = uv - (velocity * ((phase - 0.5f) * 2f));
            advected += read.Read(noiseSheet, layerUv + new Vector2(hash.X, hash.Y)).Y * (layer == 0 ? weights.X : weights.Y);
        }

        float waveCoord = (oceanDist * (-1f / FieldWaveLength)) - (clock * FieldWaveSpeed / FieldWaveLength) + advected + stationary;

        float sprayPhase = Saturated(Fractional(waveCoord - 0.15f));
        float sprayIntensity = sprayPhase * sprayPhase * (1f - MathF.Pow(sprayPhase, 5f));
        float heightFade = Saturated(0.8f + (place.Z / sprayHeight))
            * Saturated((150f - place.Z) / 100f)
            * Saturated((shoreDist + 200f) * 0.01f);
        float fadePhase = RampNorm(heightFade, ((1f - sprayIntensity) * 0.9f) + 0.01f);
        float foamIntensity = RampNorm(fadePhase, curvature);

        Vector3 flow = Vector3.Lerp(
            new Vector3(0f, 0f, 100f), new Vector3(0f, 0f, 50f) + (new Vector3(oceanSlope, 0f) * (-FieldWaveSpeed * 0.5f)), sprayIntensity);
        Vector3 along = flow - (normal * (Vector3.Dot(normal, flow) / Vector3.Dot(normal, normal)));
        (Vector4 colour, Vector3 foamNormal, float rough, float ao) = TriplanarFoam(
            place * 0.002f, normal, along * 0.002f, clock, 0.8f, read, colourSheet, normalSheet);
        float foamAlpha = colour.W;
        colour.W = MathSmoothStep(foamIntensity, 1f - Gain(colour.W, 0.1f), 0.5f);
        float wetness = RampNorm(heightFade, curvature);
        if (probed)
        {
            float above = Saturated(0.8f + (place.Z / sprayHeight)), below = Saturated((150f - place.Z) / 100f);
            string cell = shore.CellAt(at, out int cellX, out int cellY);
            terms = new ProbeSpray(
                -1, 0f, place, normal, curvature, sprayHeight, clock, true, shore.Level, cellX, cellY, cell, shoreDist, oceanDist, oceanSlope,
                stationary, advected, waveCoord, sprayPhase, sprayIntensity, above, below, Saturated((shoreDist + 200f) * 0.01f), heightFade,
                fadePhase, foamIntensity, along, foamAlpha, colour.W, wetness, new Vector3(colour.X, colour.Y, colour.Z));
        }

        return new Spray(colour, foamNormal, ao, 1f - rough, wetness);
    }

    /// <summary>GetShoreData's distances at a place, in world units: the shore's <c>(1 - r) / dist_to_color</c> and out to sea's <c>(1 - b) / dist_to_color</c>.</summary>
    private static float ShoreWorld(ShoreField shore, Vector2 place, out float ocean)
    {
        shore.At(place, out float shoreDist, out float oceanDist);
        ocean = (1f - oceanDist) * ShoreField.Reach;
        return shoreDist * ShoreField.Reach;
    }

    /// <summary>GetTriplanarScrollingFoam: the foam read on the three planes, weighted by the normal's squares, its normal from the three gradients.</summary>
    private static (Vector4 Colour, Vector3 Normal, float Rough, float Ao) TriplanarFoam<TRead>(
        Vector3 place, Vector3 normal, Vector3 velocity, float phase, float normalStrength, TRead read, int colourSheet, int normalSheet)
        where TRead : struct, ISprayTextures
    {
        FoamLayer yz = ScrollingFoam(new Vector2(place.Y, place.Z), new Vector2(velocity.Y, velocity.Z), phase, read, colourSheet, normalSheet);
        FoamLayer xz = ScrollingFoam(new Vector2(place.X, place.Z), new Vector2(velocity.X, velocity.Z), phase, read, colourSheet, normalSheet);
        FoamLayer xy = ScrollingFoam(new Vector2(place.X, place.Y), new Vector2(velocity.X, velocity.Y), phase, read, colourSheet, normalSheet);
        Vector3 grad = new Vector3(0f, yz.Grad.X, yz.Grad.Y) + new Vector3(xz.Grad.X, 0f, xz.Grad.Y) + new Vector3(xy.Grad.X, xy.Grad.Y, 0f);
        Vector3 square = normal * normal;
        Vector3 weights = square / (square.X + square.Y + square.Z);
        grad *= normalStrength;
        Vector3 perpendicular = grad - (normal * Vector3.Dot(grad, normal));
        Vector3 foamNormal = (normal - perpendicular) / (Vector3.Dot(perpendicular, perpendicular) + 1f);
        return (
            (yz.Colour * weights.X) + (xz.Colour * weights.Y) + (xy.Colour * weights.Z),
            foamNormal,
            (yz.Rough * weights.X) + (xz.Rough * weights.Y) + (xy.Rough * weights.Z),
            (yz.Ao * weights.X) + (xz.Ao * weights.Y) + (xy.Ao * weights.Z));
    }

    private readonly record struct FoamLayer(Vector4 Colour, Vector2 Grad, float Ao, float Rough);

    /// <summary>
    /// GetFoamScrollingLayer with no turbulence and seed nought: two layers crossfading every two seconds, each turned by its hash and
    /// slid along the velocity, the colour, the height and the normal read where it lands.
    /// </summary>
    private static FoamLayer ScrollingFoam<TRead>(Vector2 uv, Vector2 velocity, float phase, TRead read, int colourSheet, int normalSheet)
        where TRead : struct, ISprayTextures
    {
        (Vector2 phases, Vector2 seeds, Vector2 weights) = LinearInterpNodes(phase);
        Vector4 colour = Vector4.Zero;
        Vector2 grad = Vector2.Zero;
        float ao = 0f, rough = 0f;
        for (var layer = 0; layer < 2; layer++)
        {
            float local = layer == 0 ? phases.X : phases.Y, weight = layer == 0 ? weights.X : weights.Y;
            Vector3 hash = Hash33(new Vector3(layer == 0 ? seeds.X : seeds.Y, 0f, 0f));
            float angle = 3.1415f * 2f * hash.Z;
            float s = MathF.Sin(angle), c = MathF.Cos(angle);
            Vector2 flow = uv - (velocity * ((local * 2f) - 1f)) + new Vector2(hash.X, hash.Y);

            // mul(v, float2x2(c, s, -s, c)): the rows are (c, s) and (-s, c).
            var turned = new Vector2((flow.X * c) - (flow.Y * s), (flow.X * s) + (flow.Y * c));
            Vector4 foam = read.Read(colourSheet, turned);
            Vector4 bumps = read.Read(normalSheet, turned);
            Vector2 back = RgToGradYFix(bumps.X, bumps.Y);

            // mul(g, transpose(...)): the rows are (c, -s) and (s, c).
            var bent = new Vector2((back.X * c) + (back.Y * s), (back.Y * c) - (back.X * s));
            colour += foam * weight;
            grad += bent * weight;
            ao += bumps.Z * weight;
            rough += bumps.W * weight;
        }

        return new FoamLayer(colour, grad, ao, rough);
    }

    /// <summary>RgToGradYFix: a texture's normal as the surface's gradient, its y turned over.</summary>
    private static Vector2 RgToGradYFix(float r, float g)
    {
        float nx = (r - 0.5f) * 2f, ny = (g - 0.5f) * 2f;
        float nz = MathF.Sqrt(Saturated(1f - ((nx * nx) + (ny * ny))));
        float under = MathF.Abs(nz) > 1e-7f ? nz : 1e-7f;
        return new Vector2(-nx / under, ny / under);
    }

    /// <summary>GetLinearInterpNodes: two nodes half a period apart, their local phases, seeds and triangular weights.</summary>
    internal static (Vector2 Phases, Vector2 Seeds, Vector2 Weights) LinearInterpNodes(float globalPhase)
    {
        var global = new Vector2(0.5f + (globalPhase * 0.5f), globalPhase * 0.5f);
        var phases = new Vector2(Fractional(global.X), Fractional(global.Y));
        var seeds = new Vector2((MathF.Floor(global.X) * 2f) + 0f, (MathF.Floor(global.Y) * 2f) + 1f);
        Vector2 weights = Vector2.Min(phases, Vector2.One - phases) * 2f;
        return (phases, seeds, weights);
    }

    /// <summary>The engine's hash33: three fractions of a float3, no integers.</summary>
    internal static Vector3 Hash33(Vector3 p)
    {
        p = Fractional3(p * new Vector3(0.1031f, 0.1030f, 0.0973f));
        float dotted = Vector3.Dot(p, new Vector3(p.Y + 19.19f, p.X + 19.19f, p.Z + 19.19f));
        p += new Vector3(dotted);
        return Fractional3(new Vector3(p.X + p.Y, p.X + p.X, p.Y + p.X) * new Vector3(p.Z, p.Y, p.X));
    }

    /// <summary>
    /// The engine's hash33Stable of a number spread to all three - see <see cref="HashStable(Vector3)"/>.
    /// </summary>
    /// <remarks>
    /// HashUToF, NOT A DIVISION BY THE LARGEST UINT: the fragments' hashes declaration (water.ffx) makes each a fraction of its top
    /// 23 bits; util.hlsl's <c>float3(h) / ~0u</c> is the engine's .hlsl files', not the graphs'. v0.1.234 to v0.1.241 divided - a
    /// difference of at most 2^-23.
    /// </remarks>
    public static Vector3 HashStable(float value) => HashStable(new Vector3(value));

    /// <summary>math_utils' RampNorm.</summary>
    internal static float RampNorm(float value, float center)
    {
        const float eps = 1e-3f;
        center = Math.Clamp(center, eps, 1f - eps);
        bool mirror = center < 0.5f;
        float mirroredValue = mirror ? value : 1f - value;
        float mirroredCenter = mirror ? center : 1f - center;
        float twice = 2f * mirroredCenter;
        float ramp = mirroredValue / MathF.Max(1e-5f, (twice * twice * (1f - mirroredValue)) + mirroredValue);
        return mirror ? ramp : 1f - ramp;
    }

    /// <summary>math_utils' Gain.</summary>
    internal static float Gain(float value, float steepness)
    {
        const float eps = 1e-3f;
        steepness = Math.Clamp(steepness, -1f + eps, 1f - eps);
        float power = (1f + steepness) / (1f - steepness);
        float half = 0.5f * MathF.Pow(2f * (value < 0.5f ? value : 1f - value), power);
        return value < 0.5f ? half : 1f - half;
    }

    /// <summary>math_utils' SmoothStep - not util.hlsl's, which clamps the value first: CrashingFoamSurface includes math_utils.</summary>
    internal static float MathSmoothStep(float value, float center, float curve)
    {
        float power = (2f / MathF.Max(1e-3f, 1f - curve)) - 1f;
        return Saturated(1f - center
            + (-MathF.Pow(Saturated(1f - MathF.Max(center, value)), power) * MathF.Pow(Saturated(1f - center), -(power - 1f)))
            + (MathF.Pow(Saturated(MathF.Min(center, value)), power) * MathF.Pow(Saturated(center), -(power - 1f))));
    }

    private static Vector3 Fractional3(Vector3 v) => new(Fractional(v.X), Fractional(v.Y), Fractional(v.Z));
}
