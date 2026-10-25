using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// The server's water as the water moved on the graphics card near the camera takes it (the plugin's SimGrid), without
/// the game: for each voxel column of a square, four floats. The top of its water resting on the ground (world height;
/// <see cref="DryTop"/> where none rests there), whether the card draws it, its foam (falling into it), and the faces
/// pieces close at that water's level (1 east, 2 north). Beside it, the height to cast down from for the ground under
/// each column (over its water, or over the water near it, which may run there; NaN where no water is near).
/// <para>
/// The card moves water as a sheet over the ground, one level to a place: it draws a column only where the server's
/// water there is one layer resting on the ground (its lowest voxel has ground in it, or a solid voxel or a piece under
/// it), and where the ground it knows under the column is under that water. Elsewhere (water falling through the air, a
/// pool under another, water under a roof the cast ground is the roof of) the bricks draw it as they do.
/// </para>
/// </summary>
internal static class SimColumns
{
    /// <summary>A column's top where no water rests.</summary>
    public const float DryTop = -10000f;

    /// <summary>Floats a column.</summary>
    public const int Texel = 4;

    /// <summary>Over its water's top, metres, the ground under a wet column is cast from (a floor this close over the water is missed).</summary>
    public const float CastOverWater = 0.3f;

    /// <summary>
    /// Over the highest water near it, metres, the ground under a dry column is cast from: high enough to start over the
    /// walls round the water (a ray starting in a wall goes through it), so they hold the card's water as the ground does;
    /// a roof that low over dry ground holds it out too, until the server's water runs under it.
    /// </summary>
    public const float CastOverNear = 3f;

    /// <summary>How far from water, metres, a dry column's ground is cast (water may run out that far before the server's does).</summary>
    public const float CastReach = 12f;

    // A voxel this full (solid and water) is full: the water over it rests on its water.
    private const float Full = 0.98f;

    // Bits of a voxel's open faces (Brick.OpenFaces): east, west, down, north, south.
    private const int OpenEast = 1, OpenWest = 2, OpenDown = 8, OpenNorth = 16, OpenSouth = 32;

    /// <summary>
    /// The texels of the voxel columns x0..x0+columns-1, z0..z0+columns-1 (rows along x, then z), from these bricks (any
    /// may lie outside), with the ground known under the square's cells (<paramref name="perVoxel"/> to a column's
    /// side; NaN where not cast yet), and the height to cast each column's ground from.
    /// </summary>
    public static float[] Build(IEnumerable<BrickView> bricks, int x0, int z0, int columns, float voxelSize, float[] bed, int perVoxel, out float[] cast)
    {
        var stacks = new Dictionary<long, List<BrickView>>();
        foreach (BrickView brick in bricks)
        {
            long key = ((long)brick.X << 32) | (uint)brick.Z;
            if (!stacks.TryGetValue(key, out List<BrickView> stack))
            {
                stack = [];
                stacks[key] = stack;
            }

            stack.Add(brick);
        }

        foreach (List<BrickView> stack in stacks.Values)
        {
            stack.Sort((a, b) => b.Y.CompareTo(a.Y));
        }

        var found = new Column[columns * columns];
        for (int vz = 0; vz < columns; vz++)
        {
            for (int vx = 0; vx < columns; vx++)
            {
                int x = x0 + vx, z = z0 + vz;
                found[(vz * columns) + vx] = stacks.TryGetValue(((long)(x >> 4) << 32) | (uint)(z >> 4), out List<BrickView> stack)
                    ? Look(stack, x & 15, z & 15, voxelSize)
                    : Column.None;
            }
        }

        float[] texels = new float[columns * columns * Texel];
        for (int vz = 0; vz < columns; vz++)
        {
            for (int vx = 0; vx < columns; vx++)
            {
                int k = (vz * columns) + vx;
                Column column = found[k];
                bool east = vx + 1 < columns && found[k + 1].ClosedWest;
                bool north = vz + 1 < columns && found[k + columns].ClosedSouth;
                texels[k * Texel] = column.Top;
                texels[(k * Texel) + 1] = column.Drawn && GroundUnder(bed, columns * perVoxel, perVoxel, vx, vz, column.Top) ? 1f : 0f;
                texels[(k * Texel) + 2] = column.Foam;
                texels[(k * Texel) + 3] = (column.ClosedEast || east ? 1 : 0) | (column.ClosedNorth || north ? 2 : 0);
            }
        }

        cast = Cast(found, columns, voxelSize);
        return texels;
    }

    // What one column holds: the top of its water resting on the ground, whether the card may draw it there (one layer,
    // resting, nothing falling into it), its foam, the faces pieces close at its level (this column's own, and those it
    // shares with the columns west and south of it, which they ask for).
    private readonly struct Column
    {
        public static readonly Column None = new(DryTop, false, 0f, false, false, false, false);

        public Column(float top, bool drawn, float foam, bool closedEast, bool closedNorth, bool closedWest, bool closedSouth)
        {
            Top = top;
            Drawn = drawn;
            Foam = foam;
            ClosedEast = closedEast;
            ClosedNorth = closedNorth;
            ClosedWest = closedWest;
            ClosedSouth = closedSouth;
        }

        public float Top { get; }

        public bool Drawn { get; }

        public float Foam { get; }

        public bool ClosedEast { get; }

        public bool ClosedNorth { get; }

        public bool ClosedWest { get; }

        public bool ClosedSouth { get; }
    }

    // The column (x, z in its bricks) of a stack of bricks, highest first: its highest run of wet voxels, where it rests,
    // and whether any water lies under it (another layer).
    private static Column Look(List<BrickView> stack, int x, int z, float voxelSize)
    {
        // The wet voxels top down, as (brick, index, row): the highest run, then whether more lies under it.
        var run = new List<(BrickView Brick, int Index, int Row)>();
        BrickView lowBrick = null;
        int low = 0;
        bool under = false;
        int previous = int.MaxValue;
        foreach (BrickView brick in stack)
        {
            for (int y = Brick.Size - 1; y >= 0; y--)
            {
                int i = Brick.Index(x, y, z);
                int row = (brick.Y * Brick.Size) + y;
                if (brick.Water[i] <= VoxelField.Wet)
                {
                    continue;
                }

                if (run.Count == 0 || row == previous - 1)
                {
                    if (!under)
                    {
                        run.Add((brick, i, row));
                        (lowBrick, low) = (brick, i);
                        previous = row;
                        continue;
                    }
                }

                under = true;
            }
        }

        if (run.Count == 0)
        {
            return Column.None;
        }

        // The run rests where its lowest voxel has ground in it, or no water could leave it downward (a solid voxel or a
        // piece under it); else it hangs over the air: falling.
        bool rests = lowBrick.Solid[low] > 0f || (lowBrick.Open[low] & OpenDown) == 0;
        if (!rests)
        {
            return new Column(DryTop, false, 0f, false, false, false, false);
        }

        // Its top: up from the lowest voxel while they are full, to the first that is not. Water over that one is
        // falling into it (drawn by the bricks, the card keeping the water resting there).
        int top = run.Count - 1;
        while (top > 0 && run[top].Brick.Solid[run[top].Index] + run[top].Brick.Water[run[top].Index] >= Full)
        {
            top--;
        }

        (BrickView brickAt, int at, int rowAt) = run[top];
        float fill = Math.Min(1f, brickAt.Solid[at] + brickAt.Water[at]);
        float level = (rowAt + fill) * voxelSize;
        float foam = Math.Min(1f, Math.Max(0f, -brickAt.VelocityY[at] / SurfaceGrid.FoamFall));
        byte open = brickAt.Open[at];
        bool fallsIn = top > 0;
        return new Column(level, !under && !fallsIn, foam, (open & OpenEast) == 0, (open & OpenNorth) == 0, (open & OpenWest) == 0, (open & OpenSouth) == 0);
    }

    // Whether the ground known under a column's cells is under its water (at least half of them): the card's water there
    // stands on it. Not where any is not cast yet, nor where a roof or a floor over the water was cast instead.
    private static bool GroundUnder(float[] bed, int cells, int perVoxel, int vx, int vz, float top)
    {
        int under = 0;
        for (int j = 0; j < perVoxel; j++)
        {
            for (int i = 0; i < perVoxel; i++)
            {
                float ground = bed[(((vz * perVoxel) + j) * cells) + (vx * perVoxel) + i];
                if (float.IsNaN(ground))
                {
                    return false;
                }

                under += ground <= top ? 1 : 0;
            }
        }

        return 2 * under >= perVoxel * perVoxel;
    }

    // The height each column's ground is cast from: over its own water, else over the highest water within CastReach
    // (a square round it), else none.
    private static float[] Cast(Column[] found, int columns, float voxelSize)
    {
        int reach = Math.Max(1, (int)Math.Round(CastReach / voxelSize));

        // The highest top within reach along each row, then along each column of that.
        float[] rows = new float[found.Length];
        for (int vz = 0; vz < columns; vz++)
        {
            for (int vx = 0; vx < columns; vx++)
            {
                float most = DryTop;
                for (int dx = Math.Max(0, vx - reach); dx <= Math.Min(columns - 1, vx + reach); dx++)
                {
                    most = Math.Max(most, found[(vz * columns) + dx].Top);
                }

                rows[(vz * columns) + vx] = most;
            }
        }

        float[] cast = new float[found.Length];
        for (int vz = 0; vz < columns; vz++)
        {
            for (int vx = 0; vx < columns; vx++)
            {
                int k = (vz * columns) + vx;
                if (found[k].Top > DryTop)
                {
                    cast[k] = found[k].Top + CastOverWater;
                    continue;
                }

                float most = DryTop;
                for (int dz = Math.Max(0, vz - reach); dz <= Math.Min(columns - 1, vz + reach); dz++)
                {
                    most = Math.Max(most, rows[(dz * columns) + vx]);
                }

                cast[k] = most > DryTop ? most + CastOverNear : float.NaN;
            }
        }

        return cast;
    }
}
