using System;

namespace FlowingWater.Client;

/// <summary>
/// How the current carries a floating thing, without the game: along the current, toward the water's speed, as fast as
/// the water pulls a thing of that mass. A light thing (an item dropped, a crate) catches up with the water within a
/// second; a boat takes several (the water's drag grows with a thing's surface, its mass with its volume, so the time
/// grows as the cube root of the mass). The water only pulls a thing slower than itself along the current: it holds
/// back a boat sailing up it, and never brakes one sailing down it faster (the game's own water drag does). The game's
/// own drag brakes a floating thing toward standing still, not toward the water's speed: what it takes along the
/// current is given back while the thing is carried, so that it drags against the water, and a crate comes to run as
/// fast as the river instead of a third as fast. (A ship keeps the game's own drag: ShipCurrent.)
/// </summary>
internal static class CurrentCarry
{
    /// <summary>How long a thing of 1 kg takes to catch up with the water, seconds (by 63 %).</summary>
    public const float KiloSeconds = 0.5f;

    /// <summary>The quickest and slowest any thing catches up, seconds.</summary>
    public const float QuickestSeconds = 0.25f, SlowestSeconds = 10f;

    /// <summary>How long a thing of this mass takes to catch up with the water, seconds (by 63 %).</summary>
    /// <param name="mass">Kilograms.</param>
    public static float Seconds(float mass) =>
        Math.Min(SlowestSeconds, Math.Max(QuickestSeconds, KiloSeconds * (float)Math.Pow(Math.Max(0f, mass), 1.0 / 3.0)));

    /// <summary>
    /// The game's own drag on a floating thing as a rate, per second, from the share of its speed it takes each physics
    /// step: 0.05 at 50 steps a second is 2.56.
    /// </summary>
    /// <param name="shareAStep">The share taken each step, 0 to under 1.</param>
    /// <param name="step">A physics step, seconds.</param>
    public static float DragRate(float shareAStep, float step) =>
        shareAStep > 0f && shareAStep < 1f && step > 0f ? (float)(-Math.Log(1.0 - shareAStep) / step) : 0f;

    /// <summary>
    /// The braking the game's own drag gives a thing along the current, metres a second squared: its drag on each of its
    /// axes across the ground (<see cref="DragRate"/>, per second; a floating thing's the same both ways), on its speed
    /// along that axis, brought onto the current's way. None where it brakes the thing against the current (it runs up
    /// it).
    /// </summary>
    /// <param name="currentX">The water's velocity east, metres a second.</param>
    /// <param name="currentZ">North.</param>
    /// <param name="bodyX">The thing's velocity east.</param>
    /// <param name="bodyZ">North.</param>
    /// <param name="forwardX">The thing's forward axis across the ground, east (any length but 0).</param>
    /// <param name="forwardZ">North.</param>
    /// <param name="forwardDrag">Its drag along that axis, per second.</param>
    /// <param name="sidewaysDrag">Across it, per second.</param>
    public static float Braking(float currentX, float currentZ, float bodyX, float bodyZ, float forwardX, float forwardZ, float forwardDrag, float sidewaysDrag)
    {
        float speed = (float)Math.Sqrt((currentX * currentX) + (currentZ * currentZ));
        float length = (float)Math.Sqrt((forwardX * forwardX) + (forwardZ * forwardZ));
        if (!(speed > 0f) || !(length > 0f))
        {
            return 0f;
        }

        float ux = currentX / speed, uz = currentZ / speed, fx = forwardX / length, fz = forwardZ / length;
        float forward = (bodyX * fx) + (bodyZ * fz), sideways = (bodyX * fz) - (bodyZ * fx);
        float onCurrent = (forwardDrag * forward * ((fx * ux) + (fz * uz))) + (sidewaysDrag * sideways * ((fz * ux) - (fx * uz)));
        return Math.Max(0f, onCurrent);
    }

    /// <summary>
    /// The acceleration the current gives a thing, metres a second squared across the ground: along the current, what
    /// the thing lacks of the water's speed (times <paramref name="strength"/>) over its <see cref="Seconds"/>, and the
    /// game's own drag's braking along it given back (<paramref name="braking"/>, <see cref="Braking"/>); none where
    /// it runs along the current as fast or faster, and none across it.
    /// </summary>
    /// <param name="currentX">The water's velocity east, metres a second.</param>
    /// <param name="currentZ">North.</param>
    /// <param name="bodyX">The thing's velocity east.</param>
    /// <param name="bodyZ">North.</param>
    /// <param name="mass">Kilograms.</param>
    /// <param name="strength">The share of the water's speed it is carried to (1: the water's own).</param>
    /// <param name="braking">The game's own drag's braking on it along the current, metres a second squared.</param>
    public static (float X, float Z) Pull(float currentX, float currentZ, float bodyX, float bodyZ, float mass, float strength, float braking)
    {
        float speed = (float)Math.Sqrt((currentX * currentX) + (currentZ * currentZ));
        if (!(speed > 0f) || !(strength > 0f))
        {
            return (0f, 0f);
        }

        float ux = currentX / speed, uz = currentZ / speed;
        float along = (bodyX * ux) + (bodyZ * uz);
        float lacking = (speed * strength) - along;
        if (!(lacking > 0f))
        {
            return (0f, 0f);
        }

        float pull = (lacking / Seconds(mass)) + Math.Max(0f, braking);
        return (ux * pull, uz * pull);
    }
}
