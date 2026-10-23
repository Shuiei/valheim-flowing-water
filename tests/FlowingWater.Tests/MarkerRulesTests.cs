using System.Collections.Generic;
using System.Globalization;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// A source's or drain's rate and reach as the server takes them: held to 100 m³/s, placed or fixed, told of once for
/// each; a drain of rate 0 still takes everything; rates that are no number refused; a drain's reach held to 16 m;
/// the config's fixed ones read, and those written wrong left out.
/// </summary>
public class MarkerRulesTests
{
    private readonly List<string> _warnings = [];

    private readonly MarkerRules _rules;

    public MarkerRulesTests() => _rules = new MarkerRules(_warnings.Add);

    [Theory]
    [InlineData(false, 2f)]
    [InlineData(false, 100f)]
    [InlineData(true, 0.5f)]
    [InlineData(true, 100f)]
    public void ARateUpToTheMostIsUsedAsItIs(bool drain, float rate)
    {
        Assert.True(_rules.TryRate(rate, drain, "here", "a", out float used));

        Assert.Equal(rate, used);
        Assert.Empty(_warnings);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARateOverTheMostIsHeldToIt(bool drain)
    {
        Assert.True(_rules.TryRate(500f, drain, "here", "a", out float used));

        Assert.Equal(MarkerRules.MaxRate, used);
        Assert.Equal(100f, MarkerRules.MaxRate);
        string warning = Assert.Single(_warnings);
        Assert.StartsWith(drain ? "Drain at here: rate 500 m³/s" : "Source at here: rate 500 m³/s", warning);
    }

    [Fact]
    public void ADrainOfRateZeroTakesEverythingStill()
    {
        Assert.True(_rules.TryRate(0f, true, "here", "a", out float used));

        Assert.Equal(0f, used);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void EachOneHeldIsToldOfOnce()
    {
        for (int scan = 0; scan < 3; scan++)
        {
            _rules.TryRate(500f, false, "here", "a", out _);
            _rules.TryRate(500f, false, "there", "b", out _);
        }

        Assert.Equal(2, _warnings.Count);
        Assert.Contains("there", _warnings[1]);
    }

    [Theory]
    [InlineData(false, 0f)]
    [InlineData(false, -1f)]
    [InlineData(true, -1f)]
    [InlineData(false, float.NaN)]
    [InlineData(true, float.NaN)]
    [InlineData(false, float.PositiveInfinity)]
    [InlineData(true, float.PositiveInfinity)]
    public void ARateNotUsableIsRefused(bool drain, float rate)
    {
        Assert.False(_rules.TryRate(rate, drain, "here", "a", out _));

        Assert.EndsWith("is not usable; ignored", Assert.Single(_warnings));
    }

    [Theory]
    [InlineData(1f, 1f)]
    [InlineData(-3f, 0f)]
    [InlineData(16f, 16f)]
    [InlineData(1000f, 16f)]
    public void ADrainReachesSixteenMetresAtMost(float radius, float reach) =>
        Assert.Equal(reach, MarkerRules.Reach(radius));

    [Fact]
    public void FixedSourcesAreReadWithTheirRates()
    {
        var found = _rules.Fixed(" 10,-20,5 ;; 0.5,1.5,2 ", false, 1f);

        Assert.Equal([(10f, -20f, 5f, 0f), (0.5f, 1.5f, 2f, 0f)], found);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void FixedOnesAreHeldToTheMostRateTooEachToldOfOnce()
    {
        _rules.Fixed("0,0,500", false, 1f);
        var found = _rules.Fixed("0,0,500; 5,5,0", true, 1f);

        Assert.Equal([(0f, 0f, MarkerRules.MaxRate, 1f), (5f, 5f, 0f, 1f)], found);
        Assert.Single(_warnings);
    }

    [Fact]
    public void AFixedDrainHasItsRadiusOrTheDefaultHeldToSixteenMetres()
    {
        var found = _rules.Fixed("0,0,1; 0,0,1,3; 0,0,1,40", true, 2f);

        Assert.Equal([2f, 3f, MarkerRules.MaxDrainRadius], found.ConvertAll(d => d.Radius));
    }

    [Theory]
    [InlineData("1,2", false)]
    [InlineData("1,2,3,4", false)]
    [InlineData("1,2,3,4,5", true)]
    [InlineData("a,2,3", false)]
    [InlineData("1,b,3", false)]
    [InlineData("1,2,c", true)]
    [InlineData("1,2,3,d", true)]
    [InlineData("1,2,3,0", true)]
    [InlineData("1,2,3,-1", true)]
    public void AFixedOneWrittenWrongIsLeftOut(string text, bool drain)
    {
        Assert.Empty(_rules.Fixed(text, drain, 1f));

        Assert.Equal($"Fixed {(drain ? "drain" : "source")} \"{text}\" is not {(drain ? "x,z,rate or x,z,rate,radius" : "x,z,rate")}; ignored", Assert.Single(_warnings));
    }

    [Fact]
    public void AFixedOneWithARateNotUsableIsLeftOut()
    {
        Assert.Empty(_rules.Fixed("1,2,0", false, 1f));

        Assert.Equal("Source at 1,2,0: rate 0 is not usable; ignored", Assert.Single(_warnings));
    }

    [Fact]
    public void FixedOnesAreReadTheSameWhateverTheCulture()
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");

            Assert.Equal([(1.5f, 2f, 3.25f, 0f)], _rules.Fixed("1.5,2,3.25", false, 1f));
        }
        finally
        {
            CultureInfo.CurrentCulture = culture;
        }
    }
}
