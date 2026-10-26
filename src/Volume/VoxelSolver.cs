using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace FlowingWater.Volume;

/// <summary>
/// Moves the water of a <see cref="VoxelGrid"/>, without the game: the pipe model in three dimensions. Each face
/// between two voxels is a pipe whose flow keeps its momentum from step to step, carried along by the water coming
/// through it (a front running onto dry ground keeps the speed of the water behind it, water off an edge keeps going
/// the way it went), sped up by the difference of the two voxels' heads and slowed by drag. A voxel's head is the level its water pushes to: its floor plus all the
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

    // Drag on every pipe: a share of its flow lost each second, and, up and down, more the faster it runs (seconds a
    // metre).
    private const float Damping = 0.3f, Drag = 0.05f;

    // Sideways, the ground's friction instead (Manning's, of a natural bed): more the faster the water runs and far more
    // the thinner it is, so a sheet a few centimetres thick loses its speed within a metre or two (a stream's water
    // fanning out at its source does not go on sideways down the slope) while a deep surge runs on.
    private const float Roughness = 0.03f;

    private const float Bed = Gravity * Roughness * Roughness;

    // The friction's depths to the four thirds: so many steps from none to a voxel's side.
    private const int PowerSteps = 257;

    private static readonly float[] FourThirdsTable = [.. Enumerable.Range(0, PowerSteps).Select(k => (float)Math.Pow(k / (double)(PowerSteps - 1), 4.0 / 3.0))];

    // Water wets new ground only once it stands this deep, metres: through a side face, water goes into a voxel holding
    // less than a film only from a voxel holding at least this much. Without it a stream fanned out over a smooth slope
    // into a sheet a few millimetres thick, its wet patches coming and going; with it, as a real rivulet, it keeps to
    // the ground it wet, its front moving on only where the water stands deep enough, and a sheet stops spreading as
    // it thins. Water already on wet ground flows as before, and nothing is lost: a film left behind stays.
    private const float Wetting = 0.03f, Film = 0.002f;

    // The share of a voxel over which the weight of the water over it comes on as it fills (the last of its room).
    private const float Contact = 0.1f;

    // The fastest the water carries its momentum, metres a second (a fall of 45 m): a face holding almost no water
    // would otherwise give the flow through it a speed it cannot have.
    private const float FastestCarried = 30f;

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
    private const int Idle = 0, HeadsPhase = 1, CarryPhase = 2, FlowsPhase = 3, SharesPhase = 4, ScalePhase = 5, ApplyPhase = 6;

    private static readonly Func<bool> Always = () => true;

    // Each phase's part of the frame's time.
    private static readonly WaterPart[] Parts = [WaterPart.Other, WaterPart.Heads, WaterPart.Carry, WaterPart.Flows, WaterPart.Shares, WaterPart.Scale, WaterPart.Apply];

    // A brick's last index along a row, and the index steps a row and a layer.
    private const int Last = Brick.Size - 1, Row = Brick.Size, Layer = Brick.Size * Brick.Size;

    private readonly VoxelGrid _grid;

    private Brick[] _bricks = [];

    private Brick[][] _columns = [];

    private int _columnsVersion = -1;

    // The most water over a voxel each column found in its last heads, in voxels.
    private float[] _deepest = [];

    // Whether the last look over the bricks found none to step, and each brick's count of changes and quiet seconds
    // then (in the order of _bricks). While every brick keeps them (nothing poured, drained, read its solids again or
    // woke it: a brick's quiet grows only in a step) and none was made or forgotten, a look would find the same: it is
    // not taken, and still water costs nothing a frame.
    private bool _idle;

    private int[] _idleChanges = [];

    private float[] _idleQuiet = [];

    // Each brick's part found, by index, and each phase's work on the k-th column or brick (made once: a part of a
    // step is run every frame, and a closure made for each would be given to the collector by the thousand).
    private readonly Action<int> _part, _heads, _carry, _flows, _shares, _scale, _apply;

    // Voxels whose water, or the ground under them, was found not to be a number, and emptied (told of by the server).
    private int _notNumbers;

    // The looks over the bricks since last asked.
    private int _looks;

    private float _stiffness = Stiffness;

    // The step under way: its length, its phase (Idle when none) and how far through it, the stiffness its heads use.
    private float _length;

    private int _phase = Idle;

    private int _cursor;

    private float _headsStiffness;

    // What tells the step under way whether the frame has time left (new bricks are made only while it has).
    private Func<bool> _stepTimeLeft = Always;

    // Where the frame's time goes: the step's beginning, each phase, the bricks made after it.
    private readonly FrameParts _parts;

    /// <summary>A solver for this grid, its time told apart in <paramref name="parts"/> when given.</summary>
    public VoxelSolver(VoxelGrid grid, FrameParts parts = null)
    {
        _grid = grid;
        _parts = parts ?? new FrameParts();
        _part = k => Part(_bricks[k]);
        _heads = k => _deepest[k] = Heads(_columns[k], _grid.VoxelSize, _headsStiffness);
        _carry = k => Carry(_bricks[k], _length, _grid.VoxelSize);
        _flows = k => Flows(_bricks[k], _length, _grid.VoxelSize);
        _shares = k => Shares(_bricks[k], _length, _grid.VoxelSize);
        _scale = k => Scale(_bricks[k]);
        _apply = k => Apply(_bricks[k], _length, _grid.VoxelSize);
    }

    /// <summary>Whether a step is under way (<see cref="Advance"/> stopped in it): the grid must not change.</summary>
    public bool InStep => _phase != Idle;

    /// <summary>The bricks the last look over them found to step (awake, with water in or beside them).</summary>
    public int Awake { get; private set; }

    /// <summary>Water whose voxel's top is at or below this level (metres) is gone: the sea takes it.</summary>
    public float SeaLevel { get; set; } = float.NegativeInfinity;

    /// <summary>
    /// How many voxels were found holding water that is not a number (or endless) since last asked, and emptied, and
    /// forgets them. A step never stops on one: the water round it goes on.
    /// </summary>
    public int TakeNotNumbers() => Interlocked.Exchange(ref _notNumbers, 0);

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
        WaterPart was = _parts.Into(WaterPart.Begin);
        bool any = Gather();
        _parts.Into(was);
        if (!any)
        {
            MakeWanted();
            return false;
        }

        return true;
    }

    // The bricks and columns as they are now, and each brick's part; false when every brick sleeps or is dry.
    private bool Gather()
    {
        if (_columnsVersion != _grid.Version)
        {
            _columnsVersion = _grid.Version;
            _bricks = [.. _grid.Bricks];
            _columns = [.. _bricks.GroupBy(b => ((long)b.X << 32) | (uint)b.Z).Select(g => g.OrderByDescending(b => b.Y).ToArray())];
            _deepest = new float[_columns.Length];
            _idleChanges = new int[_bricks.Length];
            _idleQuiet = new float[_bricks.Length];
            _idle = false;
        }
        else if (_idle && Unstirred())
        {
            return false;
        }

        _looks++;
        Parallel.For(0, _bricks.Length, _part);
        int awake = 0;
        for (int k = 0; k < _bricks.Length; k++)
        {
            awake += _bricks[k].PartCount > 0 ? 1 : 0;
        }

        Awake = awake;
        _idle = awake == 0;
        if (_idle)
        {
            for (int k = 0; k < _bricks.Length; k++)
            {
                _idleChanges[k] = _bricks[k].Changes;
                _idleQuiet[k] = _bricks[k].Quiet;
            }
        }

        return !_idle;
    }

    // Whether every brick is as the last look found it, nothing to step.
    private bool Unstirred()
    {
        for (int k = 0; k < _bricks.Length; k++)
        {
            if (_bricks[k].Changes != _idleChanges[k] || _bricks[k].Quiet < _idleQuiet[k])
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// How many times the bricks were looked over for a step to begin since last asked (none while all the water
    /// sleeps), and forgets them.
    /// </summary>
    public int TakeLooks()
    {
        int looks = _looks;
        _looks = 0;
        return looks;
    }

    // The bricks asked for during the step made, while the frame has time.
    private void MakeWanted()
    {
        WaterPart was = _parts.Into(WaterPart.Made);
        _grid.MakeWanted(_stepTimeLeft);
        _parts.Into(was);
    }

    // One phase of the step under way, over columns (heads) or bricks from..to.
    private void Run(int phase, int from, int to)
    {
        WaterPart was = _parts.Into(Parts[phase]);
        Parallel.For(from, to, phase switch
        {
            HeadsPhase => _heads,
            CarryPhase => _carry,
            FlowsPhase => _flows,
            SharesPhase => _shares,
            ScalePhase => _scale,
            _ => _apply,
        });
        _parts.Into(was);
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
            MakeWanted();
            return StepState.Done;
        }

        _phase++;
        return null;
    }

    // After the heads: the stiffness follows the deepest water found, and the momentum carried comes next. Water deeper than the
    // heads' stiffness was for (just loaded, or filled up) has its heads worked out again at once instead, or, when
    // the step is now too long for it, the step is not taken (null while the step goes on).
    private StepState? Stiffened()
    {
        float deepest = 0f;
        foreach (float column in _deepest)
        {
            // Water that is not a number would make the stiffness, and the longest stable step, none either: the heads
            // would be worked out again for ever. It is counted here and emptied as the step is applied.
            if (float.IsNaN(column) || float.IsInfinity(column))
            {
                Interlocked.Increment(ref _notNumbers);
                continue;
            }

            deepest = Math.Max(deepest, column);
        }

        _stiffness = Math.Max(Math.Max(Stiffness, StiffnessPerDepth * deepest), _stiffness * StiffnessEasing);
        if (_stiffness <= _headsStiffness)
        {
            _phase = CarryPhase;
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
        brick.Asked = 0;

        (int wlx, int wly, int wlz) = brick.WetLow;
        (int whx, int why, int whz) = brick.WetHigh;
        bool wet = whx >= 0;
        int lx = wet ? Math.Max(0, wlx - 1) : Brick.Size, ly = wet ? Math.Max(0, wly - 1) : Brick.Size, lz = wet ? Math.Max(0, wlz - 1) : Brick.Size;
        int hx = wet ? Math.Min(Last, whx + 1) : -1, hy = wet ? Math.Min(Last, why + 1) : -1, hz = wet ? Math.Min(Last, whz + 1) : -1;
        for (int side = 0; side < 6; side++)
        {
            if (brick.Beside[side] is not { } next || !next.WetTo(side ^ 1))
            {
                continue;
            }

            // The border layer facing it, only as far across as its wet voxels reach (a voxel more): its water comes
            // over face to face. The whole layer took the part out to a slab, or the whole brick between two wet
            // neighbours, however little water this brick held.
            int axis = side / 2;
            int layer = side % 2 == 0 ? Last : 0;
            (int nlx, int nly, int nlz) = next.WetLow;
            (int nhx, int nhy, int nhz) = next.WetHigh;
            lx = Math.Min(lx, axis == 0 ? layer : Math.Max(0, nlx - 1));
            hx = Math.Max(hx, axis == 0 ? layer : Math.Min(Last, nhx + 1));
            ly = Math.Min(ly, axis == 1 ? layer : Math.Max(0, nly - 1));
            hy = Math.Max(hy, axis == 1 ? layer : Math.Min(Last, nhy + 1));
            lz = Math.Min(lz, axis == 2 ? layer : Math.Max(0, nlz - 1));
            hz = Math.Max(hz, axis == 2 ? layer : Math.Min(Last, nhz + 1));
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

    // A brick sleeps when it and every brick beside it whose water reaches the face between them have been quiet long
    // enough: water moving in a brick beside it comes over only through that face, and a brick is looked at every
    // step, so one whose water reaches the face wakes this one the step after.
    private static bool Sleeps(Brick brick)
    {
        if (brick.Quiet < SecondsToSleep)
        {
            return false;
        }

        for (int side = 0; side < 6; side++)
        {
            if (brick.Beside[side] is { } next && next.WetTo(side ^ 1) && next.Quiet < SecondsToSleep)
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
                        // air and falls, its weight carried by nothing; the weight comes on over the last tenth of a
                        // voxel under the top of its room, so that no head jumps (a jump feeds the water energy and it
                        // never settles). A voxel with less room than that (ground just under its top) carries as much
                        // of the weight as its water reaches, and is pressed fuller for the rest: the weight coming on
                        // over a tenth of its sliver of room was such a jump, and kept the water over it astir for ever.
                        float carried = Math.Min(1f, Math.Max(0f, (water - Math.Max(0f, room - Contact)) / Contact)) * above;
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

    // The water carries its momentum along: the flow through each of a voxel's own faces takes on the speed of the water
    // coming into the space round that face (the halves of the two voxels it joins) through the voxels' middles, from
    // behind or from ahead, as much as that water is of what the space holds. Upwind, and never past the speeds coming
    // in, so stable at any step. Where the water is still, nothing comes in and the flows stay as they were. Across, the
    // speed is not carried (water turns a little sooner): that cost twice as much again for a front a few hundredths of
    // a second slower.
    private static void Carry(Brick brick, float dt, float s)
    {
        int[] part = brick.Part;
        float[] cx = brick.CarriedX, cy = brick.CarriedY, cz = brick.CarriedZ;
        int count = brick.PartCount;
        for (int k = 0; k < count; k++)
        {
            int i = part[k];
            cx[i] = Carried(brick, i, 0, brick.FlowX, dt, s);
            cy[i] = Carried(brick, i, 1, brick.FlowY, dt, s);
            cz[i] = Carried(brick, i, 2, brick.FlowZ, dt, s);
        }
    }

    // The flow through voxel i's own face on this axis (0: east, 1: up, 2: north; flows: that axis's) once carried along.
    private static float Carried(Brick brick, int i, int axis, float[] flows, float dt, float s)
    {
        if (!Next(brick, i, axis * 2, out Brick other, out int j) || other.Asleep)
        {
            return 0f;
        }

        // The flows through the face behind voxel i and the face ahead of the voxel beside it: none, nor through this
        // face, and the water there is still.
        float flow = flows[i];
        bool hasBack = Next(brick, i, (axis * 2) + 1, out Brick back, out int b);
        float behind = hasBack ? FlowsOn(back, axis)[b] : 0f;
        float beyond = FlowsOn(other, axis)[j];
        if (flow == 0f && behind == 0f && beyond == 0f)
        {
            return 0f;
        }

        float area = Area(brick, i, axis, other, j, s);
        if (area <= 0f)
        {
            return 0f;
        }

        // What comes in, cubic metres a second, and that times its speed along the axis: through the middle of the voxel
        // behind (the speed through its face behind, into voxel i) and of the voxel ahead (through its face ahead).
        float inflow = 0f, carried = 0f;
        float through = 0.5f * (behind + flow);
        if (through > 0f)
        {
            float backArea = hasBack ? Area(back, b, axis, brick, i, s) : 0f;
            inflow += through;
            carried += through * (backArea > 0f ? Fastest(behind / backArea) : 0f);
        }

        float ahead = 0.5f * (flow + beyond);
        if (ahead < 0f)
        {
            inflow -= ahead;
            carried -= ahead * Speed(other, j, axis, s);
        }

        float speed = Fastest(flow / area);
        if (inflow <= 0f)
        {
            return speed * area;
        }

        // The space's water, cubic metres: the speed coming in takes it over as it fills it (wholly, once more comes in
        // in the step than it holds: a front onto dry ground).
        float held = 0.5f * (brick.Water[i] + other.Water[j]) * s * s * s;
        float share = dt * inflow >= held ? 1f : dt * inflow / held;
        return (speed + (share * ((carried / inflow) - speed))) * area;
    }

    // The speed of the water through voxel i's own face on this axis, metres a second: its flow over its area.
    private static float Speed(Brick brick, int i, int axis, float s)
    {
        float flow = FlowsOn(brick, axis)[i];
        if (flow == 0f || !Next(brick, i, axis * 2, out Brick other, out int j))
        {
            return 0f;
        }

        float area = Area(brick, i, axis, other, j, s);
        return area <= 0f ? 0f : Fastest(flow / area);
    }

    private static float Fastest(float speed) => speed > FastestCarried ? FastestCarried : speed < -FastestCarried ? -FastestCarried : speed;

    // The area water crosses a face by, square metres: all of it upward, only as high as the water stands sideways.
    private static float Area(Brick brick, int i, int axis, Brick other, int j, float s) =>
        axis == 1 ? s * s : s * Wet(brick, i, other, j, i >> 8, s);

    private static float[] FlowsOn(Brick brick, int axis) => axis == 0 ? brick.FlowX : axis == 1 ? brick.FlowY : brick.FlowZ;

    private static float[] CarriedOn(Brick brick, int axis) => axis == 0 ? brick.CarriedX : axis == 1 ? brick.CarriedY : brick.CarriedZ;

    // The voxel beside voxel i on this side (0 east, 1 west, 2 up, 3 down, 4 north, 5 south): in the brick unless i is
    // on its edge that way, else in the brick beside; false where there is no brick.
    private static bool Next(Brick brick, int i, int side, out Brick other, out int j)
    {
        int axis = side >> 1;
        int stride = axis == 0 ? 1 : axis == 1 ? Layer : Row;
        int along = axis == 0 ? i & Last : axis == 1 ? i >> 8 : (i >> 4) & Last;
        bool far = (side & 1) == 0;
        bool inside = far ? along != Last : along != 0;
        other = inside ? brick : brick.Beside[side];
        j = inside ? (far ? i + stride : i - stride) : (far ? i - (Last * stride) : i + (Last * stride));
        return other != null;
    }

    // The flows through each voxel's east, upper and north faces: as the water carried them, pushed by the heads.
    private void Flows(Brick brick, float dt, float s)
    {
        int[] part = brick.Part;
        float[] water = brick.Water, fx = brick.FlowX, fy = brick.FlowY, fz = brick.FlowZ;
        float[] cx = brick.CarriedX, cy = brick.CarriedY, cz = brick.CarriedZ;
        float side = (float)Math.Pow(s, 4.0 / 3.0);
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
            WantBeside(brick, i & Last, y, (i >> 4) & Last, i);
            fx[i] = hasEast ? Flow(brick, i, y, 0, east, e, dt, s, side, cx[i]) : 0f;
            fy[i] = hasUp ? Flow(brick, i, y, 1, up, u, dt, s, side, cy[i]) : 0f;
            fz[i] = hasNorth ? Flow(brick, i, y, 2, north, n, dt, s, side, cz[i]) : 0f;
        }
    }

    // Whether a voxel ahead is dry (or has no brick).
    private static bool Dry(bool has, Brick other, int j) => !has || other.Water[j] <= Brick.Dry;

    // A wet voxel on the brick's edge asks for the bricks its water may go into: not through a face the brick closes,
    // nor one into a brick forgotten there that was solid or closed it (it would be made, found dry and forgotten
    // again for as long as the water moves). Past the grid's reach there is nothing: that face holds the water as a
    // closed one does. Each brick beside is asked for once a step (the asking takes a lock), not once a border voxel.
    private void WantBeside(Brick brick, int x, int y, int z, int i)
    {
        if (brick.Water[i] <= Brick.Dry || (x > 0 && x < Last && y > 0 && y < Last && z > 0 && z < Last))
        {
            return;
        }

        for (int side = 0; side < 6; side++)
        {
            if (brick.Beside[side] == null && (brick.Asked & (1 << side)) == 0 && Crosses(x, y, z, side) && !brick.Shuts(side, i))
            {
                (int dx, int dy, int dz) = Offset(side);
                brick.Asked |= (byte)(1 << side);
                if (VoxelGrid.Within(brick.X + dx, brick.Y + dy, brick.Z + dz))
                {
                    _grid.Want(brick.X + dx, brick.Y + dy, brick.Z + dz);
                }
            }
        }
    }

    // The new flow of one of a voxel's own faces (side 0: east, 2: up, 4: north), from the flow the water carried there
    // (side: the voxel's side to the four thirds, for the ground's friction).
    private static float Flow(Brick brick, int i, int y, int axis, Brick other, int j, float dt, float s, float side, float flow)
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

        // A flow that is not a number (from water that was none, emptied since) would stay so for ever, and empty the
        // voxels on both sides every step: it starts again from rest.
        if (float.IsNaN(flow))
        {
            flow = 0f;
        }

        flow += dt * Gravity * area / s * (brick.Head[i] - other.Head[j]);
        float speed = (flow < 0f ? -flow : flow) / area;
        flow /= 1f + (dt * (Damping + (axis == 1 ? Drag * speed : Friction(brick, i, other, j, axis, speed, area, s, side))));

        // Sideways onto dry ground no lower than its own, only from water deep enough to wet it (no flow is none either
        // way).
        if (axis != 1 && Unwetting(brick, i, other, j, y, s, flow > 0f))
        {
            return 0f;
        }

        // Only water there is can leave.
        return (flow > 0f && wi <= Brick.Dry) || (flow < 0f && wj <= Brick.Dry) ? 0f : flow;
    }

    // The ground's friction on a side face's flow, a share lost each second: by the water's whole speed there, the
    // current across the face (the other side axis, from the two voxels' own faces on it, as carried: the flows are being
    // set) with the speed through it, over the depth to the four thirds (Manning's; side: the voxel's side to the four
    // thirds).
    private static float Friction(Brick brick, int i, Brick other, int j, int axis, float speed, float area, float s, float side)
    {
        int cross = 2 - axis;
        float across = 0.5f * (Math.Abs(CarriedOn(brick, cross)[i]) + Math.Abs(CarriedOn(other, cross)[j])) / area;
        return Bed * (float)Math.Sqrt((speed * speed) + (across * across)) / (FourThirds(area / (s * s)) * side);
    }

    // A share of a voxel's side (0 to 1) to the power four thirds, from a table: Math.Pow on every side face of every step
    // was a good part of the friction's cost.
    private static float FourThirds(float share)
    {
        float at = share * (PowerSteps - 1);
        if (at >= PowerSteps - 1)
        {
            return 1f;
        }

        int k = (int)at;
        float power = FourThirdsTable[k] + ((at - k) * (FourThirdsTable[k + 1] - FourThirdsTable[k]));
        return power > 1e-9f ? power : 1e-9f;
    }

    // Whether a flow sideways (out of voxel i if outward, else into it) would wet dry ground that is no lower than the
    // ground the water comes from, from water too thin to: such ground is wet only by water standing Wetting deep. Dry
    // ground lower down (down a slope, over an edge) is run onto freely.
    private static bool Unwetting(Brick brick, int i, Brick other, int j, int y, float s, bool outward)
    {
        (Brick fromBrick, int from, Brick toBrick, int to) = outward ? (brick, i, other, j) : (other, j, brick, i);
        return toBrick.Water[to] * s < Film && fromBrick.Water[from] * s < Wetting && Floor(toBrick, to, y, s) >= Floor(fromBrick, from, y, s) - Film;
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
        bool fed = false;
        for (int k = 0; k < count; k++)
        {
            int i = part[k];
            strongest = Stronger(Stronger(Stronger(strongest, fx[i]), fy[i]), fz[i]);
            float net = FromWest(brick, i) + FromBelow(brick, i) + FromSouth(brick, i) - fx[i] - fy[i] - fz[i];
            int y = i >> 8;
            float water = waters[i] + (net * perVolume);
            if (float.IsNaN(water) || float.IsInfinity(water))
            {
                // Not a number (nor is anything worked out from it): emptied and counted, the water round it going on.
                Interlocked.Increment(ref _notNumbers);
                water = 0f;
            }
            else if ((bottom + y + 1) * s <= SeaLevel)
            {
                // The sea's. A brick fed only here never holds water at a frame's end: that it was fed keeps it from being
                // forgotten as dry and made again (its solids read again) every so many seconds for as long as the water runs.
                fed |= water > Brick.Dry;
                water = 0f;
            }
            else if (water < 0f)
            {
                water = 0f;
            }

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
        brick.Fed |= fed;

        // Its water may be sent while the step goes on (a part at a time): the change is counted when it is made too.
        brick.Changes++;
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
