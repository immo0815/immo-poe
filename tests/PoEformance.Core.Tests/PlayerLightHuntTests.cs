using System.Buffers.Binary;
using System.Numerics;
using System.Text;
using PoEformance.Core.Diagnostics;
using PoEformance.Game.Diagnostics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>
/// The player light's hunt (PlayerLightHunt): its needles are worked out from the area's .env and the player's place, what stands beside
/// a place found - a place by the player, the light's colour at some scale, a spot's way - is listed with the float after it, the window
/// holding both a place and the colour comes first, and the report reads back alone; FloatHunt.Part hands each reader of one sweep its own.
/// </summary>
public class PlayerLightHuntTests
{
    private static readonly Vector3 Player = new(4162f, 12473f, -340f);

    /// <summary>A ground a float holds exactly, so 150 over it reads back as 150.</summary>
    private const float Ground = -328.25f;

    private static EnvironmentSettings Environment(string json) => EnvironmentSettings.Read("Metadata/EnvironmentSettings/Test.env", Encoding.UTF8.GetBytes(json));

    /// <summary>The colour times the intensity; the colour alone too where the intensity is not one; and the player's x and y.</summary>
    [Fact]
    public void THENEEDLESAreTheColourAsTheFileMakesItAndThePlayersPlace()
    {
        PlayerLightHunt unset = PlayerLightHunt.For(Environment("""{ "player_light": { "colour": [0.90754, 0.83552, 0.76674] } }"""), Player, Ground);
        Assert.Equal(2, unset.Needles.Count);
        Assert.Equal([0.90754f, 0.83552f, 0.76674f], unset.Needles[0].Values);
        Assert.Equal([Player.X, Player.Y], unset.Needles[1].Values);
        Assert.True(float.IsNaN(unset.Of.Penumbra));
        Assert.All(unset.Needles, needle => Assert.Equal(PlayerLightHunt.Around, needle.Around));

        PlayerLightHunt set = PlayerLightHunt.For(Environment("""{ "player_light": { "colour": [0.5, 0.25, 1.0], "intensity": 2.0, "penumbra": 0.78539 } }"""), Player, Ground);
        Assert.Equal(3, set.Needles.Count);
        Assert.Equal([1f, 0.5f, 2f], set.Needles[0].Values);
        Assert.Equal([0.5f, 0.25f, 1f], set.Needles[1].Values);
        Assert.Equal(0.78539f, set.Of.Penumbra);
        Assert.Equal(2f, set.Of.Intensity);
    }

    /// <summary>
    /// A made-up light beside its colour: the place 150 over the ground straight above the player with its w, a way 20 degrees off straight
    /// down with its w, and the colour with its radius after it - each found, and the window ranked as holding both.
    /// </summary>
    [Fact]
    public void ALIGHTSPlaceWayAndColourAreFoundBesideItsColour()
    {
        PlayerLightHunt hunt = PlayerLightHunt.For(Environment("""{ "player_light": { "colour": [0.90754, 0.83552, 0.76674] } }"""), Player, Ground);
        (byte[] window, int place) = Light(hunt.Of.Colour);
        List<PlayerLightHunt.Sight> sights = PlayerLightHunt.Beside(window, place, hunt.Of);

        PlayerLightHunt.Sight near = Assert.Single(sights, one => one.Kind == "near" && one.Said.Contains("150 over the ground", StringComparison.Ordinal));
        Assert.Contains("x +0, y +0 off the player", near.Said, StringComparison.Ordinal);
        Assert.Contains("then 0.80902", near.Said, StringComparison.Ordinal);
        PlayerLightHunt.Sight unit = Assert.Single(sights, one => one.Kind == "unit");
        Assert.Contains("20 degrees from straight down", unit.Said, StringComparison.Ordinal);
        PlayerLightHunt.Sight colour = Assert.Single(sights, one => one.Kind == "colour");
        Assert.Contains("1 times the file's colour", colour.Said, StringComparison.Ordinal);
        Assert.Contains("then 400", colour.Said, StringComparison.Ordinal);
        Assert.Contains(sights, one => one.Kind == "value" && one.Said.Contains("the cosine of the engine's (a reading) penumbra", StringComparison.Ordinal));
    }

    /// <summary>The report's windows read back alone: the light's window listed first as holding both, its sights again.</summary>
    [Fact]
    public void THEREPORTReadsBackAndRanksTheLightsWindowFirst()
    {
        PlayerLightHunt hunt = PlayerLightHunt.For(Environment("""{ "player_light": { "colour": [0.90754, 0.83552, 0.76674] } }"""), Player, Ground);
        (byte[] light, int place) = Light(hunt.Of.Colour);
        byte[] plain = new byte[light.Length];
        BinaryPrimitives.WriteSingleLittleEndian(plain.AsSpan(place), Player.X);
        BinaryPrimitives.WriteSingleLittleEndian(plain.AsSpan(place + 4), Player.Y);
        var result = new FloatHuntResult(1, 1, [new FloatSighting(0x2000, 1), new FloatSighting(0x9000, 0)], [], [], false, TimeSpan.Zero,
            [new FloatDump(0x2000, 1, 0x2000 - (ulong)place, plain), new FloatDump(0x9000, 0, 0x9000 - (ulong)place, light)]);

        string report = hunt.Read(result);
        Assert.Contains("2 windows read, 1 holding both a place by the player and the light's colour", report, StringComparison.Ordinal);

        string replay = PlayerLightHunt.Replay(report);
        Assert.StartsWith("player light hunt replay: 2 windows read back", replay, StringComparison.Ordinal);
        string[] lines = replay.Split('\n');
        int first = Array.FindIndex(lines, line => line.StartsWith("  window ", StringComparison.Ordinal));
        Assert.Contains("window 2: the player light's colour", lines[first], StringComparison.Ordinal);
        Assert.Contains(lines, line => line.Contains("150 over the ground", StringComparison.Ordinal));
        Assert.StartsWith("player light hunt replay: the report holds no player light hunt", PlayerLightHunt.Replay("sun: reading 9"), StringComparison.Ordinal);
    }

    /// <summary>FloatHunt.Part: a run of needles' sightings, caps and windows, numbered from nought - the others left out.</summary>
    [Fact]
    public void APARTOfASweepIsItsOwnNeedlesNumberedFromNought()
    {
        var result = new FloatHuntResult(3, 100, [new FloatSighting(0x10, 0), new FloatSighting(0x20, 2), new FloatSighting(0x30, 3)], [3], [0], false, TimeSpan.FromSeconds(1),
            [new FloatDump(0x10, 0, 0, [1]), new FloatDump(0x20, 2, 0x10, [2])]);
        FloatHuntResult part = FloatHunt.Part(result, 2, 2);
        Assert.Equal([new FloatSighting(0x20, 0), new FloatSighting(0x30, 1)], part.Sightings);
        Assert.Equal([1], part.Capped);
        Assert.Empty(part.Unsearched);
        FloatDump dump = Assert.Single(part.Dumps!);
        Assert.Equal(0, dump.Needle);
        Assert.Equal(100, part.BytesScanned);
    }

    /// <summary>
    /// A window holding a light as a renderer might keep it, the colour at the place found: its position and w (the cosine of the engine's
    /// penumbra), its way and w, then the colour and its radius.
    /// </summary>
    private static (byte[] Window, int Place) Light(Vector3 colour)
    {
        const int Place = PlayerLightHunt.Around;
        var window = new byte[(2 * PlayerLightHunt.Around) + 12];
        void Put(int at, float value) => BinaryPrimitives.WriteSingleLittleEndian(window.AsSpan(Place + at), value);
        Put(-32, Player.X);
        Put(-28, Player.Y);
        Put(-24, Ground - 150f);
        Put(-20, MathF.Cos(PlayerLightHunt.DefaultPenumbra));
        float lean = 20f * MathF.PI / 180f;
        Put(-16, MathF.Sin(lean) * 0.6f);
        Put(-12, MathF.Sin(lean) * 0.8f);
        Put(-8, MathF.Cos(lean));
        Put(-4, 0.6f);
        Put(0, colour.X);
        Put(4, colour.Y);
        Put(8, colour.Z);
        Put(12, 400f);
        return (window, Place);
    }
}
