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
    public void AFilmOverAVoxelNotFullIsLaidFlatOnTheColumnsFloor()
    {
        // Ground half way up row 3, 0.2 of water on it, and a film of 0.05 in row 4 over it (water on its way down a
        // slope): the column stands at 3.75, its water 0.25 deep, not at the film's 4.05.
        var view = new BrickView(0, 0, 0, 1f);
        view.Solid[Brick.Index(5, 3, 5)] = 0.5f;
        view.Water[Brick.Index(5, 3, 5)] = 0.2f;
        view.Water[Brick.Index(5, 4, 5)] = 0.05f;
        var grid = SurfaceGrid.Build(Field(view), 0, 0, 0);
        int k = SurfaceGrid.Index(5, 5);

        Assert.Equal(3.75f, grid.Height[k], 4);
        Assert.Equal(0.25f, grid.Depth[k], 4);
    }

    [Fact]
    public void AFallsColumnOverTheAirKeepsItsTop()
    {
        // A tenth of a voxel of water in rows 4 to 6, falling through the air: its top stays at 6.1, not laid flat.
        var view = new BrickView(0, 0, 0, 1f);
        foreach (int y in new[] { 4, 5, 6 })
        {
            view.Water[Brick.Index(5, y, 5)] = 0.1f;
        }

        var grid = SurfaceGrid.Build(Field(view), 0, 0, 0);

        Assert.Equal(6.1f, grid.Height[SurfaceGrid.Index(5, 5)], 4);
    }

    [Fact]
    public void AShortFallOntoWetGroundKeepsItsTop()
    {
        // A tenth of a voxel in rows 1 and 2 through the air, over water on ground in row 0: the shell draws a fall
        // there, and the surface stays at its top, 2.1.
        var view = new BrickView(0, 0, 0, 1f);
        view.Solid[Brick.Index(5, 0, 5)] = 0.5f;
        view.Water[Brick.Index(5, 0, 5)] = 0.3f;
        view.Water[Brick.Index(5, 1, 5)] = 0.1f;
        view.Water[Brick.Index(5, 2, 5)] = 0.1f;

        var grid = SurfaceGrid.Build(Field(view), 0, 0, 0);

        Assert.Equal(2.1f, grid.Height[SurfaceGrid.Index(5, 5)], 4);
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
    public void WaterKeptInsideASolidVoxelHasNoTop()
    {
        var view = new BrickView(0, 0, 0, 1f);
        view.Water[Brick.Index(2, 0, 2)] = 0.5f;
        view.Solid[Brick.Index(2, 0, 2)] = 1f;
        var grid = SurfaceGrid.Build(Field(view), 0, 0, 0);

        Assert.Equal(0f, grid.Shown[SurfaceGrid.Index(2, 2)]);
        Assert.False(grid.Draws);
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

    [Fact]
    public void WaterDryingUpIsDrawnAsBeforeUntilItHasFadedOut()
    {
        var wet = new BrickView(0, 0, 0, 1f);
        wet.Water[Brick.Index(2, 1, 2)] = 0.6f;
        var from = SurfaceGrid.Build(Field(wet), 0, 0, 0);
        var to = SurfaceGrid.Build(Field(new BrickView(0, 0, 0, 1f)), 0, 0, 0);

        Assert.False(to.Draws);
        Assert.True(SurfaceGrid.DrawsSquare(from, to, 2, 2));
        Assert.True(SurfaceGrid.DrawsAny(from, to));
        Assert.False(SurfaceGrid.DrawsAny(to, to));

        // Blended part way and blended on from there, the square stays this brick's.
        var half = SurfaceGrid.Blend(from, to, 0.5f);
        Assert.True(half.Draws);
        Assert.Equal(1f, half.Own[SurfaceGrid.Index(2, 2)]);
        Assert.True(SurfaceGrid.DrawsSquare(half, to, 2, 2));
    }

    [Fact]
    public void ABricksGridIsReadByEveryBrickWhoseGridItChanges()
    {
        // A column 40 deep, its top in brick 2 and its bottom in brick 0: brick 2's grid reads brick 0's floor.
        var views = new BrickView[3];
        for (int b = 0; b < 3; b++)
        {
            views[b] = new BrickView(0, b, 0, 1f);
        }

        for (int y = 8; y < 48; y++)
        {
            views[y / Brick.Size].Water[Brick.Index(4, y % Brick.Size, 4)] = 1f;
        }

        views[2].Water[Brick.Index(4, 15, 4)] = 0.5f;
        var deep = SurfaceGrid.Build(Field(views), 0, 2, 0);
        views[0].Solid[Brick.Index(4, 8, 4)] = 0.5f;
        var raised = SurfaceGrid.Build(Field(views), 0, 2, 0);

        Assert.NotEqual(deep.Depth[SurfaceGrid.Index(4, 4)], raised.Depth[SurfaceGrid.Index(4, 4)]);
        Assert.Contains((0, 2, 0), SurfaceGrid.Readers);

        // The deepest a grid reads, a full column under a top Window rows under the brick, and the bricks west and
        // south whose last columns are its first.
        int deepest = ((SurfaceGrid.Window + VoxelSurface.DeepestColumn) / Brick.Size) + 1;
        Assert.Contains((0, deepest, 0), SurfaceGrid.Readers);
        Assert.Contains((-1, -1, -1), SurfaceGrid.Readers);
        Assert.DoesNotContain((1, 0, 0), SurfaceGrid.Readers);
        Assert.DoesNotContain((0, 0, 0), SurfaceGrid.Readers);
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
