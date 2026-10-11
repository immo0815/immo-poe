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
        var tile = new MonsterModel(room, null, "room.arm", string.Empty, string.Empty)
        {
            Kind = ModelKind.Room,
            ShapeSources = ["Port_Boss_01.tdt at tile 3, 4", "ground of Port_Boss_01.tdt at tile 3, 4", string.Empty],
        };
        var actor = new MonsterModel(Box(5f, 20f), null, "monster.smd", string.Empty, string.Empty);

        MonsterModel staged = StagedModels.Staged(actor, tile, modelSize: 0, "Metadata/Terrain/Rooms/boss_01.arm");

        Assert.Equal(
            ["Port_Boss_01.tdt at tile 3, 4", "ground of Port_Boss_01.tdt at tile 3, 4", "Metadata/Terrain/Rooms/boss_01.arm", StagedModels.ActorSource],
            staged.ShapeSources);
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
