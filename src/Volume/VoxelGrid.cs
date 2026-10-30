using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowingWater.Volume;

/// <summary>What is solid where water may go: fills a new brick's solid shares and closed faces.</summary>
internal interface ISolids
{
    /// <summary>Sets the brick's <see cref="Brick.Solid"/> and <see cref="Brick.Closed"/>.</summary>
    /// <param name="brick">The new brick.</param>
    /// <param name="voxelSize">Metres along a voxel's side.</param>
    void Fill(Brick brick, float voxelSize);

    /// <summary>
    /// Whether a new brick at these brick coordinates can read its solids now without waiting on anything; if not,
    /// what it needs is asked for, to be ready at a later call.
    /// </summary>
    bool Ready(int x, int y, int z, float voxelSize);
}

/// <summary>
/// The water's 3D grid, without the game: voxels of one size, kept in bricks of 16 by 16 by 16 that exist only
/// where there is water or was lately. Voxel (x, y, z) spans x to x + 1 voxels east from the world's zero, y up and
/// z north. A brick is made when water reaches it (its solids then come from <see cref="ISolids"/>), and forgotten
/// once it has been dry for a while.
/// </summary>
internal sealed class VoxelGrid
{
    private static readonly (int X, int Y, int Z)[] Sides = [(1, 0, 0), (-1, 0, 0), (0, 1, 0), (0, -1, 0), (0, 0, 1), (0, 0, -1)];

    // Where displaced water goes first: up, then the sides, then down.
    private static readonly (int X, int Y, int Z)[] DisplaceOrder = [(0, 1, 0), (1, 0, 0), (-1, 0, 0), (0, 0, 1), (0, 0, -1), (0, -1, 0)];

    private readonly Dictionary<long, Brick> _bricks = [];

    private readonly HashSet<long> _wanted = [];

    // The bricks ForgetDry forgets, kept from call to call.
    private readonly List<Brick> _gone = [];

    // How long a jet is followed through the air at the most, seconds, and how often its place is looked at.
    private const float MostFlight = 6f, FlightStep = 0.02f;

    // A source's outlet's voxels with room as PourOutlet finds them, and the way its water leaves each.
    private readonly List<(int X, int Y, int Z, Vec3 Way)> _outlet = [];

    private readonly ISolids _solids;

    /// <summary>A grid of voxels this many metres along a side.</summary>
    public VoxelGrid(float voxelSize, ISolids solids)
    {
        VoxelSize = voxelSize;
        _solids = solids;
    }

    /// <summary>Metres along a voxel's side.</summary>
    public float VoxelSize { get; }

    /// <summary>A voxel's volume, cubic metres.</summary>
    public float VoxelVolume => VoxelSize * VoxelSize * VoxelSize;

    /// <summary>The most bricks there may be: water asking for more stays where it is.</summary>
    public int MaxBricks { get; set; } = int.MaxValue;

    /// <summary>Whether water has asked for a brick beyond <see cref="MaxBricks"/> (it stopped spreading).</summary>
    public bool Capped { get; private set; }

    /// <summary>A number that changes each time a brick is made or forgotten (a brick keeps the one it was made at, <see cref="Brick.MadeAt"/>).</summary>
    public int Version { get; private set; }

    /// <summary>The bricks there are.</summary>
    public IReadOnlyCollection<Brick> Bricks => _bricks.Values;

    /// <summary>
    /// The farthest brick from the world's zero along any axis: its key keeps 21 bits of each coordinate (over 16,000
    /// km of world at 1 m voxels, far beyond any Valheim world).
    /// </summary>
    public const int MaxBrick = (1 << 20) - 1;

    /// <summary>A brick's key from its brick coordinates (21 bits each).</summary>
    public static long Key(int x, int y, int z) => ((long)(x & 0x1FFFFF) << 42) | ((long)(y & 0x1FFFFF) << 21) | (uint)(z & 0x1FFFFF);

    /// <summary>The brick at these brick coordinates, or null.</summary>
    public Brick Find(int x, int y, int z) => _bricks.TryGetValue(Key(x, y, z), out Brick brick) ? brick : null;

    /// <summary>The brick at these brick coordinates, made (with its solids) if there was none.</summary>
    public Brick Get(int x, int y, int z)
    {
        InReach(x, y, z);
        if (Find(x, y, z) is { } found)
        {
            return found;
        }

        var brick = new Brick(x, y, z);
        _solids.Fill(brick, VoxelSize);
        _bricks[Key(x, y, z)] = brick;
        Version++;
        brick.MadeAt = Version;
        for (int side = 0; side < Sides.Length; side++)
        {
            Brick next = Find(x + Sides[side].X, y + Sides[side].Y, z + Sides[side].Z);
            brick.Beside[side] = next;
            if (next is { } linked)
            {
                linked.Beside[side ^ 1] = brick;

                // Water beside that went still against the face, held there while no brick was (MaxBricks, or the
                // brick made a few frames after it asked), wakes to run into it: a dry brick beside it never would.
                if (linked.WetTo(side ^ 1))
                {
                    linked.Quiet = 0;
                }
            }
        }

        return brick;
    }

    /// <summary>The brick and index of voxel (x, y, z); false where no brick holds it.</summary>
    public bool Locate(int x, int y, int z, out Brick brick, out int index)
    {
        brick = Find(Brick.Of(x), Brick.Of(y), Brick.Of(z));
        index = Brick.Index(Brick.In(x), Brick.In(y), Brick.In(z));
        return brick != null;
    }

    /// <summary>
    /// The brick at these brick coordinates for water to go in: made (with its solids) if there was none and fewer
    /// than <see cref="MaxBricks"/> are; null when that many are (<see cref="Capped"/> is then set).
    /// </summary>
    public Brick Make(int x, int y, int z)
    {
        InReach(x, y, z);
        if (Find(x, y, z) is { } found)
        {
            return found;
        }

        if (_bricks.Count >= MaxBricks)
        {
            Capped = true;
            return null;
        }

        return Get(x, y, z);
    }

    /// <summary>
    /// The voxel water poured at a point (world metres) goes into: its own, or the first open one over it (within four
    /// bricks); in its own voxel at or over the underside of a piece lying in the voxel's upper half (a floor there is
    /// that voxel's ceiling, <see cref="Brick.Ceiling"/>), the voxel above, onto the piece, not under it. False when none
    /// is open, a brick it needs cannot be made (<see cref="MaxBricks"/>), or the point is past the grid's reach.
    /// </summary>
    public bool Room(float px, float py, float pz, out int x, out int y, out int z)
    {
        float s = VoxelSize;
        x = (int)Math.Floor(px / s);
        y = (int)Math.Floor(py / s);
        z = (int)Math.Floor(pz / s);
        for (int up = 0; up < Brick.Size * 4; up++, y++)
        {
            if (!Within(Brick.Of(x), Brick.Of(y), Brick.Of(z)) || Make(Brick.Of(x), Brick.Of(y), Brick.Of(z)) is not { } brick)
            {
                return false;
            }

            int i = Brick.Index(Brick.In(x), Brick.In(y), Brick.In(z));
            if (brick.Solid[i] >= 1f)
            {
                continue;
            }

            float ceiling = brick.Ceiling[i];
            if (up == 0 && ceiling > 0f && py - (y * s) >= ceiling * s)
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>Pours this much water into voxel (x, y, z), cubic metres (its brick made if needed).</summary>
    /// <returns>False when its brick could not be made (<see cref="MaxBricks"/>): nothing poured.</returns>
    public bool Pour(int x, int y, int z, float cubicMetres)
    {
        if (Make(Brick.Of(x), Brick.Of(y), Brick.Of(z)) is not { } brick)
        {
            return false;
        }

        int i = Brick.Index(Brick.In(x), Brick.In(y), Brick.In(z));
        brick.Water[i] += cubicMetres / VoxelVolume;
        brick.EmptyFor = 0f;
        brick.Wets(i);
        return true;
    }

    /// <summary>
    /// Pours this much water into voxel (x, y, z), cubic metres, sent on its way: the flows through its faces the way
    /// it goes raised to at least the flow given along each axis (cubic metres a second; positive east, up and north),
    /// so a source's jet or river leaves it with its momentum, carried on and braked by the solver as any flow is.
    /// </summary>
    /// <returns>False when its brick could not be made (<see cref="MaxBricks"/>): nothing poured.</returns>
    public bool PourAimed(int x, int y, int z, float cubicMetres, float flowX, float flowY, float flowZ)
    {
        if (!Pour(x, y, z, cubicMetres))
        {
            return false;
        }

        Aim(x, y, z, 0, flowX);
        Aim(x, y, z, 1, flowY);
        Aim(x, y, z, 2, flowZ);
        return true;
    }

    /// <summary>
    /// Pours a source's water out of its outlet, the outlet's middle at a point (world metres), across the way the shape
    /// faces: a rate (m³/s) for a step, shared out evenly among the outlet's places with room (<see cref="Room"/>), each
    /// share leaving its voxel the way the outlet sends it there (fanned out across it, thrown up) at that share of the
    /// rate. An outlet partly against a wall or past the grid's reach pours all of it out of the rest. A jet thrown up
    /// (<see cref="SourceShape.Flies"/>) pours where it lands (<see cref="Lands"/>), on its way along the ground: its
    /// water flies there in the player's own water, and on the server none of it runs out at the outlet's foot.
    /// </summary>
    /// <returns>False when none of it had room: nothing poured.</returns>
    public bool PourOutlet(float px, float py, float pz, SourceShape shape, float rate, float dt)
    {
        (float rightX, float rightZ) = shape.Right;
        _outlet.Clear();
        float speed = shape.Speed(rate);
        foreach ((float across, float offset) in shape.Outlet(VoxelSize))
        {
            float ox = px + (rightX * offset), oy = py, oz = pz + (rightZ * offset);
            Vec3 way = shape.Direction(across);
            if (shape.Flies && Lands(ox, oy, oz, new Vec3(way.X * speed, way.Y * speed, way.Z * speed)) is { } landing)
            {
                (ox, oy, oz) = landing;
                way = new Vec3(way.X, 0f, way.Z);
            }

            if (Room(ox, oy, oz, out int x, out int y, out int z))
            {
                _outlet.Add((x, y, z, way));
            }
        }

        bool poured = false;
        foreach ((int x, int y, int z, Vec3 way) in _outlet)
        {
            float share = rate / _outlet.Count;
            poured |= PourAimed(x, y, z, share * dt, share * way.X, share * way.Y, share * way.Z);
        }

        return poured;
    }

    /// <summary>
    /// Where water leaving a point (world metres) at a velocity (m/s; x east, y up, z north) lands, gravity pulling it
    /// down as it flies: the last place it reaches before a voxel solid there or holding water, looked at every
    /// <see cref="FlightStep"/> seconds for <see cref="MostFlight"/> at the most (where it got to then, still falling).
    /// Null when it cannot leave the point (solid or water there already), or its way goes past the grid's reach or
    /// <see cref="MaxBricks"/>: it pours where it is.
    /// </summary>
    public (float X, float Y, float Z)? Lands(float px, float py, float pz, Vec3 velocity)
    {
        float s = VoxelSize;
        float x = px, y = py, z = pz, vy = velocity.Y;
        (float X, float Y, float Z)? open = null;
        for (float t = 0f; t <= MostFlight; t += FlightStep)
        {
            if (Hit(x, y, z) is not { } hit)
            {
                return null;
            }

            if (hit)
            {
                return open;
            }

            open = (x, y, z);
            x += velocity.X * FlightStep;
            z += velocity.Z * FlightStep;
            y += (vy * FlightStep) - (0.5f * VoxelSolver.Gravity * FlightStep * FlightStep);
            vy -= VoxelSolver.Gravity * FlightStep;
        }

        return open;

        // Whether a place is in a voxel's solid (its lower share, as much as is solid) or its water; null where no
        // brick can hold it, or the solids of one not made yet cannot be read without waiting (they are asked for:
        // till then the jet pours at its outlet). A brick the flight crosses is made (its solids read once, and kept
        // up with the ground's changes as any brick's are), and kept while the jet flies through it (Fed): forgotten
        // as dry, it was made again, its solids read again, every few seconds.
        bool? Hit(float hx, float hy, float hz)
        {
            int vx = (int)Math.Floor(hx / s), vyy = (int)Math.Floor(hy / s), vz = (int)Math.Floor(hz / s);
            int bx = Brick.Of(vx), by = Brick.Of(vyy), bz = Brick.Of(vz);
            if (!Within(bx, by, bz) || (Find(bx, by, bz) is null && !_solids.Ready(bx, by, bz, VoxelSize)) || Make(bx, by, bz) is not { } brick)
            {
                return null;
            }

            brick.Fed = true;

            int i = Brick.Index(Brick.In(vx), Brick.In(vyy), Brick.In(vz));
            return brick.Solid[i] >= 1f || (hy / s) - vyy < brick.Solid[i] || brick.Water[i] >= 0.5f;
        }
    }

    // The flow through a voxel's face along an axis raised to at least this one (its upper face going the positive
    // way, the one before it, the lower neighbour's upper face, going the negative way), its brick woken for it.
    private void Aim(int x, int y, int z, int axis, float flow)
    {
        if (!(Math.Abs(flow) > 0f))
        {
            return;
        }

        int back = flow < 0f ? 1 : 0;
        int fx = x - (axis == 0 ? back : 0), fy = y - (axis == 1 ? back : 0), fz = z - (axis == 2 ? back : 0);
        if (Make(Brick.Of(fx), Brick.Of(fy), Brick.Of(fz)) is not { } brick)
        {
            return;
        }

        brick.Wake();
        int i = Brick.Index(Brick.In(fx), Brick.In(fy), Brick.In(fz));
        float[] faces = axis == 0 ? brick.FlowX : axis == 1 ? brick.FlowY : brick.FlowZ;
        faces[i] = flow > 0f ? Math.Max(faces[i], flow) : Math.Min(faces[i], flow);
        brick.Wets(i);
    }

    /// <summary>The water in voxel (x, y, z), as a share of its volume; 0 where there is no brick.</summary>
    public float WaterAt(int x, int y, int z) => Locate(x, y, z, out Brick brick, out int index) ? brick.Water[index] : 0f;

    /// <summary>All the water there is, cubic metres.</summary>
    public float TotalWater()
    {
        double total = 0;
        foreach (Brick brick in _bricks.Values)
        {
            foreach (float water in brick.Water)
            {
                total += water;
            }
        }

        return (float)(total * VoxelVolume);
    }

    /// <summary>
    /// Reads a brick's solids again (a piece was built or broken there). Water in a voxel that turned solid moves to a
    /// voxel beside it it can reach (up first, then the sides, then down), never through a solid or a closed face: a
    /// piece built in a pool pushes the water aside, it neither destroys it nor lifts it over a floor. With nowhere to
    /// go, it stays.
    /// </summary>
    public void Refill(Brick brick)
    {
        Array.Clear(brick.Solid, 0, Brick.Count);
        Array.Clear(brick.Closed, 0, Brick.Count);
        Array.Clear(brick.Ceiling, 0, Brick.Count);
        _solids.Fill(brick, VoxelSize);

        // What changed by it may have opened its faces onto a brick forgotten beside it.
        brick.Unshut();

        // Its faces may have opened onto a sleeping brick beside it: they wake together. What the bricks beside send
        // changed too (their faces onto it, the ground under or beside their water): they are sent again.
        brick.Quiet = 0;
        brick.Changes++;
        foreach (Brick next in brick.Beside.Where(b => b != null))
        {
            next.Quiet = 0;
            next.Changes++;
        }

        for (int i = 0; i < Brick.Count; i++)
        {
            if (brick.Solid[i] >= 1f && brick.Water[i] > 0f)
            {
                (int x, int y, int z) = Brick.Place(i);
                if (Displace((brick.X * Brick.Size) + x, (brick.Y * Brick.Size) + y, (brick.Z * Brick.Size) + z, brick.Water[i]))
                {
                    brick.Water[i] = 0f;
                }
            }
        }
    }

    // Water out of solid voxel (x, y, z) into the first voxel beside it it can reach: up, east, west, north, south,
    // down (bricks made as needed, under MaxBricks). False when none is.
    private bool Displace(int x, int y, int z, float water)
    {
        foreach ((int dx, int dy, int dz) in DisplaceOrder)
        {
            if (Open(x, y, z, dx, dy, dz) is { } to)
            {
                to.Brick.Water[to.Index] += water;
                to.Brick.EmptyFor = 0f;
                to.Brick.Wets(to.Index);
                return true;
            }
        }

        return false;
    }

    // The voxel beside (x, y, z) one step (dx, dy, dz) away, when it is not solid and the face between is not closed.
    private (Brick Brick, int Index)? Open(int x, int y, int z, int dx, int dy, int dz)
    {
        // A face belongs to the voxel west, under or south of it: the step's own voxel going east, up or north, the
        // next one going the other way.
        int ox = dx < 0 ? x - 1 : x, oy = dy < 0 ? y - 1 : y, oz = dz < 0 ? z - 1 : z;
        byte bit = dx != 0 ? Brick.ClosedX : dy != 0 ? Brick.ClosedY : Brick.ClosedZ;
        if (Make(Brick.Of(ox), Brick.Of(oy), Brick.Of(oz)) is not { } owner || (owner.Closed[Brick.Index(Brick.In(ox), Brick.In(oy), Brick.In(oz))] & bit) != 0)
        {
            return null;
        }

        int i = Brick.Index(Brick.In(x + dx), Brick.In(y + dy), Brick.In(z + dz));
        return Make(Brick.Of(x + dx), Brick.Of(y + dy), Brick.Of(z + dz)) is { } brick && brick.Solid[i] < 1f ? (brick, i) : null;
    }

    /// <summary>Asks for the brick at these brick coordinates, to be made after the step (safe from any thread).</summary>
    public void Want(int x, int y, int z)
    {
        InReach(x, y, z);
        lock (_wanted)
        {
            _wanted.Add(Key(x, y, z));
        }
    }

    /// <summary>Makes the bricks asked for during the step.</summary>
    public void MakeWanted() => MakeWanted(static () => true);

    /// <summary>
    /// Makes the bricks asked for during the step while <paramref name="timeLeft"/> says so (one at least: making one
    /// reads the solids there, which can take a while); the others are made at a later call, as are those whose
    /// solids cannot be read yet without waiting (<see cref="ISolids.Ready"/>).
    /// </summary>
    public void MakeWanted(Func<bool> timeLeft)
    {
        long[] wanted;
        lock (_wanted)
        {
            // Called after every step and every frame the water sleeps: most often none is.
            if (_wanted.Count == 0)
            {
                return;
            }

            wanted = [.. _wanted];
            _wanted.Clear();
        }

        bool made = false;
        for (int k = 0; k < wanted.Length; k++)
        {
            if (_bricks.Count >= MaxBricks)
            {
                // Asked for again, to be made once room is freed: the water that asked may sleep and ask no more.
                Want(wanted, k);
                Capped = true;
                return;
            }

            if (made && !timeLeft())
            {
                Want(wanted, k);
                return;
            }

            int x = Unpack(wanted[k] >> 42), y = Unpack(wanted[k] >> 21), z = Unpack(wanted[k]);
            bool missing = Find(x, y, z) == null;
            if (missing && !_solids.Ready(x, y, z, VoxelSize))
            {
                Want(x, y, z);
                continue;
            }

            made |= missing;
            // Should reading its solids fail, it and those after it are asked for again; the failure goes on up.
            bool got = false;
            try
            {
                Get(x, y, z);
                got = true;
            }
            finally
            {
                if (!got)
                {
                    Want(wanted, k);
                }
            }
        }
    }

    // The bricks still wanted, from the k-th on, asked for again.
    private void Want(long[] wanted, int k)
    {
        lock (_wanted)
        {
            for (; k < wanted.Length; k++)
            {
                _wanted.Add(wanted[k]);
            }
        }
    }

    /// <summary>
    /// Forgets the bricks dry for longer than this, with the time since the last call. Called every frame: a brick
    /// whose water did not change since it was last looked at (asleep) is not looked over again, and one with water
    /// only within its wet voxels' box. A brick about to be forgotten has every voxel looked at, in case.
    /// </summary>
    /// <param name="dt">Seconds since the last call.</param>
    /// <param name="after">Seconds dry before a brick is forgotten.</param>
    public void ForgetDry(float dt, float after)
    {
        _gone.Clear();
        foreach (Brick brick in _bricks.Values)
        {
            brick.EmptyFor = brick.HoldsWater() || brick.Fed ? 0f : brick.EmptyFor + dt;
            brick.Fed = false;
            if (brick.EmptyFor > after)
            {
                if (brick.HoldsAnyWater())
                {
                    brick.EmptyFor = 0f;
                }
                else
                {
                    _gone.Add(brick);
                }
            }
        }

        foreach (Brick brick in _gone)
        {
            Forget(brick);
        }

        _gone.Clear();
    }

    /// <summary>
    /// Forgets a brick (and its water): it is unlinked from the bricks beside it, which keep the faces water cannot
    /// cross into it (no water asks for it again there).
    /// </summary>
    public void Forget(Brick brick)
    {
        _bricks.Remove(Key(brick.X, brick.Y, brick.Z));
        Version++;

        // Room again: water that asks for a brick now gets one (and a source with no room is told of again).
        Capped &= _bricks.Count >= MaxBricks;
        for (int side = 0; side < Sides.Length; side++)
        {
            if (brick.Beside[side] is { } next)
            {
                next.ShutBy(side ^ 1, brick);
                next.Beside[side ^ 1] = null;
            }
        }
    }

    /// <summary>Whether a brick at these brick coordinates is within the grid's reach (<see cref="MaxBrick"/>).</summary>
    public static bool Within(int x, int y, int z) =>
        x >= -MaxBrick && x <= MaxBrick && y >= -MaxBrick && y <= MaxBrick && z >= -MaxBrick && z <= MaxBrick;

    // A brick farther than the key can tell apart would share another's: refused before any lookup.
    private static void InReach(int x, int y, int z)
    {
        if (!Within(x, y, z))
        {
            throw new ArgumentOutOfRangeException(nameof(x), $"brick {x}, {y}, {z} is beyond the grid's reach ({MaxBrick} bricks from the world's zero)");
        }
    }

    // A 21-bit field back to a signed coordinate.
    private static int Unpack(long bits) => (int)((bits & 0x1FFFFF) << 43 >> 43);
}
