using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// A placed source or drain: a small sphere where the water starts or ends. A piece of the hoe's: placed where the
/// player aims, with the panel's values (<see cref="IPlaced"/>); its hover text says what it does, and [E] on it opens
/// the panel to change it.
/// </summary>
internal sealed class Marker : MonoBehaviour, Hoverable, Interactable, IPlaced
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

    /// <summary>
    /// New values for this placed one, from a player's game that may not own it: taken over first, as the game's signs
    /// are when their text is written, and the server reads them at its next look.
    /// </summary>
    public void Change(float rate, float radius)
    {
        _nview.ClaimOwnership();
        Write(_nview.GetZDO(), IsDrain, rate, radius);
    }

    // The game destroys the object at the end of the frame: until then it must not be found again
    // (a second remove in the same frame destroyed it twice and failed).
    public void Remove()
    {
        All.Remove(this);
        _nview.ClaimOwnership();
        ZNetScene.instance.Destroy(gameObject);
    }

    /// <summary>The player placed it with the hoe: it takes the values the panel shows for the next one.</summary>
    public void OnPlaced()
    {
        if (IsValid)
        {
            NextPlacement next = HoePieces.Next;
            Write(_nview.GetZDO(), IsDrain, next.Rate(IsDrain), next.Radius);
        }
    }

    public string GetHoverText()
    {
        if (!IsValid)
        {
            return string.Empty;
        }

        bool mayChange = Commands.MarkerRefusal(CommandRights.MarkerUse.Change) == null && PrivateArea.CheckAccess(transform.position, 0f, false);
        return Localization.instance.Localize(MarkerValues.Hover(IsDrain, Rate, Radius, mayChange));
    }

    public string GetHoverName() => MarkerValues.Name(IsDrain);

    public float GetHoverOffset() => 0f;

    /// <summary>[E] on it: the panel opens to change it, for a player who may (others are told why not).</summary>
    public bool Interact(Humanoid user, bool hold, bool alt)
    {
        if (hold || !IsValid || user != Player.m_localPlayer || MarkerPanel.ClosedThisFrame)
        {
            return false;
        }

        if (Commands.MarkerRefusal(CommandRights.MarkerUse.Change) is { } refusal)
        {
            user.Message(MessageHud.MessageType.Center, refusal);
            return false;
        }

        if (!PrivateArea.CheckAccess(transform.position))
        {
            return false;
        }

        MarkerPanel.Open(this);
        return true;
    }

    public bool UseItem(Humanoid user, ItemDrop.ItemData item) => false;

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
