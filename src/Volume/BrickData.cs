using System;
using System.IO;
using System.IO.Compression;

namespace FlowingWater.Volume;

/// <summary>
/// A brick's water as the world keeps it and players' games receive it, without the game: gzip of a version, the
/// brick's coordinates and voxel size, then per voxel its water (a byte, 200 a full voxel, a little more for water
/// pressed in) and, for voxels with water, its solid share (a byte, 255 all solid: where the water's floor is, which
/// the players' games cannot know) and its velocity (a byte per axis, square-root steps to 16 m/s).
/// </summary>
internal static class BrickData
{
    /// <summary>The fastest speed kept, metres a second; faster is kept as this.</summary>
    public const float MaxSpeed = 16f;

    private const byte FormatVersion = 1;

    private const float Full = 200f;

    /// <summary>The brick as bytes.</summary>
    /// <param name="brick">The brick.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    public static byte[] Encode(Brick brick, float voxelSize)
    {
        using var packed = new MemoryStream();
        using (var gzip = new GZipStream(packed, CompressionMode.Compress, leaveOpen: true))
        using (var writer = new BinaryWriter(gzip))
        {
            writer.Write(FormatVersion);
            writer.Write(brick.X);
            writer.Write(brick.Y);
            writer.Write(brick.Z);
            writer.Write(voxelSize);
            // A voxel's velocity is kept when its water byte is not 0: the reader goes by the same byte.
            byte[] water = new byte[Brick.Count];
            for (int i = 0; i < Brick.Count; i++)
            {
                water[i] = (byte)Math.Min(255, (int)Math.Round(brick.Water[i] * Full));
            }

            writer.Write(water);
            float area = voxelSize * voxelSize;
            for (int i = 0; i < Brick.Count; i++)
            {
                if (water[i] > 0)
                {
                    (float x, float y, float z) = Velocity(brick, i, area);
                    writer.Write((byte)Math.Round(Math.Min(1f, Math.Max(0f, brick.Solid[i])) * 255f));
                    writer.Write(SpeedByte(x));
                    writer.Write(SpeedByte(y));
                    writer.Write(SpeedByte(z));
                }
            }
        }

        return packed.ToArray();
    }

    /// <summary>The brick kept by <see cref="Encode"/>.</summary>
    /// <exception cref="InvalidDataException">The bytes are not that (or of another version).</exception>
    public static BrickView Decode(byte[] data)
    {
        try
        {
            using var gzip = new GZipStream(new MemoryStream(data), CompressionMode.Decompress);
            using var reader = new BinaryReader(gzip);
            byte version = reader.ReadByte();
            if (version != FormatVersion)
            {
                throw new InvalidDataException($"brick data version {version}, expected {FormatVersion}");
            }

            var view = new BrickView(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadSingle());
            byte[] water = reader.ReadBytes(Brick.Count);
            if (water.Length < Brick.Count)
            {
                throw new EndOfStreamException();
            }

            for (int i = 0; i < Brick.Count; i++)
            {
                view.Water[i] = water[i] / Full;
            }

            for (int i = 0; i < Brick.Count; i++)
            {
                if (water[i] > 0)
                {
                    view.Solid[i] = reader.ReadByte() / 255f;
                    view.VelocityX[i] = Speed(reader.ReadSByte());
                    view.VelocityY[i] = Speed(reader.ReadSByte());
                    view.VelocityZ[i] = Speed(reader.ReadSByte());
                }
            }

            return view;
        }
        catch (EndOfStreamException e)
        {
            throw new InvalidDataException("brick data cut short", e);
        }
    }

    /// <summary>
    /// A voxel's velocity, metres a second: the mean of what flows through its two faces on each axis, over a face's
    /// area (its own east, upper and north faces, and those of the voxels west, under and south of it).
    /// </summary>
    public static (float X, float Y, float Z) Velocity(Brick brick, int i, float faceArea)
    {
        (int x, int y, int z) = Brick.Place(i);
        float west = x > 0 ? brick.FlowX[i - 1] : brick.Beside[1]?.FlowX[i + Brick.Size - 1] ?? 0f;
        float under = y > 0 ? brick.FlowY[i - (Brick.Size * Brick.Size)] : brick.Beside[3]?.FlowY[i + ((Brick.Size - 1) * Brick.Size * Brick.Size)] ?? 0f;
        float south = z > 0 ? brick.FlowZ[i - Brick.Size] : brick.Beside[5]?.FlowZ[i + ((Brick.Size - 1) * Brick.Size)] ?? 0f;
        float half = 0.5f / faceArea;
        return ((brick.FlowX[i] + west) * half, (brick.FlowY[i] + under) * half, (brick.FlowZ[i] + south) * half);
    }

    private static sbyte SpeedByte(float speed)
    {
        float step = (float)Math.Sqrt(Math.Min(Math.Abs(speed), MaxSpeed) / MaxSpeed) * 127f;
        return (sbyte)((int)Math.Round(step) * Math.Sign(speed));
    }

    private static float Speed(sbyte value)
    {
        float share = value / 127f;
        return share * Math.Abs(share) * MaxSpeed;
    }
}

/// <summary>A brick's water as a player's game receives it (<see cref="BrickData.Decode"/>).</summary>
internal sealed class BrickView
{
    /// <summary>An empty view of the brick at these brick coordinates.</summary>
    public BrickView(int x, int y, int z, float voxelSize)
    {
        X = x;
        Y = y;
        Z = z;
        VoxelSize = voxelSize;
    }

    /// <summary>The brick's place in bricks (its first voxel at 16 times these).</summary>
    public int X { get; }

    /// <inheritdoc cref="X"/>
    public int Y { get; }

    /// <inheritdoc cref="X"/>
    public int Z { get; }

    /// <summary>Metres along a voxel's side.</summary>
    public float VoxelSize { get; }

    /// <summary>Each voxel's water, as a share of its volume.</summary>
    public float[] Water { get; } = new float[Brick.Count];

    /// <summary>The solid share of each voxel with water (its water's floor); 0 elsewhere.</summary>
    public float[] Solid { get; } = new float[Brick.Count];

    /// <summary>Each voxel's velocity east, metres a second.</summary>
    public float[] VelocityX { get; } = new float[Brick.Count];

    /// <summary>Up.</summary>
    public float[] VelocityY { get; } = new float[Brick.Count];

    /// <summary>North.</summary>
    public float[] VelocityZ { get; } = new float[Brick.Count];
}
