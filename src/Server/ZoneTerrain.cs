using System;
using System.Collections.Generic;
using FlowingWater.Volume;
using HarmonyLib;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// Ground heights per zone for the server, anywhere in the world and with no zone loaded: the
/// world generator's heights as the game's heightmap builder makes them, shaped by the terrain modifiers of the
/// locations in and round the zone (a village's levelled yard: the game's own terrain makes these on each player's
/// game, never in what the server keeps, and a terrain edit is made over them), plus the players' terrain changes read
/// from the zone's terrain-changes object (the same deltas and limits the game applies). The generated heights are made
/// once a zone, the shaping and changes applied again on them when the changes move on or a zone round it is generated
/// (its locations placed) (<see cref="ZoneHeights{TZone, TStamp}"/>).
/// </summary>
internal sealed class ZoneTerrain : IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Generated)>
{
    // The game's heightmap: a vertex every metre, one more than the metres along a side.
    private const int Pitch = TileData.ZoneMetres + 1;

    private static readonly int TerrainCompilerHash = "_TerrainCompiler".GetStableHashCode();

    private static readonly int ChangesHash = "TCData".GetStableHashCode();

    private static readonly int LocationProxyHash = "LocationProxy".GetStableHashCode();

    // The game's set of generated zones (its locations placed), read from its own field; null when a game update
    // took it away (every zone is then taken as generated: its locations' shaping comes in once the changes move on).
    private static readonly AccessTools.FieldRef<ZoneSystem, HashSet<Vector2s>> GeneratedZones = FindGeneratedZones();

    private readonly ZoneHeights<Vector2s, (ZDOID Id, uint Revision, int Generated)> _heights;

    private readonly List<ZDO> _found = [];

    // Each zone's terrain-changes object as last found (by the zone's key): looked up by its id first, the zone's
    // objects searched only when there was none or it is gone.
    private readonly Dictionary<long, ZDOID> _compilers = [];

    // Where the frame's time goes: asking the game for a zone's heights is a part of its own.
    private readonly FrameParts _parts;

    // Each location's terrain modifiers (by the location's hash), where they stand in it: read from its prefab once.
    private readonly Dictionary<int, TerrainShaping.Modifier[]> _locations = [];

    // Each object prefab's own terrain modifier (by its hash), null for one without.
    private readonly Dictionary<int, TerrainModifier> _shapingPieces = [];

    // The modifiers touching the zone being shaped.
    private readonly List<TerrainShaping.Modifier> _modifiers = [];

    /// <summary>The heights of no zone yet, asking the game for them timed in these parts.</summary>
    public ZoneTerrain(FrameParts parts)
    {
        _parts = parts;
        _heights = new ZoneHeights<Vector2s, (ZDOID Id, uint Revision, int Generated)>(this, Pitch * Pitch);
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

    /// <summary>The ground at a world point (x, z), metres.</summary>
    public float HeightAt(float x, float z)
    {
        Vector2s zone = ZoneSystem.GetZone(new Vector3(x, 0f, z));
        return HeightAt(zone, x - TileData.OriginX(zone), z - TileData.OriginZ(zone));
    }

    /// <summary>Like <see cref="HeightAt(Vector2s, float, float)"/> on heights already fetched with <see cref="Heights"/>.</summary>
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
    public void Keep(HashSet<long> zones)
    {
        _heights.Keep(zones);
        ZoneCache.Keep(_compilers, zones);
    }

    /// <summary>Rebuilds the zones whose terrain changes moved on since they were read, and tells of each.</summary>
    /// <param name="changed">A zone's heights changed: what was read of them before is out of date.</param>
    public void Refresh(Action<Vector2s> changed) => _heights.Refresh(changed);

    long IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Generated)>.Key(Vector2s zone) => TileData.Key(zone);

    // The heightmap builder waits for its thread, which looks for work every 10 ms: asked once a zone, and once ready
    // (CanGenerate) where it can wait.
    float[] IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Generated)>.Generated(Vector2s zone)
    {
        WaterPart was = _parts.Into(WaterPart.Heightmap);
        Vector3 center = ZoneSystem.GetZonePos(zone);
        HeightmapBuilder.HMBuildData data = HeightmapBuilder.instance.RequestTerrainSync(center, TileData.ZoneMetres, 1f, false, WorldGenerator.instance);
        float[] heights = [.. data.m_baseHeights];
        _parts.Into(was);
        return heights;
    }

    bool IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Generated)>.CanGenerate(Vector2s zone) =>
        HeightmapBuilder.instance.IsTerrainReady(ZoneSystem.GetZonePos(zone), TileData.ZoneMetres, 1f, false, WorldGenerator.instance);

    // The stamp holds which of the zone and the eight round it are generated: a location's modifiers reach over its
    // zone's edge, and come with it once it is (a zone is generated, its locations placed, when a player first nears).
    ((ZDOID Id, uint Revision, int Generated) Stamp, byte[] Data) IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Generated)>.Changes(Vector2s zone)
    {
        int generated = 0, bit = 0;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++, bit++)
            {
                generated |= GeneratedZones == null || GeneratedZones(ZoneSystem.instance).Contains(new Vector2s(zone.x + dx, zone.y + dz)) ? 1 << bit : 0;
            }
        }

        ZDO changes = FindChanges(zone);
        return changes == null ? ((ZDOID.None, 0u, generated), null) : ((changes.m_uid, changes.DataRevision, generated), changes.GetByteArray(ChangesHash));
    }

    // The terrain modifiers of the locations in the zone and round it, and of any object that shapes the ground (an old
    // world's shaping pieces), over its generated heights.
    void IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Generated)>.Shape(Vector2s zone, float[] heights)
    {
        _modifiers.Clear();
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                Gather(new Vector2s(zone.x + dx, zone.y + dz));
            }
        }

        if (_modifiers.Count > 0)
        {
            Vector3 centre = ZoneSystem.GetZonePos(zone);
            TerrainShaping.Shape(heights, centre.x, centre.z, _modifiers);
        }
    }

    void IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Generated)>.Warn(Vector2s zone, string problem) =>
        FlowingWaterPlugin.Log.LogWarning($"Zone {zone}: {problem}");

    private static AccessTools.FieldRef<ZoneSystem, HashSet<Vector2s>> FindGeneratedZones()
    {
        if (AccessTools.Field(typeof(ZoneSystem), "m_generatedZones") is not { } field || field.FieldType != typeof(HashSet<Vector2s>))
        {
            FlowingWaterPlugin.Log.LogError("The game's ZoneSystem.m_generatedZones was not found (a game update?): a zone's locations shape the water's ground only once its terrain changes move on");
            return null;
        }

        return AccessTools.FieldRefAccess<ZoneSystem, HashSet<Vector2s>>(field);
    }

    // The terrain modifiers of a zone's locations (where each proxy stands, turned as it is) and shaping objects.
    private void Gather(Vector2s zone)
    {
        _found.Clear();
        ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), _found);
        foreach (ZDO zdo in _found)
        {
            int prefab = zdo.GetPrefab();
            if (prefab == LocationProxyHash)
            {
                Vector3 at = zdo.GetPosition();
                Quaternion turn = zdo.GetRotation();
                foreach (TerrainShaping.Modifier own in Location(zdo.GetInt(ZDOVars.s_location)))
                {
                    Vector3 place = at + (turn * new Vector3(own.X, own.Y, own.Z));
                    _modifiers.Add(Placed(own, place, 0L));
                }
            }
            else if (ShapingPiece(prefab) is { } piece)
            {
                long created = zdo.GetLong(ZDOVars.s_terrainModifierTimeCreated, 0L);
                _modifiers.Add(Placed(Of(piece, Vector3.zero), zdo.GetPosition(), created != 0L ? created : ZDOExtraData.GetTimeCreated(zdo.m_uid)));
            }
        }
    }

    // A location's terrain modifiers where they stand in it, read from its prefab (loaded for it and let go): those of
    // its parts that are on (a part a location turns on or off by chance when placed is taken as its prefab has it).
    private TerrainShaping.Modifier[] Location(int hash)
    {
        if (_locations.TryGetValue(hash, out TerrainShaping.Modifier[] known))
        {
            return known;
        }

        var made = new List<TerrainShaping.Modifier>();
        ZoneSystem.ZoneLocation location = ZoneSystem.instance.m_locations.Find(l => l.m_prefab.IsValid && l.Hash == hash);
        if (location == null)
        {
            FlowingWaterPlugin.Log.LogWarning($"Location {hash} not found: its terrain modifiers are left out of the water's ground");
        }
        else
        {
            location.m_prefab.Load();
            GameObject root = location.m_prefab.Asset;
            if (root != null)
            {
                foreach (TerrainModifier modifier in root.GetComponentsInChildren<TerrainModifier>(false))
                {
                    if (modifier.enabled)
                    {
                        made.Add(Of(modifier, root.transform.InverseTransformPoint(modifier.transform.position)));
                    }
                }
            }

            location.m_prefab.Release();
        }

        TerrainShaping.Modifier[] kept = [.. made];
        _locations[hash] = kept;
        return kept;
    }

    // An object prefab's own terrain modifier, null for one without (asked once a prefab).
    private TerrainModifier ShapingPiece(int prefab)
    {
        if (!_shapingPieces.TryGetValue(prefab, out TerrainModifier modifier))
        {
            GameObject made = ZNetScene.instance.GetPrefab(prefab);
            modifier = made != null && made.TryGetComponent(out TerrainModifier own) && own.enabled ? own : null;
            _shapingPieces[prefab] = modifier;
        }

        return modifier;
    }

    // What a game modifier does, standing at a place.
    private static TerrainShaping.Modifier Of(TerrainModifier modifier, Vector3 place) => new()
    {
        X = place.x,
        Y = place.y,
        Z = place.z,
        LevelOffset = modifier.m_levelOffset,
        Level = modifier.m_level,
        LevelRadius = modifier.m_levelRadius,
        Square = modifier.m_square,
        Smooth = modifier.m_smooth,
        SmoothRadius = modifier.m_smoothRadius,
        SmoothPower = modifier.m_smoothPower,
        Radius = modifier.GetRadius(),
        SortOrder = modifier.m_sortOrder,
        PlayerMade = modifier.m_playerModifiction,
    };

    // A modifier as read, standing at a world place, made then.
    private static TerrainShaping.Modifier Placed(TerrainShaping.Modifier own, Vector3 place, long created) => new()
    {
        X = place.x,
        Y = place.y,
        Z = place.z,
        LevelOffset = own.LevelOffset,
        Level = own.Level,
        LevelRadius = own.LevelRadius,
        Square = own.Square,
        Smooth = own.Smooth,
        SmoothRadius = own.SmoothRadius,
        SmoothPower = own.SmoothPower,
        Radius = own.Radius,
        SortOrder = own.SortOrder,
        Created = created,
        PlayerMade = own.PlayerMade,
    };

    private ZDO FindChanges(Vector2s zone)
    {
        long key = TileData.Key(zone);
        if (_compilers.TryGetValue(key, out ZDOID known) && ZDOMan.instance.GetZDO(known) is { } kept && kept.GetPrefab() == TerrainCompilerHash)
        {
            return kept;
        }

        _found.Clear();
        ZDOMan.instance.FindSectorObjects(zone, new SimulationDistance(0, 0), _found);
        foreach (ZDO zdo in _found)
        {
            if (zdo.GetPrefab() == TerrainCompilerHash)
            {
                _compilers[key] = zdo.m_uid;
                return zdo;
            }
        }

        _compilers.Remove(key);
        return null;
    }
}
