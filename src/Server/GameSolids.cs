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

    /// <summary>Solids over this terrain.</summary>
    public GameSolids(ZoneTerrain terrain)
    {
        _terrain = terrain;
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
        SolidRaster.Ground(brick, voxelSize, Ground);
        _near.Clear();
        float span = Brick.Size * voxelSize;
        // The zones whose pieces may reach the brick or the voxel round it: as far as the game's pieces reach, past that
        // voxel; and the pieces reaching farther, wherever they are known.
        float margin = Shapes.Farthest + voxelSize;
        Vector2s first = ZoneSystem.GetZone(new Vector3((brick.X * span) - margin, 0f, (brick.Z * span) - margin));
        Vector2s last = ZoneSystem.GetZone(new Vector3(((brick.X + 1) * span) + margin, 0f, ((brick.Z + 1) * span) + margin));
        for (int zz = first.y; zz <= last.y; zz++)
        {
            for (int zx = first.x; zx <= last.x; zx++)
            {
                _near.AddRange(Boxes(new Vector2s(zx, zz)).Near);
            }
        }

        _farZonesNow.Clear();
        _farZonesNow.AddRange(_farZones);
        foreach (Vector2s zone in _farZonesNow)
        {
            _near.AddRange(Boxes(zone).Far);
        }

        SolidRaster.Pieces(brick, voxelSize, _near, Ground);
    }

    /// <summary>Forgets a zone's pieces, to read them again (a piece was built, broken, opened or closed there).</summary>
    public void Forget(Vector2s zone) => _boxesByZone.Remove(TileData.Key(zone));

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

    private float Ground(float x, float z)
    {
        var point = new Vector3(x, 0f, z);
        Vector2s zone = ZoneSystem.GetZone(point);
        return _terrain.HeightAt(zone, x - TileData.OriginX(zone), z - TileData.OriginZ(zone));
    }
}
