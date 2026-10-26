using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// What is solid in a brick, without the game: the ground, filling each voxel from its bottom up to the ground's
/// height at the middle of its column, and the solids standing on it (building pieces, rocks, trees, a place's
/// ruins: <see cref="Solid"/>), which fill a voxel from its bottom up to where they end on the voxel's middle line
/// (a thin floor puts the water's floor at its top) and close the faces between voxels whose middles they separate
/// (so a wall thinner than a voxel still holds water).
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

    /// <summary>
    /// Fills the brick's voxels up to the ground. A ground height that is not a number fills its column whole (solid,
    /// holding no water): it would otherwise make every head and flow round it no number either.
    /// </summary>
    /// <param name="brick">The brick.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    /// <param name="groundAt">The ground's height at a point (world x, z), metres.</param>
    /// <returns>How many columns' ground was not a number.</returns>
    public static int Ground(Brick brick, float voxelSize, Func<float, float, float> groundAt)
    {
        int notNumbers = 0;
        for (int z = 0; z < Brick.Size; z++)
        {
            for (int x = 0; x < Brick.Size; x++)
            {
                float ground = groundAt(((brick.X * Brick.Size) + x + 0.5f) * voxelSize, ((brick.Z * Brick.Size) + z + 0.5f) * voxelSize);
                if (float.IsNaN(ground))
                {
                    notNumbers++;
                    ground = float.PositiveInfinity;
                }

                for (int y = 0; y < Brick.Size; y++)
                {
                    int i = Brick.Index(x, y, z);
                    brick.Solid[i] = Math.Max(brick.Solid[i], GroundShare(ground, (brick.Y * Brick.Size) + y, voxelSize));
                }
            }
        }

        return notNumbers;
    }

    /// <summary>
    /// Fills the voxels the solids stand in from below, then closes the faces they cross: each tested at the middle of
    /// the room left above the solid (a wall standing on a floor closes the water's way over that floor, the floor
    /// itself does not close the voxel it lies in).
    /// </summary>
    /// <param name="brick">The brick.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    /// <param name="solids">The solids near the brick.</param>
    /// <param name="groundAt">The ground's height at a point (world x, z), metres, for the voxels just past the brick's
    /// edges (whose share a face there is tested by); none: no ground there.</param>
    public static void Pieces(Brick brick, float voxelSize, IReadOnlyList<Solid> solids, Func<float, float, float> groundAt = null)
    {
        // Each voxel's floor (a share of the voxel from its bottom: the ground's, then the solids') and ceiling (the
        // lowest underside of a solid over its upper half on its middle line, from its bottom; 1: none), for the
        // brick's voxels and the layer past its far edges, whose room a face there is tested by (the brick there may
        // not exist yet: its floor is the ground's).
        float[] floor = new float[Edge * Edge * Edge];
        float[] ceiling = new float[Edge * Edge * Edge];
        for (int y = 0; y < Edge; y++)
        {
            for (int z = 0; z < Edge; z++)
            {
                for (int x = 0; x < Edge; x++)
                {
                    int k = Key(x, y, z);
                    ceiling[k] = 1f;
                    if (x < Brick.Size && y < Brick.Size && z < Brick.Size)
                    {
                        floor[k] = brick.Solid[Brick.Index(x, y, z)];
                    }
                    else if (groundAt != null)
                    {
                        Vec3 middle = Corner(brick, voxelSize, x, 0, z) + new Vec3(voxelSize * 0.5f, 0f, voxelSize * 0.5f);
                        floor[k] = GroundShare(groundAt(middle.X, middle.Z), (brick.Y * Brick.Size) + y, voxelSize);
                    }
                }
            }
        }

        var spans = new List<(float Low, float High)>();
        foreach (Solid solid in solids)
        {
            Fill(brick, voxelSize, solid, floor, ceiling, spans);
        }

        for (int y = 0; y < Brick.Size; y++)
        {
            for (int z = 0; z < Brick.Size; z++)
            {
                for (int x = 0; x < Brick.Size; x++)
                {
                    int i = Brick.Index(x, y, z);
                    int k = Key(x, y, z);
                    brick.Solid[i] = floor[k];
                    if (ceiling[k] < 1f)
                    {
                        brick.Ceiling[i] = brick.Ceiling[i] > 0f ? Math.Min(brick.Ceiling[i], ceiling[k]) : ceiling[k];
                    }
                }
            }
        }

        (float Floor, float Ceiling) Room(int x, int y, int z)
        {
            int k = Key(x, y, z);
            return (floor[k], ceiling[k]);
        }

        foreach (Solid solid in solids)
        {
            Over(brick, voxelSize, solid, (x, y, z) => Close(brick, voxelSize, solid, x, y, z, Room));
        }
    }

    // A ground's share of voxel row y: from its bottom up to the ground.
    private static float GroundShare(float ground, int y, float s) => Math.Min(1f, Math.Max(0f, (ground - (y * s)) / s));

    private static int Key(int x, int y, int z) => (((y * Edge) + z) * Edge) + x;

    // The solid down each voxel column it reaches (the brick's and the layer past its edges): where it holds a
    // column's middle line below a voxel's middle, it fills the voxel from its bottom up to where it ends (a floor);
    // where only in the voxel's upper half, it is a ceiling over the voxel's room.
    private static void Fill(Brick brick, float s, Solid solid, float[] floor, float[] ceiling, List<(float Low, float High)> spans)
    {
        (Vec3 min, Vec3 max) = solid.Bounds();
        float bottom = (brick.Y * Brick.Size) * s;
        float top = ((brick.Y * Brick.Size) + Edge) * s;
        if (min.Y > top || max.Y < bottom
            || !Columns(min.X, max.X, brick.X, s, out int x0, out int x1)
            || !Columns(min.Z, max.Z, brick.Z, s, out int z0, out int z1))
        {
            return;
        }

        for (int z = z0; z <= z1; z++)
        {
            for (int x = x0; x <= x1; x++)
            {
                Vec3 middle = Corner(brick, s, x, 0, z) + new Vec3(s * 0.5f, 0f, s * 0.5f);
                spans.Clear();
                solid.Column(middle.X, middle.Z, bottom, top, spans);
                if (spans.Count == 0)
                {
                    continue;
                }

                // Every row: a span may run on below the solid's box (a mesh open at its foot).
                for (int y = 0; y < Edge; y++)
                {
                    float voxelBottom = ((brick.Y * Brick.Size) + y) * s;
                    int k = Key(x, y, z);
                    foreach ((float low, float high) in spans)
                    {
                        Meet(low, high, voxelBottom, s, ref floor[k], ref ceiling[k]);
                    }
                }
            }
        }
    }

    // Where a solid's span of a voxel's middle line (world heights) meets the voxel from its bottom: reaching below
    // the middle, it fills the voxel from below up to where it ends (a floor); only in its upper half, it is a
    // ceiling over the voxel's room.
    private static void Meet(float low, float high, float voxelBottom, float s, ref float floor, ref float ceiling)
    {
        float enter = Math.Max(0f, (low - voxelBottom) / s);
        float leave = Math.Min(1f, (high - voxelBottom) / s);
        if (enter > leave)
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
    // meet the solid.
    private static void Over(Brick brick, float s, Solid solid, Action<int, int, int> each)
    {
        (Vec3 min, Vec3 max) = solid.Bounds();
        if (!Range(min.X, max.X, brick.X, s, out int x0, out int x1, Brick.Size)
            || !Range(min.Y, max.Y, brick.Y, s, out int y0, out int y1, Brick.Size)
            || !Range(min.Z, max.Z, brick.Z, s, out int z0, out int z1, Brick.Size))
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
                    // bottom to the top of the voxel over it: a solid that cannot meet that is left out.
                    Vec3 corner = Corner(brick, s, x, y, z);
                    if (solid.Overlaps(corner + new Vec3(s * 0.5f, 0f, s * 0.5f), corner + new Vec3(s * 1.5f, s * 2f, s * 1.5f)))
                    {
                        each(x, y, z);
                    }
                }
            }
        }
    }

    private static void Close(Brick brick, float s, Solid solid, int x, int y, int z, Func<int, int, int, (float Floor, float Ceiling)> room)
    {
        int i = Brick.Index(x, y, z);
        (float Floor, float Ceiling) own = room(x, y, z);
        if (own.Floor >= 1f)
        {
            return;
        }

        Vec3 corner = Corner(brick, s, x, y, z);
        if (Side(solid, corner, s, own, room(x + 1, y, z), new Vec3(s, 0f, 0f)))
        {
            brick.Closed[i] |= Brick.ClosedX;
        }

        if (Side(solid, corner, s, own, room(x, y, z + 1), new Vec3(0f, 0f, s)))
        {
            brick.Closed[i] |= Brick.ClosedZ;
        }

        (float Floor, float Ceiling) above = room(x, y + 1, z);
        if (solid.Crosses(corner + new Vec3(s * 0.5f, Middle(own, s), s * 0.5f), corner + new Vec3(s * 0.5f, s + Middle(above, s), s * 0.5f)))
        {
            brick.Closed[i] |= Brick.ClosedY;
        }
    }

    // Whether the face between a voxel and the next one along a way is closed: they have no room at a same height
    // (one is under a piece the other's water is on), or the solid crosses the way between their middles at the middle
    // of the height where both have room. A solid next voxel takes no water whatever its faces: it is tested at its
    // top, as it always was.
    private static bool Side(Solid solid, Vec3 corner, float s, (float Floor, float Ceiling) own, (float Floor, float Ceiling) next, Vec3 way)
    {
        float low = Math.Max(own.Floor, next.Floor), high = Math.Min(own.Ceiling, next.Ceiling);
        if (high <= low && next.Floor < 1f)
        {
            return true;
        }

        var from = corner + new Vec3(s * 0.5f, (high > low ? (low + high) * 0.5f : low) * s, s * 0.5f);
        return solid.Crosses(from, from + way);
    }

    // Height of the middle of a voxel's room, from its bottom, metres.
    private static float Middle((float Floor, float Ceiling) room, float s) => (room.Floor + room.Ceiling) * 0.5f * s;

    private static Vec3 Corner(Brick brick, float s, int x, int y, int z) =>
        new Vec3((brick.X * Brick.Size) + x, (brick.Y * Brick.Size) + y, (brick.Z * Brick.Size) + z) * s;

    // The brick's voxels along one axis (count of them, the brick's own or with the layer past its far edge) from
    // which a solid spanning min..max may be met (a voxel's segment reaches the next middle, a voxel further): false
    // when none.
    private static bool Range(float min, float max, int brick, float s, out int from, out int to, int count)
    {
        int first = brick * Brick.Size;
        from = Math.Max(first, (int)Math.Floor((min / s) - 0.5f) - 1) - first;
        to = Math.Min(first + count - 1, (int)Math.Floor((max / s) - 0.5f) + 1) - first;
        return from <= to;
    }

    // The columns along one axis (the brick's and the one past its far edge) whose middle line a solid spanning
    // min..max may hold: false when none.
    private static bool Columns(float min, float max, int brick, float s, out int from, out int to)
    {
        int first = brick * Brick.Size;
        from = Math.Max(first, (int)Math.Ceiling((min / s) - 0.5f)) - first;
        to = Math.Min(first + Edge - 1, (int)Math.Floor((max / s) - 0.5f)) - first;
        return from <= to;
    }
}
