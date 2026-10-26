using System;

namespace FlowingWater.Volume;

/// <summary>
/// Where the water moved on the graphics card lies, without the game: a square of voxel columns round the camera, whole
/// groups of eight from the world's origin, cut into cells a few to a voxel's side. It moves once the camera is a
/// quarter of the square from its middle, so the camera always has as much water moved on the card all round.
/// </summary>
internal readonly struct SimWindow : IEquatable<SimWindow>
{
    /// <summary>Most cells along a side (the card's textures).</summary>
    public const int MostCells = 1024;

    // Columns line up in groups of this many (the card works on groups of eight cells).
    private const int Group = 8;

    // A voxel column with no water, ledge nor fall (SimColumns' texels and layers), for the edge a moved square is new to.
    private static readonly float[] NoTexel = [SimColumns.DryTop, 0f, 0f, 0f];

    private static readonly float[] NoLayers = [SimColumns.DryTop, SimColumns.DryTop, SimColumns.DryTop, 0f];

    private SimWindow(int x0, int z0, int columns, int perVoxel, float voxelSize)
    {
        X0 = x0;
        Z0 = z0;
        Columns = columns;
        PerVoxel = perVoxel;
        VoxelSize = voxelSize;
    }

    /// <summary>The voxel column at its south-west corner.</summary>
    public int X0 { get; }

    /// <inheritdoc cref="X0"/>
    public int Z0 { get; }

    /// <summary>Voxel columns along a side.</summary>
    public int Columns { get; }

    /// <summary>Cells along a voxel's side.</summary>
    public int PerVoxel { get; }

    /// <summary>Metres along a voxel's side.</summary>
    public float VoxelSize { get; }

    /// <summary>Cells along a side.</summary>
    public int Cells => Columns * PerVoxel;

    /// <summary>Metres along a cell's side.</summary>
    public float CellSize => VoxelSize / PerVoxel;

    /// <summary>Metres along a side.</summary>
    public float Size => Columns * VoxelSize;

    /// <summary>
    /// The square round world x, z: about <paramref name="range"/> metres a side, of cells about
    /// <paramref name="cellSize"/> metres (no larger than a voxel, a whole number to its side), at most
    /// <see cref="MostCells"/> along a side.
    /// </summary>
    public static SimWindow Around(float x, float z, float voxelSize, float range, float cellSize)
    {
        int perVoxel = Math.Max(1, (int)Math.Round(voxelSize / cellSize));
        int most = MostCells / perVoxel / Group * Group;
        int columns = Math.Min(most, Math.Max(Group, (int)Math.Round(range / voxelSize / Group) * Group));
        int x0 = Snap((int)Math.Floor(x / voxelSize) - (columns / 2));
        int z0 = Snap((int)Math.Floor(z / voxelSize) - (columns / 2));
        return new SimWindow(x0, z0, columns, perVoxel, voxelSize);
    }

    /// <summary>Whether the camera at world x, z is still near enough the middle (within a quarter of the square).</summary>
    public bool Keeps(float x, float z)
    {
        float quarter = Size / 4f;
        float middleX = (X0 + (Columns / 2f)) * VoxelSize;
        float middleZ = (Z0 + (Columns / 2f)) * VoxelSize;
        return Math.Abs(x - middleX) <= quarter && Math.Abs(z - middleZ) <= quarter;
    }

    /// <summary>
    /// How far, in voxel columns, this square is from <paramref name="other"/> when it can take over that one's water
    /// (cut the same, sharing some of it); false when it cannot (another size or voxels, or no column in common).
    /// </summary>
    public bool ShiftedFrom(SimWindow other, out int dx, out int dz)
    {
        dx = X0 - other.X0;
        dz = Z0 - other.Z0;
        return Columns == other.Columns && PerVoxel == other.PerVoxel && VoxelSize.Equals(other.VoxelSize)
            && Math.Abs(dx) < Columns && Math.Abs(dz) < Columns;
    }

    /// <summary>
    /// A grid of values (rows of <paramref name="width"/> places along x, each <paramref name="outside"/>.Length values)
    /// as seen from a square moved by <paramref name="dx"/>, <paramref name="dz"/> places: each place takes the value at
    /// its place plus the move, and those past the old grid's edge are <paramref name="outside"/>.
    /// </summary>
    public static T[] Shifted<T>(T[] values, int width, int dx, int dz, T[] outside)
    {
        int stride = outside.Length;
        int height = values.Length / (width * stride);
        var shifted = new T[values.Length];
        for (int z = 0; z < height; z++)
        {
            for (int x = 0; x < width; x++)
            {
                int fromX = x + dx, fromZ = z + dz;
                bool inside = fromX >= 0 && fromZ >= 0 && fromX < width && fromZ < height;
                int to = ((z * width) + x) * stride, from = ((fromZ * width) + fromX) * stride;
                for (int k = 0; k < stride; k++)
                {
                    shifted[to + k] = inside ? values[from + k] : outside[k];
                }
            }
        }

        return shifted;
    }

    /// <summary>
    /// The card's own record of its square, kept for this square, moved on by <paramref name="dx"/>, <paramref name="dz"/>
    /// voxel columns from the last: the ground found under each cell (NaN where the move brings cells not cast yet), and
    /// for each row of cells the heights its voxel columns were cast from (NaN: never) and whether it is cast first,
    /// shifted in place; the server's water as last built for each column (texels and layers, as SimColumns gives
    /// them; null while none was), shifted anew, the new edge's columns holding none.
    /// </summary>
    public void Move(int dx, int dz, float[] ground, float[] castUsed, bool[] castFirst, ref float[] texels, ref float[] layers)
    {
        Array.Copy(Shifted(ground, Cells, dx * PerVoxel, dz * PerVoxel, [float.NaN]), ground, ground.Length);
        Array.Copy(Shifted(castUsed, Columns, dx, dz * PerVoxel, [float.NaN]), castUsed, castUsed.Length);
        Array.Copy(Shifted(castFirst, 1, 0, dz * PerVoxel, [false]), castFirst, castFirst.Length);
        if (texels != null)
        {
            texels = Shifted(texels, Columns, dx, dz, NoTexel);
            layers = Shifted(layers, Columns, dx, dz, NoLayers);
        }
    }

    public bool Equals(SimWindow other) =>
        X0 == other.X0 && Z0 == other.Z0 && Columns == other.Columns && PerVoxel == other.PerVoxel && VoxelSize.Equals(other.VoxelSize);

    public override bool Equals(object obj) => obj is SimWindow other && Equals(other);

    public override int GetHashCode() => (((((X0 * 397) ^ Z0) * 397) ^ Columns) * 397) ^ (PerVoxel * 31) ^ VoxelSize.GetHashCode();

    private static int Snap(int column) => (int)Math.Floor(column / (double)Group) * Group;
}
