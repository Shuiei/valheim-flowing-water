using System;
using System.Linq;
using FlowingWater.Client;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// What a source's ghost draws while it is placed: facing a way, an arrow straight ahead and the outline of its outlet
/// fanned out as far as its water goes (a jet's landing, held to the most); all round, a ring and no arrow.
/// </summary>
public class GhostOutlineTests
{
    [Fact]
    public void AnAllRoundSourceDrawsARingAndNoArrow()
    {
        var lines = GhostOutline.Outline(SourceShape.Round, 2f);

        Assert.Equal(GhostOutline.RingSides, lines.Count);
        Assert.All(lines, line => Assert.Equal(GhostOutline.Ring, Math.Sqrt((line.From.X * line.From.X) + (line.From.Y * line.From.Y)), 3));
        Assert.Null(GhostOutline.Arrow(SourceShape.Round, 2f));
    }

    [Fact]
    public void AFacingSourcesArrowPointsStraightAhead()
    {
        var arrow = GhostOutline.Arrow(new SourceShape(false, 135f, 2f, 30f, 0f), 2f);

        Assert.NotNull(arrow);
        Assert.Equal(0f, arrow.Value.Tip.X);
        Assert.True(arrow.Value.Tip.Y > arrow.Value.Shaft && arrow.Value.Shaft > 0f);
        Assert.Equal(-arrow.Value.Left.X, arrow.Value.Right.X);
    }

    [Fact]
    public void TheOutlineIsTheOutletsWidthFannedOutByItsSpread()
    {
        var straight = GhostOutline.Outline(new SourceShape(false, 0f, 4f, 0f, 0f), 2f);
        var fanned = GhostOutline.Outline(new SourceShape(false, 0f, 4f, 60f, 0f), 2f);

        Assert.Equal(((-2f, 0f), (2f, 0f)), straight[0]);
        Assert.Equal((2f, GhostOutline.Along), straight[2].To);
        float fan = (float)Math.Tan(30.0 * Math.PI / 180.0) * GhostOutline.Along;
        Assert.Equal(2f + fan, fanned[2].To.X, 3);
        Assert.Equal(-2f - fan, fanned[1].To.X, 3);
    }

    [Fact]
    public void AJetsOutlineGoesAsFarAsItLandsWithinTheMost()
    {
        SourceShape weak = SourceShape.Jet(0f), strong = new(false, 0f, 0f, 20f, 45f);
        float near = SourcePreview.Lands(weak, 0.5f) ?? 0f, far = SourcePreview.Lands(strong, 100f) ?? 0f;

        Assert.InRange(near, 0.5f, far - 1f);
        Assert.InRange(far, near + 1f, GhostOutline.MostOut);
        Assert.Equal(near, GhostOutline.Outline(weak, 0.5f).Max(line => line.To.Y), 3);
        Assert.Equal(far, GhostOutline.Outline(strong, 100f).Max(line => line.To.Y), 3);
    }
}
