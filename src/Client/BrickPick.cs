using System;
using System.Collections.Generic;
using FlowingWater.Volume;

namespace FlowingWater.Client;

/// <summary>Which brick of 3D water the console's debug describes at a point, without the game.</summary>
internal static class BrickPick
{
    /// <summary>
    /// The brick holding the point, else the nearest over or under it in the column there; the first of those as near.
    /// </summary>
    /// <param name="bricks">The bricks, each with its data.</param>
    /// <param name="view">A brick's data.</param>
    /// <param name="x">The point, east (metres).</param>
    /// <param name="y">Up.</param>
    /// <param name="z">North.</param>
    /// <param name="off">How far the point is from the brick, metres (0 inside it).</param>
    /// <returns>The brick; null when none is in the column.</returns>
    public static T Nearest<T>(IEnumerable<T> bricks, Func<T, BrickView> view, float x, float y, float z, out float off)
        where T : class
    {
        T found = null;
        off = float.MaxValue;
        foreach (T brick in bricks)
        {
            BrickView data = view(brick);
            float span = Brick.Size * data.VoxelSize;
            float lowX = data.X * span;
            float lowY = data.Y * span;
            float lowZ = data.Z * span;
            if (x < lowX || x >= lowX + span || z < lowZ || z >= lowZ + span)
            {
                continue;
            }

            float distance = y < lowY ? lowY - y : Math.Max(0f, y - (lowY + span));
            if (distance < off)
            {
                off = distance;
                found = brick;
            }
        }

        return found;
    }
}
