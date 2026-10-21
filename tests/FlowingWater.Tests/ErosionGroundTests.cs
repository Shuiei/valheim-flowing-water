using System.IO;
using System.Linq;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// Erosion's change to a zone's ground as the world keeps it: millimetres at the game's one-metre points, read
/// from and written to the simulation's cells, stored and read back, and the cells kept near what players built.
/// </summary>
public class ErosionGroundTests
{
    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    public void APointTakesTheCellOnItAndGivesItBackToTheCellsAfterIt(int cells)
    {
        float[] change = new float[cells * cells];
        int step = cells / ErosionGround.Size;

        // The cell on point (3, 5), and one between points (not on any): only the first is kept.
        change[(5 * step * cells) + (3 * step)] = -1.234f;
        change[(5 * step * cells) + (3 * step) + step - 1] = step > 1 ? 9f : -1.234f;

        short[] points = ErosionGround.ToPoints(change, cells);
        float[] back = ErosionGround.ToCells(points, cells);

        Assert.Equal(-1.234f, ErosionGround.At(points, 3, 5), 3);
        for (int dz = 0; dz < step; dz++)
        {
            for (int dx = 0; dx < step; dx++)
            {
                Assert.Equal(-1.234f, back[(((5 * step) + dz) * cells) + (3 * step) + dx], 3);
            }
        }
    }

    [Fact]
    public void ChangesAreKeptToTheMillimetreWithinShortRange()
    {
        float[] change = new float[64 * 64];
        change[0] = 0.0004f;
        change[1] = 0.0006f;
        change[2] = 100f;
        change[3] = -100f;

        short[] points = ErosionGround.ToPoints(change, 64);

        Assert.Equal(new short[] { 0, 1, short.MaxValue, short.MinValue }, points[..4]);
    }

    [Fact]
    public void StoredPointsReadBackTheSame()
    {
        short[] points = new short[ErosionGround.Size * ErosionGround.Size];
        for (int i = 0; i < points.Length; i++)
        {
            points[i] = (short)((i * 37 % 16001) - 8000);
        }

        Assert.Equal(points, ErosionGround.Decode(ErosionGround.Encode(points)));
    }

    [Fact]
    public void BytesThatAreNotAGroundChangeAreRefused()
    {
        byte[] stored = ErosionGround.Encode(new short[ErosionGround.Size * ErosionGround.Size]);

        Assert.ThrowsAny<InvalidDataException>(() => ErosionGround.Decode([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => ErosionGround.Decode(stored[..(stored.Length / 2)]));
    }

    [Fact]
    public void CellsWithinTheRadiusOfAPlaceAreKept()
    {
        // A piece at (10, 10) metres from the zone's corner, cells of a metre.
        bool[] kept = ErosionGround.Kept(64, -32f, -32f, [(-22f, -22f, ErosionGround.ProtectedRadius)]);

        Assert.True(kept[(10 * 64) + 10]);
        Assert.True(kept[(10 * 64) + 15]);
        Assert.True(kept[(13 * 64) + 14]);
        Assert.False(kept[(10 * 64) + 16]);
        Assert.False(kept[(14 * 64) + 14]);
        Assert.Equal(81, kept.Count(k => k));
    }

    [Fact]
    public void APlaceInTheNextZoneKeepsTheCellsNearTheBorder()
    {
        // A piece 2 m beyond the zone's east edge.
        bool[] kept = ErosionGround.Kept(64, -32f, -32f, [(34f, 0f, ErosionGround.ProtectedRadius)]);

        Assert.True(kept[(32 * 64) + 63]);
        Assert.True(kept[(32 * 64) + 61]);
        Assert.False(kept[(32 * 64) + 60]);
    }
}
