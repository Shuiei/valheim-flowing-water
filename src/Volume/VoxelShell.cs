using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// A brick's water drawn as the voxels it fills where it meets the air, without the game: what the surface over the
/// water (<see cref="SurfaceGrid"/>) does not show. Water standing open to the air on a side shows that side, from its
/// floor (or the water beside it) up to its level; never toward water beside it the surface joins it to (nearly as high,
/// or a voxel lower: a sheet running down a slope, whose sides at each voxel drew a grid of small walls). Water falling
/// through the air (a voxel over air, holding no floor, the one under it not full) is a stream: a column narrower than
/// the voxel, as wide as its water would be, through the voxel's whole height so that the voxels of a fall join into one cascade,
/// capped where it starts and ends. Only faces the server says are open count (<see cref="BrickView.Open"/>): never one
/// a piece closes, so no water shows through a wall. Water the server keeps inside a solid voxel (a piece built over a
/// pool with nowhere for its water to go) is not drawn: it would show inside the piece.
/// </summary>
internal sealed class VoxelShell
{
    /// <summary>The narrowest a falling stream is drawn, a share of the voxel's side.</summary>
    public const float NarrowestFall = 0.25f;

    /// <summary>The widest a falling stream is drawn, a share of the voxel's side.</summary>
    public const float WidestFall = 0.9f;

    /// <summary>Falling this fast, metres a second, the water is all foam (as the surface's).</summary>
    public const float FoamFall = SurfaceGrid.FoamFall;

    // A voxel this full, with its solid share, holds the water over it up.
    private const float Full = 0.95f;

    // Water no higher than this share of a voxel over the water beside it is one sheet with it (the surface's): no side.
    private const float JoinStep = 0.35f;

    // Sides as Brick.Beside: east, west, up, down, north, south.
    private static readonly (int X, int Y, int Z)[] Sides = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    // A fall's four sides: each side's bit (as Sides) and the way it faces.
    private static readonly (int Side, int X, int Z)[] FallSides = [(0, 1, 0), (1, -1, 0), (4, 0, 1), (5, 0, -1)];

    // The corners of a face across a voxel, as shares of its reach, clockwise seen from where it faces: from above (x
    // east and z north), and from below (x mirrored).
    private static readonly (int A, int B)[] UpCorners = [(-1, -1), (-1, 1), (1, 1), (1, -1)];

    private static readonly (int A, int B)[] DownCorners = [(-1, -1), (1, -1), (1, 1), (-1, 1)];

    /// <summary>The bricks whose shell reads a brick's water, from it: the six beside it (their faces look across).</summary>
    public static IReadOnlyList<(int X, int Y, int Z)> Readers { get; } = Sides;

    /// <summary>Its corners, metres from the brick's middle (x, y, z each).</summary>
    public List<float> Positions { get; } = [];

    /// <summary>Each corner's normal, out of the water (x, y, z each).</summary>
    public List<float> Normals { get; } = [];

    /// <summary>Each corner's place on its face in world metres (across the face, up it or north), for the textures.</summary>
    public List<float> Places { get; } = [];

    /// <summary>Each corner's water: how much of it is foam (0 to 1) and how much shows (0 to 1).</summary>
    public List<float> Water { get; } = [];

    /// <summary>Each corner's current along its face, metres a second, in its place's two directions.</summary>
    public List<float> Flow { get; } = [];

    /// <summary>The triangles, three corners each, wound clockwise seen from outside the water (Unity's front faces).</summary>
    public List<int> Triangles { get; } = [];

    /// <summary>How many faces it has.</summary>
    public int Faces => Triangles.Count / 6;

    /// <summary>The faces of a brick's water, from the field's water round it (its own and the bricks beside it).</summary>
    public static VoxelShell Build(VoxelField field, int bx, int by, int bz)
    {
        var shell = new VoxelShell();
        BrickView view = field.Find(bx, by, bz);
        if (view == null)
        {
            return shell;
        }

        float s = view.VoxelSize;
        float half = Brick.Size * s * 0.5f;
        (float X, float Y, float Z) middle = ((bx * Brick.Size * s) + half, (by * Brick.Size * s) + half, (bz * Brick.Size * s) + half);
        for (int i = 0; i < Brick.Count; i++)
        {
            float water = view.Water[i];
            if (!(water * s > Drawing.HideDepth) || view.Solid[i] >= 1f)
            {
                continue;
            }

            (int lx, int ly, int lz) = Brick.Place(i);
            int x = (bx * Brick.Size) + lx, y = (by * Brick.Size) + ly, z = (bz * Brick.Size) + lz;
            var at = new Voxel(x, y, z, s, middle, view.VelocityX[i], view.VelocityY[i], view.VelocityZ[i]);
            int open = view.Open[i];
            if (Falling(field, view, i, x, y, z, open))
            {
                shell.Fall(field, at, water, open);
            }
            else
            {
                shell.Stand(field, at, open, x, y, z);
            }
        }

        return shell;
    }

    // Over the air, holding no floor, the voxel under it not full: falling.
    private static bool Falling(VoxelField field, BrickView view, int i, int x, int y, int z, int open) =>
        (open & (1 << 3)) != 0 && view.Solid[i] < 0.02f && field.WaterAt(x, y - 1, z) + Solid(field, x, y - 1, z) < Full;

    private static float Solid(VoxelField field, int x, int y, int z) =>
        field.Voxel(x, y, z, out BrickView view, out int i) ? view.Solid[i] : 0f;

    // Water standing: its open sides above the water beside them.
    private void Stand(VoxelField field, Voxel at, int open, int x, int y, int z)
    {
        float s = at.Size;
        float floor = field.Floor(x, y, z);
        float level = field.GoesUp(x, y, z) ? (y + 1) * s : field.Level(x, y, z);
        float foam = Clamp((float)Math.Sqrt((at.VX * at.VX) + (at.VZ * at.VZ)) / FoamFall) * 0.3f;
        for (int side = 0; side < Sides.Length; side++)
        {
            // Up is the surface's; down, open over the air, is a fall's.
            if ((open & (1 << side)) == 0 || side is 2 or 3)
            {
                continue;
            }

            (int dx, _, int dz) = Sides[side];
            float bottom = floor;
            if (field.IsWet(x + dx, y, z + dz))
            {
                // Water beside nearly as high: the surface joins the two (a sheet down a gentle slope), no side between.
                float beside = field.GoesUp(x + dx, y, z + dz) ? (y + 1) * s : field.Level(x + dx, y, z + dz);
                if (level - beside <= JoinStep * s)
                {
                    continue;
                }

                bottom = Math.Max(bottom, beside);
            }
            else if (field.IsWet(x + dx, y - 1, z + dz) && !field.IsWet(x + dx, y + 1, z + dz) && (open & BrickData.GroundBeside) == 0)
            {
                // Water beside a voxel lower, the top of its column: the water steps down to it, the surface joining
                // them (with water higher up that column, on a ledge or a floor, the surface joins that; with ground in
                // the voxel beside, as the server says, the side shows over it: the side stays).
                continue;
            }

            if (level - bottom > Drawing.HideDepth)
            {
                at.Side(this, dx, dz, 0.5f * s, bottom, level, foam, 1f);
            }
        }
    }

    // Water falling: a column through the whole voxel, as wide as its water, capped where the fall starts and ends;
    // never a side or a cap on a face a piece closes (behind it, it would show through it).
    private void Fall(VoxelField field, Voxel at, float water, int open)
    {
        float s = at.Size;
        float width = Math.Max(NarrowestFall, Math.Min(WidestFall, (float)Math.Sqrt(water))) * s;
        float bottom = at.Y * s, top = (at.Y + 1) * s;
        float foam = 0.6f + (0.4f * Clamp(-at.VY / FoamFall));
        foreach ((int side, int dx, int dz) in FallSides)
        {
            if ((open & (1 << side)) != 0)
            {
                at.Side(this, dx, dz, width * 0.5f, bottom, top, foam, 0.85f);
            }
        }

        if ((open & (1 << 2)) != 0 && !field.IsWet(at.X, at.Y + 1, at.Z))
        {
            at.Horizontal(this, top, true, width * 0.5f, foam, 0.85f);
        }

        if (!field.IsWet(at.X, at.Y - 1, at.Z))
        {
            at.Horizontal(this, bottom, false, width * 0.5f, foam, 0.85f);
        }
    }

    private static float Clamp(float value) => Math.Max(0f, Math.Min(1f, value));

    private void Corner(float x, float y, float z, (float X, float Y, float Z) normal, float u, float v, float foam, float shown, float fu, float fv)
    {
        Positions.Add(x);
        Positions.Add(y);
        Positions.Add(z);
        Normals.Add(normal.X);
        Normals.Add(normal.Y);
        Normals.Add(normal.Z);
        Places.Add(u);
        Places.Add(v);
        Water.Add(foam);
        Water.Add(shown);
        Flow.Add(fu);
        Flow.Add(fv);
    }

    // The two triangles of the face whose four corners were just added, clockwise round it seen from outside.
    private void Quad()
    {
        int a = (Positions.Count / 3) - 4;
        Triangles.Add(a);
        Triangles.Add(a + 1);
        Triangles.Add(a + 2);
        Triangles.Add(a);
        Triangles.Add(a + 2);
        Triangles.Add(a + 3);
    }

    // A voxel being drawn: where it is, its size, the brick's middle (where the corners are measured from), its current.
    // Never compared with another; a record, so that the equality a value type is asked for comes made.
    private readonly record struct Voxel
    {
        public Voxel(int x, int y, int z, float size, (float X, float Y, float Z) middle, float vx, float vy, float vz)
        {
            X = x;
            Y = y;
            Z = z;
            Size = size;
            Middle = middle;
            VX = vx;
            VY = vy;
            VZ = vz;
        }

        public int X { get; }

        public int Y { get; }

        public int Z { get; }

        public float Size { get; }

        public (float X, float Y, float Z) Middle { get; }

        public float VX { get; }

        public float VY { get; }

        public float VZ { get; }

        // Its middle's place, world metres.
        private float MX => (X + 0.5f) * Size;

        private float MZ => (Z + 0.5f) * Size;

        // A side facing (dx, dz), reach metres from the voxel's middle, from bottom to top (world heights).
        public void Side(VoxelShell shell, int dx, int dz, float reach, float bottom, float top, float foam, float shown)
        {
            // Across the face, to the right seen from outside (for the east face, north).
            float ax = -dz, az = dx;
            float fx = MX + (dx * reach), fz = MZ + (dz * reach);
            (float X, float Y, float Z) n = (dx, 0f, dz);
            float along = (VX * ax) + (VZ * az);
            float uA = (fx * ax) + (fz * az);
            for (int k = 0; k < 4; k++)
            {
                // Clockwise seen from outside: left bottom, left top, right top, right bottom.
                float side = k < 2 ? -reach : reach;
                float h = k is 0 or 3 ? bottom : top;
                float px = fx + (ax * side), pz = fz + (az * side);
                shell.Corner(px - Middle.X, h - Middle.Y, pz - Middle.Z, n, uA + side, h, foam, shown, along, VY);
            }

            shell.Quad();
        }

        // A face across the voxel at a height, up or down, reach metres from its middle each way.
        public void Horizontal(VoxelShell shell, float height, bool up, float reach, float foam, float shown)
        {
            (float X, float Y, float Z) n = (0f, up ? 1f : -1f, 0f);
            foreach ((int a, int b) in up ? UpCorners : DownCorners)
            {
                float ra = a * reach, rb = b * reach;
                shell.Corner(MX + ra - Middle.X, height - Middle.Y, MZ + rb - Middle.Z, n, MX + ra, MZ + rb, foam, shown, VX, VZ);
            }

            shell.Quad();
        }
    }
}
