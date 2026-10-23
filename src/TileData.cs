using UnityEngine;

namespace FlowingWater;

/// <summary>
/// The zone grid the water works on: a zone's key, where it starts, the cell a point falls in, and the voxel sizes the
/// water may have (CellSize). It was a zone's column water before the water moved into voxels (ColumnTile reads
/// what is left of that).
/// </summary>
internal static class TileData
{
    /// <summary>Metres along one side of a zone.</summary>
    public const int ZoneMetres = 64;

    /// <summary>The cell sizes the grid may have, metres.</summary>
    public static readonly float[] CellSizes = [1f, 0.5f, 0.25f];

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
}
