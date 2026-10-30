using System;
using FlowingWater.Server;
using FlowingWater.Volume;

namespace FlowingWater.Client;

/// <summary>
/// The panel's sliders, without the game: the notches each has and what each notch stands for, and the words the
/// panel and the hover text use for a source's shape. A source's width goes by half metres to 10 m, its spread by 5
/// degrees to 90 and one notch more for all round, its throw by 5 degrees to 60; a rate by notches even on a
/// logarithm from 0.1 to <see cref="MarkerRules.MaxRate"/> m³/s (a drain's first notch: everything), rounded to what a
/// player reads; a drain's reach by quarter metres from <see cref="MarkerValues.MinRadius"/> to
/// <see cref="MarkerRules.MaxDrainRadius"/>.
/// </summary>
internal static class MarkerSliders
{
    /// <summary>The width slider's last notch (0: a point; each a half metre).</summary>
    public const int WidthNotches = 20;

    /// <summary>The spread slider's last notch: all round (those before it, 5 degrees each).</summary>
    public const int SpreadNotches = 19;

    /// <summary>The throw slider's last notch (each 5 degrees).</summary>
    public const int ThrowNotches = 12;

    /// <summary>The rate slider's last notch.</summary>
    public const int RateNotches = 120;

    /// <summary>The reach slider's last notch (each a quarter metre; the first, notch 1).</summary>
    public const int ReachNotches = 64;

    // The least rate a notch gives, m³/s.
    private const float LeastRate = 0.1f;

    /// <summary>A width notch's width, metres.</summary>
    public static float Width(int notch) => Clamp(notch, 0, WidthNotches) * 0.5f;

    /// <summary>The width notch nearest a width.</summary>
    public static int WidthNotch(float width) => Clamp((int)Math.Round(width / 0.5f), 0, WidthNotches);

    /// <summary>Whether a spread notch is the all-round one.</summary>
    public static bool AllRound(int notch) => notch >= SpreadNotches;

    /// <summary>A spread notch's spread, degrees (the all-round notch keeps the last one's).</summary>
    public static float Spread(int notch) => Clamp(notch, 0, SpreadNotches - 1) * 5f;

    /// <summary>The spread notch a shape stands at: all round, or the one nearest its spread.</summary>
    public static int SpreadNotch(SourceShape shape) => shape.AllRound ? SpreadNotches : FanNotch(shape.Spread);

    /// <summary>A throw notch's throw, degrees.</summary>
    public static float Throw(int notch) => Clamp(notch, 0, ThrowNotches) * 5f;

    /// <summary>The throw notch nearest a throw.</summary>
    public static int ThrowNotch(float degrees) => Clamp((int)Math.Round(degrees / 5f), 0, ThrowNotches);

    /// <summary>A rate notch's rate, m³/s: a drain's notch 0 is 0 (everything); the others even on a logarithm from 0.1 to the most, rounded (<see cref="Nice"/>).</summary>
    public static float Rate(int notch, bool drain)
    {
        notch = Clamp(notch, 0, RateNotches);
        if (drain && notch == 0)
        {
            return 0f;
        }

        float share = drain ? (notch - 1) / (float)(RateNotches - 1) : notch / (float)RateNotches;
        return Nice(LeastRate * (float)Math.Pow(MarkerRules.MaxRate / LeastRate, share));
    }

    /// <summary>The rate notch nearest a rate (a drain's 0: notch 0).</summary>
    public static int RateNotch(float rate, bool drain)
    {
        if (drain && !(rate > 0f))
        {
            return 0;
        }

        double share = Math.Log(Math.Min(MarkerRules.MaxRate, Math.Max(LeastRate, rate)) / LeastRate) / Math.Log(MarkerRules.MaxRate / LeastRate);
        return drain ? 1 + (int)Math.Round(share * (RateNotches - 1)) : (int)Math.Round(share * RateNotches);
    }

    /// <summary>A rate as a player reads it: by twentieths under 1 m³/s, tenths under 10, halves under 30, then whole.</summary>
    public static float Nice(float rate)
    {
        float step = rate < 1f ? 0.05f : rate < 10f ? 0.1f : rate < 30f ? 0.5f : 1f;
        return Math.Min(MarkerRules.MaxRate, Math.Max(LeastRate, (float)Math.Round(rate / step) * step));
    }

    /// <summary>A reach notch's reach, metres.</summary>
    public static float Reach(int notch) => Clamp(notch, 1, ReachNotches) * 0.25f;

    /// <summary>The reach notch nearest a reach.</summary>
    public static int ReachNotch(float reach) => Clamp((int)Math.Round(reach / 0.25f), 1, ReachNotches);

    /// <summary>
    /// The rate the power slider gives when a slider moved: the one kept while it still stands at the notch nearest it
    /// (a rate between notches, typed before 0.47.0 or set by the console or the config, is not moved by another
    /// slider), else its notch's.
    /// </summary>
    public static float SlidRate(int notch, float kept, bool drain) => notch == RateNotch(kept, drain) ? kept : Rate(notch, drain);

    /// <summary>The reach the reach slider gives when a slider moved: the one kept while it still stands at the notch nearest it, else its notch's.</summary>
    public static float SlidReach(int notch, float kept) => notch == ReachNotch(kept) ? kept : Reach(notch);

    /// <summary>
    /// The shape the shape sliders give when one moved, from the shape kept: each value kept while its slider still
    /// stands at the notch nearest it, else its notch's, and the heading kept (the piece's turn); the spread kept too
    /// when it is made all round, so turned back to the notch nearest it, it fans out as it did.
    /// </summary>
    public static SourceShape Slid(SourceShape kept, int width, int spread, int throwUp)
    {
        bool allRound = AllRound(spread);
        float fan = allRound || spread == FanNotch(kept.Spread) ? kept.Spread : Spread(spread);
        return new SourceShape(
            allRound,
            kept.Heading,
            width == WidthNotch(kept.Width) ? kept.Width : Width(width),
            fan,
            throwUp == ThrowNotch(kept.Throw) ? kept.Throw : Throw(throwUp));
    }

    /// <summary>
    /// What a shape's water is, in a player's words: all round; thrown up (10 degrees or more), a slim or (fanned 45 or
    /// more) wide jet spray; along the ground, fanned out (15 degrees or more) a slim or wide spray, else a small or
    /// (3 m wide or more) wide river.
    /// </summary>
    public static string Kind(SourceShape shape)
    {
        if (shape.AllRound)
        {
            return "all round";
        }

        string wideSpray = shape.Spread >= 45f ? "wide" : "slim";
        if (shape.Throw >= 10f)
        {
            return $"a {wideSpray} jet spray";
        }

        return shape.Spread >= 15f ? $"a {wideSpray} spray along the ground" : shape.Width >= 3f ? "a wide river" : "a small river";
    }

    /// <summary>A shape in full, for the hover text and the panel's status: its kind, and for one facing a way its width, spread and throw.</summary>
    public static string Describe(SourceShape shape) => shape.AllRound
        ? Kind(shape)
        : $"{Kind(shape)}, {(shape.Width > 0f ? MarkerValues.Format(shape.Width) + " m wide" : "from a point")}, {(shape.Spread > 0f ? "fanned " + MarkerValues.Format(shape.Spread) + "°" : "straight")}, {(shape.Throw > 0f ? "thrown up " + MarkerValues.Format(shape.Throw) + "°" : "along the ground")}";

    // The spread notch nearest a spread, the all-round one left out.
    private static int FanNotch(float spread) => Clamp((int)Math.Round(spread / 5f), 0, SpreadNotches - 1);

    private static int Clamp(int value, int low, int high) => Math.Min(high, Math.Max(low, value));
}
