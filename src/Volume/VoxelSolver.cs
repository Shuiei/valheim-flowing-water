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

    // Metres of head, per voxel side, for each share of a voxel held over full (water pressed in), at least. Stiff
    // enough that water falling into a full voxel is pushed on rather than bouncing back (softer kept a pool sloshing
    // for ever).
    private const float Stiffness = 12f;

    // Deeper water needs stiffer: pressed water must push back harder than the weight over it, or a voxel deep down
    // empties under a column that cannot come down into it, and the pool sloshes for ever. The stiffness used is this
    // many times the most water over any voxel (in voxels), eased down by StiffnessEasing a step once the water is
    // shallower, so that a deep pool waking after a sleep finds it as it was.
    private const float StiffnessPerDepth = 3f;

    private const float StiffnessEasing = 0.99f;

    // Drag on every pipe: a share of its flow lost each second, and more the faster it runs (seconds a metre).
    private const float Damping = 0.3f, Drag = 0.05f;

    // The share of a voxel's room over which the weight of the water over it comes on as it fills.
    private const float Contact = 0.1f;

    // The deepest water steps are kept stable for, metres: longer steps are split.
    private const float DeepestStable = 10f;

    // A brick whose every flow stayed under this share of a voxel a second for this many seconds sleeps: it is
    // skipped while the bricks beside it sleep too or are dry (water poured in, a solid changed, a waking neighbour or
    // a brick made where its water meets the edge wakes it). The same at any voxel size; at 1 m, 24 of the longest
    // steps, as when it was counted in steps.
    private const float SleepingFlow = 1e-3f;

    private const float SecondsToSleep = 1.04f;

    // Water settling into a voxel wakes its brick when more than this share of a voxel: a still pool's last traces
    // settling do not keep it awake.
    private const float WakingDrop = 1e-4f;

    // A step's phases, in order.
    private const int Idle = 0, HeadsPhase = 1, FlowsPhase = 2, SharesPhase = 3, ScalePhase = 4, ApplyPhase = 5;

    private static readonly Func<bool> Always = () => true;

    // A brick's last index along a row, and the index steps a row and a layer.
    private const int Last = Brick.Size - 1, Row = Brick.Size, Layer = Brick.Size * Brick.Size;

    private readonly VoxelGrid _grid;

    private Brick[] _bricks = [];

    private Brick[][] _columns = [];

    private int _columnsVersion = -1;

    // The most water over a voxel each column found in its last heads, in voxels.
    private float[] _deepest = [];

    private float _stiffness = Stiffness;

    // The step under way: its length, its phase (Idle when none) and how far through it, the stiffness its heads use.
    private float _length;

    private int _phase = Idle;

    private int _cursor;

    private float _headsStiffness;

    // What tells the step under way whether the frame has time left (new bricks are made only while it has).
    private Func<bool> _stepTimeLeft = Always;

    /// <summary>A solver for this grid.</summary>
    public VoxelSolver(VoxelGrid grid)
    {
        _grid = grid;
    }

    /// <summary>Whether a step is under way (<see cref="Advance"/> stopped in it): the grid must not change.</summary>
    public bool InStep => _phase != Idle;

    /// <summary>Water whose voxel's top is at or below this level (metres) is gone: the sea takes it.</summary>
    public float SeaLevel { get; set; } = float.NegativeInfinity;

    /// <summary>
    /// The longest step kept stable, seconds: a wave crosses at most half a voxel in it, in water as deep as
    /// <see cref="DeepestStable"/> or through pressed water, whichever is faster. Shorter while deep water stiffens.
    /// </summary>
    public float MaxStep => 0.5f * _grid.VoxelSize / (float)Math.Sqrt(Gravity * Math.Max(DeepestStable, (1f + _stiffness) * _grid.VoxelSize));

    /// <summary>Moves the water over this much time (in as many short steps as it takes to stay stable).</summary>
    public void Step(float dt)
    {
        // The longest stable step is asked again before each: deep water found on the way shortens it, and a step
        // that finds it too long for that water is not taken but split again.
        float left = dt;
        while (left > 0f)
        {
            int steps = Math.Max(1, (int)Math.Ceiling(left / MaxStep));
            float step = left / steps;
            StepState state = Advance(step, Always, int.MaxValue);
            if (state == StepState.Idle)
            {
                // Everything sleeps: the rest of the time is as still, with no short step left to look for it.
                return;
            }

            if (state == StepState.Done)
            {
                left = steps == 1 ? 0f : left - step;
            }
        }
    }

    /// <summary>
    /// Takes one step of <paramref name="dt"/> seconds, or goes on with the one under way (whose length is kept), a
    /// part at a time: between parts of <paramref name="chunk"/> bricks it stops once <paramref name="timeLeft"/> says
    /// so, to go on at the next call. Nothing else may change the grid while a step is under way.
    /// </summary>
    /// <returns>
    /// Whether the step is done, under way, to be taken again shorter (it found deeper water), or not taken as nothing
    /// was to step.
    /// </returns>
    public StepState Advance(float dt, Func<bool> timeLeft, int chunk)
    {
        _stepTimeLeft = timeLeft;
        if (_phase == Idle)
        {
            if (!Begin())
            {
                return StepState.Idle;
            }

            _length = dt;
            _phase = HeadsPhase;
            _cursor = 0;
            _headsStiffness = _stiffness;
        }

        while (timeLeft())
        {
            int count = _phase == HeadsPhase ? _columns.Length : _bricks.Length;
            int end = (int)Math.Min(count, (long)_cursor + chunk);
            Run(_phase, _cursor, end);
            _cursor = end;
            if (_cursor == count && EndPhase() is { } state)
            {
                return state;
            }
        }

        return StepState.Pending;
    }

    // A step begins: the bricks and columns as they are now, and each brick's part. False when nothing is to step
    // (everything sleeps or is dry).
    private bool Begin()
    {
        if (_columnsVersion != _grid.Version)
        {
            _columnsVersion = _grid.Version;
            _bricks = [.. _grid.Bricks];
            _columns = [.. _bricks.GroupBy(b => ((long)b.X << 32) | (uint)b.Z).Select(g => g.OrderByDescending(b => b.Y).ToArray())];
            _deepest = new float[_columns.Length];
        }

        Parallel.For(0, _bricks.Length, k => Part(_bricks[k]));
        if (Array.TrueForAll(_bricks, b => b.PartCount == 0))
        {
            _grid.MakeWanted(_stepTimeLeft);
            return false;
        }

        return true;
    }

    // One phase of the step under way, over columns (heads) or bricks from..to.
    private void Run(int phase, int from, int to)
    {
        float s = _grid.VoxelSize;
        float dt = _length;
        switch (phase)
        {
            case HeadsPhase:
                float stiffness = _headsStiffness;
                Parallel.For(from, to, k => _deepest[k] = Heads(_columns[k], s, stiffness));
                break;
            case FlowsPhase:
                Parallel.For(from, to, k => Flows(_bricks[k], dt, s));
                break;
            case SharesPhase:
                Parallel.For(from, to, k => Shares(_bricks[k], dt, s));
                break;
            case ScalePhase:
                Parallel.For(from, to, k => Scale(_bricks[k]));
                break;
            default:
                Parallel.For(from, to, k => Apply(_bricks[k], dt, s));
                break;
        }
    }

    // A phase done: the next one, or the step's end (null while the step goes on).
    private StepState? EndPhase()
    {
        _cursor = 0;
        if (_phase == HeadsPhase)
        {
            return Stiffened();
        }

        // The step is over before new bricks are made: should making one fail, the step is not applied again.
        if (_phase == ApplyPhase)
        {
            _phase = Idle;
            _grid.MakeWanted(_stepTimeLeft);
            return StepState.Done;
        }

        _phase++;
        return null;
    }

    // After the heads: the stiffness follows the deepest water found, and the flows come next. Water deeper than the
    // heads' stiffness was for (just loaded, or filled up) has its heads worked out again at once instead, or, when
    // the step is now too long for it, the step is not taken (null while the step goes on).
    private StepState? Stiffened()
    {
        float deepest = 0f;
        foreach (float column in _deepest)
        {
            deepest = Math.Max(deepest, column);
        }

        _stiffness = Math.Max(Math.Max(Stiffness, StiffnessPerDepth * deepest), _stiffness * StiffnessEasing);
        if (_stiffness <= _headsStiffness)
        {
            _phase = FlowsPhase;
            return null;
        }

        if (_length > MaxStep)
        {
            _phase = Idle;
            return StepState.Again;
        }

        _headsStiffness = _stiffness;
        return null;
    }

    // The part a step works on: the wet voxels and those round them, the border layers facing a brick wet at that
    // border (its water may come over), and last step's part (its flows must be set again, to nothing if dry); none
    // for a sleeping brick.
    private static void Part(Brick brick)
    {
        if (Sleeps(brick))
        {
            // Falling asleep, its flows are let go: no water may cross its faces while it sleeps (one side would
            // count what the other does not), and what only stepping uses is given back until it wakes.
            if (!brick.Asleep)
            {
                brick.Rest();
                brick.LastHigh = (-1, -1, -1);
                brick.Changes++;
            }

            brick.Asleep = true;
            brick.PartCount = 0;
            return;
        }

        brick.Asleep = false;
        brick.Wake();
        brick.Changes++;

        (int wlx, int wly, int wlz) = brick.WetLow;
        (int whx, int why, int whz) = brick.WetHigh;
        bool wet = whx >= 0;
        int lx = wet ? Math.Max(0, wlx - 1) : Brick.Size, ly = wet ? Math.Max(0, wly - 1) : Brick.Size, lz = wet ? Math.Max(0, wlz - 1) : Brick.Size;
        int hx = wet ? Math.Min(Brick.Size - 1, whx + 1) : -1, hy = wet ? Math.Min(Brick.Size - 1, why + 1) : -1, hz = wet ? Math.Min(Brick.Size - 1, whz + 1) : -1;
        for (int side = 0; side < 6; side++)
        {
            if (brick.Beside[side] is not { } next || !next.WetTo(side ^ 1))
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
        if (brick.Quiet < SecondsToSleep)
        {
            return false;
        }

        foreach (Brick next in brick.Beside)
        {
            if (next != null && next.WetHigh.X >= 0 && next.Quiet < SecondsToSleep)
            {
                return false;
            }
        }

        return true;
    }

    // The heads of a column of bricks, top down: a voxel's floor, plus all the water resting on it (its own and that
    // of the wet voxels over it, up to air, a solid or a closed face), plus the push of what is pressed in. Returns
    // the most water any voxel of the column carries, in voxels.
    private static float Heads(Brick[] column, float s, float stiffness)
    {
        float deepest = 0f;

        // Over the parts of the bricks stepped (a sleeping one keeps its last part's bounds, not looked at). Settle
        // still goes down the whole column, so water over a sleeping brick still settles into it.
        int lx = Brick.Size, lz = Brick.Size, hx = -1, hz = -1;
        foreach (Brick brick in column)
        {
            if (brick.PartCount == 0)
            {
                continue;
            }

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
                            if (!brick.Asleep)
                            {
                                brick.Head[i] = Floor(brick, i, y, s);
                            }

                            above = 0f;
                            continue;
                        }

                        // What is over the voxel presses on it only as it fills: water over a voxel not full hangs over
                        // air and falls, its weight carried by nothing; the weight comes on over the last tenth of the
                        // voxel's room, so that no head jumps (a jump feeds the water energy and it never settles).
                        float carried = Math.Min(1f, Math.Max(0f, ((water / room) - (1f - Contact)) / Contact)) * above;
                        if (!brick.Asleep)
                        {
                            brick.Head[i] = Floor(brick, i, y, s) + ((water + carried) * s) + (Math.Max(0f, water - room) * stiffness * s);
                        }

                        deepest = Math.Max(deepest, carried);
                        above = OpenBelow(brick, i, y) ? carried + water : 0f;
                    }
                }
            }
        }

        return deepest;
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
                // Between sleeping voxels nothing settles: their water is still, and a sleeping brick's data is
                // sent again only when it counts a change.
                int i = Brick.Index(x, y, z);
                if (lower != null && brick.Water[i] > Brick.Dry && !(brick.Asleep && lower.Asleep))
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
                        brick.Changes++;
                        lower.Changes++;
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
        int[] part = brick.Part;
        float[] water = brick.Water, fx = brick.FlowX, fy = brick.FlowY, fz = brick.FlowZ;
        int count = brick.PartCount;
        for (int k = 0; k < count; k++)
        {
            int i = part[k];
            bool hasEast = East(brick, i, out Brick east, out int e);
            bool hasUp = Up(brick, i, out Brick up, out int u);
            bool hasNorth = North(brick, i, out Brick north, out int n);
            if (water[i] <= Brick.Dry && Dry(hasEast, east, e) && Dry(hasUp, up, u) && Dry(hasNorth, north, n))
            {
                // Dry, and so are the voxels its own faces lead to: nothing flows through them.
                fx[i] = fy[i] = fz[i] = 0f;
                continue;
            }

            int y = i >> 8;
            WantBeside(brick, i & (Brick.Size - 1), y, (i >> 4) & (Brick.Size - 1), i);
            fx[i] = hasEast ? Flow(brick, i, y, 0, east, e, dt, s, fx[i]) : 0f;
            fy[i] = hasUp ? Flow(brick, i, y, 1, up, u, dt, s, fy[i]) : 0f;
            fz[i] = hasNorth ? Flow(brick, i, y, 2, north, n, dt, s, fz[i]) : 0f;
        }
    }

    // Whether a voxel ahead is dry (or has no brick).
    private static bool Dry(bool has, Brick other, int j) => !has || other.Water[j] <= Brick.Dry;

    // A wet voxel on the brick's edge asks for the bricks its water may go into: not through a face the brick closes,
    // nor one into a brick forgotten there that was solid or closed it (it would be made, found dry and forgotten
    // again for as long as the water moves). Past the grid's reach there is nothing: that face holds the water as a
    // closed one does.
    private void WantBeside(Brick brick, int x, int y, int z, int i)
    {
        if (brick.Water[i] <= Brick.Dry || (x > 0 && x < Brick.Size - 1 && y > 0 && y < Brick.Size - 1 && z > 0 && z < Brick.Size - 1))
        {
            return;
        }

        for (int side = 0; side < 6; side++)
        {
            if (brick.Beside[side] == null && Crosses(x, y, z, side) && !brick.Shuts(side, i))
            {
                (int dx, int dy, int dz) = Offset(side);
                if (VoxelGrid.Within(brick.X + dx, brick.Y + dy, brick.Z + dz))
                {
                    _grid.Want(brick.X + dx, brick.Y + dy, brick.Z + dz);
                }
            }
        }
    }

    // The new flow of one of a voxel's own faces (side 0: east, 2: up, 4: north).
    private static float Flow(Brick brick, int i, int y, int axis, Brick other, int j, float dt, float s, float flow)
    {
        // Most faces in a step's part have no water on either side: that is looked at first.
        float wi = brick.Water[i];
        float wj = other.Water[j];
        if ((wi <= Brick.Dry && wj <= Brick.Dry) || other.Asleep || brick.Solid[i] >= 1f || other.Solid[j] >= 1f || (brick.Closed[i] & (1 << axis)) != 0)
        {
            return 0f;
        }

        float area = axis == 1 ? s * s : s * Wet(brick, i, other, j, y, s);
        if (area <= 0f)
        {
            return 0f;
        }

        flow += dt * Gravity * area / s * (brick.Head[i] - other.Head[j]);
        flow /= 1f + (dt * (Damping + (Drag * (flow < 0f ? -flow : flow) / area)));

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
        int[] part = brick.Part;
        float[] fx = brick.FlowX, fy = brick.FlowY, fz = brick.FlowZ, water = brick.Water, share = brick.Share;
        int count = brick.PartCount;
        for (int k = 0; k < count; k++)
        {
            int i = part[k];
            float west = FromWest(brick, i), down = FromBelow(brick, i), south = FromSouth(brick, i);
            float outflow = Out(fx[i]) + Out(fy[i]) + Out(fz[i]) + Out(-west) + Out(-down) + Out(-south);
            float has = water[i] * volume;
            share[i] = outflow * dt > has ? has / (outflow * dt) : 1f;
        }
    }

    // Each face's flow scaled by the share its source voxel can give.
    private static void Scale(Brick brick)
    {
        int[] part = brick.Part;
        float[] fx = brick.FlowX, fy = brick.FlowY, fz = brick.FlowZ, share = brick.Share;
        int count = brick.PartCount;
        for (int k = 0; k < count; k++)
        {
            int i = part[k];
            float own = share[i];
            fx[i] = fx[i] > 0f ? fx[i] * own : fx[i] < 0f && East(brick, i, out Brick east, out int e) ? fx[i] * east.Share[e] : fx[i];
            fy[i] = fy[i] > 0f ? fy[i] * own : fy[i] < 0f && Up(brick, i, out Brick up, out int u) ? fy[i] * up.Share[u] : fy[i];
            fz[i] = fz[i] > 0f ? fz[i] * own : fz[i] < 0f && North(brick, i, out Brick north, out int n) ? fz[i] * north.Share[n] : fz[i];
        }
    }

    // The larger of a strength and a flow's (either way).
    private static float Stronger(float strongest, float flow) => flow > strongest ? flow : -flow > strongest ? -flow : strongest;

    // A range taken out to v if it is not in it yet.
    private static void Widen(ref int low, ref int high, int v)
    {
        if (v < low)
        {
            low = v;
        }

        if (v > high)
        {
            high = v;
        }
    }

    // What leaves through a face (a flow out of the voxel, positive), else 0.
    private static float Out(float flow) => flow > 0f ? flow : 0f;

    // The water moved: in through some faces, out through others; the sea takes what falls into it.
    private void Apply(Brick brick, float dt, float s)
    {
        if (brick.PartCount == 0)
        {
            return;
        }

        float perVolume = dt / (s * s * s);
        float strongest = 0f;
        int[] part = brick.Part;
        float[] fx = brick.FlowX, fy = brick.FlowY, fz = brick.FlowZ, waters = brick.Water;
        int count = brick.PartCount, bottom = brick.Y * Brick.Size;
        int lx = Brick.Size, ly = Brick.Size, lz = Brick.Size, hx = -1, hy = -1, hz = -1;
        for (int k = 0; k < count; k++)
        {
            int i = part[k];
            strongest = Stronger(Stronger(Stronger(strongest, fx[i]), fy[i]), fz[i]);
            float net = FromWest(brick, i) + FromBelow(brick, i) + FromSouth(brick, i) - fx[i] - fy[i] - fz[i];
            int y = i >> 8;
            float water = waters[i] + (net * perVolume);
            water = water < 0f || (bottom + y + 1) * s <= SeaLevel ? 0f : water;
            waters[i] = water;
            if (water > Brick.Dry)
            {
                Widen(ref lx, ref hx, i & Last);
                Widen(ref ly, ref hy, y);
                Widen(ref lz, ref hz, (i >> 4) & Last);
            }
        }

        // The wet voxels are all in the part (water only moves into it), so their box is found here.
        brick.WetLow = (lx, ly, lz);
        brick.WetHigh = (hx, hy, hz);
        brick.Quiet = strongest < SleepingFlow * s * s * s ? brick.Quiet + dt : 0f;
    }

    // The flow of the face a voxel shares with the voxel west (under, south) of it, which that one owns (positive:
    // into this voxel); 0 where there is no brick. Each way has its own lookup: the hot loops ask with the way known.
    private static float FromWest(Brick brick, int i)
    {
        if ((i & Last) != 0)
        {
            return brick.FlowX[i - 1];
        }

        Brick west = brick.Beside[1];
        return west == null ? 0f : west.FlowX[i + Last];
    }

    private static float FromBelow(Brick brick, int i)
    {
        if (i >= Layer)
        {
            return brick.FlowY[i - Layer];
        }

        Brick under = brick.Beside[3];
        return under == null ? 0f : under.FlowY[i + (Last * Layer)];
    }

    private static float FromSouth(Brick brick, int i)
    {
        if ((i & (Last * Row)) != 0)
        {
            return brick.FlowZ[i - Row];
        }

        Brick south = brick.Beside[5];
        return south == null ? 0f : south.FlowZ[i + (Last * Row)];
    }

    // The voxel east (over, north) of voxel i: in the brick unless i is on its edge that way, else in the brick
    // beside; false where there is no brick.
    private static bool East(Brick brick, int i, out Brick other, out int j)
    {
        bool inside = (i & Last) != Last;
        other = inside ? brick : brick.Beside[0];
        j = inside ? i + 1 : i - Last;
        return other != null;
    }

    private static bool Up(Brick brick, int i, out Brick other, out int j)
    {
        bool inside = i < Last * Layer;
        other = inside ? brick : brick.Beside[2];
        j = inside ? i + Layer : i - (Last * Layer);
        return other != null;
    }

    private static bool North(Brick brick, int i, out Brick other, out int j)
    {
        bool inside = (i & (Last * Row)) != Last * Row;
        other = inside ? brick : brick.Beside[4];
        j = inside ? i + Row : i - (Last * Row);
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

/// <summary>How a step taken a part at a time (<see cref="VoxelSolver.Advance"/>) stands.</summary>
internal enum StepState
{
    /// <summary>Done.</summary>
    Done,

    /// <summary>Not taken: nothing was to step (everything sleeps or is dry), nor is for any time asked now.</summary>
    Idle,

    /// <summary>Under way: the next call goes on with it.</summary>
    Pending,

    /// <summary>Not taken: the water is deeper than its length was chosen for; take a shorter one.</summary>
    Again,
}
