using System;
using System.IO;
using UnityEngine;

namespace FlowingWater;

/// <summary>
/// One zone's water: 64 by 64 cells of one metre, each sampled at a vertex of the zone's heightmap
/// (cell x, z sits at world x = zone.x * 64 - 32 + x). Depth is the water column over the ground;
/// the velocity is the water's mean horizontal speed in that cell, metres per second.
/// </summary>
internal sealed class TileData
{
    /// <summary>Cells along one side of a zone.</summary>
    public const int Size = 64;

    /// <summary>Cells in a zone.</summary>
    public const int Cells = Size * Size;

    /// <summary>Vertices along one side of a zone's heightmap (one more than the cells).</summary>
    public const int VertexPitch = Size + 1;

    private const byte FormatVersion = 1;

    private const float DepthUnit = 0.01f;

    private const float SpeedUnit = 0.05f;

    public TileData(Vector2s zone)
    {
        Zone = zone;
    }

    public Vector2s Zone { get; }

    /// <summary>Ground height per cell, metres.</summary>
    public float[] Height { get; } = new float[Cells];

    /// <summary>Water depth per cell, metres; 0 is dry.</summary>
    public float[] Depth { get; } = new float[Cells];

    public float[] VelX { get; } = new float[Cells];

    public float[] VelZ { get; } = new float[Cells];

    public static int Index(int x, int z)
    {
        return (z * Size) + x;
    }

    public static long Key(Vector2s zone)
    {
        return ((long)zone.x << 32) | (zone.y & 0xFFFFFFFFL);
    }

    public static float WorldX(Vector2s zone, int x)
    {
        return (zone.x * ZoneSystem.c_ZoneSize) - ZoneSystem.c_ZoneSizeHalf + x;
    }

    public static float WorldZ(Vector2s zone, int z)
    {
        return (zone.y * ZoneSystem.c_ZoneSize) - ZoneSystem.c_ZoneSizeHalf + z;
    }

    /// <summary>The zone and cell a world point falls in.</summary>
    public static Vector2s CellOf(Vector3 point, out int x, out int z)
    {
        Vector2s zone = ZoneSystem.GetZone(point);
        x = Mathf.Clamp(Mathf.FloorToInt(point.x - WorldX(zone, 0)), 0, Size - 1);
        z = Mathf.Clamp(Mathf.FloorToInt(point.z - WorldZ(zone, 0)), 0, Size - 1);
        return zone;
    }

    /// <summary>Depths and velocities as the bytes kept in the tile's ZDO (gzip of centimetres and 5 cm/s steps).</summary>
    public byte[] Encode()
    {
        var pkg = new ZPackage();
        pkg.Write(FormatVersion);
        pkg.Write(Cells);
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

    /// <summary>Reads what <see cref="Encode"/> wrote; false (and a logged warning) when the bytes are not that.</summary>
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
        int cells = pkg.ReadInt();
        if (version != FormatVersion || cells != Cells)
        {
            FlowingWaterPlugin.Log.LogWarning($"Water tile {Zone}: data version {version} with {cells} cells, expected {FormatVersion} with {Cells}");
            return false;
        }

        if (pkg.Size() < 5 + (Cells * 4))
        {
            FlowingWaterPlugin.Log.LogWarning($"Water tile {Zone}: data truncated ({pkg.Size()} bytes)");
            return false;
        }

        for (int i = 0; i < Cells; i++)
        {
            Depth[i] = pkg.ReadShort() * DepthUnit;
        }

        for (int i = 0; i < Cells; i++)
        {
            VelX[i] = pkg.ReadSByte() * SpeedUnit;
        }

        for (int i = 0; i < Cells; i++)
        {
            VelZ[i] = pkg.ReadSByte() * SpeedUnit;
        }

        return true;
    }

    private static sbyte SpeedByte(float speed)
    {
        return (sbyte)Mathf.Clamp(Mathf.RoundToInt(speed / SpeedUnit), -127, 127);
    }
}
