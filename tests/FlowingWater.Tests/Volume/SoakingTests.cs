using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// The ground soaking up water lying thin on it: a sheet on the ground loses the rate's depth, deeper water and water
/// off the ground are kept, and a source running onto a floor floods only so far.
/// </summary>
public class SoakingTests
{
    [Fact]
    public void ASheetOnTheGroundLosesTheRatesDepth()
    {
        // 5 cm on the ground (the voxel under it solid), a millimetre soaked: 4.9 cm left, the brick's change counted.
        VoxelGrid grid = OnGround();
        grid.Pour(3, 0, 3, 0.05f);
        Brick brick = grid.Find(0, 0, 0);
        int changes = brick.Changes;

        float taken = Soaking.Soak(brick, 0.001f, 1f);

        Assert.Equal(0.001f, taken, 5);
        Assert.Equal(0.049f, grid.WaterAt(3, 0, 3), 5);
        Assert.True(brick.Changes > changes);
    }

    [Fact]
    public void APuddleDriesUpAndNoMore()
    {
        // 2 mm on the ground, 5 mm soaked: none left, 2 mm taken.
        VoxelGrid grid = OnGround();
        grid.Pour(3, 0, 3, 0.002f);

        float taken = Soaking.Soak(grid.Find(0, 0, 0), 0.005f, 1f);

        Assert.Equal(0.002f, taken, 5);
        Assert.Equal(0f, grid.WaterAt(3, 0, 3));
    }

    [Fact]
    public void WaterDeeperThanASheetIsKept()
    {
        // 30 cm standing on the ground: a pool's, kept.
        VoxelGrid grid = OnGround();
        grid.Pour(3, 0, 3, 0.3f);

        Assert.Equal(0f, Soaking.Soak(grid.Find(0, 0, 0), 0.001f, 1f));
        Assert.Equal(0.3f, grid.WaterAt(3, 0, 3), 5);
    }

    [Fact]
    public void AThinTopOverDeepWaterIsKept()
    {
        // A column 1.05 m deep: its full voxel on the ground and 5 cm in the one over it. Neither is a sheet on the
        // ground (the one over stands on water, the one under has water over it).
        VoxelGrid grid = OnGround();
        Brick brick = grid.Get(0, 0, 0);
        brick.Water[Brick.Index(3, 0, 3)] = 1f;
        brick.Water[Brick.Index(3, 1, 3)] = 0.05f;
        brick.Wets(Brick.Index(3, 0, 3));
        brick.Wets(Brick.Index(3, 1, 3));

        Assert.Equal(0f, Soaking.Soak(brick, 0.001f, 1f));
    }

    [Fact]
    public void APoolsTopOverABedVoxelIsKept()
    {
        // A pool 0.8 deep: its bed voxel 0.3 ground and 0.7 water, 0.1 over it in the next row. The top stands on the
        // bed voxel's water, not on its ground: neither soaks.
        VoxelGrid grid = OnGround();
        Brick brick = grid.Get(0, 0, 0);
        brick.Solid[Brick.Index(3, 0, 3)] = 0.3f;
        brick.Water[Brick.Index(3, 0, 3)] = 0.7f;
        brick.Water[Brick.Index(3, 1, 3)] = 0.1f;
        brick.Wets(Brick.Index(3, 0, 3));
        brick.Wets(Brick.Index(3, 1, 3));

        Assert.Equal(0f, Soaking.Soak(brick, 0.001f, 1f));
        Assert.Equal(0.1f, brick.Water[Brick.Index(3, 1, 3)], 5);
    }

    [Fact]
    public void ASheetTwoVoxelsHighSoaksOnlyOnTheGround()
    {
        // A sheet 6 cm deep laid over a voxel boundary: 4 cm in a bed voxel holding ground, 2 cm over it. Only the bed
        // voxel's water is on the ground: a millimetre soaked in all, not one in each.
        VoxelGrid grid = OnGround();
        Brick brick = grid.Get(0, 0, 0);
        brick.Solid[Brick.Index(3, 0, 3)] = 0.5f;
        brick.Water[Brick.Index(3, 0, 3)] = 0.04f;
        brick.Water[Brick.Index(3, 1, 3)] = 0.02f;
        brick.Wets(Brick.Index(3, 0, 3));
        brick.Wets(Brick.Index(3, 1, 3));

        Assert.Equal(0.001f, Soaking.Soak(brick, 0.001f, 1f), 5);
        Assert.Equal(0.02f, brick.Water[Brick.Index(3, 1, 3)], 5);
    }

    [Fact]
    public void WaterOffTheGroundIsKept()
    {
        // 5 cm in a voxel over the air (nothing under it holds ground): falling, not lying on anything.
        var grid = new VoxelGrid(1f, new TestSolids(-16));
        Brick brick = grid.Get(0, 0, 0);
        brick.Water[Brick.Index(3, 5, 3)] = 0.05f;
        brick.Wets(Brick.Index(3, 5, 3));

        Assert.Equal(0f, Soaking.Soak(brick, 0.001f, 1f));
    }

    [Fact]
    public void ASheetOnAPieceFloorSoaksToo()
    {
        // 5 cm over the air on a floor a piece makes (the face under the voxel closed): it dries as on the ground.
        var grid = new VoxelGrid(1f, new TestSolids(-16, (x, y, z) => x == 3 && y == 4 && z == 3 ? Brick.ClosedY : (byte)0));
        Brick brick = grid.Get(0, 0, 0);
        brick.Water[Brick.Index(3, 5, 3)] = 0.05f;
        brick.Wets(Brick.Index(3, 5, 3));

        Assert.Equal(0.001f, Soaking.Soak(brick, 0.001f, 1f), 5);
    }

    [Fact]
    public void ASourceOntoAFloorFloodsOnlySoFar()
    {
        // Half a cubic metre a second onto the middle of a 32 by 32 m floor for a minute: without soaking it covers ever
        // more of the floor; soaking 5 mm a second, the sheet stops spreading where it loses what feeds it (0.5 / 0.005
        // = 100 m² of thin water, a little more where it is deeper), its water a small part of what was poured.
        var dry = new VoxelGrid(1f, new TestSolids(0, TestSolids.Box(0, 31, 0, 31)));
        var soaked = new VoxelGrid(1f, new TestSolids(0, TestSolids.Box(0, 31, 0, 31)));
        Flood(dry, 0f);
        Flood(soaked, 0.005f);

        int Wet(VoxelGrid grid) => Enumerable.Range(0, 32).Sum(z => Enumerable.Range(0, 32).Count(x => grid.WaterAt(x, 0, z) > 0.002f));
        Assert.Equal(30f, dry.TotalWater(), 1);
        Assert.True(Wet(soaked) < Wet(dry) * 0.7f, $"{Wet(soaked)} wet voxels soaked, {Wet(dry)} not");
        Assert.InRange(soaked.TotalWater(), 0.5f, 10f);
    }

    // A grid whose ground is under row 0, the brick under it made (its solid voxels the ground under row 0).
    private static VoxelGrid OnGround()
    {
        var grid = new VoxelGrid(1f, new TestSolids(0));
        grid.Get(0, -1, 0);
        grid.Get(0, 0, 0);
        return grid;
    }

    // Half a cubic metre a second poured at (16, 0, 16) for 60 s, stepped a tenth of a second at a time, the ground
    // soaking so many metres a second.
    private static void Flood(VoxelGrid grid, float soak)
    {
        var solver = new VoxelSolver(grid);
        for (float t = 0f; t < 60f; t += 0.1f)
        {
            grid.Pour(16, 0, 16, 0.05f);
            solver.Step(0.1f);
            foreach (Brick brick in grid.Bricks.ToList())
            {
                Soaking.Soak(brick, soak * 0.1f, 1f);
            }
        }
    }
}
