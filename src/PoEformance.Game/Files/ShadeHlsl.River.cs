namespace PoEformance.Game.Files;

/// <summary>The engine's river surface on the card - see ShadeProgram.River.</summary>
public static partial class ShadeHlsl
{
    /// <summary>
    /// ShadeProgram.River and RiverFoam, line for line, for a program with a <c>River</c> step - after <see cref="WavesLibrary"/>, whose
    /// ripple texture it reads, and <see cref="FoamLibrary"/>, whose hash it takes. Its foam reads the including shader's
    /// <c>DistortionSheet</c> (ModelShaders' DistortionSlot), the card binding it for a river; where nothing is bound the foam is nought,
    /// as the processor's is without its texture.
    /// </summary>
    public static readonly string RiverLibrary = Rivers();

    private static string Rivers() => """
        // ShadeProgram.River: GetFlowSurfaceField at no flow - eight octaves fading in and out in place, its normal and height.
        float4 ShadeRiver(float2 place, float clock)
        {
            float turnCos = cos(0.5);
            float turnSin = sin(0.5);
            float amplitude = 0.3 * 3.0;
            float speed = 1.0;
            float m00 = 0.01;
            float m01 = 0.0;
            float m10 = 0.0;
            float m11 = 0.01;
            float height = 0.0;
            float2 slope = float2(0.0, 0.0);
            [unroll] for (int octave = 0; octave < 8; octave++)
            {
                float period = 3.0 / speed;
                float ratio = (float)(octave / 2) / (4.0 - 1.0 + 1e-3);
                float phase = ShadeFractional((clock / period) + ratio + ((float)(octave % 2) * 0.5));
                float opacity = phase * (1.0 - phase) * 4.0;
                float qx = (place.x * m00) + (place.y * m10);
                float qy = (place.x * m01) + (place.y * m11);
                float gx;
                float gy;
                float value = ShadeRipple(qx, qy, gx, gy);
                float weight = amplitude * opacity;
                height += value * weight;
                slope += float2((gx * m00) + (gy * m01), (gx * m10) + (gy * m11)) * weight;
                float finer = (octave % 2) == 1 ? 2.0 : 1.0;
                float n00 = ((m00 * turnCos) - (m01 * turnSin)) * finer;
                float n01 = ((m00 * turnSin) + (m01 * turnCos)) * finer;
                float n10 = ((m10 * turnCos) - (m11 * turnSin)) * finer;
                float n11 = ((m10 * turnSin) + (m11 * turnCos)) * finer;
                m00 = n00;
                m01 = n01;
                m10 = n10;
                m11 = n11;
                if ((octave % 2) == 1)
                {
                    amplitude *= 0.2;
                    speed *= 1.3;
                }
            }

            return float4(normalize(float3(-slope.x, -slope.y, -1.0)), height);
        }

        // ShadeProgram.DistortionRead: SamplerLinearWrap at level nought over the distortion texture, the four nearest texels by hand.
        float4 ShadeRiverRead(float2 spot)
        {
            uint width;
            uint height;
            DistortionSheet.GetDimensions(width, height);
            float2 at = (spot * float2(width, height)) - 0.5;
            float2 low = floor(at);
            float2 share = at - low;
            int2 size = int2(width, height);
            int2 a = (((int2)low % size) + size) % size;
            int2 b = (a + 1) % size;
            float4 top = lerp(DistortionSheet.Load(int3(a.x, a.y, 0)), DistortionSheet.Load(int3(b.x, a.y, 0)), share.x);
            float4 bottom = lerp(DistortionSheet.Load(int3(a.x, b.y, 0)), DistortionSheet.Load(int3(b.x, b.y, 0)), share.x);
            return lerp(top, bottom, share.y);
        }

        // mul(RotationMatrix(angle), v).
        float2 ShadeRiverTurned(float angle, float2 v)
        {
            float c = cos(angle);
            float s = sin(angle);
            return float2((c * v.x) + (s * v.y), (-s * v.x) + (c * v.y));
        }

        // ShadeProgram.RiverFoam: GetFlowingFoamColor at no flow - its colour white, how much it covers in w.
        float4 ShadeRiverFoam(float2 place, float clock, float flowFoam)
        {
            uint width;
            uint height;
            DistortionSheet.GetDimensions(width, height);
            if (width == 0 || height == 0)
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }

            float strength = saturate((0.015 * flowFoam) + 0.25);
            float foam = 0.0;
            float mask = 0.0;
            float weights = 0.0;
            [unroll] for (int layer = 0; layer < 4; layer++)
            {
                int harmonic = layer / 2;
                float scramble = ShadeRiverRead(place * (0.0005 * (float)(1 << harmonic))).x * 0.2;
                float phase = ShadeFractional((clock * 0.2) + ((float)(layer % 2) * 0.5) + (0.5 * (float)harmonic / 2.0) + scramble);
                float opacity = min(phase, 1.0 - phase) * 2.0;
                float frequency = harmonic == 0 ? 1.0 : 1.8;
                float3 hash = ShadeHash33(float3((float)layer, 0.0, 0.0));
                float2 at = (place * (0.01 * frequency)) + hash.xy;
                float4 turbulence = ShadeRiverRead(ShadeRiverTurned(0.5, at / 7.0));
                at += (turbulence.xy - 0.5) * 2.0 * phase / 0.2 * 0.2 * (10.0 * saturate(strength - 0.2));
                float4 moved = ShadeRiverRead(ShadeRiverTurned(2.0, at / 10.0));
                at += (moved.xy - 0.5) * 100.0;
                float4 masked = ShadeRiverRead(ShadeRiverTurned(-1.0, at) * 0.03);
                float noise = (1.0 - (((sin(at.x * 2.7) * 0.5) + 0.5) * ((sin(at.y * 2.3) * 0.5) + 0.5))) * 0.3;
                weights += opacity;
                foam += saturate(1.0 - noise) * opacity;
                mask += masked.w * opacity;
            }

            foam /= weights;
            mask /= weights;
            float maskThreshold = saturate((1.0 - mask - 0.4) * 3.0);
            foam = saturate((foam - maskThreshold) / (1.3 - maskThreshold) * 1.3);
            float threshold = 1.0 + ((0.3 - 1.0) * strength);
            foam = saturate((foam - threshold) / (1.4 - threshold) * 1.4);
            return float4(1.0, 1.0, 1.0, foam);
        }
        """;
}
