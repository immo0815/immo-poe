using PoEformance.Game.Ui;
using PoEformance.Game.World;

namespace PoEformance.Core.Tests;

/// <summary>
/// TerrainOutline.Shots: the picture the projectile sheet draws - the difference between the two grids, at the height of
/// the nearest walkable ground.
/// </summary>
public class ShotPictureTests
{
    /// <summary>'.' floor, '~' a chasm, '#' a wall - packed as the game packs both grids.</summary>
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
                int shift = (x & 1) == 0 ? 0 : 4;
                int at = (y * stride) + (x / 2);
                if (rows[y][x] == '.')
                {
                    walkable[at] |= (byte)(2 << shift);
                }

                if (rows[y][x] is '.' or '~')
                {
                    shots[at] |= (byte)(5 << shift);
                }
            }
        }

        return (walkable, shots, stride);
    }

    private static TerrainGrid Grid(params string[] rows)
    {
        (byte[] walkable, byte[] shots, int stride) = Pack(rows);
        return new TerrainGrid(walkable, stride, rows.Length, 0, 0, heights: null, shots: shots);
    }

    [Fact]
    public void ONLYTheGroundAShotCrossesAndAFootCannotIsDrawn()
    {
        TerrainGrid grid = Grid(
            "..~~##",
            "..~~##");

        ShotPlan plan = TerrainOutline.Shots(grid, 2048, isoHeightShift: false);

        Assert.Equal(1, plan.Step);
        Assert.Equal(4, plan.Cells);
        Assert.Equal(
            new byte[] { 0, 0, 255, 255, 0, 0, 0, 0, 255, 255, 0, 0 },
            plan.Coverage);
    }

    [Fact]
    public void THINNEDItKeepsTheShareOfEachPixel()
    {
        // A strip one cell wide at a step of two is half a pixel - fainter, not gone, and not grown to a whole one.
        TerrainGrid grid = Grid(
            ".~..",
            ".~..",
            "....",
            "....");

        ShotPlan plan = TerrainOutline.Shots(grid, 2, isoHeightShift: false);

        Assert.Equal(2, plan.Step);
        Assert.Equal((2, 2), (plan.Width, plan.Height));
        Assert.Equal(128, plan.Coverage[0]);
        Assert.Equal(0, plan.Coverage[1]);
    }

    [Fact]
    public void WITHOUTTheSecondGridThereIsNothing()
    {
        (byte[] walkable, _, int stride) = Pack(["..~~", "..~~"]);
        var grid = new TerrainGrid(walkable, stride, 2);

        ShotPlan plan = TerrainOutline.Shots(grid, 2048, isoHeightShift: true);

        Assert.Equal(0, plan.Cells);
        Assert.All(plan.Coverage, one => Assert.Equal(0, one));
    }

    [Fact]
    public void ACHASMIsDrawnAtTheHeightOfTheGroundBesideItNotAtItsFloor()
    {
        // Two tiles: a raised floor on the left (shift -10 cells), a deep chasm on the right (its own shift +30). Drawn at
        // its own height the chasm would be pushed thirty cells up the diagonal, off the picture altogether; drawn at the
        // floor's it moves with the floor, ten cells down the diagonal, and meets it.
        const int cells = TerrainGrid.CellsPerTile;
        var rows = new string[cells];
        for (int y = 0; y < cells; y++)
        {
            rows[y] = new string('.', cells) + new string('~', cells);
        }

        (byte[] walkable, byte[] shots, int stride) = Pack(rows);
        float perCell = 2f * MapView.HeightToGrid;
        TerrainHeightField heights = TerrainHeightField.Tiles([-10 * perCell, 30 * perCell], 2, 1);
        var grid = new TerrainGrid(walkable, stride, cells, 2, 1, heights, shots: shots);
        Assert.Equal(-10, grid.IsoHeightShift(0, 0));
        Assert.Equal(30, grid.IsoHeightShift(cells, 0));

        ShotPlan plan = TerrainOutline.Shots(grid, 2048, isoHeightShift: true);
        Assert.Equal(1, plan.Step);

        // The chasm's first column, (23, y), is drawn at (33, y + 10): right beside the floor's last, (22, y) at (32, y + 10).
        for (int y = 0; y < cells - 10; y++)
        {
            Assert.Equal(255, plan.Coverage[((y + 10) * plan.Width) + cells + 10]);
            Assert.Equal(0, plan.Coverage[((y + 10) * plan.Width) + cells + 9]);
        }

        // And nothing of it where a flat drawing puts it - which would leave a gap of ten cells between it and the floor.
        Assert.Equal(0, plan.Coverage[(0 * plan.Width) + cells]);
        Assert.Equal(255, TerrainOutline.Shots(grid, 2048, isoHeightShift: false).Coverage[(0 * plan.Width) + cells]);
    }

    [Fact]
    public void ASHEETIsWhiteAndAsSolidAsItsCoverage()
    {
        TerrainPicture picture = TerrainPicture.Sheet([0, 128, 255], 3, 1);

        Assert.Equal(new byte[] { 0, 0, 0, 0, 255, 255, 255, 128, 255, 255, 255, 255 }, picture.Pixels);
    }
}
