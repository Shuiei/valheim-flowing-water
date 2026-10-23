using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using HarmonyLib;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// What erosion did to a zone's ground, on a player's game: the server's change for the zone (its
/// <c>FlowingWater_Ground</c> object), added to the zone's heightmap after the game's own terrain changes, so
/// that its look, its collision and building on it all follow. When the change arrives the zone's ground is
/// built again, and the zones west and south of it, whose last row or column is this one's first.
/// </summary>
internal sealed class GroundChanges : MonoBehaviour
{
    // Each zone's change, with the object it came from: an older object for the zone, destroyed after a newer
    // one arrived, must not take the newer one's change away.
    private static readonly Dictionary<long, (GroundChanges From, short[] Points)> ByZone = [];

    private static readonly AccessTools.FieldRef<Heightmap, List<float>> Heights = AccessTools.FieldRefAccess<Heightmap, List<float>>("m_heights");

    private static readonly (int X, int Z)[] Rebuilt = [(0, 0), (-1, 0), (0, -1), (-1, -1)];

    private ZNetView _nview;

    private Vector2s _zone;

    private uint _revision = uint.MaxValue;

    /// <summary>Adds erosion's change to a zone heightmap just built from the game's ground and terrain changes.</summary>
    public static void ApplyTo(Heightmap heightmap)
    {
        if (ByZone.Count == 0 || heightmap.IsDistantLod || !Mathf.Approximately(heightmap.m_scale, 1f))
        {
            return;
        }

        List<float> heights = Heights(heightmap);
        int width = heightmap.m_width;
        Vector3 corner = heightmap.transform.position - new Vector3(width * 0.5f, 0f, width * 0.5f);
        for (int z = 0; z <= width; z++)
        {
            for (int x = 0; x <= width; x++)
            {
                heights[(z * (width + 1)) + x] += At(new Vector3(corner.x + x, 0f, corner.z + z));
            }
        }
    }

    // A world point's change, metres: from its zone's heightmap point (the point a whole metre from the
    // zone's corner), 0 where the zone has none.
    private static float At(Vector3 point)
    {
        Vector2s zone = TileData.CellOf(point, ErosionGround.Size, out int x, out int z);
        return ByZone.TryGetValue(TileData.Key(zone), out (GroundChanges _, short[] Points) change) ? ErosionGround.At(change.Points, x, z) : 0f;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Awake()
    {
        _nview = GetComponent<ZNetView>();
        if (_nview == null || !_nview.IsValid())
        {
            enabled = false;
            return;
        }

        _zone = ZoneSystem.GetZone(transform.position);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Update()
    {
        ZDO zdo = _nview.GetZDO();
        if (zdo == null || zdo.DataRevision == _revision)
        {
            return;
        }

        _revision = zdo.DataRevision;
        byte[] data = zdo.GetByteArray(ZdoKeys.Ground);
        if (data == null)
        {
            return;
        }

        try
        {
            ByZone[TileData.Key(_zone)] = (this, ErosionGround.Decode(data));
        }
        catch (InvalidDataException e)
        {
            FlowingWaterPlugin.Log.LogWarning($"Zone {_zone}: erosion's ground change unreadable, left out ({e.Message})");
            return;
        }

        RebuildGround();
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        long key = TileData.Key(_zone);
        if (ByZone.TryGetValue(key, out (GroundChanges From, short[] _) change) && ReferenceEquals(change.From, this))
        {
            ByZone.Remove(key);
            RebuildGround();
        }
    }

    // The game builds the zones' ground again at the end of the frame, with the change.
    private void RebuildGround()
    {
        foreach ((int dx, int dz) in Rebuilt)
        {
            Heightmap.FindHeightmap(ZoneSystem.GetZonePos(new Vector2s(_zone.x + dx, _zone.y + dz)))?.Poke(1);
        }
    }
}
