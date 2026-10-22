using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// The water on the server, in 3D: a <see cref="VoxelGrid"/> of the CellSize setting, moved by a
/// <see cref="VoxelSolver"/>, over the ground and building pieces (<see cref="GameSolids"/>, kept up to date by a
/// <see cref="SolidsWatch"/>). Sources pour at their point, drains take within their reach, the sea takes what
/// reaches it. Each brick with water is kept in a FlowingWater_Brick object, which the game saves with the world and
/// sends to the players near it. The water of the column tiles before (FlowingWater_Tile) is moved into voxels once.
/// </summary>
internal sealed class VoxelServer
{
    // Longest frame stepped at once, seconds: a hitch is let go rather than stepped in one long go.
    private const float LongestFrame = 0.25f;

    private const float TerrainRefreshSeconds = 2f;

    // The debug map draws one character per this many metres.
    private const float MapMetres = 2f;

    private readonly ZoneTerrain _terrain = new();

    private readonly GameSolids _solids;

    private readonly SolidsWatch _watch;

    private readonly Dictionary<long, ZDO> _objects = [];

    private readonly Dictionary<long, int> _sent = [];

    private readonly List<Source> _markers = [];

    private readonly List<ZDO> _zdos = [];

    private readonly Stopwatch _stepWatch = new();

    private VoxelGrid _grid;

    private VoxelSolver _solver;

    private float _scanTimer = float.MaxValue;

    private float _sendTimer;

    private float _terrainTimer;

    private float _debugTimer;

    private bool _loaded;

    private bool _capWarned;

    /// <summary>A server with an empty grid; the world's water is read on the first update.</summary>
    public VoxelServer()
    {
        _solids = new GameSolids(_terrain);
        _watch = new SolidsWatch(_solids, _terrain);
        NewGrid(SettingVoxelSize());
    }

    /// <summary>One frame: sources and drains, the solids' changes, the water moved, the bricks sent.</summary>
    public void Update(float dt)
    {
        if (!_loaded)
        {
            Load();
            _loaded = true;
        }

        ApplyVoxelSize();
        _scanTimer += dt;
        if (_scanTimer >= FlowingWaterPlugin.Settings.MarkerScanSeconds.Value)
        {
            _scanTimer = 0f;
            ScanMarkers();
        }

        _terrainTimer += dt;
        if (_terrainTimer >= TerrainRefreshSeconds)
        {
            _terrainTimer = 0f;
            _terrain.Refresh();
        }

        _watch.Update(dt, _grid);
        dt = Mathf.Min(dt, LongestFrame);
        foreach (Source marker in _markers)
        {
            if (marker.Drain)
            {
                Drain(marker, dt);
            }
            else
            {
                Pour(marker, dt);
            }
        }

        _grid.MaxBricks = FlowingWaterPlugin.Settings.MaxBricks.Value;
        _solver.SeaLevel = ZoneSystem.instance.m_waterLevel;
        _stepWatch.Start();
        _solver.Step(dt);
        _stepWatch.Stop();
        WarnCapped();
        _grid.ForgetDry(dt, FlowingWaterPlugin.Settings.EmptyTileSeconds.Value);

        _sendTimer += dt;
        if (_sendTimer >= FlowingWaterPlugin.Settings.SendSeconds.Value)
        {
            _sendTimer = 0f;
            Publish();
        }

        float debug = FlowingWaterPlugin.Settings.DebugSeconds.Value;
        if (debug > 0f)
        {
            _debugTimer += dt;
            if (_debugTimer >= debug)
            {
                _debugTimer = 0f;
                LogMaps();
            }
        }
    }

    /// <summary>Sends every changed brick now (the world is about to be saved), and forgets dry ones.</summary>
    public void Publish()
    {
        var kept = new HashSet<long>();
        foreach (Brick brick in _grid.Bricks)
        {
            if (!Array.Exists(brick.Water, w => w > Brick.Dry))
            {
                continue;
            }

            long key = VoxelGrid.Key(brick.X, brick.Y, brick.Z);
            kept.Add(key);
            byte[] data = BrickData.Encode(brick, _grid.VoxelSize);
            int hash = Hash(data);
            if (_sent.TryGetValue(key, out int was) && was == hash && _objects.TryGetValue(key, out ZDO known) && ZDOMan.instance.GetZDO(known.m_uid) != null)
            {
                continue;
            }

            ZDO zdo = Object(brick, key);
            zdo.Set(ZdoKeys.Brick, data);
            _sent[key] = hash;
        }

        foreach (long key in _objects.Keys.Where(k => !kept.Contains(k)).ToList())
        {
            if (ZDOMan.instance.GetZDO(_objects[key].m_uid) != null)
            {
                DestroyZdo(_objects[key]);
            }

            _objects.Remove(key);
            _sent.Remove(key);
        }
    }

    /// <summary>Stops listening to the world (it closes).</summary>
    public void Close() => _watch.Close();

    private static float SettingVoxelSize() => (float)TileData.ZoneMetres / TileData.SizeFor(FlowingWaterPlugin.Settings.CellSize.Value);

    private void NewGrid(float voxelSize)
    {
        _grid = new VoxelGrid(voxelSize, _solids) { MaxBricks = FlowingWaterPlugin.Settings.MaxBricks.Value };
        _solver = new VoxelSolver(_grid);
    }

    // The brick's object, made (or made again, if something removed it) where the brick is.
    private ZDO Object(Brick brick, long key)
    {
        if (_objects.TryGetValue(key, out ZDO zdo) && ZDOMan.instance.GetZDO(zdo.m_uid) != null)
        {
            if (!zdo.IsOwner())
            {
                zdo.SetOwner(ZDOMan.GetSessionID());
            }

            return zdo;
        }

        float span = Brick.Size * _grid.VoxelSize;
        zdo = NewZdo(new Vector3((brick.X + 0.5f) * span, (brick.Y + 0.5f) * span, (brick.Z + 0.5f) * span), Prefabs.BrickHash);
        _objects[key] = zdo;
        return zdo;
    }

    // As ZNetView sets up a new object's ZDO: CreateNewZDO leaves the prefab unset, and a ZDO without one is saved
    // but can never be found again by prefab.
    private static ZDO NewZdo(Vector3 position, int prefab)
    {
        ZDO zdo = ZDOMan.instance.CreateNewZDO(position, prefab);
        zdo.SetPrefab(prefab);
        zdo.Persistent = true;
        zdo.SetType(ZDO.ObjectType.Default);
        zdo.SetDistant(false);
        return zdo;
    }

    private static void DestroyZdo(ZDO zdo)
    {
        zdo.SetOwner(ZDOMan.GetSessionID());
        ZDOMan.instance.DestroyZDO(zdo);
    }

    private static int Hash(byte[] data)
    {
        int hash = data.Length;
        foreach (byte b in data)
        {
            hash = unchecked((hash * 31) + b);
        }

        return hash;
    }

    // The bricks saved with the world, and the column tiles of before, moved into voxels.
    private void Load()
    {
        float bricks = LoadBricks();
        float tiles = LoadTiles();
        FlowingWaterPlugin.Log.LogInfo(FormattableString.Invariant($"Water: {_grid.Bricks.Count} bricks of {_grid.VoxelSize} m voxels, {bricks:0.0} m³ read from bricks, {tiles:0.0} m³ moved over from column tiles"));
    }

    private float LoadBricks()
    {
        float total = 0f;
        foreach (ZDO zdo in All(Prefabs.BrickName))
        {
            BrickView view = Read(zdo);
            if (view == null)
            {
                continue;
            }

            total += PourView(view);
            DestroyZdo(zdo);
        }

        return total;
    }

    // A saved brick's water into the grid: as it was when the voxels are the same size, else poured at each voxel's
    // middle into the new grid's voxels.
    private float PourView(BrickView view)
    {
        float s = view.VoxelSize;
        float volume = s * s * s;
        float total = 0f;
        for (int i = 0; i < Brick.Count; i++)
        {
            if (view.Water[i] <= Brick.Dry)
            {
                continue;
            }

            (int x, int y, int z) = Brick.Place(i);
            var middle = new Vector3(((view.X * Brick.Size) + x + 0.5f) * s, ((view.Y * Brick.Size) + y + 0.5f) * s, ((view.Z * Brick.Size) + z + 0.5f) * s);
            PourAt(middle, view.Water[i] * volume);
            total += view.Water[i] * volume;
        }

        return total;
    }

    private static BrickView Read(ZDO zdo)
    {
        byte[] data = zdo.GetByteArray(ZdoKeys.Brick);
        if (data == null)
        {
            return null;
        }

        try
        {
            return BrickData.Decode(data);
        }
        catch (InvalidDataException e)
        {
            FlowingWaterPlugin.Log.LogWarning($"Water brick {zdo.m_uid}: unreadable, its water is lost ({e.Message})");
            return null;
        }
    }

    // Each column tile's water poured on the ground of its cells, then the tile removed.
    private float LoadTiles()
    {
        float total = 0f;
        foreach (ZDO zdo in All(Prefabs.TileName))
        {
            var data = new TileData(ZoneSystem.GetZone(zdo.GetPosition()));
            byte[] bytes = zdo.GetByteArray(ZdoKeys.Data);
            if (bytes != null && data.Decode(bytes))
            {
                float area = data.CellSize * data.CellSize;
                for (int z = 0; z < data.Size; z++)
                {
                    for (int x = 0; x < data.Size; x++)
                    {
                        float depth = data.Depth[data.Index(x, z)];
                        if (depth > 0f)
                        {
                            float wx = data.WorldX(x);
                            float wz = data.WorldZ(z);
                            PourAt(new Vector3(wx, Ground(wx, wz) + 0.01f, wz), depth * area);
                            total += depth * area;
                        }
                    }
                }
            }

            DestroyZdo(zdo);
        }

        return total;
    }

    private List<ZDO> All(string prefab)
    {
        int index = 0;
        _zdos.Clear();
        while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, _zdos, ref index))
        {
        }

        return [.. _zdos];
    }

    // The CellSize setting changed: the water is poured into a grid of the new size.
    private void ApplyVoxelSize()
    {
        float size = SettingVoxelSize();
        if (Mathf.Approximately(size, _grid.VoxelSize))
        {
            return;
        }

        float s = _grid.VoxelSize;
        List<BrickView> views = [.. _grid.Bricks.Select(b => BrickData.Decode(BrickData.Encode(b, s)))];
        NewGrid(size);
        float total = views.Sum(PourView);
        FlowingWaterPlugin.Log.LogInfo(FormattableString.Invariant($"Water: voxels of {size} m now, {total:0.0} m³ moved over"));
    }

    // Water poured at a point: into its voxel, or the first open one over it when the point is in a solid.
    private void PourAt(Vector3 point, float cubicMetres)
    {
        float s = _grid.VoxelSize;
        int x = Mathf.FloorToInt(point.x / s);
        int y = Mathf.FloorToInt(point.y / s);
        int z = Mathf.FloorToInt(point.z / s);
        for (int up = 0; up < Brick.Size * 4; up++)
        {
            Brick brick = _grid.Get(x >> 4, (y + up) >> 4, z >> 4);
            if (brick.Solid[Brick.Index(x & 15, (y + up) & 15, z & 15)] < 1f)
            {
                _grid.Pour(x, y + up, z, cubicMetres);
                return;
            }
        }

        FlowingWaterPlugin.Log.LogWarning(FormattableString.Invariant($"Water: no room over {point} for {cubicMetres:0.00} m³; dropped"));
    }

    private void Pour(Source source, float dt) => PourAt(source.Point + new Vector3(0f, 0.05f, 0f), source.Rate * dt);

    // A drain takes from every voxel within its reach: all of it with no rate, else at most its rate, from each in
    // proportion to its water.
    private void Drain(Source drain, float dt)
    {
        float s = _grid.VoxelSize;
        int reach = Mathf.Max(0, Mathf.FloorToInt(drain.Radius / s));
        int cx = Mathf.FloorToInt(drain.Point.x / s);
        int cy = Mathf.FloorToInt((drain.Point.y + 0.05f) / s);
        int cz = Mathf.FloorToInt(drain.Point.z / s);
        float there = 0f;
        var found = new List<(Brick Brick, int Index)>();
        for (int dy = -reach; dy <= reach; dy++)
        {
            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if (((dx * dx) + (dy * dy) + (dz * dz)) * s * s <= drain.Radius * drain.Radius || (dx == 0 && dy == 0 && dz == 0))
                    {
                        if (_grid.Locate(cx + dx, cy + dy, cz + dz, out Brick brick, out int i) && brick.Water[i] > 0f)
                        {
                            found.Add((brick, i));
                            there += brick.Water[i];
                        }
                    }
                }
            }
        }

        float volume = s * s * s;
        float share = there <= 0f ? 0f : drain.Rate <= 0f ? 1f : Mathf.Min(1f, drain.Rate * dt / (there * volume));
        foreach ((Brick brick, int i) in found)
        {
            brick.Water[i] -= brick.Water[i] * share;
        }
    }

    private void WarnCapped()
    {
        if (_grid.Capped && !_capWarned)
        {
            _capWarned = true;
            FlowingWaterPlugin.Log.LogWarning($"Water: {FlowingWaterPlugin.Settings.MaxBricks.Value} bricks (MaxBricks) reached; the water stops spreading further");
        }
    }

    private float Ground(float x, float z)
    {
        Vector2s zone = ZoneSystem.GetZone(new Vector3(x, 0f, z));
        return _terrain.HeightAt(zone, x - TileData.OriginX(zone), z - TileData.OriginZ(zone));
    }

    private void ScanMarkers()
    {
        int before = _markers.Count;
        _markers.Clear();
        Collect(Prefabs.SourceName, false, FlowingWaterPlugin.Settings.DefaultSourceRate.Value);
        Collect(Prefabs.DrainName, true, FlowingWaterPlugin.Settings.DefaultDrainRate.Value);
        CollectFixed(FlowingWaterPlugin.Settings.FixedSources.Value, false);
        CollectFixed(FlowingWaterPlugin.Settings.FixedDrains.Value, true);
        if (_markers.Count != before)
        {
            FlowingWaterPlugin.Log.LogInfo($"Water: {_markers.Count} sources and drains");
        }
    }

    private void Collect(string prefab, bool drain, float defaultRate)
    {
        float defaultRadius = FlowingWaterPlugin.Settings.DefaultDrainRadius.Value;
        foreach (ZDO zdo in All(prefab))
        {
            _markers.Add(new Source(zdo.GetPosition(), zdo.GetFloat(ZdoKeys.Rate, defaultRate), drain, drain ? zdo.GetFloat(ZdoKeys.Radius, defaultRadius) : 0f));
        }
    }

    // "x,z,rate; x,z,rate" from the config, drains with an optional radius: "x,z,rate,radius"; on the ground there.
    private void CollectFixed(string list, bool drain)
    {
        foreach (string item in list.Split(';'))
        {
            string trimmed = item.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            string[] parts = trimmed.Split(',');
            float radius = FlowingWaterPlugin.Settings.DefaultDrainRadius.Value;
            if (parts.Length < 3 || parts.Length > (drain ? 4 : 3)
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float rate)
                || (parts.Length == 4 && (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out radius) || radius <= 0f)))
            {
                FlowingWaterPlugin.Log.LogWarning($"Fixed {(drain ? "drain" : "source")} \"{trimmed}\" is not {(drain ? "x,z,rate or x,z,rate,radius" : "x,z,rate")}; ignored");
                continue;
            }

            _markers.Add(new Source(new Vector3(x, Ground(x, z), z), rate, drain, drain ? radius : 0f));
        }
    }

    // A top-down map per zone, a character per 2 by 2 metres: '.' dry, then deeper and deeper (all the water over
    // the column, metres).
    private void LogMaps()
    {
        float s = _grid.VoxelSize;
        var depths = new Dictionary<long, Dictionary<(int X, int Z), float>>();
        foreach (Brick brick in _grid.Bricks)
        {
            for (int i = 0; i < Brick.Count; i++)
            {
                if (brick.Water[i] <= Brick.Dry)
                {
                    continue;
                }

                (int x, int _, int z) = Brick.Place(i);
                float wx = ((brick.X * Brick.Size) + x + 0.5f) * s;
                float wz = ((brick.Z * Brick.Size) + z + 0.5f) * s;
                Vector2s zone = ZoneSystem.GetZone(new Vector3(wx, 0f, wz));
                long key = TileData.Key(zone);
                if (!depths.TryGetValue(key, out Dictionary<(int X, int Z), float> map))
                {
                    depths[key] = map = [];
                }

                var cell = ((int)((wx - TileData.OriginX(zone)) / MapMetres), (int)((wz - TileData.OriginZ(zone)) / MapMetres));
                map[cell] = (map.TryGetValue(cell, out float d) ? d : 0f) + (brick.Water[i] * s * s * s / (MapMetres * MapMetres));
            }
        }

        var text = new StringBuilder();
        text.Append(FormattableString.Invariant($"Water: {_grid.Bricks.Count} bricks of {s} m voxels, {_grid.TotalWater():0.0} m³, {_markers.Count} markers; stepping took {_stepWatch.Elapsed.TotalMilliseconds:0} ms"));
        _stepWatch.Reset();
        int cells = (int)(TileData.ZoneMetres / MapMetres);
        foreach (KeyValuePair<long, Dictionary<(int X, int Z), float>> zone in depths)
        {
            text.Append(FormattableString.Invariant($"\nzone {zone.Key >> 32},{(int)zone.Key}: {zone.Value.Values.Sum() * MapMetres * MapMetres:0.0} m³"));
            for (int z = cells - 1; z >= 0; z--)
            {
                text.Append('\n');
                for (int x = 0; x < cells; x++)
                {
                    float depth = zone.Value.TryGetValue((x, z), out float d) ? d : 0f;
                    text.Append(depth <= 0.01f ? '.' : depth < 0.1f ? ':' : depth < 0.5f ? 'o' : depth < 2f ? 'O' : '#');
                }
            }
        }

        FlowingWaterPlugin.Log.LogInfo(text.ToString());
    }

    // A source or drain: where, how much a second, and a drain's reach.
    private sealed class Source
    {
        public Source(Vector3 point, float rate, bool drain, float radius)
        {
            Point = point;
            Rate = rate;
            Drain = drain;
            Radius = radius;
        }

        public Vector3 Point { get; }

        public float Rate { get; }

        public bool Drain { get; }

        public float Radius { get; }
    }
}
