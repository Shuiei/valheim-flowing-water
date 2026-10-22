using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;

namespace FlowingWater;

/// <summary>
/// What erosion did to a zone's ground, as the world keeps it, without the game: a height change in millimetres
/// at each of the zone's own heightmap points (one a metre, 64 by 64: the 65th row and column are the next
/// zones'), the simulation's cells read from and written to it, and the cells kept from erosion (near what
/// players built, and the game's own places).
/// </summary>
internal static class ErosionGround
{
    /// <summary>Heightmap points along a zone's side that the zone owns.</summary>
    public const int Size = 64;

    /// <summary>No erosion within this many metres of anything a player built.</summary>
    public const float ProtectedRadius = 5f;

    private const byte FormatVersion = 1;

    private const float Unit = 0.001f;

    /// <summary>A change in metres at each simulation cell, as millimetres at each heightmap point (the cell on it).</summary>
    /// <param name="change">Metres per cell, row by row.</param>
    /// <param name="cells">Cells along a zone's side.</param>
    public static short[] ToPoints(float[] change, int cells)
    {
        short[] points = new short[Size * Size];
        int step = cells / Size;
        for (int z = 0; z < Size; z++)
        {
            for (int x = 0; x < Size; x++)
            {
                float metres = change[(z * step * cells) + (x * step)];
                points[(z * Size) + x] = (short)Math.Round(Math.Max(short.MinValue, Math.Min(short.MaxValue, metres / Unit)));
            }
        }

        return points;
    }

    /// <summary>The change at each simulation cell, metres: that of the heightmap point it lies on or after.</summary>
    /// <param name="points">Millimetres per point, row by row.</param>
    /// <param name="cells">Cells along a zone's side.</param>
    public static float[] ToCells(short[] points, int cells)
    {
        float[] change = new float[cells * cells];
        int step = cells / Size;
        for (int z = 0; z < cells; z++)
        {
            for (int x = 0; x < cells; x++)
            {
                change[(z * cells) + x] = points[((z / step) * Size) + (x / step)] * Unit;
            }
        }

        return change;
    }

    /// <summary>A point's change in metres.</summary>
    /// <param name="points">Millimetres per point, row by row.</param>
    /// <param name="x">The point along x (0 to 63).</param>
    /// <param name="z">Along z.</param>
    public static float At(short[] points, int x, int z) => points[(z * Size) + x] * Unit;

    /// <summary>The points as stored in the world (gzip: version, size, then little-endian shorts).</summary>
    public static byte[] Encode(short[] points)
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip))
        {
            writer.Write(FormatVersion);
            writer.Write(Size);
            foreach (short point in points)
            {
                writer.Write(point);
            }
        }

        return packed.ToArray();
    }

    /// <summary>The points stored by <see cref="Encode"/>.</summary>
    /// <exception cref="InvalidDataException">The bytes are not that (or another version or size).</exception>
    public static short[] Decode(byte[] data)
    {
        try
        {
            using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);
            byte version = reader.ReadByte();
            int size = reader.ReadInt32();
            if (version != FormatVersion || size != Size)
            {
                throw new InvalidDataException($"ground change version {version} of {size} points a side, expected {FormatVersion} of {Size}");
            }

            short[] points = new short[Size * Size];
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = reader.ReadInt16();
            }

            return points;
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("ground change cut short", e);
        }
    }

    /// <summary>The cells of a zone's grid within a radius of any of these places (where erosion leaves the ground alone).</summary>
    /// <param name="cells">Cells along the zone's side.</param>
    /// <param name="originX">The zone's west edge, world x.</param>
    /// <param name="originZ">Its south edge, world z.</param>
    /// <param name="keep">Places (world x, z) and how far around each the ground is kept, metres.</param>
    public static bool[] Kept(int cells, float originX, float originZ, IEnumerable<(float X, float Z, float Radius)> keep)
    {
        bool[] kept = new bool[cells * cells];
        float cell = (float)Size / cells;
        foreach ((float px, float pz, float radius) in keep)
        {
            int x0 = Math.Max(0, (int)Math.Floor((px - radius - originX) / cell));
            int x1 = Math.Min(cells - 1, (int)Math.Ceiling((px + radius - originX) / cell));
            int z0 = Math.Max(0, (int)Math.Floor((pz - radius - originZ) / cell));
            int z1 = Math.Min(cells - 1, (int)Math.Ceiling((pz + radius - originZ) / cell));
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    float dx = originX + (x * cell) - px;
                    float dz = originZ + (z * cell) - pz;
                    kept[(z * cells) + x] |= (dx * dx) + (dz * dz) <= radius * radius;
                }
            }
        }

        return kept;
    }
}
