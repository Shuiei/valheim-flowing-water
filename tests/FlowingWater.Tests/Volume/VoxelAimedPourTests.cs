using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Water poured sent on its way (a source facing a way): it leaves the way it was aimed, keeping its momentum as the
/// pipes carry it, where water poured plainly spreads all round; none is made or lost by aiming it, and the face it
/// goes through is the one before the voxel when it goes the negative way.
/// </summary>
public class VoxelAimedPourTests
{
    // Half-metre voxels on a flat floor 16 m square.
    private const float S = 0.5f;
    private const int Side = 32;

    [Fact]
    public void WaterAimedEastRunsEastWhereWaterPouredPlainlySpreadsAllRound()
    {
        (float aimedEast, float aimedTotal) = PourFor(2f, aimed: true);
        (float plainEast, float plainTotal) = PourFor(2f, aimed: false);

        Assert.True(aimedEast > plainEast + 1f, $"the aimed water's middle {aimedEast:0.00} m east of the source, the plain water's {plainEast:0.00} m");
        Assert.InRange(plainEast, -0.5f, 0.5f);
        Assert.Equal(4f, aimedTotal, 1);
        Assert.Equal(4f, plainTotal, 1);
    }

    [Fact]
    public void AFlowTheNegativeWayGoesThroughTheFaceBeforeTheVoxel()
    {
        var grid = new VoxelGrid(S, new TestSolids(0, TestSolids.Box(0, Side - 1, 0, Side - 1)));
        grid.PourAimed(10, 1, 10, 0.1f, -2f, 0f, 3f);

        Assert.True(grid.Locate(9, 1, 10, out Brick west, out int w));
        Assert.True(grid.Locate(10, 1, 10, out Brick here, out int i));
        Assert.Equal(-2f, west.FlowX[w]);
        Assert.Equal(0f, here.FlowX[i]);
        Assert.Equal(3f, here.FlowZ[i]);
        Assert.Equal(0.1f / (S * S * S), here.Water[i], 3);
    }

    // 2 m³/s poured for some seconds at the floor's middle, aimed east at 2 m³/s or plainly: the water's middle east of
    // the source (metres) and all the water there is.
    private static (float East, float Total) PourFor(float seconds, bool aimed)
    {
        const int Middle = Side / 2;
        const float Dt = 0.02f, Rate = 2f;
        var grid = new VoxelGrid(S, new TestSolids(0, TestSolids.Box(0, Side - 1, 0, Side - 1)));
        var solver = new VoxelSolver(grid);
        for (int k = 0; k < seconds / Dt; k++)
        {
            if (aimed)
            {
                grid.PourAimed(Middle, 0, Middle, Rate * Dt, Rate, 0f, 0f);
            }
            else
            {
                grid.Pour(Middle, 0, Middle, Rate * Dt);
            }

            solver.Step(Dt);
        }

        float sum = 0f, weighted = 0f;
        foreach (Brick brick in grid.Bricks)
        {
            for (int i = 0; i < Brick.Count; i++)
            {
                (int x, _, _) = Brick.Place(i);
                float water = brick.Water[i];
                sum += water;
                weighted += water * ((brick.X * Brick.Size) + x - Middle) * S;
            }
        }

        return (sum > 0f ? weighted / sum : 0f, grid.TotalWater());
    }
}
