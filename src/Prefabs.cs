using FlowingWater.Client;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlowingWater;

/// <summary>
/// The plugin's networked objects, made in code (no asset bundle) and added to the game's prefab
/// list each time a world opens: a water brick per 16-voxel cube with water (and the zone tiles of the column
/// water before it, read once to move their water over), the source and drain markers, erosion's ground.
/// </summary>
internal static class Prefabs
{
    public const string TileName = "FlowingWater_Tile";

    public const string SourceName = "FlowingWater_Source";

    public const string DrainName = "FlowingWater_Drain";

    public const string GroundName = "FlowingWater_Ground";

    public const string BrickName = "FlowingWater_Brick";

    public const string WaterLayer = "WaterVolume";

    public static readonly int TileHash = TileName.GetStableHashCode();

    public static readonly int SourceHash = SourceName.GetStableHashCode();

    public static readonly int DrainHash = DrainName.GetStableHashCode();

    public static readonly int GroundHash = GroundName.GetStableHashCode();

    public static readonly int BrickHash = BrickName.GetStableHashCode();

    private static GameObject _root;

    /// <summary>Adds the prefabs to a ZNetScene about to wake up (before it indexes its list).</summary>
    public static void Register(ZNetScene scene)
    {
        if (_root == null)
        {
            Build();
        }

        foreach (Transform child in _root.transform)
        {
            if (!scene.m_prefabs.Contains(child.gameObject))
            {
                scene.m_prefabs.Add(child.gameObject);
            }
        }
    }

    private static void Build()
    {
        // The root is inactive so that the components never wake up on the prefabs themselves.
        _root = new GameObject("FlowingWater prefabs");
        _root.SetActive(false);
        Object.DontDestroyOnLoad(_root);

        GameObject tile = Networked(TileName);
        tile.AddComponent<MeshFilter>();
        MeshRenderer renderer = tile.AddComponent<MeshRenderer>();
        renderer.shadowCastingMode = ShadowCastingMode.Off;
        renderer.receiveShadows = true;
        tile.AddComponent<WaterTile>();
        var trigger = new GameObject("Trigger");
        trigger.transform.SetParent(tile.transform, false);
        int layer = LayerMask.NameToLayer(WaterLayer);
        if (layer < 0)
        {
            FlowingWaterPlugin.Log.LogError($"Layer {WaterLayer} not found: swimming in flowing water will not work");
        }
        else
        {
            trigger.layer = layer;
        }

        BoxCollider box = trigger.AddComponent<BoxCollider>();
        box.isTrigger = true;
        box.size = new Vector3(TileData.ZoneMetres, 1f, TileData.ZoneMetres);
        trigger.AddComponent<WaterTrigger>();

        Networked(SourceName).AddComponent<Marker>().IsDrain = false;
        Networked(DrainName).AddComponent<Marker>().IsDrain = true;
        Networked(GroundName).AddComponent<GroundChanges>();
        Networked(BrickName);
    }

    private static GameObject Networked(string name)
    {
        var go = new GameObject(name);
        go.transform.SetParent(_root.transform, false);
        ZNetView view = go.AddComponent<ZNetView>();
        view.m_persistent = true;
        view.m_distant = false;
        view.m_type = ZDO.ObjectType.Default;
        return go;
    }
}
