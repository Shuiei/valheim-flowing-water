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

    /// <summary>A voxel this full, with its solid share, holds the water over it up.</summary>
    public const float Full = 0.95f;

    /// <summary>The least share of a voxel its water's speed is worked out over (<see cref="WaterVelocity"/>).</summary>
    public const float LeastShare = 0.05f;

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
    /// Whether the water in voxel (x, y, z) falls through the air: open under it, holding no floor, nothing under it
    /// holding ground (as the server says, <see cref="BrickData.GroundUnder"/>, or as the voxel under holds some), and
    /// the voxel under not full: a fall's water, or the spray a fall leaves over a pool, not water standing
    /// (<see cref="VoxelShell"/> draws it as a fall, and keeps no side under it).
    /// </summary>
    public bool FallsThrough(int x, int y, int z)
    {
        if (!Voxel(x, y, z, out BrickView view, out int i) || !IsWet(view, i))
        {
            return false;
        }

        int open = view.Open[i];
        if ((open & (1 << 3)) == 0 || (open & BrickData.GroundUnder) != 0 || view.Solid[i] >= BrickData.HoldsGround)
        {
            return false;
        }

        if (!Voxel(x, y - 1, z, out BrickView under, out int j))
        {
            return true;
        }

        return under.Solid[j] < BrickData.HoldsGround && under.Water[j] + under.Solid[j] < Full;
    }

    /// <summary>
    /// The top of the standing water voxel (x, y, z) is in: the highest voxel its water goes on up into, short of water
    /// falling through the air over it (<see cref="FallsThrough"/>: a fall landing on a pool, the spray over it), at
    /// most <see cref="VoxelSurface.DeepestColumn"/> over it.
    /// </summary>
    public int StandingTop(int x, int y, int z)
    {
        int top = y;
        while (top - y < VoxelSurface.DeepestColumn && GoesUp(x, top, z) && !FallsThrough(x, top + 1, z))
        {
            top++;
        }

        return top;
    }

    /// <summary>
    /// The height of the top of the standing water voxel (x, y, z) is in (<see cref="StandingTop"/>), metres, its water
    /// laid flat on the ground under it: a film the server keeps in the voxel over one that is not full and holds
    /// ground (water on its way down a slope) is that little water over the ground, not water up to the voxel it is in.
    /// Only a top's voxel straight over one holding ground (<see cref="BrickData.HoldsGround"/>), as
    /// <see cref="VoxelShell"/> takes it for a step down a slope; water over a voxel with none (a fall's, through the
    /// air) stands where it is: its top's level.
    /// </summary>
    public float Settled(int x, int y, int z)
    {
        int top = StandingTop(x, y, z);
        float level = Level(x, top, z);
        if (!Voxel(x, top - 1, z, out BrickView under, out int i) || !IsWet(under, i) || !GoesUp(x, top - 1, z) || under.Solid[i] < BrickData.HoldsGround)
        {
            return level;
        }

        return Math.Min(level, (top - 1 + under.Solid[i] + under.Water[i] + WaterAt(x, top, z)) * VoxelSize);
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
    /// The water's velocity at a point in it, metres a second (x east, y up, z north): the voxel's water's own
    /// (<see cref="WaterVelocity"/>); false where dry, and over the water's surface.
    /// </summary>
    public bool VelocityAt(float x, float y, float z, out float vx, out float vy, out float vz)
    {
        vx = vy = vz = 0f;
        if (!Wettest(x, y, z, out int ix, out int iy, out int iz) || y > SurfaceAt(x, y, z))
        {
            return false;
        }

        Voxel(ix, iy, iz, out BrickView view, out int i);
        (vx, vy, vz) = WaterVelocity(view, i);
        return true;
    }

    /// <summary>
    /// How fast a voxel's water runs, metres a second (x east, y up, z north). The server sends what flows through the
    /// voxel's faces over a face's area: the water's speed times how full the voxel is, so a sheet or a fall a tenth of
    /// a voxel thick runs ten times as fast as that (worked out over <see cref="LeastShare"/> of a voxel at least, or a
    /// film's flow would make it endless).
    /// </summary>
    public static (float X, float Y, float Z) WaterVelocity(BrickView view, int i)
    {
        float share = Math.Max(view.Water[i], LeastShare);
        return (view.VelocityX[i] / share, view.VelocityY[i] / share, view.VelocityZ[i] / share);
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
