using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// What is solid for the water in the game: the ground (the generated terrain with the players' changes) and the
/// solid objects of the zones a brick spans (building pieces, rocks, trees, a place's ruins), as
/// <see cref="SolidShapes"/> sees them. Main thread only: the terrain is read through the game's heightmap builder.
/// An object reaching farther than the game's own prefabs (scaled, or a whole place) is kept apart with its zone, so
/// that a few such objects do not make every brick read the objects of zones that far round it.
/// </summary>
internal sealed class GameSolids : ISolids
{
    private readonly ZoneTerrain _terrain;

    // Each zone's objects read: the solids of those reaching no farther than the game's prefabs, and of those that do.
    private readonly Dictionary<long, (List<Solid> Near, List<Solid> Far)> _solidsByZone = [];

    // The zones known to hold objects reaching farther than the game's prefabs: their far solids go to every brick.
    private readonly HashSet<Vector2s> _farZones = [];

    private readonly List<Vector2s> _farZonesNow = [];

    private readonly List<ZDO> _found = [];

    private readonly List<Solid> _near = [];

    // Where the frame's time goes: a brick reading its solids is a part of its own, whoever asked for it.
    private readonly FrameParts _parts;

    private bool _notNumbersTold;

    /// <summary>Solids over this terrain, their reading timed in these parts.</summary>
    public GameSolids(ZoneTerrain terrain, FrameParts parts)
    {
        _terrain = terrain;
        _parts = parts;
    }

    /// <summary>The objects' shapes used.</summary>
    public SolidShapes Shapes { get; } = new();

    /// <summary>The zones known to hold objects reaching farther than the game's prefabs (scaled, or a whole place).</summary>
    public IReadOnlyCollection<Vector2s> FarZones => _farZones;

    /// <summary>
    /// How many times a zone was found holding objects reaching farther than the game's prefabs: bricks that read
    /// their solids before may have missed them (<see cref="TakeGrown"/> says where).
    /// </summary>
    public int Grown { get; private set; }

    // Where the far-reaching objects found since last taken stand: their boxes along the world's axes.
    private readonly List<(Vec3 Low, Vec3 High)> _grown = [];

    /// <summary>
    /// The boxes (along the world's axes) of the objects reaching farther than the game's prefabs found since last
    /// asked: the bricks meeting them should read their solids again. Cleared by the asking.
    /// </summary>
    public List<(Vec3 Low, Vec3 High)> TakeGrown()
    {
        var grown = new List<(Vec3 Low, Vec3 High)>(_grown);
        _grown.Clear();
        return grown;
    }

    /// <inheritdoc/>
    public void Fill(Brick brick, float voxelSize)
    {
        WaterPart was = _parts.Into(WaterPart.Solids);
        int notNumbers = SolidRaster.Ground(brick, voxelSize, _terrain.HeightAt);
        if (notNumbers > 0 && !_notNumbersTold)
        {
            _notNumbersTold = true;
            FlowingWaterPlugin.Log.LogWarning($"Water: the ground's height under brick {brick.X},{brick.Y},{brick.Z} is not a number in {notNumbers} columns (the game's heightmap?); they are taken as solid, and no more such are told of");
        }

        _near.Clear();
        float span = Brick.Size * voxelSize;

        // The zones whose objects may reach the brick or the voxel round it: as far as the game's prefabs reach, past
        // that voxel; and the objects reaching farther, wherever they are known. Of their solids, only those reaching
        // the brick's own surroundings (a voxel round it, and the voxel over that the raster tests): a big base or a
        // wood by the water would have every brick near it try every solid of nine zones.
        float margin = Shapes.Farthest + voxelSize;
        Vector2s first = ZoneSystem.GetZone(new Vector3((brick.X * span) - margin, 0f, (brick.Z * span) - margin));
        Vector2s last = ZoneSystem.GetZone(new Vector3(((brick.X + 1) * span) + margin, 0f, ((brick.Z + 1) * span) + margin));
        var low = new Vec3((brick.X * span) - (2f * voxelSize), (brick.Y * span) - (2f * voxelSize), (brick.Z * span) - (2f * voxelSize));
        var high = new Vec3(((brick.X + 1) * span) + (2f * voxelSize), ((brick.Y + 1) * span) + (2f * voxelSize), ((brick.Z + 1) * span) + (2f * voxelSize));
        for (int zz = first.y; zz <= last.y; zz++)
        {
            for (int zx = first.x; zx <= last.x; zx++)
            {
                Near(Solids(new Vector2s(zx, zz)).Near, low, high);
            }
        }

        _farZonesNow.Clear();
        _farZonesNow.AddRange(_farZones);
        foreach (Vector2s zone in _farZonesNow)
        {
            Near(Solids(zone).Far, low, high);
        }

        SolidRaster.Pieces(brick, voxelSize, _near, _terrain.HeightAt);
        _parts.Into(was);
    }

    // The solids whose bounds reach into the box from low to high, added to those near the brick.
    private void Near(List<Solid> solids, Vec3 low, Vec3 high)
    {
        foreach (Solid solid in solids)
        {
            if (solid.Overlaps(low, high))
            {
                _near.Add(solid);
            }
        }
    }

    /// <inheritdoc/>
    public bool Ready(int x, int y, int z, float voxelSize)
    {
        // The ground is read under the brick's columns and one past its far edges: the zones under it, a voxel round.
        float span = Brick.Size * voxelSize;
        Vector2s first = ZoneSystem.GetZone(new Vector3((x * span) - voxelSize, 0f, (z * span) - voxelSize));
        Vector2s last = ZoneSystem.GetZone(new Vector3(((x + 1) * span) + (2f * voxelSize), 0f, ((z + 1) * span) + (2f * voxelSize)));
        bool ready = true;
        for (int zz = first.y; zz <= last.y; zz++)
        {
            for (int zx = first.x; zx <= last.x; zx++)
            {
                // Each is asked for, not only the first missing: they are generated side by side.
                ready &= _terrain.Ready(new Vector2s(zx, zz));
            }
        }

        return ready;
    }

    /// <summary>Forgets a zone's objects, to read them again (one was built, broken, mined, opened or closed there).</summary>
    public void Forget(Vector2s zone) => _solidsByZone.Remove(TileData.Key(zone));

    /// <summary>Forgets the objects of the zones not among these (by <see cref="TileData.Key"/>): no brick needs them.</summary>
    public void Keep(HashSet<long> zones) => ZoneCache.Keep(_solidsByZone, zones);

    /// <summary>Forgets every zone's objects, to read them again (those known to hold far-reaching objects are still read).</summary>
    public void ForgetAll() => _solidsByZone.Clear();

    // The solids of a zone's objects, those reaching no farther than the game's prefabs and the others, read once
    // until forgotten.
    private (List<Solid> Near, List<Solid> Far) Solids(Vector2s zone)
    {
        long key = TileData.Key(zone);
        if (_solidsByZone.TryGetValue(key, out (List<Solid> Near, List<Solid> Far) solids))
        {
            return solids;
        }

        solids = ([], []);
        _found.Clear();
        ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), _found);
        float farthest = Shapes.Farthest;
        foreach (ZDO zdo in _found)
        {
            Shapes.AddSolids(zdo, Shapes.Reach(zdo) > farthest ? solids.Far : solids.Near);
        }

        if (solids.Far.Count == 0)
        {
            _farZones.Remove(zone);
        }
        else if (_farZones.Add(zone))
        {
            Grown++;
            foreach (Solid solid in solids.Far)
            {
                _grown.Add(solid.Bounds());
            }
        }

        _solidsByZone[key] = solids;
        return solids;
    }
}
