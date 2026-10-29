using System;
using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// Ground heights per zone for the server, anywhere in the world and with no zone loaded: the
/// world generator's heights as the game's heightmap builder makes them, shaped by the terrain modifiers of the
/// locations in and round the zone (a village's levelled yard: the game's own terrain makes these on each player's
/// game, never in what the server keeps, and a terrain edit is made over them), plus the players' terrain changes read
/// from the zone's terrain-changes object (the same deltas and limits the game applies). The generated heights are made
/// once a zone, the shaping and changes applied again on them when the changes move on or what shapes the zone or one
/// round it does (a zone generated, its locations placed; a shaping piece added, moved or taken away)
/// (<see cref="ZoneHeights{TZone, TStamp}"/>).
/// </summary>
internal sealed class ZoneTerrain : IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Shapers)>
{
    // The game's heightmap: a vertex every metre, one more than the metres along a side.
    private const int Pitch = TileData.ZoneMetres + 1;

    private static readonly int TerrainCompilerHash = "_TerrainCompiler".GetStableHashCode();

    private static readonly int ChangesHash = "TCData".GetStableHashCode();

    private static readonly int LocationProxyHash = "LocationProxy".GetStableHashCode();

    private readonly ZoneHeights<Vector2s, (ZDOID Id, uint Revision, int Shapers)> _heights;

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

    // What shapes each sector (by its key: its location proxies and shaping pieces, by id and place), as found in the
    // frame it was asked in: a zone's stamp asks it of the nine sectors round it, and its neighbours ask again.
    private readonly Dictionary<long, int> _shapers = [];

    private int _shapersFrame = -1;

    // The modifiers touching the zone being shaped.
    private readonly List<TerrainShaping.Modifier> _modifiers = [];

    /// <summary>The heights of no zone yet, asking the game for them timed in these parts.</summary>
    public ZoneTerrain(FrameParts parts)
    {
        _parts = parts;
        _heights = new ZoneHeights<Vector2s, (ZDOID Id, uint Revision, int Shapers)>(this, Pitch * Pitch);
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

    long IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Shapers)>.Key(Vector2s zone) => TileData.Key(zone);

    // The heightmap builder waits for its thread, which looks for work every 10 ms: asked once a zone, and once ready
    // (CanGenerate) where it can wait.
    float[] IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Shapers)>.Generated(Vector2s zone)
    {
        WaterPart was = _parts.Into(WaterPart.Heightmap);
        Vector3 center = ZoneSystem.GetZonePos(zone);
        HeightmapBuilder.HMBuildData data = HeightmapBuilder.instance.RequestTerrainSync(center, TileData.ZoneMetres, 1f, false, WorldGenerator.instance);
        float[] heights = [.. data.m_baseHeights];
        _parts.Into(was);
        return heights;
    }

    bool IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Shapers)>.CanGenerate(Vector2s zone) =>
        HeightmapBuilder.instance.IsTerrainReady(ZoneSystem.GetZonePos(zone), TileData.ZoneMetres, 1f, false, WorldGenerator.instance);

    // The stamp holds what shapes the zone and the eight round it (a location's modifiers reach over its zone's edge):
    // their location proxies, placed when a zone is generated (when a player first nears), and their shaping pieces,
    // added, moved or taken away.
    ((ZDOID Id, uint Revision, int Shapers) Stamp, byte[] Data) IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Shapers)>.Changes(Vector2s zone)
    {
        if (_shapersFrame != Time.frameCount)
        {
            _shapersFrame = Time.frameCount;
            _shapers.Clear();
        }

        int shapers = 0;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                shapers = unchecked((shapers * 31) + Shapers(new Vector2s(zone.x + dx, zone.y + dz)));
            }
        }

        ZDO changes = FindChanges(zone);
        return changes == null ? ((ZDOID.None, 0u, shapers), null) : ((changes.m_uid, changes.DataRevision, shapers), changes.GetByteArray(ChangesHash));
    }

    // What shapes a sector: each of its location proxies and shaping pieces by id and place (a proxy by its turn too:
    // its modifiers stand turned as it is), in whatever order the game lists them.
    private int Shapers(Vector2s sector)
    {
        long key = TileData.Key(sector);
        if (_shapers.TryGetValue(key, out int known))
        {
            return known;
        }

        int sum = 0, count = 0;
        _found.Clear();
        ZDOMan.instance.FindSectorObjects(sector, new SimulationDistance(0, 0), _found);
        foreach (ZDO zdo in _found)
        {
            int prefab = zdo.GetPrefab();
            bool proxy = prefab == LocationProxyHash;
            if (proxy || ShapingPiece(prefab) != null)
            {
                Vector3 at = zdo.GetPosition();
                int turn = proxy ? zdo.GetRotation().GetHashCode() : 0;
                int one = unchecked((((((((zdo.m_uid.GetHashCode() * 31) + at.x.GetHashCode()) * 31) + at.y.GetHashCode()) * 31) + at.z.GetHashCode()) * 31) + turn);
                sum = unchecked(sum + one);
                count++;
            }
        }

        int shapers = unchecked((sum * 31) + count);
        _shapers[key] = shapers;
        return shapers;
    }

    // The terrain modifiers of the locations in the zone and round it, and of any object that shapes the ground (an old
    // world's shaping pieces), over its generated heights.
    void IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Shapers)>.Shape(Vector2s zone, float[] heights)
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

    void IZoneGround<Vector2s, (ZDOID Id, uint Revision, int Shapers)>.Warn(Vector2s zone, string problem) =>
        FlowingWaterPlugin.Log.LogWarning($"Zone {zone}: {problem}");

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
