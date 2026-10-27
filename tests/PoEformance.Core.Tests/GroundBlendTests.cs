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

        // THE PROBE SAYS WHAT THE MIX DID THERE: the mask's green and each ground's colour - the blue second whole.
        Assert.True(split.MixesGrounds);
        Assert.False(stripes.MixesGrounds);
        var probe = new PixelProbe(right / 4 % Size, right / 4 / Size);
        _ = MeshPicture.Of(quad, new MeshPicture.Canvas(Size) { Probe = probe }, tilt: Down, blends: [MaterialBlend.Opaque], shades: [split]);
        ProbeGround ground = Assert.Single(probe.Grounds);
        Assert.Equal(1f, ground.Green, 3);
        Assert.Equal(new Vector3(0f, 0f, 1f), ground.Second);
        Assert.True(ground.First.X > 0.5f);
        Assert.Contains("the mask's green 1.000, the second ground's share", PictureProbe.Grounded(ground), StringComparison.Ordinal);
    }

    /// <summary>
    /// Mixed by the corners' share the mesh carries in its coordinates' first channel, rather than a mask: a share of nought all over is the
    /// first ground to the byte, one the second, and a share rising across the quad each ground on its own side - the program reading
    /// no mask, and the probe saying it was the corners' share.
    /// </summary>
    [Fact]
    public void TWOGroundsAreMixedPerPixelByTheCornersShareInTheCoordinates()
    {
        ShadeProgram stripes = Compiled(Plain("Art/stripes.dds")).With([Stripes()]);
        ShadeProgram blue = Compiled(Constant(0f, 0f, 1f));
        byte[] first = Drawn(Quad(world: true), stripes);
        byte[] second = Drawn(Quad(world: true), blue);

        ShadeProgram? cornered = ShadeProgram.GroundsByCorners(stripes, blue, Tiling);
        Assert.NotNull(cornered);
        Assert.True(cornered.MixesGrounds);
        Assert.True(cornered.MixesByCorners);
        Assert.False(ShadeProgram.Grounds(stripes, blue, new ShadeTexture("Art/mask.dds", false), Mask(0, 0), Tiling)!.MixesByCorners);
        Assert.Equal([new ShadeTexture("Art/stripes.dds", true)], cornered.Textures);
        Assert.Equal(first, Drawn(Shared(_ => 0f), cornered));
        Assert.Equal(second, Drawn(Shared(_ => 1f), cornered));

        // A SHARE RISING ACROSS, nought at the left edge and one at the right: the left nearer the first, the right nearer the second,
        // the middle between - blue being the channel the two grounds differ most in.
        SkinnedMesh rising = Shared(place => (place.X + 400f) / 800f);
        byte[] both = Drawn(rising, cornered);
        int left = PixelAt(rising, new Vector3(-390f, 0f, 0f)), right = PixelAt(rising, new Vector3(390f, 0f, 0f)), middle = PixelAt(rising, Vector3.Zero);
        Assert.True(Math.Abs(both[left + 2] - first[left + 2]) < Math.Abs(both[left + 2] - second[left + 2]));
        Assert.True(Math.Abs(both[right + 2] - second[right + 2]) < Math.Abs(both[right + 2] - first[right + 2]));
        Assert.InRange(both[middle + 2], (byte)(first[middle + 2] + 1), (byte)(second[middle + 2] - 1));

        var probe = new PixelProbe(right / 4 % Size, right / 4 / Size);
        _ = MeshPicture.Of(rising, new MeshPicture.Canvas(Size) { Probe = probe }, tilt: Down, blends: [MaterialBlend.Opaque], shades: [cornered]);
        ProbeGround ground = Assert.Single(probe.Grounds);
        Assert.True(ground.Cornered);
        Assert.InRange(ground.Green, 0.95f, 1f);
        Assert.Contains("the sub-tile's corners' share, bilinear (CHOSEN for a shape across the mask's atlas)", PictureProbe.Grounded(ground), StringComparison.Ordinal);
    }

    /// <summary>
    /// A graph that writes a height compiles a twin keeping it, and one carrying EnableGroundHeightBlending says so; a graph writing no
    /// height has no twin - so a ground drawn alone reads nothing more than before.
    /// </summary>
    [Fact]
    public void AGRAPHWritingAHeightCompilesATwinKeepingIt()
    {
        ShadeProgram raised = Compiled(Raised(0.2f, 0.3f, 0.4f, 0.5f, blends: true));
        Assert.True(raised.BlendsGroundHeights);
        Assert.NotNull(raised.Heighted);
        Assert.True(raised.Heighted.BlendsGroundHeights);

        ShadeProgram flat = Compiled(Constant(0f, 0f, 1f));
        Assert.Null(flat.Heighted);
        Assert.False(flat.BlendsGroundHeights);
        Assert.False(Compiled(Raised(0.2f, 0.3f, 0.4f, 0.5f, blends: false)).BlendsGroundHeights);
    }

    /// <summary>
    /// Weighed by their heights as GroundInterpolate weighs them: at a share of a half, a first ground half a unit higher than the second
    /// takes 0.5 / (0.5 + 0.5 / 51) of the pixel - each weight times 1 / (1 + (highest - height) / 0.01), then divided by their sum - and
    /// the probe says the heights and the share they came to. Off, or where neither graph turns it on, the two mix by the share alone.
    /// </summary>
    [Fact]
    public void TWOGroundsAreWeighedByTheirHeightsAsTheEngineWeighsThem()
    {
        ShadeProgram high = Compiled(Raised(1f, 0f, 0f, 0.5f, blends: true)).Heighted!;
        ShadeProgram low = Compiled(Constant(0f, 0f, 1f));
        SkinnedMesh half = Shared(_ => 0.5f);

        ShadeProgram weighed = ShadeProgram.GroundsByCorners(high, low, Tiling, heights: true)!;
        Assert.True(weighed.MixesByHeights);
        float expected = (0.5f / (1f + (0.5f / ShadeProgram.HeightThreshold))) / (0.5f + (0.5f / (1f + (0.5f / ShadeProgram.HeightThreshold))));
        int middle = PixelAt(half, Vector3.Zero);
        var probe = new PixelProbe(middle / 4 % Size, middle / 4 / Size);
        _ = MeshPicture.Of(half, new MeshPicture.Canvas(Size) { Probe = probe }, tilt: Down, blends: [MaterialBlend.Opaque], shades: [weighed]);
        ProbeGround ground = Assert.Single(probe.Grounds);
        Assert.True(ground.ByHeights);
        Assert.Equal(0.5f, ground.Green, 5);
        Assert.Equal(0.5f, ground.FirstHeight, 5);
        Assert.Equal(0f, ground.SecondHeight, 5);
        Assert.Equal(expected, ground.Share, 5);
        Assert.Contains("weighed by their heights 0.500 and 0.000 as the engine's height blending weighs them, the second ground's share then 0.019", PictureProbe.Grounded(ground), StringComparison.Ordinal);

        // THE RED THE FIRST GROUND'S AND THE BLUE THE SECOND'S, at their weights.
        byte[] drawn = Drawn(half, weighed);
        byte[] plain = Drawn(half, ShadeProgram.GroundsByCorners(high, low, Tiling)!);
        Assert.True(drawn[middle] > plain[middle] && drawn[middle + 2] < plain[middle + 2]);

        // OFF, OR TURNED ON BY NEITHER: the share alone.
        Assert.False(ShadeProgram.GroundsByCorners(high, low, Tiling)!.MixesByHeights);
        ShadeProgram unturned = Compiled(Raised(1f, 0f, 0f, 0.5f, blends: false)).Heighted!;
        Assert.False(ShadeProgram.GroundsByCorners(unturned, low, Tiling, heights: true)!.MixesByHeights);
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
        string? name = paints.Grounds(Read, "Art/Town.mat", "Art/blackc.mat", "Art/masks/newmask.dds", GroundPainter.GroundScale, heights: false, out string why);
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
        Assert.Null(paints.Grounds(Read, "Art/Town.mat", "Art/Nothing.mat", "Art/masks/newmask.dds", GroundPainter.GroundScale, heights: false, out string not));
        Assert.Contains("Art/Nothing.mat", not, StringComparison.Ordinal);

        // BY THE CORNERS, under a name of their own that says no mask, its program reading none.
        string? byCorners = paints.GroundsByCorners(Read, "Art/Town.mat", "Art/blackc.mat", GroundPainter.GroundScale, heights: false, out string cornersWhy);
        Assert.NotNull(byCorners);
        Assert.Empty(cornersWhy);
        Assert.NotEqual(name, byCorners);
        Assert.True(GroundBlend.Parts(byCorners, out string cornerFirst, out string cornerSecond, out string noMask));
        Assert.Equal(("Art/Town.mat", "Art/blackc.mat", string.Empty), (cornerFirst, cornerSecond, noMask));
        Assert.Equal("Town.mat + blackc.mat by the sub-tiles' corners", GroundBlend.Short(byCorners));
        MonsterModels.Shade cornerShade = paints.Shade(Read, byCorners);
        Assert.NotNull(cornerShade.Program);
        Assert.True(cornerShade.Program.MixesByCorners);
        Assert.Empty(cornerShade.Program.Textures);

        // WEIGHED BY THEIR HEIGHTS, under a name of its own that says so.
        string? heighted = paints.Grounds(Read, "Art/Town.mat", "Art/blackc.mat", "Art/masks/newmask.dds", GroundPainter.GroundScale, heights: true, out _);
        Assert.NotNull(heighted);
        Assert.NotEqual(name, heighted);
        Assert.True(GroundBlend.Heighted(heighted));
        Assert.False(GroundBlend.Heighted(name));
        Assert.True(GroundBlend.Parts(heighted, out _, out _, out string heightedMask));
        Assert.Equal("Art/masks/newmask.dds", heightedMask);
        Assert.Equal("Town.mat + blackc.mat by newmask.dds's green, weighed by their heights", GroundBlend.Short(heighted));

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

    /// <summary>
    /// The quad of <see cref="Quad"/> carrying a share of the second ground in its coordinates' first channel, as the corners' mix has it -
    /// the second channel, which the program does not read, a half, so a share of nought all over is still a mesh with coordinates.
    /// </summary>
    private static SkinnedMesh Shared(Func<Vector3, float> share)
    {
        SkinnedMesh quad = Quad(world: false);
        Vector2[] shares = [.. quad.Positions.Select(place => new Vector2(share(place), 0.5f))];
        return SkinnedMesh.Of(quad.Positions, quad.Normals, quad.Indices, quad.Least, quad.Most, shares, quad.Shapes);
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

    /// <summary>A constant colour and a constant height, with or without the fragment that turns the engine's height blending on.</summary>
    private static string Raised(float red, float green, float blue, float height, bool blends) =>
        string.Create(System.Globalization.CultureInfo.InvariantCulture,
            $$$"""
            {"nodes":[
              {"type":"ConstantPixel3","index":0,"parameters":[{"value":[{{{red}}},{{{green}}},{{{blue}}}]}]},
              {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"},
              {"type":"ConstantFloat","index":0,"parameters":[{"value":{{{height}}}}]},
              {"type":"Height","index":0,"stage":"Texturing_Init"}{{{(blends ? ",\n  {\"type\":\"EnableGroundHeightBlending\",\"index\":0}" : string.Empty)}}}],
             "links":[
              {"src":{"type":"ConstantPixel3","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}},
              {"src":{"type":"ConstantFloat","index":0,"variable":"output"},"dst":{"type":"Height","index":0,"stage":"Texturing_Init","variable":"input"}}]}
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
