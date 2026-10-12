using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// The water's reflection of the picture itself: the engine's CastScreenRay, marched over the solid part of the picture - its numbers, and its march on the processor.
/// </summary>
/// <remarks>
/// THE ENGINE'S OWN MARCH (water.ffx, read whole in v0.1.217's water.txt): ApplyWaterSurfaceLighting under
/// WATER_SSR casts the view turned about the waves' normal across the screen - twenty steps, each a fiftieth
/// of the screen, the first offset by a per-pixel dither - and takes the first place where the depth buffer
/// lies in front of the ray by less than fifty units (a view thickness). The colour there is the opaque
/// framebuffer's - the picture before the water - and its share is <c>1 - pow((step + 0.5) / 20, 5)</c>, the
/// rest the environment's: a near hit is nearly all the scene, a far one fades into the cube, and no hit is
/// the cube alone. That the environment light takes the reflected colour's alpha as the cube's share is the
/// reading of <c>reflected_color.a</c> (its default is one, for no reflection); ComputeEnvironmentLighting, which
/// uses it, is in a source the capture does not yet carry - water.txt asks for it from v0.1.224.
///
/// THE DITHER IS A READING TOO: GetBayerNoise16 is named, not read, so it stands as the 4 by 4 Bayer matrix
/// its name says, by the pixel's place.
///
/// ON A PARALLEL VIEW the march is a straight line in the picture: pixels and depth move by fixed amounts
/// per unit along the ray, so a step is a sum, not a projection.
/// </remarks>
public static class ScreenReflection
{
    /// <summary>How many steps the ray takes before it gives up.</summary>
    public const int Steps = 20;

    /// <summary>How far one step goes, as a share of the picture's side.</summary>
    public const float StepShare = 0.02f;

    /// <summary>How far behind the ray a surface may lie and still be the one it hit, in world units.</summary>
    public const float Thickness = 50f;

    /// <summary>The 4 by 4 Bayer matrix, row by row - GetBayerNoise16's dither.</summary>
    public static readonly int[] Bayer = [0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5];

    /// <summary>The dither at a pixel, nought to fifteen sixteenths.</summary>
    public static float Dither(int x, int y) => Bayer[((y & 3) * 4) + (x & 3)] / 16f;

    /// <summary>The environment's share of the reflection where the ray hit at a step - <c>pow((step + 0.5) / 20, 5)</c>, one for no hit.</summary>
    public static float EnvironmentShare(int step)
    {
        if (step < 0)
        {
            return 1f;
        }

        float far = (step + 0.5f) / Steps;
        return far * far * far * far * far;
    }

    /// <summary>
    /// The ray marched over a picture's solid depth: where it first meets a surface, and at which step.
    /// </summary>
    /// <param name="x">The pixel's column.</param>
    /// <param name="y">The pixel's row.</param>
    /// <param name="away">The pixel's own depth along the view.</param>
    /// <param name="across">How far the ray moves in the picture per world unit along it, in pixels.</param>
    /// <param name="deeper">How far its depth grows per world unit along it.</param>
    /// <param name="size">The picture's side, in pixels.</param>
    /// <param name="depth">The solid depth, row by row; float.MaxValue where nothing is.</param>
    /// <param name="hitX">Where it hit, its column.</param>
    /// <param name="hitY">Where it hit, its row.</param>
    /// <returns>The step it hit at, or below nought for none.</returns>
    public static int March(int x, int y, float away, Vector2 across, float deeper, int size, ReadOnlySpan<float> depth, out int hitX, out int hitY)
    {
        hitX = -1;
        hitY = -1;
        float length = across.Length();
        if (!(length > 1e-6f))
        {
            return -1;
        }

        // A STEP IS A FIFTIETH OF THE PICTURE, in world units along the ray.
        float unit = StepShare * size / length;
        float jitter = Dither(x, y);
        for (var step = 0; step < Steps; step++)
        {
            float along = (step + jitter) * unit;
            float px = x + 0.5f + (across.X * along);
            float py = y + 0.5f + (across.Y * along);
            if (!(px >= 0f && py >= 0f && px < size && py < size))
            {
                break;
            }

            int cx = (int)px, cy = (int)py;
            float ray = away + (deeper * along);
            float solid = depth[(cy * size) + cx];
            if (solid < ray && solid > ray - Thickness)
            {
                hitX = cx;
                hitY = cy;
                return step;
            }
        }

        return -1;
    }
}
