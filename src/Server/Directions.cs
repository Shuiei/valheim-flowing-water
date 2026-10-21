namespace FlowingWater.Server;

/// <summary>The eight neighbours of a cell: east, west, north, south, then the diagonals.</summary>
internal static class Directions
{
    public const int Count = 8;

    private const float Diagonal = 0.70710678f;

    public static readonly int[] Dx = [1, -1, 0, 0, 1, -1, 1, -1];

    public static readonly int[] Dz = [0, 0, 1, -1, 1, 1, -1, -1];

    /// <summary>The direction back from the neighbour.</summary>
    public static readonly int[] Opposite = [1, 0, 3, 2, 7, 6, 5, 4];

    /// <summary>Distance to the neighbour, metres.</summary>
    public static readonly float[] Length = [1f, 1f, 1f, 1f, 1.41421356f, 1.41421356f, 1.41421356f, 1.41421356f];

    public static readonly float[] UnitX = [1f, -1f, 0f, 0f, Diagonal, -Diagonal, Diagonal, -Diagonal];

    public static readonly float[] UnitZ = [0f, 0f, 1f, -1f, Diagonal, Diagonal, -Diagonal, -Diagonal];
}
