namespace FlowingWater.Volume;

/// <summary>
/// How deep water must stand to be drawn, shared by the server (which sends a brick only when a change would show) and
/// the players' games (which fade the water in over it), without the game.
/// </summary>
internal static class Drawing
{
    /// <summary>Water thinner than this is not drawn, metres: a trace left behind, not a sheet.</summary>
    public const float HideDepth = 0.01f;

    /// <summary>Water this deep is drawn in full, metres; between <see cref="HideDepth"/> and it, the water fades in.</summary>
    public const float ShowDepth = 0.03f;
}
