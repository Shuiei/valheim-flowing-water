using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using HarmonyLib;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The brick's trigger box on the game's water layer: tells characters, items and ships inside it
/// the water level over them, as the game's own water volumes do, and lets the current carry
/// floating things toward its speed, light ones quickly and boats slowly (<see cref="CurrentCarry"/>; swimmers are
/// carried by the character patch). The level told is the higher of the
/// flowing water and the game's own (the sea at a river's mouth): what is told last is what an object has, so
/// telling the flowing water's alone would take the sea from a swimmer or a fish between the sea's own tellings.
/// Its name must be no class of the game's: Unity finds a component added in code again by its class name alone when
/// the game copies its object, so one named as the game's own WaterTrigger (splash effects) became that on every brick.
/// </summary>
internal sealed class BrickTrigger : MonoBehaviour
{
    // The bodies the current pushed in this physics step: one inside several triggers (two bricks it straddles, or a
    // tile kept beside bricks) is pushed once.
    private static readonly HashSet<Rigidbody> Carried = [];

    private static float _carriedAt = -1f;

    // The share of a floating thing's speed the game's own drag takes each physics step in water (Floating.m_damping),
    // looked up once; without it (a game update), things are carried without it given back, and so are slower.
    private static readonly AccessTools.FieldRef<Floating, float> DampingField = Damping();

    // What is in the box, each with the body it came in on (looked up once, not each physics step).
    private readonly List<(IWaterInteractable Interactable, Rigidbody Body)> _inside = [];

    private static AccessTools.FieldRef<Floating, float> Damping()
    {
        if (AccessTools.Field(typeof(Floating), "m_damping") is { } field && field.FieldType == typeof(float))
        {
            return AccessTools.FieldRefAccess<Floating, float>(field);
        }

        FlowingWaterPlugin.Log.LogError("The game's Floating.m_damping was not found (a game update?): the current carries floating things slower than the water");
        return null;
    }

    private static IWaterInteractable Interactable(Collider collider, out Rigidbody body)
    {
        body = collider.attachedRigidbody;
        return body?.GetComponent<IWaterInteractable>();
    }

    private static bool Gone(IWaterInteractable interactable)
    {
        return interactable == null || (interactable is Object unityObject && unityObject == null);
    }

    private static void Carry(IWaterInteractable interactable, Rigidbody body, Vector3 position, float surface)
    {
        if (interactable is Character || surface <= position.y)
        {
            return;
        }

        if (body == null || body.isKinematic || !VoxelWater.CurrentAt(position, out Vector3 current))
        {
            return;
        }

        // Only the game simulating the thing carries it: the others see where it goes (Unity's own null check, so
        // ?. would not do).
        ZNetView view = body.GetComponentInParent<ZNetView>();
        if (view != null && view.IsValid() && !view.IsOwner())
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

        Vector3 velocity = body.linearVelocity;
        float drag = interactable is Floating floating && DampingField != null ? CurrentCarry.DragRate(DampingField(floating), Time.fixedDeltaTime) : 0f;
        float braking = CurrentCarry.Braking(current.x, current.z, velocity.x, velocity.z, 1f, 0f, drag, drag);
        (float x, float z) = CurrentCarry.Pull(current.x, current.z, velocity.x, velocity.z, body.mass, FlowingWaterPlugin.Settings.CurrentStrength.Value, braking);
        body.AddForce(new Vector3(x, 0f, z), ForceMode.Acceleration);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnTriggerEnter(Collider other)
    {
        IWaterInteractable interactable = Interactable(other, out Rigidbody body);
        if (interactable == null || Index(interactable) >= 0)
        {
            return;
        }

        interactable.Increment(LiquidType.Water);
        _inside.Add((interactable, body));
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnTriggerExit(Collider other)
    {
        IWaterInteractable interactable = Interactable(other, out _);
        int at = interactable == null ? -1 : Index(interactable);
        if (at < 0)
        {
            return;
        }

        _inside.RemoveAt(at);
        if (interactable.Decrement(LiquidType.Water) == 0)
        {
            interactable.SetLiquidLevel(VoxelWater.NoWater, LiquidType.Water, this);
        }
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void FixedUpdate()
    {
        for (int i = _inside.Count - 1; i >= 0; i--)
        {
            (IWaterInteractable interactable, Rigidbody body) = _inside[i];
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
            // Out of the flowing water but still in the box, an object is told the game's water there, or none. Under
            // the flowing water, that is the level: the sea takes the water at its own level, so flowing water stands
            // over it wherever there is any (the game's level, which the patches make ours too, is not asked again).
            float level = surface > position.y ? surface : Mathf.Max(surface, Floating.GetLiquidLevel(position, 1f, LiquidType.Water));
            interactable.SetLiquidLevel(level, LiquidType.Water, this);
            Carry(interactable, body, position, surface);
        }
    }

    // Where this one is in the box's list, or -1.
    private int Index(IWaterInteractable interactable)
    {
        for (int i = 0; i < _inside.Count; i++)
        {
            if (ReferenceEquals(_inside[i].Interactable, interactable))
            {
                return i;
            }
        }

        return -1;
    }

    // As a game water volume does: the count of waters an object is in is the game's, shared with the sea and every
    // other box, so only the last one it leaves says it has no water; any other still holding it tells its own level.
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        foreach ((IWaterInteractable interactable, _) in _inside)
        {
            if (!Gone(interactable) && interactable.Decrement(LiquidType.Water) == 0)
            {
                interactable.SetLiquidLevel(VoxelWater.NoWater, LiquidType.Water, this);
            }
        }

        _inside.Clear();
    }
}
