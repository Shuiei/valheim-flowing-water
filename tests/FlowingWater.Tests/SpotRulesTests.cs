using System.Collections.Generic;
using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>Where a tile's water turns white: which cells, how strong, and which get the sound and spray.</summary>
public class SpotRulesTests
{
    [Theory]
    [InlineData(0.03f, 1.2f, true)]
    [InlineData(0.5f, 3f, true)]
    [InlineData(0.029f, 3f, false)]
    [InlineData(0.5f, 1.19f, false)]
    public void WaterIsWhiteFromThreeCentimetresDeepAndOnePointTwoMetresASecond(float depth, float speed, bool white)
    {
        Assert.Equal(white, SpotRules.Qualifies(depth, speed));
    }

    [Fact]
    public void MoreWaterAndASteeperSlopeScoreHigher()
    {
        Assert.Equal(0.5f, SpotRules.Score(0.5f, 0f));
        Assert.True(SpotRules.Score(1f, 0f) > SpotRules.Score(0.5f, 0f));
        Assert.True(SpotRules.Score(0.5f, 1f) > SpotRules.Score(0.5f, 0.2f));
    }

    [Fact]
    public void TheStrongestSixAreChosenStrongestFirst()
    {
        var candidates = new List<(float Score, float X, float Z, int Index)>();
        for (int i = 0; i < 10; i++)
        {
            // Ten places 10 m apart, scores 0 to 9 in no order.
            candidates.Add((i * 7 % 10, i * 10f, 0f, i));
        }

        var chosen = new List<int>();
        SpotRules.Pick(candidates, chosen);

        Assert.Equal(SpotRules.Max, chosen.Count);
        Assert.Equal([7, 4, 1, 8, 5, 2], chosen);
    }

    [Fact]
    public void NoTwoPlacesAreCloserThanSixMetres()
    {
        var candidates = new List<(float Score, float X, float Z, int Index)>
        {
            (5f, 0f, 0f, 0),
            (4f, 3f, 4f, 1), // 5 m from the first: left out
            (3f, 6f, 0f, 2), // just 6 m: kept
            (2f, 0f, -5.9f, 3), // 5.9 m from the first: left out
        };
        var chosen = new List<int>();

        SpotRules.Pick(candidates, chosen);

        Assert.Equal([0, 2], chosen);
    }

    [Fact]
    public void APickForgetsTheLastOne()
    {
        var chosen = new List<int> { 7, 8 };

        SpotRules.Pick([(1f, 0f, 0f, 0)], chosen);

        Assert.Equal([0], chosen);
    }
}
