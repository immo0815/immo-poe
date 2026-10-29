namespace PoEformance.Game.Files;

/// <summary>The engine's river surface on the card - see ShadeProgram.River.</summary>
public static partial class ShadeHlsl
{
    /// <summary>
    /// ShadeProgram.River, RiverFoam, RiverFlowAt and RiverFoaming, line for line, for a program with a <c>River</c> step - after
    /// <see cref="WavesLibrary"/>, whose ripple texture it reads, and <see cref="FoamLibrary"/>, whose hash it takes. Its foam reads the
    /// including shader's <c>DistortionSheet</c> (ModelShaders' DistortionSlot), the card binding it for a river; where nothing is bound the
    /// foam is nought, as the processor's is without its texture. Its flow reads the including shader's <c>RiverFlowSheet</c> (ModelShaders'
    /// RiverFlowSlot) over the frame's <c>RiverBox</c> - the map's box in the picture's frame, its size nought where the drawing carries no
    /// map, and everything then as at no flow, as the processor's (WaterLook.Flow).
    /// </summary>
    public static readonly string RiverLibrary = Rivers();

    private static string Rivers() => """
        // RiverFlow.Sample: GetFlowSample over the drawing's map - SamplerLinearWrap at level nought, the four nearest texels by hand;
        // nought where no map is bound (RiverBox's size nought).
        float4 ShadeRiverSample(float2 place)
        {
            if (RiverBox.z <= 0.0)
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }

            uint width;
            uint height;
            RiverFlowSheet.GetDimensions(width, height);
            int w = (int)width;
            int h = (int)height;
            float sx = (((place.x - RiverBox.x) / RiverBox.z) * (float)width) - 0.5;
            float sy = (((place.y - RiverBox.y) / RiverBox.w) * (float)height) - 0.5;
            float lx = floor(sx);
            float ly = floor(sy);
            float fx = sx - lx;
            float fy = sy - ly;
            int x0 = (((int)lx % w) + w) % w;
            int y0 = (((int)ly % h) + h) % h;
            int x1 = (x0 + 1) % w;
            int y1 = (y0 + 1) % h;
            float4 a = RiverFlowSheet.Load(int3(x0, y0, 0));
            float4 b = RiverFlowSheet.Load(int3(x1, y0, 0));
            float4 c = RiverFlowSheet.Load(int3(x0, y1, 0));
            float4 d = RiverFlowSheet.Load(int3(x1, y1, 0));
            float4 top = a + ((b - a) * fx);
            float4 bottom = c + ((d - c) * fx);
            return top + ((bottom - top) * fy);
        }

        // ShadeProgram.RiverFlowAt: GetFlowingWaterPlanarStateWorld's flow_intensity - the sample's velocity times twice the area's
        // flow_intensity, its length held to 100; nought with no map.
        float2 ShadeRiverFlowAt(float2 place, float intensity)
        {
            if (RiverBox.z <= 0.0)
            {
                return float2(0.0, 0.0);
            }

            float4 read = ShadeRiverSample(place);
            float speedMult = 2.0 * intensity;
            float2 flow = read.xy * speedMult;
            float len = length(flow);
            return flow * (min(len, 100.0) / (len + 1e-3));
        }

        // ShadeProgram.RiverFoaming: the foam's strength in x and the bubbles in y - the curl, the speed, the speed over shallow water and
        // the march of 25 half-second steps up the flow; with no map the still river's strength and no bubbles.
        float2 ShadeRiverFoaming(float2 place, float depth, float intensity, float flowFoam)
        {
            if (RiverBox.z <= 0.0)
            {
                return float2(saturate((0.015 * flowFoam) + 0.25), 0.0);
            }

            float speedMult = 2.0 * intensity;
            float2 flow = ShadeRiverFlowAt(place, intensity);
            float eps = 100.1;
            float4 xGradient = (ShadeRiverSample(place + float2(eps, 0.0)) - ShadeRiverSample(place + float2(-eps, 0.0))) / eps;
            float4 yGradient = (ShadeRiverSample(place + float2(0.0, eps)) - ShadeRiverSample(place + float2(0.0, -eps))) / eps;
            float curl = xGradient.y - yGradient.x;
            float speed = length(flow);
            float foam = saturate(abs(curl) * speedMult * 0.5);
            foam += speed * 0.005;
            foam += speed * saturate(1.0 / (depth + 5.0) * 25.0) * 0.01;
            float bubbles = saturate(max(0.0, foam * foam) * 0.1);
            float2 at = place;
            [loop] for (int marched = 0; marched < 25; marched++)
            {
                float4 read = ShadeRiverSample(at);
                float2 flowSpeed = read.xy * speedMult;
                float here = (1.0 - ((read.w + -100.0) * 0.02)) * ((length(flowSpeed) * 0.1) + 0.05) * exp(-(float)marched / 25.0 * 2.0) * 0.1;
                foam = max(foam, here);
                at += -flowSpeed * 0.5;
            }

            foam *= flowFoam;
            foam += 0.25;
            return float2(saturate(foam), bubbles);
        }

        // ShadeProgram.River: GetFlowSurfaceField at a flow - eight octaves fading in and out, scrolled back along the flow; its normal and height.
        float4 ShadeRiver(float2 place, float clock, float2 flow)
        {
            float turnCos = cos(0.5);
            float turnSin = sin(0.5);
            float len = length(flow);
            float amplitude = 0.3 * (len + 3.0);
            float speed = 1.0;
            float drop = 0.2 + ((0.9 - 0.2) * saturate(len / 300.0));
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
                float angle = -0.3 + ((0.3 - -0.3) * ratio);
                float c = cos(angle);
                float s = sin(angle);
                float scale = phase * speed * period;
                float px = place.x + ((((-flow.x) * c) + ((-flow.y) * -s)) * scale);
                float py = place.y + ((((-flow.x) * s) + ((-flow.y) * c)) * scale);
                float qx = (px * m00) + (py * m10);
                float qy = (px * m01) + (py * m11);
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
                    amplitude *= drop;
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

        // ShadeProgram.RiverFoamNode: GetFlowingFoamColorFixedFreq - one node's four layers, scrolled along the flow, hashed with its seed.
        float4 ShadeRiverFoamNode(float2 place, float clock, float2 flow, float strength, float phaseFrequency, float seed)
        {
            float foam = 0.0;
            float mask = 0.0;
            float weights = 0.0;
            [unroll] for (int layer = 0; layer < 4; layer++)
            {
                int harmonic = layer / 2;
                float scramble = ShadeRiverRead(place * (0.0005 * (float)(1 << harmonic))).x * 0.2;
                float phase = ShadeFractional((clock * phaseFrequency) + ((float)(layer % 2) * 0.5) + (0.5 * (float)harmonic / 2.0) + scramble);
                float opacity = min(phase, 1.0 - phase) * 2.0;
                float frequency = harmonic == 0 ? 1.0 : 1.8;
                float2 scrolled = place - (flow * 1.5 * (phase - 0.5) / phaseFrequency);
                float3 hash = ShadeHash33(float3((float)layer, seed, 0.0));
                float2 at = (scrolled * (0.01 * frequency)) + hash.xy;
                float4 turbulence = ShadeRiverRead(ShadeRiverTurned(0.5, at / 7.0));
                at += (turbulence.xy - 0.5) * 2.0 * phase / phaseFrequency * 0.2 * (10.0 * saturate(strength - 0.2));
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

        // ShadeProgram.RiverFoam: GetFlowingFoamColor - two nodes of frequency mixed, a node of no weight not worked out; its colour white,
        // how much it covers in w; nought where the distortion texture is not bound.
        float4 ShadeRiverFoam(float2 place, float clock, float2 flow, float strength)
        {
            uint width;
            uint height;
            DistortionSheet.GetDimensions(width, height);
            if (width == 0 || height == 0)
            {
                return float4(0.0, 0.0, 0.0, 0.0);
            }

            float nodes = 0.02 * length(flow) * 10.01;
            float phaseA = (0.5 + (nodes * 0.5)) - floor(0.5 + (nodes * 0.5));
            float phaseB = (nodes * 0.5) - floor(nodes * 0.5);
            float seedA = floor(0.5 + (nodes * 0.5)) * 2.0;
            float seedB = (floor(nodes * 0.5) * 2.0) + 1.0;
            float weightA = min(phaseA, 1.0 - phaseA) * 2.0;
            float weightB = min(phaseB, 1.0 - phaseB) * 2.0;
            float4 result = float4(0.0, 0.0, 0.0, 0.0);
            if (weightA > 0.0)
            {
                result += ShadeRiverFoamNode(place, clock, flow, strength, 0.2 + (seedA / 10.01), seedA) * weightA;
            }

            if (weightB > 0.0)
            {
                result += ShadeRiverFoamNode(place, clock, flow, strength, 0.2 + (seedB / 10.01), seedB) * weightB;
            }

            return result;
        }

        // The river step's foam: its strength and flow over the drawing's map, then GetFlowingFoamColor.
        float4 ShadeRiverFoamAt(float2 place, float clock, float depth, float intensity, float flowFoam)
        {
            float2 foaming = ShadeRiverFoaming(place, depth, intensity, flowFoam);
            return ShadeRiverFoam(place, clock, ShadeRiverFlowAt(place, intensity), foaming.x);
        }

        // The river step's bubbles - none with no map.
        float ShadeRiverBubbles(float2 place, float depth, float intensity, float flowFoam)
        {
            return RiverBox.z <= 0.0 ? 0.0 : ShadeRiverFoaming(place, depth, intensity, flowFoam).y;
        }
        """;
}
