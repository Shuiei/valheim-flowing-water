using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// Ground heights per zone for the server, anywhere in the world and with no zone loaded: the
/// world generator's heights as the game's heightmap builder makes them, plus the players' terrain
/// changes read from the zone's terrain-changes object (the same deltas and limits the game applies).
/// </summary>
internal sealed class ZoneTerrain
{
    // The game clamps a vertex to this far from its generated height, whatever the tools did.
    private const float MaxChange = 8f;

    private const int Vertices = TileData.VertexPitch * TileData.VertexPitch;

    private static readonly int TerrainCompilerHash = "_TerrainCompiler".GetStableHashCode();

    private static readonly int ChangesHash = "TCData".GetStableHashCode();

    private readonly Dictionary<long, Entry> _zones = [];

    private readonly List<ZDO> _found = [];

    /// <summary>A zone's vertex heights, 65 by 65 (index z * 65 + x), and a number that changes when they do.</summary>
    public float[] Heights(Vector2s zone, out int version)
    {
        long key = TileData.Key(zone);
        if (!_zones.TryGetValue(key, out Entry entry))
        {
            entry = new Entry();
            Build(zone, entry);
            _zones[key] = entry;
        }

        version = entry.Version;
        return entry.Heights;
    }

    public float CellHeight(Vector2s zone, int x, int z)
    {
        return Heights(zone, out _)[(z * TileData.VertexPitch) + x];
    }

    /// <summary>Rebuilds the zones whose terrain changes moved on since they were read.</summary>
    public void Refresh()
    {
        foreach (KeyValuePair<long, Entry> pair in _zones)
        {
            Entry entry = pair.Value;
            ZDO changes = FindChanges(entry.Zone);
            uint revision = changes == null ? 0 : changes.DataRevision;
            ZDOID id = changes == null ? ZDOID.None : changes.m_uid;
            if (revision != entry.Revision || id != entry.Changes)
            {
                Build(entry.Zone, entry);
            }
        }
    }

    private static void ApplyChanges(ZDO changes, float[] heights, List<float> baseHeights, Vector2s zone)
    {
        byte[] bytes = changes.GetByteArray(ChangesHash);
        if (bytes == null)
        {
            return;
        }

        ZPackage pkg;
        try
        {
            pkg = new ZPackage(Utils.Decompress(bytes));
        }
        catch (Exception e) when (e is IOException or InvalidDataException or ArgumentException)
        {
            FlowingWaterPlugin.Log.LogWarning($"Zone {zone}: terrain changes unreadable ({e.Message}); using the generated ground");
            return;
        }

        pkg.ReadInt(); // format version
        pkg.ReadInt(); // operation count
        pkg.ReadVector3(); // last operation's point
        pkg.ReadSingle(); // and radius
        int count = pkg.ReadInt();
        if (count != heights.Length)
        {
            FlowingWaterPlugin.Log.LogWarning($"Zone {zone}: terrain changes cover {count} vertices, expected {heights.Length}; using the generated ground");
            return;
        }

        for (int i = 0; i < count; i++)
        {
            if (!pkg.ReadBool())
            {
                continue;
            }

            float level = pkg.ReadSingle();
            float smooth = pkg.ReadSingle();
            float baseHeight = baseHeights[i];
            heights[i] = Mathf.Clamp(baseHeight + level + smooth, baseHeight - MaxChange, baseHeight + MaxChange);
        }
    }

    private void Build(Vector2s zone, Entry entry)
    {
        entry.Zone = zone;
        Vector3 center = ZoneSystem.GetZonePos(zone);
        HeightmapBuilder.HMBuildData data = HeightmapBuilder.instance.RequestTerrainSync(center, TileData.Size, 1f, false, WorldGenerator.instance);
        List<float> baseHeights = data.m_baseHeights;
        if (baseHeights.Count != Vertices)
        {
            FlowingWaterPlugin.Log.LogError($"Zone {zone}: the heightmap builder gave {baseHeights.Count} vertices, expected {Vertices}");
            return;
        }

        for (int i = 0; i < Vertices; i++)
        {
            entry.Heights[i] = baseHeights[i];
        }

        ZDO changes = FindChanges(zone);
        entry.Changes = changes == null ? ZDOID.None : changes.m_uid;
        entry.Revision = changes == null ? 0 : changes.DataRevision;
        if (changes != null)
        {
            ApplyChanges(changes, entry.Heights, baseHeights, zone);
        }

        entry.Version++;
    }

    private ZDO FindChanges(Vector2s zone)
    {
        _found.Clear();
        ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), _found);
        foreach (ZDO zdo in _found)
        {
            if (zdo.GetPrefab() == TerrainCompilerHash)
            {
                return zdo;
            }
        }

        return null;
    }

    private sealed class Entry
    {
        public float[] Heights { get; } = new float[Vertices];

        public Vector2s Zone { get; set; }

        public int Version { get; set; }

        public uint Revision { get; set; }

        public ZDOID Changes { get; set; }
    }
}
