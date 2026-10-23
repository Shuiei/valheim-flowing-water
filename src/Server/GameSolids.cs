using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// What is solid for the water in the game: the ground (the generated terrain with the players' changes) and the
/// building pieces of the zones a brick spans, as <see cref="PieceShapes"/> sees them. Main thread only: the
/// terrain is read through the game's heightmap builder.
/// </summary>
internal sealed class GameSolids : ISolids
{
    private readonly ZoneTerrain _terrain;

    private readonly Dictionary<long, List<SolidBox>> _boxesByZone = [];

    private readonly List<ZDO> _found = [];

    private readonly List<SolidBox> _near = [];

    /// <summary>Solids over this terrain.</summary>
    public GameSolids(ZoneTerrain terrain)
    {
        _terrain = terrain;
    }

    /// <summary>The piece shapes used.</summary>
    public PieceShapes Shapes { get; } = new();

    /// <inheritdoc/>
    public void Fill(Brick brick, float voxelSize)
    {
        SolidRaster.Ground(brick, voxelSize, Ground);
        _near.Clear();
        float span = Brick.Size * voxelSize;
        Vector2s first = ZoneSystem.GetZone(new Vector3((brick.X * span) - voxelSize, 0f, (brick.Z * span) - voxelSize));
        Vector2s last = ZoneSystem.GetZone(new Vector3(((brick.X + 1) * span) + voxelSize, 0f, ((brick.Z + 1) * span) + voxelSize));
        for (int zz = first.y; zz <= last.y; zz++)
        {
            for (int zx = first.x; zx <= last.x; zx++)
            {
                _near.AddRange(Boxes(new Vector2s(zx, zz)));
            }
        }

        SolidRaster.Pieces(brick, voxelSize, _near, Ground);
    }

    /// <summary>Forgets a zone's pieces, to read them again (a piece was built, broken, opened or closed there).</summary>
    public void Forget(Vector2s zone) => _boxesByZone.Remove(TileData.Key(zone));

    // The solid boxes of a zone's pieces, read once until forgotten.
    private List<SolidBox> Boxes(Vector2s zone)
    {
        long key = TileData.Key(zone);
        if (_boxesByZone.TryGetValue(key, out List<SolidBox> boxes))
        {
            return boxes;
        }

        boxes = [];
        _found.Clear();
        ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), _found);
        foreach (ZDO zdo in _found)
        {
            Shapes.AddBoxes(zdo, boxes);
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
