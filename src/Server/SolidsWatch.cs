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

    // A piece reaches this far from its place at most, metres: bricks this close to a changed piece are read again.
    private const float Reach = 4f;

    private static readonly int StateHash = "state".GetStableHashCode();

    private readonly GameSolids _solids;

    private readonly ZoneTerrain _terrain;

    // Each zone's pieces as last looked at: where each stands, and its turn and state.
    private readonly Dictionary<long, Dictionary<ZDOID, (Vector3 At, int Look)>> _pieces = [];

    private readonly Dictionary<long, int> _ground = [];

    // The zones whose pieces are read again, and the places whose bricks read their solids again.
    private readonly HashSet<Vector2s> _changed = [];

    private readonly List<Bounds> _regions = [];

    private readonly List<ZDO> _found = [];

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
        if (_timer >= LookSeconds)
        {
            _timer = 0f;
            foreach (Vector2s zone in Zones(grid))
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
            PieceChanged(zone, zdo.GetPosition());

            // Heard of already: the next look does not find it gone again.
            if (_pieces.TryGetValue(TileData.Key(zone), out Dictionary<ZDOID, (Vector3 At, int Look)> pieces))
            {
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
        var pieces = new Dictionary<ZDOID, (Vector3 At, int Look)>();
        foreach (ZDO zdo in _found)
        {
            if (_solids.Shapes.Count(zdo.GetPrefab()) > 0)
            {
                pieces[zdo.m_uid] = (zdo.GetPosition(), unchecked((zdo.GetRotation().GetHashCode() * 31) + zdo.GetInt(StateHash)));
            }
        }

        if (_pieces.TryGetValue(key, out Dictionary<ZDOID, (Vector3 At, int Look)> was))
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
    private void Compare(Vector2s zone, Dictionary<ZDOID, (Vector3 At, int Look)> was, Dictionary<ZDOID, (Vector3 At, int Look)> now)
    {
        foreach (KeyValuePair<ZDOID, (Vector3 At, int Look)> piece in now)
        {
            if (!was.TryGetValue(piece.Key, out (Vector3 At, int Look) old) || old != piece.Value)
            {
                PieceChanged(zone, piece.Value.At);
            }
        }

        foreach (KeyValuePair<ZDOID, (Vector3 At, int Look)> piece in was)
        {
            if (!now.TryGetValue(piece.Key, out (Vector3 At, int Look) then) || then.At != piece.Value.At)
            {
                PieceChanged(zone, piece.Value.At);
            }
        }
    }

    private void PieceChanged(Vector2s zone, Vector3 at)
    {
        _changed.Add(zone);
        _regions.Add(new Bounds(at, Vector3.one * (2f * Reach)));
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
