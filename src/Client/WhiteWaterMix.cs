namespace FlowingWater.Client;

/// <summary>
/// How a white-water place sounds and sprays, without the game: how loud (by the water passing, the slope,
/// the player's volume and the distance), how much of it is a stream's babble or the roar of rapids, and how
/// much spray it throws.
/// </summary>
internal static class WhiteWaterMix
{
    /// <summary>Heard from this far away at most, metres; further off a sound is stopped.</summary>
    public const float SoundFar = 35f;

    /// <summary>Spray from this slope up (rise over run: 14 degrees); all of it from the second (39).</summary>
    public const float SprayFrom = 0.25f, SprayFull = 0.8f;

    /// <summary>Drops thrown a second at most.</summary>
    public const float MaxSprayRate = 80f;

    /// <summary>
    /// A stream's babble turns into the roar of rapids from this slope (45 degrees: water sliding down a slope
    /// still sounds like a stream) to the second (63: it falls).
    /// </summary>
    public const float RoarFrom = 1f, RoarFull = 2f;

    /// <summary>How loud a place is, 0 to the player's volume; 0 out of earshot.</summary>
    /// <param name="flow">Speed times depth there, square metres per second.</param>
    /// <param name="slope">The surface's slope there, rise over run.</param>
    /// <param name="volume">The player's volume for running water (0 to 1).</param>
    /// <param name="distanceSquared">The square of the listener's distance, metres.</param>
    public static float Loudness(float flow, float slope, float volume, float distanceSquared) =>
        distanceSquared > SoundFar * SoundFar ? 0f : Clamp01((flow * 0.8f) + (slope * 0.3f)) * volume;

    /// <summary>How much of a place's sound is the roar of rapids (the rest is a stream's babble), 0 to 1.</summary>
    /// <param name="slope">The surface's slope there, rise over run.</param>
    public static float Roar(float slope) => Clamp01((slope - RoarFrom) / (RoarFull - RoarFrom));

    /// <summary>How much spray a place throws, 0 to 1; none with spray turned off.</summary>
    /// <param name="slope">The surface's slope there, rise over run.</param>
    /// <param name="on">Whether spray is on.</param>
    public static float Spray(float slope, bool on) => on ? Clamp01((slope - SprayFrom) / (SprayFull - SprayFrom)) : 0f;

    /// <summary>Drops thrown a second: more as more water passes, scaled by the spray's share.</summary>
    /// <param name="flow">Speed times depth there, square metres per second.</param>
    /// <param name="share">The spray's share (<see cref="Spray"/>).</param>
    public static float SprayRate(float flow, float share) => share * Clamp(flow * 120f, 8f, MaxSprayRate);

    private static float Clamp01(float value) => Clamp(value, 0f, 1f);

    private static float Clamp(float value, float low, float high) => System.Math.Min(high, System.Math.Max(low, value));
}
