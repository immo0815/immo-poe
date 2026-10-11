using System.Globalization;
using System.Text.RegularExpressions;

namespace PoEformance.Core.Tests;

/// <summary>
/// The room rules held against the captures committed under tests/fixtures/captures - the replays of
/// CaptureReplayTests, run over real areas on every build instead of only when somebody sets POEF_CAPTURE.
/// </summary>
/// <remarks>
/// WHAT IS PINNED IS THE RULE, NOT THE COUNT. The doodads place more rooms, or fewer, as RoomDoodadFinder
/// changes, and every count below moves with them; what must not move is which reading the rooms agree
/// with. So each check is that the rule's reading leads, and by how much where a margin is the point.
///
/// THE CAPTURES (v0.1.179, pictures left out): Oasis, where the orientation and origin rules were first
/// read; Steppe, the forced_blank question and the weights 1, 20 and 300; Sulphuric Caverns, whose boss
/// room lays tile files no list names (see CaptureReport.TileFiles); Steaming Springs, the most weight
/// samples and fifteen placed rooms. tests/fixtures/captures/README.md says more.
/// </remarks>
public partial class CaptureFixtureTests
{
    private static string Capture(string name) => ComponentSweepTests.Fixture(Path.Combine("captures", name));

    /// <summary>The orientation rule (RoomFinder.Laying against TileOrientation.Turn, both as written) leads every other pair.</summary>
    [Theory]
    [InlineData("oasis")]
    [InlineData("steaming-springs")]
    public void THEORIENTATIONAsWrittenLeadsEveryOtherReading(string name)
    {
        var margins = new Dictionary<(int Room, int Tile), int>();
        foreach (Match pair in Pair().Matches(CaptureReplayTests.OrientationReplay(Capture(name))))
        {
            int room = int.Parse(pair.Groups[1].Value, CultureInfo.InvariantCulture);
            int tile = int.Parse(pair.Groups[2].Value, CultureInfo.InvariantCulture);
            margins[(room, tile)] = int.Parse(pair.Groups[3].Value, CultureInfo.InvariantCulture) - int.Parse(pair.Groups[4].Value, CultureInfo.InvariantCulture);
        }

        // THE BEST TWELVE of the 64 pairs are printed, best first - the rule has to be among them to lead at all.
        Assert.True(margins.Count >= 3, $"only {margins.Count} pairs printed");
        Assert.True(margins.ContainsKey((0, 0)), "the rule is not among the best pairs");

        // Room way 2 with tile way 2 is the same reading turned half way round both sides, so it ties by construction.
        Assert.Equal(margins.Values.Max(), margins[(0, 0)]);
        Assert.All(margins.Where(one => one.Key is not ((0, 0) or (2, 2))), one => Assert.True(one.Value < margins[(0, 0)], $"{one.Key} reads as well as the rule"));
    }

    /// <summary>
    /// The origin rule (RoomSlot.Corner): a big slot's record sits at the corner its 24th number names, for most records
    /// and for every number the most often.
    /// </summary>
    /// <remarks>
    /// MOST, NOT ALL: the readings that miss stand inside their footprint rather than at another corner - a room placed a
    /// tile off - and their share is what the margin allows (Oasis 2 of 170, Steaming Springs 11 of 345, Sulphuric Caverns 7 of 63).
    /// </remarks>
    [Theory]
    [InlineData("oasis")]
    [InlineData("steaming-springs")]
    [InlineData("sulphuric-caverns")]
    public void THEORIGINRuleNamesTheRecordsCorner(string name)
    {
        string[] rule = ["left-first", "right-first", "right-last", "left-last"];
        var readings = new Dictionary<int, Dictionary<string, int>>();
        int follow = 0, all = 0;
        foreach (Match record in Origin().Matches(CaptureReplayTests.OriginReplay(Capture(name))))
        {
            int origin = int.Parse(record.Groups[1].Value, CultureInfo.InvariantCulture);
            string reading = record.Groups[2].Value;
            Dictionary<string, int> of = readings.TryGetValue(origin, out Dictionary<string, int>? known) ? known : readings[origin] = [];
            of[reading] = of.GetValueOrDefault(reading) + 1;
            follow += reading == rule[origin] ? 1 : 0;
            all++;
        }

        Assert.True(all > 50, $"only {all} records read");
        Assert.True(follow >= all * 0.85, $"{follow} of {all} records follow the rule");
        Assert.All(readings, one => Assert.Equal(rule[one.Key], one.Value.MaxBy(reading => reading.Value).Key));
    }

    /// <summary>A slot one tile answers comes out the game's file, in an area whose laid files the lists name.</summary>
    /// <remarks>
    /// STEAMING SPRINGS ALONE: Sulphuric Caverns' boss room lays files no list names (Thickwall/Fill_01 for the
    /// list's Murky/Thickwall/Fill_01, and a Fill_02), which a build from the list cannot come out as - the open
    /// question CaptureReport.TileFiles was added to answer - and Oasis' boss room lays one MudBorder_Cc_01 where
    /// its slot asks for a straight.
    /// </remarks>
    [Fact]
    public void THESETTLEDSlotsComeOutTheGamesFile()
    {
        Match settled = Settled().Match(CaptureReplayTests.BuildReplay(Capture("steaming-springs")));
        Assert.True(settled.Success);
        Assert.True(int.Parse(settled.Groups[1].Value, CultureInfo.InvariantCulture) > 0);
        Assert.Equal(settled.Groups[1].Value, settled.Groups[2].Value);
    }

    [GeneratedRegex(@"room way (\d), tile way (\d): edges (\d+) agree, (\d+) disagree")]
    private static partial Regex Pair();

    [GeneratedRegex(@"origin ([0-3]): piece [^\n]*?; record is ([a-z]+-[a-z]+)")]
    private static partial Regex Origin();

    [GeneratedRegex(@"(\d+) settled by one tile \((\d+) the game's file\)")]
    private static partial Regex Settled();
}
