using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowingWater.Volume;

/// <summary>
/// The server's water as the water moved on the graphics card near the camera takes it (the plugin's SimGrid), without
/// the game: for each voxel column of a square, four floats. The top of its water resting on the ground (world height;
/// <see cref="DryTop"/> where none rests there), whether the card draws it, how deep its water is (all of it resting,
/// metres: the card keeps that much water there), and the faces pieces close at that water's level (1 east, 2 north)
/// plus half its foam (falling into it; under 0.5). Beside them, four more (<see cref="LayerTexel"/>): where a ledge
/// crosses the column, the top of the water at its foot and the height under which a cell's ground is at its foot
/// (<see cref="DryTop"/> for none), the top of water falling through the air over it, and nothing. Beside it all, the
/// height to cast down from for the ground under each column (over its water, or over the water near it, which may run
/// there; NaN where no water is near).
/// <para>
/// The card moves water as a sheet over the ground, one level to a place: it draws a column only where the server's
/// water there is one layer resting on the ground (its lowest voxel has ground in it, or a solid voxel or a piece under
/// it), and where the ground it knows under the column is under that water somewhere. Elsewhere (water falling through
/// the air, a pool under another, water under a roof the cast ground is the roof of) the bricks draw it as they do.
/// </para>
/// <para>
/// When the card draws falls as its own particles (its spray on), it draws water falling through the air too: from high
/// over the ground, or into a pool, the card throws falling water from its top and keeps the pool under it (if any); and
/// where a ledge (a build's edge) crosses a column, the water on the ledge and the pool at its foot are each the card's,
/// at their own levels, a cell's ground saying which it stands in.
/// </para>
/// </summary>
internal static class SimColumns
{
    /// <summary>The second float of a column the card draws (its sheet, its falls' particles, or both).</summary>
    public const float Sheet = 1f;

    /// <summary>A column's top where no water rests (and no ledge, and no fall).</summary>
    public const float DryTop = -10000f;

    /// <summary>Floats a column, and floats of its ledge and fall.</summary>
    public const int Texel = 4, LayerTexel = 4;

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

    // Water hanging over the air no higher than this over the ground cast under it, metres, is running down a step (a
    // stream down a steep slope goes from voxel to voxel so): the card draws it, as deep as its water, on that ground.
    // Higher, it is a fall. A cell whose ground is half this under the floor a column's water rests on is at the foot of
    // a ledge.
    private const float StepDrop = 1.5f;

    // Water over a pool's first voxel not full, up to this many voxels of it, is spilling in over a step, as above; more
    // is a fall into the pool.
    private const int StepVoxels = 2;

    // Bits of a voxel's open faces (Brick.OpenFaces): east, west, down, north, south.
    private const int OpenEast = 1, OpenWest = 2, OpenDown = 8, OpenNorth = 16, OpenSouth = 32;

    /// <summary>
    /// The texels of the voxel columns x0..x0+columns-1, z0..z0+columns-1 (rows along x, then z), from these bricks (any
    /// may lie outside), with the ground known under the square's cells (<paramref name="perVoxel"/> to a column's
    /// side; NaN where not cast yet); each column's ledge and fall (<paramref name="layers"/>), and the height to cast its
    /// ground from. Falls (and ledges) are the card's only where it draws falls (<paramref name="falls"/>).
    /// </summary>
    public static float[] Build(IEnumerable<BrickView> bricks, int x0, int z0, int columns, float voxelSize, float[] bed, int perVoxel, out float[] cast, out float[] layers, bool falls = false)
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

        int cells = columns * perVoxel;
        var found = new Column[columns * columns];
        float[] grounds = new float[columns * columns];
        for (int vz = 0; vz < columns; vz++)
        {
            for (int vx = 0; vx < columns; vx++)
            {
                int x = x0 + vx, z = z0 + vz, k = (vz * columns) + vx;
                grounds[k] = Ground(bed, cells, perVoxel, vx, vz, out float lowest);
                found[k] = stacks.TryGetValue(((long)(x >> 4) << 32) | (uint)(z >> 4), out List<BrickView> stack)
                    ? Look(stack, x & 15, z & 15, voxelSize, grounds[k], lowest, falls)
                    : Column.None;
            }
        }

        float[] texels = new float[columns * columns * Texel];
        layers = new float[columns * columns * LayerTexel];
        for (int vz = 0; vz < columns; vz++)
        {
            for (int vx = 0; vx < columns; vx++)
            {
                int k = (vz * columns) + vx;
                Column column = found[k];
                bool east = vx + 1 < columns && Shut(column.ClosedEast, column, grounds[k + 1], voxelSize, found[k + 1].ClosedWest, found[k + 1], grounds[k]);
                bool north = vz + 1 < columns && Shut(column.ClosedNorth, column, grounds[k + columns], voxelSize, found[k + columns].ClosedSouth, found[k + columns], grounds[k]);

                // A ledge across it (the server's voxels are all on it or all at its foot; the ground cast says which
                // cells are which): cells well under the floor its water rests on stand at the ledge's foot, in the pool
                // there (or dry). The depth kept is the mean of each cell's under its own level, over the cells with one.
                float split = DryTop, depth = column.Depth;
                bool drawn = column.Drawn;
                if (falls && !float.IsNaN(column.Floor))
                {
                    float foot = column.Floor - (StepDrop / 2f);
                    int under = CountUnder(bed, cells, perVoxel, vx, vz, foot);
                    if (under > 0 && under < perVoxel * perVoxel)
                    {
                        split = foot;
                        drawn = column.Drawn || column.OverPool;
                        depth = MeanDepth(bed, cells, perVoxel, vx, vz, split, column.Top, column.FootTop);
                    }
                }

                // Water resting must stand on the ground known (not a roof cast over it); falling alone, it is the card's.
                drawn = drawn && (column.Top <= DryTop || GroundUnder(bed, cells, perVoxel, vx, vz, column.Top, split));
                texels[k * Texel] = column.Top;
                texels[(k * Texel) + 1] = drawn ? Sheet : 0f;
                texels[(k * Texel) + 2] = depth;
                texels[(k * Texel) + 3] = ((east ? 1 : 0) | (north ? 2 : 0)) + (0.5f * Math.Min(column.Foam, 0.99f));
                layers[k * LayerTexel] = split > DryTop ? column.FootTop : DryTop;
                layers[(k * LayerTexel) + 1] = split;
                layers[(k * LayerTexel) + 2] = drawn && !float.IsNaN(column.FallTop) ? column.FallTop : DryTop;
            }
        }

        cast = Cast(found, columns, voxelSize);
        return texels;
    }

    // What one column holds: the top of its water resting on the ground, whether the card may draw it there (one layer,
    // resting, nothing falling into it, or anything when it draws falls), its foam, the faces pieces close at its level
    // (this column's own, and those it shares with the columns west and south of it, which they ask for). Where it draws
    // falls: the floor that water rests on (NaN: none), the top of the pool under it all (if any), whether the
    // card may draw it all once a ledge is found across the column, and the top of the water falling in it (NaN: none).
    private readonly struct Column
    {
        public static readonly Column None = new(DryTop, 0f, int.MinValue, false, 0f, BrickData.AllOpen);

        public Column(float top, float depth, int row, bool drawn, float foam, byte open, float floor = float.NaN, float fallTop = float.NaN, float footTop = DryTop, bool overPool = false)
        {
            Top = top;
            Depth = depth;
            Row = row;
            Drawn = drawn;
            Foam = foam;
            Open = open;
            Floor = floor;
            FallTop = fallTop;
            FootTop = footTop;
            OverPool = overPool;
        }

        public float Top { get; }

        public float Depth { get; }

        // The voxel row whose faces are those said closed, and its open faces.
        public int Row { get; }

        public byte Open { get; }

        public bool Drawn { get; }

        public float Foam { get; }

        public bool ClosedEast => (Open & OpenEast) == 0;

        public bool ClosedNorth => (Open & OpenNorth) == 0;

        public bool ClosedWest => (Open & OpenWest) == 0;

        public bool ClosedSouth => (Open & OpenSouth) == 0;

        public float Floor { get; }

        public float FallTop { get; }

        public float FootTop { get; }

        public bool OverPool { get; }

        // The same with water falling in it from this height.
        public Column Falling(float fallTop) => new(Top, Depth, Row, Drawn, Foam, Open, Floor, fallTop, FootTop, OverPool);

        // The same over a pool at a ledge's foot (that pool's top), water falling into it from this height.
        public Column Over(Column foot, float fallTop) => new(Top, Depth, Row, Drawn, Foam, Open, Floor, fallTop, foot.Top, true);
    }

    // A voxel of a run of wet voxels.
    private readonly struct Wet
    {
        public Wet(BrickView brick, int index, int row)
        {
            Brick = brick;
            Index = index;
            Row = row;
        }

        public BrickView Brick { get; }

        public int Index { get; }

        public int Row { get; }

        public float Water => Brick.Water[Index];

        public float Solid => Brick.Solid[Index];
    }

    // The column (x, z in its bricks) of a stack of bricks, highest first, with the ground cast under it (its mean and
    // its lowest; NaN where not all cast yet): its highest run of wet voxels, where it rests, and whether any water lies
    // under it (another layer); where the card draws falls, the water falling in it and the pool under it all.
    private static Column Look(List<BrickView> stack, int x, int z, float voxelSize, float ground, float lowest, bool falls)
    {
        List<List<Wet>> runs = Runs(stack, x, z);
        if (runs.Count == 0)
        {
            return Column.None;
        }

        List<Wet> run = runs[0];
        bool under = runs.Count > 1;
        Wet low = run.Last();
        float depth = Water(run, run.Count, voxelSize);

        // The run rests where its lowest voxel has ground in it, or no water could leave it downward (a solid voxel or a
        // piece under it); else it hangs over the air: running down a step when the ground cast under it is near, else
        // falling (from its top).
        if (!Rests(low))
        {
            // Not where the ground cast is over the water's bottom everywhere: a roof, or a wall (ground not known). How far
            // it falls goes by the lowest ground under it: a column half over a ledge's top and half beside it falls.
            float bottom = low.Row * voxelSize;
            if (float.IsNaN(ground) || lowest > bottom)
            {
                return Column.None;
            }

            if (bottom - lowest <= StepDrop)
            {
                return Of(run[0], ground + depth, depth, !under, low.Brick.VelocityY[low.Index]);
            }

            if (!falls)
            {
                return Column.None;
            }

            // Falling: the card throws it from its top, over the pool it falls into (the lowest run, resting), if any.
            float fallTop = TopOf(run[0], voxelSize);
            List<Wet> pool = runs.Last();
            return under && Rests(pool.Last())
                ? Pool(pool, voxelSize, true).Falling(fallTop)
                : new Column(DryTop, 0f, int.MinValue, true, 0f, BrickData.AllOpen, fallTop: fallTop);
        }

        // Its top: up from the lowest voxel while they are full, to the first that is not. Water over that one spills
        // into it over a step (a few voxels: the card draws it, as deep as all the water on the run's floor), or falls
        // into it (more: the bricks draw it, the card keeping the water resting there; or, where the card draws falls,
        // it throws it from the run's top and keeps the pool).
        int top = PoolTop(run);
        float floor = (low.Row + low.Solid) * voxelSize;
        Column here;
        if (top is > 0 and <= StepVoxels)
        {
            here = Of(run[top], floor + depth, depth, !under, run[0].Brick.VelocityY[run[0].Index], floor);
        }
        else if (top > 0 && falls)
        {
            here = Pool(run, voxelSize, !under).Falling(TopOf(run[0], voxelSize));
        }
        else
        {
            // The pool's water only (a fall into it left to the bricks): the falling water is not kept on the card's
            // ground there, where it would stand as a mound at the fall's foot.
            Wet at = run[top];
            float fill = Math.Min(1f, at.Solid + at.Water);
            here = Of(at, (at.Row + fill) * voxelSize, Water(run, run.Count - top, voxelSize), !under && top == 0, at.Brick.VelocityY[at.Index], floor);
        }

        if (!falls || !under)
        {
            return here;
        }

        // Water under it: at the foot of a ledge, the pool there (the lowest run, resting) and water falling into it (all
        // the runs between, hanging). The card draws them all if a ledge is found across the column (its ground).
        List<Wet> bottomRun = runs.Last();
        if (!Rests(bottomRun.Last()))
        {
            return here;
        }

        for (int r = 1; r < runs.Count - 1; r++)
        {
            if (Rests(runs[r].Last()))
            {
                return here;
            }
        }

        // Falling: from the highest water under this run, if it is not all the foot's pool; and this run's own falls.
        float falling = runs.Count > 2 || PoolTop(bottomRun) > 0 ? TopOf(runs[1][0], voxelSize) : float.NaN;
        falling = float.IsNaN(here.FallTop) ? falling : float.IsNaN(falling) ? here.FallTop : Math.Max(falling, here.FallTop);
        return here.Over(Pool(bottomRun, voxelSize, true), falling);
    }

    // The runs of wet voxels in a column (x, z in its bricks) of a stack of bricks, highest first, each top down.
    private static List<List<Wet>> Runs(List<BrickView> stack, int x, int z)
    {
        var runs = new List<List<Wet>>();
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

                if (runs.Count == 0 || row != previous - 1)
                {
                    runs.Add([]);
                }

                runs.Last().Add(new Wet(brick, i, row));
                previous = row;
            }
        }

        return runs;
    }

    // Whether water in this voxel rests: it has ground in it, or no water could leave it downward.
    private static bool Rests(Wet voxel) => voxel.Solid > 0f || (voxel.Brick.Open[voxel.Index] & OpenDown) == 0;

    // The run's pool: up from its lowest voxel while they are full, to the first that is not (its index in the run).
    private static int PoolTop(List<Wet> run)
    {
        int top = run.Count - 1;
        while (top > 0 && run[top].Solid + run[top].Water >= Full)
        {
            top--;
        }

        return top;
    }

    // The water of a run's lowest voxels (this many, from the bottom), metres over its column.
    private static float Water(List<Wet> run, int count, float voxelSize)
    {
        float depth = 0f;
        for (int k = run.Count - count; k < run.Count; k++)
        {
            depth += run[k].Water * voxelSize;
        }

        return depth;
    }

    // The top of the water in a voxel (filled from its floor).
    private static float TopOf(Wet voxel, float voxelSize) => (voxel.Row + Math.Min(1f, voxel.Solid + voxel.Water)) * voxelSize;

    // A run's pool alone (the water over it falls), drawn by the card or not.
    private static Column Pool(List<Wet> run, float voxelSize, bool drawn)
    {
        int top = PoolTop(run);
        Wet at = run[top], low = run.Last();
        return Of(at, TopOf(at, voxelSize), Water(run, run.Count - top, voxelSize), drawn, at.Brick.VelocityY[at.Index], (low.Row + low.Solid) * voxelSize);
    }

    // Whether a piece closes the face between two columns at the water's level: one says its face is closed there, and
    // the ground cast beside it is under that voxel's top (the face is closed for water at that height only: closed
    // onto solid ground, as each step of a slope's ground is, the card's ground holds the water already, and a wall
    // there at every height would dam the water running down the slope at each voxel).
    private static bool Shut(bool closedHere, Column here, float groundThere, float voxelSize, bool closedThere, Column there, float groundHere) =>
        (closedHere && !(groundThere >= ((here.Row + 1) * voxelSize) - 0.02f)) || (closedThere && !(groundHere >= ((there.Row + 1) * voxelSize) - 0.02f));

    // A column whose water's top is at this level, this deep, its faces closed as this voxel's, foaming as fast as it
    // falls, resting on this floor.
    private static Column Of(Wet voxel, float level, float depth, bool drawn, float rising, float floor = float.NaN)
    {
        float foam = Math.Min(1f, Math.Max(0f, -rising / SurfaceGrid.FoamFall));
        return new Column(level, depth, voxel.Row, drawn, foam, voxel.Brick.Open[voxel.Index], floor);
    }

    // The mean ground cast under a column's cells, and the lowest; NaN while any is not cast.
    private static float Ground(float[] bed, int cells, int perVoxel, int vx, int vz, out float lowest)
    {
        float sum = 0f;
        lowest = float.PositiveInfinity;
        for (int j = 0; j < perVoxel; j++)
        {
            for (int i = 0; i < perVoxel; i++)
            {
                float ground = bed[(((vz * perVoxel) + j) * cells) + (vx * perVoxel) + i];
                sum += ground;
                lowest = float.IsNaN(ground) ? float.NaN : Math.Min(lowest, ground);
            }
        }

        return sum / (perVoxel * perVoxel);
    }

    // How many of a column's cells have ground cast under this height (none while any is not cast).
    private static int CountUnder(float[] bed, int cells, int perVoxel, int vx, int vz, float height)
    {
        int under = 0;
        for (int j = 0; j < perVoxel; j++)
        {
            for (int i = 0; i < perVoxel; i++)
            {
                float ground = bed[(((vz * perVoxel) + j) * cells) + (vx * perVoxel) + i];
                if (float.IsNaN(ground))
                {
                    return 0;
                }

                under += ground < height ? 1 : 0;
            }
        }

        return under;
    }

    // The mean depth of a column's cells under their own level (at a ledge's foot, under the split: the foot's), over
    // those with one; 0 where none has.
    private static float MeanDepth(float[] bed, int cells, int perVoxel, int vx, int vz, float split, float top, float footTop)
    {
        float sum = 0f;
        int counted = 0;
        for (int j = 0; j < perVoxel; j++)
        {
            for (int i = 0; i < perVoxel; i++)
            {
                float ground = bed[(((vz * perVoxel) + j) * cells) + (vx * perVoxel) + i];
                float level = ground < split ? footTop : top;
                if (level > DryTop)
                {
                    sum += Math.Max(0f, level - ground);
                    counted++;
                }
            }
        }

        return counted > 0 ? sum / counted : 0f;
    }

    // Whether the ground known under a column's cells (those not at a ledge's foot, under split) is under its water
    // somewhere: the card's water there stands on it. Not where any is not cast yet, nor where a roof or a floor just over
    // the water was cast instead (it covers them all). On a slope, the edge of a thin sheet has ground over its top in
    // the column's uphill cells: still the card's.
    private static bool GroundUnder(float[] bed, int cells, int perVoxel, int vx, int vz, float top, float split)
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

                under += ground <= top && ground >= split ? 1 : 0;
            }
        }

        return under > 0;
    }

    // The height each column's ground is cast from: over its own water (resting or falling) and the water beside it, no
    // more than CastOverNear over its own (so a ledge's top beside a pool at its foot is cast from over it, not from
    // inside the ledge); else over the highest water within CastReach (a square round it), else none.
    private static float[] Cast(Column[] found, int columns, float voxelSize)
    {
        int reach = Math.Max(1, (int)Math.Round(CastReach / voxelSize));
        float[] highest = new float[found.Length];
        for (int k = 0; k < found.Length; k++)
        {
            highest[k] = float.IsNaN(found[k].FallTop) ? found[k].Top : Math.Max(found[k].Top, found[k].FallTop);
        }

        // The highest top within reach along each row, then along each column of that.
        float[] rows = new float[found.Length];
        for (int vz = 0; vz < columns; vz++)
        {
            for (int vx = 0; vx < columns; vx++)
            {
                float most = DryTop;
                for (int dx = Math.Max(0, vx - reach); dx <= Math.Min(columns - 1, vx + reach); dx++)
                {
                    most = Math.Max(most, highest[(vz * columns) + dx]);
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
                if (highest[k] > DryTop)
                {
                    float beside = highest[k];
                    for (int dz = Math.Max(0, vz - 1); dz <= Math.Min(columns - 1, vz + 1); dz++)
                    {
                        for (int dx = Math.Max(0, vx - 1); dx <= Math.Min(columns - 1, vx + 1); dx++)
                        {
                            beside = Math.Max(beside, highest[(dz * columns) + dx]);
                        }
                    }

                    cast[k] = Math.Min(beside, highest[k] + CastOverNear) + CastOverWater;
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
