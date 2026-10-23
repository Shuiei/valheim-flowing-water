using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// A placed source or drain: a small sphere where the water starts or ends. A piece of the hoe's: placed where the
/// player aims, with the values the next one takes (<see cref="IPlaced"/>).
/// </summary>
internal sealed class Marker : MonoBehaviour, IPlaced
{
    private static readonly List<Marker> All = [];

    private ZNetView _nview;

    /// <summary>True for a drain, false for a source; set on the prefab.</summary>
    [SuppressMessage("Design", "CA1051:Do not declare visible instance fields", Justification = "Unity copies fields, not properties, from the prefab.")]
    public bool IsDrain;

    public float Rate => _nview.GetZDO().GetFloat(ZdoKeys.Rate, IsDrain ? FlowingWaterPlugin.Settings.DefaultDrainRate.Value : FlowingWaterPlugin.Settings.DefaultSourceRate.Value);

    /// <summary>A drain's radius as written in it, metres.</summary>
    public float Radius => _nview.GetZDO().GetFloat(ZdoKeys.Radius, FlowingWaterPlugin.Settings.DefaultDrainRadius.Value);

    /// <summary>Whether it is still in the world (not destroyed, its object known).</summary>
    public bool IsValid => this != null && _nview != null && _nview.IsValid();

    public static Marker Nearest(Vector3 point, float within)
    {
        Marker best = null;
        float bestDistance = within;
        foreach (Marker marker in All)
        {
            if (marker == null || !marker._nview.IsValid())
            {
                continue;
            }

            float distance = Vector3.Distance(marker.transform.position, point);
            if (distance <= bestDistance)
            {
                best = marker;
                bestDistance = distance;
            }
        }

        return best;
    }

    /// <summary>
    /// A source's or drain's values into its object, which this game owns: what the server reads (a source's radius
    /// is not written, it has none).
    /// </summary>
    public static void Write(ZDO zdo, bool drain, float rate, float radius)
    {
        zdo.Set(ZdoKeys.Rate, rate);
        if (drain)
        {
            zdo.Set(ZdoKeys.Radius, radius);
        }
    }

    // The game destroys the object at the end of the frame: until then it must not be found again
    // (a second remove in the same frame destroyed it twice and failed).
    public void Remove()
    {
        All.Remove(this);
        _nview.ClaimOwnership();
        ZNetScene.instance.Destroy(gameObject);
    }

    /// <summary>The player placed it with the hoe: it takes the values kept for the next one.</summary>
    public void OnPlaced()
    {
        if (IsValid)
        {
            NextPlacement next = HoePieces.Next;
            Write(_nview.GetZDO(), IsDrain, next.Rate(IsDrain), next.Radius);
        }
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Awake()
    {
        _nview = GetComponent<ZNetView>();

        // The hoe's placement ghost has no object of its own (the game makes it with networking off): it is drawn,
        // nothing more.
        bool ghost = ZNetView.m_forceDisableInit;
        if (!ghost && (_nview == null || !_nview.IsValid()))
        {
            enabled = false;
            return;
        }

        if (ghost)
        {
            enabled = false;
        }
        else
        {
            All.Add(this);
        }

        if (ZNet.instance != null && ZNet.instance.IsDedicated())
        {
            return;
        }

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        Destroy(sphere.GetComponent<Collider>());
        sphere.transform.SetParent(transform, false);
        sphere.transform.localScale = Vector3.one * (Prefabs.MarkerRadius * 2f);
        sphere.transform.localPosition = Vector3.up * Prefabs.MarkerRadius;
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
