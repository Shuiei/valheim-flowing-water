using System.Globalization;
using System.Threading;
using FlowingWater.Client;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// A source's or drain's rate and radius as the panel takes them from what a player types, in any language: refused or
/// held exactly as the server would take them; and what the panel, the hoe and the hover text say.
/// </summary>
public class MarkerValuesTests
{
    [Theory]
    [InlineData("7.5", 7.5f)]
    [InlineData("7,5", 7.5f)]
    [InlineData(" 2 ", 2f)]
    [InlineData("0", 0f)]
    [InlineData("-1", -1f)]
    [InlineData("1e2", 100f)]
    [InlineData(".5", 0.5f)]
    public void ANumberIsReadWithAPointOrACommaBeforeItsDecimals(string text, float expected)
    {
        Assert.True(MarkerValues.TryNumber(text, out float value));

        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("1,000.5")]
    [InlineData("1.000,5")]
    [InlineData("1 000")]
    [InlineData("NaN")]
    [InlineData("Infinity")]
    [InlineData("-Infinity")]
    [InlineData("1e60")]
    public void WhatIsNoFiniteNumberIsNoNumber(string text)
    {
        Assert.False(MarkerValues.TryNumber(text, out _));
    }

    [Fact]
    public void TheGameLanguageDoesNotChangeHowANumberIsRead()
    {
        CultureInfo before = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

            Assert.True(MarkerValues.TryNumber("7.5", out float point));
            Assert.True(MarkerValues.TryNumber("7,5", out float comma));
            Assert.Equal(7.5f, point);
            Assert.Equal(7.5f, comma);
            Assert.Equal("7.5", MarkerValues.Format(7.5f));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = before;
        }
    }

    [Theory]
    [InlineData("2", false, 2f)]
    [InlineData("0.01", false, 0.01f)]
    [InlineData("100", false, 100f)]
    [InlineData("0", true, 0f)]
    [InlineData("3,5", true, 3.5f)]
    [InlineData("100", true, 100f)]
    public void ARateUpToTheMostIsTakenAsTyped(string text, bool drain, float expected)
    {
        Assert.True(MarkerValues.TryRate(text, drain, out float rate, out string note));

        Assert.Equal(expected, rate);
        Assert.Null(note);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ARateOverTheMostIsHeldToItAndSaysSo(bool drain)
    {
        Assert.True(MarkerValues.TryRate("250", drain, out float rate, out string note));

        Assert.Equal(MarkerRules.MaxRate, rate);
        Assert.Equal("held to 100 m³/s, the most a source or drain may have", note);
    }

    [Theory]
    [InlineData("0", false, "a source's rate must be above 0")]
    [InlineData("-2", false, "a source's rate must be above 0")]
    [InlineData("-0.5", true, "a drain's rate must be 0 or more (0 takes everything)")]
    [InlineData("lots", false, "\"lots\" is not a number")]
    [InlineData("", true, "\"\" is not a number")]
    public void ARateTheServerWouldRefuseIsRefusedWithTheReason(string text, bool drain, string reason)
    {
        Assert.False(MarkerValues.TryRate(text, drain, out _, out string note));

        Assert.Equal(reason, note);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(float.NegativeInfinity)]
    public void ARateThatIsNoNumberIsRefused(float value)
    {
        Assert.False(MarkerValues.TryRate(value, true, out _, out string note));

        Assert.Equal("the rate is not a number", note);
    }

    // What the panel takes the server takes, held the same: what a player is shown is what the water does.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ThePanelTakesARateExactlyAsTheServerDoes(bool drain)
    {
        var server = new MarkerRules(_ => { });
        float[] rates = [float.NaN, float.NegativeInfinity, -5f, -0.0001f, 0f, 0.0001f, 0.5f, 2f, 99.99f, 100f, 100.01f, 1e9f, float.PositiveInfinity];
        foreach (float value in rates)
        {
            bool serverTakes = server.TryRate(value, drain, "here", value.ToString(CultureInfo.InvariantCulture), out float used);
            bool panelTakes = MarkerValues.TryRate(value, drain, out float rate, out _);

            Assert.Equal(serverTakes, panelTakes);
            Assert.Equal(used, rate);
        }
    }

    [Theory]
    [InlineData("1", 1f, null)]
    [InlineData("0.25", 0.25f, null)]
    [InlineData("16", 16f, null)]
    [InlineData("3,5", 3.5f, null)]
    [InlineData("0.1", 0.25f, "held to 0.25 m, the least")]
    [InlineData("40", 16f, "held to 16 m, the most")]
    public void ARadiusIsHeldBetweenTheLeastAndTheMost(string text, float expected, string expectedNote)
    {
        Assert.True(MarkerValues.TryRadius(text, out float radius, out string note));

        Assert.Equal(expected, radius);
        Assert.Equal(expectedNote, note);
    }

    [Theory]
    [InlineData("0", "a drain's radius must be above 0")]
    [InlineData("-3", "a drain's radius must be above 0")]
    [InlineData("far", "\"far\" is not a number")]
    public void ARadiusOfNothingOrNoNumberIsRefused(string text, string reason)
    {
        Assert.False(MarkerValues.TryRadius(text, out _, out string note));

        Assert.Equal(reason, note);
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    public void ARadiusThatIsNoNumberIsRefused(float value)
    {
        Assert.False(MarkerValues.TryRadius(value, out _, out _));
    }

    // The panel never offers a radius the server would hold: its range lies within the server's reach.
    [Fact]
    public void ThePanelsRadiiAreAllWithinTheServersReach()
    {
        Assert.Equal(MarkerValues.MinRadius, MarkerRules.Reach(MarkerValues.MinRadius));
        Assert.Equal(MarkerRules.MaxDrainRadius, MarkerRules.Reach(16f));
    }

    [Theory]
    [InlineData(0f, "0")]
    [InlineData(2f, "2")]
    [InlineData(7.5f, "7.5")]
    [InlineData(0.333f, "0.33")]
    [InlineData(1234.5f, "1234.5")]
    public void NumbersAreShownWithUpToTwoDecimals(float value, string shown)
    {
        Assert.Equal(shown, MarkerValues.Format(value));
    }

    [Theory]
    [InlineData(0f, 0f)]
    [InlineData(2f, 2f)]
    [InlineData(-3.5f, -3.5f)]
    public void AValueTypedAsKeptIsTheSame(float typed, float kept)
    {
        Assert.True(MarkerValues.Same(typed, kept));
    }

    [Fact]
    public void AValueOffByARoundingIsTheSameButOneTypedOtherwiseIsNot()
    {
        // 0.1 summed ten times is not 1 in floats, a step of rounding away: the panel takes it for the same.
        float summed = 0f;
        for (int i = 0; i < 10; i++)
        {
            summed += 0.1f;
        }

        Assert.NotEqual(1f, summed);
        Assert.True(MarkerValues.Same(summed, 1f));
        Assert.True(MarkerValues.Same(100f, 100.00001f));
        Assert.True(MarkerValues.Same(0f, float.Epsilon));

        // A hundredth (the least the panel shows) is a change, and less near 0.
        Assert.False(MarkerValues.Same(2f, 2.01f));
        Assert.False(MarkerValues.Same(100f, 100.01f));
        Assert.False(MarkerValues.Same(0f, 0.0001f));
        Assert.False(MarkerValues.Same(1f, -1f));
    }

    [Fact]
    public void TheHintsSayWhatAValueMayBeAndTheMost()
    {
        Assert.Equal("Above 0, up to 100", MarkerValues.RateHint(drain: false));
        Assert.Equal("0 takes everything; up to 100", MarkerValues.RateHint(drain: true));
        Assert.Equal("0.25 to 16", MarkerValues.RadiusHint);
    }

    [Theory]
    [InlineData(false, 2f, 0f, "Source: 2 m³/s")]
    [InlineData(false, 500f, 0f, "Source: 100 m³/s")]
    [InlineData(false, 0f, 0f, "Source: no water (its rate, 0, is not usable)")]
    [InlineData(true, 0f, 1f, "Drain: takes everything within 1 m")]
    [InlineData(true, 0.5f, 3f, "Drain: 0.5 m³/s within 3 m")]
    [InlineData(true, 1f, 40f, "Drain: 1 m³/s within 16 m")]
    [InlineData(true, -1f, 1f, "Drain: no water (its rate, -1, is not usable)")]
    public void APlacedOneIsDescribedAsTheServerTakesIt(bool drain, float rate, float radius, string expected)
    {
        Assert.Equal(expected, MarkerValues.Describe(drain, rate, radius));
    }

    [Fact]
    public void TheHoverTextOffersTheSettingsOnlyToAPlayerWhoMayChangeThem()
    {
        Assert.Equal("Source: 2 m³/s\n[<color=yellow><b>$KEY_Use</b></color>] Settings", MarkerValues.Hover(false, 2f, 0f, mayChange: true));
        Assert.Equal("Drain: takes everything within 1 m", MarkerValues.Hover(true, 0f, 1f, mayChange: false));
    }

    [Theory]
    [InlineData(false, false, "Next source")]
    [InlineData(true, false, "Next drain")]
    [InlineData(false, true, "This source")]
    [InlineData(true, true, "This drain")]
    public void ThePanelsTitleSaysWhoseValuesItShows(bool drain, bool placed, string title)
    {
        Assert.Equal(title, MarkerValues.Title(drain, placed));
    }

    [Theory]
    [InlineData(false, false, "[E] Change")]
    [InlineData(true, false, "[Enter] Apply   [Esc] or [E] Done")]
    [InlineData(true, true, "[Enter] Apply   [Esc] or [E] Close   or Remove it below")]
    public void ThePanelsKeysSayHowToChangeAndLeave(bool editing, bool placed, string keys)
    {
        Assert.Equal(keys, MarkerValues.Keys(editing, placed));
    }

    // The keys of a placed one's panel name the button under them by its own label.
    [Fact]
    public void APlacedOnesKeysPointToItsRemoveButton()
    {
        Assert.Equal("Remove", MarkerValues.RemoveLabel);
        Assert.Contains(MarkerValues.RemoveLabel, MarkerValues.Keys(editing: true, placed: true));
        Assert.DoesNotContain(MarkerValues.RemoveLabel, MarkerValues.Keys(editing: true, placed: false));
        Assert.DoesNotContain(MarkerValues.RemoveLabel, MarkerValues.Keys(editing: false, placed: false));
    }

    [Fact]
    public void ThePiecesAreNamedAndDescribed()
    {
        Assert.Equal("Water source", MarkerValues.Name(drain: false));
        Assert.Equal("Water drain", MarkerValues.Name(drain: true));
        Assert.Contains("rate is set in the panel", MarkerValues.Description(drain: false));
        Assert.Contains("rate and radius are set in the panel", MarkerValues.Description(drain: true));
        Assert.All([MarkerValues.Description(drain: false), MarkerValues.Description(drain: true)], d => Assert.Contains("changes or removes it", d));
    }
}
