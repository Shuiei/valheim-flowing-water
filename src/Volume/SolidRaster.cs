using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// What is solid in a brick, without the game: the ground, filling each voxel from its bottom up to the ground's
/// height at the middle of its column, and building pieces, as boxes that fill the voxels whose middles they hold
/// and close the faces between voxels whose middles they separate (so a wall thinner than a voxel still holds water).
/// </summary>
internal static class SolidRaster
{
    /// <summary>Fills the brick's voxels up to the ground.</summary>
    /// <param name="brick">The brick.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    /// <param name="groundAt">The ground's height at a point (world x, z), metres.</param>
    public static void Ground(Brick brick, float voxelSize, Func<float, float, float> groundAt)
    {
        for (int z = 0; z < Brick.Size; z++)
        {
            for (int x = 0; x < Brick.Size; x++)
            {
                float ground = groundAt(((brick.X * Brick.Size) + x + 0.5f) * voxelSize, ((brick.Z * Brick.Size) + z + 0.5f) * voxelSize);
                for (int y = 0; y < Brick.Size; y++)
                {
                    float bottom = ((brick.Y * Brick.Size) + y) * voxelSize;
                    int i = Brick.Index(x, y, z);
                    brick.Solid[i] = Math.Max(brick.Solid[i], Math.Min(1f, Math.Max(0f, (ground - bottom) / voxelSize)));
                }
            }
        }
    }

    /// <summary>Fills the voxels the boxes hold and closes the faces they cross.</summary>
    /// <param name="brick">The brick.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    /// <param name="boxes">The pieces' boxes near the brick.</param>
    public static void Pieces(Brick brick, float voxelSize, IEnumerable<SolidBox> boxes)
    {
        foreach (SolidBox box in boxes)
        {
            (Vec3 min, Vec3 max) = box.Bounds();

            // The voxels whose middles, or the segments from them to the next middles east, up or north, may meet it.
            if (!Range(min.X, max.X, brick.X, voxelSize, out int x0, out int x1)
                || !Range(min.Y, max.Y, brick.Y, voxelSize, out int y0, out int y1)
                || !Range(min.Z, max.Z, brick.Z, voxelSize, out int z0, out int z1))
            {
                continue;
            }

            for (int y = y0; y <= y1; y++)
            {
                for (int z = z0; z <= z1; z++)
                {
                    for (int x = x0; x <= x1; x++)
                    {
                        Mark(brick, voxelSize, box, x, y, z);
                    }
                }
            }
        }
    }

    private static void Mark(Brick brick, float s, SolidBox box, int x, int y, int z)
    {
        int i = Brick.Index(x, y, z);
        var middle = new Vec3(((brick.X * Brick.Size) + x + 0.5f) * s, ((brick.Y * Brick.Size) + y + 0.5f) * s, ((brick.Z * Brick.Size) + z + 0.5f) * s);
        if (box.Contains(middle))
        {
            brick.Solid[i] = 1f;
        }

        if (box.Crosses(middle, middle + new Vec3(s, 0f, 0f)))
        {
            brick.Closed[i] |= Brick.ClosedX;
        }

        if (box.Crosses(middle, middle + new Vec3(0f, s, 0f)))
        {
            brick.Closed[i] |= Brick.ClosedY;
        }

        if (box.Crosses(middle, middle + new Vec3(0f, 0f, s)))
        {
            brick.Closed[i] |= Brick.ClosedZ;
        }
    }

    // The brick's voxels along one axis from which a box spanning min..max may be met (a voxel's segment reaches
    // the next middle, a voxel further): false when none.
    private static bool Range(float min, float max, int brick, float s, out int from, out int to)
    {
        int first = brick * Brick.Size;
        from = Math.Max(first, (int)Math.Floor((min / s) - 0.5f) - 1) - first;
        to = Math.Min(first + Brick.Size - 1, (int)Math.Floor((max / s) - 0.5f) + 1) - first;
        return from <= to;
    }
}
