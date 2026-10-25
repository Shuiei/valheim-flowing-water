using System.Collections.Generic;
using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>Things served in the order they came, as time allows, none left waiting for ever.</summary>
public class TakeTurnsTests
{
    [Fact]
    public void TheOnesWaitingLongestAreServedFirst()
    {
        var turns = new TakeTurns<string>();
        turns.Add("a");
        turns.Add("b");
        turns.Add("c");
        var served = new List<string>();

        turns.Serve(_ => true, () => true, served.Add);

        Assert.Equal(["a", "b", "c"], served);
    }

    [Fact]
    public void OneAddedAgainWhileWaitingKeepsItsPlace()
    {
        var turns = new TakeTurns<string>();
        turns.Add("a");
        turns.Add("b");
        turns.Add("a");
        var served = new List<string>();

        turns.Serve(_ => true, () => true, served.Add);

        Assert.Equal(["a", "b"], served);
    }

    [Fact]
    public void WhenTimeRunsOutTheRestGoFirstNextTime()
    {
        var turns = new TakeTurns<int>();
        for (int i = 0; i < 6; i++)
        {
            turns.Add(i);
        }

        var served = new List<int>();
        int budget = 2;
        turns.Serve(_ => true, () => served.Count < budget, served.Add);
        Assert.Equal([0, 1], served);

        // The first ones ask again at once: they wait behind those left over.
        turns.Add(0);
        turns.Add(1);
        budget = 6;
        turns.Serve(_ => true, () => served.Count < budget, served.Add);

        Assert.Equal([0, 1, 2, 3, 4, 5], served);
    }

    [Fact]
    public void TheFirstReadyOneIsServedEvenWithNoTimeLeft()
    {
        var turns = new TakeTurns<string>();
        turns.Add("a");
        turns.Add("b");
        var served = new List<string>();

        turns.Serve(item => item == "b", () => false, served.Add);

        Assert.Equal(["b"], served);
    }

    [Fact]
    public void OnesNotReadyWaitOnAndEachIsLookedAtOnce()
    {
        var turns = new TakeTurns<string>();
        turns.Add("a");
        turns.Add("b");
        turns.Add("c");
        var asked = new List<string>();
        var served = new List<string>();

        turns.Serve(
            item =>
            {
                asked.Add(item);
                return item != "a";
            },
            () => true,
            served.Add);

        Assert.Equal(["a", "b", "c"], asked);
        Assert.Equal(["b", "c"], served);

        served.Clear();
        turns.Serve(_ => true, () => true, served.Add);
        Assert.Equal(["a"], served);
    }

    [Fact]
    public void ClearedOnesWaitNoMoreAndMayBeAddedAgain()
    {
        var turns = new TakeTurns<string>();
        turns.Add("a");
        turns.Add("b");

        turns.Clear();

        turns.Add("b");
        var served = new List<string>();
        turns.Serve(_ => true, () => true, served.Add);
        Assert.Equal(["b"], served);
    }

    [Fact]
    public void OneServedMayWaitAgainAndIsServedOnTheNextTurn()
    {
        var turns = new TakeTurns<string>();
        turns.Add("a");
        var served = new List<string>();

        turns.Serve(_ => true, () => true, item =>
        {
            served.Add(item);
            turns.Add(item);
        });

        Assert.Equal(["a"], served);
    }
}
