using System.Collections.Generic;
using FlowingWater.Client;
using FlowingWater.Volume;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlowingWater;

/// <summary>
/// The plugin's networked objects, made in code (no asset bundle) and added to the game's prefab
/// list each time a world opens: a water brick per 16-voxel cube with water (and the zone tiles of the column
/// water before it, read once to move their water over, never drawn), the source and drain markers (pieces of the
/// hoe's), erosion's ground.
/// </summary>
internal static class Prefabs
{
    public const string TileName = "FlowingWater_Tile";

    public const string SourceName = "FlowingWater_Source";

    public const string DrainName = "FlowingWater_Drain";

    public const string GroundName = "FlowingWater_Ground";

    public const string BrickName = "FlowingWater_Brick";

    public const string WaterLayer = "WaterVolume";

    /// <summary>
    /// The layer of a marker's collider: the game's for pieces nothing bumps into. Its aiming (to place, remove,
    /// hover) finds it; players, items and the water's own solids (pieces on the solid layer) do not.
    /// </summary>
    public const string MarkerLayer = "piece_nonsolid";

    /// <summary>A marker's sphere's radius, metres; it sits on the point it was placed at.</summary>
    public const float MarkerRadius = 0.3f;

    public static readonly int TileHash = TileName.GetStableHashCode();

    public static readonly int SourceHash = SourceName.GetStableHashCode();

    public static readonly int DrainHash = DrainName.GetStableHashCode();

    public static readonly int GroundHash = GroundName.GetStableHashCode();

    public static readonly int BrickHash = BrickName.GetStableHashCode();

    private static GameObject _root;

    /// <summary>The source's and drain's prefabs, made the first time they are asked for.</summary>
    public static IEnumerable<GameObject> Markers
    {
        get
        {
            Built();
            return [_root.transform.Find(SourceName).gameObject, _root.transform.Find(DrainName).gameObject];
        }
    }

    /// <summary>Adds the prefabs to a ZNetScene about to wake up (before it indexes its list).</summary>
    public static void Register(ZNetScene scene)
    {
        Built();
        foreach (Transform child in _root.transform)
        {
            if (!scene.m_prefabs.Contains(child.gameObject))
            {
                scene.m_prefabs.Add(child.gameObject);
            }
        }
    }

    private static void Built()
    {
        if (_root == null)
        {
            Build();
        }
    }

    private static void Build()
    {
        // The root is inactive so that the components never wake up on the prefabs themselves.
        _root = new GameObject("FlowingWater prefabs");
        _root.SetActive(false);
        Object.DontDestroyOnLoad(_root);

        // A column tile left in the world holds water the server could not move into voxels yet (it tries again at
        // each start): it is kept, not drawn. Drawn, it showed water no one could swim in over the bricks.
        Networked(TileName);

        MarkerPiece(Networked(SourceName), false);
        MarkerPiece(Networked(DrainName), true);
        Networked(GroundName).AddComponent<GroundChanges>();
        GameObject brick = Networked(BrickName);
        brick.AddComponent<MeshFilter>();
        MeshRenderer brickRenderer = brick.AddComponent<MeshRenderer>();
        brickRenderer.shadowCastingMode = ShadowCastingMode.Off;
        brickRenderer.receiveShadows = true;
        brick.AddComponent<WaterBrick>();

        // Sized to the brick when its data arrives (the voxels' size is the server's).
        Trigger(brick, Vector3.one * Brick.Size);
    }

    // A source or drain: a piece the hoe places exactly where the player aims, on the ground or on what was built
    // (clipping everything: no collider of its own moves it off the point), free, removed as any piece; with a small
    // sphere for the game's aiming, which nothing bumps into.
    private static void MarkerPiece(GameObject marker, bool drain)
    {
        marker.AddComponent<Marker>().IsDrain = drain;
        Piece piece = marker.AddComponent<Piece>();
        piece.m_name = MarkerValues.Name(drain);
        piece.m_description = MarkerValues.Description(drain);
        piece.m_clipEverything = true;
        piece.m_canBeRemoved = true;
        piece.m_canRotate = false;

        var aim = new GameObject("Aim");
        aim.transform.SetParent(marker.transform, false);
        int layer = LayerMask.NameToLayer(MarkerLayer);
        if (layer < 0)
        {
            FlowingWaterPlugin.Log.LogError($"Layer {MarkerLayer} not found: sources and drains cannot be aimed at with the hoe");
        }
        else
        {
            aim.layer = layer;
        }

        SphereCollider sphere = aim.AddComponent<SphereCollider>();
        sphere.radius = MarkerRadius;
        sphere.center = Vector3.up * MarkerRadius;
    }

    // A trigger box on the game's water layer under the object: what lets characters and things swim in it.
    private static void Trigger(GameObject parent, Vector3 size)
    {
        var trigger = new GameObject("Trigger");
        trigger.transform.SetParent(parent.transform, false);
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
        box.size = size;
        trigger.AddComponent<WaterTrigger>();
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
