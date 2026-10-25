using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// What is solid for the water in the game: the ground (the generated terrain with the players' changes) and the
/// building pieces of the zones a brick spans, as <see cref="PieceShapes"/> sees them. Main thread only: the
/// terrain is read through the game's heightmap builder. A piece scaled to reach farther than the game's own is kept
/// apart with its zone, so that a few such pieces do not make every brick read the pieces of zones that far round it.
/// </summary>
internal sealed class GameSolids : ISolids
{
    private readonly ZoneTerrain _terrain;

    // Each zone's pieces read: the boxes of those reaching no farther than the game's pieces, and of those that do.
    private readonly Dictionary<long, (List<SolidBox> Near, List<SolidBox> Far)> _boxesByZone = [];

    // The zones known to hold pieces reaching farther than the game's own: their far boxes go to every brick.
    private readonly HashSet<Vector2s> _farZones = [];

    private readonly List<Vector2s> _farZonesNow = [];

    private readonly List<ZDO> _found = [];

    private readonly List<SolidBox> _near = [];

    // Where the frame's time goes: a brick reading its solids is a part of its own, whoever asked for it.
    private readonly FrameParts _parts;

    private bool _notNumbersTold;

    /// <summary>Solids over this terrain, their reading timed in these parts.</summary>
    public GameSolids(ZoneTerrain terrain, FrameParts parts)
    {
        _terrain = terrain;
        _parts = parts;
    }

    /// <summary>The piece shapes used.</summary>
    public PieceShapes Shapes { get; } = new();

    /// <summary>The zones known to hold pieces reaching farther than the game's own (scaled).</summary>
    public IReadOnlyCollection<Vector2s> FarZones => _farZones;

    /// <summary>
    /// How many times a zone was found holding pieces reaching farther than the game's own: bricks that read their
    /// solids before may have missed them.
    /// </summary>
    public int Grown { get; private set; }

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

        // The zones whose pieces may reach the brick or the voxel round it: as far as the game's pieces reach, past that
        // voxel; and the pieces reaching farther, wherever they are known. Of their boxes, only those reaching the
        // brick's own surroundings (a voxel round it, and the voxel over that the raster tests): a big base by the
        // water would have every brick near it try every box of nine zones.
        float margin = Shapes.Farthest + voxelSize;
        Vector2s first = ZoneSystem.GetZone(new Vector3((brick.X * span) - margin, 0f, (brick.Z * span) - margin));
        Vector2s last = ZoneSystem.GetZone(new Vector3(((brick.X + 1) * span) + margin, 0f, ((brick.Z + 1) * span) + margin));
        var low = new Vec3((brick.X * span) - (2f * voxelSize), (brick.Y * span) - (2f * voxelSize), (brick.Z * span) - (2f * voxelSize));
        var high = new Vec3(((brick.X + 1) * span) + (2f * voxelSize), ((brick.Y + 1) * span) + (2f * voxelSize), ((brick.Z + 1) * span) + (2f * voxelSize));
        for (int zz = first.y; zz <= last.y; zz++)
        {
            for (int zx = first.x; zx <= last.x; zx++)
            {
                Near(Boxes(new Vector2s(zx, zz)).Near, low, high);
            }
        }

        _farZonesNow.Clear();
        _farZonesNow.AddRange(_farZones);
        foreach (Vector2s zone in _farZonesNow)
        {
            Near(Boxes(zone).Far, low, high);
        }

        SolidRaster.Pieces(brick, voxelSize, _near, _terrain.HeightAt);
        _parts.Into(was);
    }

    // The boxes whose bounds reach into the box from low to high, added to those near the brick.
    private void Near(List<SolidBox> boxes, Vec3 low, Vec3 high)
    {
        foreach (SolidBox box in boxes)
        {
            if (box.Overlaps(low, high))
            {
                _near.Add(box);
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

    /// <summary>Forgets a zone's pieces, to read them again (a piece was built, broken, opened or closed there).</summary>
    public void Forget(Vector2s zone) => _boxesByZone.Remove(TileData.Key(zone));

    /// <summary>Forgets the pieces of the zones not among these (by <see cref="TileData.Key"/>): no brick needs them.</summary>
    public void Keep(HashSet<long> zones) => ZoneCache.Keep(_boxesByZone, zones);

    /// <summary>Forgets every zone's pieces, to read them again (those known to hold far-reaching pieces are still read).</summary>
    public void ForgetAll() => _boxesByZone.Clear();

    // The solid boxes of a zone's pieces, those reaching no farther than the game's and the others, read once until
    // forgotten.
    private (List<SolidBox> Near, List<SolidBox> Far) Boxes(Vector2s zone)
    {
        long key = TileData.Key(zone);
        if (_boxesByZone.TryGetValue(key, out (List<SolidBox> Near, List<SolidBox> Far) boxes))
        {
            return boxes;
        }

        boxes = ([], []);
        _found.Clear();
        ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), _found);
        float farthest = Shapes.Farthest;
        foreach (ZDO zdo in _found)
        {
            Shapes.AddBoxes(zdo, Shapes.Reach(zdo) > farthest ? boxes.Far : boxes.Near);
        }

        if (boxes.Far.Count == 0)
        {
            _farZones.Remove(zone);
        }
        else if (_farZones.Add(zone))
        {
            Grown++;
        }

        _boxesByZone[key] = boxes;
        return boxes;
    }
}
