using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// A voxel of the card's solids opened for a source's outlet: no longer solid, the faces a piece closes still closed;
/// one past the bytes left alone.
/// </summary>
public class SolidsLayoutTests
{
    private const int Columns = 4;

    [Fact]
    public void AnOpenedVoxelIsNoLongerSolidItsClosedFacesStay()
    {
        byte[] solids = new byte[Columns * SolidsLayout.Rows * Columns];
        int i = SolidsLayout.Index(Columns, 1, 2, 3);
        solids[i] = SolidsLayout.Solid | SolidsLayout.ClosedEast;

        Assert.True(SolidsLayout.Open(solids, Columns, 1, 2, 3));
        Assert.Equal(SolidsLayout.ClosedEast, solids[i]);
        Assert.False(SolidsLayout.Open(solids, Columns, 1, 2, 3));
    }

    [Theory]
    [InlineData(-1, 0, 0)]
    [InlineData(Columns, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(0, SolidsLayout.Rows, 0)]
    [InlineData(0, 0, Columns)]
    public void AVoxelPastTheBytesIsLeftAlone(int x, int y, int z)
    {
        byte[] solids = new byte[Columns * SolidsLayout.Rows * Columns];
        for (int k = 0; k < solids.Length; k++)
        {
            solids[k] = SolidsLayout.Solid;
        }

        Assert.False(SolidsLayout.Open(solids, Columns, x, y, z));
        Assert.All(solids, b => Assert.Equal(SolidsLayout.Solid, b));
    }
}
