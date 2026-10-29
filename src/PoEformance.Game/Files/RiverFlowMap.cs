using System.Numerics;

namespace PoEformance.Game.Files;

/// <summary>
/// The river's flow map as the engine binds it to <c>flowmap_tex_sampler</c>: four floats a texel - the flow in world units a second in
/// x and y, the sources and sinks in z, the distance to the bank in w, positive in the water - over a box of whole texels.
/// </summary>
/// <remarks>
/// MEASURED (the schema's RiverFlowField; The Red Vale in four instances, v0.1.351 to v0.1.356): rows of <see cref="Width"/> along x from
/// the box's least y, 54.3478 units a texel (1250 / 23, five cells), and the box beginning three tenths of a texel below the area's
/// origin both ways - (-16.30435, -16.30435) to the far corner a whole number of texels on, in all four instances and in Crimson Shores'
/// fog map alike (WaterPassBlock's boxes). So the box is the map's own size, read nowhere else.
/// </remarks>
public sealed class RiverFlowMap
{
    /// <summary>A texel's side in world units - MEASURED, the schema's WaterPassBlock.</summary>
    public const float Texel = 1250f / 23f;

    /// <summary>Where the box begins, both ways, in world units: three tenths of a texel below the origin - MEASURED in every box read.</summary>
    public const float Least = -0.3f * Texel;

    /// <summary>A map of <paramref name="width"/> by <paramref name="height"/> texels, four floats each, row by row.</summary>
    public RiverFlowMap(int width, int height, float[] texels, string said)
    {
        ArgumentNullException.ThrowIfNull(texels);
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (texels.Length != width * height * 4)
        {
            throw new ArgumentException($"a {width} x {height} map is {width * height * 4} floats, not {texels.Length}", nameof(texels));
        }

        Width = width;
        Height = height;
        Texels = texels;
        Said = said ?? string.Empty;
    }

    /// <summary>Texels across.</summary>
    public int Width { get; }

    /// <summary>Texels down.</summary>
    public int Height { get; }

    /// <summary>The texels, row by row, four floats each.</summary>
    public float[] Texels { get; }

    /// <summary>Where the map was read from and what it held, for the lines under a picture.</summary>
    public string Said { get; }

    /// <summary>The box's size in world units.</summary>
    public Vector2 Size => new(Width * Texel, Height * Texel);

    /// <summary>
    /// The map read as SamplerLinearWrap reads it at level nought, at a place given as a fraction of the box: the four nearest texels'
    /// centres mixed by hand, wrapping - the card's ShadeRiverFlow the same arithmetic.
    /// </summary>
    public Vector4 At(Vector2 uv)
    {
        float sx = (uv.X * Width) - 0.5f, sy = (uv.Y * Height) - 0.5f;
        float lx = MathF.Floor(sx), ly = MathF.Floor(sy);
        float fx = sx - lx, fy = sy - ly;
        int x0 = (((int)lx % Width) + Width) % Width, y0 = (((int)ly % Height) + Height) % Height;
        int x1 = (x0 + 1) % Width, y1 = (y0 + 1) % Height;
        Vector4 a = Texel4(x0, y0), b = Texel4(x1, y0), c = Texel4(x0, y1), d = Texel4(x1, y1);
        Vector4 top = a + ((b - a) * fx);
        Vector4 bottom = c + ((d - c) * fx);
        return top + ((bottom - top) * fy);
    }

    /// <summary>One texel's four floats.</summary>
    public Vector4 Texel4(int x, int y)
    {
        int at = ((y * Width) + x) * 4;
        return new Vector4(Texels[at], Texels[at + 1], Texels[at + 2], Texels[at + 3]);
    }
}

/// <summary>
/// A drawing's river flow: the area's map and where the picture's origin lies in the area - a laid room's corner (MonsterModel.AreaOrigin).
/// </summary>
/// <param name="Map">The area's flow map.</param>
/// <param name="Origin">Where the picture's origin is in the area, in world units.</param>
public sealed record RiverFlow(RiverFlowMap Map, Vector2 Origin)
{
    /// <summary>
    /// The map's box in the picture's frame: its least x and y, and its size - what the card reads (ModelShaders' RiverBox), the size nought
    /// where no map is bound.
    /// </summary>
    public Vector4 Register => new(RiverFlowMap.Least - Origin.X, RiverFlowMap.Least - Origin.Y, Map.Size.X, Map.Size.Y);

    /// <summary>
    /// GetFlowSample at a place of the picture: <c>(place - box.xy) / box.zw</c> read as SamplerLinearWrap reads - the card's arithmetic, in
    /// its order.
    /// </summary>
    public Vector4 Sample(Vector2 place)
    {
        Vector4 box = Register;
        return Map.At(new Vector2((place.X - box.X) / box.Z, (place.Y - box.Y) / box.W));
    }
}
