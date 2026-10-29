using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>How a place of running water sounds: how loud, and the stream's babble against the roar of rapids.</summary>
public class SoundMixTests
{
    private const float Near = 10f * 10f;

    [Fact]
    public void TheVolumeSettingScalesTheSoundAndZeroSilencesIt()
    {
        float full = SoundMix.Loudness(0.5f, 0.2f, 1f, Near);

        Assert.Equal(full / 2f, SoundMix.Loudness(0.5f, 0.2f, 0.5f, Near), 5);
        Assert.Equal(0f, SoundMix.Loudness(0.5f, 0.2f, 0f, Near));
    }

    [Fact]
    public void NothingIsHeardBeyondThirtyFiveMetres()
    {
        Assert.True(SoundMix.Loudness(0.5f, 0.2f, 1f, 35f * 35f) > 0f);
        Assert.Equal(0f, SoundMix.Loudness(0.5f, 0.2f, 1f, 35.01f * 35.01f));
    }

    [Fact]
    public void MoreWaterAndSteeperAreLouderUpToTheVolume()
    {
        Assert.True(SoundMix.Loudness(0.6f, 0f, 1f, Near) > SoundMix.Loudness(0.3f, 0f, 1f, Near));
        Assert.True(SoundMix.Loudness(0.3f, 1f, 1f, Near) > SoundMix.Loudness(0.3f, 0f, 1f, Near));
        Assert.Equal(0.7f, SoundMix.Loudness(5f, 3f, 0.7f, Near), 5);
    }

    [Theory]
    [InlineData(0.5f, 0f)]
    [InlineData(1f, 0f)]
    [InlineData(1.5f, 0.5f)]
    [InlineData(2f, 1f)]
    [InlineData(4f, 1f)]
    public void WaterRoarsFromFallingAsFastAsItRunsFullyFromTwiceAsFast(float steep, float roar)
    {
        Assert.Equal(roar, SoundMix.Roar(steep), 5);
    }
}
