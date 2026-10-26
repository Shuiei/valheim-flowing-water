using System;
using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// The water carrying its momentum: a dam let go runs to the far wall about as fast as a real one (without carrying
/// it, near three seconds), water off an edge goes on the way it went, the ground slows a thin sheet far more than deep
/// water, no speed carried runs away or is not a number, and a brick falling asleep and waking keeps no carried flow.
/// </summary>
public class VoxelMomentumTests
{
    // Quarter-metre voxels, as the lab's scenes.
    private const float S = 0.25f;

    [Fact]
    public void ADamLetGoReachesTheFarWallInAboutTwoSecondsAndStaysWhole()
    {
        // A tank 10 m long and 1 m wide; 2 m of water held over its first 2.5 m, let go. Not carrying its momentum, the
        // front took 2.9 s to reach the far wall; a real one takes about 1.3 s.
        const int Long = 40, Wide = 4;
        var grid = new VoxelGrid(S, new TestSolids(0, TestSolids.Box(0, Long - 1, 0, Wide - 1)));
        Fill(grid, 0, 9, 0, Wide - 1, 8f);
        float before = grid.TotalWater();
        var solver = new VoxelSolver(grid);

        float reached = float.NaN;
        for (int k = 1; k <= 200 && float.IsNaN(reached); k++)
        {
            solver.Step(0.02f);
            if (Depth(grid, Long - 1, Wide) > 0.01f)
            {
                reached = k * 0.02f;
            }
        }

        Assert.InRange(reached, 1f, 2.5f);
        Assert.Equal(before, grid.TotalWater(), 2);
        Assert.Equal(0, solver.TakeNotNumbers());
        Assert.All(grid.Bricks, brick => Assert.True(brick.FlowX.All(f => !float.IsNaN(f) && !float.IsInfinity(f))));
    }

    [Fact]
    public void WaterRunningOffAnEdgeGoesOnTheWayItWent()
    {
        // A channel 1 m wide on a shelf 3 m high ending at x = 4 m, fed 0.5 m3/s at its west end, then a floor: halfway
        // down, the falling water is out past the edge's own column (not carrying its momentum, it fell straight down
        // the face).
        const int Edge = 16, Long = 48, Wide = 4, Shelf = 12;
        var grid = new VoxelGrid(S, new TestSolids(0, TestSolids.Box(0, Long - 1, 0, Wide - 1), (x, z) => x < Edge ? Shelf + (0.02f * (Edge - x)) : 0f));
        var solver = new VoxelSolver(grid);
        for (int k = 0; k < 500; k++)
        {
            for (int z = 0; z < Wide; z++)
            {
                grid.Pour(0, Shelf + 1, z, 0.5f * 0.02f / Wide);
            }

            solver.Step(0.02f);
        }

        int farthest = Enumerable.Range(Edge, Long - Edge).Where(x => Enumerable.Range(0, Wide).Any(z => grid.WaterAt(x, Shelf / 2, z) > 0.005f)).DefaultIfEmpty(-1).Max();
        Assert.True(farthest > Edge, $"falling water halfway down reaches x = {farthest} (the edge's column is {Edge})");
    }

    [Fact]
    public void TheGroundSlowsAThinSheetFarMoreThanDeepWater()
    {
        // The same slope, the same 2.5 m of water let go at its top: 1 m deep, its front runs 5 m and more further in
        // 3 s than 5 cm deep (Manning's friction, by the depth to the four thirds: the thinner, the more of its speed the
        // ground takes; with a flat drag and no momentum carried, the deep front led by 4 m).
        float deep = FrontAfter(4f, 3f), thin = FrontAfter(0.2f, 3f);

        Assert.True(deep - thin >= 5f, $"the deep water's front at {deep} m, the thin water's at {thin} m");
    }

    [Fact]
    public void ABrickFallingAsleepAndWakingKeepsNoCarriedFlow()
    {
        var brick = new Brick(0, 0, 0);
        brick.CarriedX[5] = 1f;
        brick.CarriedY[5] = 2f;
        brick.CarriedZ[5] = 3f;

        brick.Rest();
        Assert.True(brick.Resting);

        brick.Wake();

        float[][] carried = [brick.CarriedX, brick.CarriedY, brick.CarriedZ];
        Assert.False(brick.Resting);
        Assert.All(carried, flows => Assert.Equal(Brick.Count, flows.Length));
        Assert.All(carried, flows => Assert.All(flows, f => Assert.Equal(0f, f)));
    }

    // Where the front of 2.5 m of water this deep (voxels) let go at the top of a 5% slope is after this long, metres.
    private static float FrontAfter(float depth, float seconds)
    {
        const int Long = 80, Wide = 4;
        var grid = new VoxelGrid(S, new TestSolids(0, TestSolids.Box(0, Long - 1, 0, Wide - 1), (x, z) => 0.05f * (Long - x)));
        for (int z = 0; z < Wide; z++)
        {
            for (int x = 0; x < 10; x++)
            {
                int floor = (int)Math.Floor(0.05f * (Long - x));
                float ground = (0.05f * (Long - x)) - floor;
                float top = ground + depth;
                for (int y = 0; y < Math.Ceiling(top); y++)
                {
                    float share = Math.Min(1f, top - y) - (y == 0 ? ground : 0f);
                    if (share > 0f)
                    {
                        grid.Pour(x, floor + y, z, share * grid.VoxelVolume);
                    }
                }
            }
        }

        var solver = new VoxelSolver(grid);
        for (float t = 0f; t < seconds; t += 0.02f)
        {
            solver.Step(0.02f);
        }

        for (int x = Long - 1; x >= 0; x--)
        {
            if (Enumerable.Range(0, Wide).Average(z => Enumerable.Range(0, 40).Sum(y => grid.WaterAt(x, y, z))) * S > 0.01f)
            {
                return (x + 1) * S;
            }
        }

        return 0f;
    }

    // Depth of water at column x (averaged across the width), metres.
    private static float Depth(VoxelGrid grid, int x, int width) =>
        Enumerable.Range(0, width).Average(z => Enumerable.Range(0, 32).Sum(y => grid.WaterAt(x, y, z))) * S;

    // Water this deep (voxels) over x0..x1, z0..z1.
    private static void Fill(VoxelGrid grid, int x0, int x1, int z0, int z1, float depth)
    {
        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                for (int y = 0; y < Math.Ceiling(depth); y++)
                {
                    grid.Pour(x, y, z, Math.Min(1f, depth - y) * grid.VoxelVolume);
                }
            }
        }
    }
}
