using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// Keeps the water's solids up to date: a piece destroyed (the server hears of each) or built, moved, turned, opened
/// or closed (each zone holding water is looked over every few seconds), and the bricks within its reach read their
/// solids again; the ground changed by a player, and the bricks over that zone do. The looking is spread over
/// frames, a part of a zone's objects at a time, so that a big base by the water does not stall one.
/// </summary>
internal sealed class SolidsWatch
{
    private const float LookSeconds = 2f;

    // The most of a frame looking may take, milliseconds (a part at least), and the objects read in a part.
    private const double LookBudget = 0.5;

    private const int PartObjects = 64;

    // Zones this close to a brick are looked over, metres: a piece standing in one may reach the brick.
    private const float Reach = 4f;

    private static readonly int StateHash = "state".GetStableHashCode();

    private readonly GameSolids _solids;

    private readonly ZoneTerrain _terrain;

    // Each zone's pieces as last looked at: where each stands, and its turn and state.
    private readonly Dictionary<long, Dictionary<ZDOID, (Vector3 At, float Reach, int Look)>> _pieces = [];

    private readonly Dictionary<long, int> _ground = [];

    // The zones whose pieces are read again, and the places whose bricks read their solids again.
    private readonly HashSet<Vector2s> _changed = [];

    private readonly List<Bounds> _regions = [];

    private readonly List<ZDO> _found = [];

    // The zones still to look at this time round, the one being looked at, how far into its objects, and its pieces
    // found so far (a cleared dictionary kept to be used again).
    private readonly Queue<Vector2s> _toLook = new();

    private readonly System.Diagnostics.Stopwatch _lookWatch = new();

    private Vector2s _looking;

    private int _foundAt = -1;

    private Dictionary<ZDOID, (Vector3 At, float Reach, int Look)> _now = [];

    private float _timer;

    /// <summary>A watch over these solids and terrain; it listens to the server's destroyed objects at once.</summary>
    public SolidsWatch(GameSolids solids, ZoneTerrain terrain)
    {
        _solids = solids;
        _terrain = terrain;
        ZDOMan.instance.m_onZDODestroyed += Destroyed;
    }

    /// <summary>Stops listening (the world closes).</summary>
    public void Close()
    {
        if (ZDOMan.instance != null)
        {
            ZDOMan.instance.m_onZDODestroyed -= Destroyed;
        }
    }

    /// <summary>Looks over the zones holding water when it is time, and reads the solids of changed zones again.</summary>
    /// <returns>How many bricks read their solids again.</returns>
    public int Update(float dt, VoxelGrid grid)
    {
        _timer += dt;
        if (_timer >= LookSeconds && _toLook.Count == 0 && _foundAt < 0)
        {
            _timer = 0f;
            foreach (Vector2s zone in Zones(grid))
            {
                _toLook.Enqueue(zone);
            }
        }

        _lookWatch.Restart();
        do
        {
            LookPart();
        }
        while ((_foundAt >= 0 || _toLook.Count > 0) && _lookWatch.Elapsed.TotalMilliseconds < LookBudget);

        if (_regions.Count == 0)
        {
            return 0;
        }

        int refilled = 0;
        foreach (Vector2s zone in _changed)
        {
            _solids.Forget(zone);
        }

        foreach (Brick brick in new List<Brick>(grid.Bricks))
        {
            if (Near(brick, grid.VoxelSize))
            {
                grid.Refill(brick);
                refilled++;
            }
        }

        _changed.Clear();
        _regions.Clear();
        return refilled;
    }

    private void Destroyed(ZDO zdo)
    {
        if (_solids.Shapes.Count(zdo.GetPrefab()) > 0)
        {
            Vector2s zone = ZoneSystem.GetZone(zdo.GetPosition());
            PieceChanged(zone, zdo.GetPosition(), _solids.Shapes.Reach(zdo));

            // Heard of already: the next look does not find it gone again, so where it stood when last looked at
            // (it may have moved since) is read again now.
            if (_pieces.TryGetValue(TileData.Key(zone), out Dictionary<ZDOID, (Vector3 At, float Reach, int Look)> pieces)
                && pieces.TryGetValue(zdo.m_uid, out (Vector3 At, float Reach, int Look) was))
            {
                PieceChanged(zone, was.At, was.Reach);
                pieces.Remove(zdo.m_uid);
            }
        }
    }

    // A part of the look: the next zone's objects found, or the next PartObjects of them read, or, once all are, what
    // changed in the zone since last looked at (each piece built, gone, moved, turned, opened or closed, and the
    // ground). An object destroyed meanwhile is read as it is now (a different object, or none): at worst a change
    // that was none.
    private void LookPart()
    {
        if (_foundAt < 0)
        {
            if (_toLook.Count == 0)
            {
                return;
            }

            _looking = _toLook.Dequeue();
            _found.Clear();
            ZDOMan.instance.FindSectorObjects(_looking, new SimulationDistance(0, 0), _found);
            _foundAt = 0;
            return;
        }

        int end = System.Math.Min(_found.Count, _foundAt + PartObjects);
        for (; _foundAt < end; _foundAt++)
        {
            ZDO zdo = _found[_foundAt];
            if (zdo.IsValid() && _solids.Shapes.Count(zdo.GetPrefab()) > 0)
            {
                _now[zdo.m_uid] = (zdo.GetPosition(), _solids.Shapes.Reach(zdo), unchecked((zdo.GetRotation().GetHashCode() * 31) + zdo.GetInt(StateHash)));
            }
        }

        if (_foundAt == _found.Count)
        {
            _foundAt = -1;
            Looked(_looking);
        }
    }

    // A zone looked at in full: its pieces and ground compared with last time.
    private void Looked(Vector2s zone)
    {
        long key = TileData.Key(zone);
        Dictionary<ZDOID, (Vector3 At, float Reach, int Look)> pieces = _now;
        if (_pieces.TryGetValue(key, out Dictionary<ZDOID, (Vector3 At, float Reach, int Look)> was))
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
        _terrain.Heights(zone, out int ground);
        if (_ground.TryGetValue(key, out int wasGround) && wasGround != ground)
        {
            // The ground does not say where it changed: the whole zone, from the bottom to the top of the world.
            _changed.Add(zone);
            _regions.Add(new Bounds(new Vector3(TileData.OriginX(zone) + (TileData.ZoneMetres / 2f), 0f, TileData.OriginZ(zone) + (TileData.ZoneMetres / 2f)), new Vector3(TileData.ZoneMetres, 1e5f, TileData.ZoneMetres)));
        }

        _ground[key] = ground;
    }

    // The pieces that differ between two looks at a zone: where each was, and where it is.
    private void Compare(Vector2s zone, Dictionary<ZDOID, (Vector3 At, float Reach, int Look)> was, Dictionary<ZDOID, (Vector3 At, float Reach, int Look)> now)
    {
        foreach (KeyValuePair<ZDOID, (Vector3 At, float Reach, int Look)> piece in now)
        {
            if (!was.TryGetValue(piece.Key, out (Vector3 At, float Reach, int Look) old) || old != piece.Value)
            {
                PieceChanged(zone, piece.Value.At, piece.Value.Reach);
            }
        }

        foreach (KeyValuePair<ZDOID, (Vector3 At, float Reach, int Look)> piece in was)
        {
            if (!now.TryGetValue(piece.Key, out (Vector3 At, float Reach, int Look) then) || then.At != piece.Value.At || !Mathf.Approximately(then.Reach, piece.Value.Reach))
            {
                PieceChanged(zone, piece.Value.At, piece.Value.Reach);
            }
        }
    }

    // A piece changed at a place: the bricks its solid boxes reach (as far as they go from it) read their solids again.
    private void PieceChanged(Vector2s zone, Vector3 at, float reach)
    {
        _changed.Add(zone);
        _regions.Add(new Bounds(at, Vector3.one * (2f * reach)));
    }

    // The zones a piece of which may reach a brick: those under each brick, or within a piece's reach of it.
    private static HashSet<Vector2s> Zones(VoxelGrid grid)
    {
        var zones = new HashSet<Vector2s>();
        float span = Brick.Size * grid.VoxelSize;
        foreach (Brick brick in grid.Bricks)
        {
            Vector2s first = ZoneSystem.GetZone(new Vector3((brick.X * span) - Reach, 0f, (brick.Z * span) - Reach));
            Vector2s last = ZoneSystem.GetZone(new Vector3(((brick.X + 1) * span) + Reach, 0f, ((brick.Z + 1) * span) + Reach));
            for (int z = first.y; z <= last.y; z++)
            {
                for (int x = first.x; x <= last.x; x++)
                {
                    zones.Add(new Vector2s(x, z));
                }
            }
        }

        return zones;
    }

    // Whether a brick, or the voxel round it (its faces take its neighbours' shares), meets a changed place.
    private bool Near(Brick brick, float voxelSize)
    {
        float span = Brick.Size * voxelSize;
        var bounds = new Bounds(new Vector3(brick.X + 0.5f, brick.Y + 0.5f, brick.Z + 0.5f) * span, Vector3.one * (span + (2f * voxelSize)));
        return _regions.Exists(r => r.Intersects(bounds));
    }
}
