using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The 3D water this player's game knows (every brick received), for swimming, floating, the current and the
/// console: the surface a point is in or just over, and the velocity there.
/// </summary>
internal static class VoxelWater
{
    /// <summary>What <see cref="SurfaceAt"/> gives where there is no water: far under any ground.</summary>
    public const float NoWater = -10000f;

    /// <summary>The bricks known.</summary>
    public static VoxelField Field { get; } = new();

    /// <summary>The surface of the water a point is in or just over; <see cref="NoWater"/> where there is none.</summary>
    public static float SurfaceAt(Vector3 point)
    {
        float surface = Field.SurfaceAt(point.x, point.y, point.z);
        return float.IsNegativeInfinity(surface) ? NoWater : surface;
    }

    /// <summary>The water's velocity along the ground at a point in it; false where there is none or it is still.</summary>
    public static bool CurrentAt(Vector3 point, out Vector3 current)
    {
        current = Vector3.zero;
        if (!Field.VelocityAt(point.x, point.y, point.z, out float x, out _, out float z))
        {
            return false;
        }

        current = new Vector3(x, 0f, z);
        return current.sqrMagnitude > 0.0025f;
    }

    /// <summary>What water is at a point, for the console.</summary>
    public static string Describe(Vector3 point)
    {
        float surface = SurfaceAt(point);
        if (surface <= NoWater)
        {
            return System.FormattableString.Invariant($"no water here ({Field.Bricks.Count} bricks of water known)");
        }

        CurrentAt(point, out Vector3 current);
        return System.FormattableString.Invariant($"water surface at {surface:0.00} m ({surface - point.y:0.00} m over you), current {current.magnitude:0.00} m/s; {Field.Bricks.Count} bricks of {Field.VoxelSize:0.##} m voxels known");
    }
}
