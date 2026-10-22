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

    private readonly Dictionary<long, Brick> _bricks = [];

    private readonly HashSet<long> _wanted = [];

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

    /// <summary>A number that changes each time a brick is made or forgotten.</summary>
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
        for (int side = 0; side < Sides.Length; side++)
        {
            Brick next = Find(x + Sides[side].X, y + Sides[side].Y, z + Sides[side].Z);
            brick.Beside[side] = next;
            if (next is { } linked)
            {
                linked.Beside[side ^ 1] = brick;
            }
        }

        return brick;
    }

    /// <summary>The brick and index of voxel (x, y, z); false where no brick holds it.</summary>
    public bool Locate(int x, int y, int z, out Brick brick, out int index)
    {
        brick = Find(x >> 4, y >> 4, z >> 4);
        index = Brick.Index(x & 15, y & 15, z & 15);
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

    /// <summary>Pours this much water into voxel (x, y, z), cubic metres (its brick made if needed).</summary>
    /// <returns>False when its brick could not be made (<see cref="MaxBricks"/>): nothing poured.</returns>
    public bool Pour(int x, int y, int z, float cubicMetres)
    {
        if (Make(x >> 4, y >> 4, z >> 4) is not { } brick)
        {
            return false;
        }

        int i = Brick.Index(x & 15, y & 15, z & 15);
        brick.Water[i] += cubicMetres / VoxelVolume;
        brick.EmptyFor = 0f;
        brick.Wets(i);
        return true;
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
    /// Reads a brick's solids again (a piece was built or broken there). Water in a voxel that turned solid goes up
    /// to the first open voxel over it, in a brick above if need be (a piece built in a pool lifts the water, it does
    /// not destroy it).
    /// </summary>
    public void Refill(Brick brick)
    {
        Array.Clear(brick.Solid, 0, Brick.Count);
        Array.Clear(brick.Closed, 0, Brick.Count);
        _solids.Fill(brick, VoxelSize);

        // Its faces may have opened onto a sleeping brick beside it: they wake together.
        brick.Quiet = 0;
        brick.Changes++;
        foreach (Brick next in brick.Beside.Where(b => b != null))
        {
            next.Quiet = 0;
        }

        for (int i = 0; i < Brick.Count; i++)
        {
            if (brick.Solid[i] >= 1f && brick.Water[i] > 0f)
            {
                (int x, int y, int z) = Brick.Place(i);
                if (Lift((brick.X * Brick.Size) + x, (brick.Y * Brick.Size) + y, (brick.Z * Brick.Size) + z, brick.Water[i]))
                {
                    brick.Water[i] = 0f;
                }
            }
        }
    }

    // Water out of solid voxel (x, y, z) into the first open voxel over it (bricks made as it climbs); false when a
    // brick it needs cannot be made (MaxBricks): the water stays where it is.
    private bool Lift(int x, int y, int z, float water)
    {
        for (int up = y + 1; ; up++)
        {
            if (Make(x >> 4, up >> 4, z >> 4) is not { } brick)
            {
                return false;
            }

            int i = Brick.Index(x & 15, up & 15, z & 15);
            if (brick.Solid[i] < 1f)
            {
                brick.Water[i] += water;
                brick.EmptyFor = 0f;
                brick.Wets(i);
                return true;
            }
        }
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
    /// reads the solids there, which can take a while); the others are made at a later call.
    /// </summary>
    public void MakeWanted(Func<bool> timeLeft)
    {
        long[] wanted;
        lock (_wanted)
        {
            wanted = [.. _wanted];
            _wanted.Clear();
        }

        bool made = false;
        for (int k = 0; k < wanted.Length; k++)
        {
            if (_bricks.Count >= MaxBricks)
            {
                Capped = true;
                return;
            }

            if (made && !timeLeft())
            {
                Want(wanted, k);
                return;
            }

            made |= Find(Unpack(wanted[k] >> 42), Unpack(wanted[k] >> 21), Unpack(wanted[k])) == null;
            Get(Unpack(wanted[k] >> 42), Unpack(wanted[k] >> 21), Unpack(wanted[k]));
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

    /// <summary>Forgets the bricks dry for longer than this, with the time since the last call.</summary>
    /// <param name="dt">Seconds since the last call.</param>
    /// <param name="after">Seconds dry before a brick is forgotten.</param>
    public void ForgetDry(float dt, float after)
    {
        var gone = new List<Brick>();
        foreach (Brick brick in _bricks.Values)
        {
            brick.EmptyFor = Array.Exists(brick.Water, w => w > Brick.Dry) ? 0f : brick.EmptyFor + dt;
            if (brick.EmptyFor > after)
            {
                gone.Add(brick);
            }
        }

        foreach (Brick brick in gone)
        {
            _bricks.Remove(Key(brick.X, brick.Y, brick.Z));
            Version++;
            for (int side = 0; side < Sides.Length; side++)
            {
                if (brick.Beside[side] is { } next)
                {
                    next.Beside[side ^ 1] = null;
                }
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
