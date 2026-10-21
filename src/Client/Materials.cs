using System;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>The materials the water is drawn with, copied from the game's own at first use.</summary>
internal static class Materials
{
    private const string FlowMaterialName = "waterflow";

    private const string CryptRoomPrefix = "sunkencrypt_new_";

    private const string PlainShaderName = "Custom/Piece";

    private static readonly int ColourId = Shader.PropertyToID("_Color");

    private static Material _still;

    private static Material _flow;

    private static Material _source;

    private static Material _drain;

    private static bool _stillMissing;

    private static bool _flowMissing;

    /// <summary>The game's sea water, without its wind waves; null (logged once) when it cannot be found.</summary>
    public static Material Still()
    {
        if (_still != null || _stillMissing)
        {
            return _still;
        }

        Material source = ZoneWater();
        if (source == null)
        {
            _stillMissing = true;
            FlowingWaterPlugin.Log.LogError("The zone water material was not found: still water cannot be drawn");
            return null;
        }

        _still = new Material(source) { name = "FlowingWater still" };
        _still.SetFloat("_UseGlobalWind", 0f);
        _still.SetFloatArray("_depth", new float[4]);
        return _still;
    }

    /// <summary>The game's running-water material (a scrolling flow shader); null (logged once) when not loaded.</summary>
    public static Material Flow()
    {
        if (_flow != null || _flowMissing)
        {
            return _flow;
        }

        Material source = FindLoaded(FlowMaterialName) ?? FromCryptRooms();
        if (source == null)
        {
            _flowMissing = true;
            FlowingWaterPlugin.Log.LogWarning($"Material {FlowMaterialName} was not found: streams are drawn as still water");
            return null;
        }

        _flow = new Material(source) { name = "FlowingWater stream" };
        _flow.SetFloat("_SpeedX", 0f);
        _flow.SetFloat("_SpeedY", FlowingWaterPlugin.Settings.FlowTextureSpeed.Value);
        return _flow;
    }

    /// <summary>A plain coloured material for a source or drain marker; null when no plain material can be found.</summary>
    public static Material Marker(bool drain)
    {
        Material material = drain ? _drain : _source;
        if (material != null)
        {
            return material;
        }

        material = Plain(drain ? "FlowingWater drain" : "FlowingWater source", drain ? new Color(0.15f, 0.1f, 0.25f) : new Color(0.2f, 0.55f, 1f));
        if (drain)
        {
            _drain = material;
        }
        else
        {
            _source = material;
        }

        return material;
    }

    /// <summary>An opaque coloured copy of the game's piece material (its own shaders draw; a Standard one shows pink). Null when none is loaded.</summary>
    public static Material Plain(string name, Color colour)
    {
        Material source = null;
        foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (material.shader != null && material.shader.name == PlainShaderName && material.HasProperty(ColourId))
            {
                source = material;
                break;
            }
        }

        if (source == null)
        {
            FlowingWaterPlugin.Log.LogWarning($"No {PlainShaderName} material is loaded: {name} cannot be drawn");
            return null;
        }

        var plain = new Material(source) { name = name };
        plain.SetColor(ColourId, colour);
        return plain;
    }

    private static Material ZoneWater()
    {
        ZoneSystem zones = ZoneSystem.instance;
        if (zones == null || zones.m_zonePrefab == null)
        {
            return null;
        }

        WaterVolume volume = zones.m_zonePrefab.GetComponentInChildren<WaterVolume>(true);
        if (volume == null || volume.m_waterSurface == null)
        {
            return null;
        }

        return volume.m_waterSurface.sharedMaterial;
    }

    // The running-water material lives in the sunken crypts' rooms; loading one room brings it in
    // (and its textures), and the room stays loaded so that they do too.
    private static Material FromCryptRooms()
    {
        if (DungeonDB.instance == null)
        {
            return null;
        }

        foreach (DungeonDB.RoomData room in DungeonDB.GetRooms())
        {
            if (!room.m_prefab.Name.StartsWith(CryptRoomPrefix, StringComparison.Ordinal))
            {
                continue;
            }

            room.m_prefab.Load();
            GameObject asset = room.m_prefab.Asset;
            if (asset == null)
            {
                continue;
            }

            foreach (Renderer renderer in asset.GetComponentsInChildren<Renderer>(true))
            {
                foreach (Material material in renderer.sharedMaterials)
                {
                    if (material != null && material.name == FlowMaterialName)
                    {
                        FlowingWaterPlugin.Log.LogInfo($"Material {FlowMaterialName} taken from {room.m_prefab.Name}");
                        return material;
                    }
                }
            }
        }

        return null;
    }

    private static Material FindLoaded(string name)
    {
        foreach (Material material in Resources.FindObjectsOfTypeAll<Material>())
        {
            if (material.name == name)
            {
                return material;
            }
        }

        return null;
    }
}
