using System;
using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>A source's sphere in the volume water grows with its rate at about one speed; a drain's reach has a floor.</summary>
public class SpringShapeTests
{
    [Theory]
    [InlineData(0.1f)]
    [InlineData(0.3f)]
    [InlineData(0.5f)]
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
        // The disc over it passes the rate at the speed.
        Assert.Equal(rate, Math.PI * reach * reach * SpringShape.Speed, 2);
    }

    [Theory]
    [InlineData(2f, 0.798f)]
    [InlineData(10f, 1.784f)]
    [InlineData(100f, 5.642f)]
    public void ASpringsSphereWidensWithItsRate(float rate, float reach)
    {
        Assert.Equal(reach, SpringShape.Reach(rate), 3);
    }

    [Theory]
    [InlineData(2f)]
    [InlineData(10f)]
    [InlineData(100f)]
    public void AWideSpringIsALensHalfAMetreHigh(float rate)
    {
        Assert.Equal(SpringShape.Rise, SpringShape.Height(rate));
        Assert.True(SpringShape.Reach(rate) > SpringShape.Height(rate));
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(10f)]
    [InlineData(100f)]
    public void TheLensRimGivesTheRateOutwardAcrossTheGround(float rate)
    {
        float outflow = SpringShape.Outflow(rate);

        Assert.Equal(rate, 2.0 * Math.PI * SpringShape.Reach(rate) * SpringShape.Height(rate) * outflow, 2);
    }

    [Fact]
    public void ABigSourceGushesAndASmallOneSeeps()
    {
        // 100 m3/s out of a rim 5.6 m round and half a metre high; 0.3 m3/s out of the least lens's.
        Assert.Equal(5.64f, SpringShape.Outflow(100f), 2);
        Assert.True(SpringShape.Outflow(0.3f) < 0.31f);
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
