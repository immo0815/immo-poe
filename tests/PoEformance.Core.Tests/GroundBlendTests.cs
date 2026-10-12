using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// A tile of two grounds mixed per pixel by its mask's green, as the engine's GroundGenerateWeights_1a1b mixes them - the mesh carrying
/// the block's own coordinates for the mask, the program working the materials' out of the place.
/// </summary>
public sealed class GroundBlendTests
{
    private const int Size = 64;

    /// <summary>A tilt that looks straight down - see MeshPicture.Camera.</summary>
    private const float Down = MathF.PI / 2f;

    /// <summary>The materials' tiling here: the quad's 800 units across are half a texture.</summary>
    private const float Tiling = 1f / 1600f;

    /// <summary>
    /// Where the mask's green is nought the mixed ground is the first ground to the byte - drawn as it is alone over the place's
    /// coordinates - and where it is one the second; across a mask of both, each side is its own ground and the middle both.
    /// </summary>
    [Fact]
    public void TWOGroundsAreMixedPerPixelByTheMasksGreen()
    {
        ShadeProgram stripes = Compiled(Plain("Art/stripes.dds")).With([Stripes()]);
        ShadeProgram blue = Compiled(Constant(0f, 0f, 1f));
        var mask = new ShadeTexture("Art/mask.dds", false);

        // ALONE: the first ground over the place's coordinates, the second as it is.
        byte[] first = Drawn(Quad(world: true), stripes);
        byte[] second = Drawn(Quad(world: true), blue);

        ShadeProgram? noughts = ShadeProgram.Grounds(stripes, blue, mask, Mask(0, 0, 0, 0, 0, 0, 0, 0), Tiling);
        ShadeProgram? ones = ShadeProgram.Grounds(stripes, blue, mask, Mask(255, 255, 255, 255, 255, 255, 255, 255), Tiling);
        Assert.NotNull(noughts);
        Assert.NotNull(ones);
        Assert.False(noughts.HasAlpha);
        Assert.Equal([new ShadeTexture("Art/stripes.dds", true), mask], noughts.Textures);
        Assert.Equal(first, Drawn(Quad(world: false), noughts));
        Assert.Equal(second, Drawn(Quad(world: false), ones));

        // A MASK OF BOTH, nought on the block's left half and one on its right: away from the halves' meeting - and from the
        // texture's wrap, where the last texel meets the first - each side is its own ground, and the middle both.
        ShadeProgram split = ShadeProgram.Grounds(stripes, blue, mask, Mask(0, 0, 0, 0, 255, 255, 255, 255), Tiling)!;
        byte[] both = Drawn(Quad(world: false), split);
        SkinnedMesh quad = Quad(world: false);
        int left = PixelAt(quad, new Vector3(-200f, 0f, 0f)), right = PixelAt(quad, new Vector3(200f, 0f, 0f)), middle = PixelAt(quad, Vector3.Zero);
        Assert.Equal(first.AsSpan(left, 4).ToArray(), both.AsSpan(left, 4).ToArray());
        Assert.Equal(second.AsSpan(right, 4).ToArray(), both.AsSpan(right, 4).ToArray());
        Assert.InRange(both[middle + 2], (byte)(Math.Min(first[middle + 2], second[middle + 2]) + 1), (byte)(Math.Max(first[middle + 2], second[middle + 2]) - 1));
    }

    /// <summary>The paint cache mixes two materials under a name of their own - opaque, its program the mixed one - and says why where it cannot.</summary>
    [Fact]
    public void THEPAINTCacheMixesTwoGroundsUnderANameOfTheirOwn()
    {
        var files = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Art/Town.mat"] = Encoding.UTF8.GetBytes("""{"version":4,"graphinstances":[{"parent":"Metadata/Red.fxgraph"}]}"""),
            ["Art/blackc.mat"] = Encoding.UTF8.GetBytes("""{"version":4,"graphinstances":[{"parent":"Metadata/Black.fxgraph"}]}"""),
            ["Metadata/Red.fxgraph"] = Encoding.UTF8.GetBytes(Constant(1f, 0f, 0f)),
            ["Metadata/Black.fxgraph"] = Encoding.UTF8.GetBytes(Constant(0f, 0f, 0f)),
            ["Art/masks/newmask.dds"] = TwoChannelDds(4, 1, [0, 0, 0, 0, 0, 255, 0, 255]),
        };
        byte[]? Read(string path) => files.GetValueOrDefault(path);

        var paints = new MonsterModels.Paints();
        string? name = paints.Grounds(Read, "Art/Town.mat", "Art/blackc.mat", "Art/masks/newmask.dds", GroundPainter.GroundScale, out string why);
        Assert.NotNull(name);
        Assert.Empty(why);
        Assert.True(GroundBlend.Parts(name, out string first, out string second, out string mask));
        Assert.Equal(("Art/Town.mat", "Art/blackc.mat", "Art/masks/newmask.dds"), (first, second, mask));
        Assert.Equal("Town.mat + blackc.mat by newmask.dds's green", GroundBlend.Short(name));
        Assert.Equal((string.Empty, string.Empty), paints.Modes(Read, name));

        MonsterModels.Shade shade = paints.Shade(Read, name);
        Assert.NotNull(shade.Program);
        Assert.True(shade.Program.Bound);
        Assert.Equal([new ShadeTexture("Art/masks/newmask.dds", false)], shade.Program.Textures);

        // A GROUND WHOSE GRAPHS DO NOT COMPILE IS NOT MIXED, and the line says which.
        Assert.Null(paints.Grounds(Read, "Art/Town.mat", "Art/Nothing.mat", "Art/masks/newmask.dds", GroundPainter.GroundScale, out string not));
        Assert.Contains("Art/Nothing.mat", not, StringComparison.Ordinal);

        // AND A MATERIAL'S OWN PATH IS NO MIX.
        Assert.False(GroundBlend.Parts("Art/Town.mat", out _, out _, out _));
        Assert.Equal("Town.mat", GroundBlend.Short("Art/Town.mat"));
    }

    /// <summary>
    /// A quad 800 units across at height nought, its coordinates the place times <see cref="Tiling"/> - the old way, the materials' own -
    /// or the block's own: nought to one across x, a half down y.
    /// </summary>
    private static SkinnedMesh Quad(bool world)
    {
        Vector3[] places = [new(-400f, -400f, 0f), new(400f, -400f, 0f), new(400f, 400f, 0f), new(-400f, 400f, 0f)];
        var normals = new Vector3[places.Length];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        Vector2[] spots = world
            ? [.. places.Select(one => new Vector2(one.X, one.Y) * Tiling)]
            : [.. places.Select(one => new Vector2((one.X + 400f) / 800f, 0.5f))];
        return SkinnedMesh.Of(places, normals, [0, 1, 2, 0, 2, 3], new Vector3(-400f, -400f, 0f), new Vector3(400f, 400f, 0f), spots, [new MeshShape("Ground", 0, 6)]);
    }

    private static byte[] Drawn(SkinnedMesh mesh, ShadeProgram program)
        => [.. MeshPicture.Of(mesh, new MeshPicture.Canvas(Size), tilt: Down, blends: [MaterialBlend.Opaque], shades: [program]).Rgba];

    /// <summary>Where a point lands in the picture, as an offset into its bytes.</summary>
    private static int PixelAt(SkinnedMesh mesh, Vector3 point)
    {
        Vector3 placed = MeshPicture.Camera.Of(mesh, 0f, Down, 1f, Vector2.Zero).Place(point);
        int x = Math.Clamp((int)(placed.X * Size), 0, Size - 1), y = Math.Clamp((int)(placed.Y * Size), 0, Size - 1);
        return ((y * Size) + x) * 4;
    }

    private static ShadeProgram Compiled(string json)
    {
        ShadeCompile compiled = ShadeProgram.Compile([(new ShaderInstance("Metadata/Test.fxgraph", new Dictionary<string, ShaderValue[]>()), ShaderGraph.Read(Encoding.UTF8.GetBytes(json)))]);
        Assert.NotNull(compiled.Program);
        return compiled.Program;
    }

    /// <summary>One texture read at the mesh's coordinates into the colour.</summary>
    private static string Plain(string texture) =>
        $$$"""
        {"nodes":[
          {"type":"InputTexture","index":0,"parameters":[{"format":"DXT1","srgb":true,"path":"{{{texture}}}"}],"custom_parameter":"AlbedoTransparency_TEX"},
          {"type":"InputUV","index":0,"stage":"Texturing_Init"},
          {"type":"SampleInputTexture","index":0,"parameters":[{"value":"SamplerDynamicWrap"}]},
          {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
         "links":[
          {"src":{"type":"InputTexture","index":0,"variable":"out_texture"},"dst":{"type":"SampleInputTexture","index":0,"variable":"in_texture"}},
          {"src":{"type":"InputUV","index":0,"stage":"Texturing_Init","variable":"output"},"dst":{"type":"SampleInputTexture","index":0,"variable":"uv"}},
          {"src":{"type":"SampleInputTexture","index":0,"variable":"color","swizzle":"xyz"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}}]}
        """;

    /// <summary>A constant colour, in linear light, straight into the colour.</summary>
    private static string Constant(float red, float green, float blue) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $$$"""
            {"nodes":[
              {"type":"ConstantPixel3","index":0,"parameters":[{"value":[{{{red}}},{{{green}}},{{{blue}}}]}]},
              {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
             "links":[
              {"src":{"type":"ConstantPixel3","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}}]}
            """);

    /// <summary>Red and yellow stripes, eight texels across - something the coordinates show in.</summary>
    private static Mipmaps Stripes()
    {
        var pixels = new byte[8 * 8 * 4];
        for (var one = 0; one < 8 * 8; one++)
        {
            pixels[one * 4] = 230;
            pixels[(one * 4) + 1] = (one & 1) == 0 ? (byte)40 : (byte)210;
            pixels[(one * 4) + 2] = 20;
            pixels[(one * 4) + 3] = 255;
        }

        return Mipmaps.Of(new GamePicture(8, 8, pixels))!;
    }

    /// <summary>A mask of one row, its greens as given.</summary>
    private static Mipmaps Mask(params byte[] greens)
    {
        var pixels = new byte[greens.Length * 4];
        for (var one = 0; one < greens.Length; one++)
        {
            pixels[(one * 4) + 1] = greens[one];
            pixels[(one * 4) + 3] = 255;
        }

        return Mipmaps.Of(new GamePicture(greens.Length, 1, pixels))!;
    }

    /// <summary>A DDS of DXGI's R8G8_UNORM (49), as the ground's masks are, behind a DX10 header.</summary>
    private static byte[] TwoChannelDds(int width, int height, byte[] texels)
    {
        var dds = new byte[128 + 20 + texels.Length];
        "DDS "u8.CopyTo(dds);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(4), 124);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(8), 0x1007);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(12), height);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(16), width);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(28), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(76), 32);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(80), 0x4);
        "DX10"u8.CopyTo(dds.AsSpan(84));
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(128), 49);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(132), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(140), 1);
        texels.CopyTo(dds.AsSpan(148));
        return dds;
    }
}
