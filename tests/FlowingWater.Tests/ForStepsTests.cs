using System;
using System.Collections;
using System.Collections.Generic;
using FlowingWater.Recorder;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The recorder's setting changed for some steps (its far water shots, the card's square cut to 32 m): set before the
/// first step, kept through them, and put back to what it was when they end, when one throws, and when the run is
/// stopped.
/// </summary>
public class ForStepsTests
{
    [Fact]
    public void TheSettingIsChangedThroughTheStepsAndPutBackAfter()
    {
        float range = 96f;
        var seen = new List<float>();

        object[] yielded = Run(ForSteps.Set(() => range, metres => range = metres, 32f, Steps(() => seen.Add(range), "burst", "shot")));

        Assert.Equal(["burst", "shot"], yielded);
        Assert.Equal([32f, 32f, 32f], seen);
        Assert.Equal(96f, range);
    }

    [Fact]
    public void ItIsPutBackWhenAStepThrows()
    {
        float range = 96f;

        IEnumerator run = ForSteps.Set(() => range, metres => range = metres, 32f, Throwing());

        Assert.True(run.MoveNext());
        Assert.Equal(32f, range);
        Assert.Throws<InvalidOperationException>(() => run.MoveNext());
        Assert.Equal(96f, range);
    }

    [Fact]
    public void ItIsPutBackWhenTheRunIsStopped()
    {
        float range = 96f;

        IEnumerator run = ForSteps.Set(() => range, metres => range = metres, 32f, Steps(() => { }, "burst", "shot"));
        Assert.True(run.MoveNext());
        ((IDisposable)run).Dispose();

        Assert.Equal(96f, range);
    }

    [Fact]
    public void NothingIsSetUntilTheStepsStart()
    {
        float range = 96f;

        IEnumerator run = ForSteps.Set(() => range, metres => range = metres, 32f, Steps(() => { }));

        Assert.Equal(96f, range);
        Assert.False(run.MoveNext());
        Assert.Equal(96f, range);
    }

    // Each step noted (before it is yielded), then yielded.
    private static IEnumerator Steps(Action note, params object[] steps)
    {
        note();
        foreach (object step in steps)
        {
            note();
            yield return step;
        }
    }

    private static IEnumerator Throwing()
    {
        yield return "burst";
        throw new InvalidOperationException("the shot failed");
    }

    private static object[] Run(IEnumerator run)
    {
        var yielded = new List<object>();
        while (run.MoveNext())
        {
            yielded.Add(run.Current);
        }

        return [.. yielded];
    }
}
