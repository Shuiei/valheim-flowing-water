using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>How a white-water place sounds and sprays: loudness, the stream's babble against the roar, spray.</summary>
public class WhiteWaterMixTests
{
    private const float Near = 10f * 10f;

    [Fact]
    public void TheVolumeSettingScalesTheSoundAndZeroSilencesIt()
    {
        float full = WhiteWaterMix.Loudness(0.5f, 0.2f, 1f, Near);

        Assert.Equal(full / 2f, WhiteWaterMix.Loudness(0.5f, 0.2f, 0.5f, Near), 5);
        Assert.Equal(0f, WhiteWaterMix.Loudness(0.5f, 0.2f, 0f, Near));
    }

    [Fact]
    public void NothingIsHeardBeyondThirtyFiveMetres()
    {
        Assert.True(WhiteWaterMix.Loudness(0.5f, 0.2f, 1f, 35f * 35f) > 0f);
        Assert.Equal(0f, WhiteWaterMix.Loudness(0.5f, 0.2f, 1f, 35.01f * 35.01f));
    }

    [Fact]
    public void MoreWaterAndASteeperSlopeAreLouderUpToTheVolume()
    {
        Assert.True(WhiteWaterMix.Loudness(0.6f, 0f, 1f, Near) > WhiteWaterMix.Loudness(0.3f, 0f, 1f, Near));
        Assert.True(WhiteWaterMix.Loudness(0.3f, 1f, 1f, Near) > WhiteWaterMix.Loudness(0.3f, 0f, 1f, Near));
        Assert.Equal(0.7f, WhiteWaterMix.Loudness(5f, 3f, 0.7f, Near), 5);
    }

    [Theory]
    [InlineData(0.5f, 0f)]
    [InlineData(1f, 0f)]
    [InlineData(1.5f, 0.5f)]
    [InlineData(2f, 1f)]
    [InlineData(4f, 1f)]
    public void AStreamRoarsFromFortyFiveDegreesFullyFromSixtyThree(float slope, float roar)
    {
        Assert.Equal(roar, WhiteWaterMix.Roar(slope), 5);
    }

    [Theory]
    [InlineData(0.2f, 0f)]
    [InlineData(0.25f, 0f)]
    [InlineData(0.525f, 0.5f)]
    [InlineData(0.8f, 1f)]
    [InlineData(2f, 1f)]
    public void SprayFollowsTheSlope(float slope, float share)
    {
        Assert.Equal(share, WhiteWaterMix.Spray(slope, true), 5);
    }

    [Fact]
    public void TurnedOffThereIsNoSprayAtAll()
    {
        Assert.Equal(0f, WhiteWaterMix.Spray(2f, false));
        Assert.Equal(0f, WhiteWaterMix.SprayRate(1f, WhiteWaterMix.Spray(2f, false)));
    }

    [Fact]
    public void MoreWaterThrowsMoreDropsBetweenEightAndEighty()
    {
        Assert.Equal(8f, WhiteWaterMix.SprayRate(0.01f, 1f));
        Assert.Equal(60f, WhiteWaterMix.SprayRate(0.5f, 1f), 4);
        Assert.Equal(WhiteWaterMix.MaxSprayRate, WhiteWaterMix.SprayRate(5f, 1f));
        Assert.Equal(30f, WhiteWaterMix.SprayRate(0.5f, 0.5f), 4);
    }
}
