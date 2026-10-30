using FlowingWater.Client;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The panel's sliders: each notch's value and the notch nearest a value, both ways; a rate on a logarithm from 0.1 to
/// 100 m³/s, rounded as a player reads it, a drain's first notch taking everything; a shape in a player's words.
/// </summary>
public class MarkerSlidersTests
{
    [Fact]
    public void TheShapesNotchesGoByHalfMetresAndFiveDegrees()
    {
        Assert.Equal(0f, MarkerSliders.Width(0));
        Assert.Equal(10f, MarkerSliders.Width(MarkerSliders.WidthNotches));
        Assert.Equal(7, MarkerSliders.WidthNotch(3.4f));
        Assert.Equal(90f, MarkerSliders.Spread(MarkerSliders.SpreadNotches - 1));
        Assert.Equal(60f, MarkerSliders.Throw(MarkerSliders.ThrowNotches));
        Assert.Equal(6, MarkerSliders.ThrowNotch(30f));
        Assert.Equal(0, MarkerSliders.WidthNotch(-3f));
        Assert.Equal(MarkerSliders.ThrowNotches, MarkerSliders.ThrowNotch(400f));
    }

    [Fact]
    public void TheSpreadsLastNotchIsAllRound()
    {
        Assert.True(MarkerSliders.AllRound(MarkerSliders.SpreadNotches));
        Assert.False(MarkerSliders.AllRound(MarkerSliders.SpreadNotches - 1));
        Assert.Equal(MarkerSliders.SpreadNotches, MarkerSliders.SpreadNotch(SourceShape.Round));
        Assert.Equal(4, MarkerSliders.SpreadNotch(new SourceShape(false, 0f, 0f, 20f, 0f)));
    }

    [Theory]
    [InlineData(0, false, 0.1f)]
    [InlineData(40, false, 1f)]
    [InlineData(80, false, 10f)]
    [InlineData(120, false, 100f)]
    [InlineData(0, true, 0f)]
    [InlineData(1, true, 0.1f)]
    [InlineData(120, true, 100f)]
    public void ARateGoesOnALogarithmFromATenthToAHundred(int notch, bool drain, float rate)
    {
        Assert.Equal(rate, MarkerSliders.Rate(notch, drain), 3);
    }

    [Theory]
    [InlineData(2f, false)]
    [InlineData(10f, false)]
    [InlineData(55f, true)]
    [InlineData(0.5f, true)]
    public void ARatesNotchGivesItBackWithinHalfANotch(float rate, bool drain)
    {
        // A notch is about 6 % over the one before it: the nearest is within half that, and the rounding.
        float back = MarkerSliders.Rate(MarkerSliders.RateNotch(rate, drain), drain);

        Assert.InRange(back, rate * 0.96f, rate * 1.04f);
    }

    [Fact]
    public void ADrainTakingEverythingIsTheFirstNotch()
    {
        Assert.Equal(0, MarkerSliders.RateNotch(0f, true));
        Assert.Equal(1, MarkerSliders.RateNotch(0.01f, true));
        Assert.Equal(0, MarkerSliders.RateNotch(0.01f, false));
    }

    [Theory]
    [InlineData(0.337f, 0.35f)]
    [InlineData(2.34f, 2.3f)]
    [InlineData(17.8f, 18f)]
    [InlineData(56.4f, 56f)]
    [InlineData(0.01f, 0.1f)]
    [InlineData(500f, 100f)]
    public void ARateIsRoundedAsAPlayerReadsIt(float rate, float nice)
    {
        Assert.Equal(nice, MarkerSliders.Nice(rate), 3);
    }

    [Fact]
    public void AReachGoesByQuarterMetresFromAQuarterToSixteen()
    {
        Assert.Equal(0.25f, MarkerSliders.Reach(0));
        Assert.Equal(16f, MarkerSliders.Reach(MarkerSliders.ReachNotches));
        Assert.Equal(4, MarkerSliders.ReachNotch(1f));
        Assert.Equal(1, MarkerSliders.ReachNotch(0f));
    }

    [Theory]
    [InlineData(0f, 20f, 30f, "a slim jet spray")]
    [InlineData(0.5f, 80f, 30f, "a wide jet spray")]
    [InlineData(1.5f, 0f, 0f, "a small river")]
    [InlineData(8f, 0f, 0f, "a wide river")]
    [InlineData(1f, 30f, 0f, "a slim spray along the ground")]
    public void AShapeIsNamedInAPlayersWords(float width, float spread, float pitch, string kind)
    {
        Assert.Equal(kind, MarkerSliders.Kind(new SourceShape(false, 0f, width, spread, pitch)));
    }

    [Fact]
    public void AShapeIsDescribedInFull()
    {
        Assert.Equal("all round", MarkerSliders.Describe(SourceShape.Round));
        Assert.Equal("a slim jet spray, from a point, fanned 20°, thrown up 30°", MarkerSliders.Describe(new SourceShape(false, 0f, 0f, 20f, 30f)));
        Assert.Equal("a wide river, 6 m wide, straight, along the ground", MarkerSliders.Describe(new SourceShape(false, 0f, 6f, 0f, 0f)));
    }

    [Fact]
    public void AValueBetweenNotchesIsKeptWhileItsSliderStaysAtTheNotchNearestIt()
    {
        Assert.Equal(0.337f, MarkerSliders.SlidRate(MarkerSliders.RateNotch(0.337f, false), 0.337f, false));
        Assert.Equal(MarkerSliders.Rate(60, false), MarkerSliders.SlidRate(60, 0.337f, false));
        Assert.Equal(0f, MarkerSliders.SlidRate(0, 0f, true));
        Assert.Equal(1.1f, MarkerSliders.SlidReach(MarkerSliders.ReachNotch(1.1f), 1.1f));
        Assert.Equal(2f, MarkerSliders.SlidReach(8, 1.1f));
    }

    [Fact]
    public void MovingOneShapeSliderLeavesTheOtherValuesAndTheHeadingAsTheyWere()
    {
        var kept = new SourceShape(false, 135f, 1.3f, 22f, 33f);
        int width = MarkerSliders.WidthNotch(1.3f), spread = MarkerSliders.SpreadNotch(kept), throwUp = MarkerSliders.ThrowNotch(33f);

        Assert.Equal(kept, MarkerSliders.Slid(kept, width, spread, throwUp));
        Assert.Equal(new SourceShape(false, 135f, 4f, 22f, 33f), MarkerSliders.Slid(kept, 8, spread, throwUp));
        Assert.Equal(new SourceShape(false, 135f, 1.3f, 22f, 10f), MarkerSliders.Slid(kept, width, spread, 2));
    }

    [Fact]
    public void ASourceMadeAllRoundKeepsItsOutletAndTakesTheSpreadItsSliderStandsAtWhenTurnedBack()
    {
        var kept = new SourceShape(false, 0f, 1.3f, 22f, 33f);
        int width = MarkerSliders.WidthNotch(1.3f), throwUp = MarkerSliders.ThrowNotch(33f);

        SourceShape round = MarkerSliders.Slid(kept, width, MarkerSliders.SpreadNotches, throwUp);
        Assert.Equal(new SourceShape(true, 0f, 1.3f, 22f, 33f), round);
        Assert.Equal(new SourceShape(false, 0f, 1.3f, 20f, 33f), MarkerSliders.Slid(round, width, MarkerSliders.SpreadNotch(kept), throwUp));
        Assert.Equal(new SourceShape(false, 0f, 1.3f, 30f, 33f), MarkerSliders.Slid(round, width, 6, throwUp));
    }
}
