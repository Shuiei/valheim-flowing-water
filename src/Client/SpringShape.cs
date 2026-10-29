using System;

namespace FlowingWater.Client;

/// <summary>
/// The shape of a source or drain in the volume water (VolumeWater.Spring): a source is a lens centred on the
/// ground, as wide as its rate needs and no higher over the ground than <see cref="Rise"/> (its cells are kept full of
/// water: a sphere of them would stand on the ground as a ball of water). Its size is that of a disc passing the rate
/// at <see cref="Speed"/> metres a second, never narrower than <see cref="LeastReach"/>; its water gushes out of its
/// rim, outward and a little up (the volume water's compute shader tilts it), as fast as the rim passes the rate
/// (<see cref="Outflow"/>: welling up over the lens, a big source's water stood a metre over the water round it to get
/// away, a dome). A drain's reach is never smaller than <see cref="LeastDrain"/>. Needs no game, so the unit tests
/// check the arithmetic.
/// </summary>
internal static class SpringShape
{
    /// <summary>About how fast the water over a source rises, m/s.</summary>
    public const float Speed = 1f;

    /// <summary>A source's lens at the least, metres round (a hemisphere then).</summary>
    public const float LeastReach = 0.4f;

    /// <summary>How high a source's lens stands over the ground at the most, metres.</summary>
    public const float Rise = 0.5f;

    /// <summary>A drain's reach at the least, metres.</summary>
    public const float LeastDrain = 0.3f;

    /// <summary>A source's lens for a rate (m³/s): the radius whose disc passes the rate at <see cref="Speed"/>, <see cref="LeastReach"/> at least.</summary>
    public static float Reach(float rate) => Math.Max(LeastReach, (float)Math.Sqrt(rate / (Math.PI * Speed)));

    /// <summary>How high a source's lens stands over the ground (its half-height): its reach, <see cref="Rise"/> at the most.</summary>
    public static float Height(float rate) => Math.Min(Reach(rate), Rise);

    /// <summary>
    /// The speed (m/s) the lens's rim (its side over the ground, 2 pi r h square metres) must give the rate at, outward
    /// across the ground.
    /// </summary>
    public static float Outflow(float rate)
    {
        return (float)(rate / (2.0 * Math.PI * Reach(rate) * Height(rate)));
    }

    /// <summary>A drain's reach for the radius asked: <see cref="LeastDrain"/> at least.</summary>
    public static float DrainReach(float radius) => Math.Max(radius, LeastDrain);
}
