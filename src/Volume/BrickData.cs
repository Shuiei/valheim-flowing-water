using System;
using System.IO;

namespace FlowingWater.Volume;

/// <summary>
/// A brick's water as the world keeps it and players' games receive it, without the game: gzip of a version, the
/// brick's coordinates and voxel size, then per voxel its water (a byte, 200 a full voxel, a little more for water
/// pressed in) and, for voxels with water, its solid share (a byte, 255 all solid: where the water's floor is, which
/// the players' games cannot know) and its velocity (a byte per axis, square-root steps to 16 m/s). From version 2, a
/// water byte of 255 means more than the byte holds (water pressed in hard, or poured into smaller voxels): its exact
/// share follows the velocity, as a float, so none is lost. From version 3, a byte of the faces it could leave by
/// follows (<see cref="Brick.OpenFaces"/>: where its water meets the air, which the players' games cannot know either,
/// as they cannot know the pieces). What a voxel's byte rounds off is carried on to the next voxel with water, so the
/// brick keeps its volume to within a byte's step, however thin its water.
/// </summary>
internal static class BrickData
{
    /// <summary>The fastest speed kept, metres a second; faster is kept as this.</summary>
    public const float MaxSpeed = 16f;

    private const byte FormatVersion = 3;

    // The first version, read still: no exact share follows a water byte of 255 (it is 1.275 of a voxel).
    private const byte FirstVersion = 1;

    // The version before the open faces, read still: every face taken as open.
    private const byte ExactVersion = 2;

    /// <summary>Every face open (a brick of a version that did not say).</summary>
    public const byte AllOpen = 63;

    /// <summary>
    /// With a voxel's open faces, the bit set where the voxel under it holds ground (<see cref="Brick.OpenFaces"/>): the
    /// players' games know the ground only under water, and water hanging over a voxel that holds some runs down a
    /// slope, it does not fall. Never set by a server before it (nor read as a face by a game before it).
    /// </summary>
    public const byte GroundUnder = 64;

    /// <summary>The solid share from which a voxel holds ground (<see cref="GroundUnder"/>).</summary>
    public const float HoldsGround = 0.02f;

    /// <summary>Water steps a full voxel is kept as (a byte holds a little more, for water pressed in).</summary>
    public const float Full = 200f;

    // The water byte that means "more: the exact share follows".
    private const byte More = 255;

    /// <summary>The brick as bytes.</summary>
    /// <param name="brick">The brick.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    public static byte[] Encode(Brick brick, float voxelSize) => Encode(brick, voxelSize, Look(brick, voxelSize));

    /// <summary>
    /// The brick as bytes, from its <see cref="Look"/> taken just now (what a send weighs the change by: the bytes it
    /// holds are the ones kept, so they are not worked out twice).
    /// </summary>
    /// <param name="brick">The brick, as it was when the look was taken.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    /// <param name="look">Its look, <see cref="Look"/>.</param>
    public static byte[] Encode(Brick brick, float voxelSize, byte[] look) => Packed.Write(writer =>
    {
        writer.Write(FormatVersion);
        writer.Write(brick.X);
        writer.Write(brick.Y);
        writer.Write(brick.Z);
        writer.Write(voxelSize);

        // Every voxel's water byte first, then, for each with water, its look's other bytes and, where the byte says
        // More, its exact share (worked out again only then: a look has none).
        byte[] water = _encodeWater ??= new byte[Brick.Count];
        Array.Clear(water, 0, Brick.Count);
        bool more = false;
        for (int k = 0; k < look.Length; k += LookBytes)
        {
            water[Voxel(look, k)] = look[k + WaterByte];
            more |= look[k + WaterByte] == More;
        }

        writer.Write(water, 0, Brick.Count);
        float[] exact = more ? Exact(brick) : null;
        for (int k = 0; k < look.Length; k += LookBytes)
        {
            writer.Write(look, k + SolidByte, LookBytes - SolidByte - 1);
            if (look[k + WaterByte] == More)
            {
                writer.Write(exact[Voxel(look, k)]);
            }

            writer.Write(look[k + OpenByte]);
        }
    });

    /// <summary>
    /// Bytes a voxel takes in a brick's <see cref="Look"/>: its index (two bytes), its water, its solid share, its
    /// current on each axis and its open faces.
    /// </summary>
    public const int LookBytes = 8;

    /// <summary>Where a voxel's water byte is in its <see cref="LookBytes"/>, after its index.</summary>
    public const int WaterByte = 2;

    /// <summary>Where a voxel's solid byte is in its <see cref="LookBytes"/>.</summary>
    public const int SolidByte = 3;

    /// <summary>Where a voxel's current bytes are in its <see cref="LookBytes"/>: east, up, north.</summary>
    public const int SpeedByte0 = 4;

    /// <summary>Where a voxel's open faces byte is in its <see cref="LookBytes"/>.</summary>
    public const int OpenByte = 7;

    // Scratch for the water bytes, the shares they were rounded from and the bytes an Encode gathers: one of each a
    // thread (bricks are packed on the main thread, but nothing here need know that), made once.
    [ThreadStatic]
    private static byte[] _waterScratch;

    [ThreadStatic]
    private static float[] _exactScratch;

    [ThreadStatic]
    private static byte[] _encodeWater;

    /// <summary>The water steps (of 200 a voxel) of the thinnest water a player's game draws, metres deep, in voxels this size.</summary>
    public static int Steps(float metres, float voxelSize) => (int)Math.Ceiling(metres / voxelSize * Full);

    /// <summary>
    /// The brick as a player sees it, <see cref="LookBytes"/> for each voxel with water (in index order, no bytes for
    /// the dry ones), rounded as <see cref="Encode(Brick, float)"/> rounds them: what a send compares with the brick as last sent, to
    /// know whether the change would show (<see cref="Shows"/>) before packing it at all, and what it is then packed
    /// from.
    /// </summary>
    public static byte[] Look(Brick brick, float voxelSize)
    {
        byte[] waters = WaterBytes(brick, out _);
        int wet = 0;
        for (int i = 0; i < Brick.Count; i++)
        {
            wet += waters[i] > 0 ? 1 : 0;
        }

        byte[] look = new byte[wet * LookBytes];
        float area = voxelSize * voxelSize;
        int k = 0;
        for (int i = 0; i < Brick.Count; i++)
        {
            byte water = waters[i];
            if (water == 0)
            {
                continue;
            }

            (float x, float y, float z) = Velocity(brick, i, area);
            look[k] = (byte)i;
            look[k + 1] = (byte)(i >> 8);
            look[k + WaterByte] = water;
            look[k + SolidByte] = (byte)Math.Round(Math.Min(1f, Math.Max(0f, brick.Solid[i])) * 255f);
            look[k + SpeedByte0] = (byte)SpeedByte(x);
            look[k + SpeedByte0 + 1] = (byte)SpeedByte(y);
            look[k + SpeedByte0 + 2] = (byte)SpeedByte(z);
            look[k + OpenByte] = brick.OpenFaces(i);
            k += LookBytes;
        }

        return look;
    }

    /// <summary>The voxel the record at byte k of a <see cref="Look"/> is of.</summary>
    public static int Voxel(byte[] look, int k) => look[k] | (look[k + 1] << 8);

    /// <summary>
    /// Whether a player would see the brick change from one <see cref="Look"/> to the next: a voxel's water over
    /// <paramref name="visibleSteps"/> (of 200 a voxel: what a player's game draws at all, fading in over it) changing
    /// by more than <paramref name="waterSteps"/>, its floor or its open faces at all where it is drawn, or its current on
    /// an axis by more than <paramref name="speedSteps"/> (of 127, in square-root steps: finer when slow) where it is
    /// drawn. Traces too
    /// thin to draw come and go all along a running stream's edges and bed, and water a hair over or under the
    /// thinnest drawn barely shows: those do not count. A voxel in one look and not the other reads as dry there.
    /// </summary>
    public static bool Shows(byte[] was, byte[] now, int waterSteps, int speedSteps, int visibleSteps)
    {
        int a = 0, b = 0;
        while (a < was.Length || b < now.Length)
        {
            int ia = a < was.Length ? Voxel(was, a) : int.MaxValue;
            int ib = b < now.Length ? Voxel(now, b) : int.MaxValue;
            int ka = ia <= ib ? a : -1, kb = ib <= ia ? b : -1;
            if (Differs(was, ka, now, kb, waterSteps, speedSteps, visibleSteps))
            {
                return true;
            }

            a += ia <= ib ? LookBytes : 0;
            b += ib <= ia ? LookBytes : 0;
        }

        return false;
    }

    // Whether one voxel's record (k; none, all bytes 0, when -1) differs from the other's so that it shows.
    private static bool Differs(byte[] was, int ka, byte[] now, int kb, int waterSteps, int speedSteps, int visibleSteps)
    {
        int a = Math.Max(0, At(was, ka, WaterByte) - visibleSteps), b = Math.Max(0, At(now, kb, WaterByte) - visibleSteps);
        if (a == 0 && b == 0)
        {
            return false;
        }

        if (Math.Abs(a - b) > waterSteps || At(was, ka, SolidByte) != At(now, kb, SolidByte) || At(was, ka, OpenByte) != At(now, kb, OpenByte))
        {
            return true;
        }

        for (int axis = 0; axis < 3; axis++)
        {
            if (Math.Abs((sbyte)At(was, ka, SpeedByte0 + axis) - (sbyte)At(now, kb, SpeedByte0 + axis)) > speedSteps)
            {
                return true;
            }
        }

        return false;
    }

    private static byte At(byte[] look, int k, int field) => k < 0 ? (byte)0 : look[k + field];

    /// <summary>The brick kept by <see cref="Encode(Brick, float)"/>.</summary>
    /// <exception cref="InvalidDataException">
    /// The bytes are not that (or of another version), or hold a voxel size or a share of water that is not a number,
    /// endless, or below 0.
    /// </exception>
    public static BrickView Decode(byte[] data) => Packed.Read(data, "brick data", reader =>
    {
        byte version = reader.ReadByte();
        if (version is not (FormatVersion or ExactVersion or FirstVersion))
        {
            throw new InvalidDataException($"brick data version {version}, expected {FirstVersion} to {FormatVersion}");
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
                if (water[i] == More && version >= ExactVersion)
                {
                    float share = reader.ReadSingle();
                    if (!(share >= 0f) || float.IsInfinity(share))
                    {
                        throw new InvalidDataException($"brick data water {share} in voxel {i}");
                    }

                    view.Water[i] = share;
                }

                view.Open[i] = version >= FormatVersion ? reader.ReadByte() : AllOpen;
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
        float west = brick.Neighbour(i, 1, out Brick other, out int j) ? other.FlowX[j] : 0f;
        float under = brick.Neighbour(i, 3, out other, out j) ? other.FlowY[j] : 0f;
        float south = brick.Neighbour(i, 5, out other, out j) ? other.FlowZ[j] : 0f;
        float half = 0.5f / faceArea;
        return ((brick.FlowX[i] + west) * half, (brick.FlowY[i] + under) * half, (brick.FlowZ[i] + south) * half);
    }

    // Each voxel's water in steps of 1/Full, what each rounds off carried on into the next so the brick's volume is
    // kept, and the exact share each was rounded from (kept as is where the byte says More). Both in scratch arrays
    // of the thread's, read before it is asked again.
    private static byte[] WaterBytes(Brick brick, out float[] exact)
    {
        byte[] water = _waterScratch ??= new byte[Brick.Count];
        exact = _exactScratch ??= new float[Brick.Count];
        Array.Clear(water, 0, Brick.Count);
        float carried = 0f;
        for (int i = 0; i < Brick.Count; i++)
        {
            // A dry voxel takes nothing carried: water rounded off elsewhere does not wet it. Nor does one whose water is
            // not a number: it is kept as none, as it always was.
            if (!(brick.Water[i] > 0f) || float.IsInfinity(brick.Water[i]))
            {
                exact[i] = 0f;
                continue;
            }

            float share = brick.Water[i] + carried;
            water[i] = (byte)Math.Min(More, Math.Round(share * Full));
            exact[i] = share;
            carried = water[i] == More ? 0f : share - (water[i] / Full);
        }

        return water;
    }

    // The exact share of each voxel's water, as WaterBytes finds them.
    private static float[] Exact(Brick brick)
    {
        WaterBytes(brick, out float[] exact);
        return exact;
    }

    private static sbyte SpeedByte(float speed)
    {
        if (float.IsNaN(speed))
        {
            return 0;
        }

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

    /// <summary>Each voxel with water's faces it could leave by (<see cref="Brick.OpenFaces"/>); 0 where it has none.</summary>
    public byte[] Open { get; } = new byte[Brick.Count];
}
