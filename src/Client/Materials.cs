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

    private static readonly int FoamColourId = Shader.PropertyToID("_FoamColor");

    private static readonly int MainTextureId = Shader.PropertyToID("_MainTex");

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
        // The sea's depth tint, half way: a pool is not the open ocean, but all shallow it vanishes.
        float[] depth = [0.5f, 0.5f, 0.5f, 0.5f];
        _still.SetFloatArray("_depth", depth);
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
        // The crypts' material masks its water into thin stripes (a trickle along a channel) and
        // tints it a murky green; a river is a full sheet, blue-grey with white foam. The scrolling
        // normal map stays: that is the flow.
        _flow.SetTexture(MainTextureId, Texture2D.whiteTexture);
        _flow.SetColor(ColourId, new Color(0.45f, 0.62f, 0.72f, 0.6f));
        _flow.SetColor(FoamColourId, new Color(0.9f, 0.95f, 1f, 1f));
        _flow.SetFloat("_DepthFade", 1f);
        _flow.SetFloat("_ShoreFade", 0.3f);
        _flow.SetFloat("_BumpScale", 0.5f);
        _flow.SetFloat("_FoamDepth", 0.4f);
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

    /// <summary>Sets a float (one value) or colour (three or four) property on the still or stream material, for tuning in game.</summary>
    public static string Tune(string kind, string property, float[] values)
    {
        Material material = kind == "still" ? _still : kind == "stream" ? _flow : null;
        if (material == null)
        {
            return $"no {kind} material (still or stream, and water must have been drawn once)";
        }

        if (!material.HasProperty(property))
        {
            return $"{material.shader.name} has no property {property}; try: {Properties(material)}";
        }

        switch (values.Length)
        {
            case 1:
                material.SetFloat(property, values[0]);
                return $"{kind} {property} = {values[0]:0.###}";
            case 3:
            case 4:
                var colour = new Color(values[0], values[1], values[2], values.Length == 4 ? values[3] : 1f);
                material.SetColor(property, colour);
                return $"{kind} {property} = {colour}";
            case 0:
                return $"{kind} {property} = {material.GetFloat(property):0.###} (float) / {material.GetColor(property)} (colour)";
            default:
                return "give one value for a float or three or four for a colour";
        }
    }

    /// <summary>The names of a material's properties, for the console.</summary>
    public static string Properties(Material material)
    {
        var names = new System.Text.StringBuilder();
        Shader shader = material.shader;
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            if (names.Length > 0)
            {
                names.Append(' ');
            }

            names.Append(shader.GetPropertyName(i));
        }

        return names.ToString();
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
