using System.Collections.Generic;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>The materials the water is drawn with, copied from the game's own at first use.</summary>
internal static class Materials
{
    private const string PlainShaderName = "Custom/Piece";

    private static readonly int ColourId = Shader.PropertyToID("_Color");

    private static readonly int AbsorptionId = Shader.PropertyToID("_Absorption");

    private static readonly int DeepColourId = Shader.PropertyToID("_DeepColor");

    // What the game's water materials read from each renderer: the time their waves run on, how far they are drawn.
    private static readonly int WaterTimeId = Shader.PropertyToID("_WaterTime");

    private static readonly int VisibleMaxDistanceId = Shader.PropertyToID("_VisibleMaxDistance");

    // The plain materials made, by name: each is made once.
    private static readonly Dictionary<string, Material> PlainByName = [];

    private static Material _still;

    private static Material _source;

    private static Material _drain;

    private static Material _custom;

    private static AssetBundle _bundle;

    private static bool _customTried;

    private static bool _stillMissing;

    /// <summary>
    /// The mod's own water material, from the asset bundle built for this platform and embedded in
    /// the dll; null (logged once) on a platform without one or when it does not load.
    /// </summary>
    public static Material Custom()
    {
        if (_customTried)
        {
            return _custom;
        }

        _customTried = true;
        string platform = Application.platform switch
        {
            RuntimePlatform.WindowsPlayer => "windows",
            RuntimePlatform.LinuxPlayer => "linux",
            _ => null,
        };
        if (platform == null)
        {
            FlowingWaterPlugin.Log.LogWarning($"No water shader built for {Application.platform}: the game's materials are used");
            return null;
        }

        string name = "FlowingWater.flowingwater." + platform;
        // LoadFromMemory copies the bytes; a stream would have to stay open as long as the bundle.
        byte[] bytes;
        using (System.IO.Stream stream = typeof(Materials).Assembly.GetManifestResourceStream(name))
        {
            if (stream == null)
            {
                FlowingWaterPlugin.Log.LogError($"Resource {name} is missing from the dll: the game's materials are used");
                return null;
            }

            bytes = new byte[stream.Length];
            int read = 0;
            while (read < bytes.Length)
            {
                int got = stream.Read(bytes, read, bytes.Length - read);
                if (got <= 0)
                {
                    FlowingWaterPlugin.Log.LogError($"Resource {name} ended after {read} of {bytes.Length} bytes: the game's materials are used");
                    return null;
                }

                read += got;
            }
        }

        _bundle = AssetBundle.LoadFromMemory(bytes);

        if (_bundle == null)
        {
            FlowingWaterPlugin.Log.LogError($"Water bundle {platform} did not load (built for another Unity version?): the game's materials are used");
            return null;
        }

        Material material = _bundle.LoadAsset<Material>("FlowingWater");
        if (material == null || material.shader == null || !material.shader.isSupported)
        {
            FlowingWaterPlugin.Log.LogError($"Water shader from the {platform} bundle is not supported here (shader {(material == null || material.shader == null ? "missing" : material.shader.name)}): the game's materials are used");
            return null;
        }

        _custom = new Material(material) { name = "FlowingWater custom" };
        ApplyLook();
        FlowingWaterPlugin.Settings.WaterClarity.SettingChanged += (_, _) => ApplyLook();
        FlowingWaterPlugin.Settings.WaterColour.SettingChanged += (_, _) => ApplyLook();
        FlowingWaterPlugin.Log.LogInfo($"Water shader {material.shader.name} loaded from the {platform} bundle");
        return _custom;
    }

    // The water's clarity and colour from the settings, onto the custom material.
    private static void ApplyLook()
    {
        (float r, float g, float b) = WaterLook.Absorption(FlowingWaterPlugin.Settings.WaterClarity.Value);
        _custom.SetVector(AbsorptionId, new Vector4(r, g, b, 0f));
        _custom.SetColor(DeepColourId, FlowingWaterPlugin.Settings.WaterColour.Value);
    }

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

    /// <summary>
    /// A renderer's properties for the game's water materials, which read them from each renderer as the game's own
    /// water sets them: how far the water is drawn (the near simulation area) and, each frame (<see cref="Animate"/>),
    /// the time its waves run on.
    /// </summary>
    public static MaterialPropertyBlock GameMaterialProperties()
    {
        var properties = new MaterialPropertyBlock();
        properties.SetFloat(VisibleMaxDistanceId, ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance * ZoneSystem.c_ZoneSize);
        return properties;
    }

    /// <summary>The renderer's properties, with this frame's time for the waves.</summary>
    public static void Animate(Renderer renderer, MaterialPropertyBlock properties)
    {
        properties.SetFloat(WaterTimeId, ZNet.instance.GetWrappedDayTimeSeconds());
        renderer.SetPropertyBlock(properties);
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

    /// <summary>
    /// An opaque coloured copy of the game's piece material (its own shaders draw; a Standard one shows pink), made
    /// once for each name. Null when none is loaded.
    /// </summary>
    public static Material Plain(string name, Color colour)
    {
        if (PlainByName.TryGetValue(name, out Material made) && made != null)
        {
            return made;
        }

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

        // The copy keeps the piece's own textures, which tint the colour (a marker came out black).
        var plain = new Material(source) { name = name };
        plain.SetColor(ColourId, colour);
        foreach (string texture in plain.GetTexturePropertyNames())
        {
            plain.SetTexture(texture, texture.Contains("Bump") || texture.Contains("Normal") ? null : Texture2D.whiteTexture);
        }

        PlainByName[name] = plain;
        return plain;
    }

    /// <summary>Sets a float (one value) or colour (three or four) property on the custom or still material, for tuning in game.</summary>
    public static string Tune(string kind, string property, float[] values)
    {
        Material material = kind switch
        {
            "custom" => _custom,
            "still" => _still,
            _ => null,
        };
        if (material == null)
        {
            return $"no {kind} material (custom or still; water must have been drawn once with it)";
        }

        if (property == "list")
        {
            return $"{kind} ({material.shader.name}) properties: {Properties(material)}";
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
                if (material.shader.GetPropertyType(material.shader.FindPropertyIndex(property)) == UnityEngine.Rendering.ShaderPropertyType.Vector)
                {
                    // A vector is numbers as they are; a colour is converted to the game's colour space.
                    material.SetVector(property, colour);
                    return $"{kind} {property} = {(Vector4)colour}";
                }

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
}
