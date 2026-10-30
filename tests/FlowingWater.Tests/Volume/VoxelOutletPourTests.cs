using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// A source facing a way pours out of its outlet (<see cref="VoxelGrid.PourOutlet"/>): its rate shared out among the
/// voxels across it at the marker, none ahead of it, each share leaving the way it faces; an outlet partly against a
/// wall pours all its rate out of the rest, and one with no room at all pours nothing.
/// </summary>
public class VoxelOutletPourTests
{
    // The server's metre voxels; a 4 m outlet facing east, its middle at (10.5, 0.3, 10.5), so across it (south) it
    // spans the voxels z 12 down to 9 of column x 10.
    private const float S = 1f, Rate = 2f, Dt = 0.5f;
    private static readonly SourceShape River = new(false, 90f, 4f, 0f, 0f);
    private static readonly int[] Across = [12, 11, 10, 9];

    [Fact]
    public void AnOutletSharesItsRateAmongTheVoxelsAcrossItAtTheMarkerEachSentTheWayItFaces()
    {
        var grid = new VoxelGrid(S, new TestSolids(0));

        Assert.True(grid.PourOutlet(10.5f, 0.3f, 10.5f, River, Rate, Dt));

        foreach (int z in Across)
        {
            Assert.Equal(Rate * Dt / 4f, grid.WaterAt(10, 0, z), 4);
            Assert.True(grid.Locate(10, 0, z, out Brick brick, out int i));
            Assert.Equal(Rate / 4f, brick.FlowX[i], 4);
            Assert.Equal(0f, brick.FlowZ[i], 4);
        }

        Assert.Equal(0f, grid.WaterAt(11, 0, 10));
        Assert.Equal(Rate * Dt, grid.TotalWater(), 4);
    }

    [Fact]
    public void AnOutletPartlyAgainstAWallPoursAllItsRateOutOfTheRest()
    {
        // Ground standing past what Room looks up through over the outlet's two northern voxels.
        var grid = new VoxelGrid(S, new TestSolids(0, ground: (x, z) => x == 10 && z >= 11 ? 200f : 0f));

        Assert.True(grid.PourOutlet(10.5f, 0.3f, 10.5f, River, Rate, Dt));

        Assert.Equal(Rate * Dt / 2f, grid.WaterAt(10, 0, 10), 4);
        Assert.Equal(Rate * Dt / 2f, grid.WaterAt(10, 0, 9), 4);
        Assert.Equal(Rate * Dt, grid.TotalWater(), 4);
    }

    [Fact]
    public void AnOutletWithNoRoomPoursNothing()
    {
        var grid = new VoxelGrid(S, new TestSolids(0)) { MaxBricks = 0 };

        Assert.False(grid.PourOutlet(10.5f, 0.3f, 10.5f, River, Rate, Dt));
        Assert.Equal(0f, grid.TotalWater());
    }

    [Fact]
    public void APointsOutletPoursAllItsRateIntoTheMarkersVoxelThrownUpAndOn()
    {
        var grid = new VoxelGrid(S, new TestSolids(0));

        Assert.True(grid.PourOutlet(10.5f, 0.3f, 10.5f, SourceShape.Jet(0f), Rate, Dt));

        Assert.Equal(Rate * Dt, grid.WaterAt(10, 0, 10), 4);
        Assert.True(grid.Locate(10, 0, 10, out Brick brick, out int i));
        Assert.True(brick.FlowZ[i] > 0f && brick.FlowY[i] > 0f, $"north {brick.FlowZ[i]}, up {brick.FlowY[i]}");
        Assert.Equal(Rate * Dt, grid.TotalWater(), 4);
    }
}
