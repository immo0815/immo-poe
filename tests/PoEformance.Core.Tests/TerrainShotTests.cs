using PoEformance.Core.Schema;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// The projectiles' grid (GridLandscapeData) on the terrain grid: where a shot passes, the clear shot, and the reader that
/// fills it in beside the walkable grid.
/// </summary>
/// <remarks>
/// The grids are drawn as the two captures measured them (CaptureFixtureTests.THESECONDGridIsWhereProjectilesStop): floor is
/// open in both, a chasm open to projectiles only, a wall shut in both.
/// </remarks>
public class TerrainShotTests
{
    private const ulong AreaInstance = 0x0000_0200_0000_0000;
    private const ulong WalkableData = 0x0000_0200_0100_0000;
    private const ulong LandscapeData = 0x0000_0200_0500_0000;

    /// <summary>'.' floor, '~' a chasm, '#' a wall.</summary>
    private static TerrainGrid Grid(params string[] rows) => Grid(withShots: true, rows);

    private static TerrainGrid Grid(bool withShots, params string[] rows)
    {
        (byte[] walkable, byte[] shots, int stride) = Pack(rows);
        return new TerrainGrid(walkable, stride, rows.Length, 0, 0, heights: null, shots: withShots ? shots : null);
    }

    private static (byte[] Walkable, byte[] Shots, int Stride) Pack(string[] rows)
    {
        int width = rows[0].Length;
        int stride = (width + 1) / 2;
        var walkable = new byte[stride * rows.Length];
        var shots = new byte[stride * rows.Length];
        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * stride) + (x / 2);
                int shift = (x & 1) == 0 ? 0 : 4;

                // Distances rather than flags, as the game writes both: any value but nought is open.
                switch (rows[y][x])
                {
                    case '.':
                        walkable[index] |= (byte)(3 << shift);
                        shots[index] |= (byte)(5 << shift);
                        break;
                    case '~':
                        shots[index] |= (byte)(5 << shift);
                        break;
                }
            }
        }

        return (walkable, shots, stride);
    }

    [Fact]
    public void ACHASMIsOpenToAShotAndShutToAFoot()
    {
        TerrainGrid grid = Grid(".~#");

        Assert.True(grid.HasShotGrid);
        Assert.True(grid.IsWalkable(0, 0));
        Assert.True(grid.CanShootOver(0, 0));
        Assert.False(grid.IsShotOnly(0, 0));

        Assert.False(grid.IsWalkable(1, 0));
        Assert.True(grid.CanShootOver(1, 0));
        Assert.True(grid.IsShotOnly(1, 0));

        Assert.False(grid.IsWalkable(2, 0));
        Assert.False(grid.CanShootOver(2, 0));
        Assert.False(grid.IsShotOnly(2, 0));

        // Outside the area is shut to both, like the walkable grid's own edge.
        Assert.False(grid.CanShootOver(3, 0));
        Assert.False(grid.CanShootOver(-1, 0));
    }

    [Fact]
    public void ASHOTCrossesTheChasmThatHidesTheMonsterFromTheWalkableLine()
    {
        TerrainGrid grid = Grid(
            "...~~~...",
            "...~~~...",
            "...###...");

        Assert.False(grid.IsClearLine(0, 0, 8, 1));
        Assert.True(grid.IsClearShot(0, 0, 8, 1));
        Assert.True(grid.IsClearShot(8, 1, 0, 0));

        // A wall stops both.
        Assert.False(grid.IsClearLine(0, 2, 8, 2));
        Assert.False(grid.IsClearShot(0, 2, 8, 2));
    }

    [Fact]
    public void AnyLINEClearForWalkingIsClearForAShot()
    {
        TerrainGrid grid = Grid(
            "....#....",
            ".........",
            "~~~~#~~~~");

        for (int toX = 0; toX < 9; toX++)
        {
            for (int toY = 0; toY < 3; toY++)
            {
                if (grid.IsClearLine(0, 1, toX, toY))
                {
                    Assert.True(grid.IsClearShot(0, 1, toX, toY), $"walkable to {toX}, {toY} and not shootable");
                }
            }
        }
    }

    [Fact]
    public void WithoutTheGridNothingIsShootable()
    {
        // "Not read" is no answer, not a wall everywhere and not open air: the rule asking it answers nothing (RuleState).
        TerrainGrid grid = Grid(withShots: false, "...", "...");

        Assert.False(grid.HasShotGrid);
        Assert.False(grid.CanShootOver(0, 0));
        Assert.False(grid.IsShotOnly(1, 1));
        Assert.False(grid.IsClearShot(0, 0, 2, 1));
        Assert.True(grid.IsClearLine(0, 0, 2, 1));
    }

    [Fact]
    public void ASECONDGridOfAnotherShapeIsRefusedAndSaysWhy()
    {
        (byte[] walkable, _, int stride) = Pack(["....", "...."]);
        var grid = new TerrainGrid(walkable, stride, 2, 0, 0, heights: null, shots: new byte[walkable.Length + 2]);

        Assert.False(grid.HasShotGrid);
        Assert.Contains("not one shape", grid.ShotNote, StringComparison.Ordinal);
    }

    [Fact]
    public void THEREADERFillsItInBesideTheWalkableGrid()
    {
        string[] rows = [.. Enumerable.Repeat("....~~~~##", 20)];
        (byte[] walkable, byte[] shots, int stride) = Pack(rows);
        FakeMemoryReader fake = Area(walkable, stride, rows.Length)
            .Place(LandscapeData, shots)
            .Place<ulong>(Terrain("GridLandscapeData"), LandscapeData)
            .Place<ulong>(Terrain("GridLandscapeData") + 8, LandscapeData + (ulong)shots.Length);

        TerrainGrid? grid = new TerrainReader(fake, RealSessionTests.Schema()).Read(AreaInstance, nowMs: 0);

        Assert.NotNull(grid);
        Assert.True(grid!.HasShotGrid);
        Assert.Equal(string.Empty, grid.ShotNote);
        Assert.True(grid.IsShotOnly(5, 3));
        Assert.False(grid.CanShootOver(8, 3));
        Assert.True(grid.IsClearShot(0, 0, 7, 19));
    }

    [Theory]
    [InlineData(0, "no projectiles' grid")]
    [InlineData(2, "not one shape")]
    public void AREADERWithoutASecondGridStillReadsTheFirst(int extra, string why)
    {
        string[] rows = [.. Enumerable.Repeat("..........", 20)];
        (byte[] walkable, byte[] shots, int stride) = Pack(rows);
        FakeMemoryReader fake = Area(walkable, stride, rows.Length);
        if (extra > 0)
        {
            fake.Place(LandscapeData, [.. shots, .. new byte[extra]])
                .Place<ulong>(Terrain("GridLandscapeData"), LandscapeData)
                .Place<ulong>(Terrain("GridLandscapeData") + 8, LandscapeData + (ulong)(shots.Length + extra));
        }

        TerrainGrid? grid = new TerrainReader(fake, RealSessionTests.Schema()).Read(AreaInstance, nowMs: 0);

        Assert.NotNull(grid);
        Assert.True(grid!.IsWalkable(3, 3));
        Assert.False(grid.HasShotGrid);
        Assert.Contains(why, grid.ShotNote, StringComparison.Ordinal);
    }

    /// <summary>An area holding the walkable grid given and nothing else of the terrain.</summary>
    private static FakeMemoryReader Area(byte[] walkable, int stride, int rows)
    {
        OffsetSchema schema = RealSessionTests.Schema();
        StructDef area = schema.Structs["AreaInstance"];
        return new FakeMemoryReader()
            .Place(WalkableData, walkable)
            .Place<uint>(AreaInstance + (ulong)area.OffsetOf("CurrentAreaHash"), 0xABCD1234)
            .Place<ulong>(Terrain("GridWalkableData"), WalkableData)
            .Place<ulong>(Terrain("GridWalkableData") + 8, WalkableData + (ulong)walkable.Length)
            .Place<int>(Terrain("BytesPerRow"), stride)
            .Place<long>(Terrain("TotalTilesX"), 0)
            .Place<long>(Terrain("TotalTilesY"), 0);
    }

    private static ulong Terrain(string field)
    {
        OffsetSchema schema = RealSessionTests.Schema();
        return AreaInstance + (ulong)schema.Structs["AreaInstance"].OffsetOf("TerrainMetadata")
            + (ulong)schema.Structs["TerrainMetadata"].OffsetOf(field);
    }
}
