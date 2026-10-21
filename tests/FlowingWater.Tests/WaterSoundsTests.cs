using System;
using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>The running-water sounds made in code: their length and level, that they loop without a click, and their character.</summary>
public class WaterSoundsTests
{
    private static readonly float[] Stream = WaterSounds.Stream();

    private static readonly float[] Rapids = WaterSounds.Rapids();

    [Fact]
    public void BothLoopsLastSixSecondsAtTheirRate()
    {
        int length = (int)(WaterSounds.SampleRate * WaterSounds.LoopSeconds);

        Assert.Equal(length, Stream.Length);
        Assert.Equal(length, Rapids.Length);
    }

    [Fact]
    public void BothLoopsPeakAtTheirLevel()
    {
        Assert.Equal(WaterSounds.Peak, Loudest(Stream), 4);
        Assert.Equal(WaterSounds.Peak, Loudest(Rapids), 4);
    }

    [Fact]
    public void TheSoundsAreTheSameEveryTimeAndDifferFromEachOther()
    {
        Assert.Equal(Stream, WaterSounds.Stream());
        Assert.NotEqual(Stream, Rapids);
    }

    [Fact]
    public void TheLoopsJoinWithoutAClick()
    {
        // The jump from the last sample back to the first is no bigger than the loop's own steps.
        Assert.True(Math.Abs(Stream[^1] - Stream[0]) <= LargestStep(Stream));
        Assert.True(Math.Abs(Rapids[^1] - Rapids[0]) <= LargestStep(Rapids));
    }

    [Fact]
    public void TheStreamHasNoRumbleUnlikeTheRapids()
    {
        // The share of the sound below about 300 Hz: a stream's bubbles ring higher, rapids roar low.
        Assert.True(LowShare(Stream) < LowShare(Rapids) / 2f);
    }

    private static float Loudest(float[] samples)
    {
        float loudest = 0f;
        foreach (float sample in samples)
        {
            loudest = Math.Max(loudest, Math.Abs(sample));
        }

        return loudest;
    }

    private static float LargestStep(float[] samples)
    {
        float largest = 0f;
        for (int i = 1; i < samples.Length; i++)
        {
            largest = Math.Max(largest, Math.Abs(samples[i] - samples[i - 1]));
        }

        return largest;
    }

    // The energy left after a moving average over 3.3 ms (a low-pass near 300 Hz), as a share of all of it.
    private static float LowShare(float[] samples)
    {
        const int Width = 147;
        double sum = 0.0;
        double low = 0.0;
        double all = 0.0;
        for (int i = 0; i < samples.Length; i++)
        {
            sum += samples[i] - (i >= Width ? samples[i - Width] : 0f);
            double average = sum / Width;
            low += average * average;
            all += samples[i] * (double)samples[i];
        }

        return (float)(low / all);
    }
}
