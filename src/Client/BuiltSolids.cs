using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowingWater.Server;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// What is built round the card's square, read by the player's own game for the particle and the volume water: the
/// solid objects it has near (building pieces, rocks, trees, a place's ruins), by the shapes the server reads them by
/// (<see cref="SolidShapes"/>), rastered a brick at a time (<see cref="BuiltVoxels"/>) on another thread. The server
/// says what is built only in the voxels it has water in, so a wall or a roof the server's water has not reached would
/// not hold the card's water without this. Looked over once a second; a brick is rastered again only when an object
/// near it was built, broken, moved, opened, closed or mined. Kept from one square to the next (the bricks are the
/// world's), let go a couple of bricks past the square. Main thread only, but for the raster.
/// </summary>
internal sealed class BuiltSolids
{
    // How often the objects round the square are looked over, seconds.
    private const float LookSeconds = 1f;

    // How far past the square objects are looked for, metres: a zone, more than the game's prefabs reach from their
    // place (an object scaled to reach farther is missed from beyond it).
    private const float Margin = 64f;

    // Bricks kept past the square, on each side.
    private const int KeptPast = 2;

    private static readonly IReadOnlyDictionary<(int X, int Y, int Z), byte[]> NoBricks = new Dictionary<(int X, int Y, int Z), byte[]>();

    private readonly SolidShapes _shapes = new();

    // Each solid object found round the square, by its id: what of it changes its solids (as one number) and its solids.
    private readonly Dictionary<ZDOID, (int Look, Solid[] Parts)> _objects = [];

    private readonly HashSet<ZDOID> _seen = [];

    private readonly List<ZDOID> _gone = [];

    private readonly List<ZDO> _found = [];

    private readonly List<Solid> _parts = [];

    // Each brick rastered, by its brick coordinates: what it was rastered from (as one number) and its voxels (null:
    // none solid).
    private readonly Dictionary<(int X, int Y, int Z), (int Key, byte[] Voxels)> _bricks = [];

    // Each brick round the square this look: what reaches it, as one number, and the solids reaching it.
    private readonly Dictionary<(int X, int Y, int Z), (int Key, List<Solid> Solids)> _reaching = [];

    private readonly List<(int X, int Y, int Z)> _away = [];

    private Task<List<((int X, int Y, int Z) At, int Key, byte[] Voxels)>> _raster;

    private float _lookedAt = float.NegativeInfinity;

    private (int X0, int Z0, int Columns, int BaseRow, float VoxelSize) _lookedFor;

    /// <summary>The solid objects found round the square at the last look.</summary>
    public int Objects => _objects.Count;

    /// <summary>How long the last look took on the main thread, milliseconds (the raster is on another).</summary>
    public double LookMilliseconds { get; private set; }

    /// <summary>Goes up each time <see cref="Bricks"/> changed.</summary>
    public int Version { get; private set; }

    /// <summary>The bricks rastered with any voxel solid or closing a face, as <see cref="BuiltVoxels.Write"/> takes them (never changed: a new one is made).</summary>
    public IReadOnlyDictionary<(int X, int Y, int Z), byte[]> Bricks { get; private set; } = NoBricks;

    /// <summary>
    /// The square's solids looked over (once a second, or at once when the square moved): columns a side from voxel
    /// column x0, z0, <see cref="SolidsLayout.Rows"/> rows from voxel row baseRow, at the voxel's size; the bricks whose
    /// objects changed rastered again on another thread, taken in once rastered.
    /// </summary>
    public void Update(int x0, int z0, int columns, int baseRow, float voxelSize)
    {
        Take();
        (int, int, int, int, float) area = (x0, z0, columns, baseRow, voxelSize);
        if (_raster != null || ZDOMan.instance == null || ZNetScene.instance == null || (Time.time - _lookedAt < LookSeconds && area.Equals(_lookedFor)))
        {
            return;
        }

        // (The voxel's size is one of a few the server's setting allows, 1, 0.5 or 0.25 m: any change is a real one.)
        if (Math.Abs(voxelSize - _lookedFor.VoxelSize) > 1e-4f)
        {
            _bricks.Clear();
            Publish();
        }

        _lookedAt = Time.time;
        _lookedFor = area;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        Look(x0, z0, columns, baseRow, voxelSize);
        LookMilliseconds = watch.Elapsed.TotalMilliseconds;
    }

    // The raster, once done, taken in; a fault in it is thrown (the card's water is turned off, and says why).
    private void Take()
    {
        if (_raster == null || !_raster.IsCompleted)
        {
            return;
        }

        Task<List<((int X, int Y, int Z) At, int Key, byte[] Voxels)>> raster = _raster;
        _raster = null;
        if (raster.IsFaulted)
        {
            throw new InvalidOperationException("what is built near the water could not be rastered for the graphics card", raster.Exception);
        }

        foreach (((int X, int Y, int Z) at, int key, byte[] voxels) in raster.Result)
        {
            _bricks[at] = (key, voxels);
        }

        Publish();
    }

    // The objects round the square read (those changed since), what reaches each brick of it, and the bricks it changed
    // for rastered again.
    private void Look(int x0, int z0, int columns, int baseRow, float voxelSize)
    {
        float span = Brick.Size * voxelSize;
        int bx0 = BuiltVoxels.BrickOf(x0 - 1), bx1 = BuiltVoxels.BrickOf(x0 + columns);
        int by0 = BuiltVoxels.BrickOf(baseRow - 1), by1 = BuiltVoxels.BrickOf(baseRow + SolidsLayout.Rows);
        int bz0 = BuiltVoxels.BrickOf(z0 - 1), bz1 = BuiltVoxels.BrickOf(z0 + columns);
        ReadObjects(new Vector3((bx0 * span) - Margin, 0f, (bz0 * span) - Margin), new Vector3(((bx1 + 1) * span) + Margin, 0f, ((bz1 + 1) * span) + Margin));

        _reaching.Clear();
        foreach ((int look, Solid[] parts) in _objects.Values)
        {
            for (int p = 0; p < parts.Length; p++)
            {
                Reach(parts[p], unchecked((look * 31) + p), voxelSize, (bx0, by0, bz0), (bx1, by1, bz1));
            }
        }

        bool changed = false;
        var toRaster = new List<((int X, int Y, int Z) At, int Key, Solid[] Solids)>();
        for (int by = by0; by <= by1; by++)
        {
            for (int bz = bz0; bz <= bz1; bz++)
            {
                for (int bx = bx0; bx <= bx1; bx++)
                {
                    (int, int, int) at = (bx, by, bz);
                    bool reached = _reaching.TryGetValue(at, out (int Key, List<Solid> Solids) reaching);
                    bool kept = _bricks.TryGetValue(at, out (int Key, byte[] Voxels) brick);
                    if (!reached)
                    {
                        changed |= kept && _bricks.Remove(at) && brick.Voxels != null;
                    }
                    else if (!kept || brick.Key != reaching.Key)
                    {
                        toRaster.Add((at, reaching.Key, reaching.Solids.ToArray()));
                    }
                }
            }
        }

        // Bricks far from the square let go (they are rastered again if it comes back).
        _away.Clear();
        foreach ((int X, int Y, int Z) at in _bricks.Keys)
        {
            if (at.X < bx0 - KeptPast || at.X > bx1 + KeptPast || at.Y < by0 - KeptPast || at.Y > by1 + KeptPast || at.Z < bz0 - KeptPast || at.Z > bz1 + KeptPast)
            {
                _away.Add(at);
            }
        }

        foreach ((int X, int Y, int Z) at in _away)
        {
            changed |= _bricks[at].Voxels != null;
            _bricks.Remove(at);
        }

        if (toRaster.Count > 0)
        {
            _raster = Task.Run(() =>
            {
                var rastered = new List<((int X, int Y, int Z) At, int Key, byte[] Voxels)>(toRaster.Count);
                foreach (((int X, int Y, int Z) at, int key, Solid[] solids) in toRaster)
                {
                    rastered.Add((at, key, BuiltVoxels.Raster(at.X, at.Y, at.Z, voxelSize, solids)));
                }

                return rastered;
            });
        }
        else if (changed)
        {
            Publish();
        }
    }

    // The solid objects in the zones from low to high (world x, z) read: those new or changed since made solids again,
    // those gone forgotten.
    private void ReadObjects(Vector3 low, Vector3 high)
    {
        _seen.Clear();
        Vector2s first = ZoneSystem.GetZone(low), last = ZoneSystem.GetZone(high);
        for (int zz = first.y; zz <= last.y; zz++)
        {
            for (int zx = first.x; zx <= last.x; zx++)
            {
                _found.Clear();
                ZDOMan.instance.FindSectorObjects(new Vector2s(zx, zz), new SimulationDistance(0, 0), _found);
                foreach (ZDO zdo in _found)
                {
                    if (!zdo.IsValid() || !_shapes.Has(zdo))
                    {
                        continue;
                    }

                    _seen.Add(zdo.m_uid);
                    int look = LookOf(zdo);
                    if (!_objects.TryGetValue(zdo.m_uid, out (int Look, Solid[] Parts) known) || known.Look != look)
                    {
                        _parts.Clear();
                        _shapes.AddSolids(zdo, _parts);
                        _objects[zdo.m_uid] = (look, _parts.ToArray());
                    }
                }
            }
        }

        _gone.Clear();
        foreach (ZDOID id in _objects.Keys)
        {
            if (!_seen.Contains(id))
            {
                _gone.Add(id);
            }
        }

        foreach (ZDOID id in _gone)
        {
            _objects.Remove(id);
        }
    }

    // What of an object changes its solids, as one number: which object, where, turned and scaled how, and its state
    // (a door open, a rock's bits mined).
    private int LookOf(ZDO zdo)
    {
        Vector3 at = zdo.GetPosition();
        int look = zdo.m_uid.GetHashCode();
        look = unchecked((look * 31) + at.GetHashCode());
        look = unchecked((look * 31) + zdo.GetRotation().GetHashCode());
        look = unchecked((look * 31) + SolidShapes.Scale(zdo).GetHashCode());
        return unchecked((look * 31) + _shapes.State(zdo));
    }

    // A solid added to the bricks round the square it reaches (with the two voxels round each brick its raster reads),
    // its number added into theirs.
    private void Reach(Solid solid, int number, float voxelSize, (int X, int Y, int Z) first, (int X, int Y, int Z) last)
    {
        (Vec3 low, Vec3 high) = solid.Bounds();
        float span = Brick.Size * voxelSize, room = 2f * voxelSize;
        int x0 = Math.Max(first.X, (int)Math.Floor((low.X - room) / span)), x1 = Math.Min(last.X, (int)Math.Floor((high.X + room) / span));
        int y0 = Math.Max(first.Y, (int)Math.Floor((low.Y - room) / span)), y1 = Math.Min(last.Y, (int)Math.Floor((high.Y + room) / span));
        int z0 = Math.Max(first.Z, (int)Math.Floor((low.Z - room) / span)), z1 = Math.Min(last.Z, (int)Math.Floor((high.Z + room) / span));
        for (int y = y0; y <= y1; y++)
        {
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    (int, int, int) at = (x, y, z);
                    if (!_reaching.TryGetValue(at, out (int Key, List<Solid> Solids) reaching))
                    {
                        reaching = (0, []);
                    }

                    reaching.Solids.Add(solid);
                    _reaching[at] = (unchecked(reaching.Key + (number * 16777619) + 1), reaching.Solids);
                }
            }
        }
    }

    // The bricks with anything solid, made again for the builds to take.
    private void Publish()
    {
        var bricks = new Dictionary<(int X, int Y, int Z), byte[]>();
        foreach (KeyValuePair<(int X, int Y, int Z), (int Key, byte[] Voxels)> brick in _bricks)
        {
            if (brick.Value.Voxels != null)
            {
                bricks[brick.Key] = brick.Value.Voxels;
            }
        }

        Bricks = bricks;
        Version++;
    }
}
