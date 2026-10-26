using System;
using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// Keeps the water's solids up to date: an object destroyed (the server hears of each: a piece broken, a tree felled,
/// a rock mined away) or built, moved, turned, mined, opened or closed (each zone holding water is looked over every
/// few seconds), and the bricks within its reach read their solids again; the ground changed by a player, and the
/// bricks over that zone do. The looking is spread over frames, a part of a zone's objects at a time, so that a big
/// base by the water does not stall one.
/// </summary>
internal sealed class SolidsWatch
{
    private const float LookSeconds = 2f;

    // The most of a frame looking may take, milliseconds (a part at least), and the objects read in a part.
    private const double LookBudget = 0.5;

    private const int PartObjects = 64;

    // Zones this close to a brick are looked over, metres, at least (farther when the game's prefabs reach farther): an
    // object standing in one may reach the brick. Zones holding objects reaching farther are looked over too.
    private const float Reach = 4f;

    // The most of a frame bricks reading their solids again may take, milliseconds (a brick at least), after the look.
    private const double RefillBudget = 2.0;

    private readonly GameSolids _solids;

    private readonly ZoneTerrain _terrain;

    // The objects as last looked at, and what changed since the bricks read their solids.
    private readonly SolidsChanges<Vector2s, ZDOID> _changes = new(TileData.Key);

    // Each round of looking, spread over frames.
    private readonly ZoneScan<Vector2s, ZDO> _scan;

    private readonly System.Diagnostics.Stopwatch _lookWatch = new();

    private readonly Func<bool> _lookTimeLeft, _refillTimeLeft;

    private float _timer;

    // How many zones holding far-reaching objects had been found when last looked at.
    private int _grown;

    /// <summary>
    /// Zones whose ground is read with no brick near them (the config's fixed sources and drains stand on it): their
    /// heights are kept, not let go and made again at each look for markers. Not looked over for objects.
    /// </summary>
    public HashSet<Vector2s> Pinned { get; } = [];

    /// <summary>A watch over these solids and terrain; it listens to the server's destroyed objects at once.</summary>
    public SolidsWatch(GameSolids solids, ZoneTerrain terrain)
    {
        _solids = solids;
        _terrain = terrain;
        _scan = new ZoneScan<Vector2s, ZDO>((zone, found) => ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), found), Read, _changes.Looked, PartObjects);
        _lookTimeLeft = () => _lookWatch.Elapsed.TotalMilliseconds < LookBudget;
        _refillTimeLeft = () => _lookWatch.Elapsed.TotalMilliseconds < LookBudget + RefillBudget;
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
        // A zone holding objects reaching farther than the game's prefabs was found (a whole place, a rock scaled
        // up): bricks that read their solids before may have missed them, so those meeting them read them again.
        if (_solids.Grown != _grown)
        {
            _grown = _solids.Grown;
            foreach ((Vec3 low, Vec3 high) in _solids.TakeGrown())
            {
                _changes.Place(low, high);
            }
        }

        _timer += dt;
        if (_timer >= LookSeconds && !_scan.Busy)
        {
            _timer = 0f;
            HashSet<Vector2s> zones = Zones(grid, Mathf.Max(Reach, _solids.Shapes.Farthest) + grid.VoxelSize);
            zones.UnionWith(_solids.FarZones);
            Keep(zones);
            _scan.Start(zones);
        }

        _lookWatch.Restart();
        _scan.Run(_lookTimeLeft);
        return _changes.Refill(grid, _solids.Forget, _refillTimeLeft);
    }

    // What is kept of the zones no brick is near any more is let go: their objects as last looked at, their solids and
    // heights (but the heights of the pinned zones). A brick there again reads them anew.
    private void Keep(HashSet<Vector2s> zones)
    {
        var keys = new HashSet<long>();
        foreach (Vector2s zone in zones)
        {
            keys.Add(TileData.Key(zone));
        }

        _changes.Keep(keys);
        _solids.Keep(keys);
        foreach (Vector2s zone in Pinned)
        {
            keys.Add(TileData.Key(zone));
        }

        _terrain.Keep(keys);
    }

    private void Destroyed(ZDO zdo)
    {
        // The water switched off is not watched (nothing would read what piled up until it is on again): the first look
        // once it is on finds the object gone, as it is still among those last looked at.
        if (FlowingWaterPlugin.Settings.Enabled.Value && _solids.Shapes.Has(zdo))
        {
            _changes.Destroyed(ZoneSystem.GetZone(zdo.GetPosition()), zdo.m_uid, At(zdo.GetPosition()), _solids.Shapes.Reach(zdo));
        }
    }

    // One object of a zone looked at: its place, reach, turn, state (a door's, a mined rock's bits) and scale. One
    // destroyed since it was found is left out, or read as whatever object it is now: at worst a change that was none.
    private void Read(ZDO zdo)
    {
        if (zdo.IsValid() && _solids.Shapes.Has(zdo))
        {
            Vector3 scale = SolidShapes.Scale(zdo);
            _changes.Found(zdo.m_uid, At(zdo.GetPosition()), _solids.Shapes.Reach(zdo, scale), unchecked((((zdo.GetRotation().GetHashCode() * 31) + _solids.Shapes.State(zdo)) * 31) + scale.GetHashCode()));
        }
    }

    /// <summary>
    /// A zone's ground changed (its heights were read again): the bricks over it read their solids again. Every brick
    /// that read the heights before is among them, however soon after it read them the ground changed.
    /// </summary>
    public void GroundChanged(Vector2s zone)
    {
        // The ground does not say where it changed: the whole zone, from the bottom to the top of the world.
        float x = TileData.OriginX(zone);
        float z = TileData.OriginZ(zone);
        _changes.Ground(zone, new Vec3(x, -5e4f, z), new Vec3(x + TileData.ZoneMetres, 5e4f, z + TileData.ZoneMetres));
    }

    private static Vec3 At(Vector3 point) => new(point.x, point.y, point.z);

    // The zones an object of which may reach a brick or the voxel round it: those under each brick, or within that far
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
}
