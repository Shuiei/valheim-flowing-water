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

    [Fact]
    public void AChannelsWallFallsInUntilItHolds()
    {
        // A flat bank beside a cell cut 2 m: the 40 degree slope (0.84 m over a metre) holds, the rest evens out.
        Assert.Equal(0.58f, ErosionRules.Slide(10f, 0f, 8f, -2f, 1f, 100f), 4);
        Assert.Equal(-0.58f, ErosionRules.Slide(8f, -2f, 10f, 0f, 1f, 100f), 4);

        // Over a short time only a share of it.
        Assert.Equal(0.58f * 0.05f, ErosionRules.Slide(10f, 0f, 8f, -2f, 1f, 1f), 4);
    }

    [Fact]
    public void BanksErosionDidNotSteepenStay()
    {
        // A natural cliff, a bed cut evenly down a steep slope, and a bank gentler than the steepest that holds.
        Assert.Equal(0f, ErosionRules.Slide(10f, 0f, 5f, 0f, 1f, 100f));
        Assert.Equal(0f, ErosionRules.Slide(9f, -1f, 7.5f, -1f, 1f, 100f));
        Assert.Equal(0f, ErosionRules.Slide(10f, 0f, 9.3f, -0.7f, 1f, 100f));

        // Steeper than before erosion but within the old cliff: only what erosion added slides.
        Assert.Equal(0.25f, ErosionRules.Slide(10f, 0f, 4.5f, -0.5f, 1f, 100f), 4);
    }

    [Fact]
    public void TheSlopeThatHoldsGrowsWithTheDistance()
    {
        Assert.Equal(0f, ErosionRules.Slide(10f, 0f, 8.5f, -1.5f, 2f, 100f));
        Assert.True(ErosionRules.Slide(10f, 0f, 8.5f, -1.5f, 1f, 100f) > 0f);
    }

    [Fact]
    public void NoSlideTakesTheGroundPastTheLimit()
    {
        // The bank raised 8 m by what was laid down, the cell beside it cut 8 m.
        float slid = ErosionRules.Slide(18f, ErosionRules.MaxChange, 2f, -ErosionRules.MaxChange, 1f, 100f);

        Assert.InRange(ErosionRules.MaxChange - slid, -ErosionRules.MaxChange, ErosionRules.MaxChange);
        Assert.InRange(-ErosionRules.MaxChange + slid, -ErosionRules.MaxChange, ErosionRules.MaxChange);
    }
}
