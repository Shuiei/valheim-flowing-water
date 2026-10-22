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

    private readonly VoxelGrid _grid;

    private Brick[] _bricks = [];

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

    private void StepOnce(float dt)
    {
        _bricks = [.. _grid.Bricks];
        float s = _grid.VoxelSize;
        Brick[][] columns = [.. _bricks.GroupBy(b => ((long)b.X << 32) | (uint)b.Z).Select(g => g.OrderByDescending(b => b.Y).ToArray())];
        Parallel.For(0, columns.Length, k => Heads(columns[k], s));
        Parallel.For(0, _bricks.Length, k => Flows(_bricks[k], dt, s));
        Parallel.For(0, _bricks.Length, k => Shares(_bricks[k], dt, s));
        Parallel.For(0, _bricks.Length, k => Scale(_bricks[k]));
        Parallel.For(0, _bricks.Length, k => Apply(_bricks[k], dt, s));
        _grid.MakeWanted();
    }

    // The heads of a column of bricks, top down: a voxel's floor, plus all the water resting on it (its own and that
    // of the wet voxels over it, up to air, a solid or a closed face), plus the push of what is pressed in.
    private static void Heads(Brick[] column, float s)
    {
        for (int z = 0; z < Brick.Size; z++)
        {
            for (int x = 0; x < Brick.Size; x++)
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
        for (int i = 0; i < Brick.Count; i++)
        {
            (int x, int y, int z) = Brick.Place(i);
            WantBeside(brick, x, y, z, i);
            brick.FlowX[i] = Flow(brick, i, x, y, z, 0, dt, s, brick.FlowX[i]);
            brick.FlowY[i] = Flow(brick, i, x, y, z, 2, dt, s, brick.FlowY[i]);
            brick.FlowZ[i] = Flow(brick, i, x, y, z, 4, dt, s, brick.FlowZ[i]);
        }
    }

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
    private static float Flow(Brick brick, int i, int x, int y, int z, int side, float dt, float s, float flow)
    {
        if (!Next(brick, x, y, z, side, out Brick other, out int j) || brick.Solid[i] >= 1f || other.Solid[j] >= 1f || (brick.Closed[i] & (1 << (side / 2))) != 0)
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
        for (int i = 0; i < Brick.Count; i++)
        {
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
        for (int i = 0; i < Brick.Count; i++)
        {
            (int x, int y, int z) = Brick.Place(i);
            brick.FlowX[i] = Scaled(brick, i, x, y, z, 0, brick.FlowX[i]);
            brick.FlowY[i] = Scaled(brick, i, x, y, z, 2, brick.FlowY[i]);
            brick.FlowZ[i] = Scaled(brick, i, x, y, z, 4, brick.FlowZ[i]);
        }
    }

    private static float Scaled(Brick brick, int i, int x, int y, int z, int side, float flow)
    {
        if (flow > 0f)
        {
            return flow * brick.Share[i];
        }

        return flow < 0f && Next(brick, x, y, z, side, out Brick other, out int j) ? flow * other.Share[j] : flow;
    }

    // The water moved: in through some faces, out through others; the sea takes what falls into it.
    private void Apply(Brick brick, float dt, float s)
    {
        float perVolume = dt / (s * s * s);
        for (int i = 0; i < Brick.Count; i++)
        {
            (float west, float down, float south) = Behind(brick, i);
            float net = west + down + south - brick.FlowX[i] - brick.FlowY[i] - brick.FlowZ[i];
            float water = Math.Max(0f, brick.Water[i] + (net * perVolume));
            brick.Water[i] = ((brick.Y * Brick.Size) + Brick.Place(i).Y + 1) * s <= SeaLevel ? 0f : water;
        }
    }

    // The flows of the faces a voxel shares with the voxels west, under and south of it, which those own
    // (positive: into this voxel); 0 where there is no brick.
    private static (float West, float Down, float South) Behind(Brick brick, int i)
    {
        (int x, int y, int z) = Brick.Place(i);
        return (
            Next(brick, x, y, z, 1, out Brick west, out int w) ? west.FlowX[w] : 0f,
            Next(brick, x, y, z, 3, out Brick under, out int d) ? under.FlowY[d] : 0f,
            Next(brick, x, y, z, 5, out Brick south, out int so) ? south.FlowZ[so] : 0f);
    }

    // The voxel beside (x, y, z) on a side (0 east, 1 west, 2 up, 3 down, 4 north, 5 south), maybe in the next
    // brick; false where there is no brick.
    private static bool Next(Brick brick, int x, int y, int z, int side, out Brick other, out int j)
    {
        (int dx, int dy, int dz) = Offset(side);
        int nx = x + dx;
        int ny = y + dy;
        int nz = z + dz;
        other = Crosses(x, y, z, side) ? brick.Beside[side] : brick;
        j = Brick.Index(nx & (Brick.Size - 1), ny & (Brick.Size - 1), nz & (Brick.Size - 1));
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
