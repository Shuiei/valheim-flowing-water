namespace FlowingWater.Volume;

/// <summary>
/// Paces the water's steps over the server's frames so that, on average, they take at most a budget of each frame.
/// Each frame owes the water its time and brings the budget's worth of credit; fixed steps are taken while time is
/// owed and credit is left, each step paying what it cost. A step dearer than the budget leaves the credit short,
/// and the next frames take none until it is made up. Owed time past a limit is given up, so that water too heavy
/// for the server slows down instead of slowing the server.
/// </summary>
internal sealed class StepPacer
{
    private readonly float _mostOwed;

    private double _credit;

    /// <summary>A pacer that gives up owed time past <paramref name="mostOwed"/> seconds.</summary>
    public StepPacer(float mostOwed)
    {
        _mostOwed = mostOwed;
    }

    /// <summary>Seconds of water time owed and not stepped yet.</summary>
    public float Owed { get; private set; }

    /// <summary>Seconds of water time given up since last taken.</summary>
    public float Dropped { get; private set; }

    /// <summary>
    /// A frame of <paramref name="seconds"/> went by, with a <paramref name="budget"/> (milliseconds) for the water:
    /// the water owes that much more time, and the credit grows by the budget (up to one frame's).
    /// </summary>
    public void Add(float seconds, double budget)
    {
        Owed += seconds;
        if (Owed > _mostOwed)
        {
            Dropped += Owed - _mostOwed;
            Owed = _mostOwed;
        }

        _credit = System.Math.Min(_credit + budget, budget);
    }

    /// <summary>Whether to take one more step of <paramref name="step"/> seconds now: time owed and credit left.</summary>
    public bool Next(float step)
    {
        if (Owed < step || _credit <= 0.0)
        {
            return false;
        }

        Owed -= step;
        return true;
    }

    /// <summary>The step just taken cost this many milliseconds.</summary>
    public void Spent(double milliseconds) => _credit -= milliseconds;

    /// <summary>The seconds given up since last asked, and forgets them.</summary>
    public float TakeDropped()
    {
        float dropped = Dropped;
        Dropped = 0f;
        return dropped;
    }
}
