using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// The grid's bricks: found by their coordinates (west, south and below the world's zero too), their voxels found,
/// linked to the bricks beside them, made when asked for after a step, and forgotten once dry for long enough.
/// </summary>
public class VoxelGridTests
{
    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(-1, -1, -1)]
    [InlineData(-300, 25, 7)]
    public void ABrickIsFoundAtItsCoordinates(int x, int y, int z)
    {
        var grid = new VoxelGrid(0.5f, new TestSolids(-1000));

        Brick brick = grid.Get(x, y, z);

        Assert.Same(brick, grid.Find(x, y, z));
        Assert.Equal((x, y, z), (brick.X, brick.Y, brick.Z));
        Assert.Null(grid.Find(x + 1, y, z));
    }

    [Theory]
    [InlineData(VoxelGrid.MaxBrick, -VoxelGrid.MaxBrick, 0)]
    [InlineData(-VoxelGrid.MaxBrick, VoxelGrid.MaxBrick - 1, VoxelGrid.MaxBrick)]
    public void BricksAtTheGridsReachKeepApart(int x, int y, int z)
    {
        var grid = new VoxelGrid(1f, new TestSolids(int.MinValue));

        Brick far = grid.Get(x, y, z);
        Brick near = grid.Get(0, 0, 0);

        Assert.Same(far, grid.Find(x, y, z));
        Assert.Same(near, grid.Find(0, 0, 0));
        Assert.Equal(2, grid.Bricks.Count);
    }

    [Theory]
    [InlineData(VoxelGrid.MaxBrick + 1, 0, 0)]
    [InlineData(0, -VoxelGrid.MaxBrick - 1, 0)]
    [InlineData(0, 0, int.MaxValue)]
    [InlineData(int.MinValue, 0, 0)]
    public void ABrickBeyondTheGridsReachIsRefused(int x, int y, int z)
    {
        var grid = new VoxelGrid(1f, new TestSolids(int.MinValue));

        Assert.Throws<System.ArgumentOutOfRangeException>(() => grid.Get(x, y, z));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => grid.Want(x, y, z));
        Assert.Empty(grid.Bricks);
    }

    [Fact]
    public void ABrickBeyondTheReachSharingAKeyWithOneThereIsStillRefused()
    {
        var grid = new VoxelGrid(1f, new TestSolids(int.MinValue));
        grid.Get(0, 0, 0);

        // 2^21 packs to the same 21 bits as 0.
        Assert.Throws<System.ArgumentOutOfRangeException>(() => grid.Get(1 << 21, 0, 0));
        Assert.Throws<System.ArgumentOutOfRangeException>(() => grid.Pour(1 << 25, 0, 0, 1f));
        Assert.Equal(0f, grid.TotalWater());
    }

    [Fact]
    public void AVoxelIsFoundInItsBrickWestAndBelowTheZeroToo()
    {
        var grid = new VoxelGrid(1f, new TestSolids(-1000));
        grid.Pour(-1, -17, 5, 0.25f);

        Assert.True(grid.Locate(-1, -17, 5, out Brick brick, out int index));
        Assert.Equal((-1, -2, 0), (brick.X, brick.Y, brick.Z));
        Assert.Equal(Brick.Index(15, 15, 5), index);
        Assert.Equal(0.25f, grid.WaterAt(-1, -17, 5));
        Assert.False(grid.Locate(40, 0, 0, out _, out _));
    }

    [Fact]
    public void BricksAreLinkedToThoseBesideThemWhicheverIsMadeFirst()
    {
        var grid = new VoxelGrid(1f, new TestSolids(-1000));
        Brick middle = grid.Get(0, 0, 0);
        Brick east = grid.Get(1, 0, 0);
        Brick below = grid.Get(0, -1, 0);

        Assert.Same(east, middle.Beside[0]);
        Assert.Same(middle, east.Beside[1]);
        Assert.Same(below, middle.Beside[3]);
        Assert.Same(middle, below.Beside[2]);
        Assert.Null(middle.Beside[4]);
    }

    [Fact]
    public void WaterPouredIsCountedInCubicMetres()
    {
        var grid = new VoxelGrid(0.5f, new TestSolids(-1000));
        grid.Pour(0, 0, 0, 0.125f);
        grid.Pour(20, 3, -4, 1f);

        Assert.Equal(1f, grid.WaterAt(0, 0, 0));
        Assert.Equal(1.125f, grid.TotalWater(), 4);
    }

    [Fact]
    public void BricksAskedForAreMadeAfterTheStepWithTheirSolids()
    {
        var grid = new VoxelGrid(1f, new TestSolids(8));
        grid.Want(0, 0, 0);
        grid.Want(0, 0, 0);

        Assert.Null(grid.Find(0, 0, 0));
        grid.MakeWanted();

        Brick brick = grid.Find(0, 0, 0);
        Assert.NotNull(brick);
        Assert.Equal(1f, brick.Solid[Brick.Index(3, 7, 3)]);
        Assert.Equal(0f, brick.Solid[Brick.Index(3, 8, 3)]);
        Assert.Single(grid.Bricks);
    }

    [Fact]
    public void ABrickHoldingOnlyATraceTooSmallToFlowIsForgotten()
    {
        var grid = new VoxelGrid(1f, new TestSolids(-1000));
        grid.Pour(0, 0, 0, Brick.Dry / 10f);

        grid.ForgetDry(10f, 8f);

        Assert.Empty(grid.Bricks);
    }

    [Fact]
    public void WaterAPieceDisplacesUnderAFloorGoesAsideNotOverTheFloor()
    {
        // A piece fills a pool's voxel with a solid floor right over it: the water moves into the voxel beside it,
        // never over the floor; where every side is shut too, it stays.
        bool built = false;
        var grid = new VoxelGrid(1f, new Cells(() => built ? [(3, 3, 3), (3, 4, 3)] : []));
        grid.Pour(3, 3, 3, 0.8f);

        built = true;
        grid.Refill(grid.Find(0, 0, 0));

        Assert.Equal(0f, grid.WaterAt(3, 3, 3), 3);
        Assert.Equal(0f, grid.WaterAt(3, 5, 3), 3);
        Assert.Equal(0.8f, grid.WaterAt(4, 3, 3), 3);

        var shut = new VoxelGrid(1f, new Cells(() => built ? [(3, 3, 3), (3, 4, 3), (4, 3, 3), (2, 3, 3), (3, 3, 4), (3, 3, 2), (3, 2, 3)] : []));
        built = false;
        shut.Pour(3, 3, 3, 0.8f);
        built = true;
        shut.Refill(shut.Find(0, 0, 0));

        Assert.Equal(0.8f, shut.WaterAt(3, 3, 3), 3);
        Assert.Equal(0.8f, shut.TotalWater(), 3);
    }

    [Fact]
    public void WaterInAVoxelThatTurnsSolidClimbsIntoTheBrickAboveIfNeedBe()
    {
        int ground = 0;
        var grid = new VoxelGrid(1f, new FuncSolids((_, y, _) => y < ground ? 1f : 0f));
        grid.Pour(3, 15, 3, 0.5f);
        Brick brick = grid.Find(0, 0, 0);

        // The ground rises to cover the whole brick (a piece built there): the top row's water goes up a brick.
        ground = 16;
        grid.Refill(brick);

        Assert.Equal(0f, grid.WaterAt(3, 15, 3));
        Assert.Equal(0.5f, grid.WaterAt(3, 16, 3));
        Assert.Equal(0.5f, grid.TotalWater(), 4);
    }

    [Fact]
    public void DryBricksAreForgottenAndUnlinkedWetOnesKept()
    {
        var grid = new VoxelGrid(1f, new TestSolids(-1000));
        grid.Pour(0, 0, 0, 1f);
        Brick dry = grid.Get(1, 0, 0);

        grid.ForgetDry(5f, 8f);
        Assert.NotNull(grid.Find(1, 0, 0));
        grid.ForgetDry(5f, 8f);

        Assert.Null(grid.Find(1, 0, 0));
        Assert.NotNull(grid.Find(0, 0, 0));
        Assert.Null(grid.Find(0, 0, 0).Beside[0]);
        Assert.Null(dry.Beside[1]?.Beside[0]);
    }

    // Solid shares from a function of the voxel (x, y, z).
    private sealed class FuncSolids : ISolids
    {
        private readonly System.Func<int, int, int, float> _solid;

        public FuncSolids(System.Func<int, int, int, float> solid)
        {
            _solid = solid;
        }

        public void Fill(Brick brick, float voxelSize)
        {
            for (int i = 0; i < Brick.Count; i++)
            {
                (int x, int y, int z) = Brick.Place(i);
                brick.Solid[i] = _solid((brick.X * Brick.Size) + x, (brick.Y * Brick.Size) + y, (brick.Z * Brick.Size) + z);
            }
        }
    }

    // Whole voxels solid where a function says, nothing else.
    private sealed class Cells(System.Func<(int X, int Y, int Z)[]> solid) : ISolids
    {
        public void Fill(Brick brick, float voxelSize)
        {
            foreach ((int x, int y, int z) in solid())
            {
                if (x >> 4 == brick.X && y >> 4 == brick.Y && z >> 4 == brick.Z)
                {
                    brick.Solid[Brick.Index(x & 15, y & 15, z & 15)] = 1f;
                }
            }
        }
    }
}
