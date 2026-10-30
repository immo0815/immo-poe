using System.Numerics;
using PoEformance.Game.Files;
using Xunit;

namespace PoEformance.Core.Tests;

/// <summary>
/// What a capture reads of a drawing pixel by pixel: the light term by term (SceneLight.Shade's split), the gathered pixels (PixelGather) and
/// the sun's map that knows its casters (ShadowOwners).
/// </summary>
public class PixelGatherTests
{
    private const float Down = MathF.PI / 2f;

    /// <summary>The split light is the light to the bit, and its terms add up to what was lit before the exposure.</summary>
    [Fact]
    public void THESPLITLightIsTheLightAndItsTermsAddUp()
    {
        var light = new SceneLight(
            [new PointLight(new Vector3(30f, -20f, -80f), new Vector3(1f, 0.7f, 0.4f), 150f, 0f, "room")], SceneLight.PointShape.Zero,
            new SceneLight.PlayerLamp(new Vector3(-40f, 10f, -60f), new Vector3(0.6f, 0.6f, 0.7f), 200f))
        {
            Ambient = SceneAmbient.Flat,
            FlatAmbient = 0.04f,
            SunColour = new Vector3(2.4f, 2.1f, 1.8f),
            SunDirection = Vector3.Normalize(new Vector3(0.5f, -0.3f, 0.8f)),
            Exposure = 1.3f,
        };
        Assert.Equal(1, light.PlayerIndex);

        var random = new Random(7);
        for (var one = 0; one < 200; one++)
        {
            Vector3 normal = Vector3.Normalize(new Vector3(random.NextSingle() - 0.5f, random.NextSingle() - 0.5f, -random.NextSingle()));
            Vector3 place = new((random.NextSingle() * 200f) - 100f, (random.NextSingle() * 200f) - 100f, -random.NextSingle() * 40f);
            Vector3 toEye = Vector3.Normalize(new Vector3(0.1f, 0.2f, -1f));
            Vector3 albedo = new(random.NextSingle(), random.NextSingle(), random.NextSingle());
            bool glossy = one % 2 == 0;
            float sun = random.NextSingle();
            var specular = new Vector3(0.04f + (random.NextSingle() * 0.5f));
            float gloss = random.NextSingle();

            Vector3 plain = light.Shade(albedo, place, normal, toEye, sun, glossy, specular, gloss, default);
            Vector3 split = light.Shade(albedo, place, normal, toEye, sun, glossy, specular, gloss, default, 1f, out SceneLight.LightTerms terms);
            Assert.Equal(plain, split);
            Assert.Equal(plain, terms.Graded);

            Vector3 sum = terms.SunDiffuse + terms.RoomDiffuse + terms.PlayerDiffuse + terms.Cube + terms.GiStandIn + terms.Flat
                + terms.Lobes + terms.EnvironmentSpecular + terms.WavesReflection;
            Assert.True(Vector3.Distance(sum, terms.Unexposed) < 1e-5f * (1f + terms.Unexposed.Length()), $"{sum} against {terms.Unexposed}");
            Assert.True(Vector3.Distance(terms.Exposed, terms.Unexposed * 1.3f) < 1e-5f);
            Assert.Equal(sun, terms.Sun);
        }
    }

    /// <summary>
    /// A gathered drawing is the same picture to the byte, and each gathered pixel names the triangle drawn there and its light - the sun's
    /// share gone where the slab's shadow falls.
    /// </summary>
    [Fact]
    public void AGATHEREDDrawingIsThePictureAndSaysWhatEachPixelWasMadeOf()
    {
        const int size = 96;
        SkinnedMesh scene = Joined(Floor(200f, 0f), Floor(40f, -80f));
        SceneLight light = Sunlit();
        var plain = new MeshPicture.Canvas(size, threads: 2) { Light = light };
        byte[] expected = [.. MeshPicture.Of(scene, plain, 0f, Down).Rgba];

        var gather = new PixelGather(size, step: 2);
        var canvas = new MeshPicture.Canvas(size, threads: 2) { Light = light, Gather = gather };
        Assert.Equal(expected, MeshPicture.Of(scene, canvas, 0f, Down).Rgba);

        int floor = 0, slab = 0, shaded = 0;
        for (var at = 0; at < gather.Triangle.Length; at++)
        {
            int triangle = gather.Triangle[at];
            if (triangle < 0)
            {
                continue;
            }

            SceneLight.LightTerms terms = gather.Terms[at];
            if (triangle >= 2)
            {
                slab++;
                Assert.True(terms.SunDiffuse.X > 0f);
            }
            else
            {
                floor++;
                shaded += terms.Sun < 0.01f ? 1 : 0;
                Assert.True(terms.Sun < 0.01f ? terms.SunDiffuse == Vector3.Zero : terms.SunDiffuse.X > 0f);
            }

            // THE GATHERED COLOUR IS THE PIXEL'S, as the picture rounds it to a byte.
            int x = (at % gather.Width) * gather.Step, y = (at / gather.Width) * gather.Step;
            byte drawn = expected[((y * size) + x) * 4];
            Assert.InRange(Encoded(terms.Graded.X) * 255f, drawn - 1.5f, drawn + 1.5f);
        }

        Assert.True(floor > 200 && slab > 50 && shaded > 20, $"floor {floor}, slab {slab}, shaded {shaded}");
    }

    /// <summary>
    /// The map of casters names the slab over the floor's shadow, reads light as the picture's map does - and a shadow-only card whose
    /// texture is clear casts whole as drawn, nothing when cut by its alpha.
    /// </summary>
    [Fact]
    public void THECASTERSMapNamesWhatCastsAndAClearShadowOnlyCardCastsOnlyAsDrawn()
    {
        SkinnedMesh scene = Joined(Floor(200f, 0f), Floor(40f, -80f), shapes: true);
        Vector3 sun = Vector3.Normalize(new Vector3(0.3f, 0f, 1f));
        Mipmaps clear = Mipmaps.Of(new GamePicture(4, 4, new byte[4 * 4 * 4]))!;
        MaterialBlend[] blends = [MaterialBlend.Opaque, MaterialBlend.ShadowOnly];
        Mipmaps?[] skins = [null, clear];

        ShadowOwners drawn = MeshPicture.Casters(scene, scene.Positions, sun, 512, null, skins, blends, null)!;
        ShadowOwners cut = MeshPicture.Casters(scene, scene.Positions, sun, 512, null, skins, blends, null, MeshPicture.CastRule.ShadowOnlyCut)!;

        // UNDER THE SLAB along the sun, on the floor: the slab's triangles cast as drawn, nothing once cut.
        Vector3 under = new Vector3(0f, 0f, -80f) + (sun * (80f / sun.Z));
        Assert.True(drawn.Lit(under, -Vector3.UnitZ) < 0.01f);
        Assert.InRange(drawn.Caster(under, -Vector3.UnitZ), 2, 3);
        Assert.Equal(1f, cut.Lit(under, -Vector3.UnitZ));
        Assert.Equal(-1, cut.Caster(under, -Vector3.UnitZ));

        // AWAY FROM IT the floor is lit in both.
        Assert.Equal(1f, drawn.Lit(new Vector3(-150f, 150f, 0f), -Vector3.UnitZ));

        // AND THE FLOOR ALONE, its owners its own two triangles.
        ShadowOwners ground = MeshPicture.Casters(scene, scene.Positions, sun, 512, null, skins, blends, null, only: one => one < 2)!;
        Assert.Equal(1f, ground.Lit(under, -Vector3.UnitZ));
        Vector3 on = ground.On(under);
        Assert.InRange(ground.OwnerAt((int)on.X, (int)on.Y), 0, 1);
    }

    /// <summary>The piecewise sRGB curve the picture encodes with.</summary>
    private static float Encoded(float linear)
    {
        float value = Math.Clamp(linear, 0f, 1f);
        return value <= 0.0031308f ? value * 12.92f : (1.055f * MathF.Pow(value, 1f / 2.4f)) - 0.055f;
    }

    private static SceneLight Sunlit() => new([], SceneLight.PointShape.Zero, null)
    {
        Ambient = SceneAmbient.Flat,
        FlatAmbient = 0.05f,
        SunColour = Vector3.One,
        SunDirection = Vector3.Normalize(new Vector3(0.6f, 0f, 1f)),
        SunShadows = true,
    };

    private static SkinnedMesh Floor(float half, float z)
    {
        Vector3[] places =
        [
            new(-half, -half, z), new(half, -half, z), new(half, half, z), new(-half, half, z),
        ];
        Vector3[] normals = [.. places.Select(_ => -Vector3.UnitZ)];
        return SkinnedMesh.Of(places, normals, [0, 1, 2, 0, 2, 3], new Vector3(-half, -half, z), new Vector3(half, half, z));
    }

    private static SkinnedMesh Joined(SkinnedMesh first, SkinnedMesh second, bool shapes = false)
    {
        Vector3[] places = [.. first.Positions, .. second.Positions];
        Vector3[] normals = [.. first.Normals, .. second.Normals];
        int[] indices = [.. first.Indices, .. second.Indices.Select(one => one + first.Positions.Length)];
        Vector2[] spots = [.. places.Select(place => new Vector2((place.X / 400f) + 0.5f, (place.Y / 400f) + 0.5f))];
        return shapes
            ? SkinnedMesh.Of(
                places, normals, indices, Vector3.Min(first.Least, second.Least), Vector3.Max(first.Most, second.Most), spots,
                [new MeshShape("floor", 0, first.Indices.Length), new MeshShape("slab", first.Indices.Length, second.Indices.Length)])
            : SkinnedMesh.Of(places, normals, indices, Vector3.Min(first.Least, second.Least), Vector3.Max(first.Most, second.Most));
    }
}
