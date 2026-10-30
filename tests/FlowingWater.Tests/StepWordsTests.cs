using System;
using System.Collections.Generic;
using FlowingWater.Recorder;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The recorder's steps in numbers (<see cref="StepWords"/>): finite numbers apart by commas; "!glide x,z,speed" with
/// a speed over 0 and nothing else; a point at -9999 or lower put on the ground there, where the ground is known; a
/// glide carried on toward its target a step at a time, and ending on it.
/// </summary>
public class StepWordsTests
{
    [Theory]
    [InlineData("1,-2.5,3e2", new[] { 1f, -2.5f, 300f })]
    [InlineData("7", new[] { 7f })]
    public void NumbersApartByCommasAreRead(string text, float[] values)
    {
        Assert.True(StepWords.Numbers(text, out float[] read));
        Assert.Equal(values, read);
    }

    [Theory]
    [InlineData("1,,3")]
    [InlineData("1,NaN")]
    [InlineData("Infinity")]
    [InlineData("1;2")]
    public void AnythingButFiniteNumbersIsRefused(string text)
    {
        Assert.False(StepWords.Numbers(text, out _));
    }

    [Fact]
    public void AGlideIsWhereToAndHowFast()
    {
        Assert.True(StepWords.Glide("400.5,-489,3", out (float X, float Z, float Speed) glide));
        Assert.Equal((400.5f, -489f, 3f), glide);
    }

    [Theory]
    [InlineData("400,-489")]
    [InlineData("400,-489,3,1")]
    [InlineData("400,-489,0")]
    [InlineData("400,-489,-2")]
    [InlineData("400,x,3")]
    public void AGlideWithoutThreeNumbersOrNoSpeedIsRefused(string text)
    {
        Assert.False(StepWords.Glide(text, out _));
    }

    [Theory]
    [InlineData(-9999f, 81.2f, 81.25f)]
    [InlineData(-20000f, 81.2f, 81.25f)]
    [InlineData(-9999f, null, -9999f)]
    [InlineData(130f, 81.2f, 130f)]
    [InlineData(-9998f, 81.2f, -9998f)]
    public void APointOnTheGroundIsPutOnItWhereTheGroundIsKnown(float y, float? ground, float height)
    {
        Assert.Equal(height, StepWords.Height(y, ground), 3);
    }

    [Fact]
    public void AGlideGoesOnAStepAtATimeAndEndsOnItsTarget()
    {
        (float x, float z, bool there) = StepWords.GlideStep(0f, 0f, 30f, 40f, 5f);
        Assert.Equal((3f, 4f, false), (x, z, there), new Near());

        int steps = 1;
        while (!there)
        {
            (x, z, there) = StepWords.GlideStep(x, z, 30f, 40f, 5f);
            steps++;
        }

        Assert.Equal(10, steps);
        Assert.Equal((30f, 40f), (x, z));
        Assert.Equal((30f, 40f, true), StepWords.GlideStep(30f, 40f, 30f, 40f, 5f));
    }

    // Two places the same within a millimetre.
    private sealed class Near : IEqualityComparer<(float, float, bool)>
    {
        public bool Equals((float, float, bool) a, (float, float, bool) b) =>
            Math.Abs(a.Item1 - b.Item1) < 1e-3f && Math.Abs(a.Item2 - b.Item2) < 1e-3f && a.Item3 == b.Item3;

        public int GetHashCode((float, float, bool) obj) => obj.Item3.GetHashCode();
    }
}
