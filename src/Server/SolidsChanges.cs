using System;
using System.Collections.Generic;
using FlowingWater.Volume;

namespace FlowingWater.Server;

/// <summary>
/// What changed in the water's solids since the bricks last read them, without the game: each zone's pieces as last
/// looked at (where each stands, how far its boxes reach, its turn and state), compared with the next look; a piece
/// heard of as destroyed at once; a zone's ground changed. Each change is a place: the bricks meeting it, or the voxel
/// round them (their faces take their neighbours' shares), read their solids again, and no others.
/// </summary>
/// <typeparam name="TZone">A zone.</typeparam>
/// <typeparam name="TId">A piece's object.</typeparam>
internal sealed class SolidsChanges<TZone, TId>
{
    private readonly Func<TZone, long> _key;

    // Each zone's pieces as last looked at.
    private readonly Dictionary<long, Dictionary<TId, (Vec3 At, float Reach, int Look)>> _pieces = [];

    // The zones whose pieces are read again, and the places whose bricks read their solids again.
    private readonly HashSet<TZone> _zones = [];

    private readonly List<(Vec3 Low, Vec3 High)> _places = [];

    // The pieces of the zone being looked at, found so far (a cleared dictionary kept to be used again).
    private Dictionary<TId, (Vec3 At, float Reach, int Look)> _now = [];

    // The bricks still to read their solids again for the places taken up last (a refill is spread over frames).
    private readonly List<Brick> _pending = [];

    /// <summary>Changes in zones known by this key (<see cref="ZoneCache"/>).</summary>
    public SolidsChanges(Func<TZone, long> key)
    {
        _key = key;
    }

    /// <summary>Whether anything changed since the bricks last read their solids, or some have still to.</summary>
    public bool Any => _places.Count > 0 || _pending.Count > 0;

    /// <summary>One piece of the zone being looked at: where it stands, how far it reaches, its turn and state.</summary>
    public void Found(TId id, Vec3 at, float reach, int look) => _now[id] = (at, reach, look);

    /// <summary>
    /// The zone being looked at is done: a piece new, gone, or changed since the last look changed both where it was
    /// and where it is. The first look at a zone changes nothing.
    /// </summary>
    public void Looked(TZone zone)
    {
        long key = _key(zone);
        Dictionary<TId, (Vec3 At, float Reach, int Look)> pieces = _now;
        if (_pieces.TryGetValue(key, out Dictionary<TId, (Vec3 At, float Reach, int Look)> was))
        {
            Compare(zone, was, pieces);
            was.Clear();
            _now = was;
        }
        else
        {
            _now = [];
        }

        _pieces[key] = pieces;
    }

    /// <summary>
    /// A piece destroyed, heard of at once: where it stands, and where it stood when last looked at (it may have moved
    /// since). The next look does not find it gone again. One not heard of (the water switched off) is found gone then.
    /// </summary>
    public void Destroyed(TZone zone, TId id, Vec3 at, float reach)
    {
        Changed(zone, at, reach);
        if (_pieces.TryGetValue(_key(zone), out Dictionary<TId, (Vec3 At, float Reach, int Look)> pieces) && pieces.TryGetValue(id, out (Vec3 At, float Reach, int Look) was))
        {
            Changed(zone, was.At, was.Reach);
            pieces.Remove(id);
        }
    }

    /// <summary>A zone's ground changed: everything over it, from this low corner to this high one.</summary>
    public void Ground(TZone zone, Vec3 low, Vec3 high)
    {
        _zones.Add(zone);
        _places.Add((low, high));
    }

    /// <summary>Every brick reads its solids again.</summary>
    public void All() => _places.Add((new Vec3(float.MinValue, float.MinValue, float.MinValue), new Vec3(float.MaxValue, float.MaxValue, float.MaxValue)));

    /// <summary>Forgets the pieces last looked at in the zones not among these (by key): a look there is a first again.</summary>
    public void Keep(HashSet<long> zones) => ZoneCache.Keep(_pieces, zones);

    /// <summary>The changed zones' solids forgotten, then every brick meeting a changed place reads theirs again, now.</summary>
    /// <param name="grid">The bricks.</param>
    /// <param name="forget">Forgets what was read of a zone's solids.</param>
    /// <returns>How many bricks read their solids again.</returns>
    public int Refill(VoxelGrid grid, Action<TZone> forget) => Refill(grid, forget, static () => true);

    /// <summary>
    /// The changed zones' solids forgotten, then the bricks meeting a changed place read theirs again while
    /// <paramref name="timeLeft"/> says so (one at least: a hoe's stroke by a big pool would otherwise have every brick
    /// of it read the ground and the pieces again in one frame); the rest go on at the next call. A brick forgotten
    /// meanwhile is left out; a place changed meanwhile is taken up once these are done.
    /// </summary>
    /// <param name="grid">The bricks.</param>
    /// <param name="forget">Forgets what was read of a zone's solids.</param>
    /// <param name="timeLeft">Whether the frame has time for another brick.</param>
    /// <returns>How many bricks read their solids again.</returns>
    public int Refill(VoxelGrid grid, Action<TZone> forget, Func<bool> timeLeft)
    {
        if (_pending.Count == 0)
        {
            if (_places.Count == 0)
            {
                return 0;
            }

            foreach (TZone zone in _zones)
            {
                forget(zone);
            }

            foreach (Brick brick in grid.Bricks)
            {
                if (Near(brick, grid.VoxelSize))
                {
                    _pending.Add(brick);
                }
            }

            _zones.Clear();
            _places.Clear();
        }

        int refilled = 0;
        while (_pending.Count > 0)
        {
            Brick brick = _pending[_pending.Count - 1];
            _pending.RemoveAt(_pending.Count - 1);
            if (grid.Find(brick.X, brick.Y, brick.Z) == brick)
            {
                grid.Refill(brick);
                refilled++;
            }

            if (!timeLeft())
            {
                break;
            }
        }

        return refilled;
    }

    // A piece changed at a place: the bricks its solid boxes reach (as far as they go from it).
    private void Changed(TZone zone, Vec3 at, float reach)
    {
        _zones.Add(zone);
        var round = new Vec3(reach, reach, reach);
        _places.Add((at - round, at + round));
    }

    // The pieces that differ between two looks at a zone: where each was, and where it is.
    private void Compare(TZone zone, Dictionary<TId, (Vec3 At, float Reach, int Look)> was, Dictionary<TId, (Vec3 At, float Reach, int Look)> now)
    {
        foreach (KeyValuePair<TId, (Vec3 At, float Reach, int Look)> piece in now)
        {
            if (!was.TryGetValue(piece.Key, out (Vec3 At, float Reach, int Look) old) || old != piece.Value)
            {
                Changed(zone, piece.Value.At, piece.Value.Reach);
            }
        }

        foreach (KeyValuePair<TId, (Vec3 At, float Reach, int Look)> piece in was)
        {
            if (!now.TryGetValue(piece.Key, out (Vec3 At, float Reach, int Look) then) || then.At != piece.Value.At || !then.Reach.Equals(piece.Value.Reach))
            {
                Changed(zone, piece.Value.At, piece.Value.Reach);
            }
        }
    }

    // Whether a brick, or the voxel round it, meets a changed place.
    private bool Near(Brick brick, float voxelSize)
    {
        float span = Brick.Size * voxelSize;
        var low = new Vec3((brick.X * span) - voxelSize, (brick.Y * span) - voxelSize, (brick.Z * span) - voxelSize);
        var high = new Vec3(((brick.X + 1) * span) + voxelSize, ((brick.Y + 1) * span) + voxelSize, ((brick.Z + 1) * span) + voxelSize);
        return _places.Exists(p => p.Low.X <= high.X && p.High.X >= low.X && p.Low.Y <= high.Y && p.High.Y >= low.Y && p.Low.Z <= high.Z && p.High.Z >= low.Z);
    }
}
