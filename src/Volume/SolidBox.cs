using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>A point or a direction in world metres, without the game.</summary>
internal readonly struct Vec3 : IEquatable<Vec3>
{
    /// <summary>A point or direction (x east, y up, z north).</summary>
    public Vec3(float x, float y, float z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>East, metres.</summary>
    public float X { get; }

    /// <summary>Up, metres.</summary>
    public float Y { get; }

    /// <summary>North, metres.</summary>
    public float Z { get; }

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

    public static Vec3 operator *(Vec3 a, float k) => new(a.X * k, a.Y * k, a.Z * k);

    public static bool operator ==(Vec3 a, Vec3 b) => a.Equals(b);

    public static bool operator !=(Vec3 a, Vec3 b) => !a.Equals(b);

    /// <inheritdoc/>
    public bool Equals(Vec3 other) => X.Equals(other.X) && Y.Equals(other.Y) && Z.Equals(other.Z);

    /// <inheritdoc/>
    public override bool Equals(object obj) => obj is Vec3 other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => unchecked((((X.GetHashCode() * 397) ^ Y.GetHashCode()) * 397) ^ Z.GetHashCode());

    /// <summary>The dot product.</summary>
    public float Dot(Vec3 other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);
}

/// <summary>
/// A solid box in the world, turned any way, without the game: a box collider, or the box round another collider
/// whose shape cannot be read (<see cref="Solid"/>).
/// </summary>
internal sealed class SolidBox : Solid
{
    /// <summary>A box round its centre, with its three axes (of length 1) and its half sizes along them.</summary>
    public SolidBox(Vec3 centre, Vec3 axisX, Vec3 axisY, Vec3 axisZ, Vec3 half)
    {
        Centre = centre;
        AxisX = axisX;
        AxisY = axisY;
        AxisZ = axisZ;
        Half = half;
    }

    /// <summary>Its centre, world metres.</summary>
    public Vec3 Centre { get; }

    /// <summary>Its own x axis in the world (length 1).</summary>
    public Vec3 AxisX { get; }

    /// <summary>Its own y axis.</summary>
    public Vec3 AxisY { get; }

    /// <summary>Its own z axis.</summary>
    public Vec3 AxisZ { get; }

    /// <summary>Half its size along each of its own axes, metres.</summary>
    public Vec3 Half { get; }

    /// <summary>A box along the world's axes, from one corner to the other.</summary>
    public static SolidBox Between(Vec3 min, Vec3 max) =>
        new((min + max) * 0.5f, new Vec3(1f, 0f, 0f), new Vec3(0f, 1f, 0f), new Vec3(0f, 0f, 1f), (max - min) * 0.5f);

    /// <summary>Whether the point is in the box (its surface included).</summary>
    public bool Contains(Vec3 point)
    {
        Vec3 d = point - Centre;
        return Math.Abs(d.Dot(AxisX)) <= Half.X && Math.Abs(d.Dot(AxisY)) <= Half.Y && Math.Abs(d.Dot(AxisZ)) <= Half.Z;
    }

    /// <inheritdoc/>
    public override bool Crosses(Vec3 from, Vec3 to) => Span(from, to, out _, out _);

    /// <inheritdoc/>
    public override void Column(float x, float z, float bottom, float top, List<(float Low, float High)> spans)
    {
        if (Span(new Vec3(x, bottom, z), new Vec3(x, top, z), out float enter, out float leave))
        {
            spans.Add((bottom + (enter * (top - bottom)), bottom + (leave * (top - bottom))));
        }
    }

    /// <summary>
    /// Whether the segment from one point to the other passes through the box, and the part of it inside (0 at
    /// the first point, 1 at the second).
    /// </summary>
    public bool Span(Vec3 from, Vec3 to, out float enter, out float leave)
    {
        Vec3 start = from - Centre;
        Vec3 along = to - from;
        enter = 0f;
        leave = 1f;
        return Slab(start.Dot(AxisX), along.Dot(AxisX), Half.X, ref enter, ref leave)
            && Slab(start.Dot(AxisY), along.Dot(AxisY), Half.Y, ref enter, ref leave)
            && Slab(start.Dot(AxisZ), along.Dot(AxisZ), Half.Z, ref enter, ref leave);
    }

    /// <summary>
    /// Whether it may meet the box along the world's axes from <paramref name="min"/> to <paramref name="max"/>: false
    /// only when one of its own axes, or the world's, separates them.
    /// </summary>
    public override bool Overlaps(Vec3 min, Vec3 max)
    {
        Vec3 half = (max - min) * 0.5f;
        Vec3 d = ((min + max) * 0.5f) - Centre;
        return Apart(d, half, AxisX, Half.X) && Apart(d, half, AxisY, Half.Y) && Apart(d, half, AxisZ, Half.Z)
            && Math.Abs(d.X) <= half.X + Reach(AxisX.X, AxisY.X, AxisZ.X)
            && Math.Abs(d.Y) <= half.Y + Reach(AxisX.Y, AxisY.Y, AxisZ.Y)
            && Math.Abs(d.Z) <= half.Z + Reach(AxisX.Z, AxisY.Z, AxisZ.Z);
    }

    /// <inheritdoc/>
    public override (Vec3 Min, Vec3 Max) Bounds()
    {
        var reach = new Vec3(
            Reach(AxisX.X, AxisY.X, AxisZ.X),
            Reach(AxisX.Y, AxisY.Y, AxisZ.Y),
            Reach(AxisX.Z, AxisY.Z, AxisZ.Z));
        return (Centre - reach, Centre + reach);
    }

    private float Reach(float x, float y, float z) => (Math.Abs(x) * Half.X) + (Math.Abs(y) * Half.Y) + (Math.Abs(z) * Half.Z);

    // Whether the box along the world's axes (d from this box's centre to its, half its size) and this one overlap
    // seen along one of this box's axes.
    private static bool Apart(Vec3 d, Vec3 half, Vec3 axis, float own) =>
        Math.Abs(d.Dot(axis)) <= own + (Math.Abs(axis.X) * half.X) + (Math.Abs(axis.Y) * half.Y) + (Math.Abs(axis.Z) * half.Z);

    // One axis of the slab test: narrows the part of the segment (enter to leave, 0 to 1) inside -half..half.
    private static bool Slab(float start, float along, float half, ref float enter, ref float leave)
    {
        if (Math.Abs(along) < 1e-9f)
        {
            return Math.Abs(start) <= half;
        }

        float a = (-half - start) / along;
        float b = (half - start) / along;
        enter = Math.Max(enter, Math.Min(a, b));
        leave = Math.Min(leave, Math.Max(a, b));
        return enter <= leave;
    }
}
