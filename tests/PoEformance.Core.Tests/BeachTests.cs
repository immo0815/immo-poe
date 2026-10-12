using System.Buffers.Binary;
using System.Numerics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The engine's beach waves (water.ffx's beach_waves): their sheet over the shore's ground, their crest rolling in off the engine's own
/// wave shape and breaking at the waterline, their foam running up the sand and stopping - and the sheet drawn over the solid part,
/// writing its depth.
/// </summary>
public sealed class BeachTests
{
    private const int Size = 96;

    /// <summary>
    /// The sheet lies four units over the ground from 300 under the water to 100 above it, a vertex at every cell's middle; a square
    /// that would hang down a wall's face is left out, and a shore with no ground in the band has no sheet.
    /// </summary>
    [Fact]
    public void THEBEACHSheetLiesOverTheGroundInTheShoresBand()
    {
        SkinnedMesh sheet = SlopeShore().Beach()!;
        Assert.NotNull(sheet);
        Assert.True(sheet.Triangles > 1000);
        foreach (Vector3 place in sheet.Positions)
        {
            Assert.Equal((-0.1f * place.X) - ShoreField.BeachRaise, place.Z, 2);
        }

        // UP TO 100 ABOVE THE WATER, the ground at minus a tenth of x: x no further than 1,000, and every normal up.
        Assert.InRange(sheet.Most.X, 975f, 1000f);
        Assert.True(sheet.Least.Z >= -ShoreField.BeachHigh - ShoreField.BeachRaise - 0.01f);
        Assert.All(sheet.Normals, normal => Assert.True(normal.Z < -0.99f));

        // THE QUAY'S WALL: the sea floor 300 down and the quay 100 up are both in the band, but no square spans the wall between them.
        SkinnedMesh bay = ShoreField.Of(WaterTests.Bay(), shape => shape == 0, shape => shape > 0)!.Beach()!;
        for (var at = 0; at < bay.Indices.Length; at += 3)
        {
            float[] xs = [bay.Positions[bay.Indices[at]].X, bay.Positions[bay.Indices[at + 1]].X, bay.Positions[bay.Indices[at + 2]].X];
            Assert.False(xs.Min() < 1000f && xs.Max() > 1000f);
        }

        // ALL THE GROUND 500 UNDER THE WATER: no sheet.
        Assert.Null(ShoreField.Of(Deep(), shape => shape == 0, shape => shape == 1)!.Beach());
    }

    /// <summary>
    /// On the engine's own wave shape (Port's capture): nothing stands far out; the crest builds as it rolls in at about 170 a second,
    /// stands 35 to 55 tall at the waterline - the engine's phase shoreline, there by ShadeProgram.BeachTravelShift - and is gone past
    /// it, where a vertex moves by the area's height offset alone, as it does with no shore.
    /// </summary>
    [Fact]
    public void THEBEACHWavesCrestRollsInAndBreaksAtTheWaterline()
    {
        WaterSheet profile = WaterLookups.Read(Engine).BeachProfile!;
        Assert.True(profile is { Engine: true, Width: 256, Height: 256 });
        ShoreField shore = SlopeShore();
        WaterSheet noise = Half();
        float Lift(float x, float clock, float offset = 0f) => ShadeProgram.BeachLift(profile, noise, new Vector2(x, 0f), clock, offset, shore);
        (float Lowest, float Highest) Over(float x)
        {
            float lowest = float.MaxValue, highest = float.MinValue;
            for (float clock = 0f; clock < 10f; clock += 0.05f)
            {
                lowest = MathF.Min(lowest, Lift(x, clock));
                highest = MathF.Max(highest, Lift(x, clock));
            }

            return (lowest, highest);
        }

        // UP IS MINUS Z: a crest is a lift below nought.
        Assert.True(Over(-1000f).Lowest > -0.5f);
        Assert.InRange(-Over(0f).Lowest, 35f, 55f);
        Assert.True(-Over(-300f).Lowest < -Over(-100f).Lowest);
        Assert.True(Over(0f).Highest <= 0.01f);

        // ROLLING IN towards plus x: the tallest crest between the sea and the waterline, a second apart.
        float Crest(float clock)
        {
            float best = 0f, at = 0f;
            for (float x = -600f; x <= 0f; x += 10f)
            {
                float lift = Lift(x, clock);
                if (lift < best)
                {
                    best = lift;
                    at = x;
                }
            }

            return at;
        }

        Assert.InRange(Crest(1.5f) - Crest(0.5f), 120f, 220f);

        // PAST THE SHORELINE, AND WITHOUT A SHORE: the height offset, as BreakingWavesVertex adds it.
        for (float clock = 0f; clock < 5f; clock += 0.25f)
        {
            Assert.Equal(-15f, Lift(800f, clock, -15f));
        }

        Assert.Equal(-15f, ShadeProgram.BeachLift(profile, noise, Vector2.Zero, 1f, -15f, null));
    }

    /// <summary>
    /// The foam runs up the sand from the waterline and stops: none at sea, some within 500 of the waterline, none past 600 - and the
    /// film over deep water is the area's water colour, covering by what the path lets through.
    /// </summary>
    [Fact]
    public void THEBEACHFoamRunsUpTheShoreAndStops()
    {
        WaterSheet profile = WaterLookups.Read(Engine).BeachProfile!;
        ShoreField shore = SlopeShore();
        WaterSheet noise = Half();
        ShadeProgram.BeachLook Look(float x, float clock, float crossed)
            => ShadeProgram.Beaching(profile, noise, new Vector2(x, 0f), clock, crossed, new Vector3(0.1f, 0.2f, 0.3f), shore, default(Foamy), 0, 1, 2);
        float Most(float x)
        {
            float most = 0f;
            for (float clock = 0f; clock < 10f; clock += 0.05f)
            {
                most = MathF.Max(most, Look(x, clock, 0f).Colour.W);
            }

            return most;
        }

        // NOTHING BEHIND IT: the cover is the foam's alone, 0.7 of its alpha.
        Assert.Equal(0f, Most(-1000f), 4);
        Assert.True(Most(-100f) < 0.01f);
        Assert.InRange(Most(300f), 0.2f, 0.7f);
        Assert.Equal(0f, Most(700f), 4);
        Assert.Equal(0f, Most(1200f), 4);

        // FAR OUT, OVER 100 UNITS OF PATH: no foam, the film whole - thickness one - letting through exp(-100 * absorption), in the water's colour.
        ShadeProgram.BeachLook film = Look(-1000f, 2f, 100f);
        Assert.Equal(1f - MathF.Exp(-100f * ShadeProgram.BeachAbsorption), film.Colour.W, 3);
        Assert.Equal(new Vector3(0.1f, 0.2f, 0.3f), new Vector3(film.Colour.X, film.Colour.Y, film.Colour.Z));
        Assert.Equal(1f, film.Gloss, 3);   // DissolveLayeredSurface takes the roughness away with the foam
        Assert.True(film.Normal.Z < -0.99f);

        // NO SHORE, NOTHING.
        Assert.Equal(Vector4.Zero, ShadeProgram.Beaching(profile, noise, Vector2.Zero, 1f, 100f, Vector3.One, null, default(Foamy), 0, 1, 2).Colour);
    }

    /// <summary>
    /// The beach is drawn over the solid part before anything translucent, writing its depth: a mixed sheet between the floor and the
    /// beach is hidden by it, though it comes first in the mesh - and drawn where there is no beach.
    /// </summary>
    [Fact]
    public void THEBEACHIsDrawnOverTheSolidPartWritingItsDepth()
    {
        ShadeProgram unbound = ShadeProgram.Beach(new ShadeTexture("foam", true), new ShadeTexture("bumps", false), new ShadeTexture("ripples", false));
        Assert.True(unbound.Glossy is { Beaches: true, HasWaves: false, HasSpecular: true, HasGloss: true });
        ShadeProgram beach = unbound.With([Sheet(255, 255, 255, 128), Sheet(128, 128, 255, 128), Sheet(128, 128, 255, 255)]);
        Assert.True(beach is { Beaches: true, HasWaves: false, Bound: true });
        WaterLook water = WaterLook.EngineDefault with { Shore = SlopeShore() };

        var probe = new PixelProbe(Size / 2, Size / 2);
        byte[] Drawn(bool withBeach, PixelProbe? asked = null) => [.. MeshPicture.Of(
            Layered(), new MeshPicture.Canvas(Size) { Water = water, Time = 2f, Probe = asked }, tilt: MathF.PI / 2f,
            skins: [Sheet(200, 180, 120, 255), Sheet(30, 30, 220, 160), null],
            blends: [MaterialBlend.Opaque, MaterialBlend.Alpha, MaterialBlend.Alpha],
            shades: [null, null, withBeach ? beach : null]).Rgba];

        _ = Drawn(withBeach: true, probe);
        Assert.Single(probe.Fragments, one => one.Seen == Seen.Mixed && one.Triangle >= 4);
        Assert.Contains(probe.Fragments, one => one.Seen == Seen.Under && one.Triangle is 2 or 3);
        ProbeFragment laid = probe.Fragments.Single(one => one.Seen == Seen.Mixed);
        Assert.Equal(laid.Depth, probe.Final);

        // WITHOUT THE BEACH'S PROGRAM the sheet is an ordinary mixed shape, after the mixed one in the mesh: both blend.
        var plain = new PixelProbe(Size / 2, Size / 2);
        _ = Drawn(withBeach: false, plain);
        Assert.Equal(2, plain.Fragments.Count(one => one.Seen == Seen.Mixed));
    }

    /// <summary>
    /// A room with a shore is piled with the beach's sheet as a shape of its own, wearing the engine's graph and mixed - once: a stage
    /// piling the room again keeps the one it brings. The sheet is no water and no ground, so the shore is the room's own.
    /// </summary>
    [Fact]
    public void AROOMWithAShoreIsPiledWithTheBeachSheetOnce()
    {
        SkinnedMesh slope = Slope();
        var water = SkinnedMesh.Of(slope.Positions[..4], slope.Normals[..4], [0, 1, 2, 0, 2, 3], new Vector3(-2000f, -2000f, 0f), new Vector3(2000f, 2000f, 0f));
        var ground = SkinnedMesh.Of(slope.Positions[4..], slope.Normals[4..], [0, 1, 2, 0, 2, 3], slope.Least, slope.Most);
        var pile = new PoEformance.Features.ModelPile();
        pile.AddPlain(water, Matrix4x4.Identity, "WaterPlane3x3.ao");
        pile.Modes[0] = "water";
        pile.AddPlain(ground, Matrix4x4.Identity, PoEformance.Features.RoomModels.GroundSource + "beach.tdt");
        PoEformance.Features.MonsterModel room = PoEformance.Features.RoomModels.Piled(SkinnedMesh.Joined(pile.Joins), pile, "room.arm", []);

        Assert.Equal(3, room.Mesh.Shapes.Count);
        Assert.Equal(PoEformance.Features.RoomModels.BeachSource, room.ShapeSources[2]);
        Assert.Equal(ShadeProgram.BeachGraph, room.ShapeMaterials[2]);
        Assert.Equal(MaterialBlend.Alpha, MaterialBlends.Of(room.Modes[2]));
        Assert.Equal(SlopeShore().Beach()!.Triangles, room.Mesh.Shapes[2].Count / 3);
        Assert.NotNull(room.Shore);

        // A STAGE PILES THE ROOM AGAIN, its pieces' own names kept: the sheet it brings stays the only one.
        var stage = new PoEformance.Features.ModelPile();
        stage.Add(room, Matrix4x4.Identity, "stage");
        for (var shape = 0; shape < room.ShapeSources.Count; shape++)
        {
            stage.Sources[shape] = room.ShapeSources[shape];
        }

        PoEformance.Features.MonsterModel staged = PoEformance.Features.RoomModels.Piled(SkinnedMesh.Joined(stage.Joins), stage, "stage", [room]);
        Assert.Equal(3, staged.Mesh.Shapes.Count);
        Assert.Equal(room.Shore!.LandCells, staged.Shore!.LandCells);
    }

    /// <summary>The engine's graph builds the beach program over the three textures it binds, and a graph binding none builds none.</summary>
    [Fact]
    public void THEENGINESBeachGraphBindsItsThreeTextures()
    {
        ShaderGraph graph = ShaderGraph.Read(File.ReadAllBytes(Path.Combine(ComponentSweepTests.Fixture("beach-waves"), "BreakingOceanWaves.fxgraph")));
        ShadeCompile compiled = ShadeProgram.BeachOf(graph);
        Assert.NotNull(compiled.Program);
        Assert.Equal(
            [
                new ShadeTexture("Art/Textures/Environment/Global/BreakingWaves/SeaFoam_colour_Uncompressed.dds", true),
                new ShadeTexture("Art/Textures/Environment/Global/BreakingWaves/SeaFoam_normal_Uncompressed.dds", false),
                new ShadeTexture("Art/Textures/General/Water/backwatertile_normal_DXT1.dds", false),
            ],
            compiled.Program!.Textures);
        Assert.True(compiled.Program.Beaches && compiled.Program.UsesDepth && compiled.Program.UsesTime);

        // ITS LOOKUPS ARE THE ONES WaterLookups READS: the graph names them for the vertex and the fragment.
        string text = File.ReadAllText(Path.Combine(ComponentSweepTests.Fixture("beach-waves"), "BreakingOceanWaves.fxgraph"));
        Assert.Contains(WaterLookups.BeachProfilePath, text, StringComparison.Ordinal);
        Assert.Contains(WaterLookups.BeachNoisePath, text, StringComparison.Ordinal);

        Assert.Null(ShadeProgram.BeachOf(ShaderGraph.Read("{\"nodes\":[],\"links\":[]}"u8.ToArray())).Program);
    }

    /// <summary>
    /// hash33Stable as the engine's fragments write it - PCG's rounds over the bits, each a fraction of its top 23 (HashUToF) - against
    /// values worked out apart; and the noise's sixteen-bit fractions read as such.
    /// </summary>
    [Fact]
    public void THEENGINESStableHashAndSixteenBitFractions()
    {
        Assert.Equal(new Vector3(0.6081517934799194f, 0.659798264503479f, 0.24641847610473633f), ShadeProgram.HashStable(Vector3.Zero));
        Assert.Equal(new Vector3(0.8735014200210571f, 0.09023833274841309f, 0.7359516620635986f), ShadeProgram.HashStable(new Vector3(5f, 3f, 0f)));
        Assert.Equal(new Vector3(0.021092772483825684f, 0.8531996011734009f, 0.28804826736450195f), ShadeProgram.HashStable(new Vector3(-2f, 0f, 0f)));
        Assert.Equal(ShadeProgram.HashStable(new Vector3(-2f)), ShadeProgram.HashStable(-2f));

        // R16G16_UNORM, DXGI 35: two fractions of 65,535, blue nought, alpha one.
        var texels = new byte[8];
        BinaryPrimitives.WriteUInt16LittleEndian(texels, 65535);
        BinaryPrimitives.WriteUInt16LittleEndian(texels.AsSpan(2), 0);
        BinaryPrimitives.WriteUInt16LittleEndian(texels.AsSpan(4), 32768);
        BinaryPrimitives.WriteUInt16LittleEndian(texels.AsSpan(6), 1);
        FloatPicture picture = FloatArt.Decode(Dx10(2, 1, 35, texels), out _)!;
        Assert.Equal([1f, 0f, 0f, 1f, 32768f / 65535f, 1f / 65535f, 0f, 1f], picture.Texels);
        Assert.Equal("DXGI format 35 (R16G16_UNORM)", picture.Format);
    }

    /// <summary>
    /// A beach: water standing at nought over x from -2,000 to 2,000, and the ground rising a tenth a unit along x from 300 under it to
    /// 300 above, past the shore field's margin - the waterline at nought. Up is minus z.
    /// </summary>
    internal static SkinnedMesh Slope()
    {
        Vector3[] places =
        [
            new(-2000f, -2000f, 0f), new(2000f, -2000f, 0f), new(2000f, 2000f, 0f), new(-2000f, 2000f, 0f),
            new(-3000f, -3000f, 300f), new(3000f, -3000f, -300f), new(3000f, 3000f, -300f), new(-3000f, 3000f, 300f),
        ];
        var normals = new Vector3[places.Length];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        int[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7];
        return SkinnedMesh.Of(
            places, normals, indices, new Vector3(-3000f, -3000f, -300f), new Vector3(3000f, 3000f, 300f),
            shapes: [new MeshShape("Water", 0, 6), new MeshShape("Beach", 6, 6)]);
    }

    /// <summary>The slope's shore.</summary>
    internal static ShoreField SlopeShore() => ShoreField.Of(Slope(), shape => shape == 0, shape => shape == 1)!;

    /// <summary>Water at nought, its ground 500 under it but for a quay standing at its edge.</summary>
    private static SkinnedMesh Deep()
    {
        Vector3[] places =
        [
            new(-2000f, -2000f, 0f), new(2000f, -2000f, 0f), new(2000f, 2000f, 0f), new(-2000f, 2000f, 0f),
            new(-3000f, -3000f, 500f), new(1000f, -3000f, 500f), new(1000f, 3000f, 500f), new(-3000f, 3000f, 500f),
            new(1000f, -3000f, -500f), new(3000f, -3000f, -500f), new(3000f, 3000f, -500f), new(1000f, 3000f, -500f),
        ];
        var normals = new Vector3[places.Length];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        int[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7, 8, 9, 10, 8, 10, 11];
        return SkinnedMesh.Of(
            places, normals, indices, new Vector3(-3000f, -3000f, -500f), new Vector3(3000f, 3000f, 500f),
            shapes: [new MeshShape("Water", 0, 6), new MeshShape("Ground", 6, 12)]);
    }

    /// <summary>
    /// Seen from above, out at sea on the slope's shore: a floor at nought, a mixed sheet 40 up, and over both the beach's sheet 100 up -
    /// the beach last in the mesh.
    /// </summary>
    private static SkinnedMesh Layered()
    {
        var places = new List<Vector3>();
        var indices = new List<int>();
        foreach (float height in (ReadOnlySpan<float>)[0f, 40f, 100f])
        {
            int first = places.Count;
            places.AddRange([new(-1200f, -200f, -height), new(-800f, -200f, -height), new(-800f, 200f, -height), new(-1200f, 200f, -height)]);
            indices.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
        }

        var normals = new Vector3[places.Count];
        Array.Fill(normals, new Vector3(0f, 0f, -1f));
        return SkinnedMesh.Of(
            [.. places], normals, [.. indices], new Vector3(-1200f, -200f, -100f), new Vector3(-800f, 200f, 0f),
            [.. places.Select(one => new Vector2((one.X + 1200f) / 400f, (one.Y + 200f) / 400f))],
            [new MeshShape("Floor", 0, 6), new MeshShape("Glass", 6, 6), new MeshShape("Beach", 12, 6)]);
    }

    /// <summary>A noise of a half everywhere - the stand-in's, which shifts no wave.</summary>
    internal static WaterSheet Half() => new(2, 2, 4, [0.5f, 0.5f, 0f, 1f, 0.5f, 0.5f, 0f, 1f, 0.5f, 0.5f, 0f, 1f, 0.5f, 0.5f, 0f, 1f], false, "a half");

    /// <summary>Foam textures of a known kind: white foam half its height, a flat normal, ao one and roughness a half.</summary>
    internal readonly struct Foamy : ShadeProgram.IBeachTextures
    {
        public Vector4 Read(int sheet, Vector2 uv) => sheet == 0 ? new Vector4(1f, 1f, 1f, 0.5f) : new Vector4(0.5f, 0.5f, 1f, 0.5f);

        public Vector4 Average(int sheet) => Read(sheet, default);
    }

    /// <summary>The engine's lookups as the capture writes them, out of tests/fixtures/water-lookups.</summary>
    internal static byte[]? Engine(string path)
    {
        string file = Path.Combine(ComponentSweepTests.Fixture("water-lookups"), "water-lookup-" + Path.GetFileName(path).ToLowerInvariant());
        return File.Exists(file) ? File.ReadAllBytes(file) : null;
    }

    /// <summary>A texture of one colour.</summary>
    private static Mipmaps Sheet(byte red, byte green, byte blue, byte alpha)
    {
        var pixels = new byte[8 * 8 * 4];
        for (var one = 0; one < 8 * 8; one++)
        {
            pixels[one * 4] = red;
            pixels[(one * 4) + 1] = green;
            pixels[(one * 4) + 2] = blue;
            pixels[(one * 4) + 3] = alpha;
        }

        return Mipmaps.Of(new GamePicture(8, 8, pixels))!;
    }

    /// <summary>A DDS behind DX10's header, of a DXGI format.</summary>
    private static byte[] Dx10(int width, int height, uint format, byte[] texels)
    {
        var dds = new byte[128];
        "DDS "u8.CopyTo(dds);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(4), 124);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(8), 0x1007);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(12), height);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(16), width);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(28), 1);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(76), 32);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(80), 0x4);
        BinaryPrimitives.WriteUInt32LittleEndian(dds.AsSpan(84), 0x30315844);
        BinaryPrimitives.WriteInt32LittleEndian(dds.AsSpan(108), 0x1000);
        var extra = new byte[20];
        BinaryPrimitives.WriteUInt32LittleEndian(extra, format);
        BinaryPrimitives.WriteUInt32LittleEndian(extra.AsSpan(4), 3);
        BinaryPrimitives.WriteUInt32LittleEndian(extra.AsSpan(12), 1);
        return [.. dds, .. extra, .. texels];
    }
}
