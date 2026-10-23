using System;
using FlowingWater.Volume;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Solids for tests: voxels below a level are ground (or the ground stands as high as a function says), and faces a
/// function says a piece closes.
/// </summary>
internal sealed class TestSolids : ISolids
{
    private readonly int _groundBelow;

    private readonly Func<int, int, int, byte> _closed;

    private readonly Func<int, int, float> _ground;

    /// <summary>
    /// Ground under voxel row <paramref name="groundBelow"/>, or up to <paramref name="ground"/> (x, z → its height in
    /// voxels, the voxel it ends in solid up to there); faces closed by <paramref name="closed"/> (x, y, z → bits).
    /// </summary>
    public TestSolids(int groundBelow, Func<int, int, int, byte> closed = null, Func<int, int, float> ground = null)
    {
        _groundBelow = groundBelow;
        _closed = closed ?? ((_, _, _) => 0);
        _ground = ground;
    }

    public void Fill(Brick brick, float voxelSize)
    {
        for (int i = 0; i < Brick.Count; i++)
        {
            (int x, int y, int z) = Brick.Place(i);
            int gx = (brick.X * Brick.Size) + x, gy = (brick.Y * Brick.Size) + y, gz = (brick.Z * Brick.Size) + z;
            brick.Solid[i] = _ground == null ? (gy < _groundBelow ? 1f : 0f) : Math.Min(1f, Math.Max(0f, _ground(gx, gz) - gy));
            brick.Closed[i] = _closed(gx, gy, gz);
        }
    }

    /// <summary>The bricks (x, y, z) whose solids cannot be read yet; none unless set.</summary>
    public Func<int, int, int, bool> Waiting { get; set; }

    public bool Ready(int x, int y, int z, float voxelSize) => Waiting?.Invoke(x, y, z) != true;

    /// <summary>
    /// Walls round the voxels x0..x1, z0..z1 (from y0 up), as faces closed between them and the outside;
    /// <paramref name="open"/> leaves one voxel's face open (a hole).
    /// </summary>
    public static Func<int, int, int, byte> Box(int x0, int x1, int z0, int z1, int y0 = 0, (int X, int Y, int Z, int Side)? open = null) => (x, y, z) =>
    {
        if (y < y0)
        {
            return 0;
        }

        byte bits = 0;
        bool inZ = z >= z0 && z <= z1;
        bool inX = x >= x0 && x <= x1;
        if (inZ && (x == x1 || x == x0 - 1) && !Is(open, x == x1 ? x : x0, y, z, x == x1 ? 0 : 1))
        {
            bits |= Brick.ClosedX;
        }

        if (inX && (z == z1 || z == z0 - 1) && !Is(open, x, y, z == z1 ? z : z0, z == z1 ? 4 : 5))
        {
            bits |= Brick.ClosedZ;
        }

        return bits;
    };

    private static bool Is((int X, int Y, int Z, int Side)? open, int x, int y, int z, int side) =>
        open is { } o && o.X == x && o.Y == y && o.Z == z && o.Side == side;
}
