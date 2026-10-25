using System;

namespace FlowingWater.Volume;

/// <summary>
/// One clock for every brick's blend on the graphics card, without the game: the server writes all the bricks of a
/// send together, and they reach a player's game within a frame or two of each other, so each such wave of data
/// starts one blend, from when its first brick came and for as long as the wave before took to come round. A brick
/// drawn again later for a neighbour's new data blends over the same span as the neighbour, so the column they share
/// moves as one and shows no step between them.
/// </summary>
internal sealed class BlendClock
{
    /// <summary>Data coming within this long of a wave's start is of that wave, seconds: a send is spread over a frame or two.</summary>
    public const float Shortest = 0.25f;

    /// <summary>The longest a blend takes, seconds: a send long in coming (held back, a hitch) is not blended that long.</summary>
    public const float Longest = 2f;

    /// <summary>How long the first wave's blend takes, with no wave before to measure, seconds.</summary>
    public const float Default = 1f;

    private bool _began;

    /// <summary>When the blend under way began, seconds on the caller's clock (0 before the first wave).</summary>
    public float Start { get; private set; }

    /// <summary>How long the blend under way takes, seconds.</summary>
    public float Seconds { get; private set; } = Default;

    /// <summary>
    /// A brick's data came at this time: the start of a new wave, whose blend takes as long as since the wave before
    /// began (held from <see cref="Shortest"/> to <see cref="Longest"/>), unless it is of the wave under way.
    /// </summary>
    /// <param name="now">Seconds on the caller's clock.</param>
    public void Came(float now)
    {
        if (_began && now - Start < Shortest)
        {
            return;
        }

        Seconds = _began ? Math.Max(Shortest, Math.Min(Longest, now - Start)) : Default;
        Start = now;
        _began = true;
    }

    /// <summary>How far the blend under way has gone at this time: 0 at its start, 1 once done.</summary>
    public float Share(float now) => Math.Max(0f, Math.Min(1f, (now - Start) / Seconds));

    /// <summary>
    /// As new: no wave came yet. For a caller whose clock starts again (a game's, at each world it opens): a start kept
    /// from the clock before would lie in the future, and nothing would be shown moving until the clock caught up.
    /// </summary>
    public void Reset()
    {
        _began = false;
        Start = 0f;
        Seconds = Default;
    }
}
