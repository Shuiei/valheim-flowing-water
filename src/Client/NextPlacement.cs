namespace FlowingWater.Client;

/// <summary>
/// The rate and radius the next source or drain placed with the hoe takes, kept for the game's session, each kind its
/// own, without the game. They start from the Markers settings, held as <see cref="MarkerValues"/> holds a typed
/// value; a setting that is not usable (a source's rate of 0) starts from the mod's own default instead.
/// </summary>
internal sealed class NextPlacement
{
    /// <summary>A source's rate when its setting is not usable, cubic metres a second.</summary>
    public const float SourceRateDefault = 2f;

    /// <summary>A drain's rate when its setting is not usable: it takes everything.</summary>
    public const float DrainRateDefault = 0f;

    /// <summary>A drain's radius when its setting is not usable, metres.</summary>
    public const float RadiusDefault = 1f;

    private float _sourceRate;

    private float _drainRate;

    /// <summary>Values from the settings.</summary>
    /// <param name="sourceRate">DefaultSourceRate.</param>
    /// <param name="drainRate">DefaultDrainRate.</param>
    /// <param name="drainRadius">DefaultDrainRadius.</param>
    public NextPlacement(float sourceRate, float drainRate, float drainRadius)
    {
        _sourceRate = MarkerValues.TryRate(sourceRate, false, out float source, out _) ? source : SourceRateDefault;
        _drainRate = MarkerValues.TryRate(drainRate, true, out float drain, out _) ? drain : DrainRateDefault;
        Radius = MarkerValues.TryRadius(drainRadius, out float radius, out _) ? radius : RadiusDefault;
    }

    /// <summary>The next drain's radius, metres.</summary>
    public float Radius { get; private set; }

    /// <summary>The next source's or drain's rate, cubic metres a second.</summary>
    /// <param name="drain">A drain's, else a source's.</param>
    public float Rate(bool drain) => drain ? _drainRate : _sourceRate;

    /// <summary>A typed rate for the next source or drain; one refused leaves the rate as it was.</summary>
    /// <param name="drain">A drain's, else a source's.</param>
    /// <param name="text">What was typed.</param>
    /// <param name="note">Why it was refused, or how it was held; null when it is taken as typed.</param>
    /// <returns>Whether it was taken.</returns>
    public bool TrySetRate(bool drain, string text, out string note)
    {
        if (!MarkerValues.TryRate(text, drain, out float rate, out note))
        {
            return false;
        }

        if (drain)
        {
            _drainRate = rate;
        }
        else
        {
            _sourceRate = rate;
        }

        return true;
    }

    /// <summary>A typed radius for the next drain; one refused leaves the radius as it was.</summary>
    /// <param name="text">What was typed.</param>
    /// <param name="note">Why it was refused, or how it was held; null when it is taken as typed.</param>
    /// <returns>Whether it was taken.</returns>
    public bool TrySetRadius(string text, out string note)
    {
        if (!MarkerValues.TryRadius(text, out float radius, out note))
        {
            return false;
        }

        Radius = radius;
        return true;
    }
}
