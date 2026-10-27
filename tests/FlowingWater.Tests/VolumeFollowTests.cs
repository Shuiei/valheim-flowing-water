using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The volume water's grid following the camera: its height in rows, where it sits, when it moves up or down after the
/// camera, the edge band that takes the server's water, and what a move of the square reseeds.
/// </summary>
public class VolumeFollowTests
{
    [Theory]
    [InlineData(24f, 0.25f, 96)]
    [InlineData(24f, 0.5f, 48)]
    [InlineData(8f, 1f, 8)]
    [InlineData(1f, 0.25f, 8)]
    public void RowsAreTheHeightInCellsAndEightAtLeast(float height, float cell, int rows)
    {
        Assert.Equal(rows, VolumeFollow.Rows(height, cell));
    }

    [Fact]
    public void TheGridSitsCentredOnTheCameraOnACellBoundary()
    {
        // 96 cells of 0.25 m are 24 m: the bottom is 12 m under the camera, on the cell boundary below.
        float bottom = VolumeFollow.BaseY(10.3f, 96, 0.25f);

        Assert.Equal(-1.75f, bottom, 4);
        Assert.Equal(0f, bottom % 0.25f, 4);
        Assert.InRange(10.3f - (bottom + 12f), 0f, 0.25f);
    }

    [Theory]
    [InlineData(12f, false)]
    [InlineData(17.9f, false)]
    [InlineData(6.1f, false)]
    [InlineData(18.1f, true)]
    [InlineData(5.9f, true)]
    [InlineData(40f, true)]
    public void TheGridMovesOnceTheCameraLeavesItsMiddleHalf(float cameraY, bool moves)
    {
        // A grid from 0 to 24 m: its middle half is 6 to 18 m.
        Assert.Equal(moves, VolumeFollow.Rebase(cameraY, 0f, 96, 0.25f));
    }

    [Fact]
    public void ACameraClimbingAMountainIsFollowedInStepsAndAlwaysInsideTheMiddleHalf()
    {
        const int Rows = 96;
        const float Cell = 0.25f;
        float origin = VolumeFollow.BaseY(0f, Rows, Cell);
        int moves = 0;
        for (float camera = 0f; camera <= 200f; camera += 0.7f)
        {
            if (VolumeFollow.Rebase(camera, origin, Rows, Cell))
            {
                origin = VolumeFollow.BaseY(camera, Rows, Cell);
                moves++;
            }

            // Never out of the middle half once followed, and the water round the camera is always in the grid.
            Assert.False(VolumeFollow.Rebase(camera, origin, Rows, Cell));
            Assert.InRange(camera, origin + 6f, origin + 18f);
        }

        // 200 m climbed with 6 m of slack each way: a move every 6 m or so, not every frame.
        Assert.InRange(moves, 30, 36);
    }

    [Fact]
    public void ACameraBobbingAboutTheMiddleNeverMovesTheGrid()
    {
        float origin = VolumeFollow.BaseY(50f, 96, 0.25f);
        for (float camera = 45f; camera <= 55f; camera += 0.1f)
        {
            Assert.False(VolumeFollow.Rebase(camera, origin, 96, 0.25f));
        }
    }

    [Theory]
    [InlineData(0.25f, 8)]
    [InlineData(0.5f, 4)]
    [InlineData(0.3f, 7)]
    [InlineData(1f, 2)]
    public void TheEdgeBandIsTwoMetresWhateverTheCellSize(float cell, int cells)
    {
        Assert.Equal(cells, VolumeFollow.Band(cell));
        Assert.True(cells * cell >= VolumeFollow.BandMetres);
        Assert.True((cells - 1) * cell < VolumeFollow.BandMetres);
    }

    [Theory]
    [InlineData(3, 0, 2, 6)]
    [InlineData(0, -5, 2, 10)]
    [InlineData(-2, 4, 1, 4)]
    [InlineData(0, 0, 2, 0)]
    public void AMoveOfTheSquareReseedsTheStripItUncovered(int dx, int dz, int per, int cells)
    {
        Assert.Equal(cells, VolumeFollow.Reseed(dx, dz, per));
    }
}
