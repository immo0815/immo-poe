using System.Globalization;
using System.Text.RegularExpressions;
using PoEformance.Core.Memory;
using PoEformance.Features;
using PoEformance.Game;
using PoEformance.Game.World;

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
/// nought that settled a tile doodad's height; Augury (v0.1.188), nineteen rooms placed by their doodads, the
/// fill list's tile in hundreds of slots and the first laid tile carrying a word; Traitor's Passage and the Titan Grotto (v0.1.213),
/// the terrain's second grid against walls and chasms.
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
    /// Built from the tile list and the master's fill list, Sulphuric Caverns' boss room comes out the game's file in every
    /// slot one tile settles - the fifteen asking BlackInsideWall all round from the fill list among them.
    /// </summary>
    /// <remarks>
    /// THE FILL LIST'S RULE (RoomBuilder): before it, those fifteen took the tile list's Murky/Thickwall/Fill_01, a file the
    /// area lays nowhere, and 24 of the 39 settled slots came out the game's file.
    /// </remarks>
    [Fact]
    public void THEFILLListFillsSulphuricsBossRoomAsTheGameDid()
    {
        string replay = CaptureReplayTests.BuildReplay(Capture("sulphuric-caverns"));
        Match settled = Settled().Match(replay);
        Assert.True(settled.Success);
        Assert.Equal("39", settled.Groups[1].Value);
        Assert.Equal(settled.Groups[1].Value, settled.Groups[2].Value);
        Assert.Contains("15 filled from the master's fill list (15 the game's file)", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// Augury's slots asking BlackInsideWall all round, which no tile of its list answers, are filled from its fill list - and
    /// most come out the game's own file, black.tdt.
    /// </summary>
    /// <remarks>
    /// MOST, NOT ALL: of 703 such slots over nineteen rooms, 571 are the game's file. The rest lie in rooms the doodads place
    /// at several places, wrongly at some, or on a rim a neighbouring room's walls cover - a room laid once matched 327 of
    /// its 351. Without the fill list 707 slots of the build had no tile at all.
    /// </remarks>
    [Fact]
    public void THEFILLListFillsAugurysWallsWhereNoListedTileAnswers()
    {
        string replay = CaptureReplayTests.BuildReplay(Capture("augury"));
        Match filled = Filled().Match(replay);
        Assert.True(filled.Success);
        int all = int.Parse(filled.Groups[1].Value, CultureInfo.InvariantCulture), game = int.Parse(filled.Groups[2].Value, CultureInfo.InvariantCulture);
        Assert.True(all > 500, $"only {all} slots filled from the fill list");
        Assert.True(game >= all * 0.75, $"{game} of {all} the game's file");

        Match built = Built().Match(replay);
        Assert.True(built.Success);
        Assert.True(int.Parse(built.Groups[2].Value, CultureInfo.InvariantCulture) * 100 < int.Parse(built.Groups[1].Value, CultureInfo.InvariantCulture), "more than one slot in a hundred with no tile alike");
    }

    /// <summary>
    /// The first laid tile carrying a word: Augury's arena, written "I" in its list, lay as authored in a room laid as
    /// written.
    /// </summary>
    /// <remarks>
    /// ONE SAMPLE, PINNED AS ONE: "I" as identity fits it, and so would a word that says nothing of the way round. It is what
    /// the next laid worded tile - turned, or in a turned room - is held against.
    /// </remarks>
    [Fact]
    public void THEWORDSOfAuguryLayItsArenaAsAuthored()
    {
        string replay = CaptureReplayTests.WordsReplay(Capture("augury"));
        Assert.Contains("Metadata/Terrain/Jungle/SnakePit/MapAugury/AuguryArena.tdt: I (tile list)", replay, StringComparison.Ordinal);
        Assert.Contains("placement 3 - boss_01 slot 4, 4, room as written: in the room 3", replay, StringComparison.Ordinal);
    }

    /// <summary>
    /// A map's boss stands on the point of interest his room tags for him, at the middle of its cell - what a stage sets him on
    /// (LaidRoomModels.BossSpotOf).
    /// </summary>
    /// <remarks>
    /// THREE BOSSES NOT YET FOUGHT, each where the recording holds him: Marrow's on "force:mapboss", Steaming Springs' on
    /// "mapboss", and Sulphuric Caverns' second boss on "mapboss2" - its first, on "mapboss1", 58 units off, had moved. At the
    /// cell's corner every one was 8 units off, the distance to its middle.
    /// </remarks>
    [Theory]
    [InlineData("marrow", "force:mapboss")]
    [InlineData("steaming-springs", "mapboss")]
    [InlineData("sulphuric-caverns", "mapboss2")]
    public void THEBOSSStandsWhereHisRoomNamesHisPlace(string name, string tag)
    {
        string replay = CaptureReplayTests.BossPointsReplay(Capture(name));
        string? line = replay.Split('\n').Select(one => one.TrimEnd('\r')).FirstOrDefault(one => one.StartsWith("  \"" + tag + "\"", StringComparison.Ordinal));
        Assert.NotNull(line);
        int nearest = line.IndexOf("nearest: ", StringComparison.Ordinal);
        Assert.True(nearest > 0, line);
        string[] words = line[(nearest + 9)..].Split(' ');
        Assert.True(words.Length > 2 && words[2].StartsWith("off", StringComparison.Ordinal), line);
        Assert.True(int.Parse(words[1], CultureInfo.InvariantCulture) <= 2, line);
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

    /// <summary>
    /// Every room is its module's overlap plus whole steps each way, and where the capture carries its master, every room of the master's
    /// module the doodads or tiles place stands on the master's lattice at its strongest - from the buffer, every side less the overlap.
    /// </summary>
    /// <remarks>
    /// PLACED WITHOUT THE LATTICE, so it is held against places it had no hand in (CaptureReplayTests.LatticeReplay). Augury's places off
    /// it were the weaker of two places of one room, and stood 25 to 97 units from their lines where every place on it stood within 10 -
    /// pinned as the rule that every doodad place off the lattice stands further from its lines than every one on it.
    /// </remarks>
    [Theory]
    [InlineData("augury")]
    [InlineData("marrow")]
    [InlineData("sulphuric-caverns")]
    public void THELATTICEHoldsEveryRoomOfTheMastersModule(string name)
    {
        string replay = CaptureReplayTests.LatticeReplay(Capture(name));
        Assert.StartsWith("lattice: 1 masters in the capture name 1 lattices", replay, StringComparison.Ordinal);
        Assert.Matches(@"across \d+ = [^\n]*, whole, down \d+ = [^\n]*, whole", replay);
        Assert.Matches(@"rooms: \d+, \d+ of them the overlap plus whole steps of their module each way \(\d+ of a module no master here names\), 0 not", replay);

        Match held = LatticeHeld().Match(replay);
        Assert.True(held.Success);
        Assert.True(int.Parse(held.Groups[1].Value, CultureInfo.InvariantCulture) > 0);
        Assert.Equal(held.Groups[1].Value, held.Groups[2].Value);
        Assert.Equal("0", held.Groups[3].Value);

        var on = new List<int>();
        var off = new List<int>();
        foreach (Match place in LatticePlace().Matches(replay))
        {
            (place.Groups[2].Value == "on" ? on : off).Add(int.Parse(place.Groups[1].Value, CultureInfo.InvariantCulture));
        }

        Assert.All(off, units => Assert.True(units > on.DefaultIfEmpty(0).Max(), $"a place off the lattice stands {units} units from its lines, no further than one on it"));
    }

    /// <summary>The captures without a master: every room is still its own module's overlap plus whole steps each way.</summary>
    [Theory]
    [InlineData("oasis")]
    [InlineData("steppe")]
    [InlineData("steaming-springs")]
    public void EVERYRoomIsItsModulesOverlapPlusWholeSteps(string name)
        => Assert.Matches(@"rooms: \d+, \d+ of them the overlap plus whole steps of their module each way \(\d+ of a module no master here names\), 0 not", CaptureReplayTests.LatticeReplay(Capture(name)));

    /// <summary>
    /// Where two of Augury's placed rooms lay their rims on one row and both slots name dungeon_thick_wall on a side, the tile the game
    /// laid has what the master's edge combinations make of the two - nothing (an opening) or dungeon_stairs - on nine sides in ten,
    /// and both of the two lines are taken.
    /// </summary>
    /// <remarks>
    /// THE TALK'S "WALL + WALL = NO WALL", measured (CaptureReplayTests.JoinsReplay): 168 of 184 such sides, 84 open and 84 stairs, a
    /// join mostly one or the other whole. The rest: the boss room's joins with the lever rooms keep the thick wall (12 sides), and one
    /// join lays dungeon_ledge (4). What picks the opening or the stairs is not in either slot's numbers - every such side carries the
    /// pair 1/3 - nor in the corner heights, all nought; the file's own number (none for the opening, 1 for the stairs) is unread.
    /// </remarks>
    [Fact]
    public void THEJOINSOfAuguryAreWhatItsEdgeCombinationsMakeOfTheTwoRims()
    {
        string replay = CaptureReplayTests.JoinsReplay(Capture("augury"));
        Match joins = Joins().Match(replay);
        Assert.True(joins.Success);
        int named = int.Parse(joins.Groups[1].Value, CultureInfo.InvariantCulture), held = int.Parse(joins.Groups[2].Value, CultureInfo.InvariantCulture);
        Assert.True(named > 100, $"only {named} sides met a pair the edge combinations name");
        Assert.True(held * 10 >= named * 9, $"{held} of {named} sides laid what the edge combinations make");
        Assert.Matches(@"dungeon_thick_wall \+ dungeon_thick_wall: [^\n]*- x\d+[^\n]*\.ecf makes - or dungeon_stairs 1", replay);
        Assert.Matches(@"dungeon_thick_wall \+ dungeon_thick_wall: [^\n]*dungeon_stairs x\d+", replay);
    }

    /// <summary>
    /// Augury's overlays both say +SuppressUnderlyingDoodads, and every prop line of a placed room whose entity is missing stands where
    /// one of them fits the laid tiles - none anywhere else.
    /// </summary>
    /// <remarks>
    /// MEASURED, NOT BUILT ON (CaptureReplayTests.OverlaysReplay): 32 missing lines, every one under a place an overlay's every slot fits,
    /// 879 present and none missing under none; 24 of the 32 on a tile that is not what the room's own slot asks, where 2 of the 946
    /// present ones are. Excusing those lines from a room's two thirds would place none of the thirteen rooms the doodads leave without a
    /// place - their props are missing everywhere, not under an overlay - so the placing is left as it is.
    /// </remarks>
    [Fact]
    public void THEOVERLAYSOfAugurySuppressThePropsBeneathThem()
    {
        string replay = CaptureReplayTests.OverlaysReplay(Capture("augury"));
        Assert.Contains("boss_entrance_blank limit=1 !door_boss +OverlayFeatureRooms +OverlayFeatureTiles +SuppressUnderlyingDoodads", replay, StringComparison.Ordinal);
        Match under = Suppressed().Match(replay);
        Assert.True(under.Success);
        Assert.True(int.Parse(under.Groups[1].Value, CultureInfo.InvariantCulture) >= 20, "too few missing lines under a suppressing overlay to say anything");
        Assert.Matches(@"under no overlay: \d{3,} present, 0 missing", replay);
    }

    /// <summary>
    /// The terrain's second cell grid (GridLandscapeData) is where projectiles stop, read back out of each capture's recording: nought on
    /// a wall, five over a chasm though both are unwalkable, nought on no cell anybody can stand on - and like the walkable grid it
    /// counts up from its noughts to a cap of five.
    /// </summary>
    /// <remarks>
    /// THE CAPTURES ASKED FOR IT (v0.1.213, both Act 2, both reached by a waypoint): Traitor's Passage, built from the cave tileset whose
    /// edge combinations name thick walls and abyss edges, and the Titan Grotto, walkways over an abyss. Along the edge of where anybody
    /// can stand, every cell of black_inside_wall is nought (2875 of 2875) and every cell of cavern_abyss five (395 of 395); in the Grotto
    /// every abyss cell there is five (3413 of 3413), and of its 7.6 million unwalkable cells 18474 are nought. A walkable cell is never
    /// nought in either. A cell of one has a nought among its eight neighbours, in both grids - every one but a single cell of 36335 in
    /// Traitor's Passage's second grid - a distance from the noughts by steps of one; whether a diagonal step counts as one is not
    /// settled by these counts.
    /// </remarks>
    [Theory]
    [InlineData("traitors-passage", "black_inside_wall", "cavern_abyss")]
    [InlineData("titan-grotto", "", "abyss")]
    public void THESECONDGridIsWhereProjectilesStop(string name, string wall, string chasm)
    {
        string replay = CaptureReplayTests.LandscapeReplay(Capture(name));
        Assert.StartsWith("landscape: walkable grid ", replay, StringComparison.Ordinal);
        Assert.DoesNotContain("not compared", replay, StringComparison.Ordinal);

        // NOUGHT ON NO WALKABLE CELL: the column under "landscape 0" is nought on every line from walkable 1 to 5.
        string[] lines = replay.Split('\n');
        int header = Array.FindIndex(lines, line => line.TrimStart().StartsWith("walkable ", StringComparison.Ordinal) && line.Contains("landscape 0", StringComparison.Ordinal));
        Assert.True(header >= 0, "no table of the two grids");
        for (int at = header + 1; at < lines.Length && lines[at].Trim().Length > 0; at++)
        {
            string[] cells = lines[at].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (cells[0] != "0")
            {
                Assert.Equal("0", cells[1]);
            }
        }

        // ALONG THE EDGE OF WHERE ANYBODY CAN STAND: the wall all nought, the chasm all five.
        string edges = replay[replay.IndexOf("=== the edge of where anybody can stand", StringComparison.Ordinal)..];
        Assert.Matches(@"\n  " + chasm + @": 5 x\d+\r?\n", edges);
        if (wall.Length > 0)
        {
            Assert.Matches(@"\n  " + wall + @": 0 x\d+\r?\n", edges);
        }

        // A DISTANCE FROM ITS NOUGHTS, in both grids: a one has a nought among its eight neighbours - all but one cell in a thousand.
        foreach (string grid in (string[])["walkable", "landscape"])
        {
            Match ones = Regex.Match(replay, grid + @" 1: sides \d+ one less, \d+ no way down, \d+ a jump; all eight (\d+) one less, (\d+) no way down");
            Assert.True(ones.Success, grid);
            long down = long.Parse(ones.Groups[1].Value, CultureInfo.InvariantCulture), none = long.Parse(ones.Groups[2].Value, CultureInfo.InvariantCulture);
            Assert.True(down > 1000 && none * 1000 <= down, $"{grid}: {none} ones with no nought beside them, {down} with one");
        }
    }

    /// <summary>
    /// The projectile sheet (TerrainOutline.Shots) over the two captures' own grids: beside every edge of the walkable
    /// ground, the ground a shot crosses is drawn where the floor beside it is - lifted to the floor's height, not
    /// left at its own.
    /// </summary>
    /// <remarks>
    /// THE GROTTO SAID WHY (v0.1.218): its abyss lies up to 85 cells of displacement below the paths, so drawn at its own
    /// height or flat the sheet sat tens of cells off the outline; and pushed cell by cell to the floor's height it tore
    /// into black wedges wherever the nearest floor changed height - which is why each pixel gathers its cells instead.
    /// Counted here: an edge cell's shot-only neighbour, moved by the edge cell's own shift, lands on a drawn pixel - in
    /// both areas all but about one in 150, and those a pixel off, where the edge's height steps by a cell inside one
    /// block of the lift (blocks of four missed twice as many, at the same cost).
    /// </remarks>
    [Theory]
    [InlineData("traitors-passage", 644023)]
    [InlineData("titan-grotto", 7630179)]
    public void THEPROJECTILESheetMeetsTheFloorItBorders(string name, long cells)
    {
        TerrainGrid grid = RecordedTerrain(Capture(name));
        Assert.True(grid.HasShotGrid, grid.ShotNote);

        ShotPlan plan = TerrainOutline.Shots(grid, 2048, isoHeightShift: true);
        Assert.Equal(cells, plan.Cells);

        long met = 0, beside = 0, far = 0, shut = 0;
        for (int y = 0; y < grid.Height; y++)
        {
            for (int x = 0; x < grid.Width; x++)
            {
                if (!grid.IsWalkable(x, y))
                {
                    continue;
                }

                // No walkable cell is shut to a projectile - what makes the sheet a difference rather than a second floor.
                shut += grid.CanShootOver(x, y) ? 0 : 1;
                int shift = grid.IsoHeightShift(x, y);
                foreach ((int nx, int ny) in (ReadOnlySpan<(int, int)>)[(x - 1, y), (x + 1, y), (x, y - 1), (x, y + 1)])
                {
                    if (!grid.IsShotOnly(nx, ny) || nx - shift < 0 || ny - shift < 0)
                    {
                        continue;
                    }

                    int px = (nx - shift) / plan.Step, py = (ny - shift) / plan.Step;
                    if (px >= plan.Width || py >= plan.Height)
                    {
                        continue;
                    }

                    if (Drawn(plan, px, py))
                    {
                        met++;
                    }
                    else if (Drawn(plan, px - 1, py) || Drawn(plan, px + 1, py) || Drawn(plan, px, py - 1) || Drawn(plan, px, py + 1)
                        || Drawn(plan, px - 1, py - 1) || Drawn(plan, px + 1, py + 1) || Drawn(plan, px - 1, py + 1) || Drawn(plan, px + 1, py - 1))
                    {
                        beside++;
                    }
                    else
                    {
                        far++;
                    }
                }
            }
        }

        Assert.Equal(0, shut);
        Assert.True(met > 20_000 && beside * 100 <= met && far * 1000 <= met, $"{met} edge cells met, {beside} a pixel off, {far} further");
    }

    private static bool Drawn(ShotPlan plan, int x, int y)
        => (uint)x < (uint)plan.Width && (uint)y < (uint)plan.Height && plan.Coverage[(y * plan.Width) + x] != 0;

    /// <summary>The terrain grid the capture's recording reads back as the tool reads it live - both grids, the heights.</summary>
    private static TerrainGrid RecordedTerrain(string folder)
    {
        using var replay = ReplayMemoryReader.Load(File.OpenRead(Path.Combine(folder, CaptureReport.MemoryFile)));
        Assert.True(replay.ResolvedStatics.TryGetValue("GameStates", out ulong gameStates));
        var rotation = new TerrainRotationTables(
            replay.ResolvedStatics.GetValueOrDefault("TerrainRotationSelector"),
            replay.ResolvedStatics.GetValueOrDefault("TerrainRotatorHelper"));
        WorldSnapshot snapshot = new WorldReader(replay, RealSessionTests.LiveSchema(), rotation).Read(gameStates, CaptureMemory.MostEntities);
        return Assert.IsType<TerrainGrid>(snapshot.Terrain);
    }

    [GeneratedRegex(@"under a suppressing overlay's place: \d+ present, (\d+) missing")]
    private static partial Regex Suppressed();

    [GeneratedRegex(@"(\d+) met a pair the edge combinations name, the laid side what they make at (\d+)")]
    private static partial Regex Joins();

    [GeneratedRegex(@"places of rooms held to a lattice: \d+ on it, \d+ off it; (\d+) such rooms placed, (\d+) with their strongest place on it; (\d+) with a place off it stronger")]
    private static partial Regex LatticeHeld();

    [GeneratedRegex(@"doodads, off (\d+): (on|OFF) the lattice")]
    private static partial Regex LatticePlace();

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

    [GeneratedRegex(@"(\d+) filled from the master's fill list \((\d+) the game's file\)")]
    private static partial Regex Filled();

    [GeneratedRegex(@"rooms built and held against their places: (\d+) slots, [^\r\n]*?(\d+) with no tile alike")]
    private static partial Regex Built();
}
