using System;
using System.Collections.Generic;
using System.Globalization;

namespace FlowingWater.Server;

/// <summary>
/// What the server takes of a source's or drain's rate and reach, from a marker (whatever a player's game wrote into
/// it) or from the config's fixed ones, without the game. A rate that is no number, below 0, or 0 for a source, is
/// refused; one over <see cref="MaxRate"/> is held to it, told of once for each marker; a drain's rate of 0 takes
/// everything that reaches it, and stays 0. A drain reaches <see cref="MaxDrainRadius"/> at most.
/// </summary>
internal sealed class MarkerRules
{
    /// <summary>
    /// The most a source gives or a drain takes, cubic metres a second: fifty springs of the default rate, a big
    /// river. A marker holds whatever rate a player's game wrote into it: a billion filled every brick MaxBricks
    /// allows within a frame, while at this rate a thousand bricks of 1 m voxels take half a day to fill.
    /// </summary>
    public const float MaxRate = 100f;

    /// <summary>The farthest a drain reaches, metres: as far as the DefaultDrainRadius setting allows.</summary>
    public const float MaxDrainRadius = 16f;

    private readonly Action<string> _warn;

    // The markers (by object) and fixed sources and drains (by their text) whose rate was held, each told of once.
    private readonly HashSet<string> _capped = [];

    /// <summary>Rules that tell what they refuse or hold.</summary>
    /// <param name="warn">Logs a warning.</param>
    public MarkerRules(Action<string> warn) => _warn = warn;

    /// <summary>A drain's reach, from 0 to <see cref="MaxDrainRadius"/>: a player may write any radius into its marker.</summary>
    /// <param name="radius">Metres.</param>
    public static float Reach(float radius) => radius < 0f ? 0f : radius > MaxDrainRadius ? MaxDrainRadius : radius;

    /// <summary>
    /// The rate a source or drain is given: <paramref name="rate"/> held to <see cref="MaxRate"/> (told of once for
    /// each <paramref name="key"/>), or none when it is not usable (told of each time).
    /// </summary>
    /// <param name="rate">Cubic metres a second, as written.</param>
    /// <param name="drain">A drain (whose rate may be 0: it takes everything), else a source.</param>
    /// <param name="where">Where it is, for the warnings.</param>
    /// <param name="key">Which one it is: a marker's object, a fixed one's text.</param>
    /// <param name="used">The rate given.</param>
    /// <returns>Whether it is usable.</returns>
    public bool TryRate(float rate, bool drain, string where, string key, out float used)
    {
        used = 0f;
        if (float.IsNaN(rate) || float.IsInfinity(rate) || (drain ? rate < 0f : rate <= 0f))
        {
            _warn(FormattableString.Invariant($"{Kind(drain)} at {where}: rate {rate} is not usable; ignored"));
            return false;
        }

        used = Math.Min(rate, MaxRate);
        if (rate > MaxRate && _capped.Add(key))
        {
            _warn(FormattableString.Invariant($"{Kind(drain)} at {where}: rate {rate} m³/s is more than the most a source or drain may have; {MaxRate} m³/s used"));
        }

        return true;
    }

    /// <summary>
    /// The config's fixed sources or drains: "x,z,rate; x,z,rate", a drain with its radius if it has one,
    /// "x,z,rate,radius" (else <paramref name="defaultRadius"/>). Those written wrong or with a rate not usable are
    /// told of and left out; the rest come with their rate and reach as <see cref="TryRate"/> and
    /// <see cref="MaxDrainRadius"/> give them (a source's reach is 0).
    /// </summary>
    /// <param name="list">The setting's text.</param>
    /// <param name="drain">Drains, else sources.</param>
    /// <param name="defaultRadius">A drain's radius when it gives none, metres.</param>
    public List<(float X, float Z, float Rate, float Radius)> Fixed(string list, bool drain, float defaultRadius)
    {
        var found = new List<(float X, float Z, float Rate, float Radius)>();
        foreach (string item in list.Split(';'))
        {
            string trimmed = item.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if (!Parse(trimmed, drain, defaultRadius, out float x, out float z, out float rate, out float radius))
            {
                _warn($"Fixed {(drain ? "drain" : "source")} \"{trimmed}\" is not {(drain ? "x,z,rate or x,z,rate,radius" : "x,z,rate")}; ignored");
            }
            else if (TryRate(rate, drain, trimmed, trimmed, out float used))
            {
                found.Add((x, z, used, drain ? Math.Min(radius, MaxDrainRadius) : 0f));
            }
        }

        return found;
    }

    // "x,z,rate", or "x,z,rate,radius" for a drain (a radius over 0).
    private static bool Parse(string text, bool drain, float defaultRadius, out float x, out float z, out float rate, out float radius)
    {
        string[] parts = text.Split(',');
        radius = defaultRadius;
        x = z = rate = 0f;
        return parts.Length >= 3 && parts.Length <= (drain ? 4 : 3)
            && Number(parts[0], out x) && Number(parts[1], out z) && Number(parts[2], out rate)
            && (parts.Length == 3 || (Number(parts[3], out radius) && radius > 0f));
    }

    private static bool Number(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static string Kind(bool drain) => drain ? "Drain" : "Source";
}
