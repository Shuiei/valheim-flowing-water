namespace FlowingWater.Client;

/// <summary>
/// How what is built is laid out for the water on the card (the particle and volume water's solids texture): a byte a voxel over the
/// card's square, <see cref="Rows"/> rows up from a base row, <see cref="Solid"/> where the voxel is solid and a bit for
/// each of its faces a piece closes. Needs nothing, so the unit tests and the Unity project's preview use it as is.
/// </summary>
internal static class SolidsLayout
{
    /// <summary>
    /// Rows of voxels the texture holds: so many metres of height at the voxel's size (128 m at the server's default 1 m
    /// voxels, 64 m at 0.5 m), round the camera or the volume water's grid (which is cut to what it covers).
    /// </summary>
    public const int Rows = 128;

    /// <summary>The byte's bit for a solid voxel; the six below it close its east, west, up, down, north and south faces.</summary>
    public const byte Solid = 128;

    /// <inheritdoc cref="Solid"/>
    public const byte ClosedEast = 1, ClosedWest = 2, ClosedUp = 4, ClosedDown = 8, ClosedNorth = 16, ClosedSouth = 32;

    /// <summary>
    /// The index of a voxel in the bytes, as a 3D texture of columns wide, <see cref="Rows"/> high and columns deep lays
    /// them out: x fastest, then the row, then z.
    /// </summary>
    public static int Index(int columns, int x, int y, int z) => (((z * Rows) + y) * columns) + x;
}
