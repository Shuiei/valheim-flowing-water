using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// A source facing a way pours out of its outlet (<see cref="VoxelGrid.PourOutlet"/>): its rate shared out among the
/// voxels across it at the marker, none ahead of it, each share leaving the way it faces; an outlet partly against a
/// wall pours all its rate out of the rest, and one with no room at all pours nothing; a jet thrown up pours where it
/// lands (on the floor out ahead, at the foot of a wall it meets, below a cliff it leaps), not at its outlet.
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
    public void AJetUnderWaterPoursAtItsOutletThrownUpAndOn()
    {
        // Water standing over the outlet already: the jet cannot fly, and leaves its voxel the way it was aimed.
        var grid = new VoxelGrid(S, new TestSolids(0));
        grid.Pour(10, 0, 10, 1f);

        Assert.True(grid.PourOutlet(10.5f, 0.3f, 10.5f, SourceShape.Jet(0f), Rate, Dt));

        Assert.Equal(1f + (Rate * Dt), grid.WaterAt(10, 0, 10), 4);
        Assert.True(grid.Locate(10, 0, 10, out Brick brick, out int i));
        Assert.True(brick.FlowZ[i] > 0f && brick.FlowY[i] > 0f, $"north {brick.FlowZ[i]}, up {brick.FlowY[i]}");
    }

    [Fact]
    public void AJetThrownUpPoursWhereItLandsAndNoneAtTheOutletsFoot()
    {
        // A floor one voxel thick under row 1; a point's jet facing east, thrown up 45 degrees at 12 m/s (2 m³/s through
        // a point's slot is faster than that), which lands about 14.7 m out, a little more from 0.3 m over the floor.
        var grid = new VoxelGrid(S, new TestSolids(1));
        var jet = new SourceShape(false, 90f, 0f, 0f, 45f);

        Assert.True(grid.PourOutlet(10.5f, 1.3f, 10.5f, jet, Rate, Dt));

        Assert.Equal(0f, grid.WaterAt(10, 1, 10));
        float lands = 10.5f + (SourceShape.MostSpeed * SourceShape.MostSpeed / VoxelSolver.Gravity);
        Assert.Equal(Rate * Dt, grid.WaterAt((int)lands, 1, 10), 4);
        Assert.True(grid.Locate((int)lands, 1, 10, out Brick brick, out int i));
        Assert.True(brick.FlowX[i] > 0f, $"east {brick.FlowX[i]}");
        Assert.Equal(0f, brick.FlowY[i]);
        Assert.Equal(Rate * Dt, grid.TotalWater(), 4);
    }

    [Fact]
    public void AJetMeetingAWallPoursAtItsFoot()
    {
        // Ground standing 20 m high from 5 m east of the source: the jet meets it rising, and its water falls there.
        var grid = new VoxelGrid(S, new TestSolids(0, ground: (x, z) => x >= 15 ? 20f : 1f));
        var jet = new SourceShape(false, 90f, 0f, 0f, 45f);

        Assert.True(grid.PourOutlet(10.5f, 1.3f, 10.5f, jet, Rate, Dt));

        Assert.Equal(0f, grid.WaterAt(10, 1, 10));
        Assert.Equal(Rate * Dt, grid.WaterAt(14, 5, 10) + grid.WaterAt(14, 4, 10) + grid.WaterAt(14, 6, 10), 4);
    }

    [Fact]
    public void AJetOffACliffLandsAtTheCliffsFootFartherOut()
    {
        // The source on a ledge 10 m over the ground below, the ledge ending a metre east of it.
        var grid = new VoxelGrid(S, new TestSolids(0, ground: (x, z) => x <= 11 ? 11f : 1f));
        var jet = new SourceShape(false, 90f, 0f, 0f, 20f);
        (float X, float Y, float Z)? landing = grid.Lands(10.5f, 11.3f, 10.5f, new Vec3(4f * 0.94f, 4f * 0.342f, 0f));

        Assert.NotNull(landing);
        Assert.InRange(landing.Value.Y, 1f, 2f);
        Assert.InRange(landing.Value.X, 13f, 18f);
        Assert.True(grid.PourOutlet(10.5f, 11.3f, 10.5f, jet, 0.25f, Dt));
        Assert.Equal(0f, grid.WaterAt(10, 11, 10));
    }

    [Fact]
    public void ARiverAlongTheGroundStillPoursAtItsOutlet()
    {
        var grid = new VoxelGrid(S, new TestSolids(1));

        Assert.True(grid.PourOutlet(10.5f, 1.3f, 10.5f, new SourceShape(false, 90f, 0f, 0f, 0f), Rate, Dt));

        Assert.Equal(Rate * Dt, grid.WaterAt(10, 1, 10), 4);
    }
}
