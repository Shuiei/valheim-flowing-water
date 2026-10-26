using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Which columns the card draws of those the server's water lets it: none along the square's edge, a column only once
/// it has been the card's for a while (its water settled), the wait kept as the square moves.
/// </summary>
public class SimHandoverTests
{
    private const int Columns = 16;

    // Every column lets the card draw it, with a fall over it.
    private static (float[] Texels, float[] Layers) AllDrawn()
    {
        float[] texels = new float[Columns * Columns * SimColumns.Texel];
        float[] layers = new float[Columns * Columns * SimColumns.LayerTexel];
        for (int k = 0; k < Columns * Columns; k++)
        {
            texels[(k * SimColumns.Texel) + 1] = SimColumns.Sheet;
            layers[(k * SimColumns.LayerTexel) + 2] = 12f;
        }

        return (texels, layers);
    }

    private static float[] Handed(SimHandover handover, float[] texels, float[] layers, float now, out float[] drawnLayers)
    {
        float[] drawn = new float[texels.Length];
        drawnLayers = new float[layers.Length];
        handover.Apply(texels, layers, now, drawn, drawnLayers);
        return drawn;
    }

    private static bool Drawn(float[] texels, int x, int z) => texels[(((z * Columns) + x) * SimColumns.Texel) + 1] > 0.5f;

    [Fact]
    public void AColumnIsDrawnOnlyOnceItHasBeenTheCardsForTheSettlingTime()
    {
        var handover = new SimHandover(Columns, 1f);
        (float[] texels, float[] layers) = AllDrawn();

        float[] first = Handed(handover, texels, layers, 10f, out float[] firstLayers);
        Assert.False(Drawn(first, 8, 8));
        Assert.Equal(SimColumns.DryTop, firstLayers[(((8 * Columns) + 8) * SimColumns.LayerTexel) + 2]);
        Assert.True(handover.Waiting);

        float[] later = Handed(handover, texels, layers, 10f + SimHandover.Settle, out float[] laterLayers);
        Assert.True(Drawn(later, 8, 8));
        Assert.Equal(12f, laterLayers[(((8 * Columns) + 8) * SimColumns.LayerTexel) + 2]);
        Assert.False(handover.Waiting);
    }

    [Fact]
    public void TheBandAlongTheSquaresEdgeIsNeverTheCards()
    {
        var handover = new SimHandover(Columns, 1f);
        (float[] texels, float[] layers) = AllDrawn();
        Handed(handover, texels, layers, 0f, out _);

        float[] drawn = Handed(handover, texels, layers, 100f, out _);

        Assert.False(Drawn(drawn, 0, 8));
        Assert.False(Drawn(drawn, 3, 8));
        Assert.True(Drawn(drawn, 4, 8));
        Assert.True(Drawn(drawn, Columns - 5, 8));
        Assert.False(Drawn(drawn, Columns - 4, 8));
        Assert.False(Drawn(drawn, 8, Columns - 1));
    }

    [Fact]
    public void AColumnTheServerTakesBackWaitsAgain()
    {
        var handover = new SimHandover(Columns, 1f);
        (float[] texels, float[] layers) = AllDrawn();
        Handed(handover, texels, layers, 0f, out _);
        Assert.True(Drawn(Handed(handover, texels, layers, 5f, out _), 8, 8));

        texels[(((8 * Columns) + 8) * SimColumns.Texel) + 1] = 0f;
        Handed(handover, texels, layers, 6f, out _);
        texels[(((8 * Columns) + 8) * SimColumns.Texel) + 1] = SimColumns.Sheet;

        Assert.False(Drawn(Handed(handover, texels, layers, 7f, out _), 8, 8));
        Assert.True(Drawn(Handed(handover, texels, layers, 7f + SimHandover.Settle, out _), 8, 8));
    }

    [Fact]
    public void TheWaitMovesWithTheSquare()
    {
        var handover = new SimHandover(Columns, 1f);
        (float[] texels, float[] layers) = AllDrawn();
        Handed(handover, texels, layers, 0f, out _);
        Handed(handover, texels, layers, 5f, out _);

        // Moved two columns east: what was column 10 is column 8 now, settled still; the new east edge waits.
        handover.Shift(2, 0);
        float[] drawn = Handed(handover, texels, layers, 5.1f, out _);

        Assert.True(Drawn(drawn, 8, 8));
        Assert.False(Drawn(drawn, Columns - 5, 8));
        Assert.True(handover.Waiting);
    }

    [Fact]
    public void TheBandIsAtLeastTwoColumnsOfLargeVoxels()
    {
        var handover = new SimHandover(Columns, 4f);
        (float[] texels, float[] layers) = AllDrawn();
        Handed(handover, texels, layers, 0f, out _);

        float[] drawn = Handed(handover, texels, layers, 100f, out _);

        Assert.False(Drawn(drawn, 1, 8));
        Assert.True(Drawn(drawn, 2, 8));
    }
}
