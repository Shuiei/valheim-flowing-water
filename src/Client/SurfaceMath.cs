namespace FlowingWater.Client;

/// <summary>
/// How the water's drawn surface is shaped where tiles meet, without the game: which tile a vertex belongs to,
/// the surface's slope from the vertices either side, the normal and tangent that come from it, and a hash of
/// a tile's edge to tell when it changed.
/// </summary>
internal static class SurfaceMath
{
    /// <summary>
    /// Which tile a vertex of a tile's grid belongs to, and where it lies in it: a tile owns its vertices 0 to
    /// size - 1 each way, so its last row and column (size) are the next tile's first (0), and -1 the last cell
    /// of the tile before.
    /// </summary>
    /// <param name="size">Cells along a tile's side.</param>
    /// <param name="x">The vertex along x, in this tile's grid (-1 to size).</param>
    /// <param name="z">The vertex along z.</param>
    /// <returns>The owning tile's offset in zones, and the vertex in its grid.</returns>
    public static (int ZoneX, int ZoneZ, int X, int Z) Owner(int size, int x, int z)
    {
        (int zoneX, int ox) = Shift(size, x);
        (int zoneZ, int oz) = Shift(size, z);
        return (zoneX, zoneZ, ox, oz);
    }

    /// <summary>
    /// The surface's slope (rise over run) through a vertex in one direction: from both sides where both are
    /// drawn, else from the one that is; flat where neither is.
    /// </summary>
    /// <param name="y">The vertex's height.</param>
    /// <param name="ahead">Whether the vertex ahead is drawn.</param>
    /// <param name="yAhead">Its height.</param>
    /// <param name="behind">Whether the vertex behind is drawn.</param>
    /// <param name="yBehind">Its height.</param>
    /// <param name="cell">The distance between vertices, metres.</param>
    public static float Gradient(float y, bool ahead, float yAhead, bool behind, float yBehind, float cell) =>
        ahead && behind ? (yAhead - yBehind) / (2f * cell)
        : ahead ? (yAhead - y) / cell
        : behind ? (y - yBehind) / cell
        : 0f;

    /// <summary>
    /// The surface's normal (unit, up) and tangent at a vertex from its slopes, for texture coordinates that
    /// run with the world (u along x, v along z): the tangent along x, its sign -1 so that the bitangent
    /// (cross(normal, tangent) times it) runs along +z.
    /// </summary>
    /// <param name="slopeX">Rise over run along x.</param>
    /// <param name="slopeZ">Rise over run along z.</param>
    public static ((float X, float Y, float Z) Normal, (float X, float Y, float Z, float W) Tangent) Frame(float slopeX, float slopeZ)
    {
        var normal = Unit(-slopeX, 1f, -slopeZ);
        (float x, float y, float z) = Unit(1f, slopeX, 0f);
        return (normal, (x, y, z, -1f));
    }

    /// <summary>
    /// The tangent for texture coordinates laid along a flow (the game's stream material: u across the flow,
    /// v downstream): u's direction across the flow (z, 0, -x) laid onto the surface, its sign -1 so that the
    /// bitangent runs downstream.
    /// </summary>
    /// <param name="normal">The surface's normal (unit).</param>
    /// <param name="flowX">The flow's direction along x (unit, with flowZ).</param>
    /// <param name="flowZ">Along z.</param>
    public static (float X, float Y, float Z, float W) FlowTangent((float X, float Y, float Z) normal, float flowX, float flowZ)
    {
        float along = (flowZ * normal.X) - (flowX * normal.Z);
        (float x, float y, float z) = Unit(flowZ - (along * normal.X), -(along * normal.Y), -flowX - (along * normal.Z));
        return (x, y, z, -1f);
    }

    /// <summary>
    /// A hash of a tile's edge (its two outer rows and columns each side, what the tiles around read from it):
    /// each vertex's height where drawn, or that it is not.
    /// </summary>
    /// <param name="size">Cells along the tile's side (vertices: one more).</param>
    /// <param name="heights">The vertices' heights, row by row.</param>
    /// <param name="drawn">Each vertex's place in the mesh, negative where not drawn.</param>
    public static int EdgeHash(int size, float[] heights, int[] drawn)
    {
        int pitch = size + 1;
        int hash = 17;
        for (int z = 0; z < pitch; z++)
        {
            for (int x = 0; x < pitch; x++)
            {
                if (x > 1 && z > 1 && x < size - 1 && z < size - 1)
                {
                    continue;
                }

                int i = (z * pitch) + x;
                hash = unchecked((hash * 31) + (drawn[i] >= 0 ? heights[i].GetHashCode() : -1));
            }
        }

        return hash;
    }

    /// <summary>A tile's vertices before its first rebuild: none drawn (a neighbour may read them before).</summary>
    /// <param name="vertices">How many.</param>
    public static int[] NothingDrawn(int vertices)
    {
        int[] drawn = new int[vertices];
        for (int i = 0; i < vertices; i++)
        {
            drawn[i] = -1;
        }

        return drawn;
    }

    private static (int Zone, int Index) Shift(int size, int v) => v >= size ? (1, v - size) : v < 0 ? (-1, v + size) : (0, v);

    private static (float X, float Y, float Z) Unit(float x, float y, float z)
    {
        float length = (float)System.Math.Sqrt((x * x) + (y * y) + (z * z));
        return (x / length, y / length, z / length);
    }
}
