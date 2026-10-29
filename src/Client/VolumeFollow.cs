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
}
