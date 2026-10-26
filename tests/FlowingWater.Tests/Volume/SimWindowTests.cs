using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// The square the card moves the water in: round the camera, in whole groups of eight columns, cells a whole number to
/// a voxel's side and never larger than one, no more than the card's most, moved only once the camera is a quarter of
/// it from its middle.
/// </summary>
public class SimWindowTests
{
    [Fact]
    public void TheSquareLiesRoundTheCameraInGroupsOfEightColumns()
    {
        var window = SimWindow.Around(1000.4f, -503.7f, 1f, 96f, 0.25f);

        Assert.Equal(96, window.Columns);
        Assert.Equal(4, window.PerVoxel);
        Assert.Equal(384, window.Cells);
        Assert.Equal(0.25f, window.CellSize);
        Assert.Equal(0, window.X0 % 8);
        Assert.Equal(0, window.Z0 % 8);
        Assert.InRange(1000.4f - (window.X0 * 1f), 40f, 56f);
        Assert.InRange(-503.7f - (window.Z0 * 1f), 40f, 56f);
    }

    [Theory]
    [InlineData(0.25f, 0.25f, 1)]
    [InlineData(0.25f, 0.5f, 1)]
    [InlineData(0.5f, 0.25f, 2)]
    [InlineData(1f, 0.5f, 2)]
    public void CellsAreAWholeNumberToAVoxelAndNoLargerThanOne(float voxel, float cell, int perVoxel)
    {
        var window = SimWindow.Around(0f, 0f, voxel, 64f, cell);

        Assert.Equal(perVoxel, window.PerVoxel);
        Assert.True(window.CellSize <= voxel);
    }

    [Fact]
    public void ASquareTooBigForTheCardIsCut()
    {
        var window = SimWindow.Around(0f, 0f, 0.25f, 1000f, 0.25f);

        Assert.Equal(1024, window.Cells);
        Assert.Equal(256f, window.Size);
    }

    [Fact]
    public void TheSquareStaysUntilTheCameraIsAQuarterOfItFromItsMiddle()
    {
        var window = SimWindow.Around(500f, 500f, 1f, 96f, 0.25f);
        float middle = (window.X0 + 48) * 1f;

        Assert.True(window.Keeps(middle + 23f, middle));
        Assert.False(window.Keeps(middle + 25f, middle));
        Assert.False(window.Keeps(middle, middle - 25f));
        Assert.Equal(window, SimWindow.Around(501f, 499f, 1f, 96f, 0.25f));
    }
}
