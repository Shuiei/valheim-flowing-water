using FlowingWater.Client;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The values the next source or drain placed with the hoe takes: from the settings (held, or the mod's own default
/// for a setting that is not usable), each kind its own, kept until changed; a refused value changes nothing.
/// </summary>
public class NextPlacementTests
{
    [Fact]
    public void TheValuesStartFromTheSettings()
    {
        var next = new NextPlacement(3f, 0.5f, 2f);

        Assert.Equal(3f, next.Rate(drain: false));
        Assert.Equal(0.5f, next.Rate(drain: true));
        Assert.Equal(2f, next.Radius);
    }

    [Fact]
    public void SettingsOverTheMostAreHeld()
    {
        var next = new NextPlacement(500f, 200f, 40f);

        Assert.Equal(100f, next.Rate(drain: false));
        Assert.Equal(100f, next.Rate(drain: true));
        Assert.Equal(16f, next.Radius);
    }

    [Fact]
    public void SettingsThatAreNotUsableGiveTheModsOwnDefaults()
    {
        var next = new NextPlacement(0f, -1f, float.NaN);

        Assert.Equal(NextPlacement.SourceRateDefault, next.Rate(drain: false));
        Assert.Equal(NextPlacement.DrainRateDefault, next.Rate(drain: true));
        Assert.Equal(NextPlacement.RadiusDefault, next.Radius);
    }

    [Fact]
    public void EachKindKeepsItsOwnRate()
    {
        var next = new NextPlacement(2f, 0f, 1f);

        Assert.True(next.TrySetRate(drain: false, 7.5f, out string sourceNote));
        Assert.True(next.TrySetRate(drain: true, 1.5f, out string drainNote));

        Assert.Equal(7.5f, next.Rate(drain: false));
        Assert.Equal(1.5f, next.Rate(drain: true));
        Assert.Null(sourceNote);
        Assert.Null(drainNote);
    }

    [Fact]
    public void ARefusedRateLeavesTheRateAsItWas()
    {
        var next = new NextPlacement(2f, 0f, 1f);

        Assert.False(next.TrySetRate(drain: false, 0f, out string note));

        Assert.Equal(2f, next.Rate(drain: false));
        Assert.Equal("a source's rate must be above 0", note);
    }

    [Fact]
    public void ARateOverTheMostIsKeptHeld()
    {
        var next = new NextPlacement(2f, 0f, 1f);

        Assert.True(next.TrySetRate(drain: true, 1000f, out string note));

        Assert.Equal(100f, next.Rate(drain: true));
        Assert.NotNull(note);
    }

    [Fact]
    public void ARadiusIsKeptHeldAndARefusedOneChangesNothing()
    {
        var next = new NextPlacement(2f, 0f, 1f);

        Assert.True(next.TrySetRadius(3f, out _));
        Assert.Equal(3f, next.Radius);
        Assert.True(next.TrySetRadius(0.1f, out string held));
        Assert.Equal(0.25f, next.Radius);
        Assert.Equal("held to 0.25 m, the least", held);
        Assert.False(next.TrySetRadius(0f, out string refused));
        Assert.Equal(0.25f, next.Radius);
        Assert.Equal("a drain's radius must be above 0", refused);
    }

    [Fact]
    public void TheNextSourceStartsAsASlimJetAndKeepsTheShapeSet()
    {
        var next = new NextPlacement(2f, 0f, 1f);

        Assert.False(next.Shape.AllRound);
        Assert.Equal("a slim jet spray", MarkerSliders.Kind(next.Shape));

        next.SetShape(false, 6f, 0f, 0f);
        SourceShape facing = next.ShapeFacing(135f);

        Assert.Equal(135f, facing.Heading);
        Assert.Equal(6f, facing.Width);
        Assert.Equal("a wide river", MarkerSliders.Kind(facing));

        next.SetShape(true, 6f, 0f, 0f);
        Assert.True(next.ShapeFacing(10f).AllRound);
        Assert.Equal(6f, next.Shape.Width);
    }

    [Fact]
    public void ASlidersRateAndRadiusAreTakenAsTypedOnesAre()
    {
        var next = new NextPlacement(2f, 0f, 1f);

        Assert.True(next.TrySetRate(drain: false, 12.5f, out string note));
        Assert.Equal(12.5f, next.Rate(drain: false));
        Assert.Null(note);
        Assert.False(next.TrySetRate(drain: false, 0f, out _));
        Assert.Equal(12.5f, next.Rate(drain: false));
        Assert.True(next.TrySetRate(drain: true, 0f, out _));
        Assert.Equal(0f, next.Rate(drain: true));
        Assert.True(next.TrySetRadius(20f, out string held));
        Assert.Equal(16f, next.Radius);
        Assert.NotNull(held);
    }
}
