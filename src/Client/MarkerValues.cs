using System;
using System.Globalization;
using FlowingWater.Server;
using FlowingWater.Volume;

namespace FlowingWater.Client;

/// <summary>
/// A source's or drain's rate and radius as players type and read them, without the game: what the panel takes from
/// a typed value (refused, or held as the server would hold it), and what the panel, the hover text and the hoe say.
/// The rates agree with <see cref="MarkerRules"/>: a source's above 0, a drain's 0 or more (0 takes everything), each
/// held to <see cref="MarkerRules.MaxRate"/>; a drain's radius is held from <see cref="MinRadius"/> to
/// <see cref="MarkerRules.MaxDrainRadius"/>, as the DefaultDrainRadius setting allows.
/// </summary>
internal static class MarkerValues
{
    /// <summary>The smallest radius a drain is given, metres: as small as the DefaultDrainRadius setting allows.</summary>
    public const float MinRadius = 0.25f;

    /// <summary>
    /// A number as a player types it, whatever the game's language: a point or a comma before the decimals (not
    /// both), no thousands. False when it is no finite number.
    /// </summary>
    /// <param name="text">What was typed.</param>
    /// <param name="value">The number.</param>
    public static bool TryNumber(string text, out float value)
    {
        value = 0f;
        if (text == null)
        {
            return false;
        }

        // With its comma read as a point, a number written with both ("1,000.5") has two points: refused.
        return (Parse(text, out value) || Parse(text.Replace(',', '.'), out value)) && !float.IsNaN(value) && !float.IsInfinity(value);
    }

    private static bool Parse(string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    /// <summary>A typed rate, as <see cref="TryRate(float, bool, out float, out string)"/> takes it.</summary>
    /// <param name="text">What was typed.</param>
    /// <param name="drain">A drain's rate, else a source's.</param>
    /// <param name="rate">The rate given, cubic metres a second.</param>
    /// <param name="note">Why it was refused, or how it was held; null when it is taken as typed.</param>
    /// <returns>Whether it is usable.</returns>
    public static bool TryRate(string text, bool drain, out float rate, out string note)
    {
        if (!TryNumber(text, out float typed))
        {
            rate = 0f;
            note = $"\"{text}\" is not a number";
            return false;
        }

        return TryRate(typed, drain, out rate, out note);
    }

    /// <summary>
    /// A rate as the server takes it: a source's must be above 0, a drain's 0 or more (refused otherwise); one over
    /// <see cref="MarkerRules.MaxRate"/> is held to it.
    /// </summary>
    /// <param name="value">Cubic metres a second.</param>
    /// <param name="drain">A drain's rate, else a source's.</param>
    /// <param name="rate">The rate given.</param>
    /// <param name="note">Why it was refused, or how it was held; null when it is taken as it is.</param>
    /// <returns>Whether it is usable.</returns>
    public static bool TryRate(float value, bool drain, out float rate, out string note)
    {
        rate = 0f;
        if (float.IsNaN(value) || float.IsInfinity(value))
        {
            note = "the rate is not a number";
            return false;
        }

        if (drain ? value < 0f : value <= 0f)
        {
            note = drain ? "a drain's rate must be 0 or more (0 takes everything)" : "a source's rate must be above 0";
            return false;
        }

        rate = Math.Min(value, MarkerRules.MaxRate);
        note = value > MarkerRules.MaxRate ? $"held to {Format(MarkerRules.MaxRate)} m³/s, the most a source or drain may have" : null;
        return true;
    }

    /// <summary>A typed radius, as <see cref="TryRadius(float, out float, out string)"/> takes it.</summary>
    /// <param name="text">What was typed.</param>
    /// <param name="radius">The radius given, metres.</param>
    /// <param name="note">Why it was refused, or how it was held; null when it is taken as typed.</param>
    /// <returns>Whether it is usable.</returns>
    public static bool TryRadius(string text, out float radius, out string note)
    {
        if (!TryNumber(text, out float typed))
        {
            radius = 0f;
            note = $"\"{text}\" is not a number";
            return false;
        }

        return TryRadius(typed, out radius, out note);
    }

    /// <summary>
    /// A drain's radius: above 0 (refused otherwise), held from <see cref="MinRadius"/> to
    /// <see cref="MarkerRules.MaxDrainRadius"/>.
    /// </summary>
    /// <param name="value">Metres.</param>
    /// <param name="radius">The radius given.</param>
    /// <param name="note">Why it was refused, or how it was held; null when it is taken as it is.</param>
    /// <returns>Whether it is usable.</returns>
    public static bool TryRadius(float value, out float radius, out string note)
    {
        radius = 0f;
        if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
        {
            note = "a drain's radius must be above 0";
            return false;
        }

        radius = Math.Max(MinRadius, Math.Min(value, MarkerRules.MaxDrainRadius));
        note = value < MinRadius
            ? $"held to {Format(MinRadius)} m, the least"
            : value > MarkerRules.MaxDrainRadius ? $"held to {Format(MarkerRules.MaxDrainRadius)} m, the most" : null;
        return true;
    }

    /// <summary>Words begun with a capital letter, as a line of the panel or the hover text begins.</summary>
    public static string Capital(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        char[] letters = text.ToCharArray();
        letters[0] = char.ToUpperInvariant(letters[0]);
        return new string(letters);
    }

    /// <summary>A number as the panel and the hover text show it: up to two decimals, a point before them.</summary>
    public static string Format(float value) => value.ToString("0.##", CultureInfo.InvariantCulture);

    /// <summary>
    /// Whether a value typed is the one already kept, as the panel tells them apart: alike to within a millionth of the
    /// larger, or a few of the smallest floats near 0 (Unity's Mathf.Approximately), not exactly.
    /// </summary>
    public static bool Same(float a, float b) => Math.Abs(b - a) < Math.Max(1e-6f * Math.Max(Math.Abs(a), Math.Abs(b)), float.Epsilon * 8f);

    /// <summary>The piece's name, in the hoe and over a placed one.</summary>
    /// <param name="drain">A drain, else a source.</param>
    public static string Name(bool drain) => drain ? "Water drain" : "Water source";

    /// <summary>The name of the hoe's piece that removes a source or drain.</summary>
    public const string RemoverName = "Remove source or drain";

    /// <summary>The description of the hoe's piece that removes a source or drain.</summary>
    public const string RemoverDescription = "Shows the sources and drains, hidden otherwise; the one aimed at turns red, and a click removes it.";

    /// <summary>The piece's description in the hoe.</summary>
    /// <param name="drain">A drain, else a source.</param>
    public static string Description(bool drain) => drain
        ? "Takes the water that reaches it. Its rate and reach are set in the panel on the right. Placed ones show while a water piece is chosen; [E] on one changes or removes it."
        : "Water springs from it, the way it faces (turn it as you place it) or all round. Its width, spread, throw and power are set in the panel on the right. Placed ones show while a water piece is chosen; [E] on one changes or removes it.";

    /// <summary>
    /// What a source or drain does, as the server takes it: its rate held to the most (or "no water" when it is not
    /// usable), a drain's reach.
    /// </summary>
    /// <param name="drain">A drain, else a source.</param>
    /// <param name="rate">Its rate, as written in it.</param>
    /// <param name="radius">A drain's radius, as written in it.</param>
    public static string Describe(bool drain, float rate, float radius)
    {
        string kind = drain ? "Drain" : "Source";
        if (!TryRate(rate, drain, out float used, out _))
        {
            return $"{kind}: no water (its rate, {rate.ToString(CultureInfo.InvariantCulture)}, is not usable)";
        }

        if (!drain)
        {
            return $"{kind}: {Format(used)} m³/s";
        }

        float reach = MarkerRules.Reach(radius);
        return used <= 0f ? $"{kind}: takes everything within {Format(reach)} m" : $"{kind}: {Format(used)} m³/s within {Format(reach)} m";
    }

    /// <summary>
    /// The hover text over a placed source or drain: what it does, and the key to change it for a player who may (the
    /// game's own key token, which it puts the player's key in for).
    /// </summary>
    /// <param name="drain">A drain, else a source.</param>
    /// <param name="rate">Its rate, as written in it.</param>
    /// <param name="radius">A drain's radius, as written in it.</param>
    /// <param name="mayChange">Whether the player may change it.</param>
    public static string Hover(bool drain, float rate, float radius, bool mayChange) =>
        Describe(drain, rate, radius) + (mayChange ? "\n[<color=yellow><b>$KEY_Use</b></color>] Settings" : string.Empty);

    /// <summary>The hover text over a placed source, with its shape under its rate (<see cref="MarkerSliders.Describe"/>).</summary>
    /// <param name="rate">Its rate, as written in it.</param>
    /// <param name="shape">Its shape, as written in it.</param>
    /// <param name="mayChange">Whether the player may change it.</param>
    public static string Hover(float rate, SourceShape shape, bool mayChange)
    {
        return Describe(false, rate, 0f) + "\n" + Capital(MarkerSliders.Describe(shape)) + (mayChange ? "\n[<color=yellow><b>$KEY_Use</b></color>] Settings" : string.Empty);
    }

    /// <summary>The panel's title.</summary>
    /// <param name="drain">A drain's values, else a source's.</param>
    /// <param name="placed">A placed one's, else those the next one placed takes.</param>
    public static string Title(bool drain, bool placed) => (placed ? "This " : "Next ") + (drain ? "drain" : "source");

    /// <summary>The label of the button that removes a placed one, under the panel's keys.</summary>
    public const string RemoveLabel = "Remove";

    /// <summary>The panel's keys, at its foot (a placed one's says it may be removed with the button under them).</summary>
    /// <param name="editing">Whether its values are being changed (the mouse free, the fields open).</param>
    /// <param name="placed">A placed one's, else those the next one placed takes.</param>
    public static string Keys(bool editing, bool placed) => editing
        ? placed ? $"[Esc] or [E] Close   or {RemoveLabel} it below" : "[Esc] or [E] Done"
        : "[E] Change";

    /// <summary>The hint under the panel's sliders.</summary>
    /// <param name="drain">A drain's, else a source's.</param>
    public static string SliderHint(bool drain) => drain
        ? "Power at its left end takes everything that reaches it."
        : "It faces the way you turn it (the mouse wheel) as you place it, shown on the ground. Throw 0° runs along the ground; up to 60° arches it into the air.";

    /// <summary>A rate beside its slider: a drain's 0 is "all", the others in m³/s.</summary>
    public static string RateText(float rate, bool drain) => drain && !(rate > 0f) ? "all" : Format(rate) + " m³/s";

    /// <summary>The line naming a source's water under its drawings, and how far out a jet lands.</summary>
    public static string KindLine(SourceShape shape, float rate)
    {
        float? lands = SourcePreview.Lands(shape, rate);
        return Capital(MarkerSliders.Kind(shape)) + (lands is { } far ? $" · lands {Format((float)Math.Round(far))} m out" : string.Empty);
    }
}
