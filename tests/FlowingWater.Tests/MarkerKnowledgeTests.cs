using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// A character keeps the hoe's source and drain learned where FlowingWater is and forgets them, only them, on a server
/// without it; back where the mod is, nothing is taken away, so the game teaches them again.
/// </summary>
public class MarkerKnowledgeTests
{
    private static readonly string[] Markers = ["Water source", "Water drain"];

    [Fact]
    public void WhereTheModIsACharacterKeepsWhatItKnows()
    {
        var known = new HashSet<string>(["Water source", "Water drain", "$piece_wood_floor"]);

        Assert.Equal(0, MarkerKnowledge.Sync(modHere: true, known, Markers));
        Assert.Equal(3, known.Count);
    }

    [Fact]
    public void OnAServerWithoutTheModACharacterForgetsTheSourceAndDrainOnly()
    {
        var known = new HashSet<string>(["Water source", "Water drain", "$piece_wood_floor", "$item_hoe"]);

        Assert.Equal(2, MarkerKnowledge.Sync(modHere: false, known, Markers));
        Assert.Equal(["$item_hoe", "$piece_wood_floor"], [.. known.Order()]);
    }

    [Fact]
    public void ACharacterThatNeverLearnedThemHasNothingToForget()
    {
        var known = new HashSet<string>(["$piece_wood_floor"]);

        Assert.Equal(0, MarkerKnowledge.Sync(modHere: false, known, Markers));
        Assert.Single(known);
    }

    // Joining a server that says nothing, then one that runs FlowingWater: forgotten, then left to be taught again.
    [Fact]
    public void ACharacterForgetsThemOnAServerWithoutTheModAndIsLeftToRelearnThemOnOneWithIt()
    {
        var rights = new CommandRights();
        var known = new HashSet<string>(Markers);

        rights.Joining();
        MarkerKnowledge.Sync(rights.ModHere(isServer: false), known, Markers);
        Assert.Empty(known);

        rights.Joining();
        rights.Tell(true);
        known.UnionWith(Markers);
        Assert.Equal(0, MarkerKnowledge.Sync(rights.ModHere(isServer: false), known, Markers));
        Assert.Equal(2, known.Count);
    }
}
