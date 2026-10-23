using System.IO;
using System.IO.Compression;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// Bytes kept in the world's objects: what is written reads back the same, empty, small, large or random; and the gzip
/// written whole in one go is the same gzip as one written a write at a time, either way round, so data saved or sent
/// by an older version still reads and the other way.
/// </summary>
public class PackedTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(37)]
    [InlineData(4096)]
    [InlineData(1 << 20)]
    public void RandomBytesReadBackAsWritten(int length)
    {
        byte[] bytes = Bytes(length, seed: length);

        byte[] packed = Packed.Write(writer => writer.Write(bytes));

        Assert.Equal(bytes, Packed.Read(packed, "test data", reader => reader.ReadBytes(length + 1)));
    }

    [Fact]
    public void ManySmallWritesReadBackAsWritten()
    {
        // As a brick is written: thousands of bytes, ints and floats, one at a time.
        byte[] packed = Packed.Write(writer =>
        {
            for (int i = 0; i < 5000; i++)
            {
                writer.Write((byte)i);
                writer.Write(i * -7);
                writer.Write(i / 3f);
            }
        });

        Packed.Read(packed, "test data", reader =>
        {
            for (int i = 0; i < 5000; i++)
            {
                Assert.Equal((byte)i, reader.ReadByte());
                Assert.Equal(i * -7, reader.ReadInt32());
                Assert.Equal(i / 3f, reader.ReadSingle());
            }

            Assert.Throws<EndOfStreamException>(() => reader.ReadByte());
            return 0;
        });
    }

    [Fact]
    public void ItIsTheGzipOfAWriteAtATimeEitherWayRound()
    {
        byte[] bytes = Bytes(70_000, seed: 3);

        // An older version's: each write went straight into the gzip stream.
        using var older = new MemoryStream();
        using (var gzip = new GZipStream(older, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip))
        {
            foreach (byte b in bytes)
            {
                writer.Write(b);
            }
        }

        Assert.Equal(bytes, Packed.Read(older.ToArray(), "test data", reader => reader.ReadBytes(bytes.Length + 1)));

        using var unpacked = new MemoryStream();
        using (var gzip = new GZipStream(new MemoryStream(Packed.Write(writer => writer.Write(bytes))), CompressionMode.Decompress))
        {
            gzip.CopyTo(unpacked);
        }

        Assert.Equal(bytes, unpacked.ToArray());
    }

    [Fact]
    public void BytesCutShortAreRefused()
    {
        byte[] packed = Packed.Write(writer => writer.Write(12345L));

        Assert.Throws<InvalidDataException>(() => Packed.Read(packed, "test data", reader => reader.ReadInt64() + reader.ReadInt64()));
    }

    // Random-looking bytes, the same each run, half of them in runs (as a brick's water is) so that they pack.
    private static byte[] Bytes(int length, int seed)
    {
        byte[] bytes = new byte[length];
        new Scrambled(seed).Fill(bytes);
        for (int i = 0; i < length / 2; i++)
        {
            bytes[i] = (byte)(i / 64);
        }

        return bytes;
    }
}
