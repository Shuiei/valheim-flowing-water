using System;

namespace FlowingWater.Server;

/// <summary>
/// How running water wears the ground away and lays it down again, without the game. Ground carried by the
/// water (its sediment) is counted as a height over the cell, in metres: the ground it was taken from. Fast
/// water can carry more than slow (its capacity); carrying less, it picks ground up, carrying more it sets
/// the rest down. Over time a stream cuts its own bed and silts up where it slows.
/// </summary>
internal static class ErosionRules
{
    /// <summary>Water slower than this, metres per second, carries nothing.</summary>
    public const float MinSpeed = 0.6f;

    /// <summary>The most the ground may be cut or raised, metres: the game's own limit on terrain changes.</summary>
    public const float MaxChange = 8f;

    // Metres of ground carried per metre of water depth, per square metre per second squared of speed above
    // MinSpeed; depth counts up to a metre (a deep pool is no stronger than a deep stream).
    private const float CapacityScale = 0.02f;

    private const float CountedDepth = 1f;

    // Shares of the gap to its capacity that water picks up, or sets down, each second.
    private const float PickUp = 0.005f, SetDown = 0.2f;

    // Shallower than this, water is taken as gone: whatever it carried is set down.
    private const float MinDepth = 0.0005f;

    // The steepest bank loose ground holds (rise over run: 40 degrees), and the share of the excess that slides
    // each second; a slope steeper by less than a millimetre is left.
    private const float Talus = 0.84f, SlideRate = 0.05f, MinExcess = 0.001f;

    /// <summary>Ground carried per metre of water (its sediment over its depth); none where there is no water.</summary>
    /// <param name="sediment">Ground carried, metres over the cell.</param>
    /// <param name="depth">The water's depth, metres.</param>
    public static float Concentration(float sediment, float depth) => depth > MinDepth ? sediment / depth : 0f;

    /// <summary>How much ground water this fast and deep can carry, metres over the cell.</summary>
    /// <param name="speed">Metres per second.</param>
    /// <param name="depth">Metres.</param>
    public static float Capacity(float speed, float depth)
    {
        float excess = speed - MinSpeed;
        return excess <= 0f || depth <= MinDepth ? 0f : CapacityScale * Math.Min(depth, CountedDepth) * excess * excess;
    }

    /// <summary>
    /// How much ground the water takes from the cell over a time (positive: the ground is cut) or sets down on it
    /// (negative: raised), never more than it carries, and never taking the ground past <see cref="MaxChange"/>
    /// from where it was.
    /// </summary>
    /// <param name="sediment">Ground carried, metres over the cell.</param>
    /// <param name="capacity">What the water could carry (<see cref="Capacity"/>).</param>
    /// <param name="change">How far the ground there has been cut (negative) or raised so far, metres.</param>
    /// <param name="dt">Seconds (times the erosion speed).</param>
    public static float Exchange(float sediment, float capacity, float change, float dt)
    {
        float taken = sediment < capacity
            ? Math.Min(PickUp * (capacity - sediment) * dt, capacity - sediment)
            : -Math.Min(SetDown * (sediment - capacity) * dt, sediment);
        return Math.Max(change - MaxChange, Math.Min(change + MaxChange, taken));
    }

    /// <summary>
    /// How much ground slides from one cell to its neighbour over a time (negative: the other way), where erosion
    /// left the bank between them steeper than loose ground holds: the slope it had before erosion, or the
    /// steepest bank (40 degrees) where that was gentler. Natural cliffs, and a stream bed cut evenly down a
    /// steep slope, stay as they are; a channel's walls fall in until they hold. What slides is never more than
    /// half what erosion moved one cell against the other, so neither goes past <see cref="MaxChange"/>.
    /// </summary>
    /// <param name="a">The first cell's ground, metres.</param>
    /// <param name="aChange">How far erosion has moved it so far, metres.</param>
    /// <param name="b">The neighbour's ground.</param>
    /// <param name="bChange">How far erosion has moved it.</param>
    /// <param name="distance">Between the two cells' centres, metres.</param>
    /// <param name="dt">Seconds (times the erosion speed).</param>
    public static float Slide(float a, float aChange, float b, float bChange, float distance, float dt)
    {
        if (a < b)
        {
            return -Slide(b, bChange, a, aChange, distance, dt);
        }

        float held = Math.Max(Talus * distance, a - aChange - (b - bChange));
        float excess = a - b - held;
        if (excess <= MinExcess)
        {
            return 0f;
        }

        // Half the excess evens the two out.
        return excess * 0.5f * Math.Min(1f, SlideRate * dt);
    }
}
