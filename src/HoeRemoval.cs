namespace FlowingWater;

/// <summary>
/// What the hoe's water pieces may show and remove, without the game: placed sources and drains show while one of
/// them is chosen in the hoe in hand, and at all times when the hoe could not take its remover (they would be out of
/// everyone's reach else). The remover removes a source or drain aimed at, nothing else; the hoe otherwise removes what
/// it removed before the remover came (nothing in the game; what another mod let it), and other tools what they do.
/// </summary>
internal static class HoeRemoval
{
    /// <summary>Whether placed sources and drains show.</summary>
    /// <param name="waterPieceChosen">The local player holds the hoe with one of its water pieces chosen.</param>
    /// <param name="removerReady">The hoe has its remover: its patch applied, the game's tool in hand readable, the hoe's pieces found.</param>
    public static bool Shown(bool waterPieceChosen, bool removerReady) => waterPieceChosen || !removerReady;

    /// <summary>Whether the game's remove goes on, at the piece aimed at.</summary>
    /// <param name="hoeInHand">The tool in hand is the hoe.</param>
    /// <param name="hoeRemovedBefore">The hoe removed pieces before the remover came (another mod let it).</param>
    /// <param name="removing">The hoe's remover is chosen.</param>
    /// <param name="aimedIsMarker">The piece aimed at is a source or drain.</param>
    public static bool MayRemove(bool hoeInHand, bool hoeRemovedBefore, bool removing, bool aimedIsMarker)
    {
        if (!hoeInHand)
        {
            return true;
        }

        return removing ? aimedIsMarker : hoeRemovedBefore;
    }
}
