using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// A brick's surface as the graphics card raises its grid to it: each column's top and how much of it shows, the
/// columns beside the water levelled with it, a sheet running down out of the brick joined to the brick under it,
/// each square drawn by one brick, foam where the water falls.
/// </summary>
public class SurfaceGridTests
{
    [Fact]
    public void APoolsColumnsStandAtItsLevelFullyShownAndOwned()
    {
        // Rows 2 and 3 full, row 4 half: level 4.5 over the brick's floor, 2.5 deep.
        var view = new BrickView(0, 0, 0, 1f);
        foreach (int y in new[] { 2, 3, 4 })
        {
            view.Water[Brick.Index(5, y, 5)] = y == 4 ? 0.5f : 1f;
        }

        view.Solid[Brick.Index(5, 2, 5)] = 0f;
        var grid = SurfaceGrid.Build(Field(view), 0, 0, 0);
        int k = SurfaceGrid.Index(5, 5);

        Assert.Equal(4.5f, grid.Height[k], 4);
        Assert.Equal(2.5f, grid.Depth[k], 4);
        Assert.Equal(1f, grid.Shown[k]);
        Assert.Equal(1f, grid.Own[k]);
        Assert.True(grid.Draws);
        Assert.True(grid.DrawsSquare(5, 5));
        Assert.True(grid.DrawsSquare(4, 4));
        Assert.False(grid.DrawsSquare(7, 7));
    }

    [Fact]
    public void ThinWaterFadesInAndAFilmIsNotShown()
    {
        var view = new BrickView(0, 0, 0, 1f);
        view.Water[Brick.Index(2, 0, 2)] = 0.02f;
        view.Water[Brick.Index(4, 0, 2)] = 0.008f;
        var grid = SurfaceGrid.Build(Field(view), 0, 0, 0);

        Assert.Equal(0.5f, grid.Shown[SurfaceGrid.Index(2, 2)], 4);
        Assert.Equal(0f, grid.Shown[SurfaceGrid.Index(4, 2)]);
    }

    [Fact]
    public void ColumnsBesideTheWaterAreLevelWithItNotTheFloor()
    {
        var view = new BrickView(0, 0, 0, 1f);
        view.Water[Brick.Index(5, 6, 5)] = 0.5f;
        view.Solid[Brick.Index(5, 6, 5)] = 0.2f;
        var grid = SurfaceGrid.Build(Field(view), 0, 0, 0);

        Assert.Equal(6.7f, grid.Height[SurfaceGrid.Index(6, 5)], 4);
        Assert.Equal(6.7f, grid.Height[SurfaceGrid.Index(7, 7)], 4);
        Assert.Equal(0f, grid.Shown[SurfaceGrid.Index(6, 5)]);
    }

    [Fact]
    public void ASheetRunningDownIntoTheBrickUnderIsJoinedAndEachSquareDrawnOnce()
    {
        // A thin sheet on a slope: column x 5 tops at row 16 (the upper brick's floor row), x 6 at row 15 (the lower
        // brick's top row).
        var upper = new BrickView(0, 1, 0, 1f);
        var lower = new BrickView(0, 0, 0, 1f);
        upper.Water[Brick.Index(5, 0, 5)] = 0.1f;
        upper.Solid[Brick.Index(5, 0, 5)] = 0.3f;
        lower.Water[Brick.Index(6, 15, 5)] = 0.1f;
        lower.Solid[Brick.Index(6, 15, 5)] = 0.3f;
        var field = Field(upper, lower);

        var high = SurfaceGrid.Build(field, 0, 1, 0);
        var low = SurfaceGrid.Build(field, 0, 0, 0);

        // In the upper brick, the lower column is there (under its floor), not its own; in the lower, the reverse.
        Assert.Equal(0.4f, high.Height[SurfaceGrid.Index(5, 5)], 4);
        Assert.Equal(-0.6f, high.Height[SurfaceGrid.Index(6, 5)], 4);
        Assert.Equal(0f, high.Own[SurfaceGrid.Index(6, 5)]);
        Assert.Equal(1f, low.Own[SurfaceGrid.Index(6, 5)]);
        Assert.Equal(16.4f, low.Height[SurfaceGrid.Index(5, 5)], 4);

        // The square between them (south-west column x 5) is the upper brick's; the one east of it the lower's.
        Assert.True(high.DrawsSquare(5, 5));
        Assert.False(low.DrawsSquare(5, 5));
        Assert.False(high.DrawsSquare(6, 5));
        Assert.True(low.DrawsSquare(6, 5));
    }

    [Fact]
    public void TheBrickBesideGivesTheLastColumn()
    {
        // Water at x 15 of brick 0 and x 0 of brick 1 (column 16 of brick 0's grid).
        var west = new BrickView(0, 0, 0, 1f);
        var east = new BrickView(1, 0, 0, 1f);
        west.Water[Brick.Index(15, 3, 4)] = 0.5f;
        east.Water[Brick.Index(0, 3, 4)] = 0.75f;
        var grid = SurfaceGrid.Build(Field(west, east), 0, 0, 0);

        Assert.Equal(3.75f, grid.Height[SurfaceGrid.Index(16, 4)], 4);
        Assert.Equal(1f, grid.Own[SurfaceGrid.Index(16, 4)]);
        Assert.Equal(1f, grid.Shown[SurfaceGrid.Index(16, 4)]);
    }

    [Fact]
    public void FallingWaterFoamsAndTheCurrentIsTheTops()
    {
        var view = new BrickView(0, 0, 0, 1f);
        int v = Brick.Index(3, 2, 3);
        view.Water[v] = 0.6f;
        view.VelocityX[v] = 1.5f;
        view.VelocityZ[v] = -0.5f;
        view.VelocityY[v] = -2f;
        var grid = SurfaceGrid.Build(Field(view), 0, 0, 0);
        int k = SurfaceGrid.Index(3, 3);

        Assert.Equal(1.5f, grid.CurrentX[k]);
        Assert.Equal(-0.5f, grid.CurrentZ[k]);
        Assert.Equal(0.5f, grid.Foam[k], 4);
    }

    [Fact]
    public void ADryBrickDrawsNothing()
    {
        var grid = SurfaceGrid.Build(Field(new BrickView(0, 0, 0, 1f)), 0, 0, 0);

        Assert.False(grid.Draws);
        Assert.False(grid.DrawsSquare(0, 0));
    }

    [Fact]
    public void ABlendIsPartWayAndTakesTheNextGridsSquares()
    {
        var before = new BrickView(0, 0, 0, 1f);
        var after = new BrickView(0, 0, 0, 1f);
        before.Water[Brick.Index(2, 1, 2)] = 0.2f;
        after.Water[Brick.Index(2, 1, 2)] = 0.6f;
        after.VelocityX[Brick.Index(2, 1, 2)] = 2f;
        var from = SurfaceGrid.Build(Field(before), 0, 0, 0);
        var to = SurfaceGrid.Build(Field(after), 0, 0, 0);
        int k = SurfaceGrid.Index(2, 2);

        var half = SurfaceGrid.Blend(from, to, 0.25f);

        Assert.Equal(1.3f, half.Height[k], 4);
        Assert.Equal(0.5f, half.CurrentX[k], 4);
        Assert.Equal(to.Own[k], half.Own[k]);
        Assert.Same(to, SurfaceGrid.Blend(from, to, 1f));
        Assert.Same(to, SurfaceGrid.Blend(null, to, 0.5f));
    }

    private static VoxelField Field(params BrickView[] views)
    {
        var field = new VoxelField();
        foreach (BrickView view in views)
        {
            field.Set(view);
        }

        return field;
    }
}
