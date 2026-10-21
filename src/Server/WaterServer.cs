using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// The server's water simulation over the whole world: a tile per zone with water, stepped at a
/// fixed rate from the sources and drains players placed, with each changed tile written into its
/// ZDO so that the game sends it to the players near it. Works on ZDOs only, never on scene objects,
/// so it runs on a dedicated server and far from every player alike.
/// </summary>
internal sealed class WaterServer
{
    public static readonly int DataHash = "FW_Data".GetStableHashCode();

    public static readonly int RateHash = "FW_Rate".GetStableHashCode();

    public static readonly int RadiusHash = "FW_Radius".GetStableHashCode();

    /// <summary>A zone's ground change from erosion, in its FlowingWater_Ground object (ErosionGround).</summary>
    public static readonly int GroundHash = "FW_Ground".GetStableHashCode();

    private const float MinDepth = 0.0005f;

    private const float Gravity = 9.81f;

    // The most of a level difference a pipe may move in one step: half, so two cells level out
    // without crossing over.
    private const float Overshoot = 0.5f;

    // What a flow keeps of itself each step while pushed uphill.
    private const float UphillKeep = 0.5f;

    private const float VelocitySmoothing = 0.25f;

    private const float TerrainRefreshSeconds = 2f;

    private const int MaxStepsPerFrame = 4;

    // A source spreads its water over about this many metres around it; a drain takes from about this far.
    private const float SourceReach = 1f;

    // The depth a pipe's push counts at most: deeper water would carry waves across more than half a
    // cell a step and the step would go unstable. Lakes still level out, just no faster than this.
    private const float MaxPushDepth = 4f;

    // Shallower than this, the bed's drag is taken at this depth (no division by nothing).
    private const float MinDragDepth = 0.005f;

    // A zone's ground change is written to the world at most this often (it is resent to the players near
    // it each time), and its protected cells looked for again this often.
    private const float GroundWriteSeconds = 60f, KeptSeconds = 30f;

    // Long enough, in erosion's time, for any water that dried to set down all it carried.
    private const float AllDown = 1000f;

    // The debug map draws one character per this many metres.
    private const float MapMetres = 2f;

    private readonly ZoneTerrain _terrain = new();

    private readonly Dictionary<long, ServerTile> _tiles = [];

    private readonly List<ServerTile> _stepTiles = [];

    private readonly List<Marker> _markers = [];

    private readonly List<ZDO> _zdos = [];

    // Zones water tried to flow into during a step while they had no tile: made after the step
    // (the tiles are stepped in parallel, and only the main thread may make objects).
    private readonly HashSet<Vector2s> _wanted = [];

    // Each zone's FlowingWater_Ground object, where erosion's change to its ground is kept.
    private readonly Dictionary<long, ZDO> _grounds = [];

    private readonly List<ZDO> _near = [];

    // Seconds since the simulation started, for the ground's write pacing.
    private float _clock;

    private float _keptTimer;

    // Time spent stepping since the last debug map, for the cost of a grid.
    private readonly Stopwatch _stepWatch = new();

    private int _stepCount;

    private float _stepTime;

    private float _scanTimer = float.MaxValue;

    private float _sendTimer;

    private float _terrainTimer;

    private float _debugTimer;

    private bool _loaded;

    private bool _capWarned;

    // Cells per side of every tile, from the CellSize setting.
    private int _size;

    // How far each direction's neighbour is in a tile's arrays, for that size.
    private readonly int[] _offsets = new int[Directions.Count];

    public int TileCount => _tiles.Count;

    private float CellSize => (float)TileData.ZoneMetres / _size;

    private float CellArea => CellSize * CellSize;

    public void Update(float dt)
    {
        if (!_loaded)
        {
            SetSize(TileData.SizeFor(FlowingWaterPlugin.Settings.CellSize.Value));
            Load();
            _loaded = true;
        }

        ApplyCellSize();

        _scanTimer += dt;
        if (_scanTimer >= FlowingWaterPlugin.Settings.MarkerScanSeconds.Value)
        {
            _scanTimer = 0f;
            ScanMarkers();
        }

        _clock += dt;
        _keptTimer += dt;
        if (_keptTimer >= KeptSeconds && FlowingWaterPlugin.Settings.Erosion.Value)
        {
            _keptTimer = 0f;
            foreach (ServerTile tile in _tiles.Values)
            {
                RefreshKept(tile);
            }
        }

        _terrainTimer += dt;
        if (_terrainTimer >= TerrainRefreshSeconds)
        {
            _terrainTimer = 0f;
            _terrain.Refresh();
        }

        foreach (ServerTile tile in _tiles.Values)
        {
            SyncTerrain(tile);
        }

        // Smaller cells take shorter steps: water crosses at most about a cell a step, so it keeps
        // the same speed (and the steps stay stable) whatever the grid.
        float step = CellSize / FlowingWaterPlugin.Settings.StepsPerSecond.Value;
        int maxSteps = Mathf.CeilToInt(MaxStepsPerFrame / CellSize);
        _stepTime += dt;
        int steps = 0;
        while (_stepTime >= step && steps < maxSteps)
        {
            _stepWatch.Start();
            Step(step);
            _stepWatch.Stop();
            _stepCount++;
            _stepTime -= step;
            steps++;
        }

        if (_stepTime > step)
        {
            // Behind by more than a frame can catch up: drop the time rather than snowball.
            _stepTime = 0f;
        }

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

    // The CellSize setting changed (a config manager, or the file while running): every tile moves
    // its water to the new grid, and the markers find their cells on it again.
    private void ApplyCellSize()
    {
        int size = TileData.SizeFor(FlowingWaterPlugin.Settings.CellSize.Value);
        if (size == _size)
        {
            return;
        }

        SetSize(size);
        float kept = 0f;
        foreach (ServerTile tile in _tiles.Values)
        {
            // What erosion did is kept in the world at the game's one-metre points: the new grid starts from it.
            WriteGround(tile);

            tile.Resize(size);
            LoadGround(tile);
            SyncTerrain(tile);
            tile.Water = Sum(tile.Data.Depth) * CellArea;
            kept += tile.Water;
        }

        _scanTimer = float.MaxValue;
        FlowingWaterPlugin.Log.LogInfo(FormattableString.Invariant($"Water: cells of {CellSize} m now ({size} per zone side), {_tiles.Count} tiles moved over with {kept:0.0} m³"));
    }

    private void SetSize(int size)
    {
        _size = size;
        for (int dir = 0; dir < Directions.Count; dir++)
        {
            _offsets[dir] = (Directions.Dz[dir] * size) + Directions.Dx[dir];
        }
    }

    // One character per 2 x 2 metres: '.' dry, then deeper and deeper; '>' '<' '^' 'v' where it runs.
    private void LogMaps()
    {
        var text = new StringBuilder();
        int block = Mathf.Max(1, Mathf.RoundToInt(MapMetres / CellSize));
        text.Append(FormattableString.Invariant($"Water: {_tiles.Count} tiles, {_markers.Count} markers, cells of {CellSize} m; {_stepCount} steps took {_stepWatch.Elapsed.TotalMilliseconds:0} ms"));
        _stepWatch.Reset();
        _stepCount = 0;
        foreach (ServerTile tile in _tiles.Values)
        {
            TileData data = tile.Data;
            float minGround = float.MaxValue;
            float maxGround = float.MinValue;
            foreach (float height in data.Height)
            {
                minGround = Mathf.Min(minGround, height);
                maxGround = Mathf.Max(maxGround, height);
            }

            text.Append(FormattableString.Invariant($"\nzone {data.Zone}: {tile.Water:0.0} m³, ground {minGround:0.0}..{maxGround:0.0} m"));
            if (FlowingWaterPlugin.Settings.Erosion.Value)
            {
                text.Append(ErosionSummary(tile));
            }
            for (int z = data.Size - block; z >= 0; z -= block)
            {
                text.Append('\n');
                for (int x = 0; x < data.Size; x += block)
                {
                    text.Append(Glyph(data, x, z, block));
                }
            }
        }

        FlowingWaterPlugin.Log.LogInfo(text.ToString());
    }

    // What erosion did to a tile: ground cut and laid down (cubic metres), the deepest cut and the ground carried.
    private string ErosionSummary(ServerTile tile)
    {
        float cut = 0f;
        float raised = 0f;
        float deepest = 0f;
        foreach (float change in tile.GroundChange)
        {
            cut -= Mathf.Min(0f, change);
            raised += Mathf.Max(0f, change);
            deepest = Mathf.Min(deepest, change);
        }

        float carried = 0f;
        foreach (float sediment in tile.Sediment)
        {
            carried += sediment;
        }

        return FormattableString.Invariant($"; erosion cut {cut * CellArea:0.00} m³ (deepest {-deepest:0.000} m), laid down {raised * CellArea:0.00} m³, carried {carried * CellArea:0.00} m³");
    }

    private static char Glyph(TileData data, int x, int z, int block)
    {
        float depth = 0f;
        float vx = 0f;
        float vz = 0f;
        for (int dz = 0; dz < block; dz++)
        {
            for (int dx = 0; dx < block; dx++)
            {
                int i = data.Index(x + dx, z + dz);
                depth = Mathf.Max(depth, data.Depth[i]);
                vx += data.VelX[i];
                vz += data.VelZ[i];
            }
        }

        if (depth <= 0.01f)
        {
            return '.';
        }

        float mean = 1f / (block * block);
        vx *= mean;
        vz *= mean;
        if (Mathf.Abs(vx) + Mathf.Abs(vz) > 0.15f)
        {
            return Mathf.Abs(vx) > Mathf.Abs(vz) ? (vx > 0f ? '>' : '<') : (vz > 0f ? '^' : 'v');
        }

        return depth < 0.1f ? ':' : depth < 0.5f ? 'o' : depth < 2f ? 'O' : '#';
    }

    // As ZNetView sets up a new object's ZDO: CreateNewZDO leaves the prefab unset, and a ZDO without
    // one is saved but can never be found again by prefab.
    private static ZDO NewZdo(Vector2s zone, int prefab)
    {
        ZDO zdo = ZDOMan.instance.CreateNewZDO(ZoneSystem.GetZonePos(zone), prefab);
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

    private void Load()
    {
        LoadGrounds();
        int index = 0;
        _zdos.Clear();
        while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(Prefabs.TileName, _zdos, ref index))
        {
        }

        FlowingWaterPlugin.Log.LogInfo($"Water: {_zdos.Count} tile objects found among {ZDOMan.instance.NrOfObjects()}");
        int unreadable = 0;
        float saved = 0f;
        float kept = 0f;
        foreach (ZDO zdo in _zdos)
        {
            Vector2s zone = ZoneSystem.GetZone(zdo.GetPosition());
            long key = TileData.Key(zone);
            if (_tiles.ContainsKey(key))
            {
                FlowingWaterPlugin.Log.LogWarning($"Zone {zone} has two water tiles; removing {zdo.m_uid}");
                DestroyZdo(zdo);
                continue;
            }

            var tile = new ServerTile(zone, zdo, _size);
            byte[] data = zdo.GetByteArray(DataHash);
            if (data != null && !tile.Data.Decode(data))
            {
                unreadable++;
            }

            // Saved on another grid (the setting changed since): moved over to this one.
            saved += Sum(tile.Data.Depth) * tile.Data.CellSize * tile.Data.CellSize;
            tile.Resize(_size);
            LoadGround(tile);
            SyncTerrain(tile);
            tile.Water = Sum(tile.Data.Depth) * CellArea;
            kept += tile.Water;
            _tiles[key] = tile;
        }

        FlowingWaterPlugin.Log.LogInfo(FormattableString.Invariant($"Water: {_tiles.Count} tiles loaded, {kept:0.0} m³ on cells of {CellSize} m ({saved:0.0} m³ as saved)")
            + (unreadable > 0 ? $", {unreadable} unreadable (now empty)" : string.Empty));
    }

    private static float Sum(float[] values)
    {
        float total = 0f;
        foreach (float value in values)
        {
            total += value;
        }

        return total;
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
        int index = 0;
        _zdos.Clear();
        while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, _zdos, ref index))
        {
        }

        float defaultRadius = FlowingWaterPlugin.Settings.DefaultDrainRadius.Value;
        foreach (ZDO zdo in _zdos)
        {
            Vector2s zone = TileData.CellOf(zdo.GetPosition(), _size, out int x, out int z);
            _markers.Add(new Marker(zone, x, z, zdo.GetFloat(RateHash, defaultRate), drain, drain ? zdo.GetFloat(RadiusHash, defaultRadius) : 0f));
        }
    }

    // "x,z,rate; x,z,rate" from the config, drains with an optional radius: "x,z,rate,radius".
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

            Vector2s zone = TileData.CellOf(new Vector3(x, 0f, z), _size, out int cx, out int cz);
            _markers.Add(new Marker(zone, cx, cz, rate, drain, drain ? radius : 0f));
        }
    }

    private void SyncTerrain(ServerTile tile)
    {
        float[] heights = _terrain.Heights(tile.Data.Zone, out int version);
        if (version == tile.TerrainVersion)
        {
            return;
        }

        tile.TerrainVersion = version;
        TileData data = tile.Data;
        float cell = data.CellSize;
        for (int z = 0; z < data.Size; z++)
        {
            for (int x = 0; x < data.Size; x++)
            {
                // The terrain's own, and what erosion changed over it.
                int i = data.Index(x, z);
                data.Height[i] = ZoneTerrain.Sample(heights, x * cell, z * cell) + tile.GroundChange[i];
            }
        }
    }

    // Sources first; then every tile's flows, then every tile's water, each phase over all tiles at
    // once on the thread pool (a tile only writes its own cells, and reads its neighbours' from the
    // phase before); then the drains, and the tiles water reached.
    private void Step(float dt)
    {
        foreach (Marker marker in _markers)
        {
            if (marker.Drain)
            {
                continue;
            }

            ServerTile tile = GetOrCreate(marker.Zone);
            if (tile == null)
            {
                continue;
            }

            Emit(tile, marker.X, marker.Z, marker.Rate * dt);
            tile.HasSource = true;
            tile.Dirty = true;
        }

        _stepTiles.Clear();
        _stepTiles.AddRange(_tiles.Values);
        LoadNeighbourGround();
        var flow = new FlowStep(dt);
        Parallel.For(0, _stepTiles.Count, k => UpdateFlux(_stepTiles[k], flow));
        bool erosion = FlowingWaterPlugin.Settings.Erosion.Value;
        if (erosion)
        {
            Parallel.For(0, _stepTiles.Count, k => TakeCarry(_stepTiles[k]));
        }

        float sea = ZoneSystem.instance.m_waterLevel;
        float maxDepth = FlowingWaterPlugin.Settings.MaxDepth.Value;
        Parallel.For(0, _stepTiles.Count, k => Apply(_stepTiles[k], dt, sea, maxDepth));
        if (erosion)
        {
            float wear = FlowingWaterPlugin.Settings.ErosionSpeed.Value;
            Parallel.For(0, _stepTiles.Count, k => Erode(_stepTiles[k], dt, wear, sea));
        }

        foreach (Marker marker in _markers)
        {
            if (marker.Drain && _tiles.TryGetValue(TileData.Key(marker.Zone), out ServerTile tile))
            {
                Drain(tile, marker, dt);
            }
        }

        foreach (ServerTile tile in _stepTiles)
        {
            tile.EmptyFor = tile.Water <= 0f && !tile.HasSource ? tile.EmptyFor + dt : 0f;
            tile.HasSource = false;
        }

        foreach (Vector2s zone in _wanted)
        {
            GetOrCreate(zone);
        }

        _wanted.Clear();
    }

    // The ground of the zones beside every tile, read now: a step reads it from several threads,
    // and reading it the first time asks the game's heightmap builder, which only the main thread may.
    private void LoadNeighbourGround()
    {
        foreach (ServerTile tile in _stepTiles)
        {
            Vector2s zone = tile.Data.Zone;
            for (int dz = -1; dz <= 1; dz++)
            {
                for (int dx = -1; dx <= 1; dx++)
                {
                    _terrain.Heights(new Vector2s(zone.x + dx, zone.y + dz), out _);
                }
            }
        }
    }

    // A source's water over the cells about a metre around it in the tile: one cell alone rises
    // in a spike each step and sends rings out.
    private void Emit(ServerTile tile, int cx, int cz, float volume)
    {
        TileData data = tile.Data;
        int reach = Mathf.Max(1, Mathf.RoundToInt(SourceReach / CellSize));
        int count = 0;
        for (int z = Mathf.Max(0, cz - reach); z <= Mathf.Min(data.Size - 1, cz + reach); z++)
        {
            for (int x = Mathf.Max(0, cx - reach); x <= Mathf.Min(data.Size - 1, cx + reach); x++)
            {
                count++;
            }
        }

        float each = volume / (count * CellArea);
        for (int z = Mathf.Max(0, cz - reach); z <= Mathf.Min(data.Size - 1, cz + reach); z++)
        {
            for (int x = Mathf.Max(0, cx - reach); x <= Mathf.Min(data.Size - 1, cx + reach); x++)
            {
                data.Depth[data.Index(x, z)] += each;
            }
        }
    }

    // The pipe model: each cell has a pipe to each neighbour whose flow keeps its momentum. The
    // level difference accelerates it (gravity, over the pipe's length), friction slows it, it
    // never exceeds half the difference in a step (no overshoot, so no endless sloshing), and a
    // flow pushed uphill dies quickly. A cell never gives more than it holds. A pipe is as wide as
    // a cell and as long as the way to the neighbour, so its pull does not depend on the cell
    // size; what a flow does to the depth does (the cell's area).
    private void UpdateFlux(ServerTile tile, FlowStep flow)
    {
        float dt = flow.Dt;
        float area = CellArea;
        float cell = CellSize;
        TileData data = tile.Data;
        int size = data.Size;
        float[] height = data.Height;
        float[] depths = data.Depth;
        float[][] flux = tile.Flux;
        bool[] flowing = tile.Flowing;
        bool[] active = tile.Active;
        float[] drops = new float[Directions.Count];
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (z * size) + x;
                float depth = depths[i];
                if (depth <= MinDepth)
                {
                    // Most of a tile is dry: nothing to do there but stop what flowed out last step.
                    if (flowing[i])
                    {
                        flowing[i] = false;
                        for (int dir = 0; dir < Directions.Count; dir++)
                        {
                            flux[dir][i] = 0f;
                        }
                    }

                    continue;
                }

                // Inside the tile a neighbour is a fixed step away in the arrays; at its edge it
                // may be in the next zone.
                bool inner = x > 0 && z > 0 && x < size - 1 && z < size - 1;
                float surface = height[i] + depth;

                float total = 0f;
                for (int dir = 0; dir < Directions.Count; dir++)
                {
                    int j = i + _offsets[dir];
                    float ground;
                    float level;
                    if (inner)
                    {
                        ground = height[j];
                        level = ground + depths[j];
                    }
                    else
                    {
                        level = NeighbourLevel(tile, x + Directions.Dx[dir], z + Directions.Dz[dir], out ground);
                    }

                    // Gravity pushes the water that stands above the higher of the two beds, as
                    // deep as it is: a film moves like a film, not like a metre of water.
                    float drop = surface - level;
                    float over = Mathf.Clamp(surface - Mathf.Max(height[i], ground), 0f, MaxPushDepth);
                    float f = (flux[dir][i] * flow.Keep) + (dt * Gravity * over * drop / Directions.Length[dir]);
                    drops[dir] = drop;
                    f = drop > 0f ? f : Mathf.Max(0f, f) * UphillKeep;
                    flux[dir][i] = f;
                    total += f;
                }

                // The bed's drag (Manning): g n² / h^4/3 per metre per second of the cell's speed, so
                // a thin film is held back far more than a deep stream. Taken at the end of the step,
                // which keeps it stable however strong, and on all the cell's outflow together.
                float dragDepth = Mathf.Max(depth, MinDragDepth);
                float slow = 1f / (1f + (dt * flow.Drag / Mathf.Pow(dragDepth, 4f / 3f) * total / (cell * dragDepth)));
                total = 0f;
                for (int dir = 0; dir < Directions.Count; dir++)
                {
                    float f = flux[dir][i] * slow;
                    if (drops[dir] > 0f)
                    {
                        f = Mathf.Min(f, Overshoot * drops[dir] * area / dt);
                    }

                    flux[dir][i] = f;
                    total += f;
                }

                if (total * dt > depth * area)
                {
                    float scale = depth * area / (total * dt);
                    for (int dir = 0; dir < Directions.Count; dir++)
                    {
                        flux[dir][i] *= scale;
                    }
                }

                // Where water goes, the cell must be moved next (it may be dry so far).
                flowing[i] = true;
                for (int dir = 0; dir < Directions.Count; dir++)
                {
                    if (flux[dir][i] <= 0f)
                    {
                        continue;
                    }

                    if (inner)
                    {
                        active[i + _offsets[dir]] = true;
                    }
                    else if (Neighbour(tile, x + Directions.Dx[dir], z + Directions.Dz[dir], out ServerTile other, out int index, out Vector2s zone))
                    {
                        other.Active[index] = true;
                    }
                    else
                    {
                        // No tile there yet: the water waits a step for it to be made (or for good,
                        // at the MaxTiles limit).
                        flux[dir][i] = 0f;
                        lock (_wanted)
                        {
                            _wanted.Add(zone);
                        }
                    }
                }
            }
        }
    }

    // A neighbour cell's water surface and ground; the ground alone where its zone has no tile.
    private float NeighbourLevel(ServerTile tile, int x, int z, out float ground)
    {
        if (Neighbour(tile, x, z, out ServerTile other, out int index, out Vector2s zone))
        {
            ground = other.Data.Height[index];
            return ground + other.Data.Depth[index];
        }

        ground = _terrain.HeightAt(zone, (index % tile.Data.Size) * CellSize, (index / tile.Data.Size) * CellSize);
        return ground;
    }

    // The tile, zone and cell index of a neighbour cell, which may lie in the next zone; false when
    // that zone has no tile.
    private bool Neighbour(ServerTile tile, int x, int z, out ServerTile other, out int index, out Vector2s zone)
    {
        int size = tile.Data.Size;
        if (x >= 0 && x < size && z >= 0 && z < size)
        {
            other = tile;
            index = tile.Data.Index(x, z);
            zone = tile.Data.Zone;
            return true;
        }

        Shift(tile.Data.Zone, size, ref x, ref z, out zone);
        index = (z * size) + x;
        return _tiles.TryGetValue(TileData.Key(zone), out other);
    }

    private static void Shift(Vector2s zone, int size, ref int x, ref int z, out Vector2s shifted)
    {
        int zx = zone.x;
        int zy = zone.y;
        if (x < 0)
        {
            zx--;
            x += size;
        }
        else if (x >= size)
        {
            zx++;
            x -= size;
        }

        if (z < 0)
        {
            zy--;
            z += size;
        }
        else if (z >= size)
        {
            zy++;
            z -= size;
        }

        shifted = new Vector2s(zx, zy);
    }

    // Moves the water the pipes carry: out of each cell by its own fluxes, into it by its
    // neighbours' fluxes towards it; then the sea takes what reaches it.
    // The cell's velocity is its net flow. A dry cell nothing flows into is left alone.
    private void Apply(ServerTile tile, float dt, float sea, float maxDepth)
    {
        TileData data = tile.Data;
        int size = data.Size;
        float[] depths = data.Depth;
        float[][] flux = tile.Flux;
        bool[] active = tile.Active;
        float area = CellArea;
        float cell = CellSize;
        float total = 0f;
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (z * size) + x;
                float before = depths[i];
                if (before <= 0f && !active[i])
                {
                    continue;
                }

                active[i] = false;
                bool inner = x > 0 && z > 0 && x < size - 1 && z < size - 1;
                float change = 0f;
                float netX = 0f;
                float netZ = 0f;
                for (int dir = 0; dir < Directions.Count; dir++)
                {
                    float outflow = flux[dir][i];
                    float inflow = Inflow(tile, x, z, i, inner, dir, out _, out _);
                    change += inflow - outflow;
                    netX += (outflow - inflow) * Directions.UnitX[dir];
                    netZ += (outflow - inflow) * Directions.UnitZ[dir];
                }

                float depth = before + (change * dt / area);
                if (data.Height[i] < sea || depth < MinDepth)
                {
                    depth = 0f;
                }
                else if (depth > maxDepth)
                {
                    depth = maxDepth;
                }

                if (Mathf.Abs(depth - before) > MinDepth)
                {
                    tile.Dirty = true;
                }

                depths[i] = depth;
                total += depth;
                if (depth <= 0f)
                {
                    // Dry cells are skipped from now on: they must not keep a current.
                    data.VelX[i] = 0f;
                    data.VelZ[i] = 0f;
                    continue;
                }

                // The flow through a cell's width over its depth: metres per second.
                float column = Mathf.Max(depth, 0.05f) * cell;
                data.VelX[i] = Mathf.Lerp(data.VelX[i], netX * 0.5f / column, VelocitySmoothing);
                data.VelZ[i] = Mathf.Lerp(data.VelZ[i], netZ * 0.5f / column, VelocitySmoothing);
            }
        }

        tile.Water = total * area;
    }

    // The water flowing into a cell this step from its neighbour in a direction (cubic metres per second),
    // and that neighbour's tile and cell; none where it has no tile.
    private float Inflow(ServerTile tile, int x, int z, int i, bool inner, int dir, out ServerTile from, out int index)
    {
        if (inner)
        {
            from = tile;
            index = i + _offsets[dir];
        }
        else if (!Neighbour(tile, x + Directions.Dx[dir], z + Directions.Dz[dir], out from, out index, out _))
        {
            return 0f;
        }

        return from.Flux[Directions.Opposite[dir]][index];
    }

    // What each cell's water carries per metre of it, before the water moves: its outflow takes that along.
    private static void TakeCarry(ServerTile tile)
    {
        for (int i = 0; i < tile.Sediment.Length; i++)
        {
            tile.Carry[i] = ErosionRules.Concentration(tile.Sediment[i], tile.Data.Depth[i]);
        }
    }

    // Erosion, once the water has moved: the ground the water carries goes with it (out with each cell's
    // outflow, in with its neighbours' inflow, at what each carried), then each cell's water picks ground up
    // or sets it down (ErosionRules); where the water is gone it sets it all down, and the sea takes what
    // reaches it. Only this tile's cells are written: tiles erode in parallel.
    private void Erode(ServerTile tile, float dt, float wear, float sea)
    {
        TileData data = tile.Data;
        int size = data.Size;
        float[] sediment = tile.Sediment;
        float area = CellArea;
        for (int z = 0; z < size; z++)
        {
            for (int x = 0; x < size; x++)
            {
                int i = (z * size) + x;
                float depth = data.Depth[i];
                if (depth <= 0f && sediment[i] <= 0f)
                {
                    continue;
                }

                bool inner = x > 0 && z > 0 && x < size - 1 && z < size - 1;
                float moved = 0f;
                for (int dir = 0; dir < Directions.Count; dir++)
                {
                    float inflow = Inflow(tile, x, z, i, inner, dir, out ServerTile from, out int index);
                    float brought = inflow > 0f ? inflow * from.Carry[index] : 0f;
                    moved += brought - (tile.Flux[dir][i] * tile.Carry[i]);
                }

                sediment[i] = Mathf.Max(0f, sediment[i] + (moved * dt / area));
                if (data.Height[i] < sea)
                {
                    sediment[i] = 0f;
                    continue;
                }

                if (tile.Kept[i])
                {
                    continue;
                }

                float speed = Mathf.Sqrt((data.VelX[i] * data.VelX[i]) + (data.VelZ[i] * data.VelZ[i]));
                float taken = ErosionRules.Exchange(sediment[i], ErosionRules.Capacity(speed, depth), tile.GroundChange[i], depth > 0f ? dt * wear : AllDown);
                sediment[i] += taken;
                data.Height[i] -= taken;
                tile.GroundChange[i] -= taken;
                tile.GroundDirty |= taken != 0f;
            }
        }
    }

    // Every zone's FlowingWater_Ground object, where erosion's change to its ground is kept.
    private void LoadGrounds()
    {
        int index = 0;
        _zdos.Clear();
        while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(Prefabs.GroundName, _zdos, ref index))
        {
        }

        foreach (ZDO zdo in _zdos)
        {
            _grounds[TileData.Key(ZoneSystem.GetZone(zdo.GetPosition()))] = zdo;
        }

        FlowingWaterPlugin.Log.LogInfo($"Water: {_grounds.Count} zones with ground worn by erosion");
    }

    // A tile's ground as erosion left it in the world (none yet: as the terrain is), and the cells it must
    // leave alone. The heights are read again with it.
    private void LoadGround(ServerTile tile)
    {
        if (FlowingWaterPlugin.Settings.Erosion.Value)
        {
            RefreshKept(tile);
        }

        if (!_grounds.TryGetValue(TileData.Key(tile.Data.Zone), out ZDO zdo) || zdo.GetByteArray(GroundHash) is not { } data)
        {
            return;
        }

        try
        {
            float[] change = ErosionGround.ToCells(ErosionGround.Decode(data), tile.Data.Size);
            Array.Copy(change, tile.GroundChange, change.Length);
            tile.TerrainVersion = -1;
        }
        catch (InvalidDataException e)
        {
            FlowingWaterPlugin.Log.LogWarning($"Zone {tile.Data.Zone}: erosion's ground change unreadable, erosion starts afresh there ({e.Message})");
        }
    }

    // Erosion's change to a tile's ground, if it changed, into the world: its FlowingWater_Ground object (made
    // the first time), which the game sends to the players near it and keeps with the world.
    private void WriteGround(ServerTile tile)
    {
        if (!tile.GroundDirty)
        {
            return;
        }

        long key = TileData.Key(tile.Data.Zone);
        if (!_grounds.TryGetValue(key, out ZDO zdo) || ZDOMan.instance.GetZDO(zdo.m_uid) == null)
        {
            zdo = NewZdo(tile.Data.Zone, Prefabs.GroundHash);
            _grounds[key] = zdo;
        }

        if (!zdo.IsOwner())
        {
            zdo.SetOwner(ZDOMan.GetSessionID());
        }

        zdo.Set(GroundHash, ErosionGround.Encode(ErosionGround.ToPoints(tile.GroundChange, tile.Data.Size)));
        tile.GroundDirty = false;
        tile.GroundWrittenAt = _clock;
    }

    // The cells erosion leaves alone: within 5 m of anything a player built (what the game records a creator
    // for), and within the game's own places (dungeon entrances, ruins...) and 5 m around them, in this zone
    // or the ones around it.
    private void RefreshKept(ServerTile tile)
    {
        Vector2s zone = tile.Data.Zone;
        var keep = new List<(float X, float Z, float Radius)>();
        _near.Clear();
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                var around = new Vector2s(zone.x + dx, zone.y + dz);
                ZDOMan.instance.FindSectorObjects(around, new SimulationDistance(0, 0), _near);
                if (ZoneSystem.instance.m_locationInstances.TryGetValue(around, out ZoneSystem.LocationInstance place) && place.m_location != null)
                {
                    float radius = Mathf.Max(place.m_location.m_exteriorRadius, place.m_location.m_interiorRadius) + ErosionGround.ProtectedRadius;
                    keep.Add((place.m_position.x, place.m_position.z, radius));
                }
            }
        }

        foreach (ZDO zdo in _near)
        {
            if (zdo.GetLong(ZDOVars.s_creator) != 0L)
            {
                Vector3 at = zdo.GetPosition();
                keep.Add((at.x, at.z, ErosionGround.ProtectedRadius));
            }
        }

        tile.Kept = ErosionGround.Kept(tile.Data.Size, TileData.OriginX(zone), TileData.OriginZ(zone), keep);
    }

    // A drain takes water from every cell within its radius, in this zone or the next: all of it
    // with no rate, else at most its rate, from each cell in proportion to its depth.
    private void Drain(ServerTile tile, Marker marker, float dt)
    {
        float cell = CellSize;
        int reach = Mathf.FloorToInt(marker.Radius / cell);
        float reachSquared = (marker.Radius / cell) * (marker.Radius / cell);
        float there = 0f;
        for (int pass = 0; pass < 2; pass++)
        {
            float share = pass == 0 || there <= 0f ? 0f : marker.Rate <= 0f ? 1f : Mathf.Min(1f, marker.Rate * dt / (there * CellArea));
            if (pass == 1 && share <= 0f)
            {
                return;
            }

            for (int dz = -reach; dz <= reach; dz++)
            {
                for (int dx = -reach; dx <= reach; dx++)
                {
                    if ((dx * dx) + (dz * dz) > reachSquared && (dx != 0 || dz != 0))
                    {
                        continue;
                    }

                    if (!Neighbour(tile, marker.X + dx, marker.Z + dz, out ServerTile other, out int index, out _))
                    {
                        continue;
                    }

                    float depth = other.Data.Depth[index];
                    if (pass == 0)
                    {
                        there += depth;
                        continue;
                    }

                    float taken = depth * share;
                    other.Data.Depth[index] = depth - taken;
                    other.Water -= taken * CellArea;
                    other.Dirty = true;
                }
            }
        }
    }

    private ServerTile GetOrCreate(Vector2s zone)
    {
        long key = TileData.Key(zone);
        if (_tiles.TryGetValue(key, out ServerTile tile))
        {
            return tile;
        }

        if (_tiles.Count >= FlowingWaterPlugin.Settings.MaxTiles.Value)
        {
            if (!_capWarned)
            {
                _capWarned = true;
                FlowingWaterPlugin.Log.LogWarning($"Water: {_tiles.Count} tiles, the MaxTiles limit; water stops spreading into new zones");
            }

            return null;
        }

        tile = new ServerTile(zone, NewZdo(zone, Prefabs.TileHash), _size);
        LoadGround(tile);
        SyncTerrain(tile);
        _tiles[key] = tile;
        FlowingWaterPlugin.Log.LogInfo($"Water reached zone {zone} ({_tiles.Count} tiles)");
        return tile;
    }

    /// <summary>Writes erosion's change to every zone's ground that changed since it was last written.</summary>
    public void WriteGrounds()
    {
        foreach (ServerTile tile in _tiles.Values)
        {
            WriteGround(tile);
        }
    }

    private void Publish()
    {
        _stepTiles.Clear();
        _stepTiles.AddRange(_tiles.Values);
        foreach (ServerTile tile in _stepTiles)
        {
            if (tile.EmptyFor > FlowingWaterPlugin.Settings.EmptyTileSeconds.Value)
            {
                FlowingWaterPlugin.Log.LogInfo($"Zone {tile.Data.Zone} dry: water tile removed");
                WriteGround(tile);

                _tiles.Remove(TileData.Key(tile.Data.Zone));
                if (ZDOMan.instance.GetZDO(tile.Zdo.m_uid) != null)
                {
                    DestroyZdo(tile.Zdo);
                }

                continue;
            }

            if (_clock - tile.GroundWrittenAt >= GroundWriteSeconds)
            {
                WriteGround(tile);
            }

            if (!tile.Dirty)
            {
                continue;
            }

            tile.Dirty = false;
            if (ZDOMan.instance.GetZDO(tile.Zdo.m_uid) == null)
            {
                FlowingWaterPlugin.Log.LogWarning($"Zone {tile.Data.Zone}: water tile object gone, making it again");
                tile.Zdo = NewZdo(tile.Data.Zone, Prefabs.TileHash);
            }

            if (!tile.Zdo.IsOwner())
            {
                tile.Zdo.SetOwner(ZDOMan.GetSessionID());
            }

            tile.Zdo.Set(DataHash, tile.Data.Encode());
        }
    }

    /// <summary>What every pipe of one step uses.</summary>
    private readonly struct FlowStep
    {
        public FlowStep(float dt)
        {
            Dt = dt;
            Keep = Mathf.Max(0f, 1f - (FlowingWaterPlugin.Settings.Friction.Value * dt));
            float n = FlowingWaterPlugin.Settings.BedRoughness.Value;
            Drag = Gravity * n * n;
        }

        public float Dt { get; }

        /// <summary>What a flow keeps of its momentum over the step, besides the bed's drag.</summary>
        public float Keep { get; }

        /// <summary>g n²: the bed's drag before dividing by the depth to the 4/3.</summary>
        public float Drag { get; }
    }

    private sealed class Marker
    {
        public Marker(Vector2s zone, int x, int z, float rate, bool drain, float radius)
        {
            Zone = zone;
            X = x;
            Z = z;
            Rate = rate;
            Drain = drain;
            Radius = radius;
        }

        /// <summary>A drain's reach, metres.</summary>
        public float Radius { get; }

        public Vector2s Zone { get; }

        public int X { get; }

        public int Z { get; }

        public float Rate { get; }

        public bool Drain { get; }
    }
}
