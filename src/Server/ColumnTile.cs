using System;
using System.IO;

namespace FlowingWater.Server;

/// <summary>
/// The water of a zone's column tile, the store of the water before it moved into voxels, read without the game: gzip
/// of a format byte, the grid, each cell's depth in centimetres, then its speeds east and north (a byte each, not
/// needed to move the water over). The first format is 64 cells per side and writes their count; the second writes
/// the cells per side: 64, 128 or 256 (cells of 1, 0.5 or 0.25 m).
/// </summary>
internal static class ColumnTile
{
    private const byte FirstFormat = 1;

    private const byte SecondFormat = 2;

    private const float DepthUnit = 0.01f;

    private static readonly int[] Sides = [64, 128, 256];

    /// <summary>Each cell's depth, metres (index z * side + x), and the cells per side.</summary>
    /// <exception cref="InvalidDataException">The bytes are not gzip, end too soon, or are another format or grid.</exception>
    public static float[] Depths(byte[] data, out int side)
    {
        (float[] depths, int cells) = Packed.Read(data, "column tile", Read);
        side = cells;
        return depths;
    }

    private static (float[] Depths, int Side) Read(BinaryReader reader)
    {
        byte format = reader.ReadByte();
        int number = reader.ReadInt32();
        int side = format == FirstFormat ? (int)Math.Round(Math.Sqrt(Math.Max(number, 0))) : number;
        if ((format != FirstFormat && format != SecondFormat) || Array.IndexOf(Sides, side) < 0 || (format == FirstFormat && side != Sides[0]))
        {
            throw new InvalidDataException($"format {format} with grid {number}, expected format {SecondFormat} with 64, 128 or 256 cells per side");
        }

        float[] depths = new float[side * side];
        for (int i = 0; i < depths.Length; i++)
        {
            depths[i] = reader.ReadInt16() * DepthUnit;
        }

        // The speeds are there too, or the tile was cut short.
        if (reader.ReadBytes(2 * depths.Length).Length < 2 * depths.Length)
        {
            throw new EndOfStreamException();
        }

        return (depths, side);
    }
}
