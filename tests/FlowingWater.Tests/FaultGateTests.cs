using System;
using System.Collections.Generic;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// A part run each frame beside the water (the source and drain panel): a fault in it is logged once with the exception
/// in full and turns it off, put away once; off, it does not run; at a new world it runs again.
/// </summary>
public class FaultGateTests
{
    private readonly List<string> _errors = [];

    private int _turnedOff;

    [Fact]
    public void APartThatDoesNotFailRunsEveryFrameAndLogsNothing()
    {
        FaultGate gate = Gate();
        int runs = 0;

        for (int frame = 0; frame < 3; frame++)
        {
            gate.Run(() => runs++);
        }

        Assert.Equal(3, runs);
        Assert.True(gate.On);
        Assert.Empty(_errors);
        Assert.Equal(0, _turnedOff);
    }

    [Fact]
    public void AFaultIsLoggedOnceWithItsExceptionAndTurnsThePartOffOnce()
    {
        FaultGate gate = Gate();
        int runs = 0;

        for (int frame = 0; frame < 5; frame++)
        {
            gate.Run(() =>
            {
                runs++;
                Fail();
            });
        }

        Assert.Equal(1, runs);
        Assert.False(gate.On);
        Assert.Equal(1, _turnedOff);
        string error = Assert.Single(_errors);
        Assert.StartsWith("The panel failed: ", error, StringComparison.Ordinal);
        Assert.Contains(nameof(InvalidOperationException), error, StringComparison.Ordinal);
        Assert.Contains("the panel's fault", error, StringComparison.Ordinal);
        Assert.Contains(nameof(Fail), error, StringComparison.Ordinal);
    }

    [Fact]
    public void ResetLetsThePartRunAgainAndItsNextFaultIsLoggedToo()
    {
        FaultGate gate = Gate();
        gate.Run(Fail);

        gate.Reset();
        int runs = 0;
        gate.Run(() => runs++);
        gate.Run(Fail);
        gate.Run(() => runs++);

        Assert.Equal(1, runs);
        Assert.False(gate.On);
        Assert.Equal(2, _errors.Count);
        Assert.Equal(2, _turnedOff);
    }

    [Fact]
    public void AFaultInTurningThePartOffIsNotCaughtButTheFirstIsLoggedBeforeIt()
    {
        var gate = new FaultGate("The panel failed", _errors.Add, () => throw new InvalidOperationException("put away"));

        Assert.Throws<InvalidOperationException>(() => gate.Run(Fail));

        Assert.False(gate.On);
        Assert.Contains("the panel's fault", Assert.Single(_errors), StringComparison.Ordinal);
    }

    private FaultGate Gate() => new("The panel failed", _errors.Add, () => _turnedOff++);

    private static void Fail() => throw new InvalidOperationException("the panel's fault");
}
