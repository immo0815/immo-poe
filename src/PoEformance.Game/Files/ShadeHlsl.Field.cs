using System.Text;

namespace PoEformance.Game.Files;

/// <summary>The engine's field waves on the card - see ShadeProgram.Field.</summary>
public static partial class ShadeHlsl
{
    /// <summary>The slot the field waves' wave shape is read from - ShadeProgram.FieldProfile, by the pixel and the vertex shaders alike.</summary>
    public const int FieldProfileSlot = 15;

    /// <summary>The slot the field waves' foam colour is read from - ShadeProgram.FieldFoamColour.</summary>
    public const int FieldFoamColourSlot = 16;

    /// <summary>The slot the field waves' foam normal is read from - ShadeProgram.FieldFoamNormal.</summary>
    public const int FieldFoamNormalSlot = 17;

    /// <summary>
    /// The engine's field waves - GetFieldWaveGeom, CrashingWavesVertex's move and GetFieldFoamSurface, line for line - after
    /// <see cref="WavesLibrary"/>, <see cref="FoamLibrary"/> and <see cref="BeachLibrary"/>, whose shore, hashes and noise they read.
    /// </summary>
    public static readonly string FieldLibrary = Fields();

    /// <summary>
    /// A field step: its two outputs one sum, written once into a variable of its own and read by each - as <see cref="Beach"/> does;
    /// the foam's gradients taken at the program's top, outside any branch, so its reads may sit inside one.
    /// </summary>
    private static void Field(StringBuilder hlsl, ShadeProgram.Step step, int[] versions, Dictionary<(int, int, int, int, int, int, int, int, int), string> cached)
    {
        var key = (step.A, step.B, step.C, step.D, 2 << 24, versions[step.A], versions[step.B], versions[step.C], versions[step.D]);
        if (!cached.TryGetValue(key, out string? name))
        {
            name = "field" + Invariant(cached.Count);
            cached[key] = name;
            hlsl.Append("    ShadeFieldOut ").Append(name).Append(" = ShadeFieldAt(")
                .Append(R(step.A)).Append(".xy, ").Append(R(step.B)).Append(".xy, ").Append(R(step.C)).Append(", ").Append(R(step.D))
                .AppendLine(", wrap, fieldDx, fieldDy);");
        }

        hlsl.Append("    ").Append(R(step.To)).Append(" = ").Append(step.Extra == ShadeProgram.FieldColour
            ? name + ".colour"
            : "ShadeFieldTurned(" + R(step.E) + ", " + name + ".grad)").AppendLine(";");
    }

    /// <summary><see cref="FieldLibrary"/>: field_waves and what it includes of the engine's - see ShadeProgram.Field.</summary>
    private static string Fields() => $$"""
        Texture2D<float4> ShadeFieldProfile : register(t{{FieldProfileSlot}});
        Texture2D ShadeFieldFoamColour : register(t{{FieldFoamColourSlot}});
        Texture2D ShadeFieldFoamNormal : register(t{{FieldFoamNormalSlot}});

        static const float ShadeFieldLength = {{Number(ShadeProgram.FieldWaveLength)}};
        static const float ShadeFieldSpeed = {{Number(ShadeProgram.FieldWaveSpeed)}};

        struct ShadeFieldOut
        {
            float4 colour;
            float2 grad;
        };

        struct ShadeFieldGeom
        {
            float3 normalOffset;
            float2 velocity;
            float3 shoreDist;
            float breakPhase;
            float waveCoord;
        };

        // GetGerstnerProfile's read, SamplerLinearWrapClampWrap: wrapping along the wave's coordinate, held along the break phase -
        // its red and its blue, the four nearest texels' centres mixed.
        float2 ShadeFieldProfileAt(float u, float v)
        {
            uint across;
            uint down;
            ShadeFieldProfile.GetDimensions(across, down);
            int width = (int)across;
            int height = (int)down;
            float sx = (u * (float)width) - 0.5;
            float sy = clamp((v * (float)height) - 0.5, 0.0, (float)height - 1.0);
            float fx0 = floor(sx);
            if (!(abs(fx0) <= 16777216.0) || !(abs(sy) <= 3.40282347e38))
            {
                return float2(0.0, 0.0);
            }

            float fx = sx - fx0;
            int x0 = ((((int)fx0) % width) + width) % width;
            int x1 = (x0 + 1) % width;
            int y0 = (int)sy;
            int y1 = min(y0 + 1, height - 1);
            float fy = sy - (float)y0;
            float2 a = ShadeFieldProfile.Load(int3(x0, y0, 0)).xz;
            float2 b = ShadeFieldProfile.Load(int3(x1, y0, 0)).xz;
            float2 c = ShadeFieldProfile.Load(int3(x0, y1, 0)).xz;
            float2 d = ShadeFieldProfile.Load(int3(x1, y1, 0)).xz;
            float2 top = a + ((b - a) * fx);
            float2 bottom = c + ((d - c) * fx);
            return top + ((bottom - top) * fy);
        }

        // GetGerstnerProfile's normal offset field: the height (blue) and its rise along the coordinate and the break phase, a hundredth apart.
        float3 ShadeFieldShape(float coord, float breaking)
        {
            float height = ShadeFieldProfileAt(coord, breaking).y;
            float alongCoord = ShadeFieldProfileAt(coord + 1e-2, breaking).y;
            float alongBreak = ShadeFieldProfileAt(coord, breaking + 1e-2).y;
            return float3(height, (alongCoord - height) / 1e-2, (alongBreak - height) / 1e-2);
        }

        // field_ops: FieldMul, FieldInverse, FieldMax, FieldOneMinus, FieldSqr, FieldRampNormHalf and FieldRampNorm.
        float3 ShadeFieldMul(float3 a, float3 b)
        {
            return float3(a.x * b.x, (a.x * b.yz) + (b.x * a.yz));
        }

        float3 ShadeFieldInverse(float3 value)
        {
            float inv = 1.0 / value.x;
            return float3(inv, -inv * inv * value.yz);
        }

        float3 ShadeFieldMax(float3 a, float3 b)
        {
            return a.x > b.x ? a : b;
        }

        float3 ShadeFieldOneMinus(float3 value)
        {
            return float3(1.0 - value.x, -value.yz);
        }

        float3 ShadeFieldSqr(float3 value)
        {
            return float3(value.x * value.x, 2.0 * value.x * value.yz);
        }

        float3 ShadeFieldRampNormHalf(float3 value, float3 center)
        {
            float3 under = ShadeFieldMul(ShadeFieldSqr(2.0 * center), ShadeFieldOneMinus(value)) + value;
            return ShadeFieldMul(value, ShadeFieldInverse(ShadeFieldMax(under, float3(1e-5, 0.0, 0.0))));
        }

        float3 ShadeFieldRampNorm(float3 value, float3 center)
        {
            center.x = clamp(center.x, 1e-3, 1.0 - 1e-3);
            bool mirror = center.x > 0.5;
            float3 mirroredValue = mirror ? ShadeFieldOneMinus(value) : value;
            float3 mirroredCenter = mirror ? ShadeFieldOneMinus(center) : center;
            float3 ramp = ShadeFieldRampNormHalf(mirroredValue, mirroredCenter);
            return mirror ? ShadeFieldOneMinus(ramp) : ramp;
        }

        // GetAdvectedNoise: the noise's green at the two time layers, the coordinates carried back along the velocity.
        float ShadeFieldAdvected(float2 uv, float2 velocity, float phase)
        {
            float2 phases;
            float2 seeds;
            float2 weights;
            ShadeInterpNodes(phase, phases, seeds, weights);
            float noise = 0.0;
            [unroll] for (int layer = 0; layer < 2; layer++)
            {
                float2 layerUv = uv - (velocity * (phases[layer] - 0.5) * 2.0);
                float3 hash = ShadeHashStable(seeds[layer]);
                noise += ShadeNoiseAt(layerUv + hash.xy).y * weights[layer];
            }

            return noise;
        }

        // GetAdvectedNoiseField4: the noise and its rise along u, v and the phase, a hundredth apart.
        float4 ShadeFieldAdvectedField(float2 uv, float2 velocity, float phase)
        {
            float centre = ShadeFieldAdvected(uv, velocity, phase);
            float alongX = ShadeFieldAdvected(uv + float2(1e-2, 0.0), velocity, phase);
            float alongY = ShadeFieldAdvected(uv + float2(0.0, 1e-2), velocity, phase);
            float alongT = ShadeFieldAdvected(uv, velocity, phase + 1e-2);
            return float4(centre, (alongX - centre) / 1e-2, (alongY - centre) / 1e-2, (alongT - centre) / 1e-2);
        }

        // GetShoreFieldData: the shore's distance and the distance out to sea, world units, each with its rise along x and y -
        // central differences a texel apart; and the ground's shore's distance alike (ShoreField.GroundTexels).
        void ShadeFieldShore(float2 place, float4 box, out float3 shoreDist, out float3 oceanDist, out float3 groundDist)
        {
            float eps = box.z;
            float3 centre = ShadeShoreAll(place, box);
            float3 xp = ShadeShoreAll(place + float2(eps, 0.0), box);
            float3 xn = ShadeShoreAll(place - float2(eps, 0.0), box);
            float3 yp = ShadeShoreAll(place + float2(0.0, eps), box);
            float3 yn = ShadeShoreAll(place - float2(0.0, eps), box);
            float inv = 0.5 / eps;
            float reach = {{Number(ShoreField.Reach)}};
            shoreDist = float3(centre.x, (xp.x - xn.x) * inv, (yp.x - yn.x) * inv) * reach;
            oceanDist = float3(1.0 - centre.y, -(xp.y - xn.y) * inv, -(yp.y - yn.y) * inv) * reach;
            groundDist = float3(centre.z, (xp.z - xn.z) * inv, (yp.z - yn.z) * inv) * reach;
        }

        // GetFieldWaveGeom: the crest's height and slope, the wave's velocity, the shore's distance, the break phase and the coordinate -
        // the break phase off the ground's shore's distance where byGround (SceneLight.WaterBreakByGround, CHOSEN), the shore's otherwise.
        ShadeFieldGeom ShadeFieldGeomAt(float2 place, float clock, float4 box, bool byGround)
        {
            float3 shoreDist;
            float3 oceanDist;
            float3 groundDist;
            ShadeFieldShore(place, box, shoreDist, oceanDist, groundDist);
            float2 dir = -oceanDist.yz;
            float3 stationary = ShadeStationaryNoise(place * 1.2e-4);
            stationary.yz *= 1.2e-4;
            float4 advected = ShadeFieldAdvectedField(place * 1.2e-4, dir * ShadeFieldSpeed * 1.0 * 1.2e-4 / 0.1, clock * 0.1);
            advected.yz *= 1.2e-4;
            advected.w *= 0.1 * 0.0;
            float4 coord = float4(oceanDist * (-1.0 / ShadeFieldLength), 0.0);
            coord += float4(clock, 0.0, 0.0, 1.0) * (-ShadeFieldSpeed / ShadeFieldLength);
            coord += advected;
            coord += float4(stationary, 0.0);
            float3 breakDist = byGround ? groundDist : shoreDist;
            float3 breakField = ShadeFieldRampNorm(ShadeFieldOneMinus(breakDist * 0.0002), (stationary * 0.3) + float3(0.5, 0.0, 0.0));
            float3 shape = ShadeFieldShape(coord.x, breakField.x) * 0.3;
            ShadeFieldGeom geom;
            geom.normalOffset = float3(shape.x, (coord.yz * shape.y) + (breakField.yz * shape.z)) * ShadeFieldLength;
            geom.velocity = dir * ShadeFieldSpeed;
            geom.shoreDist = shoreDist;
            geom.breakPhase = breakField.x;
            geom.waveCoord = coord.x;
            return geom;
        }

        // CrashingWavesVertex's move along z, the area's height offset added - nought where the field waves are off or there is no shore;
        // swell.w one where they are on, two where their break phase is the ground's (ModelGpu's Swell register).
        float ShadeFieldLift(float2 place, float clock, float heightOffset, float4 box, float4 swell)
        {
            [branch] if (!(box.w > 0.5) || !(swell.w > 0.5))
            {
                return 0.0;
            }

            return -ShadeFieldGeomAt(place, clock, box, swell.w > 1.5).normalOffset.x + heightOffset;
        }

        // GetWaveInterpNodes, eight nodes a wave, the curve two.
        void ShadeFieldNodes(float waveCoord, out float2 local, out float2 exterp, out float2 indices, out float2 weights, out float overlap)
        {
            float nodes = 8.0;
            float coord = floor(waveCoord) + pow(waveCoord - floor(waveCoord), 2.0);
            float global = coord * nodes;
            float2 phases;
            float2 seeds;
            ShadeInterpNodes(global, phases, seeds, weights);
            float2 shifted = (seeds + 0.5) / nodes;
            float2 linearLocal = floor((shifted - floor(shifted)) * nodes) / nodes;
            local = pow(linearLocal, 0.5);
            float2 linearExterp = linearLocal + ((global - seeds) / nodes);
            exterp = floor(linearExterp) + pow(linearExterp - floor(linearExterp), 0.5);
            indices = floor(seeds / nodes);
            float interval = floor(((global / nodes) - floor(global / nodes)) * nodes);
            overlap = 1.0 - saturate(abs(interval - (nodes - 1.0)));
        }

        // GetFieldFoamVelocity.
        float ShadeFieldFoamVelocity(float local, float breaking)
        {
            float velocity = 1.5 * pow(saturate(local / 0.95), 10.0);
            float over = saturate(((local / 0.95) - 1.0) / (1.0 - 0.95));
            velocity *= 0.8 + (0.2 * (1.0 - (over * over)));
            velocity *= ShadePolyStep(breaking, 0.5, 0.9);
            return velocity;
        }

        // GetFieldFoamIntensity as written - SmoothStepSimple's 3x² - 2x² too.
        float ShadeFieldFoamIntensity(float foamCoord, float breaking)
        {
            float breakFoam = ShadePolyStep(breaking, 0.53, 0.95);
            float spike = 0.3 * breakFoam;
            float tail = 0.2 * breakFoam;
            float constant = 0.1 * breakFoam;
            float humped = pow(saturate(foamCoord / 0.9), 2.0);
            float intensity = ((3.0 * humped * humped) - (2.0 * humped * humped)) * spike;
            intensity += humped * tail;
            intensity += constant;
            return lerp(intensity, (intensity * 0.7) + 0.3, breaking);
        }

        // GetFieldFoamProfile: its intensity and its sharpness.
        float2 ShadeFieldFoamProfile(float waveCoord, float shoreDist, float breaking)
        {
            float intensity = ShadeFieldFoamIntensity(waveCoord, breaking);
            intensity += saturate(exp(-(shoreDist - 150.0) / 100.0)) * 0.7;
            intensity = saturate(intensity);
            return float2(intensity, 0.5 + (0.4 * intensity));
        }

        // SampleFlowingTexture over the noise: its red and green at two time layers.
        float2 ShadeFieldFlowing(float2 uv, float2 strength, float clock, float period)
        {
            float2 phases;
            float2 seeds;
            float2 weights;
            ShadeInterpNodes(clock * (1.0 / period), phases, seeds, weights);
            float2 said = float2(0.0, 0.0);
            [unroll] for (int node = 0; node < 2; node++)
            {
                float3 hash = ShadeHash33(float3(seeds[node], 0.0, 0.0));
                float2 nodeUv = uv + hash.xy - (strength * period * ((phases[node] * 2.0) - 1.0));
                said += ShadeNoiseAt(nodeUv) * weights[node];
            }

            return said;
        }

        // GetFoamScrollingLayer with its turbulence - the beach's noise standing for noise_sampler - the sheets read at the gradients
        // of their coordinates, turned as they are.
        ShadeFoamSurface ShadeFieldScrolling(float2 uv, float2 velocity, float turbUv, float turbVelocity, float turbPhase, float phase, float layerSeed, SamplerState wrap, float2 duvx, float2 duvy)
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
                float swing = (phases[layer] * 2.0) - 1.0;
                float2 flow = uv - (velocity * swing);
                float2 turbulence = ShadeNoiseAt((flow * turbUv) + hash.zy);
                float2 moved = flow + hash.xy - ((turbulence - 0.5) * (swing + turbPhase) * turbVelocity);
                float2 turned = float2((moved.x * c) - (moved.y * s), (moved.x * s) + (moved.y * c));
                float2 gx = float2((duvx.x * c) - (duvx.y * s), (duvx.x * s) + (duvx.y * c));
                float2 gy = float2((duvy.x * c) - (duvy.y * s), (duvy.x * s) + (duvy.y * c));
                float4 colour = ShadeLinearised(ShadeFieldFoamColour.SampleGrad(wrap, turned, gx, gy));
                float4 bumps = ShadeFieldFoamNormal.SampleGrad(wrap, turned, gx, gy);
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

        // DissolveFoamSurface.
        ShadeFoamSurface ShadeFieldDissolved(ShadeFoamSurface surface, float intensity, float sharpness)
        {
            float fade = ShadeMathSmoothStep(intensity, 1.0 - surface.normal.x, sharpness);
            surface.colour.w = fade;
            surface.normal = lerp(float3(0.5, 0.0, 0.0), surface.normal, 1.0 - saturate(1.0 - intensity));
            surface.rough *= fade;
            float threshold = lerp(1.0, 0.1, saturate(surface.subsurface));
            surface.subsurface = ShadeMathSmoothStep(intensity, threshold, 0.8);
            return surface;
        }

        // GetFieldFoamSurface: two scrolling layers a node, blended where the nodes do not overlap and overlaid where they do.
        ShadeFoamSurface ShadeFieldFoam(float2 place, float2 velocity, float foamCoord, float3 shoreDist, float breaking, float clock, SamplerState wrap, float2 dpx, float2 dpy)
        {
            float2 following = ShadeFieldFlowing(place * 0.004, velocity * 0.004 * 1.5, clock, 0.5);
            float worldToUv = 2e-3 / 2.0;
            float foamMult = 0.8;
            float frontOffset = lerp(0.09 * 0.75, 0.09 * 1.1, ShadeMathSmoothStep(breaking, 0.6, 0.91));
            float globalCoord = foamCoord - frontOffset;
            float2 local;
            float2 exterp;
            float2 indices;
            float2 weights;
            float overlap;
            ShadeFieldNodes(globalCoord, local, exterp, indices, weights, overlap);
            float interp = globalCoord - floor(globalCoord);
            ShadeFoamSurface layers[2];
            [unroll] for (int layer = 0; layer < 2; layer++)
            {
                float layerLocal = lerp(interp, exterp[layer], overlap);
                layerLocal *= 1.0 - frontOffset;
                layerLocal += frontOffset;
                float2 flow = velocity * ShadeFieldFoamVelocity(layerLocal, breaking);
                float timeFreq = lerp(0.75, 5.0, pow(saturate(local[layer]), 3.0));
                float spaceFreq = worldToUv * lerp(0.75, 2.0, local[layer]);
                float seed = indices[layer] + local[layer];
                layers[layer] = ShadeFieldScrolling(
                    place * spaceFreq, flow * spaceFreq / timeFreq, 1.5, 0.05, 1.0, clock * timeFreq, seed, wrap, dpx * spaceFreq, dpy * spaceFreq);
            }

            ShadeFoamSurface surface = layers[0];
            if (overlap < 0.5)
            {
                // BlendSurfaces, then DissolveFoamSurface by the profile at the wave's own coordinate.
                float share = weights.y;
                surface.colour = lerp(layers[0].colour, layers[1].colour, share);
                surface.normal = lerp(layers[0].normal, layers[1].normal, share);
                surface.ao = lerp(layers[0].ao, layers[1].ao, share);
                surface.rough = lerp(layers[0].rough, layers[1].rough, share);
                surface.subsurface = lerp(layers[0].subsurface, layers[1].subsurface, share);
                surface.sqrWeight = (layers[0].sqrWeight * (1.0 - share) * (1.0 - share)) + (layers[1].sqrWeight * share * share);
                float2 profile = ShadeFieldFoamProfile(interp, shoreDist.x, breaking);
                surface = ShadeFieldDissolved(surface, profile.x, profile.y);
            }
            else
            {
                float sprayRamp = (ShadePolyStep(following.x, 0.5, 0.3) - 0.5) * 7.0;
                sprayRamp = lerp(3.0, sprayRamp, ShadeRampPow(saturate(breaking - 0.45) * 2.0, -5.0));
                float2 ramps = float2(-20.0, sprayRamp);
                [unroll] for (int fading = 0; fading < 2; fading++)
                {
                    float fadeout = ShadeRampPow(weights[fading], ramps[fading]);
                    float2 profile = ShadeFieldFoamProfile(exterp[fading], shoreDist.x, breaking);
                    layers[fading] = ShadeFieldDissolved(layers[fading], profile.x * fadeout, profile.y);
                }

                float darkening = lerp(0.5 + (0.5 * weights.x), 1.0, saturate(breaking));
                layers[0].colour.xyz *= lerp(1.0, darkening, foamMult);
                layers[1].subsurface *= pow(saturate(weights.y), 3.0);

                // OverlaySurfaces.
                float a0 = layers[0].colour.w;
                float a1 = layers[1].colour.w;
                surface.colour.w = 1.0 - ((1.0 - a0) * (1.0 - a1));
                float inv = 1.0 / (surface.colour.w + 1e-5);
                surface.colour.xyz = lerp(layers[0].colour.xyz * a0, layers[1].colour.xyz, a1) * inv;
                surface.normal = lerp(layers[0].normal * a0, layers[1].normal, a1) * inv;
                surface.ao = lerp(layers[0].ao * a0, layers[1].ao, a1) * inv;
                surface.rough = lerp(layers[0].rough * a0, layers[1].rough, a1) * inv;
                surface.subsurface = 1.0 - ((1.0 - layers[0].subsurface) * (1.0 - layers[1].subsurface));
                surface.sqrWeight = 0.0;
            }

            return surface;
        }

        // CrashingWavesFragment: the foam's colour and how much it covers, and the slope its normal is made from - the crest's and
        // half the foam's. Nought where the field waves are off or there is no shore, the reads skipped.
        ShadeFieldOut ShadeFieldAt(float2 place, float2 timing, float4 box, float4 swell, SamplerState wrap, float2 dpx, float2 dpy)
        {
            ShadeFieldOut said;
            said.colour = float4(0.0, 0.0, 0.0, 0.0);
            said.grad = float2(0.0, 0.0);
            [branch] if (box.w > 0.5 && swell.w > 0.5)
            {
                float clock = timing.x;
                ShadeFieldGeom geom = ShadeFieldGeomAt(place, clock, box, swell.w > 1.5);
                ShadeFoamSurface foam = ShadeFieldFoam(place, geom.velocity, geom.waveCoord, geom.shoreDist, geom.breakPhase, clock, wrap, dpx, dpy);
                said.colour = float4(foam.colour.xyz, saturate(foam.colour.w));
                said.grad = geom.normalOffset.yz + (foam.normal.yz * 0.5);
            }

            return said;
        }

        // The waves' normal turned by the field waves' slope, their height kept: the waves' own slope back out of their normal, the
        // field's added, the normal made again.
        float4 ShadeFieldTurned(float4 waves, float2 grad)
        {
            float under = abs(waves.z) > 1e-7 ? waves.z : -1e-7;
            float2 slope = float2(waves.x / under, waves.y / under) + grad;
            return float4(normalize(float3(-slope.x, -slope.y, -1.0)), waves.w);
        }
        """;
}
