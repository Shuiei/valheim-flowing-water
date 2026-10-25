using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// The clock every brick's blend on the graphics card runs on: data coming within a moment of each other is one wave
/// and one blend, a later wave's blend takes as long as the wave before took to come round, within bounds.
/// </summary>
public class BlendClockTests
{
    [Fact]
    public void TheFirstWaveBlendsForTheDefault()
    {
        var clock = new BlendClock();
        clock.Came(10f);

        Assert.Equal(10f, clock.Start);
        Assert.Equal(BlendClock.Default, clock.Seconds);
    }

    [Fact]
    public void DataComingWithinAMomentIsOfTheWaveUnderWay()
    {
        var clock = new BlendClock();
        clock.Came(10f);
        clock.Came(10.1f);
        clock.Came(10f + BlendClock.Shortest - 0.01f);

        Assert.Equal(10f, clock.Start);
        Assert.Equal(BlendClock.Default, clock.Seconds);
    }

    [Fact]
    public void ALaterWaveBlendsForAsLongAsSinceTheWaveBefore()
    {
        var clock = new BlendClock();
        clock.Came(10f);
        clock.Came(11.2f);

        Assert.Equal(11.2f, clock.Start);
        Assert.Equal(1.2f, clock.Seconds, 4);

        // Bricks drawn again for it a frame later blend on the same clock.
        clock.Came(11.25f);
        Assert.Equal(11.2f, clock.Start);
        Assert.Equal(1.2f, clock.Seconds, 4);
    }

    [Fact]
    public void TheBlendIsHeldBetweenTheShortestAndTheLongest()
    {
        var clock = new BlendClock();
        clock.Came(0f);
        clock.Came(5f);
        Assert.Equal(BlendClock.Longest, clock.Seconds);

        clock.Came(5f + BlendClock.Shortest);
        Assert.Equal(5f + BlendClock.Shortest, clock.Start);
        Assert.Equal(BlendClock.Shortest, clock.Seconds, 4);
    }

    [Fact]
    public void TheShareGoesFromNothingToAllOverTheBlend()
    {
        var clock = new BlendClock();
        clock.Came(10f);

        Assert.Equal(0f, clock.Share(9f));
        Assert.Equal(0f, clock.Share(10f));
        Assert.Equal(0.5f, clock.Share(10.5f), 4);
        Assert.Equal(1f, clock.Share(11f));
        Assert.Equal(1f, clock.Share(20f));
    }
}
