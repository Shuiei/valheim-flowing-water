using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// The water's surface over a brick, as triangles, without the game. A wet voxel with no water over it has a top,
/// its corners shared with the tops beside it that are one water with it (<see cref="MaxStep"/>), at the mean of
/// their levels: a pool lies flat, and a stream down a slope, however steep, is one smooth sheet, not a stair. A wet
/// voxel beside one with no water has a side from its water's floor up to the surface over it (a fall's sheet, a
/// pool's edge over a cliff), except between columns of one water, where the water beside stands on what fills that
/// voxel (ground), and along a sheet thinner than <see cref="SheetDepth"/>, whose edge is its surface's. Each vertex
/// carries the depth of the water under it, how far it is from a shore (0 at the water's very edge) and the water's
/// velocity.
/// </summary>
internal static class VoxelSurface
{
    private static readonly (int X, int Z)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    /// <summary>The most voxels down a column's depth is measured, under its top.</summary>
    public const int DeepestColumn = 64;

    /// <summary>
    /// The most rows apart the tops of two columns side by side may be and still be one water: the higher one's
    /// water all over the lower top (a stream running down a slope up to two voxels a voxel, about 63 degrees). Further
    /// apart, the higher one ends there with a side (a fall's edge); water on rock steeper than that is drawn by its
    /// falls only (at three, a film on a rock's face under a source was a heap of jagged sheets standing off the rock).
    /// </summary>
    public const int MaxStep = 2;

    /// <summary>
    /// The shallowest water, a share of a voxel from its top down to its floor, whose column has sides: a thinner
    /// sheet's edge is its surface's, faded at the shore.
    /// </summary>
    public const float SheetDepth = 0.5f;

    /// <summary>
    /// How many bricks over one its surface reads: the top of the water a voxel's water goes up into is looked for
    /// through the brick over it (<see cref="VoxelField.Near"/> copies that many).
    /// </summary>
    public const int BricksOver = 1;

    // Bricks over one whose surface may read it: a column's depth is measured down DeepestColumn voxels from its top,
    // which may be a top some rows under the brick it is in (joined round a corner, at most 3 MaxStep down).
    private const int Above = (DeepestColumn / Brick.Size) + 1;

    /// <summary>The bricks round one whose surface may read it, as offsets in bricks: beside it, under it, and up to five over.</summary>
    public static IReadOnlyList<(int X, int Y, int Z)> Round { get; } = [.. Offsets()];

    /// <summary>
    /// The bricks round one (offsets in bricks) whose surface may change when its water goes from
    /// <paramref name="was"/> to <paramref name="now"/>: one beside it reads its columns facing it, one under it any
    /// of its rows (the water of a top there is joined to tops up to <see cref="MaxStep"/> over it, theirs to others,
    /// and so on round a corner), those over it up to five bricks up any column (the depth under their water is
    /// measured down through this one).
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
                (int x, _, int z) = Brick.Place(i);
                AddRound(touched, x, z);
            }
        }

        return touched;
    }

    // Whether a voxel's water and solid share are the same in both.
    private static bool Same(BrickView was, BrickView now, int i) =>
        was.Water[i].Equals(now.Water[i]) && was.Solid[i].Equals(now.Solid[i]);

    // The bricks a changed voxel in column (x, z) of a brick touches: beside it only from its border, under and over
    // it always.
    private static void AddRound(HashSet<(int X, int Y, int Z)> touched, int x, int z)
    {
        for (int dy = -1; dy <= Above; dy++)
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
    public static SurfaceMesh Build(VoxelField field, BrickView brick) => new Surface(field, brick).Build();

    // A brick's surface being built, with what it worked out of the water round it: each top's water, each top's
    // match beside it, each corner's vertex.
    private sealed class Surface
    {
        // Most tops one corner's vertex joins: one in each of the four columns round it.
        private const int CornerTops = 4;

        private readonly VoxelField _field;

        private readonly BrickView _brick;

        private readonly SurfaceMesh _mesh = new();

        // Each top's water, by the top's key (VoxelField.VoxelKey of its voxel).
        private readonly Dictionary<long, (float Level, int Bottom, float Depth)> _runs = [];

        // Each top's match beside it, by the way to it (Toward) and the top's key.
        private readonly Dictionary<long, int?>[] _matches = [[], [], [], []];

        // Each top's corners by which of its four (Way of the corner from the top's voxel) and the top's key: all the
        // tops a corner joins share it.
        private readonly Dictionary<long, Corner>[] _corners = [[], [], [], []];

        public Surface(VoxelField field, BrickView brick)
        {
            _field = field;
            _brick = brick;
        }

        public SurfaceMesh Build()
        {
            int ox = _brick.X * Brick.Size;
            int oy = _brick.Y * Brick.Size;
            int oz = _brick.Z * Brick.Size;
            for (int lz = 0; lz < Brick.Size; lz++)
            {
                for (int lx = 0; lx < Brick.Size; lx++)
                {
                    // Down the column, the top of the water the voxel above belongs to.
                    int? top = null;
                    for (int ly = Brick.Size - 1; ly >= 0; ly--)
                    {
                        int i = Brick.Index(lx, ly, lz);
                        if (_brick.Water[i] <= VoxelField.Wet)
                        {
                            top = null;
                            continue;
                        }

                        int x = ox + lx;
                        int y = oy + ly;
                        int z = oz + lz;
                        top = !_field.GoesUp(x, y, z) ? y : top ?? TopOver(x, y, z);
                        var velocity = new Vec3(_brick.VelocityX[i], _brick.VelocityY[i], _brick.VelocityZ[i]);
                        if (top == y)
                        {
                            Top(x, y, z, velocity);
                        }

                        DrawSides(x, y, z, top.Value, velocity);
                    }
                }
            }

            return _mesh;
        }

        // The top of the water a voxel's water goes on up into, as far as the brick over this one (as far as the field
        // built from reaches, VoxelField.Near): higher water is drawn by the bricks it is in.
        private int TopOver(int x, int y, int z)
        {
            int top = y;
            int highest = ((_brick.Y + BricksOver + 1) * Brick.Size) - 1;
            while (top < highest && _field.GoesUp(x, top, z))
            {
                top++;
            }

            return top;
        }

        // A top: four corners, each shared with the tops beside it that its water joins.
        private void Top(int x, int y, int z, Vec3 velocity)
        {
            int a = Vertex(x, z, (x, y, z), velocity);
            int b = Vertex(x, z + 1, (x, y, z), velocity);
            int c = Vertex(x + 1, z + 1, (x, y, z), velocity);
            int d = Vertex(x + 1, z, (x, y, z), velocity);
            _mesh.Triangles.AddRange([a, b, c, a, c, d]);
        }

        // The vertex of a top's corner at grid corner (cx, cz), made when first drawn.
        private int Vertex(int cx, int cz, (int X, int Y, int Z) top, Vec3 velocity)
        {
            Corner corner = CornerOf(cx, cz, top);
            if (corner.Index < 0)
            {
                float s = _field.VoxelSize;
                corner.Index = _mesh.Add(new Vec3(cx * s, corner.Height, cz * s), corner.Depth, corner.Shore, velocity);
            }

            return corner.Index;
        }

        // A top's corner at grid corner (cx, cz): the mean level of the tops round it its water joins (through each
        // other, one in each column at most), how many columns of the four hold them (its shore), and the deepest
        // water under them.
        private Corner CornerOf(int cx, int cz, (int X, int Y, int Z) top)
        {
            if (_corners[Way(cx - top.X, cz - top.Z)].TryGetValue(VoxelField.VoxelKey(top.X, top.Y, top.Z), out Corner known))
            {
                return known;
            }

            var tops = new List<(int X, int Y, int Z)>(CornerTops) { top };
            for (int k = 0; k < tops.Count; k++)
            {
                (int x, int y, int z) = tops[k];
                Join(tops, (x, y, z), x < cx ? 1 : -1, 0);
                Join(tops, (x, y, z), 0, z < cz ? 1 : -1);
            }

            float sum = 0f;
            float depth = 0f;
            foreach ((int X, int Y, int Z) t in tops)
            {
                (float level, _, float under) = Run(t);
                sum += level;
                depth = Math.Max(depth, under);
            }

            var corner = new Corner(sum / tops.Count, tops.Count / 4f, depth);
            foreach ((int x, int y, int z) in tops)
            {
                _corners[Way(cx - x, cz - z)][VoxelField.VoxelKey(x, y, z)] = corner;
            }

            return corner;
        }

        // Adds to a corner's tops the one beside this one (dx, dz) its water joins, unless its column has one.
        private void Join(List<(int X, int Y, int Z)> tops, (int X, int Y, int Z) top, int dx, int dz)
        {
            if (Match(top, dx, dz) is not int y)
            {
                return;
            }

            foreach ((int x, _, int z) in tops)
            {
                if (x == top.X + dx && z == top.Z + dz)
                {
                    return;
                }
            }

            tops.Add((top.X + dx, y, top.Z + dz));
        }

        // The row of the top in the column beside a top (dx, dz) that is one water with it: each is the other's
        // nearest the water joins (<see cref="Joins"/>). None when there is none.
        private int? Match((int X, int Y, int Z) top, int dx, int dz)
        {
            Dictionary<long, int?> matches = _matches[Toward(dx, dz)];
            long key = VoxelField.VoxelKey(top.X, top.Y, top.Z);
            if (matches.TryGetValue(key, out int? known))
            {
                return known;
            }

            int? match = Nearest(top, dx, dz) is int y && Nearest((top.X + dx, y, top.Z + dz), -dx, -dz) == top.Y ? y : null;
            matches[key] = match;
            return match;
        }

        // The top in the column beside a top (dx, dz) nearest its row that its water joins, the higher of two as near.
        private int? Nearest((int X, int Y, int Z) top, int dx, int dz)
        {
            int x = top.X + dx;
            int z = top.Z + dz;
            // Rows 0, +1, -1, +2, -2... from the top's.
            for (int k = 0; k <= 2 * MaxStep; k++)
            {
                int y = top.Y + ((k % 2 != 0) ? (k + 1) / 2 : -k / 2);
                if (_field.IsWet(x, y, z) && !_field.GoesUp(x, y, z) && Joins(top, (x, y, z)))
                {
                    return y;
                }
            }

            return null;
        }

        // Whether two tops side by side are one water: within a row of each other (a pool, a gentle slope), or the
        // higher one's water all over the lower top and at most MaxStep over it (a stream down a steep slope).
        private bool Joins((int X, int Y, int Z) a, (int X, int Y, int Z) b)
        {
            ((int X, int Y, int Z) high, (int X, int Y, int Z) low) = a.Y >= b.Y ? (a, b) : (b, a);
            int step = high.Y - low.Y;
            return step <= 1 || (step <= MaxStep && Run(high).Bottom > low.Y);
        }

        // A top's water: its level, the lowest voxel it goes down to (the first without water under it, at most
        // DeepestColumn down) and its depth there, metres.
        private (float Level, int Bottom, float Depth) Run((int X, int Y, int Z) top)
        {
            long key = VoxelField.VoxelKey(top.X, top.Y, top.Z);
            if (_runs.TryGetValue(key, out (float Level, int Bottom, float Depth) known))
            {
                return known;
            }

            (int x, int y, int z) = top;
            int bottom = y;
            while (_field.IsWet(x, bottom - 1, z) && _field.GoesUp(x, bottom - 1, z) && y - bottom < DeepestColumn)
            {
                bottom--;
            }

            float level = _field.Level(x, y, z);
            var run = (level, bottom, level - _field.Floor(x, bottom, z));
            _runs[key] = run;
            return run;
        }

        // The sides of a wet voxel whose water goes up to the top at row top: towards each voxel beside it without
        // water, unless the water is a sheet too thin for sides, the water beside is one with it (the same sheet),
        // or the voxel beside is under water standing in its column (ground).
        private void DrawSides(int x, int y, int z, int top, Vec3 velocity)
        {
            foreach ((int dx, int dz) in Sides)
            {
                if (!_field.IsWet(x + dx, y, z + dz)
                    && Run((x, top, z)).Depth >= SheetDepth * _field.VoxelSize
                    && Match((x, top, z), dx, dz) == null
                    && !UnderWater(x + dx, y, z + dz))
                {
                    Side(x, y, z, top, dx, dz, velocity);
                }
            }
        }

        // Whether water stands over a voxel in its column, within a brick's height: it rests on what fills the voxel
        // (the ground under a pool on a cliff's top, beside a stream at its foot).
        private bool UnderWater(int x, int y, int z)
        {
            for (int up = 1; up <= Brick.Size; up++)
            {
                if (_field.IsWet(x, y + up, z))
                {
                    return true;
                }
            }

            return false;
        }

        // A side towards (dx, dz): from the water's floor in the voxel up to its top, at most up to the surface drawn
        // over its column there (the top's corners: the side meets the surface, never sticks out over it), facing
        // out. Where the surface there is under the voxel's floor at one end, the side ends where it crosses the
        // floor (a triangle); at both, there is none.
        private void Side(int x, int y, int z, int top, int dx, int dz, Vec3 velocity)
        {
            float s = _field.VoxelSize;
            float floor = _field.Floor(x, y, z);

            // The face's two ends along the side, so that it faces out of the voxel (clockwise seen from outside).
            (int ax, int az, int bx, int bz) = (dx, dz) switch
            {
                (1, _) => (x + 1, z, x + 1, z + 1),
                (-1, _) => (x, z + 1, x, z),
                (_, 1) => (x + 1, z + 1, x, z + 1),
                _ => (x, z, x + 1, z),
            };
            float highA = CornerOf(ax, az, (x, top, z)).Height;
            float highB = CornerOf(bx, bz, (x, top, z)).Height;
            if (y != top)
            {
                highA = Math.Min(highA, (y + 1) * s);
                highB = Math.Min(highB, (y + 1) * s);
            }

            if (highA <= floor && highB <= floor)
            {
                return;
            }

            var endA = new Vec3(ax * s, floor, az * s);
            var endB = new Vec3(bx * s, floor, bz * s);
            if (highA <= floor || highB <= floor)
            {
                // Where the surface crosses the floor, from end A towards end B.
                Vec3 crossing = endA + ((endB - endA) * ((floor - highA) / (highB - highA)));
                if (highA <= floor)
                {
                    endA = crossing;
                }
                else
                {
                    endB = crossing;
                }
            }

            int a = _mesh.Add(endA, 1f, 1f, velocity);
            int b = highA > floor ? _mesh.Add(new Vec3(endA.X, highA, endA.Z), 1f, 1f, velocity) : a;
            int c = highB > floor ? _mesh.Add(new Vec3(endB.X, highB, endB.Z), 1f, 1f, velocity) : -1;
            int d = _mesh.Add(endB, 1f, 1f, velocity);
            if (c < 0)
            {
                _mesh.Triangles.AddRange([a, b, d]);
            }
            else if (b == a)
            {
                _mesh.Triangles.AddRange([a, c, d]);
            }
            else
            {
                _mesh.Triangles.AddRange([a, b, c, a, c, d]);
            }
        }
    }

    // Which of a voxel's four corners, 0 or 1 east and 0 or 1 north of its own.
    private static int Way(int x, int z) => x + (2 * z);

    // Which of the four ways to the column beside a voxel: east, west, north, south.
    private static int Toward(int dx, int dz) => dx != 0 ? (1 - dx) / 2 : 2 + ((1 - dz) / 2);

    // A corner of the surface: its height, shore and depth, and its vertex once drawn.
    private sealed class Corner(float height, float shore, float depth)
    {
        public float Height { get; } = height;

        public float Shore { get; } = shore;

        public float Depth { get; } = depth;

        public int Index { get; set; } = -1;
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
