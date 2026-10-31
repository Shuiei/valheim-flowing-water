using System.Collections.Generic;
using System.IO;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The server's jets as the players' games hear them: read back as written, each place's landing or none; landings to
/// a quarter metre, so a jet whose landing moves less is the same jet; bytes of another kind or version refused.
/// </summary>
public class JetNewsTests
{
    private static readonly SourceShape Jet = new(false, 45f, 2f, 20f, 30f);

    [Fact]
    public void JetsAreReadBackAsWrittenEachPlacesLandingOrNone()
    {
        var jets = new List<ServerJet>
        {
            new(new Vec3(1f, 2f, 3f), Jet, 8f, [new Vec3(10f, 0.5f, 12f), null]),
            new(new Vec3(-4f, 50f, 7.5f), new SourceShape(false, 270f, 0f, 0f, 60f), 2f, [new Vec3(-20f, 30f, 7.5f)]),
        };

        List<ServerJet> read = JetNews.Read(JetNews.Write(jets));

        Assert.True(JetNews.Same(jets, read));
        Assert.Null(read[0].Landings[1]);
        Assert.Equal(Jet, read[0].Shape);
        Assert.Equal(8f, read[0].Rate);
    }

    [Fact]
    public void LandingsAreSentToAQuarterMetreSoALittleMoveIsTheSameJet()
    {
        Vec3? a = JetNews.Rounded(new Vec3(10.1f, 0.62f, -3.13f));
        Vec3? b = JetNews.Rounded(new Vec3(10.05f, 0.55f, -3.2f));

        Assert.Equal(new Vec3(10f, 0.5f, -3.25f), a);
        Assert.Equal(a, b);
        Assert.Null(JetNews.Rounded(null));
    }

    [Fact]
    public void JetsDifferingInALandingAreNotTheSame()
    {
        var one = new List<ServerJet> { new(new Vec3(0f, 0f, 0f), Jet, 8f, [new Vec3(10f, 0f, 0f)]) };
        var other = new List<ServerJet> { new(new Vec3(0f, 0f, 0f), Jet, 8f, [new Vec3(10.25f, 0f, 0f)]) };
        var none = new List<ServerJet> { new(new Vec3(0f, 0f, 0f), Jet, 8f, [null]) };

        Assert.False(JetNews.Same(one, other));
        Assert.False(JetNews.Same(one, none));
        Assert.False(JetNews.Same(one, []));
        Assert.True(JetNews.Same(one, [new(new Vec3(0f, 0f, 0f), Jet, 8f, [new Vec3(10f, 0f, 0f)])]));
    }

    [Fact]
    public void BytesOfAnotherKindOrVersionAreRefused()
    {
        Assert.Throws<InvalidDataException>(() => JetNews.Read([1, 2, 3]));
        byte[] later = Packed.Write(writer =>
        {
            writer.Write(2);
            writer.Write(0);
        });
        Assert.Throws<InvalidDataException>(() => JetNews.Read(later));
    }
}
