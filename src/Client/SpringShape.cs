using System;

namespace FlowingWater.Client;

/// <summary>
/// The shape of a source or drain in the volume water (VolumeWater.Spring): a source's water comes out
/// of a sphere round it at about <see cref="Speed"/> metres a second, so the sphere grows with the rate (its upper
/// half, over the ground, passes the rate at that speed), never smaller than <see cref="LeastReach"/>; a drain's reach
/// is never smaller than <see cref="LeastDrain"/>. Needs no game, so the unit tests check the arithmetic.
/// </summary>
internal static class SpringShape
{
    /// <summary>About how fast a source's water comes out of its sphere, m/s.</summary>
    public const float Speed = 3f;

    /// <summary>A source's sphere at the least, metres.</summary>
    public const float LeastReach = 0.4f;

    /// <summary>A drain's reach at the least, metres.</summary>
    public const float LeastDrain = 0.3f;

    /// <summary>A source's sphere for a rate (m³/s): the radius whose upper half passes the rate at <see cref="Speed"/>, <see cref="LeastReach"/> at least.</summary>
    public static float Reach(float rate) => Math.Max(LeastReach, (float)Math.Sqrt(rate / (2.0 * Math.PI * Speed)));

    /// <summary>The speed (m/s) the whole sphere of <see cref="Reach"/> must give the rate at: half <see cref="Speed"/> for any rate the sphere is not held up to <see cref="LeastReach"/> for.</summary>
    public static float Outflow(float rate)
    {
        float reach = Reach(rate);
        return (float)(rate / (4.0 * Math.PI * reach * reach));
    }

    /// <summary>A drain's reach for the radius asked: <see cref="LeastDrain"/> at least.</summary>
    public static float DrainReach(float radius) => Math.Max(radius, LeastDrain);
}
