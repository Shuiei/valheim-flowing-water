using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Where the water's time goes: the time between two switches goes to the part it was in, a part inside another
/// switches back to it, each part keeps its time in all, its most in a frame and in one go, the longest frame keeps its
/// parts and whether garbage was collected in it, nothing is timed outside a frame, and a report forgets the period.
/// </summary>
public class FramePartsTests
{
    // The clock counts milliseconds.
    private long _now;

    private int _collections;

    [Fact]
    public void TheTimeBetweenTwoSwitchesGoesToThePartItWasIn()
    {
        FrameParts parts = Parts();
        parts.Begin(WaterPart.Other);
        _now += 2;
        parts.Into(WaterPart.Flows);
        _now += 5;
        parts.Into(WaterPart.Other);
        _now += 1;
        parts.Into(WaterPart.Flows);
        _now += 3;
        parts.End();

        Assert.Equal(3.0, parts.Total(WaterPart.Other));
        Assert.Equal(8.0, parts.Total(WaterPart.Flows));
        Assert.Equal(8.0, parts.Most(WaterPart.Flows));
        Assert.Equal(5.0, parts.MostAtOnce(WaterPart.Flows));
        Assert.Equal(11.0, parts.LongestFrame);
        Assert.Equal(1, parts.Frames);
    }

    [Fact]
    public void APartInsideAnotherSwitchesBackToIt()
    {
        FrameParts parts = Parts();
        parts.Begin(WaterPart.Other);
        WaterPart outer = parts.Into(WaterPart.Made);
        _now += 1;
        WaterPart inner = parts.Into(WaterPart.Solids);
        _now += 4;
        parts.Into(inner);
        _now += 2;
        parts.Into(outer);
        _now += 1;
        parts.End();

        Assert.Equal(WaterPart.Other, outer);
        Assert.Equal(WaterPart.Made, inner);
        Assert.Equal(3.0, parts.Total(WaterPart.Made));
        Assert.Equal(4.0, parts.Total(WaterPart.Solids));
        Assert.Equal(1.0, parts.Total(WaterPart.Other));
    }

    [Fact]
    public void TheLongestFrameKeepsItsPartsAndWhetherGarbageWasCollectedInIt()
    {
        FrameParts parts = Parts();
        Frame(parts, WaterPart.Flows, 3, collect: true);
        Frame(parts, WaterPart.Heightmap, 9, collect: false);
        Frame(parts, WaterPart.Flows, 4, collect: false);

        Assert.Equal(9.0, parts.LongestFrame);
        Assert.Equal(9.0, parts.InLongest(WaterPart.Heightmap));
        Assert.Equal(0.0, parts.InLongest(WaterPart.Flows));
        Assert.False(parts.LongestCollected);
        Assert.Equal(1, parts.CollectedFrames);
        Assert.Equal(7.0, parts.Total(WaterPart.Flows));
        Assert.Equal(4.0, parts.Most(WaterPart.Flows));
    }

    [Fact]
    public void NothingIsTimedOutsideAFrame()
    {
        FrameParts parts = Parts();
        Assert.Equal(WaterPart.Send, parts.Into(WaterPart.Send));
        _now += 5;
        parts.Into(WaterPart.Other);
        parts.End();

        Assert.Equal(0.0, parts.Total(WaterPart.Send));
        Assert.Equal(0, parts.Frames);
    }

    [Fact]
    public void AReportTellsThePeriodAndForgetsIt()
    {
        FrameParts parts = Parts();
        Frame(parts, WaterPart.Flows, 6, collect: true);
        Frame(parts, WaterPart.Flows, 2, collect: false);

        string report = parts.Report();

        Assert.Contains("over 2 frames", report);
        Assert.Contains(" flows 8.0/6.0/6.0", report);
        Assert.DoesNotContain(" heads ", report);
        Assert.Contains("longest frame 6.0 ms: flows 6.0, a garbage collection in it", report);
        Assert.Contains("garbage collected in 1 frames", report);
        Assert.Equal(0, parts.Frames);
        Assert.Equal(0.0, parts.Total(WaterPart.Flows));
        Assert.Equal(0.0, parts.LongestFrame);
        Assert.Equal(0, parts.CollectedFrames);
    }

    private FrameParts Parts() => new(() => _now, 1000, () => _collections);

    // A frame spent all in one part.
    private void Frame(FrameParts parts, WaterPart part, long milliseconds, bool collect)
    {
        parts.Begin(part);
        _now += milliseconds;
        _collections += collect ? 1 : 0;
        parts.End();
    }
}
