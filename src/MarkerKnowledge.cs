using System.Collections.Generic;

namespace FlowingWater;

/// <summary>
/// What a character keeps learned of the hoe's source and drain where it plays, without the game: where FlowingWater
/// is, nothing is taken away (the game teaches them, once asked to look at the character's tools again); on a server
/// without it, they are taken out of what the character knows, and only they.
/// </summary>
internal static class MarkerKnowledge
{
    /// <summary>Takes the source and drain out of what a character knows where FlowingWater is not.</summary>
    /// <param name="modHere">Whether FlowingWater is where the character plays.</param>
    /// <param name="known">The pieces and recipes the character knows.</param>
    /// <param name="names">The source's and drain's names.</param>
    /// <returns>How many were forgotten.</returns>
    public static int Sync(bool modHere, ISet<string> known, IEnumerable<string> names)
    {
        if (modHere)
        {
            return 0;
        }

        int forgotten = 0;
        foreach (string name in names)
        {
            if (known.Remove(name))
            {
                forgotten++;
            }
        }

        return forgotten;
    }
}
