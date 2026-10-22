using System;

namespace FlowingWater.Client;

/// <summary>
/// How the water's look follows its depth, without the game: the light each metre of water absorbs, colour by
/// colour, from how clear the water is. Red goes first and blue last, so the deeper the water the more it takes
/// its own colour: a few centimetres are clear, a pool turns blue-green, deeper still hides its bed.
/// </summary>
internal static class WaterLook
{
    /// <summary>How far one sees into the water by default, metres.</summary>
    public const float DefaultClarity = 4.5f;

    // Clear fresh water's absorption per metre, red, green and blue (red goes five times as fast as blue), over
    // their mean.
    private const float Red = 0.45f / 0.22f, Green = 0.12f / 0.22f, Blue = 0.09f / 0.22f;

    /// <summary>
    /// The light absorbed per metre of water, colour by colour, for water this clear: through that many metres
    /// most of the red is gone (seven parts in eight) while a little over half the green and two thirds of the blue
    /// still come through, so what lies behind is still seen, tinted blue-green.
    /// </summary>
    /// <param name="clarity">How far one sees into the water, metres (more than 0).</param>
    public static (float R, float G, float B) Absorption(float clarity)
    {
        float mean = 1f / Math.Max(clarity, 0.01f);
        return (Red * mean, Green * mean, Blue * mean);
    }
}
