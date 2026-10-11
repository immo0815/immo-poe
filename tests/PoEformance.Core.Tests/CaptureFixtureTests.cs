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
/// THE CAPTURES (pictures left out): Oasis, where the orientation and origin rules were first read; Steppe,
/// the forced_blank question and the weights 1, 20 and 300; Steaming Springs, the most weight samples and
/// fifteen placed rooms - those three v0.1.179; Marrow (v0.1.186), the fill lists, the words and the wall
/// tops; Sulphuric Caverns (v0.1.188), the fill list's tiles in its boss room and the tiles above level
/// nought that settled a tile doodad's height.
/// tests/fixtures/captures/README.md says more.
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
    /// tile off - and their share is what the margin allows (Oasis 2 of 170, Steaming Springs 11 of 345, Sulphuric Caverns 7 of 61).
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
    /// STEAMING SPRINGS ALONE: Sulphuric Caverns' boss room lays its fill list's BlackInsideWall tile
    /// (Thickwall/Fill_01) in all fifteen slots that ask for that ground all round, where a build from the tile list
    /// takes Murky/Thickwall/Fill_01 - a file laid nowhere in the area - and Oasis' boss room lays one
    /// MudBorder_Cc_01 where its slot asks for a straight.
    /// </remarks>
    [Fact]
    public void THESETTLEDSlotsComeOutTheGamesFile()
    {
        Match settled = Settled().Match(CaptureReplayTests.BuildReplay(Capture("steaming-springs")));
        Assert.True(settled.Success);
        Assert.True(int.Parse(settled.Groups[1].Value, CultureInfo.InvariantCulture) > 0);
        Assert.Equal(settled.Groups[1].Value, settled.Groups[2].Value);
    }

    /// <summary>
    /// Marrow's laid files no tile list names all come from its master's fill list: the plain Cavepassage tile and the four
    /// mine_abyss_fill tiles of its Rooms/Fills folder, every one in random_fill_tiles.gft.
    /// </summary>
    /// <remarks>
    /// THE COUNT USED TO BE THE OPEN QUESTION: the v0.1.183 capture this one replaced left four of the five unexplained,
    /// for want of area-masters.txt (v0.1.184). Read through the master's FillTiles, none is - and the fill list is a road
    /// the build has to take as well as the tile lists.
    /// </remarks>
    [Fact]
    public void THETILEFILESOfMarrowComeFromItsFillList()
    {
        string replay = CaptureReplayTests.TileFilesReplay(Capture("marrow"));
        Assert.StartsWith("tile files: 5 laid files no tile list names: 0 reached by inheriting, 5 in a fill list, 0 sharing a template with a listed file, 0 not explained", replay, StringComparison.Ordinal);
        Assert.Contains("fill list metadata/terrain/maps/marrow/random_fill_tiles.gft: version 2, 4 sections, 7 tiles (6 laid), 0 rooms", replay, StringComparison.Ordinal);
        Assert.Contains("mine_abyss_fill_v01_01: in a fill list - Metadata/Terrain/Maps/Marrow/Rooms/Fills/mine_abyss_fill_v01_01.tdt", replay, StringComparison.Ordinal);
    }

    /// <summary>Marrow's words are read off its tile list - four files, the cave entrance twice - and none of those files was laid in that instance.</summary>
    /// <remarks>
    /// THE OPEN HALF AGAIN: what a word does to a tile's placement needs a laid worded tile, which Marrow's instance did not
    /// have. The first capture that has one is a fixture worth adding, and this count is what it will move.
    /// </remarks>
    [Fact]
    public void THEWORDSOfMarrowAreReadButNotLaid()
    {
        string replay = CaptureReplayTests.WordsReplay(Capture("marrow"));
        Assert.StartsWith("words: 4 tile files carry words in the area's lists", replay, StringComparison.Ordinal);
        Assert.Contains("0 of them laid, 0 times", replay, StringComparison.Ordinal);
        Assert.Contains("Metadata/Terrain/Desert/CavePath/Features/cave_entrance_01.tdt: I (tile list); R180 (tile list)", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// How a tile's doodads reach the world, held against Marrow's entities: the tile's frame as authored, then the game's
    /// placement; a wall top only where it faces the camera's side (TileDoodadList.Shown), without one exception; and a
    /// line's z the entity's own ground, exactly.
    /// </summary>
    /// <remarks>
    /// THE WALL TOPS ARE WHAT TELL THE READINGS APART: a doodad in a tile's middle lands there under every way the tile can
    /// lie, while a top stands in the middle of one PIECE of a big tile, so only the right reading finds it. Way 3 is the
    /// tile as authored, the same matrix whichever side of the game's turn it is put, so its two readings tie by construction.
    ///
    /// THE HEIGHT IS THE ENTITY'S GROUND (Render's TerrainHeight), NOT ITS Z: a top's z is its ground less something of its
    /// model, a different amount for each model at one place. Every tile under a found doodad is at level nought in Marrow,
    /// so whether a line's z counts from the tile's level or from the area's nought this capture cannot say.
    /// </remarks>
    [Fact]
    public void THETILEDOODADSOfMarrowStandAsAuthoredAndFaceTheCamera()
    {
        string replay = CaptureReplayTests.TileDoodadsReplay(Capture("marrow"));
        Assert.StartsWith("tile doodads: 152 tile files with a doodad list, 833 doodad lines", replay, StringComparison.Ordinal);

        var tops = new Dictionary<(int Way, string Turn), int>();
        foreach (Match reading in WallTops().Matches(replay))
        {
            tops[(int.Parse(reading.Groups[1].Value, CultureInfo.InvariantCulture), reading.Groups[2].Value)] = int.Parse(reading.Groups[3].Value, CultureInfo.InvariantCulture);
        }

        Assert.Equal(16, tops.Count);
        Assert.Equal(tops[(3, "after")], tops[(3, "before")]);
        Assert.All(tops.Where(one => one.Key.Way != 3), one => Assert.True(one.Value < tops[(3, "after")], $"way {one.Key.Way}, turn {one.Key.Turn} finds {one.Value} wall tops"));

        Match rule = Rule().Match(replay);
        Assert.True(rule.Success);
        Assert.True(int.Parse(rule.Groups[2].Value, CultureInfo.InvariantCulture) > 400);
        Assert.Equal(rule.Groups[2].Value, rule.Groups[1].Value);

        MatchCollection grounds = Ground().Matches(replay);
        Assert.True(grounds.Count >= 2);
        Assert.All(grounds, ground => Assert.True(ground.Groups[1].Value.Length == 0, "a line's z is not its entity's ground: " + ground.Value));
    }

    /// <summary>
    /// Sulphuric Caverns' one laid file no tile list names is in its master's fill list - under the section of the
    /// ground it fills, BlackInsideWall.
    /// </summary>
    [Fact]
    public void THETILEFILESOfSulphuricComeFromItsFillList()
    {
        string replay = CaptureReplayTests.TileFilesReplay(Capture("sulphuric-caverns"));
        Assert.StartsWith("tile files: 1 laid files no tile list names: 0 reached by inheriting, 1 in a fill list, 0 sharing a template with a listed file, 0 not explained", replay, StringComparison.Ordinal);
        Assert.Contains("fill list metadata/terrain/maps/sulphuriccaverns/random_fill_tiles.gft: version 2, 3 sections, 4 tiles (3 laid)", replay, StringComparison.Ordinal);
        Assert.Contains("Fill_01: in a fill list - Metadata/Terrain/Islands/Tiles/CoralDungeon/Thickwall/Fill_01.tdt", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// A tile doodad's z counts from its tile's level: Sulphuric Caverns lays tiles at -203, and every doodad found there
    /// stands at its line's z plus that level - as every one on a tile at nought stands at its line's z - while the tile's
    /// frame, as authored then the game's placement, finds every doodad it puts down.
    /// </summary>
    /// <remarks>
    /// THE OPEN HALF OF MARROW'S MEASUREMENT, where every tile under a found doodad was at nought and the two readings
    /// could not be told apart. Pinned as a rule over every type found, and with a level other than nought among them, so
    /// that a capture without one cannot pass it by agreeing trivially.
    /// </remarks>
    [Fact]
    public void THETILEDOODADSOfSulphuricCountTheirHeightFromTheTilesLevel()
    {
        string replay = CaptureReplayTests.TileDoodadsReplay(Capture("sulphuric-caverns"));

        var readings = new Dictionary<(int Way, string Turn), (int Found, int Put)>();
        foreach (Match reading in Readings().Matches(replay))
        {
            readings[(int.Parse(reading.Groups[1].Value, CultureInfo.InvariantCulture), reading.Groups[2].Value)] =
                (int.Parse(reading.Groups[3].Value, CultureInfo.InvariantCulture), int.Parse(reading.Groups[4].Value, CultureInfo.InvariantCulture));
        }

        Assert.Equal(16, readings.Count);
        (int found, int put) = readings[(3, "after")];
        Assert.True(put > 300, $"only {put} doodads put");
        Assert.Equal(put, found);
        Assert.All(readings.Where(one => one.Key.Way != 3), one => Assert.True(one.Value.Found < found / 2, $"way {one.Key.Way}, turn {one.Key.Turn} finds {one.Value.Found}"));

        Assert.Matches(Raised(), replay);
        MatchCollection levels = Level().Matches(replay);
        Assert.True(levels.Count >= 3);
        Assert.All(levels, level => Assert.True(level.Groups[1].Value.Length == 0, "a doodad does not stand at its line's z above its tile's level: " + level.Value));
    }

    [GeneratedRegex(@"room way (\d), tile way (\d): edges (\d+) agree, (\d+) disagree")]
    private static partial Regex Pair();

    [GeneratedRegex(@"origin ([0-3]): piece [^\n]*?; record is ([a-z]+-[a-z]+)")]
    private static partial Regex Origin();

    [GeneratedRegex(@"(\d+) settled by one tile \((\d+) the game's file\)")]
    private static partial Regex Settled();

    [GeneratedRegex(@"way (\d) \([^)]*\), the game's turn (after|before): [^\n]*TileUpperDoodad (\d+)/\d+")]
    private static partial Regex WallTops();

    [GeneratedRegex(@"the rule \(TileDoodadList\.Shown\): (\d+) of (\d+) wall-top lines")]
    private static partial Regex Rule();

    [GeneratedRegex(@"entity ground - line z: -?0 x\d+([^\r\n]*)")]
    private static partial Regex Ground();

    [GeneratedRegex(@"way (\d) \([^)]*\), the game's turn (after|before): (\d+) / \d+ / \d+ of (\d+)")]
    private static partial Regex Readings();

    [GeneratedRegex(@"entity ground - line z: [^\r\n]*-203 x\d+")]
    private static partial Regex Raised();

    [GeneratedRegex(@"entity ground - line z - the tile's level: -?0 x\d+([^\r\n]*)")]
    private static partial Regex Level();
}
