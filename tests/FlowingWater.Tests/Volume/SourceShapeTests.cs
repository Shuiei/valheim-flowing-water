using System;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// A source's shape: the way it faces, its outlet's width, how far it fans out and how far up it is thrown, each held
/// to its range; the water's way across the outlet, its speed for a rate and the places it leaves from.
/// </summary>
public class SourceShapeTests
{
    [Fact]
    public void EachValueIsHeldToItsRange()
    {
        var shape = new SourceShape(false, -90f, 40f, 200f, -5f);

        Assert.Equal(270f, shape.Heading);
        Assert.Equal(SourceShape.MostWidth, shape.Width);
        Assert.Equal(SourceShape.MostSpread, shape.Spread);
        Assert.Equal(0f, shape.Throw);
        Assert.Equal(SourceShape.MostThrow, new SourceShape(false, 0f, 0f, 0f, 90f).Throw);
        Assert.Equal(0f, new SourceShape(false, float.NaN, float.NaN, float.NaN, float.NaN).Heading);
    }

    [Theory]
    [InlineData(0f, 0f, 1f)]
    [InlineData(90f, 1f, 0f)]
    [InlineData(180f, 0f, -1f)]
    [InlineData(270f, -1f, 0f)]
    public void ItFacesItsHeadingClockwiseFromNorth(float heading, float east, float north)
    {
        Vec3 way = new SourceShape(false, heading, 0f, 0f, 0f).Direction(0f);

        Assert.Equal(east, way.X, 4);
        Assert.Equal(0f, way.Y, 4);
        Assert.Equal(north, way.Z, 4);
    }

    [Fact]
    public void TheRightOfTheOutletIsAQuarterTurnClockwise()
    {
        (float x, float z) = new SourceShape(false, 0f, 2f, 0f, 0f).Right;
        (float eastX, float eastZ) = new SourceShape(false, 90f, 2f, 0f, 0f).Right;

        Assert.Equal((1f, 0f), (x, z));
        Assert.Equal(0f, eastX, 4);
        Assert.Equal(-1f, eastZ, 4);
    }

    [Fact]
    public void ItFansOutByHalfTheSpreadToEachSide()
    {
        var shape = new SourceShape(false, 0f, 0f, 60f, 0f);

        Vec3 left = shape.Direction(-1f), right = shape.Direction(1f), middle = shape.Direction(0f);

        Assert.Equal(-Math.Sin(Math.PI / 6.0), left.X, 4);
        Assert.Equal(Math.Sin(Math.PI / 6.0), right.X, 4);
        Assert.Equal(0f, middle.X, 4);
        Assert.Equal(Math.Cos(Math.PI / 6.0), right.Z, 4);
    }

    [Fact]
    public void AThrowTiltsItUpAndStaysAUnit()
    {
        Vec3 way = new SourceShape(false, 45f, 3f, 40f, 30f).Direction(0.5f);

        Assert.Equal(0.5f, way.Y, 4);
        Assert.Equal(1f, Math.Sqrt((way.X * way.X) + (way.Y * way.Y) + (way.Z * way.Z)), 4);
    }

    [Fact]
    public void ItsWaterLeavesAsFastAsTheSlotPassesTheRateUpToTheMost()
    {
        // A wide slow river: 1 m³/s through 10 m of slot 0.75 m high (the default), 0.133 m/s; 0.25 m high, 0.4 m/s.
        var river = new SourceShape(false, 0f, 10f, 0f, 0f);
        var thin = new SourceShape(false, 0f, 10f, 0f, 0f, 0.25f);
        // A point: 10 m³/s would need 53 m/s through 0.25 x 0.75 m: held to the most, the slot standing higher.
        var point = new SourceShape(false, 0f, 0f, 0f, 0f);

        Assert.Equal(1f / 7.5f, river.Speed(1f), 4);
        Assert.Equal(SourceShape.Nozzle, river.OutletHeight(1f), 4);
        Assert.Equal(0.4f, thin.Speed(1f), 4);
        Assert.Equal(0.25f, thin.OutletHeight(1f), 4);
        Assert.Equal(SourceShape.MostSpeed, point.Speed(10f));
        Assert.Equal(10f, point.OutletWidth * point.OutletHeight(10f) * point.Speed(10f), 3);
        Assert.Equal(0f, point.Speed(0f));
    }

    [Fact]
    public void ItsThicknessIsHeldToItsRangeAndIsTheDefaultSlotUnlessSet()
    {
        Assert.Equal(SourceShape.Nozzle, new SourceShape(false, 0f, 0f, 0f, 0f).Thickness);
        Assert.Equal(SourceShape.Nozzle, SourceShape.Jet(0f).Thickness);
        Assert.Equal(SourceShape.Nozzle, SourceShape.Kept(1, 0f, 2f, 0f, 0f).Thickness);
        Assert.Equal(1.2f, SourceShape.Kept(0, 0f, 2f, 0f, 0f, 1.2f).Thickness);
        Assert.Equal(SourceShape.LeastThickness, new SourceShape(false, 0f, 0f, 0f, 0f, 0f).Thickness);
        Assert.Equal(SourceShape.MostThickness, new SourceShape(false, 0f, 0f, 0f, 0f, 50f).Thickness);
        Assert.Equal(SourceShape.Nozzle, new SourceShape(false, 0f, 0f, 0f, 0f, float.NaN).Thickness);
        Assert.NotEqual(new SourceShape(false, 0f, 2f, 0f, 0f, 0.5f), new SourceShape(false, 0f, 2f, 0f, 0f, 0.55f));
        Assert.NotEqual(new SourceShape(false, 0f, 2f, 0f, 0f, 0.5f).GetHashCode(), new SourceShape(false, 0f, 2f, 0f, 0f, 0.55f).GetHashCode());
    }

    [Fact]
    public void AThickerSlotSendsTheSameRateSlowerAndFuller()
    {
        // 2 m³/s out of a 2 m outlet: 0.5 m thick, 2 m/s; 2 m thick, 0.5 m/s; the same rate through either.
        var half = new SourceShape(false, 0f, 2f, 0f, 0f, 0.5f);
        var full = new SourceShape(false, 0f, 2f, 0f, 0f, 2f);

        Assert.Equal(2f, half.Speed(2f), 4);
        Assert.Equal(0.5f, full.Speed(2f), 4);
        Assert.Equal(2f, full.OutletWidth * full.OutletHeight(2f) * full.Speed(2f), 4);
        Assert.Equal(2f, half.OutletWidth * half.OutletHeight(2f) * half.Speed(2f), 4);
    }

    [Fact]
    public void TheOutletsPlacesSpanItsWidthEvenly()
    {
        (float Across, float Offset)[] places = new SourceShape(false, 0f, 4f, 0f, 0f).Outlet(1f);
        (float Across, float Offset)[] point = new SourceShape(false, 0f, 0f, 0f, 0f).Outlet(1f);

        Assert.Equal(4, places.Length);
        Assert.Equal(-0.75f, places[0].Across, 4);
        Assert.Equal(-1.5f, places[0].Offset, 4);
        Assert.Equal(1.5f, places[3].Offset, 4);
        Assert.Equal((0f, 0f), point[0]);
        Assert.Single(point);
    }

    [Fact]
    public void ANewSourceIsASlimJetAndOneWithNoShapeIsAllRound()
    {
        var jet = SourceShape.Jet(90f);

        Assert.False(jet.AllRound);
        Assert.Equal(90f, jet.Heading);
        Assert.Equal(0f, jet.Width);
        Assert.True(jet.Spread > 0f && jet.Throw > 0f);
        Assert.True(SourceShape.Round.AllRound);
        Assert.NotEqual(jet, SourceShape.Round);
    }

    [Fact]
    public void AShapeComesBackAsItsObjectKeepsItFacingOrAllRound()
    {
        var facing = new SourceShape(false, 45f, 3.5f, 25f, 15f);
        var round = new SourceShape(true, 45f, 3.5f, 25f, 15f);

        Assert.Equal(facing, SourceShape.Kept(facing.Flag, 45f, facing.Width, facing.Spread, facing.Throw));
        Assert.Equal(round, SourceShape.Kept(round.Flag, 45f, round.Width, round.Spread, round.Throw));
        Assert.Equal(3.5f, SourceShape.Kept(round.Flag, 45f, 3.5f, 25f, 15f).Width);
    }

    [Fact]
    public void AnAllRoundSourcesWaterTakesNoneOfTheValuesItKeepsForATurnBack()
    {
        // No flag written (placed before 0.46.0): all round, as it always poured.
        Assert.Equal(SourceShape.Round, SourceShape.Kept(0, 120f, 0f, 0f, 0f).Poured);
        Assert.Equal(SourceShape.Round, new SourceShape(true, 45f, 3.5f, 25f, 15f).Poured);
        var facing = new SourceShape(false, 45f, 3.5f, 25f, 15f);
        Assert.Equal(facing, facing.Poured);
    }

    [Theory]
    [InlineData(0f, 1f, 0f)]
    [InlineData(1f, 0f, 90f)]
    [InlineData(0f, -1f, 180f)]
    [InlineData(-1f, 0f, 270f)]
    [InlineData(-1f, 1f, 315f)]
    [InlineData(0f, 0f, 0f)]
    public void APiecesForwardGivesTheWayItFacesClockwiseFromNorth(float forwardX, float forwardZ, float heading)
    {
        Assert.Equal(heading, SourceShape.HeadingOf(forwardX, forwardZ), 3);
    }
}
