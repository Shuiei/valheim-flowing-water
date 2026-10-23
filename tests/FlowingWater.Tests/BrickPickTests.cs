using FlowingWater.Client;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The brick the console's debug describes: the one a point is in, else the nearest over or under it in its column,
/// at any voxel size; none when no brick is in the column.
/// </summary>
public class BrickPickTests
{
    // A column of 1 m voxels at x, z 0 to 16: bricks 0 and 3 up (0 to 16 m, 48 to 64 m), and one beside it.
    private static readonly BrickView[] Bricks = [new(0, 0, 0, 1f), new(0, 3, 0, 1f), new(1, 1, 0, 1f)];

    [Theory]
    [InlineData(5f, 0f, 0)]
    [InlineData(5f, 15.9f, 0)]
    [InlineData(5f, 50f, 1)]
    [InlineData(5f, 30f, 0)]
    [InlineData(5f, 40f, 1)]
    [InlineData(5f, 90f, 1)]
    [InlineData(5f, -4f, 0)]
    [InlineData(20f, 1f, 2)]
    public void ThePointsBrickOrTheNearestInItsColumnIsPicked(float x, float y, int brick)
    {
        Assert.Same(Bricks[brick], BrickPick.Nearest(Bricks, b => b, x, y, 3f, out _));
    }

    [Fact]
    public void HowFarThePointIsFromTheBrickIsSaid()
    {
        BrickPick.Nearest(Bricks, b => b, 5f, 70f, 3f, out float above);
        BrickPick.Nearest(Bricks, b => b, 5f, 8f, 3f, out float inside);
        BrickPick.Nearest(Bricks, b => b, 20f, 10f, 3f, out float under);

        Assert.Equal(6f, above, 4);
        Assert.Equal(0f, inside);
        Assert.Equal(6f, under, 4);
    }

    [Fact]
    public void ABrickOfSmallVoxelsSpansLess()
    {
        // 0.25 m voxels: brick (1, 0, 0) spans 4 to 8 m east.
        BrickView small = new(1, 0, 0, 0.25f);

        Assert.Same(small, BrickPick.Nearest([small], b => b, 6f, 2f, 1f, out _));
        Assert.Null(BrickPick.Nearest([small], b => b, 8f, 2f, 1f, out _));
        Assert.Null(BrickPick.Nearest([small], b => b, 6f, 2f, 4.5f, out _));
    }

    [Fact]
    public void NoBrickInTheColumnIsNone()
    {
        Assert.Null(BrickPick.Nearest(Bricks, b => b, 40f, 1f, 3f, out float off));
        Assert.Equal(float.MaxValue, off);
        Assert.Null(BrickPick.Nearest(Bricks, b => b, 5f, 1f, -1f, out _));
    }
}
