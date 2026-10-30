using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// The voxels a facing source's outlet spans in the volume water (FlowingWaterVolume's InSource): across it, as wide as
/// it is and two cells at least; a cell along its way, about its marker; up from its foot as high as the server pours
/// it (<see cref="Lift"/>), or twice the slot that passes its rate at its speed, as Regulate may raise it. The player's
/// game opens them in the card's solids: an outlet stands in the open over what its marker stands on, and a rock's
/// curved top, counted by its height at a column's middle, filled them. Without the game, so that it is tested.
/// </summary>
internal static class OutletVoxels
{
    /// <summary>How far over its marker's foot a source pours (VoxelServer's PourPoint), metres.</summary>
    public const float Lift = 0.3f;

    /// <summary>The voxels (world voxel coordinates) of an outlet, its marker's foot at a point, for voxels and cells of these sizes.</summary>
    public static HashSet<(int X, int Y, int Z)> Of(Vec3 foot, SourceShape shape, float rate, float voxel, float cell)
    {
        var voxels = new HashSet<(int X, int Y, int Z)>();
        float step = Math.Min(voxel, cell);
        (float rightX, float rightZ) = shape.Right;
        float across = Math.Max(shape.OutletWidth, 2f * cell), speed = shape.Speed(rate);
        float slot = speed > 0f ? rate / (across * speed) : SourceShape.Nozzle;
        float high = Math.Max(Lift, 2f * slot);
        int low = (int)Math.Floor((foot.Y + 0.05f) / voxel), top = (int)Math.Floor((foot.Y + high) / voxel);
        for (float a = -0.5f * across; a <= (0.5f * across) + (0.5f * step); a += step)
        {
            for (float f = -0.5f * cell; f <= (0.5f * cell) + (0.5f * step); f += step)
            {
                // Out in front of it, along its way: to the left of its right.
                int x = (int)Math.Floor((foot.X + (rightX * a) - (rightZ * f)) / voxel), z = (int)Math.Floor((foot.Z + (rightZ * a) + (rightX * f)) / voxel);
                for (int y = low; y <= top; y++)
                {
                    voxels.Add((x, y, z));
                }
            }
        }

        return voxels;
    }
}
