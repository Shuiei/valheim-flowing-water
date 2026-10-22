using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// Keeps the water's solids up to date: a piece destroyed (the server hears of each) or built, moved, turned, opened
/// or closed (each zone holding water is looked over every few seconds), and the bricks within its reach read their
/// solids again; the ground changed by a player, and the bricks over that zone do.
/// </summary>
internal sealed class SolidsWatch
{
    private const float LookSeconds = 2f;

    // Zones this close to a brick are looked over, metres, at least (farther when a piece read reaches farther): a
    // piece standing in one may reach the brick.
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

    private float _timer;

    // How many times the farthest reach of pieces had grown when last looked at.
    private int _grown;

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
        // A piece read reached farther than any before: the zones each brick read its solids from may have missed
        // ones like it, so every brick reads them again, from zones as far as the new reach.
        if (_solids.Shapes.Grown != _grown)
        {
            _grown = _solids.Shapes.Grown;
            _solids.ForgetAll();
            _regions.Add(new Bounds(Vector3.zero, Vector3.one * float.MaxValue));
        }

        _timer += dt;
        if (_timer >= LookSeconds)
        {
            _timer = 0f;
            foreach (Vector2s zone in Zones(grid, Mathf.Max(Reach, _solids.Shapes.Farthest) + grid.VoxelSize))
            {
                Look(zone);
            }
        }

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

    // What changed in a zone since last looked at: each piece built, gone, moved, turned, opened or closed, and the
    // ground.
    private void Look(Vector2s zone)
    {
        long key = TileData.Key(zone);
        _found.Clear();
        ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), _found);
        var pieces = new Dictionary<ZDOID, (Vector3 At, float Reach, int Look)>();
        foreach (ZDO zdo in _found)
        {
            if (_solids.Shapes.Count(zdo.GetPrefab()) > 0)
            {
                pieces[zdo.m_uid] = (zdo.GetPosition(), _solids.Shapes.Reach(zdo), unchecked((((zdo.GetRotation().GetHashCode() * 31) + zdo.GetInt(StateHash)) * 31) + PieceShapes.Scale(zdo).GetHashCode()));
            }
        }

        if (_pieces.TryGetValue(key, out Dictionary<ZDOID, (Vector3 At, float Reach, int Look)> was))
        {
            Compare(zone, was, pieces);
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

    // The zones a piece of which may reach a brick or the voxel round it: those under each brick, or within that far
    // of it.
    private static HashSet<Vector2s> Zones(VoxelGrid grid, float reach)
    {
        var zones = new HashSet<Vector2s>();
        float span = Brick.Size * grid.VoxelSize;
        foreach (Brick brick in grid.Bricks)
        {
            Vector2s first = ZoneSystem.GetZone(new Vector3((brick.X * span) - reach, 0f, (brick.Z * span) - reach));
            Vector2s last = ZoneSystem.GetZone(new Vector3(((brick.X + 1) * span) + reach, 0f, ((brick.Z + 1) * span) + reach));
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
