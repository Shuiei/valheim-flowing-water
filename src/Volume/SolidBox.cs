using System;

namespace FlowingWater.Volume;

/// <summary>A point or a direction in world metres, without the game.</summary>
internal readonly struct Vec3
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

    /// <summary>The dot product.</summary>
    public float Dot(Vec3 other) => (X * other.X) + (Y * other.Y) + (Z * other.Z);
}

/// <summary>
/// A solid box in the world, turned any way, without the game: a building piece's collider. Water cannot be in a
/// voxel whose middle it holds, nor pass between two voxels whose middles it separates.
/// </summary>
internal readonly struct SolidBox
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

    /// <summary>Whether the segment from one point to the other passes through the box.</summary>
    public bool Crosses(Vec3 from, Vec3 to)
    {
        Vec3 start = from - Centre;
        Vec3 along = to - from;
        float enter = 0f;
        float leave = 1f;
        return Slab(start.Dot(AxisX), along.Dot(AxisX), Half.X, ref enter, ref leave)
            && Slab(start.Dot(AxisY), along.Dot(AxisY), Half.Y, ref enter, ref leave)
            && Slab(start.Dot(AxisZ), along.Dot(AxisZ), Half.Z, ref enter, ref leave);
    }

    /// <summary>The smallest box along the world's axes holding it: its lowest and highest corners.</summary>
    public (Vec3 Min, Vec3 Max) Bounds()
    {
        var reach = new Vec3(
            Reach(AxisX.X, AxisY.X, AxisZ.X),
            Reach(AxisX.Y, AxisY.Y, AxisZ.Y),
            Reach(AxisX.Z, AxisY.Z, AxisZ.Z));
        return (Centre - reach, Centre + reach);
    }

    private float Reach(float x, float y, float z) => (Math.Abs(x) * Half.X) + (Math.Abs(y) * Half.Y) + (Math.Abs(z) * Half.Z);

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
