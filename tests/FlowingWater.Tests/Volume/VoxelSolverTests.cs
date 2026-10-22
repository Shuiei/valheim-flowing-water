using System;
using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Water in the 3D grid: it falls and spreads level keeping its volume, stays still in a box, runs out of a hole
/// low in a box's side (faster from a deeper box), stays on a raised floor until it opens, levels out between two
/// chambers joined under a wall, and the sea takes it.
/// </summary>
public class VoxelSolverTests
{
    private const float Volume = 1f;

    private static readonly int[] Columns = [1, 3, 4, 6];

    [Fact]
    public void WaterPouredHighFallsAndSpreadsLevelKeepingItsVolume()
    {
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        grid.Pour(3, 6, 3, 20f);

        Run(grid, 40f);

        Assert.Equal(20f, grid.TotalWater(), 1);
        for (int z = 0; z < 8; z++)
        {
            for (int x = 0; x < 8; x++)
            {
                Assert.Equal(20f / 64f, grid.WaterAt(x, 0, z), 2);
                Assert.Equal(0f, grid.WaterAt(x, 1, z), 3);
            }
        }
    }

    [Fact]
    public void AClosedBoxHoldsItsWaterStill()
    {
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, 3.5f);

        Run(grid, 20f);

        Assert.Equal(64f * 3.5f, grid.TotalWater(), 1);
        Assert.Equal(1f, grid.WaterAt(4, 2, 4), 3);
        Assert.Equal(0.5f, grid.WaterAt(4, 3, 4), 2);
        Assert.All(grid.Bricks, b => Assert.True(b.FlowX.Concat(b.FlowY).Concat(b.FlowZ).All(f => Math.Abs(f) < 1e-3f)));
    }

    [Fact]
    public void AHoleLowInTheSideLetsWaterOutFasterFromADeeperBox()
    {
        float shallow = OutAfter(depth: 2f, seconds: 1f);
        float deep = OutAfter(depth: 6f, seconds: 1f);

        Assert.True(shallow > 0.05f, $"out of the shallow box {shallow}");
        Assert.True(deep > shallow * 1.4f, $"out of the deep box {deep}, shallow {shallow}");
    }

    [Fact]
    public void ABoxWithAHoleEmptiesDownToTheHole()
    {
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7, open: (7, 0, 3, 0))));
        Fill(grid, 0, 7, 0, 7, 3f);

        Run(grid, 120f);

        float inside = Enumerable.Range(0, 8).Sum(x => Enumerable.Range(0, 8).Sum(z => Enumerable.Range(0, 4).Sum(y => grid.WaterAt(x, y, z))));
        Assert.True(inside < 64f * 0.3f, $"left inside {inside}");
        Assert.Equal(64f * 3f, grid.TotalWater(), 0);
    }

    [Fact]
    public void WaterStaysOnARaisedFloorAndFallsWhenItOpens()
    {
        bool open = false;
        var grid = new VoxelGrid(Volume, new TestSolids(0, (x, y, z) =>
            (byte)(y == 4 && x is >= 0 and <= 7 && z is >= 0 and <= 7 && !(open && x == 3 && z == 3) ? Brick.ClosedY : 0 | TestSolids.Box(0, 7, 0, 7, y0: 5)(x, y, z))));
        Fill(grid, 0, 7, 0, 7, 1.5f, bottom: 5);

        Run(grid, 10f);
        Assert.Equal(0f, Below(grid, 5), 3);

        // The floor's middle piece breaks: its voxel's upper face opens.
        open = true;
        grid.Locate(3, 4, 3, out Brick brick, out _);
        grid.Refill(brick);
        Run(grid, 10f);

        Assert.True(Below(grid, 5) > 10f, $"fallen {Below(grid, 5)}");
        Assert.Equal(64f * 1.5f, grid.TotalWater(), 0);
    }

    [Fact]
    public void TwoChambersJoinedUnderAWallLevelOut()
    {
        // A wall between x = 3 and x = 4 from y = 1 up: below it the chambers join.
        var grid = new VoxelGrid(Volume, new TestSolids(0, (x, y, z) =>
            (byte)(TestSolids.Box(0, 7, 0, 3)(x, y, z) | (x == 3 && y >= 1 && z is >= 0 and <= 3 ? Brick.ClosedX : 0))));
        Fill(grid, 0, 3, 0, 3, 4f);
        Fill(grid, 4, 7, 0, 3, 1f);

        Run(grid, 120f);

        string columns = string.Join(" | ", Columns.Select(x => $"x{x}: " + string.Join(" ", Enumerable.Range(0, 5).Select(y => grid.WaterAt(x, y, 1).ToString("0.000", System.Globalization.CultureInfo.InvariantCulture)))));
        Assert.True(Math.Abs(grid.WaterAt(1, 2, 1) - grid.WaterAt(6, 2, 1)) < 0.05f, columns);
        Assert.True(Math.Abs(grid.WaterAt(6, 1, 1) - 1f) < 0.05f, columns);
        Assert.Equal(16f * 5f, grid.TotalWater(), 0);
    }

    [Fact]
    public void AStillPoolFallsAsleepAndWaterPouredInWakesIt()
    {
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, 2f);
        var solver = new VoxelSolver(grid);
        for (int k = 0; k < 30; k++)
        {
            solver.Step(0.1f);
        }

        Brick pool = grid.Find(0, 0, 0);
        Assert.True(pool.Quiet >= 24, $"quiet for {pool.Quiet} steps");

        // Water poured in wakes it: the pool rises evenly, the poured water does not stay where it fell.
        grid.Pour(3, 3, 3, 16f);
        Assert.Equal(0, pool.Quiet);
        Run(grid, 40f);
        Assert.Equal(1f, grid.WaterAt(0, 1, 0), 2);
        Assert.Equal(0.25f, grid.WaterAt(7, 2, 7), 2);
        Assert.Equal(64f * 2.25f, grid.TotalWater(), 1);
    }

    [Fact]
    public void WaterSettlingIntoASleepingPoolBelowWakesIt()
    {
        // A pool on ground at row 13 up to half its brick's top row, still; then water comes into the brick over it
        // without that brick counting as wet (it stays awake, the pool beside it sleeps) and settles in.
        var grid = new VoxelGrid(Volume, new TestSolids(13, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, 2.5f, bottom: 13);
        var solver = new VoxelSolver(grid);
        for (int k = 0; k < 30; k++)
        {
            solver.Step(0.1f);
        }

        Brick pool = grid.Find(0, 0, 0);
        Assert.True(pool.Asleep, $"quiet for {pool.Quiet} steps");
        grid.Locate(3, 16, 3, out Brick above, out int i);
        above.Water[i] = 0.5f;
        above.Quiet = 0;

        solver.Step(0.1f);
        Assert.False(pool.Asleep);
        Run(grid, 40f);
        Assert.Equal(0f, grid.WaterAt(3, 16, 3), 3);
        Assert.Equal(grid.WaterAt(3, 15, 3), grid.WaterAt(0, 15, 0), 3);
        Assert.Equal(grid.WaterAt(3, 15, 3), grid.WaterAt(7, 15, 7), 3);
        Assert.Equal((64f * 2.5f) + 0.5f, grid.TotalWater(), 1);
    }

    [Fact]
    public void SleepingBricksBesideAwakeOnesKeepTheVolume()
    {
        // A pool three bricks long; once it is still, its east end is fed a little every step (awake), the rest sleeps.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 47, 0, 7)));
        Fill(grid, 0, 47, 0, 7, 1.5f);
        var solver = new VoxelSolver(grid);
        for (int k = 0; k < 60; k++)
        {
            solver.Step(0.1f);
        }

        float before = grid.TotalWater();
        float poured = 0f;
        for (int k = 0; k < 300; k++)
        {
            grid.Pour(46, 1, 3, 0.002f);
            poured += 0.002f;
            solver.Step(0.1f);
        }

        Assert.True(grid.Find(0, 0, 0).Asleep || grid.Find(0, 0, 0).Quiet >= 24);
        Assert.Equal(before + poured, grid.TotalWater(), 3);
    }

    [Fact]
    public void APoolStirredHardSettlesLevelAgain()
    {
        // A pool two voxels deep, then sixteen voxels of water dropped into one voxel over it.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, 2f);
        Run(grid, 3f);
        grid.Pour(3, 3, 3, 16f);

        Run(grid, 40f);

        // Level a quarter voxel higher, the voxels under it full, and still.
        for (int z = 0; z < 8; z += 7)
        {
            for (int x = 0; x < 8; x += 7)
            {
                Assert.Equal(1f, grid.WaterAt(x, 1, z), 2);
                Assert.Equal(0.25f, grid.WaterAt(x, 2, z), 2);
            }
        }

        Assert.All(grid.Bricks, b => Assert.True(b.FlowX.Concat(b.FlowY).Concat(b.FlowZ).All(f => Math.Abs(f) < 0.02f)));
        Assert.Equal(64f * 2.25f, grid.TotalWater(), 1);
    }

    [Fact]
    public void TheSeaTakesWhatReachesIt()
    {
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        var solver = new VoxelSolver(grid) { SeaLevel = 1f };
        grid.Pour(3, 4, 3, 10f);

        for (int k = 0; k < 100; k++)
        {
            solver.Step(0.1f);
        }

        Assert.Equal(0f, grid.TotalWater(), 3);
    }

    private static float OutAfter(float depth, float seconds)
    {
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7, open: (7, 0, 3, 0))));
        Fill(grid, 0, 7, 0, 7, depth);
        float before = grid.TotalWater();
        Run(grid, seconds);
        float inside = Enumerable.Range(0, 8).Sum(x => Enumerable.Range(0, 8).Sum(z => Enumerable.Range(0, 8).Sum(y => grid.WaterAt(x, y, z))));
        return before - inside;
    }

    private static float Below(VoxelGrid grid, int y) =>
        Enumerable.Range(-4, 16).Sum(x => Enumerable.Range(-4, 16).Sum(z => Enumerable.Range(0, y).Sum(h => grid.WaterAt(x, h, z))));

    // Water this deep (voxels) over x0..x1, z0..z1, from row bottom up.
    private static void Fill(VoxelGrid grid, int x0, int x1, int z0, int z1, float depth, int bottom = 0)
    {
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                for (int y = 0; y < Math.Ceiling(depth); y++)
                {
                    grid.Pour(x, bottom + y, z, Math.Min(1f, depth - y) * grid.VoxelVolume);
                }
            }
        }
    }

    private static void Run(VoxelGrid grid, float seconds)
    {
        var solver = new VoxelSolver(grid);
        for (float t = 0f; t < seconds; t += 0.1f)
        {
            solver.Step(0.1f);
        }
    }
}
