using System.IO;
using System.Linq;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// What is left of the column water before the voxels: a tile's depths read in either format and any of its grids,
/// and a tile that is not gzip, cut short, or of another format or grid refused as unreadable, saying why.
/// </summary>
public class ColumnTileTests
{
    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    public void TheSecondFormatIsReadOnEachGrid(int side)
    {
        float[] depths = ColumnTile.Depths(Tile(2, side, side), out int read);

        Assert.Equal(side, read);
        Assert.Equal(side * side, depths.Length);
        Assert.Equal(0f, depths[0]);
        Assert.Equal(0.07f, depths[7], 4);
        Assert.Equal(1.23f, depths[123], 4);
    }

    [Fact]
    public void TheFirstFormatIs64By64WrittenAsItsCellCount()
    {
        float[] depths = ColumnTile.Depths(Tile(1, 64 * 64, 64), out int side);

        Assert.Equal(64, side);
        Assert.Equal(0.5f, depths[50], 4);
    }

    [Theory]
    [InlineData(3, 64, 64)]
    [InlineData(2, 100, 100)]
    [InlineData(1, 128 * 128, 128)]
    [InlineData(2, -5, 1)]
    public void AnotherFormatOrGridIsRefusedSayingSo(byte format, int number, int side)
    {
        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => ColumnTile.Depths(Tile(format, number, side), out _));

        Assert.Equal($"format {format} with grid {number}, expected format 2 with 64, 128 or 256 cells per side", refused.Message);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(5 + 100)]
    [InlineData(5 + (64 * 64 * 2) + 10)]
    public void ATileCutShortIsRefused(int length)
    {
        byte[] whole = Unpacked(Tile(2, 64, 64));

        InvalidDataException refused = Assert.Throws<InvalidDataException>(() => ColumnTile.Depths(Packed.Write(w => w.Write(whole[..length])), out _));

        Assert.Equal("column tile cut short", refused.Message);
    }

    [Fact]
    public void BytesThatAreNotGzipAreRefused()
    {
        Assert.Throws<InvalidDataException>(() => ColumnTile.Depths([2, 64, 0, 0, 0, 1, 2], out _));
    }

    // A tile as the column water wrote it: cell i holds i centimetres up to 200, then none; speeds of 1.
    private static byte[] Tile(byte format, int number, int side) => Packed.Write(writer =>
    {
        writer.Write(format);
        writer.Write(number);
        int cells = side * side;
        foreach (int i in Enumerable.Range(0, cells))
        {
            writer.Write((short)(i <= 200 ? i : 0));
        }

        writer.Write(Enumerable.Repeat((byte)1, 2 * cells).ToArray());
    });

    private static byte[] Unpacked(byte[] data)
    {
        using var unpacked = new MemoryStream();
        using (var gzip = new System.IO.Compression.GZipStream(new MemoryStream(data), System.IO.Compression.CompressionMode.Decompress))
        {
            gzip.CopyTo(unpacked);
        }

        return unpacked.ToArray();
    }
}
