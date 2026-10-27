using System.Collections.Generic;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>The materials the water is drawn with, copied from the game's own at first use.</summary>
internal static class Materials
{
    private const string PlainShaderName = "Custom/Piece";

    private static readonly int ColourId = Shader.PropertyToID("_Color");

    // What the water takes from the game's sea material: its textures, colours and settings, by the same names.
    private static readonly string[] SeaTextures = ["_Normal", "_FoamTex", "_RandomFoamTex"];

    private static readonly string[] SeaColours = ["_ColorTop", "_ColorBottom", "_ColorBottomShallow", "_SurfaceColor", "_FoamColor"];

    private static readonly string[] SeaFloats = ["_Glossiness", "_Metallic", "_NormalPower", "_NormalScale", "_WaveVel", "_DepthFade", "_FoamDepth", "_RefractionScale", "_RefractionMax"];

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

    private static Material _shell;

    private static bool _shellTried;

    private static bool _stillMissing;

    private static Material _sim;

    private static ComputeShader _simCompute;

    private static bool _simTried;

    private static Material _spray;

    private static ComputeShader _sprayCompute;

    private static bool _sprayTried;

    private static Material _particles;

    private static Material _particleSurface;

    private static ComputeShader _volumeCompute;

    private static Material _froth;

    private static bool _volumeTried;

    private static ComputeShader _particlesCompute;

    private static bool _particlesTried;

    // Whether the mod's materials took the sea's look: not while the sea's material could not be found (a world not
    // open yet), tried again then.
    private static bool _lookTaken;

    // Frames between two looks for the sea's material while it is not found, and the frame of the next.
    private const int LookRetryFrames = 60;

    private static int _lookRetryFrame;

    /// <summary>
    /// A world opens: what could not be made or found in the last one (the bundle, its shaders, the sea's material and
    /// look) has a fresh try, instead of staying missing for the rest of the game. What was made is kept.
    /// </summary>
    public static void WorldOpened()
    {
        _customTried &= _custom != null;
        _shellTried &= _shell != null;
        _simTried &= _sim != null && _simCompute != null;
        _sprayTried &= _spray != null && _sprayCompute != null;
        _particlesTried &= _particles != null && _particleSurface != null && _particlesCompute != null;
        _volumeTried &= _volumeCompute != null && _froth != null;
        _lookRetryFrame = 0;
        _stillMissing = false;
        _lookTaken = false;
    }

    /// <summary>
    /// The mod's own water material, from the asset bundle built for this platform and embedded in
    /// the dll; null (logged once a world) on a platform without one or when it does not load.
    /// </summary>
    public static Material Custom()
    {
        if (_customTried)
        {
            TakeSeaLookAgain();
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

        if (_bundle == null)
        {
            _bundle = AssetBundle.LoadFromMemory(bytes);
        }

        if (_bundle == null)
        {
            FlowingWaterPlugin.Log.LogError($"Water bundle {platform} did not load (built for another Unity version?): the game's materials are used");
            return null;
        }

        _custom = FromBundle("FlowingWater", "FlowingWater custom", "the game's materials are used");
        if (_custom != null)
        {
            FlowingWaterPlugin.Log.LogInfo($"Water shader {_custom.shader.name} loaded from the {platform} bundle");
        }

        return _custom;
    }

    /// <summary>
    /// The mod's material for the water's voxels where it meets the air (the sides of standing water, falls), from the
    /// same bundle as <see cref="Custom"/>, with the sea's look; null (logged once a world) where that has none.
    /// </summary>
    public static Material Shell()
    {
        if (_shellTried)
        {
            TakeSeaLookAgain();
            return _shell;
        }

        _shellTried = true;
        if (Custom() == null)
        {
            return null;
        }

        _shell = FromBundle("FlowingWaterShell", "FlowingWater shell", "the water's sides and falls are not drawn");
        return _shell;
    }

    // A material of the bundle's, with a shader this machine draws, copied with the sea's look; null (logged) where
    // the bundle has none or its shader is not supported here.
    private static Material FromBundle(string asset, string name, string whatFails)
    {
        Material material = _bundle.LoadAsset<Material>(asset);
        if (material == null || material.shader == null || !material.shader.isSupported)
        {
            FlowingWaterPlugin.Log.LogError($"Water shader {asset} from the bundle is not supported here (shader {(material == null || material.shader == null ? "missing" : material.shader.name)}): {whatFails}");
            return null;
        }

        var copy = new Material(material) { name = name };
        TakeSeaLook(copy);
        return copy;
    }

    // The sea's look taken again onto the materials made without it (the sea's material was not found then): once it
    // is, looked for once a second at most (this is asked for every brick every frame, and the look is a search of the
    // zone prefab).
    private static void TakeSeaLookAgain()
    {
        if (_lookTaken || Time.frameCount < _lookRetryFrame)
        {
            return;
        }

        _lookRetryFrame = Time.frameCount + LookRetryFrames;
        if (ZoneWater() == null)
        {
            return;
        }

        if (_custom != null)
        {
            TakeSeaLook(_custom);
        }

        if (_shell != null)
        {
            TakeSeaLook(_shell);
        }

        if (_sim != null)
        {
            TakeSeaLook(_sim);
        }

        if (_spray != null)
        {
            TakeSeaLook(_spray);
        }

        if (Falls != null)
        {
            TakeSeaLook(Falls);
        }

        if (_particles != null)
        {
            TakeSeaLook(_particles);
        }

        if (_particleSurface != null)
        {
            TakeSeaLook(_particleSurface);
        }
    }

    /// <summary>
    /// The mod's material for the water moved on the graphics card near the camera, with the sea's look, and the compute
    /// shader that moves it, from the same bundle as <see cref="Custom"/>; false (logged once a world) where that has
    /// none, or the card runs no compute shaders.
    /// </summary>
    public static bool Sim(out Material material, out ComputeShader compute)
    {
        if (_simTried)
        {
            TakeSeaLookAgain();
        }
        else
        {
            _simTried = true;
            LoadSim();
        }

        material = _sim;
        compute = _simCompute;
        return _sim != null && _simCompute != null;
    }

    /// <summary>
    /// The falls', splashes' and spray's particles of the water moved on the graphics card: their material (with the sea's
    /// foam) and the compute shader that moves them, from the same bundle; false (logged once a world) where it has none
    /// or the water is not moved on the card.
    /// </summary>
    public static bool Spray(out Material material, out ComputeShader compute)
    {
        if (!_sprayTried)
        {
            _sprayTried = true;
            if (Sim(out _, out _))
            {
                compute = _bundle.LoadAsset<ComputeShader>("FlowingWaterSpray");
                if (compute == null)
                {
                    FlowingWaterPlugin.Log.LogError("The compute shader of the water's falls and spray is missing from the bundle: none are drawn");
                }
                else
                {
                    _spray = FromBundle("FlowingWaterSpray", "FlowingWater spray", "the water's falls and spray are not drawn");
                    _sprayCompute = _spray == null ? null : compute;
                    Falls = _spray == null ? null : FromBundle("FlowingWaterFall", "FlowingWater falls", "falls are drawn as spray only");
                }
            }
        }

        material = _spray;
        compute = _sprayCompute;
        return _spray != null && _sprayCompute != null;
    }

    /// <summary>
    /// The falls' curtains' material (with the sea's foam), from the same bundle as <see cref="Spray"/>, once that is
    /// made; none (logged) where the bundle has none: the falls are drawn as their spray only.
    /// </summary>
    public static Material Falls { get; private set; }

    /// <summary>
    /// The particle water's (a prototype, poured by the console): the material of its particles, the material of the
    /// surface drawn over them (with the sea's look) and the compute shader that moves them, from the same bundle; false
    /// (logged once a world) where it has none or the water is not moved on the card.
    /// </summary>
    public static bool Particles(out Material material, out Material surface, out ComputeShader compute)
    {
        if (!_particlesTried)
        {
            _particlesTried = true;
            if (Sim(out _, out _))
            {
                compute = _bundle.LoadAsset<ComputeShader>("FlowingWaterParticles");
                if (compute == null)
                {
                    FlowingWaterPlugin.Log.LogError("The compute shader of the particle water is missing from the bundle: none is poured");
                }
                else
                {
                    _particles = FromBundle("FlowingWaterParticles", "FlowingWater particles", "no particle water is poured");
                    _particleSurface = _particles == null ? null : FromBundle("FlowingWaterParticleSurface", "FlowingWater particle surface", "no particle water is poured");
                    _particlesCompute = _particleSurface == null ? null : compute;
                    if (_particleSurface != null)
                    {
                        TakeSeaLook(_particles);
                        TakeSeaLook(_particleSurface);
                    }
                }
            }
        }

        material = _particles;
        surface = _particleSurface;
        compute = _particlesCompute;
        return _particles != null && _particleSurface != null && _particlesCompute != null;
    }

    /// <summary>
    /// The volume water's: the compute shader that moves it, with the particle water's materials it is drawn with
    /// (<see cref="Particles"/>) and its froth's (<paramref name="froth"/>: null, logged once a world, where the bundle
    /// has none, the water going on without froth); false (logged once a world) where the bundle has no compute shader.
    /// </summary>
    public static bool Volume(out Material material, out Material surface, out Material froth, out ComputeShader compute)
    {
        compute = null;
        froth = null;
        if (!Particles(out material, out surface, out _))
        {
            return false;
        }

        if (!_volumeTried)
        {
            _volumeTried = true;
            _volumeCompute = _bundle.LoadAsset<ComputeShader>("FlowingWaterVolume");
            if (_volumeCompute == null)
            {
                FlowingWaterPlugin.Log.LogError("The compute shader of the volume water is missing from the bundle: the water near you stays a sheet");
            }

            _froth = FromBundle("FlowingWaterFroth", "FlowingWater froth", "the volume water makes no spray, foam or bubbles");
            if (_froth != null)
            {
                TakeSeaLook(_froth);
            }
        }

        compute = _volumeCompute;
        froth = _froth;
        return compute != null;
    }

    private static void LoadSim()
    {
        if (!SystemInfo.supportsComputeShaders)
        {
            FlowingWaterPlugin.Log.LogWarning($"The graphics card ({SystemInfo.graphicsDeviceType}) runs no compute shaders: the water near you is drawn as the server sends it");
            return;
        }

        if (Custom() == null)
        {
            return;
        }

        var compute = _bundle.LoadAsset<ComputeShader>("FlowingWaterSim");
        if (compute == null)
        {
            FlowingWaterPlugin.Log.LogError("The compute shader that moves the water on the graphics card is missing from the bundle: the water near you is drawn as the server sends it");
            return;
        }

        _sim = FromBundle("FlowingWaterSim", "FlowingWater sim", "the water near you is drawn as the server sends it");
        if (_sim == null)
        {
            return;
        }

        _simCompute = compute;
        FlowingWaterPlugin.Log.LogInfo("Water moved on the graphics card loaded from the bundle");
    }

    // The sea's look onto the water: its own textures, colours and settings, so the two are the same water. Without
    // the sea's material (or one of its values) the shader's own stand in: the sea's as they were when it was written,
    // with made textures.
    private static void TakeSeaLook(Material water)
    {
        Material sea = ZoneWater();
        if (sea == null)
        {
            FlowingWaterPlugin.Log.LogWarning("The sea's material was not found: the water is drawn with the shader's own copy of its look until a world is open");
            return;
        }

        _lookTaken = true;

        var missing = new List<string>();
        foreach (string name in SeaTextures)
        {
            if (sea.HasProperty(name) && sea.GetTexture(name) is { } texture)
            {
                water.SetTexture(name, texture);
            }
            else
            {
                missing.Add(name);
            }
        }

        foreach (string name in SeaColours)
        {
            if (sea.HasProperty(name))
            {
                water.SetColor(name, sea.GetColor(name));
            }
            else
            {
                missing.Add(name);
            }
        }

        foreach (string name in SeaFloats)
        {
            if (sea.HasProperty(name))
            {
                water.SetFloat(name, sea.GetFloat(name));
            }
            else
            {
                missing.Add(name);
            }
        }

        if (missing.Count > 0)
        {
            FlowingWaterPlugin.Log.LogWarning($"The sea's material has no {string.Join(", ", missing)}: the water uses the shader's own for those");
        }
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

    /// <summary>The plain red of a source or drain the hoe's remover aims at; null when no plain material can be found.</summary>
    public static Material MarkerAimed() => Plain("FlowingWater marker to remove", new Color(0.95f, 0.2f, 0.12f));

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

    /// <summary>
    /// Sets a float (one value) or colour (three or four) property on the custom (the bricks'), card (the water moved on
    /// the graphics card), falls (its falls' curtains), spray or still material, for tuning in game.
    /// </summary>
    public static string Tune(string kind, string property, float[] values)
    {
        Material material = kind switch
        {
            "custom" => _custom,
            "card" => _sim,
            "falls" => Falls,
            "spray" => _spray,
            "still" => _still,
            "froth" => _froth,
            _ => null,
        };
        if (material == null)
        {
            return $"no {kind} material (custom, card, falls, spray, still or froth; water must have been drawn once with it)";
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
