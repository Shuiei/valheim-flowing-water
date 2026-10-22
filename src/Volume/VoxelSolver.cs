using System;
using System.Linq;
using System.Threading.Tasks;

namespace FlowingWater.Volume;

/// <summary>
/// Moves the water of a <see cref="VoxelGrid"/>, without the game: the pipe model in three dimensions. Each face
/// between two voxels is a pipe whose flow keeps its momentum from step to step, sped up by the difference of the
/// two voxels' heads and slowed by drag. A voxel's head is the level its water pushes to: its floor plus all the
/// water resting on it in its column (so a still stack has one head throughout and stays still, and a hole low in a
/// box's side lets out water faster the deeper the box), and, where water is pressed in under a ceiling, a little
/// more for each share it holds over full (so a pipe carries the push of the water above). Air below water is a low
/// head: the water falls.
/// </summary>
internal sealed class VoxelSolver
{
    /// <summary>Metres a second squared.</summary>
    public const float Gravity = 9.81f;

    // Metres of head, per voxel side, for each share of a voxel held over full (water pressed in). Stiff enough that
    // water falling into a full voxel is pushed on rather than bouncing back (softer kept a pool sloshing for ever).
    private const float Stiffness = 12f;

    // Drag on every pipe: a share of its flow lost each second, and more the faster it runs (seconds a metre).
    private const float Damping = 0.3f, Drag = 0.05f;

    // The share of a voxel's room over which the weight of the water over it comes on as it fills.
    private const float Contact = 0.1f;

    // The deepest water steps are kept stable for, metres: longer steps are split.
    private const float DeepestStable = 10f;

    // A brick whose every flow stayed under this (cubic metres a second) for this many steps sleeps: it is skipped
    // while the bricks beside it sleep too or are dry (water poured in, a solid changed or a waking neighbour wakes it).
    private const float SleepingFlow = 1e-3f;

    private const int StepsToSleep = 24;

    // Water settling into a voxel wakes its brick when more than this share of a voxel: a still pool's last traces
    // settling do not keep it awake.
    private const float WakingDrop = 1e-4f;

    private readonly VoxelGrid _grid;

    private Brick[] _bricks = [];

    private Brick[][] _columns = [];

    private int _columnsVersion = -1;

    /// <summary>A solver for this grid.</summary>
    public VoxelSolver(VoxelGrid grid)
    {
        _grid = grid;
    }

    /// <summary>Water whose voxel's top is at or below this level (metres) is gone: the sea takes it.</summary>
    public float SeaLevel { get; set; } = float.NegativeInfinity;

    /// <summary>
    /// The longest step kept stable, seconds: a wave crosses at most half a voxel in it, in water as deep as
    /// <see cref="DeepestStable"/> or through pressed water, whichever is faster.
    /// </summary>
    public float MaxStep => 0.5f * _grid.VoxelSize / (float)Math.Sqrt(Gravity * Math.Max(DeepestStable, (1f + Stiffness) * _grid.VoxelSize));

    /// <summary>Moves the water over this much time (in as many short steps as it takes to stay stable).</summary>
    public void Step(float dt)
    {
        int steps = Math.Max(1, (int)Math.Ceiling(dt / MaxStep));
        for (int k = 0; k < steps; k++)
        {
            StepOnce(dt / steps);
        }
    }

    // One step, on each brick's wet part only (with the voxels round it): a still pool's air costs nothing.
    private void StepOnce(float dt)
    {
        if (_columnsVersion != _grid.Version)
        {
            _columnsVersion = _grid.Version;
            _bricks = [.. _grid.Bricks];
            _columns = [.. _bricks.GroupBy(b => ((long)b.X << 32) | (uint)b.Z).Select(g => g.OrderByDescending(b => b.Y).ToArray())];
        }

        float s = _grid.VoxelSize;
        Parallel.For(0, _bricks.Length, k => Part(_bricks[k]));
        if (Array.TrueForAll(_bricks, b => b.PartCount == 0))
        {
            // Everything sleeps (or is dry): nothing to step.
            _grid.MakeWanted();
            return;
        }

        Parallel.For(0, _columns.Length, k => Heads(_columns[k], s));
        Parallel.For(0, _bricks.Length, k => Flows(_bricks[k], dt, s));
        Parallel.For(0, _bricks.Length, k => Shares(_bricks[k], dt, s));
        Parallel.For(0, _bricks.Length, k => Scale(_bricks[k]));
        Parallel.For(0, _bricks.Length, k => Apply(_bricks[k], dt, s));
        _grid.MakeWanted();
    }

    // The part a step works on: the wet voxels and those round them, the border layers facing a brick wet at that
    // border (its water may come over), and last step's part (its flows must be set again, to nothing if dry); none
    // for a sleeping brick.
    private static void Part(Brick brick)
    {
        if (Sleeps(brick))
        {
            // Falling asleep, its flows are let go: no water may cross its faces while it sleeps (one side would
            // count what the other does not).
            if (!brick.Asleep)
            {
                Array.Clear(brick.FlowX, 0, Brick.Count);
                Array.Clear(brick.FlowY, 0, Brick.Count);
                Array.Clear(brick.FlowZ, 0, Brick.Count);
                brick.LastHigh = (-1, -1, -1);
            }

            brick.Asleep = true;
            brick.PartCount = 0;
            return;
        }

        brick.Asleep = false;

        (int wlx, int wly, int wlz) = brick.WetLow;
        (int whx, int why, int whz) = brick.WetHigh;
        bool wet = whx >= 0;
        int lx = wet ? Math.Max(0, wlx - 1) : Brick.Size, ly = wet ? Math.Max(0, wly - 1) : Brick.Size, lz = wet ? Math.Max(0, wlz - 1) : Brick.Size;
        int hx = wet ? Math.Min(Brick.Size - 1, whx + 1) : -1, hy = wet ? Math.Min(Brick.Size - 1, why + 1) : -1, hz = wet ? Math.Min(Brick.Size - 1, whz + 1) : -1;
        for (int side = 0; side < 6; side++)
        {
            if (brick.Beside[side] is not { } next || next.WetHigh.X < 0 || !Touches(next, side ^ 1))
            {
                continue;
            }

            // The whole border layer facing it.
            int axis = side / 2;
            int layer = side % 2 == 0 ? Brick.Size - 1 : 0;
            (int fx, int fy, int fz) = axis == 0 ? (layer, -1, -1) : axis == 1 ? (-1, layer, -1) : (-1, -1, layer);
            lx = Math.Min(lx, fx < 0 ? 0 : fx);
            hx = Math.Max(hx, fx < 0 ? Brick.Size - 1 : fx);
            ly = Math.Min(ly, fy < 0 ? 0 : fy);
            hy = Math.Max(hy, fy < 0 ? Brick.Size - 1 : fy);
            lz = Math.Min(lz, fz < 0 ? 0 : fz);
            hz = Math.Max(hz, fz < 0 ? Brick.Size - 1 : fz);
        }

        (int llx, int lly, int llz) = brick.LastLow;
        (int lhx, int lhy, int lhz) = brick.LastHigh;
        brick.LastLow = (lx, ly, lz);
        brick.LastHigh = (hx, hy, hz);
        if (lhx >= 0)
        {
            (lx, ly, lz) = (Math.Min(lx, llx), Math.Min(ly, lly), Math.Min(lz, llz));
            (hx, hy, hz) = (Math.Max(hx, lhx), Math.Max(hy, lhy), Math.Max(hz, lhz));
        }

        brick.Low = (lx, ly, lz);
        brick.High = (hx, hy, hz);
        int count = 0;
        for (int y = ly; y <= hy; y++)
        {
            for (int z = lz; z <= hz; z++)
            {
                for (int x = lx; x <= hx; x++)
                {
                    brick.Part[count++] = Brick.Index(x, y, z);
                }
            }
        }

        brick.PartCount = count;
    }

    // A brick sleeps when it and every brick beside it with water have been quiet long enough.
    private static bool Sleeps(Brick brick)
    {
        if (brick.Quiet < StepsToSleep)
        {
            return false;
        }

        foreach (Brick next in brick.Beside)
        {
            if (next != null && next.WetHigh.X >= 0 && next.Quiet < StepsToSleep)
            {
                return false;
            }
        }

        return true;
    }

    // Whether a brick's wet voxels reach its border on this side (0 east ... 5 south).
    private static bool Touches(Brick brick, int side) => side switch
    {
        0 => brick.WetHigh.X == Brick.Size - 1,
        1 => brick.WetLow.X == 0,
        2 => brick.WetHigh.Y == Brick.Size - 1,
        3 => brick.WetLow.Y == 0,
        4 => brick.WetHigh.Z == Brick.Size - 1,
        _ => brick.WetLow.Z == 0,
    };

    // The heads of a column of bricks, top down: a voxel's floor, plus all the water resting on it (its own and that
    // of the wet voxels over it, up to air, a solid or a closed face), plus the push of what is pressed in.
    private static void Heads(Brick[] column, float s)
    {
        int lx = Brick.Size, lz = Brick.Size, hx = -1, hz = -1;
        foreach (Brick brick in column)
        {
            lx = Math.Min(lx, brick.Low.X);
            lz = Math.Min(lz, brick.Low.Z);
            hx = Math.Max(hx, brick.High.X);
            hz = Math.Max(hz, brick.High.Z);
        }

        for (int z = lz; z <= hz; z++)
        {
            for (int x = lx; x <= hx; x++)
            {
                Settle(column, x, z);
                float above = 0f;
                int over = int.MaxValue;
                foreach (Brick brick in column)
                {
                    // A gap between bricks is air.
                    if (brick.Y != over - 1)
                    {
                        above = 0f;
                    }

                    over = brick.Y;
                    for (int y = Brick.Size - 1; y >= 0; y--)
                    {
                        int i = Brick.Index(x, y, z);
                        float room = 1f - brick.Solid[i];
                        float water = brick.Water[i];
                        if (room <= 0f || water <= Brick.Dry)
                        {
                            brick.Head[i] = Floor(brick, i, y, s);
                            above = 0f;
                            continue;
                        }

                        // What is over the voxel presses on it only as it fills: water over a voxel not full hangs over
                        // air and falls, its weight carried by nothing; the weight comes on over the last tenth of the
                        // voxel's room, so that no head jumps (a jump feeds the water energy and it never settles).
                        float carried = Math.Min(1f, Math.Max(0f, ((water / room) - (1f - Contact)) / Contact)) * above;
                        brick.Head[i] = Floor(brick, i, y, s) + ((water + carried) * s) + (Math.Max(0f, water - room) * Stiffness * s);
                        above = OpenBelow(brick, i, y) ? carried + water : 0f;
                    }
                }
            }
        }
    }

    // Water resting on a voxel with room left drops into it, bottom up through the column: water cannot hang over
    // water that is not full (it would fall at once), and once it cannot, a voxel's head never switches between
    // carrying the water over it and not. Water over an empty voxel is left to fall through the pipes.
    private static void Settle(Brick[] column, int x, int z)
    {
        Brick lower = null;
        int below = -1;
        for (int b = column.Length - 1; b >= 0; b--)
        {
            Brick brick = column[b];

            // A gap between bricks is air: nothing rests across it.
            if (lower != null && lower.Y != brick.Y - 1)
            {
                lower = null;
            }

            for (int y = 0; y < Brick.Size; y++)
            {
                int i = Brick.Index(x, y, z);
                if (lower != null && brick.Water[i] > Brick.Dry)
                {
                    float room = 1f - lower.Solid[below] - lower.Water[below];
                    if (room > 0f && lower.Water[below] > Brick.Dry && (lower.Closed[below] & Brick.ClosedY) == 0 && lower.Solid[below] < 1f && brick.Solid[i] <= 0f)
                    {
                        float drop = Math.Min(room, brick.Water[i]);
                        lower.Water[below] += drop;
                        if (drop > WakingDrop)
                        {
                            lower.Wets(below);
                        }

                        brick.Water[i] -= drop;
                    }
                }

                lower = brick;
                below = i;
            }
        }
    }

    // The bottom of a voxel's room for water: its solid share fills it from below.
    private static float Floor(Brick brick, int i, int y, float s) => (((brick.Y * Brick.Size) + y) * s) + (brick.Solid[i] * s);

    private static bool OpenBelow(Brick brick, int i, int y)
    {
        if (y > 0)
        {
            int below = i - (Brick.Size * Brick.Size);
            return brick.Solid[below] < 1f && (brick.Closed[below] & Brick.ClosedY) == 0;
        }

        Brick under = brick.Beside[3];
        int j = i + ((Brick.Size - 1) * Brick.Size * Brick.Size);
        return under != null && under.Solid[j] < 1f && (under.Closed[j] & Brick.ClosedY) == 0;
    }

    // The flows through each voxel's east, upper and north faces.
    private void Flows(Brick brick, float dt, float s)
    {
        for (int k = 0; k < brick.PartCount; k++)
        {
            int i = brick.Part[k];
            if (brick.Water[i] <= Brick.Dry && DryAhead(brick, i))
            {
                // Dry, and so are the voxels its own faces lead to: nothing flows through them.
                brick.FlowX[i] = brick.FlowY[i] = brick.FlowZ[i] = 0f;
                continue;
            }

            (int x, int y, int z) = Brick.Place(i);
            WantBeside(brick, x, y, z, i);
            brick.FlowX[i] = Flow(brick, i, y, 0, dt, s, brick.FlowX[i]);
            brick.FlowY[i] = Flow(brick, i, y, 2, dt, s, brick.FlowY[i]);
            brick.FlowZ[i] = Flow(brick, i, y, 4, dt, s, brick.FlowZ[i]);
        }
    }

    // Whether the voxels east, over and north of voxel i are dry (or have no brick).
    private static bool DryAhead(Brick brick, int i) => Dry(brick, i, 0) && Dry(brick, i, 2) && Dry(brick, i, 4);

    private static bool Dry(Brick brick, int i, int side) => !Next(brick, i, side, out Brick other, out int j) || other.Water[j] <= Brick.Dry;

    // A wet voxel on the brick's edge asks for the bricks its water may go into.
    private void WantBeside(Brick brick, int x, int y, int z, int i)
    {
        if (brick.Water[i] <= Brick.Dry || (x > 0 && x < Brick.Size - 1 && y > 0 && y < Brick.Size - 1 && z > 0 && z < Brick.Size - 1))
        {
            return;
        }

        for (int side = 0; side < 6; side++)
        {
            if (brick.Beside[side] == null && Crosses(x, y, z, side))
            {
                (int dx, int dy, int dz) = Offset(side);
                _grid.Want(brick.X + dx, brick.Y + dy, brick.Z + dz);
            }
        }
    }

    // The new flow of one of a voxel's own faces (side 0: east, 2: up, 4: north).
    private static float Flow(Brick brick, int i, int y, int side, float dt, float s, float flow)
    {
        if (!Next(brick, i, side, out Brick other, out int j) || other.Asleep || brick.Solid[i] >= 1f || other.Solid[j] >= 1f || (brick.Closed[i] & (1 << (side / 2))) != 0)
        {
            return 0f;
        }

        float wi = brick.Water[i];
        float wj = other.Water[j];
        if (wi <= Brick.Dry && wj <= Brick.Dry)
        {
            return 0f;
        }

        float area = side == 2 ? s * s : s * Wet(brick, i, other, j, y, s);
        if (area <= 0f)
        {
            return 0f;
        }

        flow += dt * Gravity * area / s * (brick.Head[i] - other.Head[j]);
        flow /= 1f + (dt * (Damping + (Drag * Math.Abs(flow) / area)));

        // Only water there is can leave.
        return (flow > 0f && wi <= Brick.Dry) || (flow < 0f && wj <= Brick.Dry) ? 0f : flow;
    }

    // How high the water stands across a side face, metres: from the higher of the two floors to the higher
    // of the two levels.
    private static float Wet(Brick brick, int i, Brick other, int j, int y, float s)
    {
        float bottom = (((brick.Y * Brick.Size) + y) * s) + (Math.Max(brick.Solid[i], other.Solid[j]) * s);
        float top = Math.Max(Level(brick, i, y, s), Level(other, j, y, s));
        return Math.Min(Math.Max(top - bottom, 0f), s);
    }

    private static float Level(Brick brick, int i, int y, float s) =>
        (((brick.Y * Brick.Size) + y) * s) + (Math.Min(brick.Solid[i] + brick.Water[i], 1f) * s);

    // How much of each voxel's outflow it can give this step: all of it, unless that is more than it has.
    private static void Shares(Brick brick, float dt, float s)
    {
        float volume = s * s * s;
        for (int k = 0; k < brick.PartCount; k++)
        {
            int i = brick.Part[k];
            (float west, float down, float south) = Behind(brick, i);
            float outflow = Math.Max(brick.FlowX[i], 0f) + Math.Max(brick.FlowY[i], 0f) + Math.Max(brick.FlowZ[i], 0f)
                + Math.Max(-west, 0f) + Math.Max(-down, 0f) + Math.Max(-south, 0f);
            float has = brick.Water[i] * volume;
            brick.Share[i] = outflow * dt > has ? has / (outflow * dt) : 1f;
        }
    }

    // Each face's flow scaled by the share its source voxel can give.
    private static void Scale(Brick brick)
    {
        for (int k = 0; k < brick.PartCount; k++)
        {
            int i = brick.Part[k];
            brick.FlowX[i] = Scaled(brick, i, 0, brick.FlowX[i]);
            brick.FlowY[i] = Scaled(brick, i, 2, brick.FlowY[i]);
            brick.FlowZ[i] = Scaled(brick, i, 4, brick.FlowZ[i]);
        }
    }

    private static float Scaled(Brick brick, int i, int side, float flow)
    {
        if (flow > 0f)
        {
            return flow * brick.Share[i];
        }

        return flow < 0f && Next(brick, i, side, out Brick other, out int j) ? flow * other.Share[j] : flow;
    }

    // The water moved: in through some faces, out through others; the sea takes what falls into it.
    private void Apply(Brick brick, float dt, float s)
    {
        if (brick.PartCount == 0)
        {
            return;
        }

        float perVolume = dt / (s * s * s);
        float strongest = 0f;
        int lx = Brick.Size, ly = Brick.Size, lz = Brick.Size, hx = -1, hy = -1, hz = -1;
        for (int k = 0; k < brick.PartCount; k++)
        {
            int i = brick.Part[k];
            strongest = Math.Max(strongest, Math.Max(Math.Abs(brick.FlowX[i]), Math.Max(Math.Abs(brick.FlowY[i]), Math.Abs(brick.FlowZ[i]))));
            (float west, float down, float south) = Behind(brick, i);
            float net = west + down + south - brick.FlowX[i] - brick.FlowY[i] - brick.FlowZ[i];
            float water = Math.Max(0f, brick.Water[i] + (net * perVolume));
            (int x, int y, int z) = Brick.Place(i);
            brick.Water[i] = ((brick.Y * Brick.Size) + y + 1) * s <= SeaLevel ? 0f : water;
            if (brick.Water[i] > Brick.Dry)
            {
                (lx, ly, lz) = (Math.Min(lx, x), Math.Min(ly, y), Math.Min(lz, z));
                (hx, hy, hz) = (Math.Max(hx, x), Math.Max(hy, y), Math.Max(hz, z));
            }
        }

        // The wet voxels are all in the part (water only moves into it), so their box is found here.
        brick.WetLow = (lx, ly, lz);
        brick.WetHigh = (hx, hy, hz);
        brick.Quiet = strongest < SleepingFlow ? brick.Quiet + 1 : 0;
    }

    // The flows of the faces a voxel shares with the voxels west, under and south of it, which those own
    // (positive: into this voxel); 0 where there is no brick.
    private static (float West, float Down, float South) Behind(Brick brick, int i) => (
        Next(brick, i, 1, out Brick west, out int w) ? west.FlowX[w] : 0f,
        Next(brick, i, 3, out Brick under, out int d) ? under.FlowY[d] : 0f,
        Next(brick, i, 5, out Brick south, out int so) ? south.FlowZ[so] : 0f);

    // The voxel beside voxel i on a side (0 east, 1 west, 2 up, 3 down, 4 north, 5 south): in the brick unless i is
    // on its edge that way, else in the brick beside; false where there is no brick.
    private static bool Next(Brick brick, int i, int side, out Brick other, out int j)
    {
        const int Last = Brick.Size - 1, Row = Brick.Size, Layer = Brick.Size * Brick.Size;
        bool edge;
        switch (side)
        {
            case 0:
                edge = (i & Last) == Last;
                j = edge ? i - Last : i + 1;
                break;
            case 1:
                edge = (i & Last) == 0;
                j = edge ? i + Last : i - 1;
                break;
            case 2:
                edge = i >= Last * Layer;
                j = edge ? i - (Last * Layer) : i + Layer;
                break;
            case 3:
                edge = i < Layer;
                j = edge ? i + (Last * Layer) : i - Layer;
                break;
            case 4:
                edge = (i & (Last * Row)) == Last * Row;
                j = edge ? i - (Last * Row) : i + Row;
                break;
            default:
                edge = (i & (Last * Row)) == 0;
                j = edge ? i + (Last * Row) : i - Row;
                break;
        }

        other = edge ? brick.Beside[side] : brick;
        return other != null;
    }

    private static bool Crosses(int x, int y, int z, int side) => side switch
    {
        0 => x == Brick.Size - 1,
        1 => x == 0,
        2 => y == Brick.Size - 1,
        3 => y == 0,
        4 => z == Brick.Size - 1,
        _ => z == 0,
    };

    private static (int X, int Y, int Z) Offset(int side) => side switch
    {
        0 => (1, 0, 0),
        1 => (-1, 0, 0),
        2 => (0, 1, 0),
        3 => (0, -1, 0),
        4 => (0, 0, 1),
        _ => (0, 0, -1),
    };
}
