using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// Where running water is heard, without the game: the places of a brick's water that run fast or fall (each with how
/// much water passes there and how steeply it runs), and the few of them round the listener that sound, the strongest
/// first, never two close together.
/// </summary>
internal static class SoundSpots
{
    /// <summary>Water is heard where it is this deep (metres) and runs this fast (metres a second), falling or not.</summary>
    public const float MinDepth = 0.03f, MinSpeed = 1.2f;

    /// <summary>At most this many places sound at once.</summary>
    public const int Max = 8;

    /// <summary>At most this many places a brick gives.</summary>
    public const int PerBrick = 4;

    /// <summary>Never two places closer than this, metres.</summary>
    public const float Spacing = 6f;

    /// <summary>The steepest a place runs, falling over running across (a fall straight down is this steep).</summary>
    public const float Steepest = 4f;

    // Across slower than this, water falling is as steep as it falls over this (a fall straight down is not endless).
    private const float SlowestAcross = 0.3f;

    /// <summary>Whether water this deep running this fast is heard.</summary>
    /// <param name="depth">Metres.</param>
    /// <param name="speed">Metres a second, all three ways.</param>
    public static bool Qualifies(float depth, float speed) => depth >= MinDepth && speed >= MinSpeed;

    /// <summary>How steeply water runs: how fast it falls over how fast it runs across, at most <see cref="Steepest"/>.</summary>
    /// <param name="across">Metres a second across the ground.</param>
    /// <param name="up">Metres a second up (falling, below 0).</param>
    public static float Steep(float across, float up) => Math.Min(Steepest, Math.Max(0f, -up) / Math.Max(across, SlowestAcross));

    /// <summary>How strong a place is: how much water passes (speed times depth), more where it runs steep.</summary>
    /// <param name="flow">Speed times depth, square metres a second.</param>
    /// <param name="steep">How steeply it runs (<see cref="Steep"/>).</param>
    public static float Score(float flow, float steep) => flow * (1f + (2f * steep));

    /// <summary>
    /// A brick's places: of its voxels with water deep enough to draw, those running fast enough (<see cref="Qualifies"/>,
    /// the voxel's water its depth), at the top of their water there, the strongest <see cref="PerBrick"/> spaced
    /// apart; the water's speed is its own (<see cref="VoxelField.WaterVelocity"/>: a fall through the air a tenth of a
    /// voxel thick runs ten times as fast as the flow the server sends says).
    /// </summary>
    public static List<Spot> Of(BrickView view)
    {
        var candidates = new List<Spot>();
        float s = view.VoxelSize;
        for (int i = 0; i < Brick.Count; i++)
        {
            float depth = view.Water[i] * s;
            if (!VoxelField.IsWet(view, i) || !(depth > Drawing.HideDepth))
            {
                continue;
            }

            (float vx, float vy, float vz) = VoxelField.WaterVelocity(view, i);
            float across = (float)Math.Sqrt((vx * vx) + (vz * vz));
            float speed = (float)Math.Sqrt((across * across) + (vy * vy));
            if (!Qualifies(depth, speed))
            {
                continue;
            }

            (int lx, int ly, int lz) = Brick.Place(i);
            int x = (view.X * Brick.Size) + lx, y = (view.Y * Brick.Size) + ly, z = (view.Z * Brick.Size) + lz;
            float level = (y + Math.Min(1f, view.Solid[i] + view.Water[i])) * s;
            candidates.Add(new Spot((x + 0.5f) * s, level, (z + 0.5f) * s, speed * depth, Steep(across, vy)));
        }

        var chosen = new List<Spot>(PerBrick);
        Pick(candidates, null, float.PositiveInfinity, PerBrick, chosen);
        return chosen;
    }

    /// <summary>
    /// The places that sound among these: within <paramref name="far"/> metres of the listener (none given: anywhere),
    /// the strongest first, each at least <see cref="Spacing"/> from those already chosen, at most
    /// <paramref name="most"/>. The candidates are sorted in place.
    /// </summary>
    public static void Pick(List<Spot> candidates, (float X, float Y, float Z)? ear, float far, int most, List<Spot> chosen)
    {
        chosen.Clear();
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        foreach (Spot spot in candidates)
        {
            if (chosen.Count >= most)
            {
                break;
            }

            if ((ear is not { } e || spot.DistanceSquared(e.X, e.Y, e.Z) <= far * far)
                && chosen.TrueForAll(c => c.DistanceSquared(spot.X, spot.Y, spot.Z) >= Spacing * Spacing))
            {
                chosen.Add(spot);
            }
        }
    }
}

/// <summary>A place where water is heard: its surface point (world metres), how much water passes and how steeply it runs.</summary>
internal readonly record struct Spot
{
    public Spot(float x, float y, float z, float flow, float steep)
    {
        X = x;
        Y = y;
        Z = z;
        Flow = flow;
        Steep = steep;
    }

    public float X { get; }

    public float Y { get; }

    public float Z { get; }

    /// <summary>Speed times depth, square metres a second.</summary>
    public float Flow { get; }

    /// <summary>How steeply it runs (<see cref="SoundSpots.Steep"/>).</summary>
    public float Steep { get; }

    /// <summary>How strong it is (<see cref="SoundSpots.Score"/>).</summary>
    public float Score => SoundSpots.Score(Flow, Steep);

    /// <summary>The square of its distance to a point, metres.</summary>
    public float DistanceSquared(float x, float y, float z) => ((X - x) * (X - x)) + ((Y - y) * (Y - y)) + ((Z - z) * (Z - z));
}
