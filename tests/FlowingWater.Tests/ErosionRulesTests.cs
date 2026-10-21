using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>How running water wears the ground away and lays it down: what it can carry, what it picks up and sets down, the limit.</summary>
public class ErosionRulesTests
{
    [Fact]
    public void SlowWaterCarriesNothing()
    {
        Assert.Equal(0f, ErosionRules.Capacity(ErosionRules.MinSpeed, 0.5f));
        Assert.Equal(0f, ErosionRules.Capacity(0.2f, 0.5f));
        Assert.Equal(0f, ErosionRules.Capacity(3f, 0f));
    }

    [Fact]
    public void FasterAndDeeperWaterCarriesMoreUpToAMetreDeep()
    {
        Assert.True(ErosionRules.Capacity(3f, 0.2f) > ErosionRules.Capacity(2f, 0.2f));
        Assert.True(ErosionRules.Capacity(2f, 0.4f) > ErosionRules.Capacity(2f, 0.2f));
        Assert.Equal(ErosionRules.Capacity(2f, 1f), ErosionRules.Capacity(2f, 5f));

        // Twice the speed above the threshold carries four times as much.
        float once = ErosionRules.Capacity(ErosionRules.MinSpeed + 1f, 0.5f);
        Assert.Equal(4f * once, ErosionRules.Capacity(ErosionRules.MinSpeed + 2f, 0.5f), 5);
    }

    [Fact]
    public void WaterCarryingLessThanItCanCutsTheGround()
    {
        float taken = ErosionRules.Exchange(0f, 0.01f, 0f, 1f);

        Assert.True(taken > 0f);
        Assert.True(taken < 0.01f);
    }

    [Fact]
    public void WaterCarryingMoreThanItCanLaysTheRestDown()
    {
        float taken = ErosionRules.Exchange(0.02f, 0.01f, 0f, 1f);

        Assert.True(taken < 0f);
        Assert.True(-taken <= 0.01f);
    }

    [Fact]
    public void ItNeverPicksUpPastItsCapacityNorSetsDownMoreThanItCarries()
    {
        Assert.Equal(0.01f, ErosionRules.Exchange(0f, 0.01f, 0f, 1e6f), 6);
        Assert.Equal(-0.02f, ErosionRules.Exchange(0.02f, 0f, 0f, 1e6f), 6);
        Assert.Equal(0f, ErosionRules.Exchange(0.01f, 0.01f, 0f, 1f));
    }

    [Fact]
    public void TheGroundIsNeverCutOrRaisedPastTheGamesLimit()
    {
        // Already cut 7.999 m: only 0.001 m more may go; already raised to the limit, nothing more may come.
        Assert.Equal(0.001f, ErosionRules.Exchange(0f, 1f, -7.999f, 1e6f), 4);
        Assert.Equal(0f, ErosionRules.Exchange(1f, 0f, ErosionRules.MaxChange, 1e6f));
    }

    [Fact]
    public void WhatWaterCarriesIsSharedOverItsDepth()
    {
        Assert.Equal(0.02f, ErosionRules.Concentration(0.004f, 0.2f), 6);
        Assert.Equal(0f, ErosionRules.Concentration(0.004f, 0f));
    }
}
