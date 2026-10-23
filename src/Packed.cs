using System;
using System.IO;
using System.IO.Compression;

namespace FlowingWater;

/// <summary>Bytes kept in the world's objects, without the game: gzip of what a binary writer wrote.</summary>
internal static class Packed
{
    /// <summary>What <paramref name="write"/> writes, gzipped.</summary>
    public static byte[] Write(Action<BinaryWriter> write)
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip))
        {
            write(writer);
        }

        return packed.ToArray();
    }

    /// <summary>What <paramref name="read"/> reads from bytes <see cref="Write"/> made.</summary>
    /// <param name="data">The bytes.</param>
    /// <param name="what">What they hold, for the error.</param>
    /// <param name="read">Reads it, throwing <see cref="InvalidDataException"/> when it is not that.</param>
    /// <exception cref="InvalidDataException">The bytes are not gzip, or end too soon, or <paramref name="read"/> refused them.</exception>
    public static T Read<T>(byte[] data, string what, Func<BinaryReader, T> read)
    {
        try
        {
            using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);
            return read(reader);
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException(what + " cut short", e);
        }
    }
}
