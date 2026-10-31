using System.Globalization;
using System.Numerics;
using System.Text;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// Transform by the TBN basis for any vector (ShadeProgram's Tbn step, ThroughBasis): the triangle's frame made orthonormal against the
/// pixel's normal, the binormal against ∂p/∂v - and what it opened, Port's quay stone under its master's swap (MPRT_GraveBrickTile01c).
/// </summary>
public class TbnBasisTests
{
    /// <summary>The frame of <see cref="Tilted"/>: ∂p/∂u and ∂p/∂v, not at right angles.</summary>
    private static readonly Vector3 AlongU = new(20f, 0f, 0f);

    private static readonly Vector3 AlongV = new(10f, 12f, 16f);

    /// <summary>Every vertex's normal on <see cref="Tilted"/> - not the face's, which is (0, -0.8, 0.6).</summary>
    private static readonly Vector3 Leaning = new(0.6f, -0.8f, 0f);

    /// <summary>
    /// The rows worked out by hand: ∂p/∂u (20, 0, 0) less its part along the normal (0.6, -0.8, 0) is (12.8, 9.6, 0), the tangent
    /// (0.8, 0.6, 0); ∂p/∂v (10, 12, 16) less its parts along the normal and that tangent is (0, 0, 16), the binormal turned against it
    /// (0, 0, -1); and the normal itself. A vector of length one comes out of length one.
    /// </summary>
    [Fact]
    public void THEBASISRowsAreTheFrameMadeOrthonormalAgainstTheNormal()
    {
        AssertNear(new Vector3(0.8f, 0.6f, 0f), ShadeProgram.ThroughBasis(Vector3.UnitX, AlongU, AlongV, Leaning));
        AssertNear(new Vector3(0f, 0f, -1f), ShadeProgram.ThroughBasis(Vector3.UnitY, AlongU, AlongV, Leaning));
        AssertNear(Leaning, ShadeProgram.ThroughBasis(Vector3.UnitZ, AlongU, AlongV, Leaning));

        // 0.36 T + 0.48 B + 0.8 N.
        Vector3 turned = ShadeProgram.ThroughBasis(new Vector3(0.36f, 0.48f, 0.8f), AlongU, AlongV, Leaning);
        AssertNear(new Vector3(0.768f, -0.424f, -0.48f), turned);
        AssertNear(1f, turned.Length());
    }

    /// <summary>
    /// Where the coordinates do not span the triangle the frame is nought and what stays is the vector's part along the normal - never not
    /// a number; where only ∂p/∂u is nought the binormal still comes from ∂p/∂v alone.
    /// </summary>
    [Fact]
    public void ANOUGHTFrameLeavesThePartAlongTheNormal()
    {
        Vector3 along = ShadeProgram.ThroughBasis(new Vector3(0.6f, 0.8f, 0.5f), Vector3.Zero, Vector3.Zero, Leaning);
        Assert.Equal(Leaning * 0.5f, along);

        var up = new Vector3(0f, 0f, -1f);
        AssertNear(new Vector3(0f, -1f, 0f), ShadeProgram.ThroughBasis(new Vector3(1f, 1f, 0f), Vector3.Zero, new Vector3(0f, 20f, 0f), up));
    }

    /// <summary>
    /// Drawn on the processor, the frame is the triangle's own (its corners and coordinates, ShadeProgram.Spanned) under each pixel's
    /// normal: each row the hand-worked one, and a mix of rows, carried into the colour as <c>0.4 v + 0.5</c> - so the binormal's sign,
    /// the tangent's part along the normal and the normal's row are each a colour of their own.
    /// </summary>
    [Theory]
    [InlineData(1f, 0f, 0f, 0.82f, 0.74f, 0.5f)]
    [InlineData(0f, 1f, 0f, 0.5f, 0.5f, 0.1f)]
    [InlineData(0.6f, 0f, 0.8f, 0.884f, 0.388f, 0.5f)]
    [InlineData(0f, 0.6f, 0.8f, 0.692f, 0.244f, 0.26f)]
    public void THETRANSFORMIsDrawnWithTheTrianglesOwnFrame(float x, float y, float z, float red, float green, float blue)
    {
        ShadeProgram program = Compiled(Carrying(x, y, z));
        Assert.True(program.UsesTangents);
        Assert.False(program.Marches);
        AssertClose(
            MeshPicture.Of(Tilted(spanned: true), 64, skins: [Sheet(Srgb(red), Srgb(green), Srgb(blue))]),
            MeshPicture.Of(Tilted(spanned: true), 64, shades: [program]));
    }

    /// <summary>The same quad with every vertex at one coordinate: no frame, so a vector along the tangent comes to nought and one along the normal stays.</summary>
    [Theory]
    [InlineData(1f, 0f, 0f, 0.5f, 0.5f, 0.5f)]
    [InlineData(0.6f, 0f, 0.8f, 0.692f, 0.244f, 0.5f)]
    public void ANDWITHNoFrameOnlyThePartAlongTheNormalIsDrawn(float x, float y, float z, float red, float green, float blue)
        => AssertClose(
            MeshPicture.Of(Tilted(spanned: false), 64, skins: [Sheet(Srgb(red), Srgb(green), Srgb(blue))]),
            MeshPicture.Of(Tilted(spanned: false), 64, shades: [Compiled(Carrying(x, y, z))]));

    /// <summary>
    /// Its HLSL works the frame out of the screen's derivatives and carries the vector along it in one block, the binormal turned.
    /// </summary>
    [Fact]
    public void THECARDWorksTheFrameOutOfTheScreenAndCarriesTheVectorInOneBlock()
    {
        string hlsl = ShadeHlsl.Of(Compiled(Carrying(1f, 0f, 0f)), 13);
        Assert.Contains("float2 uvx = ddx(uv);", hlsl, StringComparison.Ordinal);
        Assert.Single(hlsl.Split("float3 tbnN = r2.xyz;").Skip(1));
        Assert.Contains("float3 tbnT = r8.xyz - (tbnN * dot(tbnN, r8.xyz));", hlsl, StringComparison.Ordinal);
        Assert.Contains("tbnB = tbnSize > 1e-12 ? -tbnB / tbnSize : float3(0.0, 0.0, 0.0);", hlsl, StringComparison.Ordinal);
    }

    /// <summary>
    /// Port's quay stone as its master swaps it (MPRT_GraveBrickTile01c.mat, from the capture of 2026-10-30): MaterialBlend_GradientUV's
    /// moss weighs its mask by the normal-mapped normal carried out of the basis, so its colour, the occlusion it writes and the sea's
    /// spray that reads that occlusion now compile - nothing is left out, the moss's own textures are read, and nothing resets the
    /// occlusion before the spray reads it, so there is no twin.
    /// </summary>
    [Fact]
    public void PORTSSwappedQuayStoneCompilesItsMossAndItsSpray()
    {
        ShadeCompile compiled = Compile(Mprt);
        Assert.Empty(compiled.Skipped);
        Assert.DoesNotContain(compiled.Lost, one => one.Contains("Transform of", StringComparison.Ordinal) || one.Contains("InputIndirectColor after", StringComparison.Ordinal));

        ShadeProgram program = Assert.IsType<ShadeProgram>(compiled.Program);
        Assert.True(program.Sprays);
        Assert.True(program.UsesTangents);
        Assert.Contains("Metadata/Materials/Environment/MaterialBlends/MaterialBlend_GradientUV.fxgraph", program.Graphs);
        Assert.Contains(program.Textures, one => one.Path == MossColour && one.Srgb);
        Assert.Contains(program.Textures, one => one.Path == MossNormal && !one.Srgb);
        Assert.Null(program.Occluded);
    }

    /// <summary>
    /// The spray reads the moss-lerped occlusion, not one: with both normal maps flat, the moss lies whole on a floor - its own occlusion
    /// (the moss normal map's blue, 64) taken over the stone's (200) - and on a wall all but nowhere, the mask only the AO term's
    /// <c>(1 - z) * 50</c> of the stone's nearly flat normal; the spray's curvature is <c>lerp(occlusion, 0.2741, -0.325)</c> of each.
    /// </summary>
    [Fact]
    public void THESPRAYReadsTheOcclusionTheMossLaid()
    {
        ShadeProgram program = Bound(Assert.IsType<ShadeProgram>(Compile(Mprt).Program));
        float stone = 200f / 255f, moss = 64f / 255f;
        float flat = (2f * (128f / 255f)) - 1f;
        float mask = (1f - MathF.Sqrt(1f - (2f * flat * flat))) * 50f;

        AssertNear(Curvature(moss), CurvatureDrawn(program, floor: true));
        AssertNear(Curvature(stone + ((moss - stone) * mask)), CurvatureDrawn(program, floor: false));
        Assert.True(MathF.Abs(Curvature(1f) - CurvatureDrawn(program, floor: true)) > 0.5f, "the spray read an occlusion of one");
    }

    /// <summary>
    /// A ground that carries a vector out of its basis still mixes per pixel: its frame is the engine's ground basis - the world's x for
    /// the tangent, <c>normalize(cross(N, x))</c> for the binormal: GetGroundTBN's (1, 0, 0), (0, -1, 0), (0, 0, -1) on a floor, and on a
    /// wall facing minus y (0, 0, 1) - not the mesh's, so the mixed program reads no frame of the triangle's at all, and draws on a quad
    /// whose coordinates span nothing.
    /// </summary>
    [Theory]
    [InlineData(true, 1f, 0f, 0f, 0.9f, 0.5f, 0.5f)]
    [InlineData(true, 0f, 1f, 0f, 0.5f, 0.1f, 0.5f)]
    [InlineData(false, 1f, 0f, 0f, 0.9f, 0.5f, 0.5f)]
    [InlineData(false, 0f, 1f, 0f, 0.5f, 0.5f, 0.9f)]
    public void AGROUNDCarryingTheBasisStillMixesPerPixel(bool floor, float x, float y, float z, float red, float green, float blue)
    {
        ShadeProgram plain = Compiled(Plain).With([]);
        ShadeProgram carrying = Compiled(Carrying(x, y, z)).With([]);
        ShadeProgram mixed = Assert.IsType<ShadeProgram>(ShadeProgram.GroundsByCorners(plain, carrying, 0.004f));
        Assert.True(mixed.MixesGrounds);
        Assert.False(mixed.UsesTangents);
        string hlsl = ShadeHlsl.Of(mixed, 13);
        Assert.DoesNotContain("ddx(uv)", hlsl, StringComparison.Ordinal);
        Assert.Contains("float3 tbnN = r2.xyz;", hlsl, StringComparison.Ordinal);

        // EVERY VERTEX THE SECOND GROUND'S WHOLE SHARE, the corners' share riding the coordinates' first channel.
        Vector3[] places = floor
            ? [new(-10f, -10f, 0f), new(10f, -10f, 0f), new(10f, 10f, 0f), new(-10f, 10f, 0f)]
            : [new(-10f, 0f, -10f), new(10f, 0f, -10f), new(10f, 0f, 10f), new(-10f, 0f, 10f)];
        var normals = new Vector3[4];
        Array.Fill(normals, floor ? new Vector3(0f, 0f, -1f) : new Vector3(0f, -1f, 0f));
        var spots = new Vector2[4];
        Array.Fill(spots, new Vector2(1f, 0f));
        SkinnedMesh ground = SkinnedMesh.Of(
            places, normals, [0, 1, 2, 0, 2, 3], places.Aggregate(Vector3.Min), places.Aggregate(Vector3.Max), spots, [new MeshShape("Ground", 0, 6)]);
        var probe = new PixelProbe(Size / 2, Size / 2);
        _ = MeshPicture.Of(ground, new MeshPicture.Canvas(Size) { Probe = probe }, tilt: floor ? MathF.PI / 2f : 0f, shades: [mixed]);

        ProbeGround mix = Assert.Single(probe.Grounds);
        Assert.Equal(1f, mix.Green);
        AssertNear(new Vector3(red, green, blue), mix.Second);
    }

    /// <summary>A ground that marches a parallax is still refused: its march reads the triangle's frame, and the mesh's coordinates there are the mask's.</summary>
    [Fact]
    public void ANDAGROUNDThatMarchesIsStillRefused()
    {
        ShadeProgram marching = Assert.IsType<ShadeProgram>(Compile("Art/Models/Terrain/Jungle/Tiles/Jungle/Textures/Dung01c.mat").Program);
        Assert.True(marching.Marches);
        Assert.Null(ShadeProgram.GroundsByCorners(marching, Compiled(Plain), 0.004f));
        Assert.Null(ShadeProgram.GroundsByCorners(Compiled(Plain), marching, 0.004f));
    }

    private const string Mprt = "Art/Models/Terrain/Maps/Port/Textures/Architecture/MPRT_GraveBrickTile01c.mat";

    private const string MossColour = "Art/Models/Terrain/Maps/Stronghold/Textures/Architecture/MSHD_Moss01_colour.dds";

    private const string MossNormal = "Art/Models/Terrain/Maps/Stronghold/Textures/Architecture/MSHD_Moss01_normal.dds";

    private const string StoneNormal = "Art/Models/Terrain/Maps/Stronghold/Textures/Architecture/MSHD_GraveBrickTile01_normal.dds";

    /// <summary>The cells of a drawing a probe stands in the middle of.</summary>
    private const int Size = 32;

    /// <summary>A plain colour, written at Texturing_Init.</summary>
    private const string Plain = """
        {"nodes":[
          {"type":"ConstantPixel4","index":0,"parameters":[{"value":[0.2,0.3,0.4,1.0]}]},
          {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
         "links":[
          {"src":{"type":"ConstantPixel4","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input"}}]}
        """;

    /// <summary>The colour <c>0.4 mul(v, InputTbnBasis) + 0.5</c> - a vector carried out of the basis, each of its parts a colour.</summary>
    private static string Carrying(float x, float y, float z) =>
        string.Create(CultureInfo.InvariantCulture, $$$"""
            {"nodes":[
              {"type":"ConstantFloat3","index":0,"parameters":[{"value":[{{{x}}},{{{y}}},{{{z}}}]}]},
              {"type":"InputTbnBasis","index":0,"stage":"Texturing_Init"},
              {"type":"Transform","index":0},
              {"type":"ConstantFloat","index":0,"parameters":[{"value":0.4}]},
              {"type":"ConstantFloat","index":1,"parameters":[{"value":0.5}]},
              {"type":"MultiplyAdd3","index":0},
              {"type":"AlbedoColor","index":0,"stage":"Texturing_Init"}],
             "links":[
              {"src":{"type":"ConstantFloat3","index":0,"variable":"output"},"dst":{"type":"Transform","index":0,"variable":"input"}},
              {"src":{"type":"InputTbnBasis","index":0,"stage":"Texturing_Init","variable":"output"},"dst":{"type":"Transform","index":0,"variable":"inmatrix"}},
              {"src":{"type":"Transform","index":0,"variable":"output"},"dst":{"type":"MultiplyAdd3","index":0,"variable":"a"}},
              {"src":{"type":"ConstantFloat","index":0,"variable":"output"},"dst":{"type":"MultiplyAdd3","index":0,"variable":"b"}},
              {"src":{"type":"ConstantFloat","index":1,"variable":"output"},"dst":{"type":"MultiplyAdd3","index":0,"variable":"c"}},
              {"src":{"type":"MultiplyAdd3","index":0,"variable":"output"},"dst":{"type":"AlbedoColor","index":0,"stage":"Texturing_Init","variable":"input","swizzle":"xyz"}}]}
            """);

    /// <summary>
    /// A quad laid by its coordinates, <c>p0 + u ∂p/∂u + v ∂p/∂v</c> over <see cref="AlongU"/> and <see cref="AlongV"/>, every normal
    /// <see cref="Leaning"/> - or, not spanned, every vertex at one coordinate.
    /// </summary>
    private static SkinnedMesh Tilted(bool spanned)
    {
        Vector3 origin = -(AlongU + AlongV) / 2f;
        Vector2[] corners = [new(0f, 0f), new(1f, 0f), new(1f, 1f), new(0f, 1f)];
        Vector3[] places = [.. corners.Select(one => origin + (AlongU * one.X) + (AlongV * one.Y))];
        var normals = new Vector3[4];
        Array.Fill(normals, Leaning);
        Vector2[] spots = spanned ? corners : [.. corners.Select(_ => new Vector2(0.5f, 0.5f))];
        Vector3 least = places.Aggregate(Vector3.Min), most = places.Aggregate(Vector3.Max);
        return SkinnedMesh.Of(places, normals, [0, 1, 2, 0, 2, 3], least, most, spots, [new MeshShape("Quad", 0, 6)]);
    }

    /// <summary>OceanSprayFoam's curvature from an occlusion: <c>lerp(occlusion, 0.2741, OceanSpray_Intensity)</c>, MPRT's intensity -0.325.</summary>
    private static float Curvature(float occlusion) => occlusion + ((0.27410000562667847f - occlusion) * -0.3249999880790711f);

    /// <summary>
    /// The curvature the spray was handed in the middle of a floor looked at from above, or of a wall facing minus y looked at from the
    /// side - coordinates along x and along the floor's y or the wall's z, so the frame is the plane's own.
    /// </summary>
    private static float CurvatureDrawn(ShadeProgram program, bool floor)
    {
        Vector3[] places = floor
            ? [new(-10f, -10f, 0f), new(10f, -10f, 0f), new(10f, 10f, 0f), new(-10f, 10f, 0f)]
            : [new(-10f, 0f, -10f), new(10f, 0f, -10f), new(10f, 0f, 10f), new(-10f, 0f, 10f)];
        var normals = new Vector3[4];
        Array.Fill(normals, floor ? new Vector3(0f, 0f, -1f) : new Vector3(0f, -1f, 0f));
        Vector2[] spots = [.. places.Select(one => new Vector2((one.X + 10f) / 20f, ((floor ? one.Y : one.Z) + 10f) / 20f))];
        SkinnedMesh mesh = SkinnedMesh.Of(
            places, normals, [0, 1, 2, 0, 2, 3], places.Aggregate(Vector3.Min), places.Aggregate(Vector3.Max), spots, [new MeshShape("Face", 0, 6)]);
        var probe = new PixelProbe(Size / 2, Size / 2);
        _ = MeshPicture.Of(mesh, new MeshPicture.Canvas(Size) { Probe = probe }, tilt: floor ? MathF.PI / 2f : 0f, shades: [program]);
        return Assert.Single(probe.Sprays).Curvature;
    }

    /// <summary>MPRT's textures in hand: both normal maps flat, the stone's occlusion 200 and the moss's 64; everything else a plain grey.</summary>
    private static ShadeProgram Bound(ShadeProgram program)
    {
        ShadeProgram bound = program.With([.. program.Textures.Select(one => one.Path switch
        {
            StoneNormal => Sheet(128, 128, 200),
            MossNormal => Sheet(128, 128, 64),
            _ => Sheet(200, 200, 200),
        })]);
        Assert.True(bound.Bound);
        return bound;
    }

    /// <summary>A graph compiled whole: a program, nothing left out.</summary>
    private static ShadeProgram Compiled(string graph)
    {
        ShaderGraph read = ShaderGraph.Read(Encoding.UTF8.GetBytes(graph));
        Assert.True(read.Ready);
        ShadeCompile compiled = ShadeProgram.Compile([(new ShaderInstance("Metadata/Test.fxgraph", new Dictionary<string, ShaderValue[]>()), read)]);
        Assert.Empty(compiled.Skipped);
        return Assert.IsType<ShadeProgram>(compiled.Program);
    }

    /// <summary>A material as the install holds it (tests/fixtures/shaders), compiled - its graphs from the same folder.</summary>
    private static ShadeCompile Compile(string path)
    {
        IReadOnlyList<ShaderInstance> instances = ShaderGraph.Instances(Fixture(path));
        return ShadeProgram.Compile([.. instances.Select(one => (one, ShaderGraph.Read(Fixture(one.Parent))))]);
    }

    private static byte[] Fixture(string path)
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "tests", "fixtures")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllBytes(Path.Combine(dir.FullName, "tests", "fixtures", "shaders", path.Replace("/", "__", StringComparison.Ordinal)));
    }

    private static void AssertNear(Vector3 expected, Vector3 actual)
        => Assert.True(Vector3.Distance(expected, actual) < 1e-4f, $"{expected} expected, {actual} worked out");

    private static void AssertNear(float expected, float actual)
        => Assert.True(MathF.Abs(expected - actual) < 1e-4f, $"{expected} expected, {actual} worked out");

    /// <summary>A texture of one colour, opaque. Whether it is read as sRGB is the graph's to say, not the texture's.</summary>
    private static Mipmaps Sheet(byte red, byte green, byte blue)
    {
        var pixels = new byte[8 * 8 * 4];
        for (var one = 0; one < 8 * 8; one++)
        {
            pixels[(one * 4) + 0] = red;
            pixels[(one * 4) + 1] = green;
            pixels[(one * 4) + 2] = blue;
            pixels[(one * 4) + 3] = 255;
        }

        Mipmaps? levels = Mipmaps.Of(new GamePicture(8, 8, pixels));
        Assert.NotNull(levels);
        return levels;
    }

    /// <summary>The sRGB byte for a linear value - what a plain texture would hold.</summary>
    private static byte Srgb(float linear)
    {
        float encoded = linear <= 0.0031308f ? linear * 12.92f : (1.055f * MathF.Pow(linear, 1f / 2.4f)) - 0.055f;
        return (byte)Math.Clamp((int)MathF.Round(encoded * 255f), 0, 255);
    }

    /// <summary>The two pictures alike within the sRGB table's step, and both actually covered.</summary>
    private static void AssertClose(GamePicture expected, GamePicture actual)
    {
        Assert.Equal(expected.Rgba.Length, actual.Rgba.Length);
        var covered = 0;
        for (var at = 0; at < expected.Rgba.Length; at += 4)
        {
            if (expected.Rgba[at + 3] == 0 && actual.Rgba[at + 3] == 0)
            {
                continue;
            }

            covered++;
            for (var part = 0; part < 4; part++)
            {
                int apart = Math.Abs(expected.Rgba[at + part] - actual.Rgba[at + part]);
                Assert.True(apart <= 2, $"pixel {at / 4} channel {part}: {expected.Rgba[at + part]} expected, {actual.Rgba[at + part]} drawn");
            }
        }

        Assert.True(covered > 100, $"only {covered} pixels covered");
    }
}
