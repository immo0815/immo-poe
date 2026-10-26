using System.Globalization;
using System.Text;
using PoEformance.Features;
using PoEformance.Game.Diagnostics;
using PoEformance.Game.Files;

namespace PoEformance.Core.Tests;

/// <summary>The wobble watch's arithmetic: the period it names, the turn it reads, and its file read back.</summary>
public class WobbleTests
{
    [Fact]
    public void APERIODIsFoundInASeriesReadAtUnevenTimes()
    {
        // THE SHIP'S MEASURED PERIOD (Port's gangway off a video, 2.05 s), its amplitude coming and going, read at uneven steps.
        var random = new Random(5);
        var times = new List<double>();
        var values = new List<float>();
        for (double at = 0; at < 20; at += 0.004 + (random.NextDouble() * 0.012))
        {
            times.Add(at);
            values.Add((float)((1 + (0.5 * Math.Sin(at * 0.7))) * Math.Sin(2 * Math.PI * at / 2.05)));
        }

        Assert.Equal(2.05, WobbleProbe.Period([.. times], [.. values]), 1);
    }

    [Fact]
    public void ADRIFTHasNoPeriod()
    {
        double[] times = [.. Enumerable.Range(0, 2000).Select(index => index * 0.01)];
        float[] values = [.. times.Select(at => (float)at)];
        Assert.Equal(0, WobbleProbe.Period(times, values));
    }

    [Fact]
    public void THETURNMeasuredInPortIsSeventySevenThousandthsOfADegree()
    {
        // THE ONE READ OF v0.1.289's CAPTURE, its bytes from +0x60 as the recording holds them.
        byte[] block = new byte[WobbleProbe.PointBytes];
        ulong[] quads = [0x1, 0xB52F71573F7FFFFE, 0xBA08F9E2, 0x3F7FFFF300000000, 0xBAA3F221, 0x3AA3F21B3A08F9EA, 0x3F7FFFF1, 0x3ED900003E300000];
        for (var index = 0; index < quads.Length; index++)
        {
            BitConverter.TryWriteBytes(block.AsSpan(0x60 + (index * 8)), quads[index]);
        }

        (double degrees, double x, double y, double z) = WobbleSeries.TurnOf(block);
        Assert.Equal(0.0777, degrees, 4);

        // ABOUT AN AXIS LYING IN THE GROUND'S PLANE: a tilt, not a turn about the vertical.
        Assert.True(Math.Abs(z) < 0.01, $"axis {x} {y} {z}");
    }

    [Fact]
    public void THEFILEIsReadBackWithItsFirstReadUnderTheChangedFields()
    {
        // THE PROBE'S OWN FORMAT: the point's heading, its WobblePoint's first read, then the table of the fields that changed.
        var text = new StringBuilder();
        text.AppendLine("wobble: 1 wobble points");
        text.AppendLine("=== wobble point 42 Metadata/Wobble at 0 0 0, model x.ao, entity 0x10 - 1 blocks");
        text.AppendLine("first read of every block:");
        text.AppendLine("  -- WobblePoint 0x100");
        text.AppendLine("    +060  0000000000000001 000000003F800000  . . 1 .");
        text.AppendLine("    +070  0000000000000000 3F80000000000000  . . . 1");
        text.AppendLine("    +080  0000000000000000 0000000000000000  . . . .");
        text.AppendLine("    +090  000000003F800000 0000000000000000  1 . . .");
        text.AppendLine("=== doodad 43 Metadata/MiscellaneousObjects/Doodad");
        text.AppendLine("  -- WobblePoint 0x999");
        text.AppendLine("    +060  0000000000000009 0000000000000009  . . . .");
        text.AppendLine();
        text.AppendLine("=== WobblePoint of 42 over time: 3 reads that changed it, every changed field a column, a row every 0.02 s");
        text.AppendLine("t\t+0x68\t+0x70\t+0x88\t+0x90");
        foreach (double at in (double[])[0, 0.5, 1])
        {
            float tilt = (float)Math.Sin(at), upright = (float)Math.Cos(at);
            text.AppendLine(string.Create(CultureInfo.InvariantCulture, $"{at:0.000}\t{upright}\t{-tilt}\t{tilt}\t{upright}"));
        }

        IReadOnlyList<WobbleSeries.Point> points = WobbleSeries.Read(text.ToString());
        WobbleSeries.Point point = Assert.Single(points);
        Assert.Equal("42", point.Name);
        Assert.Equal([0x68, 0x70, 0x88, 0x90], point.Fields);
        Assert.Equal(3, point.Rows.Count);

        // THE ROW AT ONE SECOND: the first read's one in the middle of the diagonal, the changed fields round it - a radian about y.
        (double degrees, double x, double y, double z) = WobbleSeries.TurnOf(point.At(2));
        Assert.Equal(180 / Math.PI, degrees, 3);
        Assert.Equal((0, -1, 0), (Math.Round(x, 3), Math.Round(y, 3), Math.Round(z, 3)));
        Assert.StartsWith("wobble replay: 1 wobble points", WobbleSeries.Report(points), StringComparison.Ordinal);
    }
}

/// <summary>Port's ship's sway as the files and the game write it - tests/fixtures/wobble, from v0.1.290's capture.</summary>
public class PortSwayTests
{
    private static string Folder => ComponentSweepTests.Fixture("wobble");

    /// <summary>The install as far as the fixture carries it: the object types and the .ao, by path.</summary>
    private static byte[]? Read(string path)
    {
        string file = Path.Combine(Folder, "files", path.Replace('\\', '/').Trim());
        return File.Exists(file) ? File.ReadAllBytes(file) : null;
    }

    private static RoomLayout Room => RoomLayout.Read(File.ReadAllBytes(Path.Combine(Folder, "Boss_01.arm")));

    [Fact]
    public void THEBOSSROOMNamesItsZonesTheShipsAmongThem()
    {
        RoomLayout room = Room;
        Assert.True(room.Ready, room.Why);
        RoomZone caravan = Assert.Single(room.Zones, zone => zone.Name == "caravan");
        Assert.Equal((26, -102, 261, 454), (caravan.LeastX, caravan.LeastY, caravan.MostX, caravan.MostY));
        Assert.Equal("Metadata/MiscellaneousObjects/SectorDisableInstantTeleports", Assert.Single(room.Zones, zone => zone.Name == "hide_teleports").Type);
        Assert.Equal(4, room.Zones.Count(zone => zone.Name == "no_movement_skills"));

        // THE ZONES DO NOT COST THE GROUND OVERRIDES after them.
        Assert.Equal(string.Empty, room.OverridesWhy);
    }

    [Fact]
    public void THECORESTYPENamesItsBoneSectorAndRig()
    {
        (string bone, string sector, string ao) = WobbleRig.Point(Read, "Metadata/Terrain/Gallows/Act4/4_6/Objects/BoatWobbleCore")
            ?? throw new InvalidOperationException("no WobblePoint read");
        Assert.Equal(("caravan_jntBnd", "caravan"), (bone, sector));
        Assert.Equal("Metadata/Terrain/Doodads/Islands/Boatnaut/BoatWobbleCores/BoatnautWobbleCore_small.ao", ao);
        Assert.Null(WobbleRig.Point(Read, "Metadata/MiscellaneousObjects/Doodad"));
    }

    [Fact]
    public void ADOODADSWAYSUnlessItsTypeSetsNoWobble()
    {
        Assert.True(WobbleRig.Sways(Read, "Metadata/MiscellaneousObjects/Doodad"));
        Assert.False(WobbleRig.Sways(Read, "Metadata/MiscellaneousObjects/DoodadNoWobble"));
    }

    [Fact]
    public void THESHIPANDITSDeckSwayAndNothingOutsideTheZone()
    {
        RoomLayout room = Room;
        RoomSways sways = RoomSways.Of(room, Read, null) ?? throw new InvalidOperationException("no core found");
        Assert.Equal(1, sways.Count);

        // NO RIG IN THE FIXTURE, so the core plays nothing - and still claims its sector.
        Assert.Null(sways.RigOf(0));
        Assert.Contains("not played", sways.Said, StringComparison.Ordinal);

        RoomDoodad ship = Assert.Single(room.Doodads, one => one.Ao.EndsWith("boss_boat.ao", StringComparison.Ordinal));
        RoomDoodad core = Assert.Single(room.Doodads, one => one.Ao.EndsWith("BoatnautWobbleCore_small.ao", StringComparison.Ordinal));
        Assert.Equal(0, sways.GroupOf(ship));
        Assert.Equal(0, sways.CoreOf(core));
        Assert.Equal(-1, sways.GroupOf(core));

        RoomZone caravan = room.Zones.First(zone => zone.Name == "caravan");
        foreach (RoomDoodad one in room.Doodads)
        {
            if (!caravan.Holds(one.X, one.Y))
            {
                Assert.Equal(-1, sways.GroupOf(one));
            }
        }

        Assert.True(room.Doodads.Count(one => sways.GroupOf(one) == 0) > 50);
    }

    [Fact]
    public void THEGAMESSWAYIsATurnAboutOnePoint()
    {
        // THE WATCH (v0.1.290's wobble.txt, Port): every row's matrix leaves one point where it is, to half a unit, against
        // translations up to 81 - the turn's fixed point, at the core's ground (its Render TerrainHeight -328.125).
        WobbleSeries.Point point = Assert.Single(WobbleSeries.Read(File.ReadAllText(Path.Combine(Folder, "watch.txt"))));
        (System.Numerics.Vector3 at, double residual, double largest) = WobbleSeries.FixedPoint(point) ?? throw new InvalidOperationException("no fit");
        Assert.InRange(at.X, 4150, 4175);
        Assert.InRange(at.Y, 12465, 12480);
        Assert.InRange(at.Z, -330, -326);
        Assert.True(residual < 1, $"residual {residual}");
        Assert.True(largest > 80, $"largest {largest}");
    }
}
