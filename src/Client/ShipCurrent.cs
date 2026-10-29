using System.Collections.Generic;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The current carrying ships (<see cref="CurrentCarry"/>): the game's ships float on the flowing water through its
/// water-level checks, but are no thing its water boxes tell of (<see cref="BrickTrigger"/>), so each physics step every
/// ship this game simulates (its owner) with someone aboard, and the flowing water holds, is pulled toward the water's
/// speed at its waterline, as slowly as its mass makes it, against the game's own drag on it (strong sideways, light
/// along its hull: a ship lying across a river drifts down it slowly, one pointing down it nearly as fast as the
/// water). A ship with no one aboard the game holds where it is, on the sea as on a river (it loses nine tenths of any
/// speed each physics step): it is left moored. The ships are looked for again every two seconds.
/// </summary>
internal static class ShipCurrent
{
    // How often the ships are looked for, seconds.
    private const float LookSeconds = 2f;

    // How far under the surface the current a ship is in is read, metres: its hull's, not the film over it.
    private const float Under = 0.3f;

    // How far under its own place a ship's waterline may be, metres (a ship's place is a little over the water).
    private const float Reach = 1.5f;

    private static readonly FaultGate Gate = new(
        "The current on ships failed; it carries no ship until a world opens",
        FlowingWaterPlugin.Log.LogError,
        Forget);

    private static readonly List<Ship> Ships = [];

    private static float _lookedAt = float.NegativeInfinity;

    /// <summary>Each physics step: the ships in the flowing water carried by its current.</summary>
    public static void Tick() => Gate.Run(TickOn);

    /// <summary>A world opens: the last one's ships are gone, and off after a fault, it has a fresh try.</summary>
    public static void WorldOpened()
    {
        Forget();
        Gate.Reset();
    }

    private static void TickOn()
    {
        if (VoxelWater.Field.Bricks.Count == 0)
        {
            return;
        }

        if (Time.time - _lookedAt >= LookSeconds)
        {
            _lookedAt = Time.time;
            Ships.Clear();
            Ships.AddRange(Object.FindObjectsByType<Ship>(FindObjectsSortMode.None));
        }

        float strength = FlowingWaterPlugin.Settings.CurrentStrength.Value;
        foreach (Ship ship in Ships)
        {
            // (Unity's own null checks: a ship gone is not null to C#.)
            if (ship == null)
            {
                continue;
            }

            Rigidbody body = ship.GetComponent<Rigidbody>();
            ZNetView view = ship.GetComponent<ZNetView>();
            if (body == null || body.isKinematic || view == null || !view.IsValid() || !view.IsOwner() || !ship.HasPlayerOnboard())
            {
                continue;
            }

            Vector3 at = body.position;
            float surface = VoxelWater.SurfaceAt(at);
            if (surface == VoxelWater.NoWater)
            {
                surface = VoxelWater.SurfaceAt(at + (Vector3.down * Reach));
            }

            if (surface == VoxelWater.NoWater || surface < at.y - Reach || !VoxelWater.CurrentAt(new Vector3(at.x, surface - Under, at.z), out Vector3 current))
            {
                continue;
            }

            Vector3 velocity = body.linearVelocity;
            (float x, float z) = CurrentCarry.Pull(current.x, current.z, velocity.x, velocity.z, body.mass, strength, 0f);
            body.AddForce(new Vector3(x, 0f, z), ForceMode.Acceleration);
        }
    }

    private static void Forget()
    {
        Ships.Clear();
        _lookedAt = float.NegativeInfinity;
    }
}
