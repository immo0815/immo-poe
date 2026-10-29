using System.Globalization;
using System.Numerics;
using System.Text;

namespace PoEformance.Game.Files;

/// <summary>
/// A shade program written out as HLSL - the same steps <see cref="ShadeProgram"/> runs, one statement each, for a graphics card to run instead.
/// </summary>
/// <remarks>
/// NOT A SECOND READING OF THE GAME'S GRAPHS. The graphs are read once, by
/// <see cref="ShadeProgram.Compile"/>: the stages ordered, the fragments followed, what the colour
/// does not read dropped. What comes out is a list of steps over registers, and this writes that list
/// out - a register a variable, a step a statement, each as <c>ShadeProgram.Run</c> does it and with
/// the same helpers line for line (<see cref="Library"/>) - so the card's picture is the processor's
/// by construction, not by a second implementation that happens to agree.
///
/// WHY NOT THE GAME'S OWN HLSL: the install ships it only as fragments, one per node, in the game's
/// own fragment language - macros, includes and stage hooks the engine assembles per material at run
/// time inside its own renderer. The program is already that assembly.
///
/// THE CONSTANTS ARE WRITTEN IN, to the bit (see <see cref="Number"/>): the compiler folds what it
/// can, and a program is compiled once per material.
///
/// WHAT THE INCLUDING SHADER PROVIDES: <c>float LinearOf(float)</c> and <c>float SrgbOf(float)</c>,
/// ShadeProgram.Linear and Srgb by the same tables; and a pixel shader to call <c>ShadeRun</c> from,
/// since a texture read takes its level from the screen's derivatives - the game's own way, and the
/// processor's on every triangle whose coordinates are straight - setting <c>ShadePixel</c> to its own
/// pixel first, where ReadPixelWetness reads the card's wetness pass. Everything else is in what this
/// writes: the sheets as <c>ShadeSheet0</c> on, from the slot asked for, read through the sampler
/// handed in, and what the drawing needs to know of the program as constants - <c>ShadeHasAlpha</c>,
/// <c>ShadeHasSpecular</c>, <c>ShadeHasGloss</c>, <c>ShadeUsesDepth</c>.
/// </remarks>
public static partial class ShadeHlsl
{
    /// <summary>The largest float, where nothing solid lies behind - ShadeProgram.Preset's.</summary>
    public const string Nothing = "3.40282347e38";

    /// <summary>
    /// The helpers the steps call - ShadeProgram's, line for line, under names of their own.
    /// </summary>
    /// <remarks>
    /// NOT A NUMBER IS KEPT AS THE PROCESSOR KEEPS IT, which is as a card does: saturate makes it
    /// nought, a comparison with it is false, pow of a negative is not a number - the processor's
    /// helpers were written to the card's rules in the first place (see ShadeProgram.Pow), and the
    /// shaders are compiled with IEEE strictness so the compiler does not reason it away.
    /// </remarks>
    public const string Library = """
        struct ShadeOut
        {
            float3 colour;
            float alpha;
            bool dropped;
            float3 specular;
            float gloss;
            float3 waves;
            float4 waterFoam;
            float4 waterState;
        };

        // ShadeProgram.Read's: an sRGB sheet's colour linearised by the table, its alpha as it is.
        float4 ShadeLinearised(float4 texel)
        {
            return float4(LinearOf(texel.x), LinearOf(texel.y), LinearOf(texel.z), texel.w);
        }

        float4 ShadeDivided(float4 a, float4 b)
        {
            return float4(
                abs(b.x) > 0.0 ? a.x / b.x : 0.0,
                abs(b.y) > 0.0 ? a.y / b.y : 0.0,
                abs(b.z) > 0.0 ? a.z / b.z : 0.0,
                abs(b.w) > 0.0 ? a.w / b.w : 0.0);
        }

        float4 ShadeSigned(float4 v)
        {
            return float4(v > 0.0) - float4(v < 0.0);
        }

        float ShadeDotted(float4 a, float4 b, int width)
        {
            return width == 2 ? (a.x * b.x) + (a.y * b.y)
                : width == 3 ? (a.x * b.x) + (a.y * b.y) + (a.z * b.z)
                : dot(a, b);
        }

        float4 ShadeNormalized(float4 v, int width)
        {
            float4 part = width == 2 ? float4(v.xy, 0.0, 0.0) : width == 3 ? float4(v.xyz, 0.0) : v;
            return part / (sqrt(dot(part, part)) + 1e-7);
        }

        float ShadeFitted(float value, float inMin, float inMax, float outMin, float outMax)
        {
            float divisor = inMax - inMin;
            return ((value - inMin) * (outMax - outMin) / (abs(divisor) > 0.0 ? divisor : 1.0)) + outMin;
        }

        float ShadeSmoothed(float center, float steepness, float value)
        {
            float power = clamp((2.0 / max(1e-3, 1.0 - steepness)) - 1.0, -1e5, 1e5);
            float said = 1.0 - center
                + ((-pow(saturate((1.0 - max(center, value)) / (1.0 - center)), power) * (1.0 - center))
                   + (pow(saturate(min(center, value) / center), power) * center));
            return isnan(said) ? center : said;
        }

        float ShadeHermite(float low, float high, float value)
        {
            float t = saturate((value - low) / (high - low));
            return t * t * (3.0 - (2.0 * t));
        }

        float ShadeCompared(float a, float b, int how)
        {
            bool said = how == 0 ? a == b
                : how == 1 ? a > b
                : how == 2 ? a < b
                : how == 3 ? a != 0.0 && b != 0.0
                : how == 4 ? a != 0.0 || b != 0.0
                : how == 5 ? a == 0.0
                : false;
            return said ? 1.0 : 0.0;
        }

        float4 ShadeTurn(float hue, float saturation, float brightness)
        {
            float angle = hue / 180.0 * 3.1425;
            return float4(cos(angle), sin(angle), saturation * 2.0, (brightness * 2.0) - 1.0);
        }

        float4 ShadeRemapped(float4 colour, float4 turn)
        {
            const float k = 0.57735;
            float3 c = colour.xyz;
            float cosine = turn.x;
            float3 crossed = float3(k * (c.z - c.y), k * (c.x - c.z), k * (c.y - c.x));
            float3 hue = (c * cosine) + (crossed * turn.y) + (float3(k, k, k) * (k * (c.x + c.y + c.z)) * (1.0 - cosine));
            float3 bright = hue + turn.www;
            float intensity = dot(bright, float3(0.299, 0.587, 0.114));
            return float4(intensity.xxx + ((bright - intensity.xxx) * turn.z), 0.0);
        }

        float ShadeHash33X(float3 p3)
        {
            p3 *= float3(0.1031, 0.1030, 0.0973);
            p3 -= floor(p3);
            float d = (p3.x * (p3.y + 19.19)) + (p3.y * (p3.x + 19.19)) + (p3.z * (p3.z + 19.19));
            p3 += d.xxx;
            float h = (p3.x + p3.y) * p3.z;
            return h - floor(h);
        }

        float ShadeValueNoise(float3 x)
        {
            float3 p = floor(x);
            float3 f = x - p;
            f = f * f * (3.0 - (2.0 * f));
            float a = ShadeHash33X(p);
            float b = ShadeHash33X(p + float3(1.0, 0.0, 0.0));
            float c = ShadeHash33X(p + float3(0.0, 1.0, 0.0));
            float d = ShadeHash33X(p + float3(1.0, 1.0, 0.0));
            float e = ShadeHash33X(p + float3(0.0, 0.0, 1.0));
            float g = ShadeHash33X(p + float3(1.0, 0.0, 1.0));
            float h = ShadeHash33X(p + float3(0.0, 1.0, 1.0));
            float i = ShadeHash33X(p + float3(1.0, 1.0, 1.0));
            float near = (a + (f.x * (b - a))) + (f.y * ((c + (f.x * (d - c))) - (a + (f.x * (b - a)))));
            float far = (e + (f.x * (g - e))) + (f.y * ((h + (f.x * (i - h))) - (e + (f.x * (g - e)))));
            return near + (f.z * (far - near));
        }

        uint3 ShadePcg(uint3 v)
        {
            v = (v * 1664525u) + 1013904223u;
            v.x += v.y * v.z;
            v.y += v.z * v.x;
            v.z += v.x * v.y;
            v ^= v >> 16;
            v.x += v.y * v.z;
            v.y += v.z * v.x;
            v.z += v.x * v.y;
            return v;
        }

        float ShadeHashUnit(uint hash)
        {
            return saturate(asfloat((hash >> 9) | 0x3f800000u) - 1.0);
        }

        float ShadeGradient(int3 cell, float3 pos)
        {
            uint3 h = ShadePcg(asuint(cell));
            float3 grad = float3(ShadeHashUnit(h.x) - 0.5, ShadeHashUnit(h.y) - 0.5, ShadeHashUnit(h.z) - 0.5);
            grad /= sqrt(dot(grad, grad));
            return dot(pos - float3(cell), grad);
        }

        float ShadePerlinNoise(float3 pos)
        {
            int3 b = int3(floor(pos));
            float3 ratio = pos - floor(pos);
            float3 r2 = ratio * ratio;
            float3 r3 = r2 * ratio;
            ratio = (3.0 * r2) - (2.0 * r3);
            float3 inverse = 1.0 - ratio;
            float res = 0.0;
            res += ShadeGradient(b, pos) * inverse.x * inverse.y * inverse.z;
            res += ShadeGradient(b + int3(1, 0, 0), pos) * ratio.x * inverse.y * inverse.z;
            res += ShadeGradient(b + int3(1, 1, 0), pos) * ratio.x * ratio.y * inverse.z;
            res += ShadeGradient(b + int3(0, 1, 0), pos) * inverse.x * ratio.y * inverse.z;
            res += ShadeGradient(b + int3(0, 0, 1), pos) * inverse.x * inverse.y * ratio.z;
            res += ShadeGradient(b + int3(1, 0, 1), pos) * ratio.x * inverse.y * ratio.z;
            res += ShadeGradient(b + int3(1, 1, 1), pos) * ratio.x * ratio.y * ratio.z;
            res += ShadeGradient(b + int3(0, 1, 1), pos) * inverse.x * ratio.y * ratio.z;
            return (res / (sqrt(3.0) / 2.0) * 0.5) + 0.5;
        }

        float4 ShadeVibrant(float value, float4 colour)
        {
            float lift = saturate((3.0 * value * value) - (2.0 * value * value * value));
            return float4(pow(lift, 1.0 / (colour.x + 1e-6)), pow(lift, 1.0 / (colour.y + 1e-6)), pow(lift, 1.0 / (colour.z + 1e-6)), 0.0);
        }

        float4 ShadeRotated(float angle, float4 uv, float4 centre)
        {
            float s;
            float c;
            sincos(angle, s, c);
            float u = uv.x - centre.x;
            float v = uv.y - centre.y;
            return float4((c * u) + (s * v) + centre.x, (-s * u) + (c * v) + centre.y, 0.0, 0.0);
        }

        float4 ShadePolar(float4 radius)
        {
            float len = sqrt((radius.x * radius.x) + (radius.y * radius.y));
            return float4(len, (atan2(radius.y / len, radius.x / len) / 3.1415 * 0.5) + 0.5, 0.0, 0.0);
        }

        float4 ShadeTriplanar(float4 alongX, float4 alongY, float4 alongZ, float4 normal, bool absolute)
        {
            float3 n = normal.xyz;
            float3 weights;
            if (absolute)
            {
                weights = abs(n / (length(n) + 1e-7));
                weights /= max(1e-7, weights.x + weights.y + weights.z);
            }
            else
            {
                n /= max(1e-5, length(n));
                weights = n * n;
                weights /= max(1e-5, weights.x + weights.y + weights.z);
            }

            return (alongX * weights.x) + (alongY * weights.y) + (alongZ * weights.z);
        }

        float4 ShadeHardLit(float4 one, float4 other)
        {
            return float4(
                other.x < 0.5 ? 2.0 * one.x * other.x : 1.0 - (2.0 * (1.0 - one.x) * (1.0 - other.x)),
                other.y < 0.5 ? 2.0 * one.y * other.y : 1.0 - (2.0 * (1.0 - one.y) * (1.0 - other.y)),
                other.z < 0.5 ? 2.0 * one.z * other.z : 1.0 - (2.0 * (1.0 - one.z) * (1.0 - other.z)),
                other.w < 0.5 ? 2.0 * one.w * other.w : 1.0 - (2.0 * (1.0 - one.w) * (1.0 - other.w)));
        }

        float ShadeDeep(float4 eye, float4 at)
        {
            return (at.x * eye.x) + (at.y * eye.y) + (at.z * eye.z) + eye.w;
        }

        // ShadeProgram.Raying: the view's ray through a point, a unit of depth long - the eye's way on a parallel view.
        float3 ShadeRaying(float4 eye, float4 viewer, float behind, float4 at)
        {
            if (!(viewer.w > 0.5) || !(behind < 3.40282347e38))
            {
                return eye.xyz;
            }

            float deep = ShadeDeep(eye, at);
            return deep > 1e-6 ? (at.xyz - viewer.xyz) / deep : eye.xyz;
        }

        // ShadeProgram.Viewed: ViewDir - the eye's way on a parallel view, from the eye to the place in perspective.
        float4 ShadeViewed(float4 eye, float4 viewer, float4 at)
        {
            if (viewer.w > 0.5)
            {
                float3 way = at.xyz - viewer.xyz;
                float length = sqrt(dot(way, way));
                if (length > 1e-6)
                {
                    return float4(way / length, 0.0);
                }
            }

            return float4(eye.xyz, 0.0);
        }
        """;

    /// <summary>The slot the ripple texture is read from - ShadeProgram.Ripples, bound by the drawing for the pixel and the vertex shaders alike.</summary>
    public const int RippleSlot = 10;

    /// <summary>The slot the room's shore is read from - ShoreField.Texels, bound by the drawing where it has one.</summary>
    public const int ShoreSlot = 11;

    /// <summary>The slot the swell's cross-section is read from - ShadeProgram.Swells, bound with the ripple texture.</summary>
    public const int SwellSlot = 12;

    /// <summary>The slot the beach waves' wave shape is read from - ShadeProgram.BeachProfile, bound for the pixel and the vertex shaders alike.</summary>
    public const int BeachProfileSlot = 13;

    /// <summary>The slot the beach waves' noise is read from - ShadeProgram.BeachNoise.</summary>
    public const int BeachNoiseSlot = 14;

    /// <summary>
    /// The slot the card's wetness pass is read from by a program that reads it (ShadeProgram.ReadsWetness) - clear of every sheet a
    /// program reads, which run on from the fixed lookups as many as it has, and of the vertex shader's skin, two slots above.
    /// </summary>
    public const int WetnessSlot = 125;

    /// <summary>
    /// ShadeProgram.Ocean, WindRipples and Ripple, line for line, for a program with a <c>Waves</c> step.
    /// </summary>
    public static readonly string WavesLibrary = Waves();

    /// <summary>
    /// The engine's helpers the spray and the beach waves share - hash33, hash33Stable, GetLinearInterpNodes, RampNorm, Gain, SmoothStep, RgToGradYFix.
    /// </summary>
    public static readonly string FoamLibrary = Foams();

    /// <summary>
    /// ShadeProgram.Spraying, line for line, for a program with a <c>Spray</c> step - after <see cref="WavesLibrary"/>, whose shore it
    /// reads, and <see cref="FoamLibrary"/>.
    /// </summary>
    public static readonly string SprayLibrary = Sprays();

    /// <summary>
    /// ShadeProgram.Beaching and BeachLift, line for line, for a program with a <c>Beach</c> step and the beach's vertex shader - after
    /// <see cref="WavesLibrary"/> and <see cref="FoamLibrary"/>.
    /// </summary>
    public static readonly string BeachLibrary = Beaches();

    /// <summary>
    /// The program as HLSL: its sheets, <see cref="Library"/>, and <c>ShadeOut ShadeRun(...)</c> - the colour as ShadeProgram.Colour returns it.
    /// </summary>
    /// <param name="program">The program.</param>
    /// <param name="firstSheet">The texture slot its first sheet is read from; the rest follow.</param>
    /// <remarks>
    /// <c>ShadeRun(SamplerState wrap, float2 uv, float3 place, float3 turn, float4 tint, float behind, float clock, float4 eye, float3 dust, float4 water, float4 flow, float4 wind, float4 shore, float4 swell, float4 viewer)</c>:
    /// the coordinates, the place and the interpolated normal in the model's space, the vertex
    /// colour, the solid depth behind (<see cref="Nothing"/> where none), the clock, the way into
    /// the picture, the area's dust colour and its water - ShadeProgram's fixed registers, in its order.
    /// </remarks>
    public static string Of(ShadeProgram program, int firstSheet)
    {
        ArgumentNullException.ThrowIfNull(program);
        var hlsl = new StringBuilder(4096);
        for (var sheet = 0; sheet < program.Textures.Count; sheet++)
        {
            hlsl.Append(CultureInfo.InvariantCulture, $"Texture2D ShadeSheet{sheet} : register(t{firstSheet + sheet});").AppendLine();
        }

        // WHAT THE DRAWING ASKS OF THE PROGRAM, as constants the compiler folds - see ShadeProgram's own.
        hlsl.Append("static const bool ShadeHasAlpha = ").Append(Truth(program.HasAlpha)).AppendLine(";");
        hlsl.Append("static const bool ShadeHasSpecular = ").Append(Truth(program.HasSpecular)).AppendLine(";");
        hlsl.Append("static const bool ShadeHasGloss = ").Append(Truth(program.HasGloss)).AppendLine(";");
        hlsl.Append("static const bool ShadeUsesDepth = ").Append(Truth(program.UsesDepth)).AppendLine(";");
        hlsl.Append("static const bool ShadeHasWaves = ").Append(Truth(program.HasWaves)).AppendLine(";");
        hlsl.Append("static const bool ShadeBeach = ").Append(Truth(program.Beaches)).AppendLine(";");
        hlsl.Append("static const bool ShadeWater = ").Append(Truth(program.LightsWater)).AppendLine(";");

        // THE PIXEL THE PROGRAM RUNS FOR, set by the including shader before ShadeRun - what ReadPixelWetness reads the card's
        // wetness pass at (ShadeProgram.ReadsWetness); off the picture until set, where a read gives nought.
        hlsl.AppendLine("static float2 ShadePixel = float2(-1.0, -1.0);");
        hlsl.Append(CultureInfo.InvariantCulture, $"Texture2D<float> ShadeWetness : register(t{WetnessSlot});").AppendLine();
        hlsl.AppendLine(Library);
        bool sprays = false;
        bool fields = false;
        bool rivers = false;
        foreach (ShadeProgram.Step step in program.Steps)
        {
            sprays |= step.Op == ShadeProgram.Op.Spray;
            fields |= step.Op == ShadeProgram.Op.Field;
            rivers |= step.Op == ShadeProgram.Op.River;
        }

        foreach (ShadeProgram.Step step in program.Steps)
        {
            if (step.Op is ShadeProgram.Op.Waves or ShadeProgram.Op.Foam or ShadeProgram.Op.Spray or ShadeProgram.Op.Beach or ShadeProgram.Op.Field
                or ShadeProgram.Op.River)
            {
                hlsl.AppendLine(WavesLibrary);
                break;
            }
        }

        if (sprays || program.Beaches || fields || rivers)
        {
            hlsl.AppendLine(FoamLibrary);
        }

        if (rivers)
        {
            hlsl.AppendLine(RiverLibrary);
        }

        if (sprays)
        {
            hlsl.AppendLine(SprayLibrary);
        }

        // THE FIELD WAVES READ THE BEACH'S NOISE, PROFILE HELPERS AND RAMPS - its library before theirs.
        if (program.Beaches || fields)
        {
            hlsl.AppendLine(BeachLibrary);
        }

        if (fields)
        {
            hlsl.AppendLine(FieldLibrary);
        }

        hlsl.AppendLine("ShadeOut ShadeRun(SamplerState wrap, float2 uv, float3 place, float3 turn, float4 tint, float behind, float clock, float4 eye, float3 dust, float4 water, float4 flow, float4 wind, float4 shore, float4 swell, float4 viewer)");
        hlsl.AppendLine("{");

        // THE FIXED REGISTERS, as Colour and Preset set them.
        hlsl.AppendLine("    float4 r0 = float4(uv, 0.0, 0.0);");
        hlsl.AppendLine("    float4 r1 = float4(place, 0.0);");
        hlsl.AppendLine("    float4 r2 = float4(dot(turn, turn) > 1e-12 ? normalize(turn) : turn, 0.0);");
        hlsl.AppendLine("    float4 r3 = float4(turn, 0.0);");
        hlsl.AppendLine("    float4 r4 = tint;");
        hlsl.AppendLine("    float4 r5 = clock.xxxx;");
        hlsl.AppendLine("    float4 r6 = behind.xxxx;");
        hlsl.AppendLine("    float4 r7 = eye;");
        if (program.UsesTangents)
        {
            // FixModelTBN's frame from the screen's derivatives - GetScreenspaceTangentFrame, the
            // engine's own: dp = T du + B dv along x and along y, solved. On a flat triangle it is
            // ShadeProgram.Spanned's answer from the edges.
            hlsl.AppendLine("    float2 uvx = ddx(uv);");
            hlsl.AppendLine("    float2 uvy = ddy(uv);");
            hlsl.AppendLine("    float3 px = ddx(place);");
            hlsl.AppendLine("    float3 py = ddy(place);");
            hlsl.AppendLine("    float spans = (uvx.x * uvy.y) - (uvx.y * uvy.x);");
            hlsl.AppendLine("    float4 r8 = abs(spans) > 0.0 ? float4(((px * uvy.y) - (py * uvx.y)) / spans, 0.0) : float4(0.0, 0.0, 0.0, 0.0);");
            hlsl.AppendLine("    float4 r9 = abs(spans) > 0.0 ? float4(((py * uvx.x) - (px * uvy.x)) / spans, 0.0) : float4(0.0, 0.0, 0.0, 0.0);");
        }
        else
        {
            hlsl.AppendLine("    float4 r8 = float4(0.0, 0.0, 0.0, 0.0);");
            hlsl.AppendLine("    float4 r9 = float4(0.0, 0.0, 0.0, 0.0);");
        }

        hlsl.AppendLine("    float4 r10 = float4(0.0, 0.0, 0.0, 0.0);");
        hlsl.AppendLine("    float4 r11 = float4(dust, 0.0);");
        hlsl.AppendLine("    float4 r12 = water;");
        hlsl.AppendLine("    float4 r13 = flow;");
        hlsl.AppendLine("    float4 r14 = wind;");
        hlsl.AppendLine("    float4 r15 = shore;");
        hlsl.AppendLine("    float4 r16 = swell;");
        hlsl.AppendLine("    float4 r17 = viewer;");
        if (fields)
        {
            // THE FIELD WAVES' FOAM IS READ AT THE GRADIENTS OF ITS COORDINATES, taken here outside any branch - see Field.
            hlsl.AppendLine("    float2 fieldDx = ddx(place.xy);");
            hlsl.AppendLine("    float2 fieldDy = ddy(place.xy);");
        }

        // EVERY OTHER REGISTER STARTS AT NOUGHT, as the processor's zeroed scratch does - a swizzle
        // writes part of one and leaves the rest as it found it.
        ReadOnlySpan<int> constantAt = program.ConstantRegisters;
        ReadOnlySpan<Vector4> constants = program.ConstantValues;
        var constant = new Vector4?[program.Registers];
        for (var one = 0; one < constantAt.Length; one++)
        {
            constant[constantAt[one]] = constants[one];
        }

        for (var register = FixedRegisters; register < program.Registers; register++)
        {
            hlsl.Append(CultureInfo.InvariantCulture, $"    float4 r{register} = ")
                .Append(constant[register] is { } value ? Vector(value) : "float4(0.0, 0.0, 0.0, 0.0)")
                .AppendLine(";");
        }

        int parallax = 0;

        // A SPRAY'S THREE OUTPUTS ARE ONE SUM, written once into a variable of its own and read by each - while none of what it
        // was worked out from has been written since.
        var versions = new int[program.Registers];
        var sprayed = new Dictionary<(int, int, int, int, int, int, int, int, int), string>();
        foreach (ShadeProgram.Step step in program.Steps)
        {
            if (step.Op == ShadeProgram.Op.Spray)
            {
                Spray(hlsl, program, step, versions, sprayed);
            }
            else if (step.Op == ShadeProgram.Op.Beach)
            {
                Beach(hlsl, program, step, versions, sprayed);
            }
            else if (step.Op == ShadeProgram.Op.Field)
            {
                Field(hlsl, step, versions, sprayed);
            }
            else
            {
                Step(hlsl, program, step, ref parallax);
            }

            versions[step.To]++;
        }

        hlsl.AppendLine("    ShadeOut said;");
        hlsl.Append(CultureInfo.InvariantCulture, $"    float4 colour = r{program.Result};").AppendLine();
        hlsl.AppendLine("    said.colour = float3(SrgbOf(colour.x), SrgbOf(colour.y), SrgbOf(colour.z));");
        hlsl.AppendLine("    said.alpha = colour.w;");
        hlsl.AppendLine(program.Discards ? "    said.dropped = r10.x > 0.0;" : "    said.dropped = false;");
        hlsl.AppendLine(program.SpecularRegister >= 0
            ? string.Create(CultureInfo.InvariantCulture, $"    said.specular = r{program.SpecularRegister}.xyz;")
            : "    said.specular = float3(0.0, 0.0, 0.0);");
        hlsl.AppendLine(program.GlossRegister >= 0
            ? string.Create(CultureInfo.InvariantCulture, $"    said.gloss = r{program.GlossRegister}.x;")
            : "    said.gloss = 0.0;");
        hlsl.AppendLine(program.WavesRegister >= 0
            ? string.Create(CultureInfo.InvariantCulture, $"    said.waves = r{program.WavesRegister}.xyz;")
            : "    said.waves = float3(0.0, 0.0, 0.0);");
        hlsl.AppendLine(program.LightsWater
            ? string.Create(CultureInfo.InvariantCulture, $"    said.waterFoam = r{program.WaterFoamRegister};")
            : "    said.waterFoam = float4(0.0, 0.0, 0.0, 0.0);");
        hlsl.AppendLine(program.LightsWater
            ? string.Create(CultureInfo.InvariantCulture, $"    said.waterState = r{program.WaterStateRegister};")
            : "    said.waterState = float4(0.0, 0.0, 0.0, 0.0);");
        hlsl.AppendLine("    return said;");
        hlsl.AppendLine("}");
        return hlsl.ToString();
    }

    /// <summary><see cref="FoamLibrary"/>: the helpers of the engine's the spray and the beach waves share.</summary>
    private static string Foams() => """
        // The engine's hash33: three fractions of a float3, no integers.
        float3 ShadeHash33(float3 p)
        {
            p = frac(p * float3(0.1031, 0.1030, 0.0973));
            p += dot(p, p.yxz + 19.19);
            return frac((p.xxy + p.yxx) * p.zyx);
        }

        // The engine's hash33Stable: PCG's three rounds over the numbers' bits, each a fraction by HashUToF - ShadeProgram.HashStable.
        float3 ShadeHashStable3(float3 value)
        {
            uint3 v = asuint(value);
            v = (v * 1664525u) + 1013904223u;
            v.x += v.y * v.z;
            v.y += v.z * v.x;
            v.z += v.x * v.y;
            v ^= v >> 16u;
            v.x += v.y * v.z;
            v.y += v.z * v.x;
            v.z += v.x * v.y;
            return float3(ShadeHashUnit(v.x), ShadeHashUnit(v.y), ShadeHashUnit(v.z));
        }

        // The same of a number spread to all three.
        float3 ShadeHashStable(float value)
        {
            return ShadeHashStable3(float3(value, value, value));
        }

        // GetLinearInterpNodes.
        void ShadeInterpNodes(float phase, out float2 phases, out float2 seeds, out float2 weights)
        {
            float2 nodes = float2(0.5, 0.0) + (phase * 0.5);
            phases = nodes - floor(nodes);
            seeds = (floor(nodes) * 2.0) + float2(0.0, 1.0);
            weights = min(phases, 1.0 - phases) * 2.0;
        }

        // math_utils' RampNorm, Gain and SmoothStep.
        float ShadeRampNorm(float value, float center)
        {
            center = clamp(center, 1e-3, 1.0 - 1e-3);
            bool mirror = center < 0.5;
            float mirroredValue = mirror ? value : 1.0 - value;
            float mirroredCenter = mirror ? center : 1.0 - center;
            float twice = 2.0 * mirroredCenter;
            float ramp = mirroredValue / max(1e-5, (twice * twice * (1.0 - mirroredValue)) + mirroredValue);
            return mirror ? ramp : 1.0 - ramp;
        }

        float ShadeGain(float value, float steepness)
        {
            steepness = clamp(steepness, -1.0 + 1e-3, 1.0 - 1e-3);
            float power = (1.0 + steepness) / (1.0 - steepness);
            float part = 0.5 * pow(2.0 * (value < 0.5 ? value : 1.0 - value), power);
            return value < 0.5 ? part : 1.0 - part;
        }

        float ShadeMathSmoothStep(float value, float center, float curve)
        {
            float power = (2.0 / max(1e-3, 1.0 - curve)) - 1.0;
            return saturate(1.0 - center
                + (-pow(saturate(1.0 - max(center, value)), power) * pow(saturate(1.0 - center), -(power - 1.0)))
                + (pow(saturate(min(center, value)), power) * pow(saturate(center), -(power - 1.0))));
        }

        // RgToGradYFix.
        float2 ShadeRgToGradYFix(float2 rg)
        {
            float nx = (rg.x - 0.5) * 2.0;
            float ny = (rg.y - 0.5) * 2.0;
            float nz = sqrt(saturate(1.0 - ((nx * nx) + (ny * ny))));
            float under = abs(nz) > 1e-7 ? nz : 1e-7;
            return float2(-nx / under, ny / under);
        }

        float4 ShadeSprayRead(Texture2D sheet, bool srgb, SamplerState wrap, float2 uv)
        {
            float4 texel = sheet.Sample(wrap, uv);
            return srgb ? ShadeLinearised(texel) : texel;
        }
        """;

    /// <summary><see cref="SprayLibrary"/>: CrashingFoamSurface and what it includes of the engine's - see ShadeProgram.Spraying.</summary>
    private static string Sprays() => $$"""
        struct ShadeSprayOut
        {
            float4 colour;
            float4 normalAo;
            float4 wetGloss;
        };

        // GetShoreData's distances in world units: the shore's and out to sea's - ShadeProgram.ShoreWorld.
        float ShadeShoreWorld(float2 place, float4 box, out float ocean)
        {
            float shore;
            float b;
            ShadeShoreAt(place, box, shore, b);
            ocean = (1.0 - b) * {{Number(ShoreField.Reach)}};
            return shore * {{Number(ShoreField.Reach)}};
        }

        // GetFoamScrollingLayer with no turbulence and seed nought - ShadeProgram.ScrollingFoam.
        void ShadeScrollingFoam(float2 uv, float2 velocity, float phase, SamplerState wrap, Texture2D colourSheet, bool colourSrgb, Texture2D normalSheet, bool normalSrgb, out float4 colour, out float2 grad, out float ao, out float rough)
        {
            float2 phases;
            float2 seeds;
            float2 weights;
            ShadeInterpNodes(phase, phases, seeds, weights);
            colour = float4(0.0, 0.0, 0.0, 0.0);
            grad = float2(0.0, 0.0);
            ao = 0.0;
            rough = 0.0;
            [unroll] for (int layer = 0; layer < 2; layer++)
            {
                float3 hash = ShadeHash33(float3(seeds[layer], 0.0, 0.0));
                float angle = 3.1415 * 2.0 * hash.z;
                float s = sin(angle);
                float c = cos(angle);
                float2 flow = uv - (velocity * ((phases[layer] * 2.0) - 1.0)) + hash.xy;
                float2 turned = float2((flow.x * c) - (flow.y * s), (flow.x * s) + (flow.y * c));
                float4 foam = ShadeSprayRead(colourSheet, colourSrgb, wrap, turned);
                float4 bumps = ShadeSprayRead(normalSheet, normalSrgb, wrap, turned);
                float2 back = ShadeRgToGradYFix(bumps.xy);
                float2 bent = float2((back.x * c) + (back.y * s), (back.y * c) - (back.x * s));
                colour += foam * weights[layer];
                grad += bent * weights[layer];
                ao += bumps.z * weights[layer];
                rough += bumps.w * weights[layer];
            }
        }

        // CrashingFoamSurface - ShadeProgram.Spraying: place, normal, (curvature, spray height, clock), the shore's box.
        ShadeSprayOut ShadeSpray(float3 place, float3 normal, float4 packed, float4 box, SamplerState wrap, Texture2D colourSheet, bool colourSrgb, Texture2D normalSheet, bool normalSrgb, Texture2D noiseSheet, bool noiseSrgb)
        {
            float curvature = packed.x;
            float sprayHeight = packed.y;
            float clock = packed.z;
            normal = dot(normal, normal) > 1e-12 ? normalize(normal) : normal;

            // GetShoreFieldData: the slope out to sea by central differences a texel apart.
            float2 at = place.xy;
            float eps = box.z;
            float oceanDist;
            float shoreDist = ShadeShoreWorld(at, box, oceanDist);
            float xp;
            float xn;
            float yp;
            float yn;
            ShadeShoreWorld(at + float2(eps, 0.0), box, xp);
            ShadeShoreWorld(at - float2(eps, 0.0), box, xn);
            ShadeShoreWorld(at + float2(0.0, eps), box, yp);
            ShadeShoreWorld(at - float2(0.0, eps), box, yn);
            float2 oceanSlope = float2(xp - xn, yp - yn) * (0.5 / eps);

            // GetFieldWaveGeom's wave_coord.
            float2 uv = at * 0.00012;
            float4 still = noiseSheet.SampleLevel(wrap, uv, 0.0);
            float stationary = (noiseSrgb ? ShadeLinearised(still) : still).y;
            float2 velocity = -oceanSlope * ({{Number(ShadeProgram.FieldWaveSpeed)}} * 0.00012 / 0.1);
            float2 phases;
            float2 seeds;
            float2 weights;
            ShadeInterpNodes(clock * 0.1, phases, seeds, weights);
            float advected = 0.0;
            [unroll] for (int layer = 0; layer < 2; layer++)
            {
                float3 hash = ShadeHashStable(seeds[layer]);
                float2 layerUv = uv - (velocity * ((phases[layer] - 0.5) * 2.0));
                float4 moved = noiseSheet.SampleLevel(wrap, layerUv + hash.xy, 0.0);
                advected += (noiseSrgb ? ShadeLinearised(moved) : moved).y * weights[layer];
            }

            float waveCoord = (oceanDist * (-1.0 / {{Number(ShadeProgram.FieldWaveLength)}})) - (clock * {{Number(ShadeProgram.FieldWaveSpeed)}} / {{Number(ShadeProgram.FieldWaveLength)}}) + advected + stationary;
            float sprayPhase = saturate(frac(waveCoord - 0.15));
            float sprayIntensity = sprayPhase * sprayPhase * (1.0 - pow(sprayPhase, 5.0));
            float heightFade = saturate(0.8 + (place.z / sprayHeight))
                * saturate((150.0 - place.z) / 100.0)
                * saturate((shoreDist + 200.0) * 0.01);
            float fadePhase = ShadeRampNorm(heightFade, ((1.0 - sprayIntensity) * 0.9) + 0.01);
            float foamIntensity = ShadeRampNorm(fadePhase, curvature);

            float3 flow = lerp(float3(0.0, 0.0, 100.0), float3(0.0, 0.0, 50.0) + (float3(oceanSlope, 0.0) * (-{{Number(ShadeProgram.FieldWaveSpeed)}} * 0.5)), sprayIntensity);
            float3 along = flow - (normal * (dot(normal, flow) / dot(normal, normal)));

            // GetTriplanarScrollingFoam.
            float3 foamPlace = place * 0.002;
            float3 foamVelocity = along * 0.002;
            float4 c0;
            float4 c1;
            float4 c2;
            float2 g0;
            float2 g1;
            float2 g2;
            float a0;
            float a1;
            float a2;
            float s0;
            float s1;
            float s2;
            ShadeScrollingFoam(foamPlace.yz, foamVelocity.yz, clock, wrap, colourSheet, colourSrgb, normalSheet, normalSrgb, c0, g0, a0, s0);
            ShadeScrollingFoam(foamPlace.xz, foamVelocity.xz, clock, wrap, colourSheet, colourSrgb, normalSheet, normalSrgb, c1, g1, a1, s1);
            ShadeScrollingFoam(foamPlace.xy, foamVelocity.xy, clock, wrap, colourSheet, colourSrgb, normalSheet, normalSrgb, c2, g2, a2, s2);
            float3 grad = (float3(0.0, g0.x, g0.y) + float3(g1.x, 0.0, g1.y) + float3(g2.x, g2.y, 0.0)) * 0.8;
            float3 square = normal * normal;
            float3 shares = square / (square.x + square.y + square.z);
            float3 perpendicular = grad - (normal * dot(grad, normal));
            float3 foamNormal = (normal - perpendicular) / (dot(perpendicular, perpendicular) + 1.0);
            float4 colour = (c0 * shares.x) + (c1 * shares.y) + (c2 * shares.z);
            float rough = (s0 * shares.x) + (s1 * shares.y) + (s2 * shares.z);
            float ao = (a0 * shares.x) + (a1 * shares.y) + (a2 * shares.z);
            colour.w = ShadeMathSmoothStep(foamIntensity, 1.0 - ShadeGain(colour.w, 0.1), 0.5);

            // NO SHORE, NO SPRAY - chosen after the sum rather than before it, so every read stays in the program's own flow.
            ShadeSprayOut said;
            bool shored = box.w > 0.5;
            said.colour = shored ? colour : float4(0.0, 0.0, 0.0, 0.0);
            said.normalAo = shored ? float4(foamNormal, ao) : float4(normal, 1.0);
            said.wetGloss = shored ? float4(ShadeRampNorm(heightFade, curvature), 1.0 - rough, 0.0, 0.0) : float4(0.0, 0.0, 0.0, 0.0);
            return said;
        }
        """;

    /// <summary>
    /// <see cref="WavesLibrary"/>: the ripple texture's read, the shore's and the swell's, the engine's noise, and the ocean's surface.
    /// </summary>
    private static string Waves()
    {
        int side = ShadeProgram.RippleSide;
        return $$"""
            Texture2D<float4> ShadeRipples : register(t{{RippleSlot}});
            Texture2D<float4> ShadeShore : register(t{{ShoreSlot}});
            Texture2D<float4> ShadeSwell : register(t{{SwellSlot}});

            // ShadeProgram.Ripple: the four nearest texels' centres of the ripple texture, mixed by hand.
            float ShadeRipple(float qx, float qy, out float gx, out float gy)
            {
                float sx = (qx * 4.0) - 0.5;
                float sy = (qy * 4.0) - 0.5;
                float fx0 = floor(sx);
                float fy0 = floor(sy);
                float fx = sx - fx0;
                float fy = sy - fy0;
                int x0 = ((int)fx0) & {{side - 1}};
                int y0 = ((int)fy0) & {{side - 1}};
                int x1 = (x0 + 1) & {{side - 1}};
                int y1 = (y0 + 1) & {{side - 1}};
                float3 a = ShadeRipples.Load(int3(x0, y0, 0)).xyz;
                float3 b = ShadeRipples.Load(int3(x1, y0, 0)).xyz;
                float3 c = ShadeRipples.Load(int3(x0, y1, 0)).xyz;
                float3 d = ShadeRipples.Load(int3(x1, y1, 0)).xyz;
                float3 top = a + ((b - a) * fx);
                float3 bottom = c + ((d - c) * fx);
                float3 mixed = top + ((bottom - top) * fy);
                gx = mixed.y * 12.0;
                gy = mixed.z * 12.0;
                return (mixed.x - 0.4) * 3.0;
            }

            // ShoreField.At and GroundAt: the four nearest texels' centres of the room's shore, held at the box's edges - the shore's
            // distance, the distance out to sea and the ground's shore's distance (ShoreField.CardTexels).
            float3 ShadeShoreAll(float2 place, float4 box)
            {
                uint width;
                uint height;
                ShadeShore.GetDimensions(width, height);
                float sx = clamp(((place.x - box.x) / box.z) - 0.5, 0.0, (float)width - 1.0);
                float sy = clamp(((place.y - box.y) / box.z) - 0.5, 0.0, (float)height - 1.0);
                int x0 = (int)sx;
                int y0 = (int)sy;
                int x1 = min(x0 + 1, (int)width - 1);
                int y1 = min(y0 + 1, (int)height - 1);
                float fx = sx - x0;
                float fy = sy - y0;
                float3 a = ShadeShore.Load(int3(x0, y0, 0)).xyz;
                float3 b = ShadeShore.Load(int3(x1, y0, 0)).xyz;
                float3 c = ShadeShore.Load(int3(x0, y1, 0)).xyz;
                float3 d = ShadeShore.Load(int3(x1, y1, 0)).xyz;
                float3 top = a + ((b - a) * fx);
                float3 bottom = c + ((d - c) * fx);
                return top + ((bottom - top) * fy);
            }

            void ShadeShoreAt(float2 place, float4 box, out float shore, out float ocean)
            {
                float3 mixed = ShadeShoreAll(place, box);
                shore = mixed.x;
                ocean = mixed.y;
            }

            // ShadeProgram.Swell: the swell's cross-section, wrapping along the phase and held along the shore's distance, its own size.
            float4 ShadeSwellAt(float phase, float near)
            {
                uint across;
                uint down;
                ShadeSwell.GetDimensions(across, down);
                int width = (int)across;
                int height = (int)down;
                float sx = (phase * (float)width) - 0.5;
                float sy = clamp((near * (float)height) - 0.5, 0.0, (float)(height - 1));
                float fx0 = floor(sx);
                float fx = sx - fx0;
                int x0 = ((((int)fx0) % width) + width) % width;
                int x1 = (x0 + 1) % width;
                int y0 = (int)sy;
                int y1 = min(y0 + 1, height - 1);
                float fy = sy - y0;
                float4 a = ShadeSwell.Load(int3(x0, y0, 0));
                float4 b = ShadeSwell.Load(int3(x1, y0, 0));
                float4 c = ShadeSwell.Load(int3(x0, y1, 0));
                float4 d = ShadeSwell.Load(int3(x1, y1, 0));
                float4 top = a + ((b - a) * fx);
                float4 bottom = c + ((d - c) * fx);
                return top + ((bottom - top) * fy);
            }

            float ShadeFractional(float x)
            {
                return x - floor(x);
            }

            // ShadeProgram.Hash: the engine's Hash, as water.ffx writes it.
            float ShadeHash(float p)
            {
                float px = ShadeFractional(p * 4.438975);
                float py = ShadeFractional(p * 3.972973);
                float dotted = (py * (px + 19.19)) + (px * (py + 19.19));
                px += dotted;
                py += dotted;
                return ShadeFractional(px * py);
            }

            // ShadeProgram.SmoothNoise: GetSmoothNoise's value, minus one to one.
            float ShadeSmoothNoise(float x, float y)
            {
                float ix = floor(x);
                float iy = floor(y);
                float fx = x - ix;
                float fy = y - iy;
                float seed = ix + (iy * 57.0);
                float v00 = ShadeHash(seed);
                float v01 = ShadeHash(seed + 1.0);
                float v10 = ShadeHash(seed + 57.0);
                float v11 = ShadeHash(seed + 58.0);
                float u = (3.0 * fx * fx) - (2.0 * fx * fx * fx);
                float v = (3.0 * fy * fy) - (2.0 * fy * fy * fy);
                return ((v00 + ((v01 - v00) * u) + ((v10 - v00) * v) + ((v00 - v01 + v11 - v10) * u * v)) * 2.0) - 1.0;
            }

            // ShadeProgram.WindRipples: GetWindRippleSurfaceField3's three octaves, scrolling steadily.
            float ShadeWindRipples(float2 place, float4 wind, float clock, bool overGround, out float2 slope)
            {
                float ground = overGround ? 1.0 : 0.0;
                float directionness = wind.w + ((0.0 - wind.w) * ground);
                float frequency = 0.16 * (1.0 + ((2.0 - 1.0) * ground));
                float amplitude = wind.y * (0.2 + ((1.0 - 0.2) * directionness)) * (1.0 + ((0.15 - 1.0) * ground));
                float time = 1.0 + (clock * 2.5);
                float stepping = 1.5 + (((1.9 * 1.6) - 1.5) * directionness);
                float turn = 2.0 + ((0.2 - 2.0) * directionness);
                float turnCos = cos(turn);
                float turnSin = sin(turn);
                float windX = cos(wind.x);
                float windY = sin(wind.x);
                float p00 = 0.12 * windX;
                float p01 = 0.12 * windY;
                float p10 = -0.12 * windY;
                float p11 = 0.12 * windX;
                float scroll = (2.5 + ((1.25 - 2.5) * directionness)) * wind.z * (1.0 + ((0.5 - 1.0) * ground));
                float stretch = 0.7 + ((0.225 - 0.7) * directionness);
                float falloff = 0.9 + ((0.22 - 0.9) * directionness);
                float height = 0.0;
                slope = float2(0.0, 0.0);
                [unroll] for (int octave = 0; octave < 3; octave++)
                {
                    float m00 = p00 * stretch * frequency;
                    float m01 = p01 * frequency;
                    float m10 = p10 * stretch * frequency;
                    float m11 = p11 * frequency;
                    float shift = scroll * frequency;
                    float qx = (place.x * m00) + (place.y * m10) - (time * 0.5 * shift);
                    float qy = (place.x * m01) + (place.y * m11) - (time * 1.5 * shift);
                    float gx;
                    float gy;
                    float value = ShadeRipple(qx, qy, gx, gy);
                    height += value * amplitude;
                    slope += float2((gx * m00) + (gy * m01), (gx * m10) + (gy * m11)) * amplitude;
                    float n00 = (p00 * turnCos) - (p01 * turnSin);
                    float n01 = (p00 * turnSin) + (p01 * turnCos);
                    float n10 = (p10 * turnCos) - (p11 * turnSin);
                    float n11 = (p10 * turnSin) + (p11 * turnCos);
                    p00 = n00;
                    p01 = n01;
                    p10 = n10;
                    p11 = n11;
                    frequency *= stepping;
                    scroll /= sqrt(stepping);
                    amplitude *= falloff;
                }

                slope = slope / 0.4;
                return height / 0.4;
            }

            // ShadeProgram.TideAt: GetTidalWaterPlanarState's state at a place - the shore's fields, the tide's phase, the cross-section there.
            void ShadeTideAt(float2 place, float4 swell, float clock, float4 shore, out float shoreDist, out float tide, out float4 profile, out float2 phaseSlope, out float underGround)
            {
                float oceanDist;
                float unused;
                float alongX;
                float alongY;
                ShadeShoreAt(place, shore, shoreDist, oceanDist);
                ShadeShoreAt(place + float2({{Number(ShoreField.Eps)}}, 0.0), shore, unused, alongX);
                ShadeShoreAt(place + float2(0.0, {{Number(ShoreField.Eps)}}), shore, unused, alongY);
                float2 oceanSlope = -float2(alongX - oceanDist, alongY - oceanDist) / {{Number(ShoreField.Eps)}};
                float pi = 3.1415;
                float periods = 1.0 / swell.y;
                underGround = 1.0 - saturate(1.0 + ((shoreDist + 0.05) * 10.0));
                tide = 1.0 - ShadeFractional((((1.0 - oceanDist) * periods) + (clock * 1.1 / (swell.y * 25.0))) / (2.0 * pi));
                float noise = (ShadeSmoothNoise(clock * 0.1, clock * 0.15) * 0.5) + 0.5;
                float reduction = saturate(noise);
                float slowdown = 1.0 - (shoreDist * 4.0);
                float hurry = 1.0 - (shoreDist * 5.5);
                float offset = (pow(saturate(slowdown), 1.1) * 0.5) - (pow(saturate(hurry), 1.1) * 0.75);
                tide = ShadeFractional((noise * 0.2) + tide + (offset * 0.8) + 10.0);
                float effective = saturate(1.0 - shoreDist);
                effective = pow(effective, 1.0 / ((saturate(1.0 - (reduction * 0.4)) * swell.z * 0.8) + 0.2));
                profile = ShadeSwellAt(tide, effective);
                phaseSlope = -oceanSlope * (periods / (2.0 * pi));
            }

            // ShadeProgram's Tide.Leading: isLeadingEdgeArea.
            bool ShadeLeading(float tide, float4 profile)
            {
                return (tide < 0.45 && profile.w < 1.0) || (tide > 0.55 && profile.w > 0.0);
            }

            // ShadeProgram.Ocean: GetOceanWaterPlanarState - the swell over the room's shore, and the wind's ripples on top, flattened by the foam.
            float4 ShadeWaves(float2 place, float4 wind, float2 timing, float4 shore, float4 swell)
            {
                float clock = timing.x;
                float height = 0.0;
                float2 slope = float2(0.0, 0.0);
                float ripples = 2.0;
                bool groundWater = false;
                if (shore.w > 0.5)
                {
                    float shoreDist;
                    float tide;
                    float4 profile;
                    float2 phaseSlope;
                    float underGround;
                    ShadeTideAt(place, swell, clock, shore, shoreDist, tide, profile, phaseSlope, underGround);
                    float kept = 1.0 - underGround;
                    height = (profile.x - 0.5) * swell.x * kept;
                    slope = profile.y * phaseSlope * swell.x * kept;
                    groundWater = underGround > 0.5;
                    float trail = saturate(profile.z) * saturate(timing.y / 25.0);
                    float damping = trail * trail * trail;
                    if (ShadeLeading(tide, profile))
                    {
                        float held = saturate((0.8 - profile.w) / 0.8 * 2.0);
                        damping *= held * held;
                    }

                    damping = damping + ((0.0 - damping) * underGround);
                    ripples = (1.0 - pow(saturate(1.0 - abs(shoreDist)), 10.0)) * (1.0 - damping) * 2.0;
                }

                float2 rippleSlope;
                float rippleHeight = ShadeWindRipples(place, wind, clock, groundWater, rippleSlope);
                height += rippleHeight * ripples;
                slope += rippleSlope * ripples;
                return float4(normalize(float3(-slope.x, -slope.y, -1.0)), height);
            }

            // ShadeProgram.Hash2: the engine's, two fractions of a sine.
            float2 ShadeHash2(float2 p)
            {
                float r = 523.0 * sin(dot(p, float2(53.3158, 43.6143)));
                return float2(ShadeFractional(15.32354 * r), ShadeFractional(17.25865 * r));
            }

            // ShadeProgram.Voronoi: GetVoronoiNoise.
            float ShadeVoronoi(float2 p)
            {
                float nearest = 100.0;
                float next = 100.0;
                float2 cell = floor(p);
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    [unroll] for (int y = -1; y <= 1; y++)
                    {
                        float2 grid = cell + float2((float)x, (float)y);
                        float2 offset = p - grid - float2(1.0, 1.0) + ShadeHash2(grid);
                        float distance = dot(offset, offset);
                        if (nearest > distance)
                        {
                            next = nearest;
                            nearest = distance;
                        }
                        else if (next > distance)
                        {
                            next = distance;
                        }
                    }
                }

                return abs(sqrt(nearest) - sqrt(next));
            }

            // ShadeProgram.Foam: GetTidalWaterPlanarState's foam, GetFoamColor3's trail - its leading edge's textured layers left out.
            float4 ShadeFoam(float2 place, float2 timing, float4 shore, float4 swell)
            {
                if (shore.w <= 0.5)
                {
                    return float4(0.0, 0.0, 0.0, 0.0);
                }

                float shoreDist;
                float tide;
                float4 profile;
                float2 phaseSlope;
                float underGround;
                ShadeTideAt(place, swell, timing.x, shore, shoreDist, tide, profile, phaseSlope, underGround);
                float trail = saturate(profile.z) * saturate(timing.y / 25.0);
                float front = ShadeLeading(tide, profile) ? profile.w : 0.0;
                float shallowFoam = -(shoreDist - 0.18) * 10.0;
                float shallow = saturate(max(saturate(1.0 - ((shoreDist - 0.1) * 5.0)), shallowFoam));
                front = saturate(shallowFoam < 1.0 ? front / (0.1 + (1.0 - saturate(shallowFoam))) : 1.0);

                float intensity = max(0.0, saturate(trail - 0.03));
                if (intensity <= 0.0)
                {
                    return float4(1.0, 1.0, 1.0, 0.0);
                }

                float2 offsetPos = place * 0.05;
                float shallowFoamPow = pow(saturate(shallowFoam), 5.0);
                float sumNoise = 0.0;
                float sumMult = 0.0;
                float amp = 1.0;
                [unroll] for (int octave = 0; octave < 3; octave++)
                {
                    sumMult += amp;
                    sumNoise += ShadeVoronoi(offsetPos * 0.25) * amp;
                    offsetPos *= 2.0;
                    amp *= 0.5 + ((1.3 - 0.5) * trail);
                }

                float threshold = (intensity * 0.4) + 0.05;
                float noise = saturate(sumNoise / sumMult);
                noise = saturate(noise + saturate(front) + shallowFoamPow);
                noise = shallowFoam < 0.99 ? saturate(noise - ((1.0 - saturate(front)) * saturate(shallowFoamPow))) : 1.0;
                float fade = 1.0 - pow(1.0 - intensity, 20.0);
                float covered = fade * pow(saturate(1.0 - (noise / threshold)), 0.2 + ((1.0 - 0.2) * (1.0 - intensity)));
                float4 large = float4(1.0, 1.0, 1.0, 0.6 * covered);
                float grey = 0.1 + ((0.4 - 0.1) * intensity);
                float fine = pow(saturate(intensity * (1.0 - pow(saturate(front), 3.0))), 1.3) * 0.4 * (1.0 - shallow);
                float4 small = float4(grey, grey, grey, fine);
                return small + ((large - small) * saturate(large.w + (1.0 - small.w)));
            }
            """;
    }

    /// <summary>The registers the mesh and the drawing set before the program's own - ShadeProgram's Fixed.</summary>
    private const int FixedRegisters = ShadeProgram.Viewer + 1;

    /// <summary>One step as one statement - ShadeProgram.Run's case for its op.</summary>
    private static void Step(StringBuilder hlsl, ShadeProgram program, ShadeProgram.Step step, ref int parallax)
    {
        string to = R(step.To), a = R(step.A), b = R(step.B), c = R(step.C), d = R(step.D), e = R(step.E);
        string? said = step.Op switch
        {
            ShadeProgram.Op.Clear => "float4(0.0, 0.0, 0.0, 0.0)",
            ShadeProgram.Op.Add => step.C >= 0 ? $"{a} + {b} + {c}" : $"{a} + {b}",
            ShadeProgram.Op.Subtract => $"{a} - {b}",
            ShadeProgram.Op.Multiply => $"{a} * {b}",
            ShadeProgram.Op.Divide => $"ShadeDivided({a}, {b})",
            ShadeProgram.Op.Min => $"min({a}, {b})",
            ShadeProgram.Op.Max => $"max({a}, {b})",
            ShadeProgram.Op.Fmod => $"fmod({a}, {b})",
            ShadeProgram.Op.MultiplyAdd => $"({a} * {b}) + {c}",
            ShadeProgram.Op.Clamp => $"min(max({a}, {b}), {c})",
            ShadeProgram.Op.Power => $"pow(max(abs({a}.x), 1e-7), {b}.x).xxxx",
            ShadeProgram.Op.Step => $"({b}.x >= {a}.x ? 1.0 : 0.0).xxxx",
            ShadeProgram.Op.Negate => $"-{a}",
            ShadeProgram.Op.OneMinus => $"1.0 - {a}",
            ShadeProgram.Op.Saturate => $"saturate({a})",
            ShadeProgram.Op.Abs => $"abs({a})",
            ShadeProgram.Op.Floor => $"floor({a})",
            ShadeProgram.Op.Ceil => $"ceil({a})",

            // TO EVEN AT A HALF, which is what round compiles to.
            ShadeProgram.Op.Round => $"round({a})",
            ShadeProgram.Op.Truncate => $"trunc({a})",
            ShadeProgram.Op.Frac => $"{a} - floor({a})",
            ShadeProgram.Op.Sqrt => $"sqrt({a})",
            ShadeProgram.Op.Sign => $"ShadeSigned({a})",
            ShadeProgram.Op.Normalize => Invariant($"ShadeNormalized({a}, {step.Extra})"),
            ShadeProgram.Op.Dot => Invariant($"ShadeDotted({a}, {b}, {step.Extra}).xxxx"),
            ShadeProgram.Op.Length => Invariant($"sqrt(ShadeDotted({a}, {a}, {step.Extra})).xxxx"),
            ShadeProgram.Op.Lerp => $"{a} + (({b} - {a}) * {c}.x)",
            ShadeProgram.Op.Fit => $"ShadeFitted({a}.x, {b}.x, {c}.x, {d}.x, {e}.x).xxxx",
            ShadeProgram.Op.SmoothStep => $"ShadeSmoothed({a}.x, {b}.x, {c}.x).xxxx",
            ShadeProgram.Op.Hermite => $"ShadeHermite({a}.x, {b}.x, {c}.x).xxxx",

            // NOT A NUMBER IS NEITHER GREATER NOR LESSER, so it takes the equal branch.
            ShadeProgram.Op.If => $"({a}.x > {b}.x ? {c}.x : {a}.x < {b}.x ? {e}.x : {d}.x).xxxx",
            ShadeProgram.Op.Select => $"({c}.x != 0.0 ? {b} : {a})",
            ShadeProgram.Op.Compare => Invariant($"ShadeCompared({a}.x, {(step.B >= 0 ? b + ".x" : "0.0")}, {step.Extra}).xxxx"),
            ShadeProgram.Op.Pick => $"({b}.x == 0.0 ? {a}.x : {b}.x == 1.0 ? {a}.y : {b}.x == 2.0 ? {a}.z : {b}.x == 3.0 ? {a}.w : 0.0).xxxx",
            ShadeProgram.Op.HueTurn => $"ShadeTurn({a}.x, {b}.x, {c}.x)",
            ShadeProgram.Op.RemapHue => $"ShadeRemapped({a}, {b})",
            ShadeProgram.Op.Noise => step.Extra == 0 ? $"ShadeValueNoise({a}.xyz).xxxx" : $"ShadePerlinNoise({a}.xyz).xxxx",
            ShadeProgram.Op.Vibrance => $"ShadeVibrant({a}.x, {b})",
            ShadeProgram.Op.Rotate => $"ShadeRotated({a}.x, {b}, {(step.C >= 0 ? c : "float4(0.0, 0.0, 0.0, 0.0)")})",
            ShadeProgram.Op.Polar => $"ShadePolar({a})",
            ShadeProgram.Op.Triplanar => $"ShadeTriplanar({a}, {b}, {c}, {d}, {(step.Extra != 0 ? "true" : "false")})",
            ShadeProgram.Op.HardLight => $"ShadeHardLit({a}, {b})",
            ShadeProgram.Op.Sine => $"sin({a})",
            ShadeProgram.Op.Sample => Read(program, step.Extra, $"ShadeSheet{Invariant(step.Extra)}.Sample(wrap, {a}.xy)"),

            // A LEVEL THAT IS NOT A NUMBER READS THE TOP ONE, and max gives the number where one is not.
            ShadeProgram.Op.SampleLod => Read(program, step.Extra, $"ShadeSheet{Invariant(step.Extra)}.SampleLevel(wrap, {a}.xy, max({b}.x, 0.0))"),
            ShadeProgram.Op.Waves => $"ShadeWaves({a}.xy, {b}, {c}.xy, {d}, {e})",
            ShadeProgram.Op.Foam => $"ShadeFoam({a}.xy, {b}.xy, {c}, {d})",
            ShadeProgram.Op.River => step.Extra switch
            {
                ShadeProgram.RiverFoamOut => $"ShadeRiverFoamAt({a}.xy, {b}.x, {b}.y, {c}.x, {c}.w)",
                ShadeProgram.RiverBubbleOut => $"ShadeRiverBubbles({a}.xy, {b}.y, {c}.x, {c}.w).xxxx",
                _ => $"ShadeRiver({a}.xy, {b}.x, ShadeRiverFlowAt({a}.xy, {c}.x))",
            },
            _ => null,
        };

        if (said is not null)
        {
            hlsl.Append("    ").Append(to).Append(" = ").Append(said).AppendLine(";");
        }
        else
        {
            switch (step.Op)
            {
                case ShadeProgram.Op.Swizzle:
                {
                    // FROM A COPY, so a register swizzled into itself reads what it held before.
                    hlsl.Append("    { float4 from = ").Append(a).AppendLine(";");
                    int packed = step.Extra;
                    int mask = packed >> 8;
                    for (var part = 0; part < 4; part++)
                    {
                        if ((mask & (1 << part)) != 0)
                        {
                            hlsl.Append("      ").Append(to).Append('.').Append(Part(part))
                                .Append(" = from.").Append(Part((packed >> (part * 2)) & 3)).AppendLine(";");
                        }
                    }

                    hlsl.AppendLine("    }");
                    break;
                }

                case ShadeProgram.Op.Depth:
                    // length(bottom - camera) - length(world_pos - camera): on a parallel view, the
                    // difference of the two depths along it - and in perspective along the point's own ray (ShadeProgram.Viewer).
                    hlsl.Append("    { float distance = r6.x - ShadeDeep(r7, ").Append(a).AppendLine(");");
                    hlsl.Append("      if (r17.w > 0.5) { distance *= length(ShadeRaying(r7, r17, r6.x, ").Append(a).AppendLine(")); }");
                    if (step.Extra != 0)
                    {
                        hlsl.AppendLine("      if (distance < 0.0) { r10 = float4(1.0, 1.0, 1.0, 1.0); }");
                    }

                    hlsl.Append("      ").Append(to).AppendLine(" = distance.xxxx; }");
                    break;

                case ShadeProgram.Op.ContactFade:
                    hlsl.Append("    { float4 albedo = ").Append(a).AppendLine(";");
                    hlsl.Append("      float surface = ShadeRaying(r7, r17, r6.x, ").Append(b).Append(").z * (r6.x - ShadeDeep(r7, ").Append(b).AppendLine("));");
                    hlsl.AppendLine("      if (surface < 0.0) { r10 = float4(1.0, 1.0, 1.0, 1.0); }");
                    hlsl.Append("      float fade = ").Append(d).Append(".x * 10.0 * (1.0 - pow(saturate(1.0 - abs(").Append(c).AppendLine(".z)), 0.2));");
                    hlsl.AppendLine("      float ratio = fade > 0.0 ? saturate(surface / fade) : surface > 0.0 ? 1.0 : 0.0;");
                    hlsl.AppendLine("      albedo.w *= ratio;");
                    hlsl.Append("      ").Append(to).AppendLine(" = albedo; }");
                    break;

                case ShadeProgram.Op.Parallax:
                    Parallax(hlsl, program, step, parallax++);
                    break;

                case ShadeProgram.Op.ViewWay:
                    hlsl.Append("    ").Append(to).Append(" = ShadeViewed(r7, r17, ").Append(a).AppendLine(");");
                    break;

                case ShadeProgram.Op.Wet:
                    hlsl.Append("    ").Append(to).AppendLine(" = float4(0.0, 0.0, 0.0, ShadeWetness.Load(int3(int2(ShadePixel), 0)));");
                    break;

                // A STEP LEFT OUT WOULD BE A COLOUR GONE WRONG WITHOUT A WORD - an op added to the
                // program and not here fails the first time it is written instead.
                default:
                    throw new InvalidOperationException($"no HLSL for the {step.Op} step");
            }
        }

        if (step.Splat)
        {
            hlsl.Append("    ").Append(to).Append(" = ").Append(to).AppendLine(".xxxx;");
        }
    }

    /// <summary>One output of a spray step - see <see cref="SprayLibrary"/>: its sum once, in a variable, and the output read from it.</summary>
    private static void Spray(
        StringBuilder hlsl, ShadeProgram program, ShadeProgram.Step step, int[] versions, Dictionary<(int, int, int, int, int, int, int, int, int), string> sprayed)
    {
        int sheets = step.Extra & 0xFFFFFF;
        var key = (step.A, step.B, step.C, step.D, sheets, versions[step.A], versions[step.B], versions[step.C], versions[step.D]);
        if (!sprayed.TryGetValue(key, out string? name))
        {
            name = "spray" + Invariant(sprayed.Count);
            sprayed[key] = name;
            hlsl.Append("    ShadeSprayOut ").Append(name).Append(" = ShadeSpray(")
                .Append(R(step.A)).Append(".xyz, ").Append(R(step.B)).Append(".xyz, ").Append(R(step.C)).Append(", ").Append(R(step.D)).Append(", wrap");
            for (var part = 0; part < 3; part++)
            {
                int sheet = (sheets >> (part * 8)) & 0xFF;
                hlsl.Append(", ShadeSheet").Append(Invariant(sheet)).Append(", ").Append(Truth(program.Textures[sheet].Srgb));
            }

            hlsl.AppendLine(");");
        }

        string field = (step.Extra >> 24) switch
        {
            ShadeProgram.SprayColour => "colour",
            ShadeProgram.SprayNormal => "normalAo",
            _ => "wetGloss",
        };
        hlsl.Append("    ").Append(R(step.To)).Append(" = ").Append(name).Append('.').Append(field).AppendLine(";");
    }

    /// <summary>
    /// A beach step: its two outputs one sum, written once into a variable of its own and read by each - as <see cref="Spray"/> does.
    /// </summary>
    private static void Beach(
        StringBuilder hlsl, ShadeProgram program, ShadeProgram.Step step, int[] versions, Dictionary<(int, int, int, int, int, int, int, int, int), string> cached)
    {
        int sheets = step.Extra & 0xFFFFFF;
        var key = (step.A, step.B, step.C, step.D, sheets | (1 << 24), versions[step.A], versions[step.B], versions[step.C], versions[step.D]);
        if (!cached.TryGetValue(key, out string? name))
        {
            name = "beach" + Invariant(cached.Count);
            cached[key] = name;
            hlsl.Append("    ShadeBeachOut ").Append(name).Append(" = ShadeBeachAt(")
                .Append(R(step.A)).Append(".xy, ").Append(R(step.B)).Append(", ").Append(R(step.C)).Append(", ").Append(R(step.D)).Append(".xyz, wrap");
            for (var part = 0; part < 3; part++)
            {
                int sheet = (sheets >> (part * 8)) & 0xFF;
                hlsl.Append(", ShadeSheet").Append(Invariant(sheet)).Append(", ").Append(Truth(program.Textures[sheet].Srgb));
            }

            hlsl.AppendLine(");");
        }

        string field = (step.Extra >> 24) switch
        {
            ShadeProgram.BeachColour => "colour",
            ShadeProgram.BeachNormal => "normalGloss",
            _ => "wetness.xxxx",
        };
        hlsl.Append("    ").Append(R(step.To)).Append(" = ").Append(name).Append('.').Append(field).AppendLine(";");
    }

    /// <summary><see cref="BeachLibrary"/>: beach_waves and BreakingWavesFragment - see ShadeProgram.Beaching and BeachLift.</summary>
    private static string Beaches() => $$"""
        Texture2D<float4> ShadeBeachProfile : register(t{{BeachProfileSlot}});
        Texture2D<float4> ShadeBeachNoise : register(t{{BeachNoiseSlot}});

        struct ShadeBeachOut
        {
            float4 colour;
            float4 normalGloss;
            float wetness;
        };

        struct ShadeWavePhase
        {
            float3 waveCoord;
            float3 breakPhase;
            float3 shorePhase;
            float2 dir;
            float phaseSpeed;
            int index;
            float intensityMult;
            float interpRatio;
        };

        struct ShadeFoamSurface
        {
            float3 normal;
            float4 colour;
            float subsurface;
            float ao;
            float rough;
            float sqrWeight;
        };

        // ShadeProgram.ProfileAt: the wave shape's red and green, held at its edges, the four nearest texels' centres mixed.
        float2 ShadeProfileAt(float u, float v)
        {
            uint across;
            uint down;
            ShadeBeachProfile.GetDimensions(across, down);
            float sx = (u * (float)across) - 0.5;
            float sy = (v * (float)down) - 0.5;
            if (!(abs(sx) <= 3.40282347e38) || !(abs(sy) <= 3.40282347e38))
            {
                return float2(0.0, 0.0);
            }

            sx = clamp(sx, 0.0, (float)across - 1.0);
            sy = clamp(sy, 0.0, (float)down - 1.0);
            int x0 = (int)sx;
            int y0 = (int)sy;
            int x1 = min(x0 + 1, (int)across - 1);
            int y1 = min(y0 + 1, (int)down - 1);
            float fx = sx - x0;
            float fy = sy - y0;
            float2 a = ShadeBeachProfile.Load(int3(x0, y0, 0)).xy;
            float2 b = ShadeBeachProfile.Load(int3(x1, y0, 0)).xy;
            float2 c = ShadeBeachProfile.Load(int3(x0, y1, 0)).xy;
            float2 d = ShadeBeachProfile.Load(int3(x1, y1, 0)).xy;
            float2 top = a + ((b - a) * fx);
            float2 bottom = c + ((d - c) * fx);
            return top + ((bottom - top) * fy);
        }

        // ShadeProgram.NoiseAt: the noise's red and green, wrapping, the four nearest texels' centres mixed.
        float2 ShadeNoiseAt(float2 uv)
        {
            uint across;
            uint down;
            ShadeBeachNoise.GetDimensions(across, down);
            int width = (int)across;
            int height = (int)down;
            float sx = (uv.x * (float)width) - 0.5;
            float sy = (uv.y * (float)height) - 0.5;
            float fx0 = floor(sx);
            float fy0 = floor(sy);
            if (!(abs(fx0) <= 3.40282347e38) || !(abs(fy0) <= 3.40282347e38))
            {
                return float2(0.5, 0.5);
            }

            float fx = sx - fx0;
            float fy = sy - fy0;
            int x0 = ((((int)fx0) % width) + width) % width;
            int y0 = ((((int)fy0) % height) + height) % height;
            int x1 = (x0 + 1) % width;
            int y1 = (y0 + 1) % height;
            float2 a = ShadeBeachNoise.Load(int3(x0, y0, 0)).xy;
            float2 b = ShadeBeachNoise.Load(int3(x1, y0, 0)).xy;
            float2 c = ShadeBeachNoise.Load(int3(x0, y1, 0)).xy;
            float2 d = ShadeBeachNoise.Load(int3(x1, y1, 0)).xy;
            float2 top = a + ((b - a) * fx);
            float2 bottom = c + ((d - c) * fx);
            return top + ((bottom - top) * fy);
        }

        // ShadeProgram.Unit: normalize, nought where there is no direction.
        float2 ShadeUnit(float2 v)
        {
            float len = sqrt(dot(v, v));
            return len > 1e-12 ? v / len : float2(0.0, 0.0);
        }

        // math_utils' RampPow and PolyStep.
        float ShadeRampPow(float value, float power)
        {
            return power > 0.0 ? pow(saturate(value), 1.0 + power) : 1.0 - pow(saturate(1.0 - value), 1.0 - power);
        }

        float ShadePolyStep(float value, float center, float steepness)
        {
            center = clamp(center, 1e-2, 1.0 - 1e-2);
            float power = clamp(1.0 / (1.0 - steepness), -100.0, 100.0);
            float k = clamp(0.5 * power / min(center, 1.0 - center), 0.0, 100.0);
            float a = min(2.0 * k * center, 100.0);
            float b = min(2.0 * k * (1.0 - center), 100.0);
            return 0.5 - (0.5 * pow(saturate(1.0 - ((value - center) / (1.0 - center))), b)) + (0.5 * pow(saturate(value / center), a));
        }

        // GetInterpIndices.
        int2 ShadeInterpIndices(float phase)
        {
            float x = 0.5 + (phase * 0.5);
            float y = phase * 0.5;
            return int2((int)floor((floor(x) * 2.0) + 0.5), (int)floor((floor(y) * 2.0) + 1.0 + 0.5));
        }

        // GetStationaryNoiseField.
        float3 ShadeStationaryNoise(float2 uv)
        {
            float x = ShadeNoiseAt(uv + float2(1e-2, 0.0)).y;
            float y = ShadeNoiseAt(uv + float2(0.0, 1e-2)).y;
            float centre = ShadeNoiseAt(uv).y;
            return float3(centre, (x - centre) / 1e-2, (y - centre) / 1e-2);
        }

        // GetFrontAdvectedNoiseField - ShadeProgram.FrontAdvectedNoise.
        float3 ShadeFrontAdvectedNoise(float3 row0, float3 row1, float2 velocity, float3 hash, float clock)
        {
            float2 pos = float2(row0.x, row1.x);
            float2 uv = (pos * 6e-4) + hash.xy;
            float2 drift = velocity * 6e-4 / 0.5;
            float2 phases;
            float2 seeds;
            float2 weights;
            ShadeInterpNodes((clock * 0.5) + hash.z, phases, seeds, weights);
            float3 field = (ShadeStationaryNoise(uv - (drift * ((phases.x - 0.5) * 2.0))) * weights.x)
                + (ShadeStationaryNoise(uv - (drift * ((phases.y - 0.5) * 2.0))) * weights.y);
            float dy = field.y * 6e-4;
            float dz = field.z * 6e-4;
            return float3(field.x, (dy * row0.y) + (dz * row1.y), (dy * row0.z) + (dz * row1.z));
        }

        // BuildWavePhase - ShadeProgram.BuildWavePhase.
        ShadeWavePhase ShadeBuildWavePhase(float2 place, float2 dir, int index, float3 shoreCoord, float3 frontCoord, float clock)
        {
            float3 waveHash = ShadeHashStable3(float3((float)index, 3.0, 0.0));
            float shoreNoise = ShadeNoiseAt((place * 2e-3) + waveHash.xy).x;
            float3 row0 = float3(place.x, 1.0, 0.0) - (frontCoord * (dir.x * {{Number(ShadeProgram.BeachWaveLength)}}));
            float3 row1 = float3(place.y, 0.0, 1.0) - (frontCoord * (dir.y * {{Number(ShadeProgram.BeachWaveLength)}}));
            ShadeWavePhase phase;
            phase.waveCoord = frontCoord;
            phase.shorePhase = -phase.waveCoord - shoreCoord;
            float phaseSpeed = 1.0;
            if (phase.shorePhase.x > 0.0)
            {
                float shorePhase = phase.shorePhase.x;
                float a = -1.5 * (0.5 + shoreNoise);
                phase.waveCoord.x -= a * shorePhase * shorePhase / 2.0;
                phaseSpeed += a * shorePhase;
            }

            float3 hash = ShadeHashStable3(float3((float)index, 0.0, 0.0));
            float3 offsetNoise = ShadeFrontAdvectedNoise(row0, row1, dir * {{Number(ShadeProgram.BeachWaveSpeed)}}, hash, clock);
            phase.interpRatio = (frontCoord.x + 1.0) / 2.0;
            phase.waveCoord += float3(0.5, 0.0, 0.0) - ((1.0 - 0.2) * offsetNoise);
            phase.intensityMult = ShadeMathSmoothStep(waveHash.x, 0.9, 0.95) * 0.3;
            float breakLength = (phase.intensityMult * 0.5) + 2.5;
            float breakOffset = (phase.intensityMult - 0.3) * 5.0;
            phase.breakPhase = float3(1.0, 0.0, 0.0) - ((shoreCoord + float3(-breakOffset, 0.0, 0.0)) / breakLength);
            float3 breakNoise = ShadeStationaryNoise((place * 5e-4) + hash.xy);
            breakNoise = float3(breakNoise.x, breakNoise.y * 5e-4, breakNoise.z * 5e-4);
            phase.breakPhase += (breakNoise - float3(0.5, 0.0, 0.0)) * 5.0 * saturate(shoreCoord.x);
            phase.dir = dir;
            phase.phaseSpeed = phaseSpeed;
            phase.index = index;
            return phase;
        }

        // GetBeachWaveFront's shore, coordinates and direction - ShadeProgram.FrontAt.
        void ShadeBeachFront(float2 place, float clock, float4 box, out float3 globalFront, out float3 shoreCoord, out float2 dir, out float inland)
        {
            float eps = box.z;
            float centre;
            float xp;
            float xn;
            float yp;
            float yn;
            float unused;
            ShadeShoreAt(place, box, centre, unused);
            ShadeShoreAt(place + float2(eps, 0.0), box, xp, unused);
            ShadeShoreAt(place - float2(eps, 0.0), box, xn, unused);
            ShadeShoreAt(place + float2(0.0, eps), box, yp, unused);
            ShadeShoreAt(place - float2(0.0, eps), box, yn, unused);
            float inv = 0.5 / eps;
            float3 ocean = float3(centre * {{Number(ShoreField.Reach)}}, (xp - xn) * inv * {{Number(ShoreField.Reach)}}, (yp - yn) * inv * {{Number(ShoreField.Reach)}});
            globalFront = (-ocean / {{Number(ShadeProgram.BeachWaveLength)}}) - float3(clock * ({{Number(ShadeProgram.BeachWaveSpeed)}} / {{Number(ShadeProgram.BeachWaveLength)}}), 0.0, 0.0);
            shoreCoord = ((ocean - float3({{Number(ShadeProgram.BeachTravelShift)}}, 0.0, 0.0)) / {{Number(ShadeProgram.BeachWaveLength)}}) + float3(1.0 - 0.5, 0.0, 0.0);
            dir = -ShadeUnit(float2(ocean.y, ocean.z));
            inland = min(0.0, shoreCoord.x);
        }

        // GetSingleWaveNormalField with its fades - ShadeProgram.SingleWaveNormal.
        float3 ShadeSingleWaveNormal(ShadeWavePhase phase)
        {
            float shoreFade = saturate(-phase.shorePhase.x / 0.4);
            float intensityFade = 0.7 + (phase.intensityMult * 0.3);
            float coord = phase.waveCoord.x;
            float breaking = phase.breakPhase.x;
            float height = ShadeProfileAt(coord, breaking).y;
            float rise = (ShadeProfileAt(coord + (1.0 / 256.0), breaking).y - height) / (1.0 / 256.0);
            float fade = saturate(coord / 0.01) * saturate(1.0 - ((coord - (1.0 - 0.01)) / 0.01));
            height *= fade;
            rise *= fade;
            float scale = {{Number(ShadeProgram.BeachWaveLength)}} * 0.5 * shoreFade * intensityFade;
            return float3(height, phase.waveCoord.y * rise, phase.waveCoord.z * rise) * scale;
        }

        // GetBeachWaveNormalField over the geometry waves - and BeachLift, the vertex's move along z.
        float3 ShadeBeachNormalField(float2 place, float clock, float3 globalFront, float3 shoreCoord, float2 dir, float inland)
        {
            int2 geom = ShadeInterpIndices(globalFront.x - 0.5 + inland);
            ShadeWavePhase g0 = ShadeBuildWavePhase(place, dir, geom.x, shoreCoord, globalFront - float3((float)geom.x, 0.0, 0.0), clock);
            ShadeWavePhase g1 = ShadeBuildWavePhase(place, dir, geom.y, shoreCoord, globalFront - float3((float)geom.y, 0.0, 0.0), clock);
            return ShadeSingleWaveNormal(g0) + ShadeSingleWaveNormal(g1);
        }

        float ShadeBeachLift(float2 place, float clock, float heightOffset, float4 box)
        {
            if (!(box.w > 0.5))
            {
                return heightOffset;
            }

            float3 globalFront;
            float3 shoreCoord;
            float2 dir;
            float inland;
            ShadeBeachFront(place, clock, box, globalFront, shoreCoord, dir, inland);
            return -ShadeBeachNormalField(place, clock, globalFront, shoreCoord, dir, inland).x + heightOffset;
        }

        // GetFoamScrollingLayer with the turbulence's offset nought - ShadeProgram.ScrollingLayer.
        ShadeFoamSurface ShadeScrollingLayer(float2 uv, float2 velocity, float phase, float layerSeed, SamplerState wrap, Texture2D colourSheet, bool colourSrgb, Texture2D normalSheet, bool normalSrgb)
        {
            float2 phases;
            float2 seeds;
            float2 weights;
            ShadeInterpNodes(phase, phases, seeds, weights);
            ShadeFoamSurface surface;
            surface.normal = float3(0.0, 0.0, 0.0);
            surface.colour = float4(0.0, 0.0, 0.0, 0.0);
            surface.subsurface = 0.0;
            surface.ao = 0.0;
            surface.rough = 0.0;
            surface.sqrWeight = 0.0;
            [unroll] for (int layer = 0; layer < 2; layer++)
            {
                float3 hash = ShadeHash33(float3(seeds[layer], layerSeed, 0.0));
                float angle = 3.1415 * 2.0 * hash.z;
                float s = sin(angle);
                float c = cos(angle);
                float2 flow = uv - (velocity * ((phases[layer] * 2.0) - 1.0)) + hash.xy;
                float2 turned = float2((flow.x * c) - (flow.y * s), (flow.x * s) + (flow.y * c));
                float4 colour = ShadeSprayRead(colourSheet, colourSrgb, wrap, turned);
                float4 bumps = ShadeSprayRead(normalSheet, normalSrgb, wrap, turned);
                float2 back = ShadeRgToGradYFix(bumps.xy);
                float2 world = float2((back.x * c) + (back.y * s), (back.y * c) - (back.x * s));
                float weight = weights[layer];
                surface.colour += colour * weight;
                surface.normal += float3(colour.w, world) * weight;
                surface.ao += bumps.z * weight;
                surface.rough += bumps.w * weight;
                surface.subsurface += ((colour.w * 0.5) + 0.5) * weight;
                surface.sqrWeight += weight * weight;
            }

            return surface;
        }

        // One space layer of a wave's foam - ShadeProgram.FoamLayer.
        ShadeFoamSurface ShadeFoamLayer(float2 place, float seed, int index, float2 flowVelocity, float clock, float4 average, SamplerState wrap, Texture2D colourSheet, bool colourSrgb, Texture2D normalSheet, bool normalSrgb)
        {
            float scaleMult = pow(saturate(seed), 3.0);
            float timeFreq = lerp(0.5, 3.0, scaleMult);
            float spaceFreq = 1.8e-3 * lerp(0.75, 1.2, scaleMult);
            float layerSeed = ((float)index * 4.0) + seed;
            ShadeFoamSurface layer = ShadeScrollingLayer(place * spaceFreq, flowVelocity * spaceFreq / timeFreq, clock * timeFreq, layerSeed, wrap, colourSheet, colourSrgb, normalSheet, normalSrgb);
            layer.subsurface = 1.0;
            layer.colour = ((layer.colour - average) / sqrt(layer.sqrWeight)) + average;
            return layer;
        }

        // GetBeachFoamIntensity as written - SmoothStepSimple's 3x² - 2x² too.
        float ShadeBeachFoamIntensity(float foamCoord, float sprayCoord, float shorePhase, float breakPhase, float intensityMult)
        {
            float breakFoam = ShadePolyStep(breakPhase, 0.53, 0.95);
            float residualFoam = saturate((shorePhase - -0.4) * 5.0);
            float shoreFade = ShadeRampPow(saturate(1.0 - shorePhase), 4.0);
            float spike = 0.2 * breakFoam * shoreFade;
            float tail = 0.1 * breakFoam * shoreFade * (0.0 + (1.0 * intensityMult));
            float residual = 0.8 * residualFoam;
            float constant = 0.3 * breakFoam * (0.7 + (0.3 * intensityMult));
            float humped = pow(saturate(foamCoord / 0.9), 4.0);
            float intensity = ((3.0 * humped * humped) - (2.0 * humped * humped)) * spike;
            intensity += pow(saturate(foamCoord / 0.9), 3.0) * tail;
            intensity += constant;
            intensity += residual * pow(saturate(sprayCoord), 20.0);
            return lerp(intensity, (intensity * 0.8) + 0.2, saturate(breakPhase));
        }

        // GetSingleWaveFoamSurface - ShadeProgram.SingleWaveFoam, GetBeachFoamProfile and DissolveLayeredSurface within.
        ShadeFoamSurface ShadeSingleWaveFoam(float2 place, ShadeWavePhase phase, float clock, float4 average, SamplerState wrap, Texture2D colourSheet, bool colourSrgb, Texture2D normalSheet, bool normalSrgb)
        {
            float foamCoord = phase.waveCoord.x + 0.5;
            float shorePhase = phase.shorePhase.x;
            float breakPhase = phase.breakPhase.x;

            float sprayVelocity = 1.5 * pow(saturate(foamCoord / 0.95), 20.0);
            float over = saturate((foamCoord - 1.0) * 10.0);
            sprayVelocity *= 0.7 + (0.3 * (1.0 - (over * over)));
            sprayVelocity *= ShadePolyStep(breakPhase, 0.5, 0.9);
            float shoreVelocity = lerp(1.2 * pow(saturate(foamCoord / 0.95), 3.0), 1.0, saturate((shorePhase - 0.2) / 0.8));
            float inherited = lerp(sprayVelocity, shoreVelocity, saturate((shorePhase + 0.25) * 5.0));
            float sprayPhase = saturate((breakPhase - 0.5) * 2.0);
            float spraySharpness = lerp(0.6, 0.9, sprayPhase);
            float sharpness = lerp(0.6, spraySharpness, saturate((foamCoord - 0.9) * 5.0));
            sharpness = lerp(sharpness, 0.4, saturate(shorePhase));
            float sprayWidthScale = (sprayPhase * lerp(0.4, 0.6, phase.intensityMult)) + 0.1;
            float maxOffset = lerp(0.5, 1.0, phase.intensityMult);
            float sprayOffsetScale = lerp(0.0, maxOffset, sprayPhase);
            float slide = saturate((shorePhase - -0.2) * 5.0);
            sprayOffsetScale *= lerp(1.0, 0.75, slide);
            sprayWidthScale = lerp(sprayWidthScale, 0.1, slide);
            sprayWidthScale = lerp(sprayWidthScale, 0.3, ShadePolyStep(saturate(shorePhase), 0.6, 0.8));
            float sprayStart = 0.2 * (1.0 - sprayWidthScale) * sprayOffsetScale;
            float sprayEnd = sprayStart + (sprayWidthScale * 0.2);
            float profileIntensity = ShadeBeachFoamIntensity(foamCoord, foamCoord - sprayEnd, shorePhase, breakPhase, phase.intensityMult);

            float2 phases;
            float2 seeds;
            float2 weights;
            ShadeInterpNodes(foamCoord * 4.0, phases, seeds, weights);
            seeds /= 4.0;
            float2 flowDir = ShadeUnit((phase.dir * 0.7) + (ShadeUnit(phase.waveCoord.yz) * 0.3));
            float2 flowVelocity = ((phase.dir * (phase.phaseSpeed * inherited)) + (flowDir * 0.0)) * {{Number(ShadeProgram.BeachWaveSpeed)}};
            ShadeFoamSurface layer0 = ShadeFoamLayer(place, seeds.x, phase.index, flowVelocity, clock, average, wrap, colourSheet, colourSrgb, normalSheet, normalSrgb);
            ShadeFoamSurface layer1 = ShadeFoamLayer(place, seeds.y, phase.index, flowVelocity, clock, average, wrap, colourSheet, colourSrgb, normalSheet, normalSrgb);
            float share = weights.y;
            ShadeFoamSurface surface;
            surface.colour = lerp(layer0.colour, layer1.colour, share);
            surface.normal = lerp(layer0.normal, layer1.normal, share);
            surface.ao = lerp(layer0.ao, layer1.ao, share);
            surface.rough = lerp(layer0.rough, layer1.rough, share);
            surface.subsurface = lerp(layer0.subsurface, layer1.subsurface, share);
            surface.sqrWeight = (layer0.sqrWeight * (1.0 - share) * (1.0 - share)) + (layer1.sqrWeight * share * share);

            float intensity = saturate(0.0 + profileIntensity) * saturate(5.0 * phase.interpRatio);
            float sprayFade = saturate(1.0 - ((phase.waveCoord.x - (0.5 + sprayStart)) / (sprayEnd - sprayStart)));
            float3 waveHash = ShadeHash33(float3((float)phase.index, 1.0, 0.0));
            float beachNoise = ShadeNoiseAt((place * 3e-3) + waveHash.xy).x;
            float dissipation = lerp(-5.0, 1.0, ShadeMathSmoothStep(beachNoise, 0.5, 0.5));
            float opacity = ShadeRampPow(sprayFade, 0.8) * ShadeRampPow(saturate(1.0 - shorePhase), dissipation);
            float threshold = 1.0 - surface.normal.x;
            float surfaceFade = ShadeMathSmoothStep(intensity * opacity, threshold, sharpness);
            float subsurfaceFade = ShadeMathSmoothStep(opacity, threshold, sharpness);
            surface.colour.w *= surfaceFade;
            surface.normal *= 1.0 - saturate(1.0 - (intensity * opacity));
            surface.rough *= surfaceFade;
            surface.subsurface *= subsurfaceFade;
            return surface;
        }

        // BreakingWavesFragment's ripples - ShadeProgram.BeachRipples.
        float2 ShadeBeachRipples(float2 place, float clock, SamplerState wrap, Texture2D rippleSheet, bool rippleSrgb)
        {
            float phaseNoise = ShadeNoiseAt(place * 1e-3).x;
            float2 phases;
            float2 seeds;
            float2 weights;
            ShadeInterpNodes(clock * 0.15 * (1.0 / 5.0), phases, seeds, weights);
            float3 h0 = ShadeHash33(float3(seeds.x, 0.0, 0.0));
            float3 h1 = ShadeHash33(float3(seeds.y, 0.0, 0.0));
            float2 at = place * 4e-3;
            float2 following = (ShadeNoiseAt(at + h0.xy) * weights.x) + (ShadeNoiseAt(at + h1.xy) * weights.y);
            float2 turbulent = (following - float2(0.5, 0.5)) * 60.0;
            float velocityMult = 50.0 * 0.002 / 0.25;
            float2 velocityCoeffs = float2(0.5 * velocityMult * velocityMult, 0.02 * velocityMult * velocityMult);
            float2 flow = turbulent * 0.002 / 0.25;
            float phase = (clock * 0.25) + phaseNoise;
            float2 uv = place * 0.002;
            float2 grad = float2(0.0, 0.0);
            [unroll] for (int octave = 0; octave < 3; octave++)
            {
                float3 octaveSeed = ShadeHashStable3(float3(0.0, 0.0, (float)octave));
                float octaveScale = pow(2.0, (float)octave);
                float2 nodePhases;
                float2 nodeSeeds;
                float2 nodeWeights;
                ShadeInterpNodes(phase + octaveSeed.z, nodePhases, nodeSeeds, nodeWeights);
                float speed = sqrt((velocityCoeffs.x / octaveScale) + (velocityCoeffs.y * octaveScale));
                float ampMult = 1.0 / (1.0 + (0.5 * octaveScale));
                [unroll] for (int node = 0; node < 2; node++)
                {
                    float3 nodeSeed = ShadeHashStable3(float3(nodeSeeds[node], (float)octave, 0.0));
                    float velocityAngle = nodeSeed.z * 2.0 * 3.1415;
                    float2 rippleVelocity = float2(cos(velocityAngle), sin(velocityAngle)) * speed;
                    float turn = nodeSeed.y * 2.0 * 3.1415;
                    float s = sin(turn);
                    float c = cos(turn);
                    float2 nodeUv = (uv * octaveScale) + nodeSeed.xy - ((flow + rippleVelocity) * octaveScale * ((nodePhases[node] * 2.0) - 1.0));
                    float2 turned = float2((nodeUv.x * c) - (nodeUv.y * s), (nodeUv.x * s) + (nodeUv.y * c));
                    float4 sample = ShadeSprayRead(rippleSheet, rippleSrgb, wrap, turned);
                    float2 back = ShadeRgToGradYFix(sample.xy);
                    grad += float2((back.x * c) + (back.y * s), (back.y * c) - (back.x * s)) * ampMult * nodeWeights[node];
                }
            }

            return grad * 0.5;
        }

        // ShadeProgram.Beaching: BreakingWavesFragment, as BreakingOceanWaves.fxgraph mixes it.
        ShadeBeachOut ShadeBeachAt(float2 place, float4 packed, float4 box, float3 water, SamplerState wrap, Texture2D colourSheet, bool colourSrgb, Texture2D normalSheet, bool normalSrgb, Texture2D rippleSheet, bool rippleSrgb)
        {
            float clock = packed.x;
            float crossed = packed.y;
            float3 globalFront;
            float3 shoreCoord;
            float2 dir;
            float inland;
            ShadeBeachFront(place, clock, box, globalFront, shoreCoord, dir, inland);
            int2 foamNodes = ShadeInterpIndices(globalFront.x + inland);
            ShadeWavePhase f0 = ShadeBuildWavePhase(place, dir, foamNodes.x, shoreCoord, globalFront - float3((float)foamNodes.x, 0.0, 0.0), clock);
            ShadeWavePhase f1 = ShadeBuildWavePhase(place, dir, foamNodes.y, shoreCoord, globalFront - float3((float)foamNodes.y, 0.0, 0.0), clock);
            float4 averaged = colourSheet.SampleLevel(wrap, float2(0.5, 0.5), 15.0);
            float4 average = colourSrgb ? ShadeLinearised(averaged) : averaged;
            ShadeFoamSurface first = ShadeSingleWaveFoam(place, f0, clock, average, wrap, colourSheet, colourSrgb, normalSheet, normalSrgb);
            ShadeFoamSurface second = ShadeSingleWaveFoam(place, f1, clock, average, wrap, colourSheet, colourSrgb, normalSheet, normalSrgb);
            ShadeFoamSurface bottom = first;
            ShadeFoamSurface top = second;
            if (f0.index < f1.index)
            {
                bottom = second;
                top = first;
            }

            // OverlayLayeredSurfaces.
            float a0 = bottom.colour.w;
            float a1 = top.colour.w;
            float alpha = saturate(1.0 - ((1.0 - a0) * (1.0 - a1)));
            float inv = 1.0 / (alpha + 1e-5);
            ShadeFoamSurface surface;
            surface.colour = float4(lerp(bottom.colour.xyz * a0, top.colour.xyz, a1) * inv, alpha);
            surface.normal = bottom.normal + top.normal;
            surface.ao = lerp(bottom.ao * a0, top.ao, a1) * inv;
            surface.rough = lerp(bottom.rough * a0, top.rough, a1) * inv;
            surface.subsurface = 1.0 - ((1.0 - bottom.subsurface) * (1.0 - top.subsurface));
            surface.sqrWeight = 0.0;

            float3 normalField = ShadeBeachNormalField(place, clock, globalFront, shoreCoord, dir, inland);
            float2 ripples = ShadeBeachRipples(place, clock, wrap, rippleSheet, rippleSrgb);
            float2 grad = normalField.yz + surface.normal.yz + ripples;
            float3 normal = normalize(float3(-grad.x, -grad.y, -1.0));

            float foam = 0.7 * surface.colour.w;
            float thickness = saturate(surface.subsurface);
            float through = exp(-crossed * thickness * {{Number(ShadeProgram.BeachAbsorption)}});
            float film = (1.0 - foam) * thickness * (1.0 - through);
            float cover = foam + film;
            float3 albedo = cover > 0.0 ? ((surface.colour.xyz * foam) + (water * film)) / cover : float3(0.0, 0.0, 0.0);

            // THE WETNESS IT CASTS (ShadeProgram.BeachWetted): the foam's subsurface past a tenth, at least the shore's own band.
            float band = 0.8 * saturate((shoreCoord.x + 1.4 + ((ShadeNoiseAt(place * 1e-3).x - 0.5) * 0.5)) * 3.1);
            float wetness = max(ShadeMathSmoothStep(surface.subsurface, 0.1, 0.5), band);

            // NO SHORE, NOTHING DRAWN - chosen after the sum, so every read stays in the program's own flow.
            ShadeBeachOut said;
            bool shored = box.w > 0.5;
            said.colour = shored ? float4(albedo, cover) : float4(0.0, 0.0, 0.0, 0.0);
            said.normalGloss = shored ? float4(normal, saturate(1.0 - surface.rough)) : float4(0.0, 0.0, -1.0, 0.0);
            said.wetness = shored ? wetness : 0.0;
            return said;
        }
        """;

    /// <summary>
    /// ParallaxUvSpace with UvSpaceRaymarchChord, as ShadeProgram.Parallaxed marches it - the height read at the level the starting coordinates step at, the fragment's SAMPLE_TEX2DGRAD.
    /// </summary>
    private static void Parallax(StringBuilder hlsl, ShadeProgram program, ShadeProgram.Step step, int which)
    {
        string uv = R(step.A), way = R(step.C), packed = R(step.D), to = R(step.To);
        string sheet = $"ShadeSheet{Invariant(step.Extra)}";
        string read = Read(program, step.Extra, $"{sheet}.SampleGrad(wrap, along.xy, startX, startY)");
        string n = Invariant(which);
        hlsl.Append("    { float4 start = ").Append(uv).AppendLine(";");
        hlsl.Append("      float4 packed = ").Append(packed).AppendLine(";");
        hlsl.AppendLine("      float2 startX = ddx(start.xy);");
        hlsl.AppendLine("      float2 startY = ddy(start.xy);");
        hlsl.AppendLine("      float3 normal = r3.xyz;");
        hlsl.AppendLine("      float size = length(normal);");
        hlsl.AppendLine("      float3 m0 = r8.xyz;");
        hlsl.AppendLine("      float3 m1 = r9.xyz;");
        hlsl.AppendLine("      float3 m2 = normal / (size + 1e-7) * packed.x;");
        hlsl.AppendLine("      float3 c0 = cross(m1, m2);");
        hlsl.AppendLine("      float3 c1 = cross(m2, m0);");
        hlsl.AppendLine("      float3 c2 = cross(m0, m1);");
        hlsl.AppendLine("      float det = dot(m0, c0);");
        hlsl.AppendLine("      if (!(abs(det) > 0.0))");
        hlsl.AppendLine("      {");
        hlsl.Append("        ").Append(to).AppendLine(" = float4(start.xy, 0.0, 0.0);");
        hlsl.AppendLine("      }");
        hlsl.AppendLine("      else");
        hlsl.AppendLine("      {");
        hlsl.Append("        float3 toward = ").Append(way).AppendLine(".xyz;");
        hlsl.AppendLine("        float3 dir = float3(dot(toward, c0), dot(toward, c1), dot(toward, c2)) / det;");
        hlsl.AppendLine("        float dz = abs(dir.z) > 1e-7 ? dir.z : 1e-7;");
        hlsl.AppendLine("        float offsetScale = packed.y / dz;");
        hlsl.AppendLine("        float3 origin = float3(start.x + (dir.x * offsetScale), start.y + (dir.y * offsetScale), 1.0);");
        hlsl.AppendLine("        int straight = (int)packed.z;");
        hlsl.AppendLine("        int refine = (int)packed.w;");
        hlsl.AppendLine("        float most = -origin.z / dz;");
        hlsl.AppendLine("        float linearStep = 1.0 / (straight + 1);");
        hlsl.AppendLine("        float2 low = float2(0.0, -1.0);");
        hlsl.AppendLine("        float2 high = float2(1.0, 1.0);");
        hlsl.AppendLine("        float chord = 0.0;");
        hlsl.AppendLine("        bool found = false;");
        hlsl.Append("        [loop] for (int at").Append(n).Append(" = 0; at").Append(n).Append(" < straight + refine + 2; at").Append(n).AppendLine("++)");
        hlsl.AppendLine("        {");
        hlsl.Append("          float x = found ? chord : linearStep * at").Append(n).AppendLine(";");
        hlsl.AppendLine("          float3 along = origin + (dir * (most * x));");
        hlsl.Append("          float y = (").Append(read).AppendLine(").x - along.z;");
        hlsl.AppendLine("          if (y > 0.0) { found = true; high = float2(x, y); } else { low = float2(x, y); }");
        hlsl.AppendLine("          chord = low.x + ((high.x - low.x) * saturate(-low.y / max(1e-7, -low.y + high.y)));");
        hlsl.AppendLine("        }");
        hlsl.AppendLine("        float reached = (chord * most) + offsetScale;");
        hlsl.Append("        ").Append(to).AppendLine(" = float4(start.x + (dir.x * reached), start.y + (dir.y * reached), reached, 0.0);");
        hlsl.AppendLine("      }");
        hlsl.AppendLine("    }");
    }

    /// <summary>A sheet's read, linearised by the table where the sheet is sRGB - ShadeProgram.Read.</summary>
    private static string Read(ShadeProgram program, int sheet, string sample)
        => program.Textures[sheet].Srgb
            ? $"ShadeLinearised({sample})"
            : sample;

    private static string R(int register) => register >= 0 ? "r" + register.ToString(CultureInfo.InvariantCulture) : "float4(0.0, 0.0, 0.0, 0.0)";

    private static char Part(int part) => "xyzw"[part];

    private static string Truth(bool value) => value ? "true" : "false";

    private static string Invariant(FormattableString text) => text.ToString(CultureInfo.InvariantCulture);

    private static string Invariant(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static string Vector(Vector4 value) => $"float4({Number(value.X)}, {Number(value.Y)}, {Number(value.Z)}, {Number(value.W)})";

    /// <summary>A float written so the compiler reads back the very same bits - the infinities and not-a-number by their bits.</summary>
    public static string Number(float value)
    {
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            return string.Create(CultureInfo.InvariantCulture, $"asfloat(0x{BitConverter.SingleToUInt32Bits(value):X8}u)");
        }

        string said = value.ToString("G9", CultureInfo.InvariantCulture);
        return said.Contains('.', StringComparison.Ordinal) || said.Contains('E', StringComparison.Ordinal) ? said : said + ".0";
    }
}
