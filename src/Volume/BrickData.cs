using System;
using System.IO;

namespace FlowingWater.Volume;

/// <summary>
/// A brick's water as the world keeps it and players' games receive it, without the game: gzip of a version, the
/// brick's coordinates and voxel size, then per voxel its water (a byte, 200 a full voxel, a little more for water
/// pressed in) and, for voxels with water, its solid share (a byte, 255 all solid: where the water's floor is, which
/// the players' games cannot know) and its velocity (a byte per axis, square-root steps to 16 m/s). From version 2, a
/// water byte of 255 means more than the byte holds (water pressed in hard, or poured into smaller voxels): its exact
/// share follows the velocity, as a float, so none is lost. What a voxel's byte rounds off is carried on to the next
/// voxel with water, so the brick keeps its volume to within a byte's step, however thin its water.
/// </summary>
internal static class BrickData
{
    /// <summary>The fastest speed kept, metres a second; faster is kept as this.</summary>
    public const float MaxSpeed = 16f;

    private const byte FormatVersion = 2;

    // The first version, read still: no exact share follows a water byte of 255 (it is 1.275 of a voxel).
    private const byte FirstVersion = 1;

    private const float Full = 200f;

    // The water byte that means "more: the exact share follows".
    private const byte More = 255;

    /// <summary>The brick as bytes.</summary>
    /// <param name="brick">The brick.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    public static byte[] Encode(Brick brick, float voxelSize) => Packed.Write(writer =>
    {
        writer.Write(FormatVersion);
        writer.Write(brick.X);
        writer.Write(brick.Y);
        writer.Write(brick.Z);
        writer.Write(voxelSize);

        // A voxel's velocity is kept when its water byte is not 0: the reader goes by the same byte.
        byte[] water = WaterBytes(brick, out float[] exact);
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
                if (water[i] == More)
                {
                    writer.Write(exact[i]);
                }
            }
        }
    });

    /// <summary>Bytes a voxel in a brick's <see cref="Look"/>: its water, its solid share and its current on each axis.</summary>
    public const int LookBytes = 5;

    /// <summary>The water steps (of 200 a voxel) of the thinnest water a player's game draws, metres deep, in voxels this size.</summary>
    public static int Steps(float metres, float voxelSize) => (int)Math.Ceiling(metres / voxelSize * Full);

    /// <summary>
    /// The brick as a player sees it, <see cref="LookBytes"/> a voxel, rounded as <see cref="Encode"/> rounds them: what a
    /// send compares with the brick as last sent, to know whether the change would show (<see cref="Shows"/>) before
    /// packing it at all.
    /// </summary>
    public static byte[] Look(Brick brick, float voxelSize)
    {
        byte[] look = new byte[Brick.Count * LookBytes];
        float area = voxelSize * voxelSize;
        byte[] waters = WaterBytes(brick, out _);
        for (int i = 0; i < Brick.Count; i++)
        {
            byte water = waters[i];
            if (water == 0)
            {
                continue;
            }

            (float x, float y, float z) = Velocity(brick, i, area);
            int k = i * LookBytes;
            look[k] = water;
            look[k + 1] = (byte)Math.Round(Math.Min(1f, Math.Max(0f, brick.Solid[i])) * 255f);
            look[k + 2] = (byte)SpeedByte(x);
            look[k + 3] = (byte)SpeedByte(y);
            look[k + 4] = (byte)SpeedByte(z);
        }

        return look;
    }

    /// <summary>
    /// Whether a player would see the brick change from one <see cref="Look"/> to the next: a voxel's water over
    /// <paramref name="visibleSteps"/> (of 200 a voxel: what a player's game draws at all, fading in over it) changing
    /// by more than <paramref name="waterSteps"/>, its floor at all where it is drawn, or its current on an axis by more
    /// than <paramref name="speedSteps"/> (of 127, in square-root steps: finer when slow) where it is drawn. Traces too
    /// thin to draw come and go all along a running stream's edges and bed, and water a hair over or under the
    /// thinnest drawn barely shows: those do not count.
    /// </summary>
    public static bool Shows(byte[] was, byte[] now, int waterSteps, int speedSteps, int visibleSteps)
    {
        for (int k = 0; k < now.Length; k += LookBytes)
        {
            int a = Math.Max(0, was[k] - visibleSteps), b = Math.Max(0, now[k] - visibleSteps);
            if (a == 0 && b == 0)
            {
                continue;
            }

            if (Math.Abs(a - b) > waterSteps || was[k + 1] != now[k + 1])
            {
                return true;
            }

            for (int axis = 2; axis < LookBytes; axis++)
            {
                if (Math.Abs((sbyte)was[k + axis] - (sbyte)now[k + axis]) > speedSteps)
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>The brick kept by <see cref="Encode"/>.</summary>
    /// <exception cref="InvalidDataException">
    /// The bytes are not that (or of another version), or hold a voxel size or a share of water that is not a number,
    /// endless, or below 0.
    /// </exception>
    public static BrickView Decode(byte[] data) => Packed.Read(data, "brick data", reader =>
    {
        byte version = reader.ReadByte();
        if (version is not (FormatVersion or FirstVersion))
        {
            throw new InvalidDataException($"brick data version {version}, expected {FirstVersion} or {FormatVersion}");
        }

        var view = new BrickView(reader.ReadInt32(), reader.ReadInt32(), reader.ReadInt32(), reader.ReadSingle());
        if (!(view.VoxelSize > 0f) || float.IsInfinity(view.VoxelSize))
        {
            throw new InvalidDataException($"brick data voxel size {view.VoxelSize}");
        }

        byte[] water = reader.ReadBytes(Brick.Count);
        if (water.Length < Brick.Count)
        {
            throw new EndOfStreamException();
        }

        for (int i = 0; i < Brick.Count; i++)
        {
            view.Water[i] = water[i] / Full;
            if (water[i] > 0)
            {
                view.Solid[i] = reader.ReadByte() / 255f;
                view.VelocityX[i] = Speed(reader.ReadSByte());
                view.VelocityY[i] = Speed(reader.ReadSByte());
                view.VelocityZ[i] = Speed(reader.ReadSByte());
                if (water[i] == More && version >= FormatVersion)
                {
                    float share = reader.ReadSingle();
                    if (!(share >= 0f) || float.IsInfinity(share))
                    {
                        throw new InvalidDataException($"brick data water {share} in voxel {i}");
                    }

                    view.Water[i] = share;
                }
            }
        }

        return view;
    });

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

    // Each voxel's water in steps of 1/Full, what each rounds off carried on into the next so the brick's volume is
    // kept, and the exact share each was rounded from (kept as is where the byte says More).
    private static byte[] WaterBytes(Brick brick, out float[] exact)
    {
        byte[] water = new byte[Brick.Count];
        exact = new float[Brick.Count];
        float carried = 0f;
        for (int i = 0; i < Brick.Count; i++)
        {
            // A dry voxel takes nothing carried: water rounded off elsewhere does not wet it. Nor does one whose water is
            // not a number: it is kept as none, as it always was.
            if (!(brick.Water[i] > 0f) || float.IsInfinity(brick.Water[i]))
            {
                continue;
            }

            float share = brick.Water[i] + carried;
            water[i] = (byte)Math.Max(0, Math.Min(More, (int)Math.Round(share * Full)));
            exact[i] = share;
            carried = water[i] == More ? 0f : share - (water[i] / Full);
        }

        return water;
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
