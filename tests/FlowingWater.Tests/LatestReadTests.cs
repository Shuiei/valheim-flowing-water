using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FlowingWater.Client;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// A brick's data read away from the frame as it changes: each revision read once, one read at a time, the newest
/// kept (an older result never after a newer), failures always given, and a read left behind (its brick gone)
/// dropped but still telling its failure.
/// </summary>
public class LatestReadTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    [Fact]
    public void NewDataIsReadAtOnceAndTakenOnce()
    {
        var read = new LatestRead<int>();
        Assert.True(read.Wants(0));
        read.Start(() => 7);
        Assert.True(WaitDone(read));

        Assert.True(read.TryTake(out int result, out Exception failure));
        Assert.Equal(7, result);
        Assert.Null(failure);
        Assert.False(read.Busy);
        Assert.False(read.TryTake(out _, out _));

        // The same revision is not read again.
        Assert.False(read.Wants(0));
    }

    [Fact]
    public void NothingIsTakenBeforeAReadStartsOrWhileItRuns()
    {
        var read = new LatestRead<int>();
        Assert.False(read.TryTake(out _, out _));

        using var gate = new ManualResetEventSlim();
        read.Wants(1);
        read.Start(() => Gated(gate, 1));
        Assert.True(read.Busy);
        Assert.False(read.TryTake(out _, out _));
        gate.Set();
    }

    [Fact]
    public void DataChangedWhileReadEndsWithTheNewestAndNeverTheOlder()
    {
        var read = new LatestRead<int>();
        using var gate = new ManualResetEventSlim();
        read.Wants(1);
        read.Start(() => Gated(gate, 1));

        // Changed twice while the first is read: none is started meanwhile, not even once that one is done.
        Assert.False(read.Wants(2));
        Assert.False(read.Wants(3));
        gate.Set();
        Assert.True(WaitDone(read));
        Assert.False(read.Wants(3));

        // The older result is dropped; the newest is read next and kept.
        Assert.False(read.TryTake(out int dropped, out Exception none));
        Assert.Equal(0, dropped);
        Assert.Null(none);
        Assert.False(read.Busy);
        Assert.True(read.Wants(3));
        read.Start(() => 3);
        Assert.True(WaitDone(read));
        Assert.True(read.TryTake(out int newest, out _));
        Assert.Equal(3, newest);
    }

    [Fact]
    public void AStartWhileAReadIsUnderWayIsRefused()
    {
        var read = new LatestRead<int>();
        using var gate = new ManualResetEventSlim();
        read.Wants(1);
        read.Start(() => Gated(gate, 1));

        Assert.Throws<InvalidOperationException>(() => read.Start(() => 2));
        gate.Set();
    }

    [Fact]
    public void UnreadableDataGivesItsExceptionAsItIsAndIsNotReadAgain()
    {
        var read = new LatestRead<BrickView>();
        read.Wants(4);
        read.Start(() => BrickData.Decode([1, 2, 3]));
        Assert.True(WaitDone(read));

        Assert.True(read.TryTake(out BrickView view, out Exception failure));
        Assert.Null(view);
        Assert.IsType<InvalidDataException>(failure);
        Assert.False(read.Wants(4));
    }

    [Fact]
    public void AFailureIsGivenEvenWhenNewerDataCameMeanwhile()
    {
        var read = new LatestRead<int>();
        using var gate = new ManualResetEventSlim();
        read.Wants(1);
        read.Start(() =>
        {
            gate.Wait(Patience);
            throw new InvalidOperationException("broken");
        });
        read.Wants(2);
        gate.Set();
        Assert.True(WaitDone(read));

        Assert.True(read.TryTake(out _, out Exception failure));
        Assert.Equal("broken", failure.Message);
        Assert.True(read.Wants(2));
    }

    [Fact]
    public async Task AReadLeftBehindIsDroppedAndStillTellsItsFailure()
    {
        var read = new LatestRead<int>();
        using var gate = new ManualResetEventSlim();
        var told = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        read.Wants(1);
        read.Start(() =>
        {
            gate.Wait(Patience);
            throw new InvalidDataException("late");
        });

        read.Abandon(told.SetResult, TaskScheduler.Default);
        Assert.False(read.Busy);
        Assert.False(read.TryTake(out _, out _));
        gate.Set();

        Exception failure = await told.Task.WaitAsync(Patience, TestContext.Current.CancellationToken);
        Assert.IsType<InvalidDataException>(failure);
        Assert.Equal("late", failure.Message);
    }

    [Fact]
    public void ReadsDoneAreHandedBackAsTimeAllowsAndTheRestNextTime()
    {
        // As the bricks are: a queue of those reading, served while the frame's time lasts (here two a frame).
        var reads = new LatestRead<int>[4];
        var turns = new TakeTurns<LatestRead<int>>();
        for (int i = 0; i < reads.Length; i++)
        {
            int value = i;
            reads[i] = new LatestRead<int>();
            reads[i].Wants(0);
            reads[i].Start(() => value);
            turns.Add(reads[i]);
        }

        Assert.True(SpinWait.SpinUntil(() => Array.TrueForAll(reads, r => r.Done), Patience));
        var taken = new List<int>();
        int handed = 0;
        void Frame()
        {
            handed = 0;
            turns.Serve(r => r.Done, () => handed < 2, r =>
            {
                handed++;
                Assert.True(r.TryTake(out int value, out _));
                taken.Add(value);
            });
        }

        Frame();
        Assert.Equal([0, 1], taken);
        Frame();
        Assert.Equal([0, 1, 2, 3], taken);
    }

    private static int Gated(ManualResetEventSlim gate, int value)
    {
        gate.Wait(Patience);
        return value;
    }

    private static bool WaitDone<T>(LatestRead<T> read) => SpinWait.SpinUntil(() => read.Done, Patience);
}
