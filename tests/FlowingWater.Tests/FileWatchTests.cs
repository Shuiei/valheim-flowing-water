using System;
using System.Collections.Generic;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The settings file is read again a look or two after it is written, once the write is done, not while it goes on,
/// not when nothing was written, and not more than once a second; what changed is told by name.
/// </summary>
public class FileWatchTests
{
    private static readonly DateTime Start = new(2026, 10, 24, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void AFileNotWrittenIsNotReadAgain()
    {
        var watch = new FileWatch(() => Start);

        Assert.False(watch.Changed(0f));
        Assert.False(watch.Changed(5f));
    }

    [Fact]
    public void AWrittenFileIsReadAgainOnceItsWriteTimeHoldsStill()
    {
        DateTime written = Start;
        var watch = new FileWatch(() => written);

        written = Start.AddSeconds(10);
        Assert.False(watch.Changed(1f));
        Assert.True(watch.Changed(2f));
        Assert.False(watch.Changed(3f));
    }

    [Fact]
    public void AFileStillBeingWrittenIsReadOnlyWhenItStops()
    {
        DateTime written = Start;
        var watch = new FileWatch(() => written);

        written = Start.AddSeconds(1);
        Assert.False(watch.Changed(1f));
        written = Start.AddSeconds(2);
        Assert.False(watch.Changed(2f));
        Assert.True(watch.Changed(3f));
    }

    [Fact]
    public void TheFileIsLookedAtNoMoreThanOnceASecond()
    {
        int looks = 0;
        var watch = new FileWatch(() =>
        {
            looks++;
            return Start;
        });

        watch.Changed(0f);
        watch.Changed(0.3f);
        watch.Changed(0.9f);
        watch.Changed(1f);

        Assert.Equal(3, looks);
    }

    [Fact]
    public void OnlyTheSettingsThatChangedAreTold()
    {
        var before = new Dictionary<string, string> { ["Server.DebugSeconds"] = "0", ["Server.StepBudget"] = "8", ["Client.Spray"] = "true" };
        var after = new Dictionary<string, string> { ["Server.DebugSeconds"] = "10", ["Server.StepBudget"] = "8", ["Client.Spray"] = "false", ["Server.New"] = "1" };

        Assert.Equal(["Client.Spray: true → false", "Server.DebugSeconds: 0 → 10", "Server.New: (none) → 1"], FileWatch.Changes(before, after));
        Assert.Empty(FileWatch.Changes(after, after));
    }
}
