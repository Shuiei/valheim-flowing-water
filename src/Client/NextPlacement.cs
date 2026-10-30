using FlowingWater.Volume;

namespace FlowingWater.Client;

/// <summary>
/// The rate and radius the next source or drain placed with the hoe takes, and the next source's shape, kept for the
/// game's session, each kind its own, without the game. They start from the Markers settings, held as
/// <see cref="MarkerValues"/> holds a typed value; a setting that is not usable (a source's rate of 0) starts from the
/// mod's own default instead; the shape starts as a slim jet (<see cref="SourceShape.Jet"/>), facing the way the
/// source is turned as it is placed.
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

    /// <summary>The next source's shape, facing north (it faces the way it is turned as it is placed).</summary>
    public SourceShape Shape { get; private set; } = SourceShape.Jet(0f);

    /// <summary>The next source's shape, facing a heading (its piece's turn as it is placed).</summary>
    public SourceShape ShapeFacing(float heading) => new(Shape.AllRound, heading, Shape.Width, Shape.Spread, Shape.Throw);

    /// <summary>A new shape for the next source, held to its ranges (its heading is the piece's: not kept).</summary>
    public void SetShape(bool allRound, float width, float spread, float throwUp) => Shape = new SourceShape(allRound, 0f, width, spread, throwUp);

    /// <summary>A rate for the next source or drain (from the panel's slider), as <see cref="MarkerValues.TryRate(float, bool, out float, out string)"/> takes it; one refused leaves the rate as it was.</summary>
    /// <param name="drain">A drain's, else a source's.</param>
    /// <param name="value">Cubic metres a second.</param>
    /// <param name="note">Why it was refused, or how it was held; null when it is taken as it is.</param>
    /// <returns>Whether it was taken.</returns>
    public bool TrySetRate(bool drain, float value, out string note)
    {
        if (!MarkerValues.TryRate(value, drain, out float rate, out note))
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

    /// <summary>A radius for the next drain (from the panel's slider), as <see cref="MarkerValues.TryRadius(float, out float, out string)"/> takes it; one refused leaves the radius as it was.</summary>
    /// <param name="value">Metres.</param>
    /// <param name="note">Why it was refused, or how it was held; null when it is taken as it is.</param>
    /// <returns>Whether it was taken.</returns>
    public bool TrySetRadius(float value, out string note)
    {
        if (!MarkerValues.TryRadius(value, out float radius, out note))
        {
            return false;
        }

        Radius = radius;
        return true;
    }

    /// <summary>The next source's or drain's rate, cubic metres a second.</summary>
    /// <param name="drain">A drain's, else a source's.</param>
    public float Rate(bool drain) => drain ? _drainRate : _sourceRate;
}
