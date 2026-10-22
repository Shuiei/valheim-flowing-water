using System;
using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>How the water's look follows its depth: the light each metre absorbs, from how clear the water is.</summary>
public class WaterLookTests
{
    [Theory]
    [InlineData(1f)]
    [InlineData(WaterLook.DefaultClarity)]
    [InlineData(20f)]
    public void ThroughAsMuchWaterAsItsClarityAboutAThirdIsSeen(float clarity)
    {
        (float r, float g, float b) = WaterLook.Absorption(clarity);

        Assert.Equal(1f / clarity, (r + g + b) / 3f, 4);
    }

    [Theory]
    [InlineData(1f)]
    [InlineData(WaterLook.DefaultClarity)]
    [InlineData(20f)]
    public void ThroughAsMuchWaterAsItsClarityMostRedIsGoneAndMostBlueComesThrough(float clarity)
    {
        (float r, float g, float b) = WaterLook.Absorption(clarity);

        Assert.Equal(0.129f, MathF.Exp(-r * clarity), 3);
        Assert.Equal(0.580f, MathF.Exp(-g * clarity), 3);
        Assert.Equal(0.664f, MathF.Exp(-b * clarity), 3);
    }

    [Fact]
    public void RedGoesFirstAndBlueLast()
    {
        (float r, float g, float b) = WaterLook.Absorption(WaterLook.DefaultClarity);

        Assert.True(r > g);
        Assert.True(g > b);
    }

    [Fact]
    public void ByDefaultTheWaterIsClearFreshWater()
    {
        (float r, float g, float b) = WaterLook.Absorption(WaterLook.DefaultClarity);

        Assert.Equal(0.45f, r, 2);
        Assert.Equal(0.12f, g, 2);
        Assert.Equal(0.09f, b, 2);
    }

    [Fact]
    public void ClearerWaterAbsorbsLessAndNoClarityIsNotInfinite()
    {
        Assert.True(WaterLook.Absorption(10f).R < WaterLook.Absorption(2f).R);
        Assert.True(float.IsFinite(WaterLook.Absorption(0f).R));
    }
}
