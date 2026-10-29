using System;
using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The volume water's grid following the camera: its height in rows, where it sits, when it moves up or down after the
/// camera, the edge band that takes the server's water, what a move of the square reseeds, and how much of deep water
/// it moves when all of it would not fit.
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
    [InlineData(128, 0.25f, 1f, 64, 128)]
    [InlineData(128, 0.25f, 0.5f, 64, 85)]
    [InlineData(256, 0.25f, 1f, 64, 170)]
    [InlineData(96, 0.5f, 0.5f, 64, 42)]
    public void TheGridIsCutSoTheSolidsCoverItWithRoomToMove(int asked, float cell, float voxel, int solidRows, int rows)
    {
        Assert.Equal(rows, VolumeFollow.RowsWithinSolids(asked, cell, voxel, solidRows));
        // What is left is half again under the texture's height: a quarter of the grid's to spare above and below.
        Assert.True(rows * cell * 1.5f <= (solidRows * voxel) + 1e-4f);
    }

    [Fact]
    public void TheSolidsAreBuiltWithTheGridInTheirMiddleAndAgainOnlyOnceItLeavesThem()
    {
        // A grid 40 voxel rows tall from row 100: the 64-row texture from row 88, twelve rows spare each way.
        int built = VolumeFollow.SolidsBase(100, 140, 64);
        Assert.Equal(88, built);
        Assert.True(VolumeFollow.SolidsCover(built, 100, 140, 64));

        // Moved up twelve rows it is still covered; one more and it is not, nor moved down past the bottom.
        Assert.True(VolumeFollow.SolidsCover(built, 112, 152, 64));
        Assert.False(VolumeFollow.SolidsCover(built, 113, 153, 64));
        Assert.False(VolumeFollow.SolidsCover(built, 87, 127, 64));

        // A grid as tall as the texture has none to spare: built from its own bottom.
        Assert.Equal(100, VolumeFollow.SolidsBase(100, 164, 64));
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
    [Fact]
    public void WaterThatFitsNeedsNoFloorUnderIt()
    {
        // 100 columns of 1 m², 2 m deep: 200 m³ in a budget of 300.
        (float[] tops, float[] depths) = Pool(100, top: 50f, depth: 2f);

        Assert.Equal(0f, VolumeFollow.DeepKeep(tops, depths, 1f, 30f, 70f, 300f, 0f));
    }

    [Fact]
    public void DeepWaterKeepsTheDeepestTopThatFits()
    {
        // 100 columns 5 m deep: 500 m³; a budget of 300 holds their top 3 m.
        (float[] tops, float[] depths) = Pool(100, top: 50f, depth: 5f);

        Assert.Equal(3f, VolumeFollow.DeepKeep(tops, depths, 1f, 30f, 70f, 300f, 0f), 3);
    }

    [Fact]
    public void AtTheLeastAMetreIsKept()
    {
        (float[] tops, float[] depths) = Pool(100, top: 50f, depth: 5f);

        Assert.Equal(VolumeFollow.LeastKeep, VolumeFollow.DeepKeep(tops, depths, 1f, 30f, 70f, 50f, 0f));
    }

    [Fact]
    public void OnlyTheWaterInTheGridCounts()
    {
        // 5 m deep, but the grid starts 2 m under the top: 200 m³ in it, which fits.
        (float[] tops, float[] depths) = Pool(100, top: 50f, depth: 5f);

        Assert.Equal(0f, VolumeFollow.DeepKeep(tops, depths, 1f, 48f, 70f, 250f, 0f));
    }

    [Fact]
    public void DryColumnsHoldNothing()
    {
        (float[] tops, float[] depths) = Pool(100, top: 50f, depth: 5f);
        for (int i = 0; i < 50; i++)
        {
            depths[i] = 0f;
        }

        // Half the columns wet, 5 m deep: 250 m³ in a budget of 300.
        Assert.Equal(0f, VolumeFollow.DeepKeep(tops, depths, 1f, 30f, 70f, 300f, 0f));
    }

    [Fact]
    public void AKeptDepthIsLetBeWhileWhatItHoldsStaysNearTheBudget()
    {
        (float[] tops, float[] depths) = Pool(100, top: 50f, depth: 5f);

        // 3 m fits 300 m³: 2.6 m (260) and 3.4 m (340) kept stay, 2 m (200) and 4 m (400) go to 3.
        Assert.Equal(2.6f, VolumeFollow.DeepKeep(tops, depths, 1f, 30f, 70f, 300f, 2.6f));
        Assert.Equal(3.4f, VolumeFollow.DeepKeep(tops, depths, 1f, 30f, 70f, 300f, 3.4f));
        Assert.Equal(3f, VolumeFollow.DeepKeep(tops, depths, 1f, 30f, 70f, 300f, 2f), 3);
        Assert.Equal(3f, VolumeFollow.DeepKeep(tops, depths, 1f, 30f, 70f, 300f, 4f), 3);
    }

    [Fact]
    public void TheFloorGoesOnlyOnceAllTheWaterFitsWithRoomToSpare()
    {
        // 260 m³ fits a budget of 300, but not with a fifth to spare: the floor at 2.6 m stays; at 230 m³ it goes.
        (float[] tops, float[] depths) = Pool(100, top: 50f, depth: 2.6f);
        Assert.Equal(2.6f, VolumeFollow.DeepKeep(tops, depths, 1f, 30f, 70f, 300f, 2.6f));

        (tops, depths) = Pool(100, top: 50f, depth: 2.3f);
        Assert.Equal(0f, VolumeFollow.DeepKeep(tops, depths, 1f, 30f, 70f, 300f, 2.6f));
    }

    [Fact]
    public void TopsAndDepthsMustPair() =>
        Assert.Throws<ArgumentException>(() => VolumeFollow.DeepKeep(new float[2], new float[3], 1f, 0f, 10f, 1f, 0f));

    private static (float[] Tops, float[] Depths) Pool(int columns, float top, float depth)
    {
        float[] tops = new float[columns], depths = new float[columns];
        for (int i = 0; i < columns; i++)
        {
            tops[i] = top;
            depths[i] = depth;
        }

        return (tops, depths);
    }
}
