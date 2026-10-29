using System;

namespace FlowingWater.Client;

/// <summary>
/// How the volume water's grid follows the camera (GpuWater): how tall it is, where it sits up and down,
/// when it moves up or down after the camera, how wide the band along its sides is that takes the server's water each
/// step, and how wide a strip a move of the square reseeds. Needs no game, so the unit tests walk a camera through it.
/// </summary>
internal static class VolumeFollow
{
    /// <summary>The band along the square's sides that takes the server's water each step, metres.</summary>
    public const float BandMetres = 2f;

    /// <summary>Rows at the least, whatever the height asked for.</summary>
    public const int LeastRows = 8;

    /// <summary>
    /// Cells the grid may have at most: the card runs a kernel over 65,535 groups of 256 threads at once, and the
    /// drawing's density has eight half-cells a cell.
    /// </summary>
    public const int MostCells = 65535 * 256 / 8;

    /// <summary>
    /// The rows a square of <paramref name="cells"/> a side may have of the <paramref name="rows"/> asked, within
    /// <see cref="MostCells"/>: fewer than <see cref="LeastRows"/> means the square alone is too wide for the card.
    /// </summary>
    public static int RowsWithin(int rows, int cells) => Math.Min(rows, MostCells / Math.Max(1, cells * cells));

    /// <summary>The grid's rows for a height in metres at a cell size: the nearest count, <see cref="LeastRows"/> at least.</summary>
    public static int Rows(float heightMetres, float cellSize) => Math.Max(LeastRows, (int)Math.Round(heightMetres / cellSize));

    /// <summary>
    /// The share of the grid's height that stands above the camera: more than half, since water comes to a player
    /// from above more than from below (a fall is looked up at from its foot) and the ground under the feet soon ends
    /// the water below.
    /// </summary>
    public const float Above = 0.7f;

    /// <summary>The grid's bottom (world y) that sets the camera <see cref="Above"/> of the way down from its top, on a cell boundary.</summary>
    public static float BaseY(float cameraY, int rows, float cellSize) => (float)Math.Floor((cameraY - (rows * cellSize * (1f - Above))) / cellSize) * cellSize;

    /// <summary>
    /// Whether a grid whose bottom is <paramref name="originY"/> must move after the camera: it does once the camera
    /// has left the middle half of the grid's play (a quarter of its height above or below where <see cref="BaseY"/>
    /// put it), so a camera bobbing about never moves it.
    /// </summary>
    public static bool Rebase(float cameraY, float originY, int rows, float cellSize)
    {
        float height = rows * cellSize;
        float rest = originY + (height * (1f - Above));
        return Math.Abs(cameraY - rest) > height / 4f;
    }

    /// <summary>
    /// The rows of <paramref name="cellSize"/> the grid may have so that, with the solids' texture of
    /// <paramref name="solidRows"/> voxel rows of <paramref name="voxelSize"/>, the texture covers it with room to move
    /// a quarter of its height either way before it must be built again (what was built would be ignored past its top).
    /// </summary>
    public static int RowsWithinSolids(int rows, float cellSize, float voxelSize, int solidRows) =>
        Math.Min(rows, (int)Math.Floor(solidRows * voxelSize / (1.5f * cellSize)));

    /// <summary>
    /// The voxel row the solids' texture of <paramref name="solidRows"/> rows starts at for a grid from voxel row
    /// <paramref name="bottom"/> to <paramref name="top"/> (excluded): the grid in its middle, its spare rows shared
    /// above and below.
    /// </summary>
    public static int SolidsBase(int bottom, int top, int solidRows) => bottom - Math.Max(0, (solidRows - (top - bottom)) / 2);

    /// <summary>Whether a solids' texture built from voxel row <paramref name="built"/> up <paramref name="solidRows"/> rows covers a grid from <paramref name="bottom"/> to <paramref name="top"/> (excluded).</summary>
    public static bool SolidsCover(int built, int bottom, int top, int solidRows) => bottom >= built && top <= built + solidRows;

    /// <summary>Cells of the edge band for a cell size: <see cref="BandMetres"/> whatever the size, rounded up.</summary>
    public static int Band(float cellSize) => (int)Math.Ceiling(BandMetres / cellSize);

    /// <summary>
    /// Cells of the square's sides to seed from the server's water when the square moved <paramref name="dx"/>
    /// voxels east and <paramref name="dz"/> north (of <paramref name="perVoxel"/> cells each): the strip the move
    /// uncovered, on every side (the band is the same all round; the old sides hold water already).
    /// </summary>
    public static int Reseed(int dx, int dz, int perVoxel) => Math.Max(Math.Abs(dx), Math.Abs(dz)) * perVoxel;

    /// <summary>Of the room for particles, the share the server's water may take (the rest for what sources pour, falls, waves).</summary>
    public const float DeepShare = 0.75f;

    /// <summary>Metres of deep water the volume moves at the least, however full it would be.</summary>
    public const float LeastKeep = 1f;

    /// <summary>
    /// The depth kept is let be while the water it holds stays within this share of the budget, either way (the floor
    /// under deep water does not move with every change of the server's water; what it holds over the budget fits in
    /// the room left over); and with none kept, all of it moves until it takes more than the budget, then again once it
    /// takes this share less.
    /// </summary>
    public const float KeepSlack = 0.2f;

    /// <summary>
    /// How much of the server's deep water the volume moves (its top so many metres; under them the server's water is
    /// the volume's floor), so that the water in its grid fits <paramref name="budget"/> cubic metres: 0 where all of it
    /// fits (no such floor). The columns' tops and depths are the server's (a column with no water has a depth of 0),
    /// <paramref name="area"/> square metres each; only the water between the grid's bottom and top counts. The depth
    /// kept is the most that fits, at least <see cref="LeastKeep"/>; the one kept now (<paramref name="current"/>, 0:
    /// none) is let be within <see cref="KeepSlack"/>.
    /// </summary>
    public static float DeepKeep(float[] tops, float[] depths, float area, float gridBottom, float gridTop, float budget, float current)
    {
        if (tops.Length != depths.Length)
        {
            throw new ArgumentException($"{tops.Length} tops and {depths.Length} depths", nameof(depths));
        }

        float deepest = 0f;
        for (int i = 0; i < tops.Length; i++)
        {
            deepest = Math.Max(deepest, InGrid(tops[i], depths[i], gridBottom, gridTop));
        }

        float all = Held(tops, depths, area, gridBottom, gridTop, deepest);
        if (current > 0f)
        {
            float held = Held(tops, depths, area, gridBottom, gridTop, current);
            if (all <= budget * (1f - KeepSlack))
            {
                return 0f;
            }

            if (held >= budget * (1f - KeepSlack) && held <= budget * (1f + KeepSlack))
            {
                return current;
            }
        }

        if (all <= budget)
        {
            return 0f;
        }

        // The deepest kept that fits, by halves (the water held grows with the depth kept).
        float low = LeastKeep, high = deepest;
        if (Held(tops, depths, area, gridBottom, gridTop, low) > budget)
        {
            return LeastKeep;
        }

        for (int k = 0; k < 24; k++)
        {
            float middle = 0.5f * (low + high);
            if (Held(tops, depths, area, gridBottom, gridTop, middle) <= budget)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    // The water the grid holds with this much kept of each column's: from its top down so far, within the grid.
    private static float Held(float[] tops, float[] depths, float area, float gridBottom, float gridTop, float keep)
    {
        float held = 0f;
        for (int i = 0; i < tops.Length; i++)
        {
            held += Math.Min(InGrid(tops[i], depths[i], gridBottom, gridTop), keep);
        }

        return held * area;
    }

    // How deep a column's water is within the grid (0: none there).
    private static float InGrid(float top, float depth, float gridBottom, float gridTop) =>
        depth > 0f ? Math.Max(0f, Math.Min(top, gridTop) - Math.Max(top - depth, gridBottom)) : 0f;
}
