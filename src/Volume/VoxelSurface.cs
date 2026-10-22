using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// The water's surface over a brick, as triangles, without the game. A wet voxel with no water over it has a top,
/// its corners shared with the tops beside it at the mean of their levels (a pool lies flat, a stream slopes
/// smoothly); a wet voxel beside one with no water has a side from its water's floor to its top (a fall's sheet, the
/// side of a stream leaving a ledge). Each vertex carries the depth of the water under it, how far it is from a
/// shore (0 at the water's very edge) and the water's velocity.
/// </summary>
internal static class VoxelSurface
{
    private static readonly (int X, int Z)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>The most voxels down a column's depth is measured, under its top.</summary>
    public const int DeepestColumn = 64;

    // Bricks over one whose surface may read it: a column's depth is measured down DeepestColumn voxels from its top,
    // which may be a row over the brick it is in.
    private const int Above = (DeepestColumn / Brick.Size) + 1;

    /// <summary>The bricks round one whose surface may read it, as offsets in bricks: beside it, under it, and up to five over.</summary>
    public static IReadOnlyList<(int X, int Y, int Z)> Round { get; } = [.. Offsets()];

    /// <summary>
    /// The bricks round one (offsets in bricks) whose surface may change when its water goes from
    /// <paramref name="was"/> to <paramref name="now"/>: one beside it reads its columns facing it, one under it its
    /// bottom two layers (its top voxels are tops when no water goes on up through them), those over it up to five
    /// bricks up any column (the depth under their water is measured down through this one).
    /// All of them when there was none before.
    /// </summary>
    public static ICollection<(int X, int Y, int Z)> Touched(BrickView was, BrickView now)
    {
        if (was == null)
        {
            return [.. Round];
        }

        var touched = new HashSet<(int X, int Y, int Z)>();
        for (int i = 0; i < Brick.Count && touched.Count < Round.Count; i++)
        {
            if (!Same(was, now, i))
            {
                (int x, int y, int z) = Brick.Place(i);
                AddRound(touched, x, y, z);
            }
        }

        return touched;
    }

    // Whether a voxel's water and solid share are the same in both.
    private static bool Same(BrickView was, BrickView now, int i) =>
        was.Water[i].Equals(now.Water[i]) && was.Solid[i].Equals(now.Solid[i]);

    // The bricks a changed voxel at (x, y, z) of a brick touches: beside it only from its border, under it only from
    // its bottom two layers, over it always.
    private static void AddRound(HashSet<(int X, int Y, int Z)> touched, int x, int y, int z)
    {
        for (int dy = y <= 1 ? -1 : 0; dy <= Above; dy++)
        {
            for (int dz = z == 0 ? -1 : 0; dz <= (z == Brick.Size - 1 ? 1 : 0); dz++)
            {
                for (int dx = x == 0 ? -1 : 0; dx <= (x == Brick.Size - 1 ? 1 : 0); dx++)
                {
                    if (dx != 0 || dy != 0 || dz != 0)
                    {
                        touched.Add((dx, dy, dz));
                    }
                }
            }
        }
    }

    private static IEnumerable<(int X, int Y, int Z)> Offsets()
    {
        for (int dy = -1; dy <= Above; dy++)
        {
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    if (dx != 0 || dy != 0 || dz != 0)
                    {
                        yield return (dx, dy, dz);
                    }
                }
            }
        }
    }

    /// <summary>The surface over a brick of the field.</summary>
    public static SurfaceMesh Build(VoxelField field, BrickView brick)
    {
        var mesh = new SurfaceMesh();
        var corners = new Dictionary<(int X, int Y, int Z), int>();
        int ox = brick.X * Brick.Size;
        int oy = brick.Y * Brick.Size;
        int oz = brick.Z * Brick.Size;
        for (int i = 0; i < Brick.Count; i++)
        {
            if (brick.Water[i] <= VoxelField.Wet)
            {
                continue;
            }

            (int lx, int ly, int lz) = Brick.Place(i);
            int x = ox + lx;
            int y = oy + ly;
            int z = oz + lz;
            var velocity = new Vec3(brick.VelocityX[i], brick.VelocityY[i], brick.VelocityZ[i]);
            bool top = !field.GoesUp(x, y, z);
            if (top)
            {
                Top(field, mesh, corners, x, y, z, velocity);
            }

            foreach ((int dx, int dz) in Sides)
            {
                if (!field.IsWet(x + dx, y, z + dz))
                {
                    Side(field, mesh, x, y, z, dx, dz, top, velocity);
                }
            }
        }

        return mesh;
    }

    // A top: four shared corners at the mean level of the tops around each.
    private static void Top(VoxelField field, SurfaceMesh mesh, Dictionary<(int X, int Y, int Z), int> corners, int x, int y, int z, Vec3 velocity)
    {
        int a = Corner(field, mesh, corners, x, y, z, velocity);
        int b = Corner(field, mesh, corners, x, y, z + 1, velocity);
        int c = Corner(field, mesh, corners, x + 1, y, z + 1, velocity);
        int d = Corner(field, mesh, corners, x + 1, y, z, velocity);
        mesh.Triangles.AddRange([a, b, c, a, c, d]);
    }

    // The vertex at grid corner (cx, cz) of row y (<see cref="Level"/>), shared by the tops round it.
    private static int Corner(VoxelField field, SurfaceMesh mesh, Dictionary<(int X, int Y, int Z), int> corners, int cx, int y, int cz, Vec3 velocity)
    {
        if (corners.TryGetValue((cx, y, cz), out int index))
        {
            return index;
        }

        (float height, float shore, float depth) = Level(field, cx, y, cz);
        float s = field.VoxelSize;
        index = mesh.Add(new Vec3(cx * s, height, cz * s), depth, shore, velocity);
        corners[(cx, y, cz)] = index;
        return index;
    }

    // At grid corner (cx, cz) of row y: the mean level of the tops of the four columns round it (within a voxel of
    // y), how many of them hold water (its shore), and the deepest water under them.
    private static (float Height, float Shore, float Depth) Level(VoxelField field, int cx, int y, int cz)
    {
        float sum = 0f;
        int found = 0;
        float depth = 0f;
        for (int dz = -1; dz <= 0; dz++)
        {
            for (int dx = -1; dx <= 0; dx++)
            {
                if (TopNear(field, cx + dx, y, cz + dz, out int ty))
                {
                    sum += field.Level(cx + dx, ty, cz + dz);
                    found++;
                    depth = Math.Max(depth, Depth(field, cx + dx, ty, cz + dz));
                }
            }
        }

        return (found > 0 ? sum / found : (y + 1) * field.VoxelSize, found / 4f, depth);
    }

    // The top voxel of a column within a voxel of row y (a wet voxel with no water over it); the one a row up only if
    // the column's water at row y goes on into it (a pool over a floor is not the one under it).
    private static bool TopNear(VoxelField field, int x, int y, int z, out int top)
    {
        for (top = y + 1; top >= y - 1; top--)
        {
            if (top == y + 1 && !field.GoesUp(x, y, z))
            {
                continue;
            }

            if (field.IsWet(x, top, z) && !field.GoesUp(x, top, z))
            {
                return true;
            }
        }

        return false;
    }

    // The water's depth over a column from its top voxel down, metres (down to the first voxel without water).
    private static float Depth(VoxelField field, int x, int top, int z)
    {
        int bottom = top;
        while (field.IsWet(x, bottom - 1, z) && field.GoesUp(x, bottom - 1, z) && top - bottom < DeepestColumn)
        {
            bottom--;
        }

        return field.Level(x, top, z) - field.Floor(x, bottom, z);
    }

    // A side towards (dx, dz): from the water's floor in the voxel up to its top (the top's own corners there, so
    // that it meets the surface drawn, or the voxel's top under more water), facing out.
    private static void Side(VoxelField field, SurfaceMesh mesh, int x, int y, int z, int dx, int dz, bool top, Vec3 velocity)
    {
        float s = field.VoxelSize;
        float floor = field.Floor(x, y, z);

        // The face's two ends along the side, so that it faces out of the voxel (clockwise seen from outside).
        (int ax, int az, int bx, int bz) = (dx, dz) switch
        {
            (1, _) => (x + 1, z, x + 1, z + 1),
            (-1, _) => (x, z + 1, x, z),
            (_, 1) => (x + 1, z + 1, x, z + 1),
            _ => (x, z, x + 1, z),
        };
        float highA = top ? Level(field, ax, y, az).Height : (y + 1) * s;
        float highB = top ? Level(field, bx, y, bz).Height : (y + 1) * s;
        int a = mesh.Add(new Vec3(ax * s, floor, az * s), 1f, 1f, velocity);
        int b = mesh.Add(new Vec3(ax * s, highA, az * s), 1f, 1f, velocity);
        int c = mesh.Add(new Vec3(bx * s, highB, bz * s), 1f, 1f, velocity);
        int d = mesh.Add(new Vec3(bx * s, floor, bz * s), 1f, 1f, velocity);
        mesh.Triangles.AddRange([a, b, c, a, c, d]);
    }
}

/// <summary>Triangles of the water's surface and what each vertex carries (<see cref="VoxelSurface"/>).</summary>
internal sealed class SurfaceMesh
{
    /// <summary>Vertex positions, world metres.</summary>
    public List<Vec3> Vertices { get; } = [];

    /// <summary>The water's depth under each vertex, metres.</summary>
    public List<float> Depths { get; } = [];

    /// <summary>How far each vertex is from the water's edge: 0 at it, 1 among water all round.</summary>
    public List<float> Shores { get; } = [];

    /// <summary>The water's velocity at each vertex, metres a second.</summary>
    public List<Vec3> Velocities { get; } = [];

    /// <summary>Three vertex indices per triangle, clockwise seen from the side drawn.</summary>
    public List<int> Triangles { get; } = [];

    /// <summary>Adds a vertex, and gives its index.</summary>
    public int Add(Vec3 position, float depth, float shore, Vec3 velocity)
    {
        Vertices.Add(position);
        Depths.Add(depth);
        Shores.Add(shore);
        Velocities.Add(velocity);
        return Vertices.Count - 1;
    }
}
