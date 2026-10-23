using System;
using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// Ground heights per zone for the server, anywhere in the world and with no zone loaded: the
/// world generator's heights as the game's heightmap builder makes them, plus the players' terrain
/// changes read from the zone's terrain-changes object (the same deltas and limits the game applies). The generated
/// heights are made once a zone, the changes applied again on them when they move on (<see cref="ZoneHeights{TZone, TStamp}"/>).
/// </summary>
internal sealed class ZoneTerrain : IZoneGround<Vector2s, (ZDOID Id, uint Revision)>
{
    // The game's heightmap: a vertex every metre, one more than the metres along a side.
    private const int Pitch = TileData.ZoneMetres + 1;

    private static readonly int TerrainCompilerHash = "_TerrainCompiler".GetStableHashCode();

    private static readonly int ChangesHash = "TCData".GetStableHashCode();

    private readonly ZoneHeights<Vector2s, (ZDOID Id, uint Revision)> _heights;

    private readonly List<ZDO> _found = [];

    // Where the frame's time goes: asking the game for a zone's heights is a part of its own.
    private readonly FrameParts _parts;

    /// <summary>The heights of no zone yet, asking the game for them timed in these parts.</summary>
    public ZoneTerrain(FrameParts parts)
    {
        _parts = parts;
        _heights = new ZoneHeights<Vector2s, (ZDOID Id, uint Revision)>(this, Pitch * Pitch);
    }

    /// <summary>A zone's vertex heights, 65 by 65 (index z * 65 + x).</summary>
    public float[] Heights(Vector2s zone) => _heights.Heights(zone);

    /// <summary>
    /// The ground at a point of the zone, metres east and north of its south-west corner (0 to 64),
    /// between vertices as the game itself reads its heightmap: bilinear.
    /// </summary>
    public float HeightAt(Vector2s zone, float x, float z)
    {
        return Sample(Heights(zone), x, z);
    }

    /// <summary>Like <see cref="HeightAt"/> on heights already fetched with <see cref="Heights"/>.</summary>
    public static float Sample(float[] heights, float x, float z)
    {
        int x0 = Mathf.Clamp(Mathf.FloorToInt(x), 0, Pitch - 2);
        int z0 = Mathf.Clamp(Mathf.FloorToInt(z), 0, Pitch - 2);
        float fx = Mathf.Clamp01(x - x0);
        float fz = Mathf.Clamp01(z - z0);
        int i = (z0 * Pitch) + x0;
        float south = Mathf.Lerp(heights[i], heights[i + 1], fx);
        float north = Mathf.Lerp(heights[i + Pitch], heights[i + Pitch + 1], fx);
        return Mathf.Lerp(south, north, fz);
    }

    /// <summary>
    /// Whether the zone's heights can be read now without waiting on the game's heightmap builder; if not, they are
    /// asked of it, to be ready at a later call.
    /// </summary>
    public bool Ready(Vector2s zone) => _heights.Ready(zone);

    /// <summary>Forgets the zones not among these (by <see cref="TileData.Key"/>): they are made again when read.</summary>
    public void Keep(HashSet<long> zones) => _heights.Keep(zones);

    /// <summary>Rebuilds the zones whose terrain changes moved on since they were read, and tells of each.</summary>
    /// <param name="changed">A zone's heights changed: what was read of them before is out of date.</param>
    public void Refresh(Action<Vector2s> changed) => _heights.Refresh(changed);

    long IZoneGround<Vector2s, (ZDOID Id, uint Revision)>.Key(Vector2s zone) => TileData.Key(zone);

    // The heightmap builder waits for its thread, which looks for work every 10 ms: asked once a zone, and once ready
    // (CanGenerate) where it can wait.
    float[] IZoneGround<Vector2s, (ZDOID Id, uint Revision)>.Generated(Vector2s zone)
    {
        WaterPart was = _parts.Into(WaterPart.Heightmap);
        Vector3 center = ZoneSystem.GetZonePos(zone);
        HeightmapBuilder.HMBuildData data = HeightmapBuilder.instance.RequestTerrainSync(center, TileData.ZoneMetres, 1f, false, WorldGenerator.instance);
        float[] heights = [.. data.m_baseHeights];
        _parts.Into(was);
        return heights;
    }

    bool IZoneGround<Vector2s, (ZDOID Id, uint Revision)>.CanGenerate(Vector2s zone) =>
        HeightmapBuilder.instance.IsTerrainReady(ZoneSystem.GetZonePos(zone), TileData.ZoneMetres, 1f, false, WorldGenerator.instance);

    ((ZDOID Id, uint Revision) Stamp, byte[] Data) IZoneGround<Vector2s, (ZDOID Id, uint Revision)>.Changes(Vector2s zone)
    {
        ZDO changes = FindChanges(zone);
        return changes == null ? ((ZDOID.None, 0u), null) : ((changes.m_uid, changes.DataRevision), changes.GetByteArray(ChangesHash));
    }

    void IZoneGround<Vector2s, (ZDOID Id, uint Revision)>.Warn(Vector2s zone, string problem) =>
        FlowingWaterPlugin.Log.LogWarning($"Zone {zone}: {problem}");

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
}
