using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
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

    private const float MinDepth = 0.0005f;

    private const float TerrainRefreshSeconds = 2f;

    private const int MaxStepsPerFrame = 4;

    private readonly ZoneTerrain _terrain = new();

    private readonly Dictionary<long, ServerTile> _tiles = [];

    private readonly List<ServerTile> _stepTiles = [];

    private readonly List<Marker> _markers = [];

    private readonly List<ZDO> _zdos = [];

    private float _stepTime;

    private float _scanTimer = float.MaxValue;

    private float _sendTimer;

    private float _terrainTimer;

    private float _debugTimer;

    private bool _loaded;

    private bool _capWarned;

    public int TileCount => _tiles.Count;

    public void Update(float dt)
    {
        if (!_loaded)
        {
            Load();
            _loaded = true;
        }

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

        foreach (ServerTile tile in _tiles.Values)
        {
            SyncTerrain(tile);
        }

        float step = 1f / FlowingWaterPlugin.Settings.StepsPerSecond.Value;
        _stepTime += dt;
        int steps = 0;
        while (_stepTime >= step && steps < MaxStepsPerFrame)
        {
            Step(step);
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

    // One character per 2 x 2 cells: '.' dry, then deeper and deeper; '>' '<' '^' 'v' where it runs.
    private void LogMaps()
    {
        var text = new StringBuilder();
        text.Append(FormattableString.Invariant($"Water: {_tiles.Count} tiles, {_markers.Count} markers"));
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
            for (int z = TileData.Size - 2; z >= 0; z -= 2)
            {
                text.Append('\n');
                for (int x = 0; x < TileData.Size; x += 2)
                {
                    text.Append(Glyph(data, x, z));
                }
            }
        }

        FlowingWaterPlugin.Log.LogInfo(text.ToString());
    }

    private static char Glyph(TileData data, int x, int z)
    {
        float depth = 0f;
        float vx = 0f;
        float vz = 0f;
        for (int dz = 0; dz < 2; dz++)
        {
            for (int dx = 0; dx < 2; dx++)
            {
                int i = TileData.Index(x + dx, z + dz);
                depth = Mathf.Max(depth, data.Depth[i]);
                vx += data.VelX[i];
                vz += data.VelZ[i];
            }
        }

        if (depth <= 0.01f)
        {
            return '.';
        }

        if (Mathf.Abs(vx) + Mathf.Abs(vz) > 0.6f)
        {
            return Mathf.Abs(vx) > Mathf.Abs(vz) ? (vx > 0f ? '>' : '<') : (vz > 0f ? '^' : 'v');
        }

        return depth < 0.1f ? ':' : depth < 0.5f ? 'o' : depth < 2f ? 'O' : '#';
    }

    private static void DestroyZdo(ZDO zdo)
    {
        zdo.SetOwner(ZDOMan.GetSessionID());
        ZDOMan.instance.DestroyZDO(zdo);
    }

    private void Load()
    {
        int index = 0;
        _zdos.Clear();
        while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(Prefabs.TileName, _zdos, ref index))
        {
        }

        int unreadable = 0;
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

            var tile = new ServerTile(zone, zdo);
            SyncTerrain(tile);
            byte[] data = zdo.GetByteArray(DataHash);
            if (data != null && !tile.Data.Decode(data))
            {
                unreadable++;
            }

            tile.Water = Sum(tile.Data.Depth);
            _tiles[key] = tile;
        }

        FlowingWaterPlugin.Log.LogInfo($"Water: {_tiles.Count} tiles loaded" + (unreadable > 0 ? $", {unreadable} unreadable (now empty)" : string.Empty));
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

        foreach (ZDO zdo in _zdos)
        {
            Vector2s zone = TileData.CellOf(zdo.GetPosition(), out int x, out int z);
            _markers.Add(new Marker(zone, TileData.Index(x, z), zdo.GetFloat(RateHash, defaultRate), drain));
        }
    }

    // "x,z,rate; x,z,rate" from the config.
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
            if (parts.Length != 3
                || !float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out float x)
                || !float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float z)
                || !float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float rate))
            {
                FlowingWaterPlugin.Log.LogWarning($"Fixed {(drain ? "drain" : "source")} \"{trimmed}\" is not x,z,rate; ignored");
                continue;
            }

            Vector2s zone = TileData.CellOf(new Vector3(x, 0f, z), out int cx, out int cz);
            _markers.Add(new Marker(zone, TileData.Index(cx, cz), rate, drain));
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
        for (int z = 0; z < TileData.Size; z++)
        {
            for (int x = 0; x < TileData.Size; x++)
            {
                tile.Data.Height[TileData.Index(x, z)] = heights[(z * TileData.VertexPitch) + x];
            }
        }
    }

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

            tile.Data.Depth[marker.Cell] += marker.Rate * dt;
            tile.HasSource = true;
            tile.Dirty = true;
        }

        _stepTiles.Clear();
        _stepTiles.AddRange(_tiles.Values);
        foreach (ServerTile tile in _stepTiles)
        {
            Array.Clear(tile.Delta, 0, tile.Delta.Length);
            Array.Clear(tile.FluxX, 0, tile.FluxX.Length);
            Array.Clear(tile.FluxZ, 0, tile.FluxZ.Length);
        }

        foreach (ServerTile tile in _stepTiles)
        {
            Flow(tile);
        }

        float sea = ZoneSystem.instance.m_waterLevel;
        foreach (ServerTile tile in _tiles.Values)
        {
            Apply(tile, dt, sea);
        }
    }

    private void Flow(ServerTile tile)
    {
        float rate = FlowingWaterPlugin.Settings.FlowRate.Value;
        TileData data = tile.Data;
        for (int z = 0; z < TileData.Size; z++)
        {
            for (int x = 0; x < TileData.Size; x++)
            {
                int i = TileData.Index(x, z);
                float depth = data.Depth[i];
                if (depth <= MinDepth)
                {
                    continue;
                }

                float surface = data.Height[i] + depth;
                float east = Want(surface, tile, x + 1, z);
                float west = Want(surface, tile, x - 1, z);
                float north = Want(surface, tile, x, z + 1);
                float south = Want(surface, tile, x, z - 1);
                float total = east + west + north + south;
                if (total <= 0f)
                {
                    continue;
                }

                float scale = rate * Mathf.Min(1f, depth / total);
                Move(tile, i, x + 1, z, east * scale, 1, 0);
                Move(tile, i, x - 1, z, west * scale, -1, 0);
                Move(tile, i, x, z + 1, north * scale, 0, 1);
                Move(tile, i, x, z - 1, south * scale, 0, -1);
            }
        }
    }

    // Half the level difference down to a lower neighbour (0 to a higher one): the amount that
    // would level the two, before the step's rate and the cell's own water limit it.
    private float Want(float surface, ServerTile tile, int x, int z)
    {
        float diff = surface - NeighbourSurface(tile, x, z);
        return diff > 0f ? diff * 0.5f : 0f;
    }

    private void Move(ServerTile from, int fromIndex, int x, int z, float amount, int dx, int dz)
    {
        if (amount <= 0f)
        {
            return;
        }

        if (!Neighbour(from, x, z, true, out ServerTile to, out int toIndex))
        {
            return;
        }

        from.Delta[fromIndex] -= amount;
        to.Delta[toIndex] += amount;
        from.FluxX[fromIndex] += amount * dx;
        from.FluxZ[fromIndex] += amount * dz;
    }

    private float NeighbourSurface(ServerTile tile, int x, int z)
    {
        if (Neighbour(tile, x, z, false, out ServerTile other, out int index))
        {
            return other.Data.Height[index] + other.Data.Depth[index];
        }

        Shift(tile.Data.Zone, ref x, ref z, out Vector2s zone);
        return _terrain.CellHeight(zone, x, z);
    }

    // The tile and cell index of a neighbour cell, which may lie in the next zone. A missing
    // neighbour tile is created when asked and allowed; otherwise false.
    private bool Neighbour(ServerTile tile, int x, int z, bool create, out ServerTile other, out int index)
    {
        if (x >= 0 && x < TileData.Size && z >= 0 && z < TileData.Size)
        {
            other = tile;
            index = TileData.Index(x, z);
            return true;
        }

        Shift(tile.Data.Zone, ref x, ref z, out Vector2s zone);
        index = TileData.Index(x, z);
        if (_tiles.TryGetValue(TileData.Key(zone), out other))
        {
            return true;
        }

        if (create)
        {
            other = GetOrCreate(zone);
            return other != null;
        }

        return false;
    }

    private static void Shift(Vector2s zone, ref int x, ref int z, out Vector2s shifted)
    {
        int zx = zone.x;
        int zy = zone.y;
        if (x < 0)
        {
            zx--;
            x += TileData.Size;
        }
        else if (x >= TileData.Size)
        {
            zx++;
            x -= TileData.Size;
        }

        if (z < 0)
        {
            zy--;
            z += TileData.Size;
        }
        else if (z >= TileData.Size)
        {
            zy++;
            z -= TileData.Size;
        }

        shifted = new Vector2s(zx, zy);
    }

    private void Apply(ServerTile tile, float dt, float sea)
    {
        TileData data = tile.Data;
        float maxDepth = FlowingWaterPlugin.Settings.MaxDepth.Value;
        float total = 0f;
        for (int i = 0; i < TileData.Cells; i++)
        {
            float before = data.Depth[i];
            float depth = before + tile.Delta[i];
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

            data.Depth[i] = depth;
            total += depth;
            float column = dt * Mathf.Max(before, 0.05f);
            float vx = before > 0f ? tile.FluxX[i] / column : 0f;
            float vz = before > 0f ? tile.FluxZ[i] / column : 0f;
            data.VelX[i] = Mathf.Lerp(data.VelX[i], vx, 0.25f);
            data.VelZ[i] = Mathf.Lerp(data.VelZ[i], vz, 0.25f);
        }

        foreach (Marker marker in _markers)
        {
            if (!marker.Drain || TileData.Key(marker.Zone) != TileData.Key(data.Zone))
            {
                continue;
            }

            float depth = data.Depth[marker.Cell];
            float left = marker.Rate <= 0f ? 0f : Mathf.Max(0f, depth - (marker.Rate * dt));
            if (left != depth)
            {
                total -= depth - left;
                data.Depth[marker.Cell] = left;
                tile.Dirty = true;
            }
        }

        tile.Water = total;
        tile.EmptyFor = total <= 0f && !tile.HasSource ? tile.EmptyFor + dt : 0f;
        tile.HasSource = false;
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

        ZDO zdo = ZDOMan.instance.CreateNewZDO(ZoneSystem.GetZonePos(zone), Prefabs.TileHash);
        zdo.Persistent = true;
        zdo.SetType(ZDO.ObjectType.Default);
        zdo.SetDistant(false);
        tile = new ServerTile(zone, zdo);
        SyncTerrain(tile);
        _tiles[key] = tile;
        FlowingWaterPlugin.Log.LogInfo($"Water reached zone {zone} ({_tiles.Count} tiles)");
        return tile;
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
                _tiles.Remove(TileData.Key(tile.Data.Zone));
                if (ZDOMan.instance.GetZDO(tile.Zdo.m_uid) != null)
                {
                    DestroyZdo(tile.Zdo);
                }

                continue;
            }

            if (!tile.Dirty)
            {
                continue;
            }

            tile.Dirty = false;
            if (ZDOMan.instance.GetZDO(tile.Zdo.m_uid) == null)
            {
                FlowingWaterPlugin.Log.LogWarning($"Zone {tile.Data.Zone}: water tile object gone, making it again");
                tile.Zdo = ZDOMan.instance.CreateNewZDO(ZoneSystem.GetZonePos(tile.Data.Zone), Prefabs.TileHash);
                tile.Zdo.Persistent = true;
                tile.Zdo.SetType(ZDO.ObjectType.Default);
                tile.Zdo.SetDistant(false);
            }

            if (!tile.Zdo.IsOwner())
            {
                tile.Zdo.SetOwner(ZDOMan.GetSessionID());
            }

            tile.Zdo.Set(DataHash, tile.Data.Encode());
        }
    }

    private sealed class Marker
    {
        public Marker(Vector2s zone, int cell, float rate, bool drain)
        {
            Zone = zone;
            Cell = cell;
            Rate = rate;
            Drain = drain;
        }

        public Vector2s Zone { get; }

        public int Cell { get; }

        public float Rate { get; }

        public bool Drain { get; }
    }
}
