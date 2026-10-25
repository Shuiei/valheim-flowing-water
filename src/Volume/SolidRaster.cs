using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// What is solid in a brick, without the game: the ground, filling each voxel from its bottom up to the ground's
/// height at the middle of its column, and building pieces, as boxes that fill a voxel from its bottom up to where
/// they end on the voxel's middle line (a thin floor puts the water's floor at its top) and close the faces between
/// voxels whose middles they separate (so a wall thinner than a voxel still holds water).
/// <para>
/// A piece only in a voxel's upper half is its ceiling: the voxel's water is the water under it, and the water on it
/// is in the voxel above. Two voxels side by side pass water only at a height where both have room, between the
/// higher of their floors and the lower of their ceilings: none, and the face between them is closed (a sloping plank
/// is the ceiling of one voxel and the floor of the next: the water on it does not run under it).
/// </para>
/// </summary>
internal static class SolidRaster
{
    // Voxels along an axis of a brick and the layer past its far edge (a face there is tested by that layer's share).
    private const int Edge = Brick.Size + 1;

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
                    int i = Brick.Index(x, y, z);
                    brick.Solid[i] = Math.Max(brick.Solid[i], GroundShare(ground, (brick.Y * Brick.Size) + y, voxelSize));
                }
            }
        }
    }

    /// <summary>
    /// Fills the voxels the boxes stand in from below, then closes the faces they cross: each tested at the middle of
    /// the room left above the solid (a wall standing on a floor closes the water's way over that floor, the floor
    /// itself does not close the voxel it lies in).
    /// </summary>
    /// <param name="brick">The brick.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    /// <param name="boxes">The pieces' boxes near the brick.</param>
    /// <param name="groundAt">The ground's height at a point (world x, z), metres, for the voxels just past the brick's
    /// edges (whose share a face there is tested by); none: no ground there.</param>
    public static void Pieces(Brick brick, float voxelSize, IReadOnlyList<SolidBox> boxes, Func<float, float, float> groundAt = null)
    {
        // Each voxel's ceiling: the lowest underside of a piece over its upper half on its middle line, a share of the
        // voxel from its bottom (1: none).
        float[] ceiling = new float[Brick.Count];
        for (int i = 0; i < ceiling.Length; i++)
        {
            ceiling[i] = 1f;
        }

        foreach (SolidBox box in boxes)
        {
            Over(brick, voxelSize, box, (x, y, z) => Fill(brick, voxelSize, box, x, y, z, ceiling));
        }

        // A voxel's room, from its floor to its ceiling: from the brick, or worked out from the ground and the boxes
        // past its edges (the brick there may not exist yet), once each: every box near the edge asks for it.
        var beyond = new (float Floor, float Ceiling)?[Edge * Edge * Edge];
        (float Floor, float Ceiling) Room(int x, int y, int z)
        {
            if (x < Brick.Size && y < Brick.Size && z < Brick.Size)
            {
                int i = Brick.Index(x, y, z);
                return (brick.Solid[i], ceiling[i]);
            }

            int k = (((y * Edge) + z) * Edge) + x;
            beyond[k] ??= Beyond(brick, voxelSize, boxes, groundAt, x, y, z);
            return beyond[k].Value;
        }

        foreach (SolidBox box in boxes)
        {
            Over(brick, voxelSize, box, (x, y, z) => Close(brick, voxelSize, box, x, y, z, Room));
        }

        for (int i = 0; i < ceiling.Length; i++)
        {
            if (ceiling[i] < 1f)
            {
                brick.Ceiling[i] = brick.Ceiling[i] > 0f ? Math.Min(brick.Ceiling[i], ceiling[i]) : ceiling[i];
            }
        }
    }

    // A ground's share of voxel row y: from its bottom up to the ground.
    private static float GroundShare(float ground, int y, float s) => Math.Min(1f, Math.Max(0f, (ground - (y * s)) / s));

    // The room of a voxel past the brick's edge (brick-relative coordinates): its floor and its ceiling.
    private static (float Floor, float Ceiling) Beyond(Brick brick, float s, IReadOnlyList<SolidBox> boxes, Func<float, float, float> groundAt, int x, int y, int z)
    {
        Vec3 bottom = Corner(brick, s, x, y, z) + new Vec3(s * 0.5f, 0f, s * 0.5f);
        float floor = groundAt == null ? 0f : GroundShare(groundAt(bottom.X, bottom.Z), (brick.Y * Brick.Size) + y, s);
        float ceiling = 1f;
        foreach (SolidBox box in boxes)
        {
            Meet(box, bottom, s, ref floor, ref ceiling);
        }

        return (floor, ceiling);
    }

    // Where a box meets a voxel's middle line, from its bottom: reaching below the middle, it fills the voxel from
    // below up to where it ends (a floor); only in its upper half, it is a ceiling over the voxel's room.
    private static void Meet(SolidBox box, Vec3 bottom, float s, ref float floor, ref float ceiling)
    {
        if (!box.Span(bottom, bottom + new Vec3(0f, s, 0f), out float enter, out float leave))
        {
            return;
        }

        if (enter <= 0.5f)
        {
            floor = Math.Max(floor, leave);
        }
        else
        {
            ceiling = Math.Min(ceiling, enter);
        }
    }

    // Each voxel of the brick whose middle, or the segments from it to the next middles east, up or north, may
    // meet the box.
    private static void Over(Brick brick, float s, SolidBox box, Action<int, int, int> each)
    {
        (Vec3 min, Vec3 max) = box.Bounds();
        if (!Range(min.X, max.X, brick.X, s, out int x0, out int x1)
            || !Range(min.Y, max.Y, brick.Y, s, out int y0, out int y1)
            || !Range(min.Z, max.Z, brick.Z, s, out int z0, out int z1))
        {
            return;
        }

        for (int y = y0; y <= y1; y++)
        {
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    // Every test for a voxel lies between its middle and the next middles east and north, from its
                    // bottom to the top of the voxel over it: a box that cannot meet that is left out.
                    Vec3 corner = Corner(brick, s, x, y, z);
                    if (box.Overlaps(corner + new Vec3(s * 0.5f, 0f, s * 0.5f), corner + new Vec3(s * 1.5f, s * 2f, s * 1.5f)))
                    {
                        each(x, y, z);
                    }
                }
            }
        }
    }

    // A box reaching below a voxel's middle fills it from its bottom up to where it ends on the middle line (a floor
    // lying in it); one only in its upper half is a ceiling over its room, closing the face above instead.
    private static void Fill(Brick brick, float s, SolidBox box, int x, int y, int z, float[] ceiling)
    {
        int i = Brick.Index(x, y, z);
        float floor = brick.Solid[i];
        Meet(box, Corner(brick, s, x, y, z) + new Vec3(s * 0.5f, 0f, s * 0.5f), s, ref floor, ref ceiling[i]);
        brick.Solid[i] = floor;
    }

    private static void Close(Brick brick, float s, SolidBox box, int x, int y, int z, Func<int, int, int, (float Floor, float Ceiling)> room)
    {
        int i = Brick.Index(x, y, z);
        (float Floor, float Ceiling) own = room(x, y, z);
        if (own.Floor >= 1f)
        {
            return;
        }

        Vec3 corner = Corner(brick, s, x, y, z);
        if (Side(box, corner, s, own, room(x + 1, y, z), new Vec3(s, 0f, 0f)))
        {
            brick.Closed[i] |= Brick.ClosedX;
        }

        if (Side(box, corner, s, own, room(x, y, z + 1), new Vec3(0f, 0f, s)))
        {
            brick.Closed[i] |= Brick.ClosedZ;
        }

        (float Floor, float Ceiling) above = room(x, y + 1, z);
        if (box.Crosses(corner + new Vec3(s * 0.5f, Middle(own, s), s * 0.5f), corner + new Vec3(s * 0.5f, s + Middle(above, s), s * 0.5f)))
        {
            brick.Closed[i] |= Brick.ClosedY;
        }
    }

    // Whether the face between a voxel and the next one along a way is closed: they have no room at a same height
    // (one is under a piece the other's water is on), or the box crosses the way between their middles at the middle
    // of the height where both have room. A solid next voxel takes no water whatever its faces: it is tested at its
    // top, as it always was.
    private static bool Side(SolidBox box, Vec3 corner, float s, (float Floor, float Ceiling) own, (float Floor, float Ceiling) next, Vec3 way)
    {
        float low = Math.Max(own.Floor, next.Floor), high = Math.Min(own.Ceiling, next.Ceiling);
        if (high <= low && next.Floor < 1f)
        {
            return true;
        }

        var from = corner + new Vec3(s * 0.5f, (high > low ? (low + high) * 0.5f : low) * s, s * 0.5f);
        return box.Crosses(from, from + way);
    }

    // Height of the middle of a voxel's room, from its bottom, metres.
    private static float Middle((float Floor, float Ceiling) room, float s) => (room.Floor + room.Ceiling) * 0.5f * s;

    private static Vec3 Corner(Brick brick, float s, int x, int y, int z) =>
        new Vec3((brick.X * Brick.Size) + x, (brick.Y * Brick.Size) + y, (brick.Z * Brick.Size) + z) * s;

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
