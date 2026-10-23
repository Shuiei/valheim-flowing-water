using System;
using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Water in the 3D grid: it falls and spreads level keeping its volume, stays still in a box, runs out of a hole
/// low in a box's side (faster from a deeper box), stays on a raised floor until it opens, levels out between two
/// chambers joined under a wall, settles level again when stirred (deep or not), and the sea takes it.
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
        VoxelGrid grid = StillPool(0, 2f, out _);
        Brick pool = grid.Find(0, 0, 0);
        Assert.True(pool.Quiet >= 1f, $"quiet for {pool.Quiet} s");

        // Water poured in wakes it: the pool rises evenly, the poured water does not stay where it fell.
        grid.Pour(3, 3, 3, 16f);
        Assert.Equal(0f, pool.Quiet);
        Run(grid, 40f);
        Assert.Equal(1f, grid.WaterAt(0, 1, 0), 2);
        Assert.Equal(0.25f, grid.WaterAt(7, 2, 7), 2);
        Assert.Equal(64f * 2.25f, grid.TotalWater(), 1);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(0.25f)]
    public void StillWaterSleepsAfterAboutASecondAtAnyVoxelSize(float voxelSize)
    {
        var grid = new VoxelGrid(voxelSize, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, 2f);
        var solver = new VoxelSolver(grid);
        Brick pool = grid.Find(0, 0, 0);
        float seconds = 0f;
        int steps = 0;
        while (!pool.Asleep && seconds < 5f)
        {
            float step = solver.MaxStep;
            solver.Step(step);
            seconds += step;
            steps++;
        }

        Assert.True(seconds is > 1f and < 1.2f, $"asleep after {seconds} s ({steps} steps)");
        if (voxelSize == 1f)
        {
            // As when it was counted in steps: 24 quiet steps, then the step that finds it asleep.
            Assert.Equal(25, steps);
        }
    }

    [Fact]
    public void AThinSheetOfSmallVoxelsLevelsOutBeforeItSleeps()
    {
        // 4 m by 1 m of 0.25 m voxels: a sheet 0.6 voxels deep on one half, 0.2 on the other.
        var grid = new VoxelGrid(0.25f, new TestSolids(0, TestSolids.Box(0, 15, 0, 3)));
        Fill(grid, 0, 7, 0, 3, 0.6f);
        Fill(grid, 8, 15, 0, 3, 0.2f);

        Run(grid, 30f);

        float low = Enumerable.Range(0, 16).Min(x => grid.WaterAt(x, 0, 1));
        float high = Enumerable.Range(0, 16).Max(x => grid.WaterAt(x, 0, 1));
        Assert.True(high - low < 0.01f, $"from {low} to {high}");
        Assert.Equal(16 * 4 * 0.4f * grid.VoxelVolume, grid.TotalWater(), 3);
    }

    [Fact]
    public void WaterSettlingIntoASleepingPoolBelowWakesIt()
    {
        // A pool on ground at row 13 up to half its brick's top row, still; then water comes into the brick over it
        // without that brick counting as wet (it stays awake, the pool beside it sleeps) and settles in.
        VoxelGrid grid = StillPool(13, 2.5f, out VoxelSolver solver);
        Brick pool = grid.Find(0, 0, 0);
        Assert.True(pool.Asleep, $"quiet for {pool.Quiet} s");
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
    public void AFloorOpeningInADryBrickWakesTheSleepingPoolOnIt()
    {
        // A pool in the brick over a floor whose faces are those of the dry brick's top row.
        bool open = false;
        var grid = new VoxelGrid(Volume, new TestSolids(0, (x, y, z) =>
            (byte)(TestSolids.Box(0, 7, 0, 7)(x, y, z) | (!open && y == 15 && x is >= 0 and <= 7 && z is >= 0 and <= 7 ? Brick.ClosedY : 0))));
        Fill(grid, 0, 7, 0, 7, 1.5f, bottom: 16);
        Still(grid);
        Brick pool = grid.Find(0, 1, 0);
        Assert.True(pool.Asleep, $"quiet for {pool.Quiet} s");

        // The floor goes: only the brick under it reads its solids again.
        open = true;
        grid.Refill(grid.Get(0, 0, 0));
        Run(grid, 10f);

        Assert.True(Below(grid, 16) > 10f, $"fallen {Below(grid, 16)}");
        Assert.Equal(64f * 1.5f, grid.TotalWater(), 0);
    }

    [Fact]
    public void ASleepingPoolCountsNoChangesUntilWaterComes()
    {
        VoxelGrid grid = StillPool(0, 2f, out VoxelSolver solver);
        Brick pool = grid.Find(0, 0, 0);
        solver.Step(0.1f);
        int asleep = pool.Changes;

        Run(grid, 2f);
        Assert.Equal(asleep, pool.Changes);

        grid.Pour(3, 2, 3, 1f);
        Assert.NotEqual(asleep, pool.Changes);
    }

    [Fact]
    public void AStepOverStillWaterIsIdleUntilWaterComes()
    {
        VoxelGrid grid = StillPool(0, 2f, out VoxelSolver solver);
        solver.Step(0.1f);
        float before = grid.TotalWater();

        Assert.Equal(StepState.Idle, solver.Advance(solver.MaxStep, () => true, int.MaxValue));
        Assert.False(solver.InStep);
        solver.Step(5f);
        Assert.Equal(before, grid.TotalWater(), 3);

        grid.Pour(3, 2, 3, 1f);
        Assert.NotEqual(StepState.Idle, solver.Advance(solver.MaxStep, () => true, int.MaxValue));
    }

    [Fact]
    public void ADryGridIsIdle()
    {
        var solver = new VoxelSolver(new VoxelGrid(Volume, new TestSolids(0)));

        Assert.Equal(StepState.Idle, solver.Advance(0.1f, () => true, int.MaxValue));
    }

    [Fact]
    public void ATraceSettlingIntoASleepingPoolCountsAChangeWithoutWakingIt()
    {
        // As in the test above, but too little water to wake the pool: its data still changed.
        VoxelGrid grid = StillPool(13, 2.5f, out VoxelSolver solver);
        Brick pool = grid.Find(0, 0, 0);
        int before = pool.Changes;
        grid.Locate(3, 16, 3, out Brick above, out int i);
        above.Water[i] = 5e-5f;
        above.Quiet = 0;

        solver.Step(0.1f);

        Assert.True(pool.Asleep);
        Assert.NotEqual(before, pool.Changes);
    }

    [Fact]
    public void ASleepingBrickRestsAndWakesWithItsWaterAsItWas()
    {
        // Two pools in bricks apart: the west one still, the east one fed all along (awake). The sleeping brick holds
        // no flows, heads or shares of its own, and nothing the awake one does writes into what it reads instead.
        var grid = new VoxelGrid(Volume, new TestSolids(0, (x, y, z) => (byte)(TestSolids.Box(0, 7, 0, 7)(x, y, z) | TestSolids.Box(32, 39, 0, 7)(x, y, z))));
        Fill(grid, 0, 7, 0, 7, 1.5f);
        Fill(grid, 32, 39, 0, 7, 1.5f);
        var solver = new VoxelSolver(grid);
        for (int k = 0; k < 100; k++)
        {
            grid.Pour(35, 1, 3, 0.01f);
            solver.Step(0.1f);
        }

        Brick west = grid.Find(0, 0, 0);
        Assert.True(west.Resting);
        Assert.False(grid.Find(2, 0, 0).Resting);
        Assert.All(west.FlowX.Concat(west.FlowY).Concat(west.FlowZ).Concat(west.Head).Concat(west.Share), v => Assert.Equal(0f, v));
        float water = west.Water.Sum();

        grid.Pour(3, 1, 3, 1f);
        solver.Step(0.1f);

        Assert.False(west.Resting);
        Assert.Equal(water + 1f, west.Water.Sum(), 1);
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

        Assert.True(grid.Find(0, 0, 0).Asleep || grid.Find(0, 0, 0).Quiet >= 1f);
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
    public void ADeepPoolStirredHardSettlesLevelAgain()
    {
        // 24.5 voxels deep, twice as deep as the least stiffness holds: sixteen voxels of water dropped on it.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, 24.5f);
        grid.Pour(3, 26, 3, 16f);

        Run(grid, 90f);

        Assert.All(grid.Bricks, b => Assert.True(b.FlowX.Concat(b.FlowY).Concat(b.FlowZ).All(f => Math.Abs(f) < 0.02f)));
        for (int y = 0; y < 25; y++)
        {
            Assert.Equal(grid.WaterAt(0, y, 0), grid.WaterAt(7, y, 7), 2);
        }

        // The poured water spread over the pool: its top row a quarter voxel higher, far from where it fell too.
        Assert.Equal(0.75f, grid.WaterAt(0, 24, 0), 2);
        Assert.Equal(0.75f, grid.WaterAt(7, 24, 0), 2);
        Assert.Equal(64f * 24.75f, grid.TotalWater(), 0);
    }

    [Fact]
    public void AStepTakenAPartAtATimeEndsTheSameAsAWholeOne()
    {
        // The same splash, stepped whole in one grid and a brick at a time (stopping after each) in another.
        VoxelGrid whole = Splash(), parts = Splash();
        var wholeSolver = new VoxelSolver(whole);
        var partsSolver = new VoxelSolver(parts);
        int calls = 0;
        for (int k = 0; k < 40; k++)
        {
            float step = wholeSolver.MaxStep;
            wholeSolver.Step(step);
            bool more = true;
            StepState state;
            do
            {
                calls++;
                more = !more;
                bool once = more;
                state = partsSolver.Advance(step, () => once = !once, 1);
                Assert.Equal(state == StepState.Pending, partsSolver.InStep);
            }
            while (state == StepState.Pending);
        }

        Assert.True(calls > 40 * 10, $"{calls} calls");
        Assert.Equal(Volumes(whole), Volumes(parts));
    }

    [Fact]
    public void AStepTooLongForTheDeepWaterItFindsIsNotTaken()
    {
        // A pool 24.5 voxels deep, never stepped: a step as long as shallow water allows finds it and is not taken.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, 24.5f);
        var solver = new VoxelSolver(grid);
        float shallow = solver.MaxStep;
        float before = grid.TotalWater();

        StepState state = solver.Advance(shallow, () => true, int.MaxValue);

        Assert.Equal(StepState.Again, state);
        Assert.False(solver.InStep);
        Assert.True(solver.MaxStep < shallow);
        Assert.Equal(StepState.Done, solver.Advance(solver.MaxStep, () => true, int.MaxValue));
        Assert.Equal(before, grid.TotalWater(), 1);
    }

    [Fact]
    public void AStepFindsTheBoxOfTheWetVoxels()
    {
        // Water poured in two corners of a closed box falls to its floor: the wet box spans both, one row high.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        grid.Pour(1, 4, 2, 0.5f);
        grid.Pour(6, 3, 5, 0.5f);

        Run(grid, 1f);

        Brick brick = grid.Find(0, 0, 0);
        Assert.Equal(0, brick.WetLow.Y);
        Assert.Equal(0, brick.WetHigh.Y);
        Assert.True(brick.WetLow.X <= 1 && brick.WetHigh.X >= 6, $"x {brick.WetLow.X}..{brick.WetHigh.X}");
        Assert.True(brick.WetLow.Z <= 2 && brick.WetHigh.Z >= 5, $"z {brick.WetLow.Z}..{brick.WetHigh.Z}");
        Assert.All(Enumerable.Range(0, Brick.Count).Where(i => brick.Water[i] > Brick.Dry), i =>
        {
            (int x, int y, int z) = Brick.Place(i);
            Assert.InRange(x, brick.WetLow.X, brick.WetHigh.X);
            Assert.InRange(y, brick.WetLow.Y, brick.WetHigh.Y);
            Assert.InRange(z, brick.WetLow.Z, brick.WetHigh.Z);
        });
    }

    [Fact]
    public void AShallowPoolKeepsTheStepItAlwaysHad()
    {
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, 3f);
        var solver = new VoxelSolver(grid);
        float before = solver.MaxStep;

        for (int k = 0; k < 20; k++)
        {
            solver.Step(0.1f);
        }

        Assert.Equal(0.5f / (float)Math.Sqrt(VoxelSolver.Gravity * 13f), before, 4);
        Assert.Equal(before, solver.MaxStep, 4);
    }

    [Fact]
    public void DeepWaterShortensTheStepOnceFoundAndEasesBackAfter()
    {
        // A pool 24.5 voxels deep: the step shortens at once. Then all but its bottom three voxels go (as if drained)
        // and what is left is stirred: as it moves, the step lengthens again.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, 24.5f);
        var solver = new VoxelSolver(grid);
        float shallow = solver.MaxStep;

        solver.Step(0.01f);
        float deep = solver.MaxStep;
        foreach (Brick brick in grid.Bricks)
        {
            for (int i = 0; i < Brick.Count; i++)
            {
                if ((brick.Y * Brick.Size) + Brick.Place(i).Y >= 3)
                {
                    brick.Water[i] = 0f;
                }
            }
        }

        grid.Pour(3, 5, 3, 8f);
        for (int k = 0; k < 100; k++)
        {
            solver.Step(0.1f);
        }

        Assert.True(deep < shallow * 0.6f, $"deep {deep}, shallow {shallow}");
        Assert.True(solver.MaxStep > deep * 1.5f, $"after {solver.MaxStep}, deep {deep}");
    }

    [Fact]
    public void ADeepPoolFedSlowlySettlesAndSleeps()
    {
        // 15.5 voxels deep, sixteen voxels of water poured over four seconds.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, 15.5f);
        var solver = new VoxelSolver(grid);
        for (int k = 0; k < 40; k++)
        {
            grid.Pour(3, 17, 3, 0.4f);
            solver.Step(0.1f);
        }

        for (int k = 0; k < 900; k++)
        {
            solver.Step(0.1f);
        }

        Assert.All(grid.Bricks.Where(b => b.WetHigh.X >= 0), b => Assert.True(b.Asleep, $"brick {b.Y} awake"));
        Assert.Equal(0.75f, grid.WaterAt(0, 15, 0), 2);
        Assert.Equal(64f * 15.75f, grid.TotalWater(), 0);
    }

    [Theory]
    [InlineData(12.5f)]
    [InlineData(40.5f)]
    public void PoolsOfManyDepthsSettleStillAfterASplash(float depth)
    {
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, depth);
        grid.Pour(3, (int)Math.Ceiling(depth) + 1, 3, 16f);

        Run(grid, 90f);

        Assert.All(grid.Bricks, b => Assert.True(b.FlowX.Concat(b.FlowY).Concat(b.FlowZ).All(f => Math.Abs(f) < 0.02f)));
        Assert.Equal(64f * (depth + 0.25f), grid.TotalWater(), 0);
    }

    [Fact]
    public void WaterCrossesEveryFaceOfABrickAlike()
    {
        // Poured high over the corner four bricks share, in a box spanning them: it falls through the bricks under it
        // and spreads into all four alike.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(8, 23, 8, 23)));
        grid.Pour(16, 20, 16, 64f);

        Run(grid, 60f);

        float level = grid.WaterAt(8, 0, 8);
        Assert.Equal(64f / 256f, level, 2);
        Assert.Equal(level, grid.WaterAt(23, 0, 23), 2);
        Assert.Equal(level, grid.WaterAt(8, 0, 23), 2);
        Assert.Equal(level, grid.WaterAt(23, 0, 8), 2);
        Assert.Equal(64f, grid.TotalWater(), 1);
    }

    [Fact]
    public void AVoxelTheWaterLeftHoldsNoFlow()
    {
        // Water poured high falls into the sea, which takes it: once dry, no face keeps a flow from its fall.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        grid.Pour(3, 6, 3, 20f);
        var solver = new VoxelSolver(grid) { SeaLevel = 1f };

        for (int k = 0; k < 200; k++)
        {
            solver.Step(0.1f);
        }

        Assert.Equal(0f, grid.TotalWater(), 3);
        Assert.All(grid.Bricks, b => Assert.True(b.FlowX.Concat(b.FlowY).Concat(b.FlowZ).All(f => Math.Abs(f) < 1e-4f)));
    }

    [Fact]
    public void ABrickMadeAfterStepsBeganIsSteppedToo()
    {
        // The solver's columns of bricks are made again when bricks come: water poured where there was none falls.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, 1f);
        var solver = new VoxelSolver(grid);
        for (int k = 0; k < 10; k++)
        {
            solver.Step(0.1f);
        }

        grid.Pour(3, 40, 3, 4f);
        for (int k = 0; k < 100; k++)
        {
            solver.Step(0.1f);
        }

        Assert.Equal(0f, grid.WaterAt(3, 40, 3), 3);
        Assert.Equal(1f + (4f / 64f), grid.WaterAt(0, 0, 0) + grid.WaterAt(0, 1, 0), 2);
    }

    [Fact]
    public void WaterReachingABricksEdgeMakesTheBrickBesideAndRunsIntoIt()
    {
        // A channel two bricks long, the water poured at its west end only.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 31, 0, 3)));
        Fill(grid, 0, 3, 0, 3, 2f);
        Assert.Null(grid.Find(1, 0, 0));

        Run(grid, 30f);

        Assert.NotNull(grid.Find(1, 0, 0));
        Assert.Equal(grid.WaterAt(2, 0, 1), grid.WaterAt(29, 0, 1), 2);
        Assert.Equal(16f * 2f, grid.TotalWater(), 1);
    }

    [Fact]
    public void OneLongStepIsTakenInStableShortOnes()
    {
        // Forty seconds asked for at once: the water settles level and still as it does a tenth at a time.
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7)));
        grid.Pour(3, 6, 3, 20f);

        new VoxelSolver(grid).Step(40f);

        Assert.Equal(20f, grid.TotalWater(), 1);
        Assert.Equal(20f / 64f, grid.WaterAt(0, 0, 0), 2);
        Assert.Equal(20f / 64f, grid.WaterAt(7, 0, 7), 2);
        Assert.All(grid.Bricks, b => Assert.True(b.FlowX.Concat(b.FlowY).Concat(b.FlowZ).All(f => Math.Abs(f) < 0.01f)));
    }

    [Fact]
    public void WaterAtTheGridsReachIsHeldThereNotLost()
    {
        // A brick at the farthest place the grid reaches, water poured at its east edge: no brick past it is asked for.
        int edge = (VoxelGrid.MaxBrick * Brick.Size) + Brick.Size - 1;
        var grid = new VoxelGrid(Volume, new TestSolids(int.MinValue));
        grid.Pour(edge, 0, 0, 4f);

        Run(grid, 2f);

        Assert.DoesNotContain(grid.Bricks, b => !VoxelGrid.Within(b.X, b.Y, b.Z));
        Assert.Equal(4f, grid.TotalWater(), 1);
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

    // A pool two voxels deep in a box two bricks long with sixteen voxels of water dropped on it.
    private static VoxelGrid Splash()
    {
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 23, 0, 7)));
        Fill(grid, 0, 23, 0, 7, 2f);
        grid.Pour(12, 5, 3, 16f);
        return grid;
    }

    private static float[] Volumes(VoxelGrid grid) =>
        [.. grid.Bricks.OrderBy(b => VoxelGrid.Key(b.X, b.Y, b.Z)).SelectMany(b => b.Water.Concat(b.FlowX).Concat(b.FlowY).Concat(b.FlowZ))];

    private static float OutAfter(float depth, float seconds)
    {
        var grid = new VoxelGrid(Volume, new TestSolids(0, TestSolids.Box(0, 7, 0, 7, open: (7, 0, 3, 0))));
        Fill(grid, 0, 7, 0, 7, depth);
        float before = grid.TotalWater();
        Run(grid, seconds);
        float inside = Enumerable.Range(0, 8).Sum(x => Enumerable.Range(0, 8).Sum(z => Enumerable.Range(0, 8).Sum(y => grid.WaterAt(x, y, z))));
        return before - inside;
    }

    // A pool this deep in an 8 x 8 box on ground under row ground, stepped for three seconds (long enough to fall
    // asleep).
    private static VoxelGrid StillPool(int ground, float depth, out VoxelSolver solver)
    {
        var grid = new VoxelGrid(Volume, new TestSolids(ground, TestSolids.Box(0, 7, 0, 7)));
        Fill(grid, 0, 7, 0, 7, depth, bottom: ground);
        solver = Still(grid);
        return grid;
    }

    // The grid stepped for three seconds (long enough for still water to fall asleep), and the solver that did it.
    private static VoxelSolver Still(VoxelGrid grid)
    {
        var solver = new VoxelSolver(grid);
        for (int k = 0; k < 30; k++)
        {
            solver.Step(0.1f);
        }

        return solver;
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
