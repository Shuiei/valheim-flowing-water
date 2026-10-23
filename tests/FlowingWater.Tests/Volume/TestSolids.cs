using System;
using FlowingWater.Volume;

namespace FlowingWater.Tests.Volume;

/// <summary>Solids for tests: voxels below a level are ground, and faces a function says a piece closes.</summary>
internal sealed class TestSolids : ISolids
{
    private readonly int _groundBelow;

    private readonly Func<int, int, int, byte> _closed;

    /// <summary>Ground under voxel row <paramref name="groundBelow"/>; faces closed by <paramref name="closed"/> (x, y, z → bits).</summary>
    public TestSolids(int groundBelow, Func<int, int, int, byte> closed = null)
    {
        _groundBelow = groundBelow;
        _closed = closed ?? ((_, _, _) => 0);
    }

    public void Fill(Brick brick, float voxelSize)
    {
        for (int i = 0; i < Brick.Count; i++)
        {
            (int x, int y, int z) = Brick.Place(i);
            int gy = (brick.Y * Brick.Size) + y;
            brick.Solid[i] = gy < _groundBelow ? 1f : 0f;
            brick.Closed[i] = _closed((brick.X * Brick.Size) + x, gy, (brick.Z * Brick.Size) + z);
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
