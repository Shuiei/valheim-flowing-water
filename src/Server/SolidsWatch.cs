using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// Keeps the water's solids up to date: a piece destroyed (the server hears of each) or built, moved, opened or
/// closed (each zone holding water is looked over every few seconds), or the ground changed by a player, and the
/// bricks over that zone read their solids again.
/// </summary>
internal sealed class SolidsWatch
{
    private const float LookSeconds = 2f;

    // A piece reaches this far past its own zone at most, metres: bricks this close to a changed zone are read again.
    private const float Reach = 4f;

    private static readonly int StateHash = "state".GetStableHashCode();

    private readonly GameSolids _solids;

    private readonly ZoneTerrain _terrain;

    private readonly Dictionary<long, int> _pieces = [];

    private readonly Dictionary<long, int> _ground = [];

    private readonly HashSet<Vector2s> _changed = [];

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

        if (_changed.Count == 0)
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
        return refilled;
    }

    private void Destroyed(ZDO zdo)
    {
        if (_solids.Shapes.Count(zdo.GetPrefab()) > 0)
        {
            _changed.Add(ZoneSystem.GetZone(zdo.GetPosition()));
        }
    }

    // A zone changed when its pieces (which, where, open or not) or its ground did since last looked at.
    private void Look(Vector2s zone)
    {
        long key = TileData.Key(zone);
        _found.Clear();
        ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), _found);
        int pieces = 17;
        foreach (ZDO zdo in _found)
        {
            if (_solids.Shapes.Count(zdo.GetPrefab()) > 0)
            {
                pieces = unchecked((pieces * 31) + zdo.m_uid.GetHashCode() + zdo.GetPosition().GetHashCode() + zdo.GetInt(StateHash));
            }
        }

        _terrain.Heights(zone, out int ground);
        bool known = _pieces.TryGetValue(key, out int was);
        if ((known && was != pieces) || (_ground.TryGetValue(key, out int wasGround) && wasGround != ground))
        {
            _changed.Add(zone);
        }

        _pieces[key] = pieces;
        _ground[key] = ground;
    }

    // The zones under the bricks.
    private static HashSet<Vector2s> Zones(VoxelGrid grid)
    {
        var zones = new HashSet<Vector2s>();
        float span = Brick.Size * grid.VoxelSize;
        foreach (Brick brick in grid.Bricks)
        {
            zones.Add(ZoneSystem.GetZone(new Vector3((brick.X + 0.5f) * span, 0f, (brick.Z + 0.5f) * span)));
        }

        return zones;
    }

    // Whether a brick lies within a changed zone, or close enough to it for a piece there to reach it.
    private bool Near(Brick brick, float voxelSize)
    {
        float span = Brick.Size * voxelSize;
        float x0 = (brick.X * span) - Reach;
        float z0 = (brick.Z * span) - Reach;
        float x1 = ((brick.X + 1) * span) + Reach;
        float z1 = ((brick.Z + 1) * span) + Reach;
        foreach (Vector2s zone in _changed)
        {
            float zx = TileData.OriginX(zone);
            float zz = TileData.OriginZ(zone);
            if (x1 >= zx && x0 <= zx + TileData.ZoneMetres && z1 >= zz && z0 <= zz + TileData.ZoneMetres)
            {
                return true;
            }
        }

        return false;
    }
}
