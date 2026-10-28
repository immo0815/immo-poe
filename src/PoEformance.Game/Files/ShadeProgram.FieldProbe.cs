using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// The engine's field waves worked out on the processor, for the probe alone - ShadeHlsl.FieldLibrary's functions line for line. See
/// <see cref="FieldProbe(Vector3, Vector3, Vector3, Vector3, float, float, ShoreField, bool)"/>.
/// </summary>
public sealed partial class ShadeProgram
{
    /// <summary>GetFieldWaveGeom's state at a place, with what it was made from - the card's ShadeFieldGeom.</summary>
    private readonly record struct FieldGeom(
        Vector3 ShoreDist, Vector3 OceanDist, Vector3 GroundDist, Vector3 Stationary, Vector3 Raw, Vector3 Break, float WaveCoord, Vector3 NormalOffset,
        Vector2 Velocity);

    /// <summary>What the field waves' foam came to at a place, and how it was laid.</summary>
    private readonly record struct FieldFoamOut(Vector4 Colour, bool Overlaid, float Interp, Vector2 Layers, float Level);

    /// <summary>
    /// The field waves at a probed pixel of water, term by term - see <see cref="ProbeField"/>: the card's functions, worked out on the processor.
    /// </summary>
    /// <param name="drawn">Where the processor's water stood at the pixel - lifted by everything but the field waves, which it does not draw.</param>
    /// <param name="toEye">The way to the eye, in the model's space - the picture's view is parallel, so one way for every pixel.</param>
    /// <param name="right">One pixel to the right, in the model's space.</param>
    /// <param name="down">One pixel down.</param>
    /// <param name="clock">The clock, in seconds.</param>
    /// <param name="heightOffset">The area's <c>water_height_offset</c> - WaterLook.FlowRegister's z.</param>
    /// <param name="shore">The room's shore.</param>
    /// <param name="on">Whether the card draws them - the light panel's switch (SceneLight.WaterCrash).</param>
    /// <param name="byGround">Whether their break phase takes the ground's shore's distance (SceneLight.WaterBreakByGround).</param>
    /// <remarks>
    /// AT THE PLACE THE CARD DRAWS, not the processor's: the card lifts the water by the crest (CrashingWavesVertex's move, the area's
    /// height offset in it) and draws the nearest crossing of the lifted sea alone, so the view through the probed pixel meets it
    /// elsewhere than the processor's flat water - tens of units along the view at a crest. <see cref="FieldCrossing"/> finds that
    /// crossing, the water's flat plane at the processor's height lifted by the field waves' lift; the foam's reads take the level
    /// the card's gradients come to, ddx and ddy of the place being the crossings one pixel across and one down.
    /// </remarks>
    public static ProbeField FieldProbe(
        Vector3 drawn, Vector3 toEye, Vector3 right, Vector3 down, float clock, float heightOffset, ShoreField shore, bool on, bool byGround = false)
        => FieldProbe(FieldProfile, BeachNoise, FieldFoamColour, FieldFoamNormal, drawn, toEye, right, down, clock, heightOffset, shore, on, byGround);

    /// <summary>The same, over a given wave shape, noise and foam.</summary>
    public static ProbeField FieldProbe(
        WaterSheet profile, WaterSheet noise, Mipmaps foamColour, Mipmaps foamNormal, Vector3 drawn, Vector3 toEye, Vector3 right, Vector3 down,
        float clock, float heightOffset, ShoreField shore, bool on, bool byGround = false)
    {
        ArgumentNullException.ThrowIfNull(profile);
        ArgumentNullException.ThrowIfNull(noise);
        ArgumentNullException.ThrowIfNull(foamColour);
        ArgumentNullException.ThrowIfNull(foamNormal);
        ArgumentNullException.ThrowIfNull(shore);
        float reach = FieldReach(profile) + MathF.Abs(heightOffset) + 1f;
        Vector3 place = FieldCrossing(profile, noise, drawn, toEye, drawn.Z, reach, clock, heightOffset, shore, byGround, out float off);
        Vector3 across = FieldCrossing(profile, noise, drawn + right, toEye, drawn.Z, reach, clock, heightOffset, shore, byGround, out _);
        Vector3 under = FieldCrossing(profile, noise, drawn + down, toEye, drawn.Z, reach, clock, heightOffset, shore, byGround, out _);
        var at = new Vector2(place.X, place.Y);
        Vector2 dpx = new Vector2(across.X, across.Y) - at, dpy = new Vector2(under.X, under.Y) - at;

        FieldGeom geom = FieldGeomAt(profile, noise, at, clock, shore, byGround);
        FieldFoamOut foam = FieldFoam(noise, foamColour, foamNormal, at, geom.Velocity, geom.WaveCoord, geom.ShoreDist.X, geom.Break.X, clock, dpx, dpy);
        float centre = 0.5f + (geom.Stationary.X * 0.3f);
        return new ProbeField(
            on, byGround, drawn, place, off, geom.ShoreDist.X, new Vector2(geom.ShoreDist.Y, geom.ShoreDist.Z), geom.GroundDist.X, geom.OceanDist.X,
            geom.Velocity / FieldWaveSpeed, geom.Stationary.X, centre, geom.Raw.X, geom.Break.X, geom.WaveCoord,
            -geom.NormalOffset.X + heightOffset, heightOffset, foam.Interp,
            FieldFoamProfileAt(foam.Interp, geom.ShoreDist.X, geom.Break.X), foam.Overlaid, foam.Layers, foam.Colour.W,
            new Vector3(foam.Colour.X, foam.Colour.Y, foam.Colour.Z), foam.Level);
    }

    /// <summary>
    /// GetFieldFoamProfile and GetFieldFoamIntensity term by term, at the wave's own coordinate - see <see cref="FieldFoamProfile"/>.
    /// </summary>
    /// <param name="waveCoord">Where in its wave the place lies, nought to one, nought the foam's front.</param>
    /// <param name="shoreDist">The shore's distance, world units.</param>
    /// <param name="breaking">The break phase.</param>
    public static FieldFoamProfile FieldFoamProfileAt(float waveCoord, float shoreDist, float breaking)
    {
        float breakFoam = PolyStep(breaking, 0.53f, 0.95f);
        float humped = MathF.Pow(Saturated(waveCoord / 0.9f), 2f);
        float spike = ((3f * humped * humped) - (2f * humped * humped)) * (0.3f * breakFoam);
        float tail = humped * (0.2f * breakFoam);
        float constant = 0.1f * breakFoam;
        float sum = spike + tail + constant;
        float wave = Lerp(sum, (sum * 0.7f) + 0.3f, breaking);
        float near = Saturated(MathF.Exp(-(shoreDist - 150f) / 100f)) * 0.7f;
        float intensity = Saturated(wave + near);
        return new FieldFoamProfile(breakFoam, spike, tail, constant, wave, near, intensity, 0.5f + (0.4f * intensity));
    }

    /// <summary>
    /// How far the field waves' lift reaches either way, the area's height offset aside: the wave shape's highest blue times a wave's 0.3.
    /// </summary>
    private static float FieldReach(WaterSheet profile)
    {
        float most = 0f;
        float[] texels = profile.Texels;
        for (int at = 2; at < texels.Length; at += profile.Channels)
        {
            float blue = MathF.Abs(texels[at]);
            most = blue > most ? blue : most;
        }

        return most * 0.3f * FieldWaveLength;
    }

    /// <summary>
    /// Where the view through <paramref name="origin"/> meets the water the field waves lift - its flat plane at <paramref name="level"/>
    /// along z, lifted by <see cref="FieldGeomAt"/>'s crest and the height offset as CrashingWavesVertex lifts it - the crossing nearest
    /// the eye, as the card draws its lifted sea; <paramref name="off"/> how far along z the crossing found misses, NaN where none was.
    /// </summary>
    /// <remarks>
    /// MARCHED, NOT SOLVED BY STEPPING ONTO THE LIFT: a breaking crest leans past the view, and the lift at the place the view first meets
    /// is not the lift where it ends - so the ray is walked from the eye's side of every lift the wave shape can make, four units along
    /// the water at a time, and the first change of side is halved down to the crossing.
    /// </remarks>
    private static Vector3 FieldCrossing(
        WaterSheet profile, WaterSheet noise, Vector3 origin, Vector3 toEye, float level, float reach, float clock, float heightOffset,
        ShoreField shore, bool byGround, out float off)
    {
        off = float.NaN;
        if (!(MathF.Abs(toEye.Z) > 0.05f))
        {
            return origin;
        }

        // ONTO THE WATER'S PLANE first, along the view - a pixel's neighbour starts off it.
        Vector3 start = origin + (toEye * ((level - origin.Z) / toEye.Z));
        var flat = new Vector2(start.X, start.Y);
        var along = new Vector2(toEye.X, toEye.Y);
        float Missed(float t) => (t * toEye.Z) - FieldLift(profile, noise, flat + (along * t), clock, heightOffset, shore, byGround);

        float most = reach / MathF.Abs(toEye.Z);
        float step = MathF.Min(4f / MathF.Max(along.Length(), 1e-3f), most / 16f);
        int steps = Math.Min((int)MathF.Ceiling(2f * most / step), 4096);
        float high = most, missedHigh = Missed(high);
        for (var one = 1; one <= steps; one++)
        {
            float low = MathF.Max(most - (one * step), -most);
            float missedLow = Missed(low);
            if (MathF.Sign(missedLow) != MathF.Sign(missedHigh))
            {
                for (var halving = 0; halving < 24; halving++)
                {
                    float middle = (low + high) * 0.5f;
                    float missedMiddle = Missed(middle);
                    if (MathF.Sign(missedMiddle) == MathF.Sign(missedHigh))
                    {
                        high = middle;
                        missedHigh = missedMiddle;
                    }
                    else
                    {
                        low = middle;
                    }
                }

                float found = (low + high) * 0.5f;
                off = MathF.Abs(Missed(found));
                return start + (toEye * found);
            }

            high = low;
            missedHigh = missedLow;
        }

        return start;
    }

    /// <summary>CrashingWavesVertex's move along z at a place, the area's height offset added - ShadeHlsl's ShadeFieldLift.</summary>
    private static float FieldLift(WaterSheet profile, WaterSheet noise, Vector2 place, float clock, float heightOffset, ShoreField shore, bool byGround)
        => -FieldGeomAt(profile, noise, place, clock, shore, byGround).NormalOffset.X + heightOffset;

    /// <summary>GetFieldWaveGeom as written - ShadeHlsl's ShadeFieldGeomAt, the break phase off the ground's shore where <paramref name="byGround"/>.</summary>
    private static FieldGeom FieldGeomAt(WaterSheet profile, WaterSheet noise, Vector2 place, float clock, ShoreField shore, bool byGround)
    {
        // GetShoreFieldData: the shore's distance and the distance out to sea, world units, each with its rise along x and y - central
        // differences a texel apart.
        float eps = shore.CellSize;
        shore.At(place, out float s0, out float o0);
        shore.At(place + new Vector2(eps, 0f), out float sxp, out float oxp);
        shore.At(place - new Vector2(eps, 0f), out float sxn, out float oxn);
        shore.At(place + new Vector2(0f, eps), out float syp, out float oyp);
        shore.At(place - new Vector2(0f, eps), out float syn, out float oyn);
        float inv = 0.5f / eps;
        Vector3 shoreDist = new Vector3(s0, (sxp - sxn) * inv, (syp - syn) * inv) * ShoreField.Reach;
        Vector3 oceanDist = new Vector3(1f - o0, -(oxp - oxn) * inv, -(oyp - oyn) * inv) * ShoreField.Reach;
        float g0 = shore.GroundAt(place), gxp = shore.GroundAt(place + new Vector2(eps, 0f)), gxn = shore.GroundAt(place - new Vector2(eps, 0f));
        float gyp = shore.GroundAt(place + new Vector2(0f, eps)), gyn = shore.GroundAt(place - new Vector2(0f, eps));
        Vector3 groundDist = new Vector3(g0, (gxp - gxn) * inv, (gyp - gyn) * inv) * ShoreField.Reach;

        var dir = new Vector2(-oceanDist.Y, -oceanDist.Z);
        Vector3 stationary = StationaryNoise(place * 1.2e-4f, noise);
        stationary = new Vector3(stationary.X, stationary.Y * 1.2e-4f, stationary.Z * 1.2e-4f);
        Vector4 advected = FieldAdvectedField(noise, place * 1.2e-4f, dir * (FieldWaveSpeed * 1f * 1.2e-4f / 0.1f), clock * 0.1f);
        advected = new Vector4(advected.X, advected.Y * 1.2e-4f, advected.Z * 1.2e-4f, advected.W * 0.1f * 0f);
        var coord = new Vector4(oceanDist * (-1f / FieldWaveLength), 0f);
        coord += new Vector4(clock, 0f, 0f, 1f) * (-FieldWaveSpeed / FieldWaveLength);
        coord += advected;
        coord += new Vector4(stationary, 0f);

        // GetBreakPhaseField: one less a five-thousandth of the shore's distance, ramped about a centre the noise moves from a half up.
        Vector3 raw = FieldOneMinus((byGround ? groundDist : shoreDist) * 0.0002f);
        Vector3 breaking = FieldRampNorm(raw, (stationary * 0.3f) + new Vector3(0.5f, 0f, 0f));
        Vector3 shape = FieldShape(profile, coord.X, breaking.X) * 0.3f;
        Vector3 normalOffset = new Vector3(
            shape.X, (coord.Y * shape.Y) + (breaking.Y * shape.Z), (coord.Z * shape.Y) + (breaking.Z * shape.Z)) * FieldWaveLength;
        return new FieldGeom(shoreDist, oceanDist, groundDist, stationary, raw, breaking, coord.X, normalOffset, dir * FieldWaveSpeed);
    }

    /// <summary>GetAdvectedNoise: the noise's green at the two time layers, the coordinates carried back along the velocity.</summary>
    private static float FieldAdvected(WaterSheet noise, Vector2 uv, Vector2 velocity, float phase)
    {
        (Vector2 phases, Vector2 seeds, Vector2 weights) = LinearInterpNodes(phase);
        float said = 0f;
        for (var layer = 0; layer < 2; layer++)
        {
            Vector2 layerUv = uv - (velocity * (((layer == 0 ? phases.X : phases.Y) - 0.5f) * 2f));
            Vector3 hash = HashStable(layer == 0 ? seeds.X : seeds.Y);
            said += NoiseAt(noise, layerUv + new Vector2(hash.X, hash.Y)).Y * (layer == 0 ? weights.X : weights.Y);
        }

        return said;
    }

    /// <summary>GetAdvectedNoiseField4: the noise and its rise along u, v and the phase, a hundredth apart.</summary>
    private static Vector4 FieldAdvectedField(WaterSheet noise, Vector2 uv, Vector2 velocity, float phase)
    {
        float centre = FieldAdvected(noise, uv, velocity, phase);
        float alongX = FieldAdvected(noise, uv + new Vector2(1e-2f, 0f), velocity, phase);
        float alongY = FieldAdvected(noise, uv + new Vector2(0f, 1e-2f), velocity, phase);
        float alongT = FieldAdvected(noise, uv, velocity, phase + 1e-2f);
        return new Vector4(centre, (alongX - centre) / 1e-2f, (alongY - centre) / 1e-2f, (alongT - centre) / 1e-2f);
    }

    // field_ops: a value with its rise along x and y, as the engine carries its fields.
    private static Vector3 FieldMul(Vector3 a, Vector3 b) => new(a.X * b.X, (a.X * b.Y) + (b.X * a.Y), (a.X * b.Z) + (b.X * a.Z));

    private static Vector3 FieldInverse(Vector3 value)
    {
        float inv = 1f / value.X;
        return new Vector3(inv, -inv * inv * value.Y, -inv * inv * value.Z);
    }

    private static Vector3 FieldMax(Vector3 a, Vector3 b) => a.X > b.X ? a : b;

    private static Vector3 FieldOneMinus(Vector3 value) => new(1f - value.X, -value.Y, -value.Z);

    private static Vector3 FieldSqr(Vector3 value) => new(value.X * value.X, 2f * value.X * value.Y, 2f * value.X * value.Z);

    private static Vector3 FieldRampNormHalf(Vector3 value, Vector3 center)
    {
        Vector3 under = FieldMul(FieldSqr(2f * center), FieldOneMinus(value)) + value;
        return FieldMul(value, FieldInverse(FieldMax(under, new Vector3(1e-5f, 0f, 0f))));
    }

    private static Vector3 FieldRampNorm(Vector3 value, Vector3 center)
    {
        center.X = Math.Clamp(center.X, 1e-3f, 1f - 1e-3f);
        bool mirror = center.X > 0.5f;
        Vector3 mirroredValue = mirror ? FieldOneMinus(value) : value;
        Vector3 mirroredCenter = mirror ? FieldOneMinus(center) : center;
        Vector3 ramp = FieldRampNormHalf(mirroredValue, mirroredCenter);
        return mirror ? FieldOneMinus(ramp) : ramp;
    }

    /// <summary>
    /// GetGerstnerProfile's read, SamplerLinearWrapClampWrap: wrapping along the wave's coordinate, held along the break phase - its red
    /// and its blue, the four nearest texels' centres mixed.
    /// </summary>
    private static Vector2 FieldProfileAt(WaterSheet sheet, float u, float v)
    {
        int width = sheet.Width, height = sheet.Height, channels = sheet.Channels;
        float sx = (u * width) - 0.5f;
        float sy = Math.Clamp((v * height) - 0.5f, 0f, height - 1f);
        float fx0 = MathF.Floor(sx);
        if (!(MathF.Abs(fx0) <= 16777216f) || !(MathF.Abs(sy) <= float.MaxValue))
        {
            return Vector2.Zero;
        }

        float fx = sx - fx0;
        int x0 = ((((int)fx0) % width) + width) % width;
        int x1 = (x0 + 1) % width;
        var y0 = (int)sy;
        int y1 = Math.Min(y0 + 1, height - 1);
        float fy = sy - y0;
        float[] texels = sheet.Texels;
        int a = ((y0 * width) + x0) * channels, b = ((y0 * width) + x1) * channels;
        int c = ((y1 * width) + x0) * channels, d = ((y1 * width) + x1) * channels;
        var ta = new Vector2(texels[a], texels[a + 2]);
        var tb = new Vector2(texels[b], texels[b + 2]);
        var tc = new Vector2(texels[c], texels[c + 2]);
        var td = new Vector2(texels[d], texels[d + 2]);
        Vector2 top = ta + ((tb - ta) * fx);
        Vector2 bottom = tc + ((td - tc) * fx);
        return top + ((bottom - top) * fy);
    }

    /// <summary>GetGerstnerProfile's normal offset field: the height (blue) and its rise along the coordinate and the break phase, a hundredth apart.</summary>
    private static Vector3 FieldShape(WaterSheet profile, float coord, float breaking)
    {
        float height = FieldProfileAt(profile, coord, breaking).Y;
        float alongCoord = FieldProfileAt(profile, coord + 1e-2f, breaking).Y;
        float alongBreak = FieldProfileAt(profile, coord, breaking + 1e-2f).Y;
        return new Vector3(height, (alongCoord - height) / 1e-2f, (alongBreak - height) / 1e-2f);
    }

    /// <summary>GetWaveInterpNodes, eight nodes a wave, the curve two.</summary>
    private static void FieldNodes(float waveCoord, out Vector2 local, out Vector2 exterp, out Vector2 indices, out Vector2 weights, out float overlap)
    {
        const float nodes = 8f;
        float coord = MathF.Floor(waveCoord) + MathF.Pow(waveCoord - MathF.Floor(waveCoord), 2f);
        float global = coord * nodes;
        (_, Vector2 seeds, weights) = LinearInterpNodes(global);
        Vector2 shifted = (seeds + new Vector2(0.5f)) / nodes;
        Vector2 linearLocal = Floored((shifted - Floored(shifted)) * nodes) / nodes;
        local = new Vector2(MathF.Pow(linearLocal.X, 0.5f), MathF.Pow(linearLocal.Y, 0.5f));
        Vector2 linearExterp = linearLocal + ((new Vector2(global) - seeds) / nodes);
        Vector2 part = linearExterp - Floored(linearExterp);
        exterp = Floored(linearExterp) + new Vector2(MathF.Pow(part.X, 0.5f), MathF.Pow(part.Y, 0.5f));
        indices = Floored(seeds / nodes);
        float interval = MathF.Floor(((global / nodes) - MathF.Floor(global / nodes)) * nodes);
        overlap = 1f - Saturated(MathF.Abs(interval - (nodes - 1f)));
    }

    private static Vector2 Floored(Vector2 v) => new(MathF.Floor(v.X), MathF.Floor(v.Y));

    /// <summary>GetFieldFoamVelocity.</summary>
    private static float FieldFoamVelocity(float local, float breaking)
    {
        float velocity = 1.5f * MathF.Pow(Saturated(local / 0.95f), 10f);
        float over = Saturated(((local / 0.95f) - 1f) / (1f - 0.95f));
        velocity *= 0.8f + (0.2f * (1f - (over * over)));
        velocity *= PolyStep(breaking, 0.5f, 0.9f);
        return velocity;
    }

    /// <summary>SampleFlowingTexture over the noise: its red and green at two time layers.</summary>
    private static Vector2 FieldFlowing(WaterSheet noise, Vector2 uv, Vector2 strength, float clock, float period)
    {
        (Vector2 phases, Vector2 seeds, Vector2 weights) = LinearInterpNodes(clock * (1f / period));
        Vector2 said = Vector2.Zero;
        for (var node = 0; node < 2; node++)
        {
            Vector3 hash = Hash33(new Vector3(node == 0 ? seeds.X : seeds.Y, 0f, 0f));
            Vector2 nodeUv = uv + new Vector2(hash.X, hash.Y) - (strength * (period * (((node == 0 ? phases.X : phases.Y) * 2f) - 1f)));
            said += NoiseAt(noise, nodeUv) * (node == 0 ? weights.X : weights.Y);
        }

        return said;
    }

    /// <summary>The level a read at these gradients takes, as the card's sampler works it out: the longer of the two, in texels, as a power of two.</summary>
    private static float LevelOf(Mipmaps sheet, Vector2 gx, Vector2 gy)
    {
        float wide = sheet.Top.Width, high = sheet.Top.Height;
        float rho = MathF.Max(
            MathF.Sqrt((gx.X * wide * gx.X * wide) + (gx.Y * high * gx.Y * high)),
            MathF.Sqrt((gy.X * wide * gy.X * wide) + (gy.Y * high * gy.Y * high)));
        return rho > 1f ? MathF.Min(MathF.Log2(rho), sheet.Count - 1) : 0f;
    }

    /// <summary>
    /// GetFoamScrollingLayer with its turbulence - the beach's noise standing for noise_sampler - the sheets read at the level their turned
    /// gradients come to; the colour's sheet sRGB.
    /// </summary>
    private static Surface FieldScrolling(
        WaterSheet noise, Mipmaps colourSheet, Mipmaps normalSheet, Vector2 uv, Vector2 velocity, float turbUv, float turbVelocity,
        float turbPhase, float phase, float layerSeed, Vector2 duvx, Vector2 duvy, out float level)
    {
        (Vector2 phases, Vector2 seeds, Vector2 weights) = LinearInterpNodes(phase);
        Surface surface = default;
        level = 0f;
        for (var layer = 0; layer < 2; layer++)
        {
            Vector3 hash = Hash33(new Vector3(layer == 0 ? seeds.X : seeds.Y, layerSeed, 0f));
            float angle = 3.1415f * 2f * hash.Z;
            float s = MathF.Sin(angle), c = MathF.Cos(angle);
            float swing = ((layer == 0 ? phases.X : phases.Y) * 2f) - 1f;
            Vector2 flow = uv - (velocity * swing);
            Vector2 turbulence = NoiseAt(noise, (flow * turbUv) + new Vector2(hash.Z, hash.Y));
            Vector2 moved = flow + new Vector2(hash.X, hash.Y) - ((turbulence - new Vector2(0.5f)) * ((swing + turbPhase) * turbVelocity));

            // mul(v, float2x2(c, s, -s, c)), and the gradient back through its transpose.
            var turned = new Vector2((moved.X * c) - (moved.Y * s), (moved.X * s) + (moved.Y * c));
            var gx = new Vector2((duvx.X * c) - (duvx.Y * s), (duvx.X * s) + (duvx.Y * c));
            var gy = new Vector2((duvy.X * c) - (duvy.Y * s), (duvy.X * s) + (duvy.Y * c));
            level = LevelOf(colourSheet, gx, gy);
            Vector4 texel = MeshPicture.Sample4(colourSheet, turned, level);
            var colour = new Vector4(Linear(texel.X), Linear(texel.Y), Linear(texel.Z), texel.W);
            Vector4 bumps = MeshPicture.Sample4(normalSheet, turned, LevelOf(normalSheet, gx, gy));
            Vector2 back = RgToGradYFix(bumps.X, bumps.Y);
            var world = new Vector2((back.X * c) + (back.Y * s), (back.Y * c) - (back.X * s));
            float weight = layer == 0 ? weights.X : weights.Y;
            surface.Colour += colour * weight;
            surface.Normal += new Vector3(colour.W, world.X, world.Y) * weight;
            surface.Ao += bumps.Z * weight;
            surface.Rough += bumps.W * weight;
            surface.Subsurface += ((colour.W * 0.5f) + 0.5f) * weight;
            surface.SqrWeight += weight * weight;
        }

        return surface;
    }

    /// <summary>DissolveFoamSurface.</summary>
    private static Surface FieldDissolved(Surface surface, float intensity, float sharpness)
    {
        float fade = MathSmoothStep(intensity, 1f - surface.Normal.X, sharpness);
        surface.Colour.W = fade;
        surface.Normal = Vector3.Lerp(new Vector3(0.5f, 0f, 0f), surface.Normal, 1f - Saturated(1f - intensity));
        surface.Rough *= fade;
        float threshold = Lerp(1f, 0.1f, Saturated(surface.Subsurface));
        surface.Subsurface = MathSmoothStep(intensity, threshold, 0.8f);
        return surface;
    }

    /// <summary>GetFieldFoamSurface: two scrolling layers a node, blended where the nodes do not overlap and overlaid where they do.</summary>
    private static FieldFoamOut FieldFoam(
        WaterSheet noise, Mipmaps colourSheet, Mipmaps normalSheet, Vector2 place, Vector2 velocity, float foamCoord, float shoreDist,
        float breaking, float clock, Vector2 dpx, Vector2 dpy)
    {
        Vector2 following = FieldFlowing(noise, place * 0.004f, velocity * (0.004f * 1.5f), clock, 0.5f);
        const float worldToUv = 2e-3f / 2f, foamMult = 0.8f;
        float frontOffset = Lerp(0.09f * 0.75f, 0.09f * 1.1f, MathSmoothStep(breaking, 0.6f, 0.91f));
        float globalCoord = foamCoord - frontOffset;
        FieldNodes(globalCoord, out Vector2 local, out Vector2 exterp, out Vector2 indices, out Vector2 weights, out float overlap);
        float interp = globalCoord - MathF.Floor(globalCoord);
        float level = 0f;
        Surface Layer(int layer)
        {
            float layerLocal = Lerp(interp, layer == 0 ? exterp.X : exterp.Y, overlap);
            layerLocal *= 1f - frontOffset;
            layerLocal += frontOffset;
            Vector2 flow = velocity * FieldFoamVelocity(layerLocal, breaking);
            float nodeLocal = layer == 0 ? local.X : local.Y;
            float timeFreq = Lerp(0.75f, 5f, MathF.Pow(Saturated(nodeLocal), 3f));
            float spaceFreq = worldToUv * Lerp(0.75f, 2f, nodeLocal);
            float seed = (layer == 0 ? indices.X : indices.Y) + nodeLocal;
            Surface surface = FieldScrolling(
                noise, colourSheet, normalSheet, place * spaceFreq, flow * spaceFreq / timeFreq, 1.5f, 0.05f, 1f, clock * timeFreq, seed,
                dpx * spaceFreq, dpy * spaceFreq, out float read);
            level = MathF.Max(level, read);
            return surface;
        }

        Surface first = Layer(0), second = Layer(1);
        if (overlap < 0.5f)
        {
            // BlendSurfaces, then DissolveFoamSurface by the profile at the wave's own coordinate.
            Surface blended = Blend(first, second, weights.Y);
            FieldFoamProfile profile = FieldFoamProfileAt(interp, shoreDist, breaking);
            blended = FieldDissolved(blended, profile.Intensity, profile.Sharpness);
            return new FieldFoamOut(
                new Vector4(blended.Colour.X, blended.Colour.Y, blended.Colour.Z, Saturated(blended.Colour.W)), false, interp,
                new Vector2(profile.Intensity), level);
        }

        float sprayRamp = (PolyStep(following.X, 0.5f, 0.3f) - 0.5f) * 7f;
        sprayRamp = Lerp(3f, sprayRamp, RampPow(Saturated(breaking - 0.45f) * 2f, -5f));
        float fadeFirst = RampPow(weights.X, -20f), fadeSecond = RampPow(weights.Y, sprayRamp);
        FieldFoamProfile profileFirst = FieldFoamProfileAt(exterp.X, shoreDist, breaking);
        FieldFoamProfile profileSecond = FieldFoamProfileAt(exterp.Y, shoreDist, breaking);
        first = FieldDissolved(first, profileFirst.Intensity * fadeFirst, profileFirst.Sharpness);
        second = FieldDissolved(second, profileSecond.Intensity * fadeSecond, profileSecond.Sharpness);
        float darkening = Lerp(0.5f + (0.5f * weights.X), 1f, Saturated(breaking));
        float darker = Lerp(1f, darkening, foamMult);
        first.Colour = new Vector4(first.Colour.X * darker, first.Colour.Y * darker, first.Colour.Z * darker, first.Colour.W);
        second.Subsurface *= MathF.Pow(Saturated(weights.Y), 3f);

        // OverlaySurfaces.
        float a0 = first.Colour.W, a1 = second.Colour.W;
        float alpha = 1f - ((1f - a0) * (1f - a1));
        float inv = 1f / (alpha + 1e-5f);
        Vector3 rgb = Vector3.Lerp(
            new Vector3(first.Colour.X, first.Colour.Y, first.Colour.Z) * a0, new Vector3(second.Colour.X, second.Colour.Y, second.Colour.Z), a1) * inv;
        return new FieldFoamOut(
            new Vector4(rgb, Saturated(alpha)), true, interp,
            new Vector2(profileFirst.Intensity * fadeFirst, profileSecond.Intensity * fadeSecond), level);
    }
}
