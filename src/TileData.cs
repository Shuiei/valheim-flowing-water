using System;
using System.IO;
using UnityEngine;

namespace FlowingWater;

/// <summary>
/// One zone's water: a square grid of cells (64 per side for one-metre cells, 128 for half a metre,
/// 256 for a quarter), cell x, z sampled at world x = zone.x * 64 - 32 + x * cell size. Depth is the
/// water column over the ground; the velocity is the water's mean horizontal speed in that cell,
/// metres per second. The data carries its own grid, so a player draws whatever the server sends.
/// </summary>
internal sealed class TileData
{
    /// <summary>Metres along one side of a zone.</summary>
    public const int ZoneMetres = 64;

    /// <summary>The cell sizes the grid may have, metres.</summary>
    public static readonly float[] CellSizes = [1f, 0.5f, 0.25f];

    // 1: 64 x 64 cells, speeds in 5 cm/s steps. 2: any grid, speeds on a square-root scale to 16 m/s.
    private const byte FormatVersion = 2;

    private const byte FirstFormat = 1;

    private const float DepthUnit = 0.01f;

    private const float FirstSpeedUnit = 0.05f;

    // Fine steps for slow water, where the eye sees them, coarser for rapids and falls.
    private const float MaxSpeed = 16f;

    public TileData(Vector2s zone, int size = ZoneMetres)
    {
        Zone = zone;
        Allocate(size);
    }

    public Vector2s Zone { get; }

    /// <summary>Cells along one side.</summary>
    public int Size { get; private set; }

    public int Cells => Size * Size;

    /// <summary>Vertices along one side of a mesh over the cells (one more than the cells).</summary>
    public int VertexPitch => Size + 1;

    /// <summary>Metres along one side of a cell.</summary>
    public float CellSize => (float)ZoneMetres / Size;

    /// <summary>Ground height per cell, metres.</summary>
    public float[] Height { get; private set; }

    /// <summary>Water depth per cell, metres; 0 is dry.</summary>
    public float[] Depth { get; private set; }

    public float[] VelX { get; private set; }

    public float[] VelZ { get; private set; }

    /// <summary>Cells per side for a cell size, metres.</summary>
    public static int SizeFor(float cellSize)
    {
        return Mathf.RoundToInt(ZoneMetres / cellSize);
    }

    public static long Key(Vector2s zone)
    {
        return ((long)zone.x << 32) | (zone.y & 0xFFFFFFFFL);
    }

    /// <summary>World x of the zone's west edge, where cell 0 sits.</summary>
    public static float OriginX(Vector2s zone)
    {
        return (zone.x * ZoneSystem.c_ZoneSize) - ZoneSystem.c_ZoneSizeHalf;
    }

    /// <summary>World z of the zone's south edge, where cell 0 sits.</summary>
    public static float OriginZ(Vector2s zone)
    {
        return (zone.y * ZoneSystem.c_ZoneSize) - ZoneSystem.c_ZoneSizeHalf;
    }

    /// <summary>The zone and cell a world point falls in, on a grid of this many cells per side.</summary>
    public static Vector2s CellOf(Vector3 point, int size, out int x, out int z)
    {
        Vector2s zone = ZoneSystem.GetZone(point);
        float cell = (float)ZoneMetres / size;
        x = Mathf.Clamp(Mathf.FloorToInt((point.x - OriginX(zone)) / cell), 0, size - 1);
        z = Mathf.Clamp(Mathf.FloorToInt((point.z - OriginZ(zone)) / cell), 0, size - 1);
        return zone;
    }

    public int Index(int x, int z)
    {
        return (z * Size) + x;
    }

    public float WorldX(int x)
    {
        return OriginX(Zone) + (x * CellSize);
    }

    public float WorldZ(int z)
    {
        return OriginZ(Zone) + (z * CellSize);
    }

    /// <summary>
    /// Changes the grid, keeping the water: a finer cell takes the depth and speed of the coarse cell
    /// it lies in, a coarser cell the mean of the fine cells in it, so the volume stays the same.
    /// Heights are not carried over (they are sampled again from the ground).
    /// </summary>
    public void Resample(int size)
    {
        if (size == Size)
        {
            return;
        }

        int oldSize = Size;
        float[] depth = Depth;
        float[] velX = VelX;
        float[] velZ = VelZ;
        Allocate(size);
        if (size > oldSize)
        {
            int ratio = size / oldSize;
            for (int z = 0; z < size; z++)
            {
                for (int x = 0; x < size; x++)
                {
                    int from = ((z / ratio) * oldSize) + (x / ratio);
                    int i = Index(x, z);
                    Depth[i] = depth[from];
                    VelX[i] = velX[from];
                    VelZ[i] = velZ[from];
                }
            }

            return;
        }

        int merge = oldSize / size;
        float share = 1f / (merge * merge);
        for (int z = 0; z < oldSize; z++)
        {
            for (int x = 0; x < oldSize; x++)
            {
                int from = (z * oldSize) + x;
                int i = Index(x / merge, z / merge);
                Depth[i] += depth[from] * share;
                VelX[i] += velX[from] * share;
                VelZ[i] += velZ[from] * share;
            }
        }
    }

    /// <summary>The grid, depths and velocities as the bytes kept in the tile's ZDO (gzip of centimetres and speed steps).</summary>
    public byte[] Encode()
    {
        var pkg = new ZPackage();
        pkg.Write(FormatVersion);
        pkg.Write(Size);
        for (int i = 0; i < Cells; i++)
        {
            pkg.Write((short)Mathf.Clamp(Mathf.RoundToInt(Depth[i] / DepthUnit), 0, short.MaxValue));
        }

        for (int i = 0; i < Cells; i++)
        {
            pkg.Write(SpeedByte(VelX[i]));
        }

        for (int i = 0; i < Cells; i++)
        {
            pkg.Write(SpeedByte(VelZ[i]));
        }

        return Utils.Compress(pkg.GetArray());
    }

    /// <summary>
    /// Reads what <see cref="Encode"/> wrote, or the first format (64 by 64 cells, before the grid
    /// could change), taking on the grid it holds; false (and a logged warning) when the bytes are
    /// not that.
    /// </summary>
    public bool Decode(byte[] data)
    {
        ZPackage pkg;
        try
        {
            pkg = new ZPackage(Utils.Decompress(data));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
        {
            FlowingWaterPlugin.Log.LogWarning($"Water tile {Zone}: data unreadable ({e.Message})");
            return false;
        }

        if (pkg.Size() < 5)
        {
            FlowingWaterPlugin.Log.LogWarning($"Water tile {Zone}: data too short ({pkg.Size()} bytes)");
            return false;
        }

        byte version = pkg.ReadByte();
        int number = pkg.ReadInt();

        // The first format wrote the cell count, the second the cells per side.
        int size = version == FirstFormat ? Mathf.RoundToInt(Mathf.Sqrt(number)) : number;
        if ((version != FormatVersion && version != FirstFormat) || Array.IndexOf(CellSizes, (float)ZoneMetres / size) < 0 || (version == FirstFormat && size != ZoneMetres))
        {
            FlowingWaterPlugin.Log.LogWarning($"Water tile {Zone}: data version {version} with grid {number}, expected version {FormatVersion} with 64, 128 or 256 cells per side");
            return false;
        }

        if (pkg.Size() < 5 + (size * size * 4))
        {
            FlowingWaterPlugin.Log.LogWarning($"Water tile {Zone}: data truncated ({pkg.Size()} bytes)");
            return false;
        }

        if (size != Size)
        {
            Allocate(size);
        }

        for (int i = 0; i < Cells; i++)
        {
            Depth[i] = pkg.ReadShort() * DepthUnit;
        }

        for (int i = 0; i < Cells; i++)
        {
            VelX[i] = Speed(pkg.ReadSByte(), version);
        }

        for (int i = 0; i < Cells; i++)
        {
            VelZ[i] = Speed(pkg.ReadSByte(), version);
        }

        return true;
    }

    private static sbyte SpeedByte(float speed)
    {
        float step = Mathf.Sqrt(Mathf.Min(Mathf.Abs(speed), MaxSpeed) / MaxSpeed) * 127f;
        return (sbyte)(Mathf.RoundToInt(step) * Math.Sign(speed));
    }

    private static float Speed(sbyte value, byte version)
    {
        if (version == FirstFormat)
        {
            return value * FirstSpeedUnit;
        }

        float share = value / 127f;
        return share * Mathf.Abs(share) * MaxSpeed;
    }

    private void Allocate(int size)
    {
        Size = size;
        Height = new float[Cells];
        Depth = new float[Cells];
        VelX = new float[Cells];
        VelZ = new float[Cells];
    }
}
