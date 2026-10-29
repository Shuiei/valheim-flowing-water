using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// The water a player's game knows, without the game: the bricks it received (<see cref="BrickView"/>), and from
/// them the level of the water at any point (where a swimmer floats) and its velocity (what carries them).
/// </summary>
internal sealed class VoxelField
{
    /// <summary>Less water than this share of a voxel is drawn as none (half a step of the brick data's byte).</summary>
    public const float Wet = 0.5f / BrickData.Full;

    /// <summary>A voxel this full of water and solid holds the water over it up (no film on its way down over it).</summary>
    public const float Full = 0.95f;

    // A voxel coordinate's bits in a voxel key: ±1,048,575 voxels along each axis.
    private const int KeyBits = 21;

    private const int KeyMask = (1 << KeyBits) - 1;

    private readonly Dictionary<long, BrickView> _bricks = [];

    /// <summary>Metres along a voxel's side (the latest brick's).</summary>
    public float VoxelSize { get; private set; } = 1f;

    /// <summary>The bricks known.</summary>
    public IReadOnlyCollection<BrickView> Bricks => _bricks.Values;

    /// <summary>Counts the changes to the bricks known: while it stays the same, so do they.</summary>
    public int Version { get; private set; }

    /// <summary>
    /// A voxel's key from its voxel coordinates (not its brick's: <see cref="VoxelGrid.Key"/> is for bricks), 21 bits
    /// each: 262 km from the world's zero along each axis at 0.25 m voxels, far beyond any Valheim world. Two voxels
    /// farther apart than that would share a key.
    /// </summary>
    public static long VoxelKey(int x, int y, int z) => ((long)(x & KeyMask) << (2 * KeyBits)) | ((long)(y & KeyMask) << KeyBits) | (uint)(z & KeyMask);

    /// <summary>Keeps a brick's latest water (it replaces what was known of it).</summary>
    public void Set(BrickView view)
    {
        VoxelSize = view.VoxelSize;
        _bricks[VoxelGrid.Key(view.X, view.Y, view.Z)] = view;
        Version++;
    }

    /// <summary>
    /// A field of only the bricks <see cref="VoxelSurface.Build"/> reads for this one, to build its surface away from
    /// this field (on another thread, while this one changes): those round it, the ones over them as far as a column's
    /// top is looked for (<see cref="VoxelSurface.BricksOver"/>), and those under them as far down as a column's depth
    /// is measured (from a top as low as the tops its corners join, a few rows under the brick).
    /// </summary>
    public VoxelField Near(BrickView view)
    {
        var near = new VoxelField { VoxelSize = VoxelSize };
        for (int dy = -(VoxelSurface.DeepestColumn / Brick.Size) - 1; dy <= VoxelSurface.BricksOver; dy++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    long key = VoxelGrid.Key(view.X + dx, view.Y + dy, view.Z + dz);
                    if (_bricks.TryGetValue(key, out BrickView brick))
                    {
                        near._bricks[key] = brick;
                    }
                }
            }
        }

        return near;
    }

    /// <summary>Forgets a brick (its object is gone).</summary>
    public void Remove(int x, int y, int z)
    {
        if (_bricks.Remove(VoxelGrid.Key(x, y, z)))
        {
            Version++;
        }
    }

    /// <summary>The brick at these brick coordinates, or null.</summary>
    public BrickView Find(int x, int y, int z) => _bricks.TryGetValue(VoxelGrid.Key(x, y, z), out BrickView view) ? view : null;

    /// <summary>The brick holding voxel (x, y, z) and the voxel's index in it; false (no brick) where none is known.</summary>
    public bool Voxel(int x, int y, int z, out BrickView view, out int index)
    {
        view = Find(Brick.Of(x), Brick.Of(y), Brick.Of(z));
        index = Brick.Index(Brick.In(x), Brick.In(y), Brick.In(z));
        return view != null;
    }

    /// <summary>The water in voxel (x, y, z), as a share of its volume; 0 where no brick is known.</summary>
    public float WaterAt(int x, int y, int z) => Voxel(x, y, z, out BrickView view, out int i) ? view.Water[i] : 0f;

    /// <summary>
    /// Whether voxel (x, y, z) holds water to draw: more than <see cref="Wet"/>, and the voxel not all solid (water the
    /// server keeps inside a piece built over it, with nowhere to go, is not seen).
    /// </summary>
    public bool IsWet(int x, int y, int z) => Voxel(x, y, z, out BrickView view, out int i) && IsWet(view, i);

    /// <summary>Whether a brick's voxel holds water to draw (<see cref="IsWet(int, int, int)"/>).</summary>
    public static bool IsWet(BrickView view, int index) => view.Water[index] > Wet && view.Solid[index] < 1f;

    /// <summary>
    /// Whether the water of voxel (x, y, z) goes on into the voxel over it: that one is wet and has no floor in it (a
    /// solid share there is a floor between them, the water over it a pool of its own).
    /// </summary>
    public bool GoesUp(int x, int y, int z) => Voxel(x, y + 1, z, out BrickView view, out int i) && IsWet(view, i) && view.Solid[i] <= 0f;

    /// <summary>The height of the water's top in voxel (x, y, z), metres: on its solid share, at most its top.</summary>
    public float Level(int x, int y, int z)
    {
        float fill = Voxel(x, y, z, out BrickView view, out int i) ? Math.Min(1f, view.Solid[i] + view.Water[i]) : 0f;
        return (y + fill) * VoxelSize;
    }

    /// <summary>The height of the floor of the water in voxel (x, y, z): the top of its solid share, metres.</summary>
    public float Floor(int x, int y, int z) => (y + (Voxel(x, y, z, out BrickView view, out int i) ? view.Solid[i] : 0f)) * VoxelSize;

    /// <summary>
    /// The height of the top of the water voxel (x, y, z) is in, metres, its water laid flat on the ground under it: a
    /// film the server keeps in the voxel over one that is not full (water on its way down a slope) is that little
    /// water over the ground, not water up to the voxel it is in. Only the water of the top's voxel and those under it
    /// down to the first holding ground (<see cref="BrickData.HoldsGround"/>), at most <see cref="VoxelSurface.MaxStep"/>
    /// under it; water over a full voxel, or over the air (a fall), stands where it is: its top's level.
    /// </summary>
    public float Settled(int x, int y, int z)
    {
        int top = y;
        while (top - y < VoxelSurface.DeepestColumn && GoesUp(x, top, z))
        {
            top++;
        }

        float level = Level(x, top, z);
        float water = 0f;
        for (int v = top; top - v <= VoxelSurface.MaxStep; v--)
        {
            if (!Voxel(x, v, z, out BrickView view, out int i) || (v < top && !(IsWet(view, i) && GoesUp(x, v, z))))
            {
                return level;
            }

            water += view.Water[i];
            if (view.Solid[i] >= BrickData.HoldsGround)
            {
                return Math.Min(level, (v + view.Solid[i] + water) * VoxelSize);
            }

            if (v < top && view.Water[i] >= Full)
            {
                return level;
            }
        }

        return level;
    }

    /// <summary>
    /// The surface of the water a point is in or just over (within a voxel), metres: the top of its stack of wet
    /// voxels; negative infinity where there is none.
    /// </summary>
    public float SurfaceAt(float x, float y, float z)
    {
        if (!Wettest(x, y, z, out int vx, out int vy, out int vz))
        {
            return float.NegativeInfinity;
        }

        while (GoesUp(vx, vy, vz))
        {
            vy++;
        }

        return Level(vx, vy, vz);
    }

    /// <summary>
    /// Where a ray first goes down into the water's surface, along its direction (a unit vector), within a distance:
    /// looked for in steps of half a voxel (the last one at the distance), and at the crossing, the point of the ray at
    /// the surface's level. A step from over a surface to past its level is a crossing too, though the ray is through
    /// the water by then (a sheet thinner than the step, the ground under it): where the ray is at that level, it is
    /// in that water, and only if that water's surface is at that level (not a lower pool beside it). Water gone into
    /// through its side, under its level, is no crossing. A ray starting in the water finds where it is first out of it
    /// and in again.
    /// </summary>
    /// <returns>Whether it meets the surface within the distance.</returns>
    public bool RayToSurface(float ox, float oy, float oz, float dx, float dy, float dz, float within, out float distance)
    {
        float step = VoxelSize * 0.5f;
        float before = 0f;
        bool above = false;
        float over = float.NegativeInfinity;
        for (float t = 0f; ; t = Math.Min(t + step, within))
        {
            float y = oy + (dy * t);
            float surface = SurfaceAt(ox + (dx * t), y, oz + (dz * t));
            bool under = !float.IsNegativeInfinity(surface) && y <= surface;
            // Down through the surface's level since the last step (so the ray goes down), not in through its side.
            if (above && under && oy + (dy * before) > surface)
            {
                distance = Math.Min(t, Math.Max(before, (oy - surface) / -dy));
                return true;
            }

            if (above && dy < 0f && y <= over)
            {
                float crossing = (oy - over) / -dy;
                float there = SurfaceAt(ox + (dx * crossing), over - (VoxelSize * 0.01f), oz + (dz * crossing));
                if (Math.Abs(there - over) <= VoxelSize * 0.01f)
                {
                    distance = crossing;
                    return true;
                }
            }

            above = !under;
            over = above ? surface : float.NegativeInfinity;
            before = t;
            if (t >= within)
            {
                distance = 0f;
                return false;
            }
        }
    }

    /// <summary>
    /// The water's velocity at a point in it, metres a second (x east, y up, z north); false where dry, and over the
    /// water's surface.
    /// </summary>
    public bool VelocityAt(float x, float y, float z, out float vx, out float vy, out float vz)
    {
        vx = vy = vz = 0f;
        if (!Wettest(x, y, z, out int ix, out int iy, out int iz) || y > SurfaceAt(x, y, z))
        {
            return false;
        }

        Voxel(ix, iy, iz, out BrickView view, out int i);
        vx = view.VelocityX[i];
        vy = view.VelocityY[i];
        vz = view.VelocityZ[i];
        return true;
    }

    // The wet voxel a point is in, or the one under it when the point is just over the water; none for a point in a
    // wet voxel's solid share, under its floor (inside a raised floor, not in the water on it).
    private bool Wettest(float x, float y, float z, out int vx, out int vy, out int vz)
    {
        float s = VoxelSize;
        vx = (int)Math.Floor(x / s);
        vy = (int)Math.Floor(y / s);
        vz = (int)Math.Floor(z / s);
        if (IsWet(vx, vy, vz))
        {
            return y >= Floor(vx, vy, vz);
        }

        vy--;
        return IsWet(vx, vy, vz);
    }
}
