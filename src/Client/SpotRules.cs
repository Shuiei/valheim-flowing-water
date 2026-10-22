using System.Collections.Generic;

namespace FlowingWater.Client;

/// <summary>
/// Where a tile's water turns white, without the game: which cells qualify, how strong each is, and which
/// of them get the sound and spray (the strongest first, never two close together, a few at most).
/// </summary>
internal static class SpotRules
{
    /// <summary>White water where the water is this deep (metres) and runs this fast (metres per second).</summary>
    public const float MinDepth = 0.03f, MinSpeed = 1.2f;

    /// <summary>At most this many places a tile.</summary>
    public const int Max = 6;

    /// <summary>Never two places closer than this, metres (across the ground).</summary>
    public const float Spacing = 6f;

    /// <summary>Whether water this deep running this fast is white.</summary>
    /// <param name="depth">Metres.</param>
    /// <param name="speed">Metres per second.</param>
    public static bool Qualifies(float depth, float speed) => depth >= MinDepth && speed >= MinSpeed;

    /// <summary>How strong a place is: how much water passes (speed times depth), more where it runs steep.</summary>
    /// <param name="flow">Speed times depth, square metres per second.</param>
    /// <param name="slope">The surface's slope, rise over run.</param>
    public static float Score(float flow, float slope) => flow * (1f + (2f * slope));

    /// <summary>
    /// The places chosen among these candidates: the highest scores first, each at least <see cref="Spacing"/>
    /// from those already chosen, at most <see cref="Max"/>. The list is sorted in place.
    /// </summary>
    /// <param name="candidates">Each place's score and position across the ground, with an index of the caller's.</param>
    /// <param name="chosen">Filled with the chosen places' indices, the strongest first.</param>
    public static void Pick(List<(float Score, float X, float Z, int Index)> candidates, List<int> chosen)
    {
        chosen.Clear();
        candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        var placed = new List<(float X, float Z)>(Max);
        foreach ((float _, float x, float z, int index) in candidates)
        {
            if (placed.Count >= Max)
            {
                break;
            }

            if (placed.TrueForAll(p => ((p.X - x) * (p.X - x)) + ((p.Z - z) * (p.Z - z)) >= Spacing * Spacing))
            {
                placed.Add((x, z));
                chosen.Add(index);
            }
        }
    }
}
