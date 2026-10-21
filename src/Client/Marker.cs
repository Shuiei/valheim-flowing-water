using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using FlowingWater.Server;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>A placed source or drain: a small sphere where the water starts or ends.</summary>
internal sealed class Marker : MonoBehaviour
{
    private static readonly List<Marker> All = [];

    private ZNetView _nview;

    /// <summary>True for a drain, false for a source; set on the prefab.</summary>
    [SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Unity copies fields, not properties, from the prefab.")]
    public bool IsDrain;

    public float Rate => _nview.GetZDO().GetFloat(WaterServer.RateHash, IsDrain ? FlowingWaterPlugin.Settings.DefaultDrainRate.Value : FlowingWaterPlugin.Settings.DefaultSourceRate.Value);

    public static Marker Nearest(Vector3 point, float within)
    {
        Marker best = null;
        float bestDistance = within;
        foreach (Marker marker in All)
        {
            float distance = Vector3.Distance(marker.transform.position, point);
            if (distance <= bestDistance)
            {
                best = marker;
                bestDistance = distance;
            }
        }

        return best;
    }

    public void Remove()
    {
        _nview.ClaimOwnership();
        ZNetScene.instance.Destroy(gameObject);
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

        All.Add(this);
        if (ZNet.instance != null && ZNet.instance.IsDedicated())
        {
            return;
        }

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(transform, false);
        sphere.transform.localScale = Vector3.one * 0.6f;
        sphere.transform.localPosition = Vector3.up * 0.3f;
        Material material = Materials.Marker(IsDrain);
        if (material != null)
        {
            sphere.GetComponent<Renderer>().sharedMaterial = material;
        }
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        All.Remove(this);
    }
}
