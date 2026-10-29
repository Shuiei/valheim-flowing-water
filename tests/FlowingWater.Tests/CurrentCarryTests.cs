using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// How the current carries a floating thing: toward the water's speed along the current, a light thing quickly and a
/// boat slowly, never braking one that runs faster down it, holding back one going up it.
/// </summary>
public class CurrentCarryTests
{
    [Theory]
    [InlineData(1f, 0.5f)]
    [InlineData(8f, 1f)]
    [InlineData(1000f, 5f)]
    [InlineData(0.001f, CurrentCarry.QuickestSeconds)]
    [InlineData(1e6f, CurrentCarry.SlowestSeconds)]
    public void AThingCatchesUpWithTheWaterInTimeGrowingAsTheCubeRootOfItsMass(float mass, float seconds)
    {
        Assert.Equal(seconds, CurrentCarry.Seconds(mass), 3);
    }

    [Fact]
    public void ACrateIsPulledFasterThanABoatAndFasterWaterPullsHarder()
    {
        (float crate, _) = CurrentCarry.Pull(2f, 0f, 0f, 0f, 10f, 1f, 0f);
        (float boat, _) = CurrentCarry.Pull(2f, 0f, 0f, 0f, 1000f, 1f, 0f);
        (float faster, _) = CurrentCarry.Pull(4f, 0f, 0f, 0f, 10f, 1f, 0f);

        Assert.True(crate > 4f * boat);
        Assert.Equal(2f * crate, faster, 4);
    }

    [Fact]
    public void ItPullsAlongTheCurrentByWhatTheThingLacksOfItsSpeed()
    {
        // Water at 3 m/s north, a 1 kg thing at 1 m/s north and 2 m/s east: 2 m/s lacking over 0.5 s, all north.
        (float x, float z) = CurrentCarry.Pull(0f, 3f, 2f, 1f, 1f, 1f, 0f);

        Assert.Equal(0f, x, 4);
        Assert.Equal(4f, z, 4);
    }

    [Fact]
    public void AThingAsFastOrFasterDownTheCurrentIsNotPulled()
    {
        Assert.Equal((0f, 0f), CurrentCarry.Pull(2f, 0f, 2f, 0f, 1000f, 1f, 0f));
        Assert.Equal((0f, 0f), CurrentCarry.Pull(2f, 0f, 6f, 1f, 1000f, 1f, 0f));
    }

    [Fact]
    public void ABoatGoingUpTheCurrentIsHeldBackHarder()
    {
        (float still, _) = CurrentCarry.Pull(2f, 0f, 0f, 0f, 1000f, 1f, 0f);
        (float up, _) = CurrentCarry.Pull(2f, 0f, -3f, 0f, 1000f, 1f, 0f);

        Assert.Equal(2.5f * still, up, 4);
    }

    [Fact]
    public void TheGamesOwnDragAlongTheCurrentIsGivenBackWhileCarried()
    {
        // A 1 kg thing at 1 m/s in water at 3 m/s, the game braking it 2.56 m/s²: 4 m/s² for what it lacks, and that.
        (float x, _) = CurrentCarry.Pull(3f, 0f, 1f, 0f, 1f, 1f, 2.56f);
        (float fast, _) = CurrentCarry.Pull(3f, 0f, 3f, 0f, 1f, 1f, 2.56f);

        Assert.Equal(4f + 2.56f, x, 4);
        Assert.Equal(0f, fast);
    }

    [Fact]
    public void TheDragsBrakingIsItsDragOnEachAxisBroughtOntoTheCurrent()
    {
        // A floating thing, the same both ways: its drag on its speed along the current.
        Assert.Equal(2.56f, CurrentCarry.Braking(3f, 0f, 1f, 0f, 1f, 0f, 2.56f, 2.56f), 4);
        Assert.Equal(2.56f, CurrentCarry.Braking(0f, 3f, 0f, 1f, 1f, 0f, 2.56f, 2.56f), 4);

        // A ship pointing north in water running east, drifting east at 1 m/s: its sideways drag, not its hull's.
        Assert.Equal(7.6f, CurrentCarry.Braking(2f, 0f, 1f, 0f, 0f, 1f, 0.05f, 7.6f), 4);

        // Pointing down the current: its hull's drag only.
        Assert.Equal(0.05f, CurrentCarry.Braking(2f, 0f, 1f, 0f, 1f, 0f, 0.05f, 7.6f), 4);

        // Going up the current, or across it only: nothing to give back.
        Assert.Equal(0f, CurrentCarry.Braking(2f, 0f, -1f, 0f, 0f, 1f, 0.05f, 7.6f));
        Assert.Equal(0f, CurrentCarry.Braking(2f, 0f, 0f, 1f, 0f, 1f, 0.05f, 7.6f), 4);
    }

    [Fact]
    public void AShareOfItsSpeedTakenEachStepIsADragRate()
    {
        Assert.Equal(2.565f, CurrentCarry.DragRate(0.05f, 0.02f), 3);
        Assert.Equal(1.523f, CurrentCarry.DragRate(0.03f, 0.02f), 3);
        Assert.Equal(0f, CurrentCarry.DragRate(0f, 0.02f));
        Assert.Equal(0f, CurrentCarry.DragRate(1f, 0.02f));
        Assert.Equal(0f, CurrentCarry.DragRate(0.05f, 0f));
    }

    [Fact]
    public void TheStrengthIsTheShareOfTheWatersSpeedAndStillWaterPullsNothing()
    {
        (float half, _) = CurrentCarry.Pull(2f, 0f, 0f, 0f, 1f, 0.5f, 0f);

        Assert.Equal(2f, half, 4);
        Assert.Equal((0f, 0f), CurrentCarry.Pull(2f, 0f, 0f, 0f, 1f, 0f, 0f));
        Assert.Equal((0f, 0f), CurrentCarry.Pull(0f, 0f, 1f, 0f, 1f, 1f, 0f));
    }
}
