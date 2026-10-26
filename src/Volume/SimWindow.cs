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

    public bool Equals(SimWindow other) =>
        X0 == other.X0 && Z0 == other.Z0 && Columns == other.Columns && PerVoxel == other.PerVoxel && VoxelSize.Equals(other.VoxelSize);

    public override bool Equals(object obj) => obj is SimWindow other && Equals(other);

    public override int GetHashCode() => (((((X0 * 397) ^ Z0) * 397) ^ Columns) * 397) ^ (PerVoxel * 31) ^ VoxelSize.GetHashCode();

    private static int Snap(int column) => (int)Math.Floor(column / (double)Group) * Group;
}
