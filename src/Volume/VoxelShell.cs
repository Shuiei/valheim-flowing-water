using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// A brick's water drawn as the voxels it fills where it meets the air, without the game: what the surface over the
/// water (<see cref="SurfaceGrid"/>) does not show. Water standing open to the air on a side shows that side, from its
/// floor (or the water beside it) up to its level, at most up to its top's water laid flat on the ground under it, as
/// the surface stands (<see cref="VoxelField.Settled"/>); never toward water beside it the surface joins it to (the top
/// of the column beside up to <see cref="VoxelSurface.MaxStep"/> rows lower, however deep either is: the surface runs
/// from one column's top to the other's, and a side between them stood under it or through it, a grid of small walls
/// seen as dark patches down a sheet on a slope). Water falling through the air (a voxel over air, holding no floor,
/// the one under it not full nor holding ground: a step down a steep slope is the surface's) is a stream: a column
/// narrower than the voxel, as wide as its water would be, through the voxel's whole height so that the voxels of a
/// fall join into one cascade, capped where it starts and ends. Only faces the server says are open count
/// (<see cref="BrickView.Open"/>): never one a piece closes, so no water shows through a wall. Water the server keeps
/// inside a solid voxel (a piece built over a pool with nowhere for its water to go) is not drawn: it would show inside
/// the piece.
/// </summary>
internal sealed class VoxelShell
{
    /// <summary>The narrowest a falling stream is drawn, a share of the voxel's side.</summary>
    public const float NarrowestFall = 0.25f;

    /// <summary>The widest a falling stream is drawn, a share of the voxel's side.</summary>
    public const float WidestFall = 0.9f;

    /// <summary>
    /// The most voxels a fall's column runs through between two voxels drawn falling: dry, or holding water too thin to
    /// draw (falling water fills every other voxel as it goes down, a thin fall fewer).
    /// </summary>
    public const int LongestGap = 2;

    /// <summary>Falling this fast, metres a second, the water is all foam (as the surface's).</summary>
    public const float FoamFall = SurfaceGrid.FoamFall;

    // Sides as Brick.Beside: east, west, up, down, north, south.
    private static readonly (int X, int Y, int Z)[] Sides = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    // A fall's four sides: each side's bit (as Sides) and the way it faces.
    private static readonly (int Side, int X, int Z)[] FallSides = [(0, 1, 0), (1, -1, 0), (4, 0, 1), (5, 0, -1)];

    // The corners of a face across a voxel, as shares of its reach, clockwise seen from where it faces: from above (x
    // east and z north), and from below (x mirrored).
    private static readonly (int A, int B)[] UpCorners = [(-1, -1), (-1, 1), (1, 1), (1, -1)];

    private static readonly (int A, int B)[] DownCorners = [(-1, -1), (1, -1), (1, 1), (-1, 1)];

    /// <summary>
    /// The bricks whose shell reads a brick's water, from it: the six beside it (their faces look across), the four
    /// beside the one over it (a side looks down the column beside it, a few rows under its own) and the four beside
    /// the one under it (a side in a brick's top row looks for water over the voxel beside, a row up).
    /// </summary>
    public static IReadOnlyList<(int X, int Y, int Z)> Readers { get; } =
        [.. Sides, (1, 1, 0), (-1, 1, 0), (0, 1, 1), (0, 1, -1), (1, -1, 0), (-1, -1, 0), (0, -1, 1), (0, -1, -1)];

    /// <summary>Its corners, metres from the brick's middle (x, y, z each).</summary>
    public List<float> Positions { get; } = [];

    /// <summary>Each corner's normal, out of the water (x, y, z each).</summary>
    public List<float> Normals { get; } = [];

    /// <summary>Each corner's place on its face in world metres (across the face, up it or north), for the textures.</summary>
    public List<float> Places { get; } = [];

    // A fall's reach each way (FallSides' order), kept from voxel to voxel.
    private readonly float[] _reach = new float[FallSides.Length];

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
                if (!Steps(field, x, y, z, open))
                {
                    shell.Fall(field, at, water, open);
                }
            }
            else
            {
                shell.Stand(field, at, open, view.GroundBeside[i], x, y, z);
            }
        }

        return shell;
    }

    // Over the air, holding no floor, the voxel under it not full: falling.
    private static bool Falling(VoxelField field, BrickView view, int i, int x, int y, int z, int open) =>
        (open & (1 << 3)) != 0 && view.Solid[i] < BrickData.HoldsGround && field.WaterAt(x, y - 1, z) + Solid(field, x, y - 1, z) < VoxelField.Full;

    // Over a voxel holding ground water falling runs down a slope steeper than the voxels, voxel to voxel: the surface
    // draws it there as one sheet down the slope (a column capped at each step drew a box a voxel). The server says so
    // with its open faces (the ground under dry voxels is not sent). Water falling on into the voxel under it is a fall
    // all the same, down a cliff's face too (the surface draws a column's top only), and so is the foot of a fall, water
    // falling into it from over it: its column goes down to the ground. (The voxel under holding ground itself, its
    // water a sheet's lower part on it, as a sheet down a slope leaves it crossing from one row to the next: the water
    // over it stands on that, it is no fall.)
    private static bool Steps(VoxelField field, int x, int y, int z, int open)
    {
        bool groundUnder = Solid(field, x, y - 1, z) >= BrickData.HoldsGround;
        return ((open & BrickData.GroundUnder) != 0 || groundUnder) && (!field.IsWet(x, y - 1, z) || groundUnder) && !FallsInto(field, x, y, z);
    }

    // Whether voxel (x, y, z) is drawn as a fall, as Build draws it: water enough to show, out of the rock, falling, no
    // sheet's step down a slope.
    private static bool DrawnFall(VoxelField field, int x, int y, int z) =>
        field.Voxel(x, y, z, out BrickView view, out int i) && view.Water[i] * view.VoxelSize > Drawing.HideDepth && view.Solid[i] < 1f
        && Falling(field, view, i, x, y, z, view.Open[i]) && !Steps(field, x, y, z, view.Open[i]);

    // Whether the voxel over (x, y, z) holds water falling into it.
    private static bool FallsInto(VoxelField field, int x, int y, int z) => FallingWater(field, x, y + 1, z) > 0f;

    // The water falling in voxel (x, y, z), as a share of it; 0 where none falls.
    private static float FallingWater(VoxelField field, int x, int y, int z) =>
        field.Voxel(x, y, z, out BrickView view, out int i) && VoxelField.IsWet(view, i) && Falling(field, view, i, x, y, z, view.Open[i]) ? view.Water[i] : 0f;

    private static float Solid(VoxelField field, int x, int y, int z) =>
        field.Voxel(x, y, z, out BrickView view, out int i) ? view.Solid[i] : 0f;

    // Water standing: its open sides above the water beside them.
    private void Stand(VoxelField field, Voxel at, int open, int groundBeside, int x, int y, int z)
    {
        float s = at.Size;
        float floor = field.Floor(x, y, z);
        float level = field.GoesUp(x, y, z) ? (y + 1) * s : field.Level(x, y, z);
        float foam = Clamp((float)Math.Sqrt((at.VX * at.VX) + (at.VZ * at.VZ)) / FoamFall) * 0.3f;
        float? settled = null;
        for (int side = 0; side < Sides.Length; side++)
        {
            // Up is the surface's; down, open over the air, is a fall's.
            if ((open & (1 << side)) == 0 || side is 2 or 3)
            {
                continue;
            }

            (int dx, _, int dz) = Sides[side];

            // Ground in the dry voxel beside on this side, as the server says, stands between the water and the water
            // beside: its side shows over it however the surface joins them.
            bool groundBetween = (groundBeside & (1 << side)) != 0 && !field.IsWet(x + dx, y, z + dz);
            if (!groundBetween && OneSheet(field, x, y, z, dx, dz))
            {
                continue;
            }

            // Water beside under other water (on a floor up that column, which the surface joins instead): the side
            // shows over it.
            float bottom = floor;
            if (field.IsWet(x + dx, y, z + dz))
            {
                bottom = Math.Max(bottom, field.GoesUp(x + dx, y, z + dz) ? (y + 1) * s : field.Level(x + dx, y, z + dz));
            }

            // Up to the water laid flat at most, as the surface over it stands (a film over the voxel is not water
            // up to it).
            settled ??= Math.Min(level, field.Settled(x, y, z));
            if (settled.Value - bottom > Drawing.HideDepth)
            {
                at.Side(this, dx, dz, 0.5f * s, bottom, settled.Value, foam, 1f);
            }
        }
    }

    // Whether the water a voxel is in and the water in the column beside it (dx, dz) are one sheet, as the surface
    // draws them: the column's top at this row or lower, within the surface's reach of this one's (VoxelSurface.MaxStep
    // rows), with nothing over it up to this row. However deep each is (a sheet crossing a row at another height in
    // each column, on the ground under it; a pool spilling down a step into a stream), the surface runs from one top
    // to the other over no air to show a side to.
    private static bool OneSheet(VoxelField field, int x, int y, int z, int dx, int dz)
    {
        int bx = x + dx, bz = z + dz;
        if (!NoneOver(field, bx, y, bz))
        {
            return false;
        }

        int top = field.StandingTop(x, y, z);
        for (int row = y; row >= top - VoxelSurface.MaxStep; row--)
        {
            if (field.IsWet(bx, row, bz) && !field.GoesUp(bx, row, bz))
            {
                return true;
            }
        }

        return false;
    }

    // No water standing over row y of column (x, z), up to the top of the brick row y is in (one row at least): the
    // surface takes a column's highest standing top in a brick's rows, so with water standing higher up there it joins
    // that, not the water at or under y. Water falling through the air over it (a fall's, its spray over a pool) is the
    // fall's, as air is to the surface.
    private static bool NoneOver(VoxelField field, int x, int y, int z)
    {
        int ceiling = Math.Max(y + 1, (Brick.Of(y) * Brick.Size) + Brick.Size - 1);
        for (int up = y + 1; up <= ceiling; up++)
        {
            if (field.IsWet(x, up, z) && !field.FallsThrough(x, up, z))
            {
                return false;
            }
        }

        return true;
    }

    // Water falling: a column through the whole voxel, as wide as the most water in it or the falling voxels over and
    // under it (across a gap, the one past it), capped where the fall starts and ends; the voxels a fall runs through
    // between two drawn falling ones (LongestGap at most) taken in by the one over them, uncapped. Toward a voxel beside
    // drawn falling too (or a row under or over it, the fall stepping aside), the column reaches the voxels' shared face
    // and has no side there: falls side by side are one sheet, and one stepping aside down a face meets at its steps. Falling water is uneven from voxel to voxel and spread thin over a face: as wide as each voxel's own, each
    // a column of its own and capped at each voxel too thin to draw, a fall down a high face was rows of dashed poles.
    // Never a side or a cap on a face a piece closes (behind it, it would show through it).
    private void Fall(VoxelField field, Voxel at, float water, int open)
    {
        float s = at.Size;
        int gapUnder = GapTo(field, at.X, at.Y, at.Z, -1), gapOver = GapTo(field, at.X, at.Y, at.Z, 1);
        float over = FallingWater(field, at.X, at.Y + gapOver + 1, at.Z), under = FallingWater(field, at.X, at.Y - gapUnder - 1, at.Z);
        float half = Math.Max(NarrowestFall, Math.Min(WidestFall, (float)Math.Sqrt(Math.Max(water, Math.Max(over, under))))) * s * 0.5f;
        float bottom = (at.Y - gapUnder) * s, top = (at.Y + 1) * s;
        float foam = 0.6f + (0.4f * Clamp(-at.VY / FoamFall));

        // How far the column reaches each way (east, west, north, south): to the shared face where the voxel beside falls,
        // or the one beside it a row up or down (a fall down a slanting face steps aside a voxel a row: its columns meet
        // at their corners, and are one sheet stepping down the face); its face toward this one open too.
        float[] reach = _reach;
        for (int k = 0; k < FallSides.Length; k++)
        {
            (int side, int dx, int dz) = FallSides[k];
            bool joins = JoinedFall(field, at.X + dx, at.Y, at.Z + dz, side)
                || (gapUnder == 0 && JoinedFall(field, at.X + dx, at.Y - 1, at.Z + dz, side) && !DrawnFall(field, at.X, at.Y - 1, at.Z))
                || (gapOver == 0 && JoinedFall(field, at.X + dx, at.Y + 1, at.Z + dz, side) && !DrawnFall(field, at.X, at.Y + 1, at.Z));
            reach[k] = (open & (1 << side)) != 0 && joins ? 0.5f * s : half;
        }

        for (int k = 0; k < FallSides.Length; k++)
        {
            (int side, int dx, int dz) = FallSides[k];
            if ((open & (1 << side)) != 0 && reach[k] < 0.5f * s)
            {
                // Across the face, to the right seen from outside: the reach toward the side the other way, and this one.
                at.Side(this, dx, dz, reach[k], reach[Toward(dz, -dx)], reach[Toward(-dz, dx)], bottom, top, foam, 0.85f);
            }
        }

        if ((open & (1 << 2)) != 0 && !field.IsWet(at.X, at.Y + 1, at.Z) && gapOver == 0)
        {
            at.Horizontal(this, top, true, reach, foam, 0.85f);
        }

        if (!field.IsWet(at.X, at.Y - 1, at.Z) && gapUnder == 0)
        {
            at.Horizontal(this, bottom, false, reach, foam, 0.85f);
        }
    }

    // Whether voxel (x, y, z) beside a fall, on its side (Sides' bit), is drawn falling with its face toward the fall
    // open (the bit on the other side, side ^ 1): no piece closes it between them.
    private static bool JoinedFall(VoxelField field, int x, int y, int z, int side) =>
        DrawnFall(field, x, y, z) && field.Voxel(x, y, z, out BrickView view, out int i) && (view.Open[i] & (1 << (side ^ 1))) != 0;

    // The fall side (FallSides' index) facing (dx, dz).
    private static int Toward(int dx, int dz) => dx > 0 ? 0 : dx < 0 ? 1 : dz > 0 ? 2 : 3;

    // How many voxels from (x, y, z) up (dy 1) or down (-1) a fall runs through to the next voxel drawn falling (Gap
    // each, LongestGap at most): 0 when the next is no gap, or no voxel drawn falling is found past them.
    private static int GapTo(VoxelField field, int x, int y, int z, int dy)
    {
        for (int k = 1; k <= LongestGap && Gap(field, x, y + (k * dy), z); k++)
        {
            if (DrawnFall(field, x, y + ((k + 1) * dy), z))
            {
                return k;
            }
        }

        return 0;
    }

    // A voxel a fall may run through though nothing in it is drawn: dry, or its water too thin to draw (as Build has
    // it), and no ground (a fall never draws into the rock). A dry voxel's ground is not sent: the voxel over it says
    // it holds some (BrickData.GroundUnder).
    private static bool Gap(VoxelField field, int x, int y, int z) =>
        !(field.Voxel(x, y, z, out BrickView view, out int i) && view.Water[i] * view.VoxelSize > Drawing.HideDepth) && Solid(field, x, y, z) < BrickData.HoldsGround
        && !(field.Voxel(x, y + 1, z, out BrickView over, out int j) && (over.Open[j] & BrickData.GroundUnder) != 0);

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

        // A side facing (dx, dz), reach metres from the voxel's middle, as wide each way, from bottom to top (world
        // heights).
        public void Side(VoxelShell shell, int dx, int dz, float reach, float bottom, float top, float foam, float shown) =>
            Side(shell, dx, dz, reach, reach, reach, bottom, top, foam, shown);

        // A side facing (dx, dz), reach metres from the voxel's middle, from left metres to its left to right to its right
        // (seen from outside), from bottom to top (world heights).
        public void Side(VoxelShell shell, int dx, int dz, float reach, float left, float right, float bottom, float top, float foam, float shown)
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
                float side = k < 2 ? -left : right;
                float h = k is 0 or 3 ? bottom : top;
                float px = fx + (ax * side), pz = fz + (az * side);
                shell.Corner(px - Middle.X, h - Middle.Y, pz - Middle.Z, n, uA + side, h, foam, shown, along, VY);
            }

            shell.Quad();
        }

        // A face across the voxel at a height, up or down, reach metres from its middle each way (east, west, north,
        // south: FallSides' order).
        public void Horizontal(VoxelShell shell, float height, bool up, float[] reach, float foam, float shown)
        {
            (float X, float Y, float Z) n = (0f, up ? 1f : -1f, 0f);
            foreach ((int a, int b) in up ? UpCorners : DownCorners)
            {
                float ra = a > 0 ? reach[0] : -reach[1], rb = b > 0 ? reach[2] : -reach[3];
                shell.Corner(MX + ra - Middle.X, height - Middle.Y, MZ + rb - Middle.Z, n, MX + ra, MZ + rb, foam, shown, VX, VZ);
            }

            shell.Quad();
        }
    }
}
