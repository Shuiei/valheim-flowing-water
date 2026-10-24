using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// Placed sources and drains show only while a water piece of the hoe's is chosen or the hammer is in hand, unless the
/// hoe has no remover; the remover removes them and nothing else, the hammer as any piece, and the hoe's remove key
/// removes nothing it did not before.
/// </summary>
public class HoeRemovalTests
{
    [Theory]
    [InlineData(false, false, false, false)]
    [InlineData(false, true, true, false)]
    [InlineData(true, false, true, true)]
    public void TheHoeShowsThemWithAWaterPieceAndMarksTheAimedOneWithTheRemover(bool remover, bool waterPiece, bool shown, bool marked)
    {
        Assert.Equal((shown, marked), HoeRemoval.Sight(hoeInHand: true, remover, waterPiece, toolRemovesPieces: false));
    }

    [Fact]
    public void TheHoeWithAnotherPieceHidesThemEvenWhereItRemovesPieces()
    {
        Assert.Equal((false, false), HoeRemoval.Sight(hoeInHand: true, removerChosen: false, waterPieceChosen: false, toolRemovesPieces: true));
    }

    [Fact]
    public void TheHammerShowsThemAndMarksTheAimedOneToBreakAsAnyPiece()
    {
        Assert.Equal((true, true), HoeRemoval.Sight(hoeInHand: false, removerChosen: false, waterPieceChosen: false, toolRemovesPieces: true));
    }

    [Fact]
    public void AToolThatRemovesNothingLeavesThemHidden()
    {
        Assert.Equal((false, false), HoeRemoval.Sight(hoeInHand: false, removerChosen: false, waterPieceChosen: false, toolRemovesPieces: false));
    }

    [Theory]
    [InlineData(true, true, true)]
    [InlineData(false, true, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    public void PlacedOnesShowWithAWaterPieceChosenOrWhenTheHoeHasNoRemover(bool chosen, bool ready, bool shown)
    {
        Assert.Equal(shown, HoeRemoval.Shown(chosen, ready));
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void AnotherToolRemovesWhatItDoes(bool removing, bool marker)
    {
        Assert.True(HoeRemoval.MayRemove(hoeInHand: false, hoeRemovedBefore: false, removing, marker));
    }

    [Fact]
    public void TheRemoverRemovesASourceOrDrain()
    {
        Assert.True(HoeRemoval.MayRemove(hoeInHand: true, hoeRemovedBefore: false, removing: true, aimedIsMarker: true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheRemoverRemovesNothingElseEvenWhereTheHoeRemovedBefore(bool removedBefore)
    {
        Assert.False(HoeRemoval.MayRemove(hoeInHand: true, removedBefore, removing: true, aimedIsMarker: false));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheHoeWithAnotherPieceRemovesNothingButWhereItRemovedBefore(bool marker)
    {
        Assert.False(HoeRemoval.MayRemove(hoeInHand: true, hoeRemovedBefore: false, removing: false, marker));
        Assert.True(HoeRemoval.MayRemove(hoeInHand: true, hoeRemovedBefore: true, removing: false, marker));
    }
}
