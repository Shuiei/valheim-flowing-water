using System;
using System.Threading;
using System.Threading.Tasks;
using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// A build away from the frame: its result taken once when done, its failure given instead, no second build while
/// one is under way (a change meanwhile waits for it), and one left behind still telling its failure.
/// </summary>
public class BackgroundBuildTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void ADoneBuildIsTakenOnce()
    {
        var build = new BackgroundBuild<int>();
        using var gate = new ManualResetEventSlim();
        Assert.True(build.TryStart(() => Gated(gate, 7)));
        Assert.False(build.TryTake(out _, out _));

        gate.Set();
        Assert.True(WaitDone(build));
        Assert.True(build.TryTake(out int result, out Exception failure));

        Assert.Equal(7, result);
        Assert.Null(failure);
        Assert.False(build.Busy);
        Assert.False(build.TryTake(out _, out _));
    }

    [Fact]
    public void AFailedBuildGivesItsExceptionAndNoResult()
    {
        var build = new BackgroundBuild<int>();
        build.TryStart(() => throw new InvalidOperationException("broken"));

        Assert.True(WaitDone(build));
        Assert.True(build.TryTake(out int result, out Exception failure));

        Assert.Equal(0, result);
        Assert.IsType<InvalidOperationException>(failure);
        Assert.Equal("broken", failure.Message);
    }

    [Fact]
    public void AChangeWhileABuildRunsWaitsForIt()
    {
        var build = new BackgroundBuild<int>();
        using var gate = new ManualResetEventSlim();
        build.TryStart(() => Gated(gate, 1));

        // The newer change cannot start until the running build is taken; then it can.
        Assert.False(build.TryStart(() => 2));
        gate.Set();
        Assert.True(WaitDone(build));
        Assert.False(build.TryStart(() => 2));
        build.TryTake(out int first, out _);
        Assert.True(build.TryStart(() => 2));
        Assert.True(WaitDone(build));
        build.TryTake(out int second, out _);

        Assert.Equal(1, first);
        Assert.Equal(2, second);
    }

    [Fact]
    public async Task ABuildLeftBehindStillTellsItsFailure()
    {
        var build = new BackgroundBuild<int>();
        using var gate = new ManualResetEventSlim();
        var told = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        build.TryStart(() =>
        {
            gate.Wait(Patience);
            throw new InvalidOperationException("late");
        });

        build.Abandon(told.SetResult, TaskScheduler.Default);
        Assert.False(build.Busy);
        gate.Set();

        Exception failure = await told.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.Equal("late", failure.Message);
    }

    private static int Gated(ManualResetEventSlim gate, int value)
    {
        gate.Wait(Patience);
        return value;
    }

    private static bool WaitDone<T>(BackgroundBuild<T> build) => SpinWait.SpinUntil(() => build.Done, Patience);
}
