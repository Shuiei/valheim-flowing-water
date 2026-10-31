namespace FlowingWater;

/// <summary>
/// What the hoe's water pieces may show and remove, without the game: placed sources and drains show while one of
/// them is chosen in the hoe in hand or a tool that removes pieces is (the hammer, which removes them as any piece),
/// and at all times when the hoe could not take its remover (they would be out of everyone's reach else). The remover
/// removes a source or drain aimed at, nothing else; the hoe otherwise removes what it removed before the remover came
/// (nothing in the game; what another mod let it), and other tools what they do.
/// </summary>
internal static class HoeRemoval
{
    /// <summary>
    /// What the tool in the local player's hand makes of placed sources and drains: whether they show, and whether
    /// the one aimed at is marked to go (the hoe's remover chosen, or a tool that removes pieces in hand).
    /// </summary>
    /// <param name="hoeInHand">The tool in hand is the hoe.</param>
    /// <param name="removerChosen">The hoe's remover is chosen.</param>
    /// <param name="waterPieceChosen">The hoe's source or drain is chosen.</param>
    /// <param name="toolRemovesPieces">The tool in hand removes pieces (the hammer).</param>
    public static (bool Shown, bool Marked) Sight(bool hoeInHand, bool removerChosen, bool waterPieceChosen, bool toolRemovesPieces) =>
        hoeInHand ? (removerChosen || waterPieceChosen, removerChosen) : (toolRemovesPieces, toolRemovesPieces);

    /// <summary>Whether placed sources and drains show.</summary>
    /// <param name="waterPieceChosen">The tool in hand shows them (<see cref="Sight"/>).</param>
    /// <param name="removerReady">The hoe has its remover: its patch applied, the game's tool in hand readable, the hoe's pieces found.</param>
    public static bool Shown(bool waterPieceChosen, bool removerReady) => waterPieceChosen || !removerReady;

    /// <summary>
    /// Whether the placed source or drain under the aim stands in for what the player hovers (its hover text shown,
    /// [E] opening its panel): while a water piece is chosen, unless the game hovers something itself. Placed ones show
    /// only in build mode, where the game hovers nothing, so the game's own hover and [E] never reach them.
    /// </summary>
    /// <param name="waterPieceChosen">The hoe's source or drain is chosen.</param>
    /// <param name="aimedIsMarker">The piece under the game's aim (the one it would remove) is a placed source or drain.</param>
    /// <param name="gameHovers">The game hovers something (another mod's doing in build mode).</param>
    public static bool AimedHovered(bool waterPieceChosen, bool aimedIsMarker, bool gameHovers) => waterPieceChosen && aimedIsMarker && !gameHovers;

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
