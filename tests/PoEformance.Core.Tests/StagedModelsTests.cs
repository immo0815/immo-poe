using System.Numerics;
using PoEformance.Features;
using PoEformance.Game.Files;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// A monster stood on a tile: where he lands, how big he is, and that the stage keeps its own vertices.
/// </summary>
/// <remarks>
/// SYNTHETIC MESHES, because the arithmetic is the whole of what is being tested - the game's files
/// go through the same join. The files' up is minus z throughout: a floor at z = 10 is HIGHER than
/// one at z = 30, and a monster's feet are its greatest z.
/// </remarks>
public class StagedModelsTests
{
    /// <summary>A square of two triangles from (0, 0) to (side, side) at a height, facing up unless told otherwise, as a mesh.</summary>
    private static SkinnedMesh Square(float side, float z, string name = "", bool facingUp = true)
    {
        Vector3[] at =
        [
            new(0f, 0f, z), new(side, 0f, z), new(side, side, z), new(0f, side, z),
        ];
        Vector3 facing = facingUp ? -Vector3.UnitZ : Vector3.UnitX;
        Vector3[] up = [facing, facing, facing, facing];
        int[] indices = [0, 1, 2, 0, 2, 3];
        return SkinnedMesh.Of(at, up, indices, new Vector3(0f, 0f, z), new Vector3(side, side, z), shapes: [new MeshShape(name, 0, 6)]);
    }

    /// <summary>A box standing on z = 0 with its feet at z = 0 and its head at -tall, half a unit wide each way.</summary>
    private static SkinnedMesh Box(float half, float tall)
    {
        Vector3[] at =
        [
            new(-half, -half, 0f), new(half, -half, 0f), new(half, half, 0f), new(-half, half, 0f),
            new(-half, -half, -tall), new(half, -half, -tall), new(half, half, -tall), new(-half, half, -tall),
        ];
        var up = new Vector3[8];
        Array.Fill(up, Vector3.UnitX);
        int[] indices = [0, 1, 2, 0, 2, 3, 4, 5, 6, 4, 6, 7];
        return SkinnedMesh.Of(at, up, indices, new Vector3(-half, -half, -tall), new Vector3(half, half, 0f), shapes: [new MeshShape("body", 0, 12)]);
    }

    /// <summary>A rectangle from (x0, y0) to (x1, y1) at a height, facing up, as one part of a stage's join.</summary>
    private static MeshJoin Slab(float x0, float y0, float x1, float y1, float z, string name)
        => new(Square(1f, z, name), Place: Matrix4x4.CreateScale(x1 - x0, y1 - y0, 1f) * Matrix4x4.CreateTranslation(x0, y0, 0f));

    /// <summary>The area round you as a stage, out of its parts in order, each shape's blend word in <paramref name="modes"/> - none for solid.</summary>
    private static MonsterModel Around(IReadOnlyList<string> modes, params MeshJoin[] parts)
        => new(SkinnedMesh.Joined(parts), null, AreaBlock.Path, string.Empty, string.Empty) { Kind = ModelKind.Room, Modes = modes };

    /// <summary>
    /// Whether a floor found is the one at a height, to a hundredth: a height is worked out across a triangle by its three weights, and a
    /// flat one comes out a bit or two off where they do not sum to one exactly.
    /// </summary>
    private static bool Near(float found, float height) => MathF.Abs(found - height) < 0.01f;

    /// <summary>Where the monster's origin landed on the stage, to a thousandth.</summary>
    private static void Landed(Vector3 expected, MonsterModel staged)
    {
        Vector3 at = staged.ActorPlace.Translation;
        Assert.Equal(expected.X, at.X, 3);
        Assert.Equal(expected.Y, at.Y, 3);
        Assert.Equal(expected.Z, at.Z, 3);
    }

    [Fact]
    public void TheFloorIsTheHighestSurfaceFacingUpUnderThePoint()
    {
        // Two floors over the same ground: the one at z = 10 is above the one at z = 30.
        SkinnedMesh tile = SkinnedMesh.Joined(
        [
            new MeshJoin(Square(100f, 30f, "ground")),
            new MeshJoin(Square(100f, 10f, "floor"), Place: Matrix4x4.Identity),
        ]);

        Assert.True(StagedModels.Floor(tile, null, 50f, 50f, out float height));
        Assert.Equal(10f, height, 3);

        // On the seam between the two triangles too.
        Assert.True(StagedModels.Floor(tile, null, 25f, 25f, out height));
        Assert.Equal(10f, height, 3);

        // And nothing lies past the square's edge.
        Assert.False(StagedModels.Floor(tile, null, 150f, 50f, out _));
        Assert.False(StagedModels.Floor(null, null, 50f, 50f, out _));
    }

    [Fact]
    public void ADecalOrAWallOverTheFloorIsNotStoodOn()
    {
        // A ground layer laid over the floor, drawn mixed, and a sideways-facing sheet above both.
        SkinnedMesh tile = SkinnedMesh.Joined(
        [
            new MeshJoin(Square(100f, 30f, "floor")),
            new MeshJoin(Square(100f, 20f, "mud"), Place: Matrix4x4.Identity),
            new MeshJoin(Square(100f, 5f, "wall", facingUp: false), Place: Matrix4x4.Identity),
        ]);
        MaterialBlend[] blends = [MaterialBlend.Opaque, MaterialBlend.Alpha, MaterialBlend.Opaque];

        Assert.True(StagedModels.Floor(tile, blends, 50f, 50f, out float height));
        Assert.Equal(30f, height, 3);

        // Without the blends every shape is solid, and the mud is the floor.
        Assert.True(StagedModels.Floor(tile, null, 50f, 50f, out height));
        Assert.Equal(20f, height, 3);
    }

    [Fact]
    public void TheMonsterStandsInTheMiddleOnTheFloorAtTheGamesSize()
    {
        var tile = new MonsterModel(Square(100f, 10f), null, "stage.tgm", string.Empty, string.Empty) { Kind = ModelKind.Tile };
        var actor = new MonsterModel(Box(5f, 20f), null, "monster.smd", string.Empty, string.Empty)
        {
            BodyLeast = new Vector3(-5f, -5f, -20f),
            BodyMost = new Vector3(5f, 5f, 0f),
        };

        MonsterModel staged = StagedModels.Staged(actor, tile, modelSize: 200, "Metadata/Terrain/Arena.tdt");

        Assert.True(staged.Staged);
        Assert.Same(actor, staged.Actor);
        Assert.Equal("Metadata/Terrain/Arena.tdt", staged.Stage);
        Assert.Contains("on the floor there", staged.StageSaid, StringComparison.Ordinal);
        Assert.Contains("2 of its file's size", staged.StageSaid, StringComparison.Ordinal);

        // The tile's four vertices first, as the file put them; the monster's eight after, at twice
        // their size, his feet on the floor at the middle of the square.
        Assert.Equal(4, staged.ActorFirst);
        Assert.Equal(12, staged.Mesh.Positions.Length);
        Assert.Equal(new Vector3(0f, 0f, 10f), staged.Mesh.Positions[0]);
        Assert.Equal(new Vector3(40f, 40f, 10f), staged.Mesh.Positions[4]);
        Assert.Equal(new Vector3(60f, 60f, -30f), staged.Mesh.Positions[10]);

        // The place the pane moves his posed vertices by is the same one.
        Assert.Equal(new Vector3(40f, 40f, 10f), Vector3.Transform(new Vector3(-5f, -5f, 0f), staged.ActorPlace));
        Assert.Equal(new Vector3(40f, 40f, -30f), staged.BodyLeast);
        Assert.Equal(new Vector3(60f, 60f, 10f), staged.BodyMost);

        // Each shape keeps its source, which is what the probe names.
        Assert.Equal(2, staged.Mesh.Shapes.Count);
        Assert.Equal(["Metadata/Terrain/Arena.tdt", StagedModels.ActorSource], staged.ShapeSources);
        Assert.Equal(ModelKind.Ao, staged.Kind);
    }

    /// <summary>
    /// A room naming its boss's place stands the monster there, turned the way the place faces, on the deck round it - not on the
    /// seat of the throne the place is.
    /// </summary>
    [Fact]
    public void TheMonsterStandsWhereTheRoomNamesItsBossTurnedItsWay()
    {
        // A deck four hundred across at z 10, and a throne's seat forty across, 30 higher (up is minus z), over the place.
        SkinnedMesh deck = SkinnedMesh.Joined(
        [
            new MeshJoin(Square(400f, 10f, "deck")),
            new MeshJoin(Square(40f, -20f, "seat"), Place: Matrix4x4.CreateTranslation(80f, 280f, 0f)),
        ]);
        var tile = new MonsterModel(deck, null, "room.arm", string.Empty, string.Empty)
        {
            Kind = ModelKind.Room,
            BossSpot = new RoomSpot(new Vector2(100f, 300f), MathF.PI / 2f, "mapboss", Others: 1),
        };
        var actor = new MonsterModel(Box(5f, 20f), null, "monster.smd", string.Empty, string.Empty);

        MonsterModel staged = StagedModels.Staged(actor, tile, modelSize: 0, "Metadata/Terrain/Rooms/boss_01.arm");

        Assert.Contains("where the room names its boss (\"mapboss\", facing 90°; 1 more boss places passed over)", staged.StageSaid, StringComparison.Ordinal);
        Assert.Contains("on the floor there (10 on the tile's z)", staged.StageSaid, StringComparison.Ordinal);

        // His feet at the place, on the deck; the way he faces - minus y in his own file - turned to plus x.
        Assert.Equal(new Vector3(100f, 300f, 10f), Vector3.Transform(Vector3.Zero, staged.ActorPlace));
        Vector3 facing = Vector3.TransformNormal(new Vector3(0f, -1f, 0f), staged.ActorPlace);
        Assert.Equal(1f, facing.X, 3);
        Assert.Equal(0f, facing.Y, 3);
    }

    /// <summary>
    /// The area round you stands its monster where the player stood when the block was taken - on the floor there, facing as at the middle -
    /// and a stage handed no place stands him at its middle as before.
    /// </summary>
    /// <remarks>
    /// A DECK WITH A PLATFORM AT ITS MIDDLE, so the two places stand on two floors: the platform 30 higher than the deck (up is minus z), the
    /// player's place on the deck, its whole ring of floors asked on the deck too.
    /// </remarks>
    [Fact]
    public void THEMONSTERStandsWhereThePlayerStoodWhenTheBlockWasTaken()
    {
        SkinnedMesh ground = SkinnedMesh.Joined(
        [
            new MeshJoin(Square(400f, 10f, "deck")),
            new MeshJoin(Square(100f, -20f, "platform"), Place: Matrix4x4.CreateTranslation(150f, 150f, 0f)),
        ]);
        var block = new MonsterModel(ground, null, AreaBlock.Path, string.Empty, string.Empty) { Kind = ModelKind.Room };
        var actor = new MonsterModel(Box(5f, 20f), null, "monster.smd", string.Empty, string.Empty);

        MonsterModel staged = StagedModels.Staged(actor, block, modelSize: 0, AreaBlock.Path, stood: new Vector2(60f, 300f));

        Assert.Contains(
            "the monster stands where the player stood when the block was taken (on the floor at that place - the key names no height of his) at 1 of its file's size, on the floor there (10 on the tile's z)",
            staged.StageSaid,
            StringComparison.Ordinal);
        Assert.Equal(new Vector3(60f, 300f, 10f), Vector3.Transform(Vector3.Zero, staged.ActorPlace));
        StageStanding standing = Assert.IsType<StageStanding>(staged.Standing);
        Assert.Equal((new Vector2(60f, 300f), 0f, 10f), (standing.At, standing.Turn, standing.Floor));
        Vector3 facing = Vector3.TransformNormal(new Vector3(0f, -1f, 0f), staged.ActorPlace);
        Assert.Equal(-1f, facing.Y, 3);

        // HANDED NO PLACE, he stands at the middle of the stage's box - on the platform.
        MonsterModel middle = StagedModels.Staged(actor, block, modelSize: 0, AreaBlock.Path);
        Assert.Contains("the monster stands at its middle", middle.StageSaid, StringComparison.Ordinal);
        Assert.Equal(new Vector3(200f, 200f, -20f), Vector3.Transform(Vector3.Zero, middle.ActorPlace));
    }

    /// <summary>
    /// On a footbridge over a river, narrower than the ring the floor is asked on round the place, the monster stands on the deck - with the
    /// player's height, the floor nearest it, and without, as an older key has it, the floor at the place itself - not on the river's bed.
    /// </summary>
    /// <remarks>
    /// THE RED VALE'S FOOTBRIDGES, A REVIEW'S FINDING: the deck 100 wide along y at z 10, the river's water at 60 over its whole width - mixed,
    /// so no floor - and its bed at 100 (up is minus z). Six of the ring's eight points miss the deck and find the bed, so the nine floors'
    /// middle, a boss's rule, stood him on the bed 90 units under the player's feet.
    /// </remarks>
    [Fact]
    public void THEMONSTERStandsOnTheFootbridgeThePlayerStoodOnNotOnTheRiversBed()
    {
        MonsterModel bridge = Around(
            [string.Empty, MonsterModels.Paints.RiverMode, string.Empty],
            Slab(0f, 0f, 400f, 400f, 100f, "bed"),
            Slab(0f, 0f, 400f, 400f, 60f, "river"),
            Slab(150f, 0f, 250f, 400f, 10f, "deck"));
        Assert.Equal([MaterialBlend.Opaque, MaterialBlend.Alpha, MaterialBlend.Opaque], bridge.Blends);
        var actor = new MonsterModel(Box(5f, 20f), null, "monster.smd", string.Empty, string.Empty);

        MonsterModel staged = StagedModels.Staged(actor, bridge, modelSize: 0, AreaBlock.Path, stood: new Vector2(200f, 200f), height: 10f);

        StageStanding standing = Assert.IsType<StageStanding>(staged.Standing);
        Assert.Equal(6, standing.Floors.Count(one => Near(one.Height, 100f)));
        Landed(new Vector3(200f, 200f, 10f), staged);
        Assert.Equal("the one nearest the player's own height (10) taken", standing.Taken);
        Assert.Contains(
            "the monster stands where the player stood when the block was taken (on the floor nearest his own height there, 10) at 1 of its file's size, on the floor there (10 on the tile's z)",
            staged.StageSaid,
            StringComparison.Ordinal);

        // AN OLDER KEY NAMES NO HEIGHT: the floor at the place itself, which is the deck too.
        MonsterModel older = StagedModels.Staged(actor, bridge, modelSize: 0, AreaBlock.Path, stood: new Vector2(200f, 200f));
        Landed(new Vector3(200f, 200f, 10f), older);
        Assert.Equal("the one at the place itself taken - the key names no height of the player's", older.Standing!.Taken);
    }

    /// <summary>In a passage between two walls narrower than the ring, the monster stands on the passage's floor, not on the walls' tops.</summary>
    [Fact]
    public void THEMONSTERStandsOnThePassagesFloorNotOnTheWallsTops()
    {
        // THE FLOOR AT z 10 AND THE WALLS' TOPS 200 OVER IT, on either side of a passage 100 wide: six of the ring's points on the walls.
        MonsterModel passage = Around(
            [],
            Slab(0f, 0f, 400f, 400f, 10f, "floor"),
            Slab(0f, 0f, 150f, 400f, -190f, "wall"),
            Slab(250f, 0f, 400f, 400f, -190f, "wall"));
        var actor = new MonsterModel(Box(5f, 20f), null, "monster.smd", string.Empty, string.Empty);

        MonsterModel staged = StagedModels.Staged(actor, passage, modelSize: 0, AreaBlock.Path, stood: new Vector2(200f, 200f), height: 10f);
        Assert.Equal(6, staged.Standing!.Floors.Count(one => Near(one.Height, -190f)));
        Landed(new Vector3(200f, 200f, 10f), staged);

        MonsterModel older = StagedModels.Staged(actor, passage, modelSize: 0, AreaBlock.Path, stood: new Vector2(200f, 200f));
        Landed(new Vector3(200f, 200f, 10f), older);
    }

    /// <summary>
    /// Under a roof over the player, his height at the ground, the monster stands on the ground - which the floor at his place, the roof's
    /// top, is not. Without the height, as an older key has it, he stands on the roof: why the height rides in the key.
    /// </summary>
    [Fact]
    public void UNDERAROOFOverThePlayerTheMonsterStandsOnTheGroundAtHisHeight()
    {
        // THE GROUND AT z 10, THE ROOF'S TOP 300 OVER IT, 300 across and 200 deep round the place: the place and six of the ring's points
        // under it, the two along y out from under it.
        MonsterModel roofed = Around(
            [],
            Slab(0f, 0f, 400f, 400f, 10f, "ground"),
            Slab(50f, 100f, 350f, 300f, -290f, "roof"));
        var actor = new MonsterModel(Box(5f, 20f), null, "monster.smd", string.Empty, string.Empty);

        MonsterModel staged = StagedModels.Staged(actor, roofed, modelSize: 0, AreaBlock.Path, stood: new Vector2(200f, 200f), height: 10f);
        Assert.Equal(7, staged.Standing!.Floors.Count(one => Near(one.Height, -290f)));
        Landed(new Vector3(200f, 200f, 10f), staged);
        Assert.Equal(10f, staged.Standing.Floor, 3);

        MonsterModel older = StagedModels.Staged(actor, roofed, modelSize: 0, AreaBlock.Path, stood: new Vector2(200f, 200f));
        Landed(new Vector3(200f, 200f, -290f), older);
    }

    /// <summary>
    /// Where nothing faces up at the place itself and the key names no height, the middle of the ring's floors stands in; a height with
    /// no place is not asked; and a tie between two floors equally near the player's height goes to the place's own.
    /// </summary>
    [Fact]
    public void THEPLACESOwnFloorWinsATieAndTheRingsMiddleStandsInForNone()
    {
        // A HOLE 40 ACROSS AT THE PLACE, the ground at z 10 round it.
        MonsterModel holed = Around(
            [],
            Slab(0f, 0f, 180f, 400f, 10f, "ground"),
            Slab(220f, 0f, 400f, 400f, 10f, "ground"),
            Slab(180f, 0f, 220f, 180f, 10f, "ground"),
            Slab(180f, 220f, 220f, 400f, 10f, "ground"));
        var actor = new MonsterModel(Box(5f, 20f), null, "monster.smd", string.Empty, string.Empty);
        MonsterModel staged = StagedModels.Staged(actor, holed, modelSize: 0, AreaBlock.Path, stood: new Vector2(200f, 200f));
        Assert.False(float.IsFinite(staged.Standing!.Floors[0].Height));
        Landed(new Vector3(200f, 200f, 10f), staged);
        Assert.Equal("the middle of the ring's taken - none at the place itself, and the key names no height of the player's", staged.Standing.Taken);

        // TWO FLOORS 20 EITHER SIDE OF HIS HEIGHT: the place's own, a step at z 0, against the ground at 40 round it.
        MonsterModel stepped = Around(
            [],
            Slab(0f, 0f, 400f, 400f, 40f, "ground"),
            Slab(150f, 150f, 250f, 250f, 0f, "step"));
        MonsterModel tie = StagedModels.Staged(actor, stepped, modelSize: 0, AreaBlock.Path, stood: new Vector2(200f, 200f), height: 20f);
        Assert.Equal(0f, tie.Standing!.Floor, 3);

        // A HEIGHT HANDED WITHOUT A PLACE stands him at the middle, as before.
        MonsterModel middle = StagedModels.Staged(actor, stepped, modelSize: 0, AreaBlock.Path, height: 40f);
        Assert.Contains("the monster stands at its middle", middle.StageSaid, StringComparison.Ordinal);
        Landed(new Vector3(200f, 200f, 0f), middle);
    }

    /// <summary>
    /// A monster whose rest pose hangs below his origin stands his ORIGIN on the floor, as the game stands Malgor: his entity's z is
    /// the terrain less half his height over his origin, while his bind pose hangs 209.7 under his feet (v0.1.328's capture).
    /// </summary>
    [Fact]
    public void AMONSTERWhoseRestHangsBelowHisOriginStandsHisOriginOnTheFloor()
    {
        var tile = new MonsterModel(Square(100f, 10f), null, "stage.tgm", string.Empty, string.Empty) { Kind = ModelKind.Tile };
        SkinnedMesh hanging = SkinnedMesh.Joined(
        [
            new MeshJoin(Box(5f, 20f)),
            new MeshJoin(Box(2f, 15f), Place: Matrix4x4.CreateTranslation(0f, 0f, 15f)),
        ]);
        Assert.Equal(15f, hanging.Most.Z);
        var actor = new MonsterModel(hanging, null, "monster.smd", string.Empty, string.Empty);

        MonsterModel staged = StagedModels.Staged(actor, tile, modelSize: 0, "Arena.tdt");

        Assert.Equal(new Vector3(50f, 50f, 10f), Vector3.Transform(Vector3.Zero, staged.ActorPlace));
        Assert.Equal(10f, staged.Standing!.Floor);
    }

    /// <summary>
    /// A room that names every shape's piece - a tile, its ground, a doodad - keeps those names on the stage, where the probe reads
    /// them; a shape it leaves unnamed is the stage's.
    /// </summary>
    [Fact]
    public void ASTAGEDRoomKeepsTheNamesOfItsPiecesForTheProbe()
    {
        SkinnedMesh room = SkinnedMesh.Joined(
        [
            new MeshJoin(Square(400f, 10f, "deck")),
            new MeshJoin(Square(400f, 12f, "ground")),
            new MeshJoin(Square(40f, -20f, "boat")),
        ]);
        DoodadFile water = new("WaterPlane3x3.ao", 31, 0, 0, 31, 0, string.Empty, Vector3.Zero);
        var tile = new MonsterModel(room, null, "room.arm", string.Empty, string.Empty)
        {
            Kind = ModelKind.Room,
            ShapeSources = ["Port_Boss_01.tdt at tile 3, 4", "ground of Port_Boss_01.tdt at tile 3, 4", string.Empty],
            Move = "tile doodads: 31 from the doodad lists of 1 tile files",
            DoodadFiles = [water],
        };
        var actor = new MonsterModel(Box(5f, 20f), null, "monster.smd", string.Empty, string.Empty) { Move = "the monster's own" };

        MonsterModel staged = StagedModels.Staged(actor, tile, modelSize: 0, "Metadata/Terrain/Rooms/boss_01.arm");

        Assert.Equal(
            ["Port_Boss_01.tdt at tile 3, 4", "ground of Port_Boss_01.tdt at tile 3, 4", "Metadata/Terrain/Rooms/boss_01.arm", StagedModels.ActorSource],
            staged.ShapeSources);

        // AND THE ROOM'S OWN LINES AND DOODAD FILES, beside the monster's: dropped, the stage could not say where its water went.
        Assert.Equal("the monster's own", staged.Move);
        Assert.Equal(tile.Move, staged.StageMove);
        Assert.Equal([water], staged.DoodadFiles);

        // A TILE STAGE HAS NONE: its Move is why a rigid mesh does not move, which is not a stage's account.
        var plain = new MonsterModel(room, null, "stage.tgm", string.Empty, string.Empty) { Kind = ModelKind.Tile, Move = "a fixed mesh is rigid" };
        Assert.Equal(string.Empty, StagedModels.Staged(actor, plain, modelSize: 0, "Arena.tdt").StageMove);
    }

    /// <summary>
    /// A stage keeps the master its room was made under and that master's swaps, for the capture to hold against what each does to its
    /// material (CapturePanes) - the monster set on it wears none of them; a stage made under none carries none.
    /// </summary>
    [Fact]
    public void ASTAGEKeepsTheMastersSwapsItsRoomWasMadeUnder()
    {
        var swaps = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) { ["Art/stone.mat"] = "Art/dry.mat" };
        var room = new MonsterModel(Square(400f, 10f, "deck"), null, "room.arm", string.Empty, string.Empty)
        {
            Kind = ModelKind.Room,
            MasterSwaps = swaps,
            MasterFile = "Metadata/Terrain/Maps/Port/master.tsi",
        };
        var actor = new MonsterModel(Box(5f, 20f), null, "monster.smd", string.Empty, string.Empty);

        MonsterModel staged = StagedModels.Staged(actor, room, modelSize: 0, "Metadata/Terrain/Rooms/boss_01.arm");

        Assert.Same(swaps, staged.MasterSwaps);
        Assert.Equal("Metadata/Terrain/Maps/Port/master.tsi", staged.MasterFile);

        MonsterModel bare = StagedModels.Staged(actor, room with { MasterSwaps = new Dictionary<string, string>(), MasterFile = string.Empty }, modelSize: 0, "Arena.tdt");
        Assert.Equal((string.Empty, 0), (bare.MasterFile, bare.MasterSwaps.Count));
    }

    [Fact]
    public void WithNothingFacingUpUnderTheMiddleTheMonsterStandsAtTheTilesLowestPoint()
    {
        // A tile that is one wall: nothing under the middle faces up.
        var tile = new MonsterModel(Square(100f, 10f, "wall", facingUp: false), null, "stage.tgm", string.Empty, string.Empty) { Kind = ModelKind.Tile };
        var actor = new MonsterModel(Box(5f, 20f), null, "monster.smd", string.Empty, string.Empty);

        MonsterModel staged = StagedModels.Staged(actor, tile, modelSize: 0, "Arena.tdt");

        Assert.Contains("nothing faces up there", staged.StageSaid, StringComparison.Ordinal);
        Assert.Equal(new Vector3(50f, 50f, 10f), staged.Mesh.Positions[staged.ActorFirst] + new Vector3(5f, 5f, 0f));
    }

    [Fact]
    public void ARoomIsAStageOnlyAsTheAreaLaidItAndOnlyWhileThatAreaStands()
    {
        const string Room = "Metadata/Terrain/Rooms/arena.arm";
        static byte[]? Read(string path) => null;

        // The file alone names no place, so there is nothing to stand on.
        Assert.Contains("only as the area laid it", StagedModels.LaidRoom(Read, Room, () => null).Why, StringComparison.Ordinal);

        // A place is one grid's: without it, or with another reading of the area, the stage is gone.
        var grid = new TerrainGrid([0], 1, 1, 0, 0, null, string.Empty, []);
        string key = new RoomKey(Room, Laid: RoomKey.LaidAt(3, 4, 0, RoomKey.Stamp(grid))).ToString();
        Assert.Equal(Room, StagedModels.PathOf(key));
        Assert.Contains("is gone", StagedModels.LaidRoom(Read, key, () => null).Why, StringComparison.Ordinal);
        var other = new TerrainGrid([0], 1, 1, 0, 0, null, string.Empty, []);
        Assert.Contains("is gone", StagedModels.LaidRoom(Read, key, () => other).Why, StringComparison.Ordinal);

        // With its own grid the room goes on to be laid - and this grid has no tiles to lay it from.
        Assert.Contains("tiles are not read", StagedModels.LaidRoom(Read, key, () => grid).Why, StringComparison.Ordinal);
    }
}
