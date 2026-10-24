using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowingWater.Volume;

/// <summary>
/// A brick's water surface as the graphics card draws it, without the game: for each of its columns, and one column
/// more east and north (from the bricks beside it, so that bricks meet), the top of the water there, the share of it
/// shown, its depth, its current along the ground and how fast it falls. The card raises a fixed grid of points to these
/// tops, between them as they are between the columns, so the surface is one smooth sheet however the water steps down
/// from voxel to voxel.
/// <para>
/// A column's top is the highest top of its water in the brick's own rows (a wet voxel whose water does not go on up
/// into the one over it); without one, the top nearest them within <see cref="Window"/> rows over or under it, so that
/// a sheet running down out of the brick, or up into it, is joined to the columns beside it. Each square between four
/// columns is drawn once: by the brick whose rows hold the top of its first column shown (south-west, south-east,
/// north-west, north-east), and whose grid covers the square (the squares whose south-west column is one of its own).
/// While the surface blends from one grid to the next, a square where the next shows no water at all is drawn as the
/// grid blended from drew it, so water drying up fades out instead of going at once.
/// </para>
/// </summary>
internal sealed class SurfaceGrid
{
    /// <summary>Columns along each side: the brick's and one more.</summary>
    public const int Side = Brick.Size + 1;

    /// <summary>Rows over and under the brick looked at for a column's top when it has none in the brick's own.</summary>
    public const int Window = VoxelSurface.MaxStep;

    /// <summary>Water thinner than this, metres, is not shown.</summary>
    public const float HideDepth = 0.01f;

    /// <summary>Water this deep, metres, is fully shown; thinner, it fades.</summary>
    public const float ShowDepth = 0.03f;

    /// <summary>Falling this fast, metres a second, the water is all foam.</summary>
    public const float FoamFall = 4f;

    /// <summary>
    /// The bricks whose grid reads a brick's water, from it: those west and south of it (their last columns are its
    /// first), one under it (a top up to <see cref="Window"/> rows over its own), and those over it as far as a column's
    /// depth reaches down from a top (<see cref="VoxelSurface.DeepestColumn"/> rows, from a top up to Window rows
    /// under the brick drawing it).
    /// </summary>
    public static IReadOnlyList<(int X, int Y, int Z)> Readers { get; } =
        [.. from dx in new[] { -1, 0 } from dz in new[] { -1, 0 } from dy in Enumerable.Range(-1, ((VoxelSurface.DeepestColumn + Window + Brick.Size - 1) / Brick.Size) + 2) where dx != 0 || dy != 0 || dz != 0 select (dx, dy, dz)];

    /// <summary>The grid of this brick.</summary>
    public SurfaceGrid(int x, int y, int z, float voxelSize)
    {
        X = x;
        Y = y;
        Z = z;
        VoxelSize = voxelSize;
    }

    /// <summary>The brick's coordinates, in bricks.</summary>
    public int X { get; }

    /// <summary>The brick's coordinates, in bricks.</summary>
    public int Y { get; }

    /// <summary>The brick's coordinates, in bricks.</summary>
    public int Z { get; }

    /// <summary>Metres along a voxel's side.</summary>
    public float VoxelSize { get; }

    /// <summary>Each column's top, metres over the brick's floor; where there is none, the mean of those beside it.</summary>
    public float[] Height { get; } = new float[Side * Side];

    /// <summary>Each column's water depth under its top, metres.</summary>
    public float[] Depth { get; } = new float[Side * Side];

    /// <summary>How much of each column's top is shown, 0 to 1 (0 where there is none, fading in as it thickens).</summary>
    public float[] Shown { get; } = new float[Side * Side];

    /// <summary>1 where the column's top is in the brick's own rows, else 0.</summary>
    public float[] Own { get; } = new float[Side * Side];

    /// <summary>Each column's current east, metres a second, at its top.</summary>
    public float[] CurrentX { get; } = new float[Side * Side];

    /// <summary>Each column's current north, metres a second, at its top.</summary>
    public float[] CurrentZ { get; } = new float[Side * Side];

    /// <summary>How much of each column's top is foam from falling, 0 to 1.</summary>
    public float[] Foam { get; } = new float[Side * Side];

    /// <summary>Whether the brick draws any square at all (a top of its own is shown somewhere).</summary>
    public bool Draws { get; private set; }

    /// <summary>The index of column (i, j) of the grid, i east and j north from the brick's south-west column.</summary>
    public static int Index(int i, int j) => (j * Side) + i;

    /// <summary>The grid of a brick, from the field's water round it.</summary>
    public static SurfaceGrid Build(VoxelField field, int x, int y, int z)
    {
        var grid = new SurfaceGrid(x, y, z, field.VoxelSize);
        int floor = y * Brick.Size;
        bool[] found = new bool[Side * Side];
        for (int j = 0; j < Side; j++)
        {
            for (int i = 0; i < Side; i++)
            {
                int vx = (x * Brick.Size) + i;
                int vz = (z * Brick.Size) + j;
                int k = Index(i, j);
                if (!Top(field, vx, vz, floor, out int top, out bool own))
                {
                    continue;
                }

                found[k] = true;
                float level = field.Level(vx, top, vz);
                float depth = level - field.Floor(vx, Bottom(field, vx, top, vz), vz);
                grid.Height[k] = level - (floor * field.VoxelSize);
                grid.Depth[k] = depth;
                grid.Shown[k] = Clamp((depth - HideDepth) / (ShowDepth - HideDepth));
                grid.Own[k] = own ? 1f : 0f;
                if (field.Find(vx >> 4, top >> 4, vz >> 4) is { } view)
                {
                    int v = Brick.Index(vx & 15, top & 15, vz & 15);
                    grid.CurrentX[k] = view.VelocityX[v];
                    grid.CurrentZ[k] = view.VelocityZ[v];
                    grid.Foam[k] = Clamp(-view.VelocityY[v] / FoamFall);
                }
            }
        }

        Spread(grid, found);
        for (int j = 0; j < Brick.Size && !grid.Draws; j++)
        {
            for (int i = 0; i < Brick.Size && !grid.Draws; i++)
            {
                grid.Draws = grid.DrawsSquare(i, j);
            }
        }

        return grid;
    }

    /// <summary>
    /// Whether the square whose south-west column is (i, j) is drawn by this brick: its first column shown (south-west,
    /// south-east, north-west, north-east) has its top in the brick's own rows. The graphics card asks the same.
    /// </summary>
    public bool DrawsSquare(int i, int j) => Owns(i, j) ?? false;

    /// <summary>
    /// Whether the square whose south-west column is (i, j) is drawn by this brick while its surface blends from one
    /// grid to the next: as the next draws it, or where the next shows no water at any of its corners, as the grid
    /// blended from drew it (the water drying up there fades out). The graphics card asks the same.
    /// </summary>
    public static bool DrawsSquare(SurfaceGrid from, SurfaceGrid to, int i, int j) => to.Owns(i, j) ?? from?.Owns(i, j) ?? false;

    /// <summary>Whether the brick draws any square at all while its surface blends from one grid to the next.</summary>
    public static bool DrawsAny(SurfaceGrid from, SurfaceGrid to)
    {
        for (int j = 0; j < Brick.Size; j++)
        {
            for (int i = 0; i < Brick.Size; i++)
            {
                if (DrawsSquare(from, to, i, j))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>
    /// The surface as it is drawn part way from one grid to the next, as the graphics card blends them: each column's
    /// values that share of the way; the next grid's say on whose each column's top is, or where it shows none there, the
    /// grid blended from's. New data coming part way, the grid drawn then is where the next blend starts from, so the
    /// water never jumps.
    /// </summary>
    /// <param name="from">The grid blended from; none, the next grid as it is.</param>
    /// <param name="to">The grid blended to.</param>
    /// <param name="share">How far, 0 to 1.</param>
    public static SurfaceGrid Blend(SurfaceGrid from, SurfaceGrid to, float share)
    {
        if (from == null || share >= 1f)
        {
            return to;
        }

        var blend = new SurfaceGrid(to.X, to.Y, to.Z, to.VoxelSize) { Draws = from.Draws || to.Draws };
        float t = Clamp(share);
        Lerp(from.Height, to.Height, blend.Height, t);
        Lerp(from.Depth, to.Depth, blend.Depth, t);
        Lerp(from.Shown, to.Shown, blend.Shown, t);
        Lerp(from.CurrentX, to.CurrentX, blend.CurrentX, t);
        Lerp(from.CurrentZ, to.CurrentZ, blend.CurrentZ, t);
        Lerp(from.Foam, to.Foam, blend.Foam, t);
        for (int k = 0; k < blend.Own.Length; k++)
        {
            blend.Own[k] = to.Shown[k] > 0f ? to.Own[k] : from.Own[k];
        }

        return blend;
    }

    private static void Lerp(float[] a, float[] b, float[] into, float t)
    {
        for (int k = 0; k < into.Length; k++)
        {
            into[k] = a[k] + ((b[k] - a[k]) * t);
        }
    }

    private static readonly (int I, int J)[] Corners = [(0, 0), (1, 0), (0, 1), (1, 1)];

    // The brick's say on a square: whether it draws it, by its first corner shown; none shown, none.
    private bool? Owns(int i, int j)
    {
        foreach ((int di, int dj) in Corners)
        {
            int k = Index(i + di, j + dj);
            if (Shown[k] > 0f)
            {
                return Own[k] > 0f;
            }
        }

        return null;
    }

    private static float Clamp(float value) => Math.Max(0f, Math.Min(1f, value));

    // The column's top: the highest in the brick's rows, else the nearest within Window rows over or under them.
    private static bool Top(VoxelField field, int x, int z, int floor, out int top, out bool own)
    {
        int ceiling = floor + Brick.Size - 1;
        top = 0;
        own = false;
        int nearest = int.MaxValue;
        for (int y = ceiling + Window; y >= floor - Window; y--)
        {
            if (!field.IsWet(x, y, z) || field.GoesUp(x, y, z))
            {
                continue;
            }

            if (y >= floor && y <= ceiling)
            {
                top = y;
                own = true;
                return true;
            }

            int away = y > ceiling ? y - ceiling : floor - y;
            if (away < nearest)
            {
                nearest = away;
                top = y;
            }
        }

        return nearest != int.MaxValue;
    }

    // The lowest voxel the top's water goes down to: the first without water under it, at most DeepestColumn down.
    private static int Bottom(VoxelField field, int x, int top, int z)
    {
        int bottom = top;
        while (top - bottom < VoxelSurface.DeepestColumn && field.IsWet(x, bottom - 1, z) && field.GoesUp(x, bottom - 1, z))
        {
            bottom--;
        }

        return bottom;
    }

    // Columns with no top take the mean height of those beside them that have one (twice over, so a column two from
    // the water has a height too): the surface fades out over them level with the water, not down to the brick's floor.
    private static void Spread(SurfaceGrid grid, bool[] found)
    {
        for (int pass = 0; pass < 2; pass++)
        {
            bool[] next = (bool[])found.Clone();
            for (int j = 0; j < Side; j++)
            {
                for (int i = 0; i < Side; i++)
                {
                    int k = Index(i, j);
                    if (found[k])
                    {
                        continue;
                    }

                    float sum = 0f;
                    int count = 0;
                    for (int dj = -1; dj <= 1; dj++)
                    {
                        for (int di = -1; di <= 1; di++)
                        {
                            int ni = i + di;
                            int nj = j + dj;
                            if (ni >= 0 && nj >= 0 && ni < Side && nj < Side && found[Index(ni, nj)])
                            {
                                sum += grid.Height[Index(ni, nj)];
                                count++;
                            }
                        }
                    }

                    if (count > 0)
                    {
                        grid.Height[k] = sum / count;
                        next[k] = true;
                    }
                }
            }

            found = next;
        }
    }
}
