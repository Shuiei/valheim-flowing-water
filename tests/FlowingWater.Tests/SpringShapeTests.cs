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
    [InlineData(10f)]
    [InlineData(100f)]
    public void TheLensDomeGivesTheRateUnderTheSpeed(float rate)
    {
        float reach = SpringShape.Reach(rate);
        float outflow = SpringShape.Outflow(rate);

        Assert.True(outflow is < SpringShape.Speed and > SpringShape.Speed / 2f);
        Assert.Equal(rate, Math.PI * reach * (reach + SpringShape.Height(rate)) * outflow, 2);
    }

    [Fact]
    public void ASmallSourceIsAHemisphereGivingItsRateOverItsDome()
    {
        Assert.Equal(SpringShape.LeastReach, SpringShape.Height(0.3f));
        Assert.Equal(0.3f, 2.0 * Math.PI * SpringShape.LeastReach * SpringShape.LeastReach * SpringShape.Outflow(0.3f), 3);
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
