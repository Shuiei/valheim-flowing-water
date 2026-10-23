using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The tile's trigger box on the game's water layer: tells characters, items and ships inside it
/// the water level over them, as the game's own water volumes do, and lets the current carry
/// floating things (swimmers are carried by the character patch). The level told is the higher of the
/// flowing water and the game's own (the sea at a river's mouth): what is told last is what an object has, so
/// telling the flowing water's alone would take the sea from a swimmer or a fish between the sea's own tellings.
/// </summary>
internal sealed class WaterTrigger : MonoBehaviour
{
    private const float NoWater = -10000f;

    // The bodies the current pushed in this physics step: one inside several triggers (two bricks it straddles, or a
    // tile kept beside bricks) is pushed once.
    private static readonly HashSet<Rigidbody> Carried = [];

    private static float _carriedAt = -1f;

    private readonly List<IWaterInteractable> _inside = [];

    private static IWaterInteractable Interactable(Collider collider)
    {
        Rigidbody body = collider.attachedRigidbody;
        if (body == null)
        {
            return null;
        }

        return body.GetComponent<IWaterInteractable>();
    }

    private static bool Gone(IWaterInteractable interactable)
    {
        return interactable == null || (interactable is Object unityObject && unityObject == null);
    }

    private static void Carry(IWaterInteractable interactable, Transform target, Vector3 position, float surface)
    {
        if (interactable is Character || surface <= position.y)
        {
            return;
        }

        Rigidbody body = target.GetComponent<Rigidbody>();
        if (body == null || body.isKinematic || !VoxelWater.CurrentAt(position, out Vector3 current))
        {
            return;
        }

        if (Time.fixedTime != _carriedAt)
        {
            _carriedAt = Time.fixedTime;
            Carried.Clear();
        }

        if (!Carried.Add(body))
        {
            return;
        }

        body.AddForce(current * (body.mass * FlowingWaterPlugin.Settings.CurrentStrength.Value), ForceMode.Force);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnTriggerEnter(Collider other)
    {
        IWaterInteractable interactable = Interactable(other);
        if (interactable == null || _inside.Contains(interactable))
        {
            return;
        }

        interactable.Increment(LiquidType.Water);
        _inside.Add(interactable);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnTriggerExit(Collider other)
    {
        IWaterInteractable interactable = Interactable(other);
        if (interactable == null || !_inside.Remove(interactable))
        {
            return;
        }

        if (interactable.Decrement(LiquidType.Water) == 0)
        {
            interactable.SetLiquidLevel(NoWater, LiquidType.Water, this);
        }
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void FixedUpdate()
    {
        for (int i = _inside.Count - 1; i >= 0; i--)
        {
            IWaterInteractable interactable = _inside[i];
            if (Gone(interactable))
            {
                _inside.RemoveAt(i);
                continue;
            }

            Transform target = interactable.GetTransform();
            if (target == null)
            {
                _inside.RemoveAt(i);
                continue;
            }

            Vector3 position = target.position;
            float surface = VoxelWater.SurfaceAt(position);
            // Out of the flowing water but still in the box, an object is told the game's water there, or none.
            interactable.SetLiquidLevel(Mathf.Max(surface, Floating.GetLiquidLevel(position, 1f, LiquidType.Water)), LiquidType.Water, this);
            Carry(interactable, target, position, surface);
        }
    }

    // As a game water volume does: the count of waters an object is in is the game's, shared with the sea and every
    // other box, so only the last one it leaves says it has no water; any other still holding it tells its own level.
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        foreach (IWaterInteractable interactable in _inside)
        {
            if (!Gone(interactable) && interactable.Decrement(LiquidType.Water) == 0)
            {
                interactable.SetLiquidLevel(NoWater, LiquidType.Water, this);
            }
        }

        _inside.Clear();
    }
}
