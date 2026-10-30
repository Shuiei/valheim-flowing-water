using System;
using System.Globalization;

namespace FlowingWater.Recorder;

/// <summary>What a run's steps say in numbers: their points, a glide's target and speed, and a place on the ground.</summary>
internal static class StepWords
{
    /// <summary>A point's height at this or lower: on the ground there (a place whose height is not known before the run).</summary>
    public const float OnTheGround = -9999f;

    /// <summary>How far over the ground a point on it is put (the player's feet, a source's foot).</summary>
    public const float OverGround = 0.05f;

    /// <summary>Numbers apart by commas, each a finite number; false if any is not.</summary>
    public static bool Numbers(string text, out float[] values)
    {
        string[] parts = text.Split(',');
        values = new float[parts.Length];
        for (int i = 0; i < parts.Length; i++)
        {
            if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]) || float.IsNaN(values[i]) || float.IsInfinity(values[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>"x,z,speed" of "!glide": where to and how fast (m/s, over 0); false for anything else.</summary>
    public static bool Glide(string text, out (float X, float Z, float Speed) glide)
    {
        glide = default;
        if (!Numbers(text, out float[] g) || g.Length != 3 || !(g[2] > 0f))
        {
            return false;
        }

        glide = (g[0], g[1], g[2]);
        return true;
    }

    /// <summary>
    /// A point's height: <see cref="OverGround"/> over the ground there (<paramref name="ground"/>, null where it is not
    /// known yet) when it says <see cref="OnTheGround"/>, else its own (and its own where the ground is not known).
    /// </summary>
    public static float Height(float y, float? ground) => y <= OnTheGround && ground is { } g ? g + OverGround : y;

    /// <summary>
    /// A glide's next place from (x, z) toward its target, at most <paramref name="step"/> metres on; whether it is there.
    /// </summary>
    public static (float X, float Z, bool There) GlideStep(float x, float z, float toX, float toZ, float step)
    {
        float dx = toX - x, dz = toZ - z, left = (float)Math.Sqrt((dx * dx) + (dz * dz));
        if (left <= step || !(left > 0f))
        {
            return (toX, toZ, true);
        }

        return (x + (dx / left * step), z + (dz / left * step), false);
    }
}
