namespace FlowingWater.Server;

/// <summary>
/// The wait between two looks over the world (for sources and drains), without the game: it counts from the end of a
/// look, not from its start, so that in a world whose look takes longer than the wait the next does not begin at once.
/// The first look is due at once.
/// </summary>
internal sealed class LookWait
{
    // Seconds waited since the last look ended; none taken yet, the first is due.
    private float _waited = float.MaxValue;

    /// <summary>Whether to begin a look, a frame <paramref name="dt"/> seconds long having passed.</summary>
    /// <param name="dt">The frame's seconds.</param>
    /// <param name="seconds">The wait from one look's end to the next's start.</param>
    /// <param name="looking">Whether a look is under way: the wait does not count meanwhile.</param>
    public bool Due(float dt, float seconds, bool looking)
    {
        if (looking)
        {
            return false;
        }

        _waited += dt;
        return _waited >= seconds;
    }

    /// <summary>A look began: the wait starts again once it ends.</summary>
    public void Began() => _waited = 0f;
}
