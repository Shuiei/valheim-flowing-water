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

    /// <summary>The grid's bottom (world y) that centres it on the camera, on a cell boundary.</summary>
    public static float BaseY(float cameraY, int rows, float cellSize) => (float)Math.Floor((cameraY - (rows * cellSize / 2f)) / cellSize) * cellSize;

    /// <summary>
    /// Whether a grid whose bottom is <paramref name="originY"/> must move after the camera: it does once the camera
    /// has left the grid's middle half (a quarter of its height above or below its middle), so a camera bobbing about
    /// the middle never moves it.
    /// </summary>
    public static bool Rebase(float cameraY, float originY, int rows, float cellSize)
    {
        float height = rows * cellSize;
        float middle = originY + (height / 2f);
        return Math.Abs(cameraY - middle) > height / 4f;
    }

    /// <summary>Cells of the edge band for a cell size: <see cref="BandMetres"/> whatever the size, rounded up.</summary>
    public static int Band(float cellSize) => (int)Math.Ceiling(BandMetres / cellSize);

    /// <summary>
    /// Cells of the square's sides to seed from the server's water when the square moved <paramref name="dx"/>
    /// voxels east and <paramref name="dz"/> north (of <paramref name="perVoxel"/> cells each): the strip the move
    /// uncovered, on every side (the band is the same all round; the old sides hold water already).
    /// </summary>
    public static int Reseed(int dx, int dz, int perVoxel) => Math.Max(Math.Abs(dx), Math.Abs(dz)) * perVoxel;
}
