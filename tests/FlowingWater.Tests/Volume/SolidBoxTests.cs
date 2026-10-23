using System;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// A piece's box, turned any way: which points it holds, which segments pass through it, what it spans, and which boxes
/// along the world's axes it may meet.
/// </summary>
public class SolidBoxTests
{
    private static readonly SolidBox Wall = SolidBox.Between(new Vec3(9.9f, 0f, 8f), new Vec3(10.1f, 4f, 12f));

    // A 2 m by 0.2 m by 2 m wall standing at (5, 1, 5), turned 45 degrees round the vertical.
    private static readonly SolidBox Turned = new(
        new Vec3(5f, 1f, 5f),
        new Vec3(MathF.Sqrt(0.5f), 0f, MathF.Sqrt(0.5f)),
        new Vec3(0f, 1f, 0f),
        new Vec3(-MathF.Sqrt(0.5f), 0f, MathF.Sqrt(0.5f)),
        new Vec3(1f, 1f, 0.1f));

    [Fact]
    public void ItHoldsThePointsInsideItAndOnItsSurface()
    {
        Assert.True(Wall.Contains(new Vec3(10f, 2f, 10f)));
        Assert.True(Wall.Contains(new Vec3(10.1f, 0f, 8f)));
        Assert.False(Wall.Contains(new Vec3(10.2f, 2f, 10f)));
        Assert.False(Wall.Contains(new Vec3(10f, 4.1f, 10f)));
    }

    [Fact]
    public void ATurnedBoxHoldsPointsAlongItsOwnAxes()
    {
        Assert.True(Turned.Contains(new Vec3(5.6f, 1f, 5.6f)));
        Assert.False(Turned.Contains(new Vec3(5.6f, 1f, 4.4f)));
        Assert.False(Turned.Contains(new Vec3(6f, 1f, 6f)));
    }

    [Fact]
    public void ASegmentCrossesItOnlyWherePartOfItIsInside()
    {
        Assert.True(Wall.Crosses(new Vec3(9.5f, 1.5f, 9.5f), new Vec3(10.5f, 1.5f, 9.5f)));
        Assert.False(Wall.Crosses(new Vec3(9.5f, 1.5f, 12.5f), new Vec3(10.5f, 1.5f, 12.5f)));
        Assert.False(Wall.Crosses(new Vec3(8.5f, 1.5f, 9.5f), new Vec3(9.5f, 1.5f, 9.5f)));
        Assert.False(Wall.Crosses(new Vec3(9.5f, 4.5f, 9.5f), new Vec3(10.5f, 4.5f, 9.5f)));

        // Along the wall, inside it, and beside it.
        Assert.True(Wall.Crosses(new Vec3(10f, 1f, 8.5f), new Vec3(10f, 1f, 9.5f)));
        Assert.False(Wall.Crosses(new Vec3(10.5f, 1f, 8.5f), new Vec3(10.5f, 1f, 9.5f)));
    }

    [Fact]
    public void ItMeetsABoxAlongTheWorldsAxesOnlyWhenNoAxisSeparatesThem()
    {
        // Over the turned wall, through it, and in its bounds' corner where the wall is not (its own axes part them).
        Assert.True(Turned.Overlaps(new Vec3(4.5f, 0f, 4.5f), new Vec3(5.5f, 2f, 5.5f)));
        Assert.True(Turned.Overlaps(new Vec3(5.6f, 1.9f, 5.6f), new Vec3(6f, 3f, 6f)));
        Assert.False(Turned.Overlaps(new Vec3(5.4f, 0f, 4.0f), new Vec3(5.7f, 2f, 4.3f)));
        Assert.False(Turned.Overlaps(new Vec3(5f, 2.5f, 5f), new Vec3(6f, 3f, 6f)));
        Assert.False(Wall.Overlaps(new Vec3(10.2f, 0f, 9f), new Vec3(11f, 1f, 10f)));
    }

    [Fact]
    public void ItsBoundsHoldItTurned()
    {
        (Vec3 min, Vec3 max) = Turned.Bounds();

        float reach = (MathF.Sqrt(0.5f) * 1f) + (MathF.Sqrt(0.5f) * 0.1f);
        Assert.Equal(5f - reach, min.X, 4);
        Assert.Equal(5f + reach, max.Z, 4);
        Assert.Equal(0f, min.Y, 4);
        Assert.Equal(2f, max.Y, 4);
    }
}
