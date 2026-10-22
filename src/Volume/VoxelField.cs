using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// The water a player's game knows, without the game: the bricks it received (<see cref="BrickView"/>), and from
/// them the level of the water at any point (where a swimmer floats) and its velocity (what carries them).
/// </summary>
internal sealed class VoxelField
{
    /// <summary>Less water than this share of a voxel is drawn as none (one step of the brick data's byte).</summary>
    public const float Wet = 0.5f / 200f;

    private readonly Dictionary<long, BrickView> _bricks = [];

    /// <summary>Metres along a voxel's side (the latest brick's).</summary>
    public float VoxelSize { get; private set; } = 1f;

    /// <summary>The bricks known.</summary>
    public IReadOnlyCollection<BrickView> Bricks => _bricks.Values;

    /// <summary>Keeps a brick's latest water (it replaces what was known of it).</summary>
    public void Set(BrickView view)
    {
        VoxelSize = view.VoxelSize;
        _bricks[VoxelGrid.Key(view.X, view.Y, view.Z)] = view;
    }

    /// <summary>
    /// A field of only the bricks <see cref="VoxelSurface.Build"/> reads for this one, to build its surface away from
    /// this field (on another thread, while this one changes): those round it, the one over them, and those under
    /// them as far down as a column's depth is measured (from a top as low as a voxel under the brick).
    /// </summary>
    public VoxelField Near(BrickView view)
    {
        var near = new VoxelField { VoxelSize = view.VoxelSize };
        for (int dy = -(VoxelSurface.DeepestColumn / Brick.Size) - 1; dy <= 1; dy++)
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
    public void Remove(int x, int y, int z) => _bricks.Remove(VoxelGrid.Key(x, y, z));

    /// <summary>The brick at these brick coordinates, or null.</summary>
    public BrickView Find(int x, int y, int z) => _bricks.TryGetValue(VoxelGrid.Key(x, y, z), out BrickView view) ? view : null;

    /// <summary>The water in voxel (x, y, z), as a share of its volume; 0 where no brick is known.</summary>
    public float WaterAt(int x, int y, int z) => Find(x >> 4, y >> 4, z >> 4) is { } view ? view.Water[Brick.Index(x & 15, y & 15, z & 15)] : 0f;

    /// <summary>Whether voxel (x, y, z) holds water to draw.</summary>
    public bool IsWet(int x, int y, int z) => WaterAt(x, y, z) > Wet;

    /// <summary>
    /// Whether the water of voxel (x, y, z) goes on into the voxel over it: that one is wet and has no floor in it (a
    /// solid share there is a floor between them, the water over it a pool of its own).
    /// </summary>
    public bool GoesUp(int x, int y, int z) => IsWet(x, y + 1, z) && Floor(x, y + 1, z) <= (y + 1) * VoxelSize;

    /// <summary>The height of the water's top in voxel (x, y, z), metres: on its solid share, at most its top.</summary>
    public float Level(int x, int y, int z)
    {
        BrickView view = Find(x >> 4, y >> 4, z >> 4);
        int i = Brick.Index(x & 15, y & 15, z & 15);
        float fill = view == null ? 0f : Math.Min(1f, view.Solid[i] + view.Water[i]);
        return (y + fill) * VoxelSize;
    }

    /// <summary>The height of the floor of the water in voxel (x, y, z): the top of its solid share, metres.</summary>
    public float Floor(int x, int y, int z)
    {
        BrickView view = Find(x >> 4, y >> 4, z >> 4);
        return (y + (view == null ? 0f : view.Solid[Brick.Index(x & 15, y & 15, z & 15)])) * VoxelSize;
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

        BrickView view = Find(ix >> 4, iy >> 4, iz >> 4);
        int i = Brick.Index(ix & 15, iy & 15, iz & 15);
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
