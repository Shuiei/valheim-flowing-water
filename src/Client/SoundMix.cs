using System;

namespace FlowingWater.Client;

/// <summary>
/// How a place of running water sounds, without the game: how loud (by the water passing, how steeply it runs, the
/// player's volume and the distance) and how much of it is a stream's babble or the roar of rapids and falls.
/// </summary>
internal static class SoundMix
{
    /// <summary>Heard from this far away at most, metres; further off a sound is stopped.</summary>
    public const float SoundFar = 35f;

    /// <summary>
    /// A stream's babble turns into the roar of rapids from this steepness (falling as fast as it runs across: water
    /// sliding down a slope still sounds like a stream) to the second (falling twice as fast: it falls).
    /// </summary>
    public const float RoarFrom = 1f, RoarFull = 2f;

    /// <summary>How loud a place is, 0 to the player's volume; 0 out of earshot.</summary>
    /// <param name="flow">Speed times depth there, square metres a second.</param>
    /// <param name="steep">How steeply it runs (falling over running across).</param>
    /// <param name="volume">The player's volume for running water (0 to 1).</param>
    /// <param name="distanceSquared">The square of the listener's distance, metres.</param>
    public static float Loudness(float flow, float steep, float volume, float distanceSquared) =>
        distanceSquared > SoundFar * SoundFar ? 0f : Clamp01((flow * 0.8f) + (steep * 0.3f)) * volume;

    /// <summary>How much of a place's sound is the roar of rapids (the rest is a stream's babble), 0 to 1.</summary>
    /// <param name="steep">How steeply it runs.</param>
    public static float Roar(float steep) => Clamp01((steep - RoarFrom) / (RoarFull - RoarFrom));

    private static float Clamp01(float value) => Math.Min(1f, Math.Max(0f, value));
}
