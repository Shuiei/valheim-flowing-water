using FlowingWater.Client;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The panel's drawings of a source: from above, a river's band as wide as its outlet and a jet's fan opening out, a
/// ring all round; from the side, a jet's arc rising over the ground and landing as far out as its speed throws it.
/// </summary>
public class SourcePreviewTests
{
    [Fact]
    public void FromAboveARiverIsAsWideAsItsOutletAndAJetFansOut()
    {
        byte[] river = SourcePreview.Top(new SourceShape(false, 0f, 10f, 0f, 0f));
        byte[] jet = SourcePreview.Top(new SourceShape(false, 0f, 0f, 90f, 0f));

        // Near the top: the river's band spans its outlet's 120 pixels; the point's jet is still narrow there.
        Assert.True(SourcePreview.Drawn(river, 30, 30));
        Assert.False(SourcePreview.Drawn(jet, 30, 30));
        // Far down, the jet has fanned out past where the band ends.
        Assert.True(SourcePreview.Drawn(jet, 10, 110));
        Assert.False(SourcePreview.Drawn(river, 10, 110));
    }

    [Fact]
    public void AllRoundIsARingAboutTheSource()
    {
        byte[] round = SourcePreview.Top(SourceShape.Round);

        Assert.True(SourcePreview.Drawn(round, 80, 20));
        Assert.True(SourcePreview.Drawn(round, 40, 60));
        Assert.False(SourcePreview.Drawn(round, 5, 5));
    }

    [Fact]
    public void FromTheSideAJetRisesAndARiverRunsAlongTheGround()
    {
        byte[] jet = SourcePreview.Side(new SourceShape(false, 0f, 0f, 20f, 45f), 2f);
        byte[] river = SourcePreview.Side(new SourceShape(false, 0f, 6f, 0f, 0f), 2f);

        // 12 m/s thrown up 45 degrees peaks some 3.7 m up: over 2.9 m (row 80, 7 pixels a metre over the ground's row 100).
        bool high = false;
        for (int x = 20; x < 150; x++)
        {
            high |= SourcePreview.Drawn(jet, x, 80);
        }

        Assert.True(high);
        Assert.False(SourcePreview.Drawn(river, 80, 80));
        Assert.True(SourcePreview.Drawn(river, 80, 101));
    }

    [Fact]
    public void AJetLandsAsFarAsItsSpeedThrowsIt()
    {
        // A point's 2 m3/s leaves at the most speed, 12 m/s: thrown up 45 degrees it lands v^2 / g out.
        float? lands = SourcePreview.Lands(new SourceShape(false, 0f, 0f, 0f, 45f), 2f);

        Assert.NotNull(lands);
        Assert.Equal(12f * 12f / VoxelSolver.Gravity, lands.Value, 2);
        Assert.Null(SourcePreview.Lands(new SourceShape(false, 0f, 3f, 0f, 0f), 2f));
        Assert.Null(SourcePreview.Lands(SourceShape.Round, 2f));
    }
}
