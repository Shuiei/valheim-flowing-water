using System;
using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>A source's sphere in the volume water grows with its rate at about one speed; a drain's reach has a floor.</summary>
public class SpringShapeTests
{
    [Theory]
    [InlineData(0.1f)]
    [InlineData(1f)]
    [InlineData(3f)]
    public void ASmallSourceKeepsTheLeastSphere(float rate)
    {
        Assert.Equal(SpringShape.LeastReach, SpringShape.Reach(rate));
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(100f)]
    [InlineData(1000f)]
    public void ABigSourcesSphereGrowsWithTheSquareRootOfItsRate(float rate)
    {
        float reach = SpringShape.Reach(rate);
        float fourTimes = SpringShape.Reach(4f * rate);

        Assert.True(reach > SpringShape.LeastReach);
        Assert.Equal(2f, fourTimes / reach, 3);
        // Its upper half passes the rate at the speed.
        Assert.Equal(rate, 2.0 * Math.PI * reach * reach * SpringShape.Speed, 2);
    }

    [Fact]
    public void AHundredCubicMetresASecondComeOutOfASphereTwoAndAHalfMetresWide()
    {
        Assert.Equal(2.303f, SpringShape.Reach(100f), 3);
    }

    [Theory]
    [InlineData(10f)]
    [InlineData(100f)]
    public void TheWholeSphereGivesTheRateAtHalfTheSpeed(float rate)
    {
        float reach = SpringShape.Reach(rate);
        float outflow = SpringShape.Outflow(rate);

        Assert.Equal(SpringShape.Speed / 2f, outflow, 3);
        Assert.Equal(rate, 4.0 * Math.PI * reach * reach * outflow, 2);
    }

    [Fact]
    public void ASmallSourcesSphereHeldUpGivesItsRateSlower()
    {
        Assert.True(SpringShape.Outflow(1f) < SpringShape.Speed / 2f);
        Assert.Equal(1f, 4.0 * Math.PI * SpringShape.LeastReach * SpringShape.LeastReach * SpringShape.Outflow(1f), 3);
    }

    [Theory]
    [InlineData(0f, 0.3f)]
    [InlineData(0.1f, 0.3f)]
    [InlineData(2f, 2f)]
    public void ADrainReachesAtLeastThirtyCentimetres(float asked, float reach)
    {
        Assert.Equal(reach, SpringShape.DrainReach(asked));
    }
}
