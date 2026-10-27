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
    public void TheGridSitsWithMoreOfItsHeightAboveTheCameraOnACellBoundary()
    {
        // 96 cells of 0.25 m are 24 m: the bottom is 7.2 m under the camera (seven tenths of the grid above it), on
        // the cell boundary below.
        float bottom = VolumeFollow.BaseY(10.3f, 96, 0.25f);

        Assert.Equal(3f, bottom, 4);
        Assert.Equal(0f, bottom % 0.25f, 4);
        Assert.InRange(10.3f - (bottom + 7.2f), 0f, 0.25f);
    }

    [Theory]
    [InlineData(7.2f, false)]
    [InlineData(13.1f, false)]
    [InlineData(1.3f, false)]
    [InlineData(13.3f, true)]
    [InlineData(1.1f, true)]
    [InlineData(40f, true)]
    public void TheGridMovesOnceTheCameraLeavesTheMiddleHalfOfItsPlay(float cameraY, bool moves)
    {
        // A grid from 0 to 24 m sits its camera at 7.2 m: it moves once the camera is 6 m from there.
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

            // Never out of its play once followed, and the water round the camera is always in the grid, more of it
            // above than below.
            Assert.False(VolumeFollow.Rebase(camera, origin, Rows, Cell));
            Assert.InRange(camera, origin + 1.2f, origin + 13.2f);
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
    [InlineData(96, 128, 96)]
    [InlineData(192, 128, 127)]
    [InlineData(96, 256, 31)]
    [InlineData(96, 512, 7)]
    [InlineData(96, 1024, 1)]
    public void TheRowsAreCutSoTheGridStaysWithinWhatTheCardRunsAtOnce(int asked, int cells, int rows)
    {
        int within = VolumeFollow.RowsWithin(asked, cells);

        Assert.Equal(rows, within);
        Assert.True((long)cells * cells * within <= VolumeFollow.MostCells);
    }

    [Fact]
    public void TheDefaultGridFitsWithRoomToSpare()
    {
        // 32 m of 0.25 m cells, 24 m tall: the game's defaults.
        Assert.Equal(96, VolumeFollow.RowsWithin(VolumeFollow.Rows(24f, 0.25f), 128));
        Assert.True(128 * 128 * 96 * 8 <= 65535 * 256);
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
