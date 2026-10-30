using System;
using System.Collections.Generic;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// A source's jet as the player's game draws it in the air: its outlet cut as the server cuts it, each strip thrown at
/// the source's speed and fanning out by its share of the spread, and followed down till it meets something.
/// </summary>
public class JetFlightTests
{
    private static readonly Vec3 Outlet = new(0f, 100f, 0f);

    [Fact]
    public void AJetIsCutAsTheServerCutsItsOutletEachStripThrownAtTheSourcesSpeed()
    {
        var shape = new SourceShape(false, 90f, 4f, 0f, 30f);
        var strips = new List<JetFlight.Strip>();
        JetFlight.Strips(Outlet, shape, 8f, 1f, strips);

        Assert.Equal(shape.Outlet(1f).Length, strips.Count);
        Assert.Equal(4, strips.Count);
        float speed = shape.Speed(8f);
        foreach (JetFlight.Strip strip in strips)
        {
            Assert.Equal(1f, strip.Width, 3);
            Assert.Equal(speed, (float)Math.Sqrt(strip.Velocity.Dot(strip.Velocity)), 3);
            // Facing east, the outlet runs north to south; every strip at the outlet's height.
            Assert.Equal(0f, strip.Start.X, 3);
            Assert.Equal(100f, strip.Start.Y, 3);
            Assert.Equal(0f, strip.Widen, 3);
            Assert.Equal(8f / 4f, strip.Flow, 3);
        }

        Assert.Equal(3f, Math.Abs(strips[0].Start.Z - strips[3].Start.Z), 3);
    }

    [Fact]
    public void ASpreadJetsStripsFanOutByTheirShareOfIt()
    {
        var point = new SourceShape(false, 0f, 0f, 40f, 30f);
        var strips = new List<JetFlight.Strip>();
        JetFlight.Strips(Outlet, point, 2f, 1f, strips);

        // A point's one strip spans the whole fan: after a second its sides are as far apart as its edges' ways are.
        JetFlight.Strip strip = Assert.Single(strips);
        double flat = point.Speed(2f) * Math.Cos(30.0 * Math.PI / 180.0);
        Assert.Equal((float)(2.0 * flat * Math.Tan(20.0 * Math.PI / 180.0)), 2f * strip.Widen, 3);
    }

    [Fact]
    public void AJetAlongTheGroundOrAllRoundFliesNot()
    {
        var strips = new List<JetFlight.Strip>();
        JetFlight.Strips(Outlet, new SourceShape(false, 0f, 2f, 0f, 0f), 8f, 1f, strips);
        Assert.Empty(strips);
        JetFlight.Strips(Outlet, SourceShape.Round, 8f, 1f, strips);
        Assert.Empty(strips);
    }

    [Fact]
    public void AStripFliesTillItMeetsTheGroundAsTheArcComesDownOnIt()
    {
        var strip = new JetFlight.Strip(Outlet, new Vec3(5f, 5f, 0f), 1f, 0f, 1f);
        float flight = JetFlight.Flight(strip, (a, b) => b.Y <= 90f ? (a.Y - 90f) / (a.Y - b.Y) : null, out bool lands);

        // 100 + 5t - g t² / 2 = 90.
        float g = VoxelSolver.Gravity;
        float expected = (5f + (float)Math.Sqrt(25f + (2f * g * 10f))) / g;
        Assert.True(lands);
        Assert.Equal(expected, flight, 2);
        Assert.Equal(90f, strip.At(flight).Y, 1);
    }

    [Fact]
    public void StripsAreTheSameByEveryValue()
    {
        var strip = new JetFlight.Strip(Outlet, new Vec3(5f, 5f, 0f), 1f, 0.2f, 2f);

        Assert.Equal(strip, new JetFlight.Strip(Outlet, new Vec3(5f, 5f, 0f), 1f, 0.2f, 2f));
        Assert.True(strip == new JetFlight.Strip(Outlet, new Vec3(5f, 5f, 0f), 1f, 0.2f, 2f));
        Assert.True(strip != new JetFlight.Strip(Outlet, new Vec3(5f, 5f, 0f), 1f, 0.3f, 2f));
        Assert.NotEqual(strip, new JetFlight.Strip(Outlet, new Vec3(5f, 5f, 0f), 2f, 0.2f, 2f));
    }

    [Fact]
    public void AStripMeetingNothingFliesTheLongestStillFalling()
    {
        var strip = new JetFlight.Strip(Outlet, new Vec3(5f, 5f, 0f), 1f, 0f, 1f);
        float flight = JetFlight.Flight(strip, (_, _) => null, out bool lands);

        Assert.False(lands);
        Assert.Equal(JetFlight.Longest, flight);
    }
}
