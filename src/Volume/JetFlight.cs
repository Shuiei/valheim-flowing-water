using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// A source's jet in the air, as the player's game draws it wherever the camera sees it (the server pours its water
/// where it lands, and the water near the camera flies it only within its grid): its outlet cut into strips a voxel
/// wide, as the server's <see cref="VoxelGrid.PourOutlet"/> cuts it, each thrown the way the outlet sends it there at
/// the source's speed, fanning out by its share of the spread, and followed down till it meets something
/// (<see cref="Flight"/>). Needs no game, so the unit tests check the arithmetic.
/// </summary>
internal static class JetFlight
{
    /// <summary>The arc is looked along in steps this long, seconds.</summary>
    public const float Step = 0.05f;

    /// <summary>The longest flight followed, seconds, as the server's (where it got to then, still falling).</summary>
    public const float Longest = 6f;

    /// <summary>
    /// A strip of a jet: where it leaves the outlet, its velocity there (m/s), its width (m), how fast each of its sides
    /// moves out as it flies (m/s: its share of the spread), and the water it carries across its width (m²/s).
    /// </summary>
    public readonly struct Strip : IEquatable<Strip>
    {
        public Strip(Vec3 start, Vec3 velocity, float width, float widen, float flow)
        {
            Start = start;
            Velocity = velocity;
            Width = width;
            Widen = widen;
            Flow = flow;
        }

        public Vec3 Start { get; }

        public Vec3 Velocity { get; }

        public float Width { get; }

        public float Widen { get; }

        public float Flow { get; }

        public static bool operator ==(Strip a, Strip b) => a.Equals(b);

        public static bool operator !=(Strip a, Strip b) => !a.Equals(b);

        /// <summary>Where it is after so many seconds of flight.</summary>
        public Vec3 At(float t) => new(Start.X + (Velocity.X * t), Start.Y + (Velocity.Y * t) - (0.5f * VoxelSolver.Gravity * t * t), Start.Z + (Velocity.Z * t));

        public bool Equals(Strip other) => Start == other.Start && Velocity == other.Velocity && Width.Equals(other.Width) && Widen.Equals(other.Widen) && Flow.Equals(other.Flow);

        public override bool Equals(object obj) => obj is Strip other && Equals(other);

        public override int GetHashCode() => (Start, Velocity, Width, Widen, Flow).GetHashCode();
    }

    /// <summary>
    /// The strips of a source's jet, its outlet's middle at a point, giving a rate (m³/s) through a shape, cut a voxel
    /// wide; none for a source whose water does not fly (<see cref="SourceShape.Flies"/>).
    /// </summary>
    public static void Strips(Vec3 outlet, SourceShape shape, float rate, float voxel, List<Strip> into)
    {
        into.Clear();
        float speed = shape.Speed(rate);
        if (!shape.Flies || !(speed > 0f))
        {
            return;
        }

        (float rightX, float rightZ) = shape.Right;
        (float Across, float Offset)[] places = shape.Outlet(voxel);
        float width = shape.OutletWidth / places.Length;
        double flat = speed * Math.Cos(shape.Throw * Math.PI / 180.0);
        float widen = (float)(flat * Math.Tan(shape.Spread / places.Length / 2.0 * Math.PI / 180.0));
        foreach ((float across, float offset) in places)
        {
            Vec3 way = shape.Direction(across);
            into.Add(new Strip(new Vec3(outlet.X + (rightX * offset), outlet.Y, outlet.Z + (rightZ * offset)), way * speed, width, widen, rate / shape.OutletWidth));
        }
    }

    /// <summary>
    /// How long a strip flies, seconds: till the step its way meets something (<paramref name="meets"/>: from one place
    /// to the next, the share of the way along it where it meets something, null where nothing), <see cref="Longest"/>
    /// at the most (still falling then: it does not land).
    /// </summary>
    public static float Flight(Strip strip, Func<Vec3, Vec3, float?> meets, out bool lands)
    {
        Vec3 from = strip.Start;
        for (float t = Step; t <= Longest + (Step / 2f); t += Step)
        {
            Vec3 to = strip.At(t);
            if (meets(from, to) is { } share)
            {
                lands = true;
                return Math.Max(0f, t - Step + (Math.Min(1f, Math.Max(0f, share)) * Step));
            }

            from = to;
        }

        lands = false;
        return Longest;
    }
}
