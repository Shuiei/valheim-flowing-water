using System.Collections.Generic;
using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Where running water is heard: water deep and fast enough, how steeply it runs, each brick's strongest places spaced
/// apart, and the few round the listener that sound.
/// </summary>
public class SoundSpotsTests
{
    [Theory]
    [InlineData(0.03f, 1.2f, true)]
    [InlineData(0.5f, 3f, true)]
    [InlineData(0.029f, 3f, false)]
    [InlineData(0.5f, 1.19f, false)]
    public void WaterIsHeardFromThreeCentimetresDeepAndOnePointTwoMetresASecond(float depth, float speed, bool heard)
    {
        Assert.Equal(heard, SoundSpots.Qualifies(depth, speed));
    }

    [Fact]
    public void WaterRunsAsSteeplyAsItFallsOverHowFastItRunsAcross()
    {
        Assert.Equal(0f, SoundSpots.Steep(2f, 0f));
        Assert.Equal(0f, SoundSpots.Steep(2f, 1f));
        Assert.Equal(0.5f, SoundSpots.Steep(2f, -1f), 5);
        Assert.Equal(SoundSpots.Steepest, SoundSpots.Steep(0f, -5f));
    }

    [Fact]
    public void MoreWaterAndSteeperScoreHigher()
    {
        Assert.Equal(0.5f, SoundSpots.Score(0.5f, 0f));
        Assert.True(SoundSpots.Score(1f, 0f) > SoundSpots.Score(0.5f, 0f));
        Assert.True(SoundSpots.Score(0.5f, 1f) > SoundSpots.Score(0.5f, 0.2f));
    }

    [Fact]
    public void ABrickGivesItsStrongestPlacesSpacedApartAtTheTopOfTheirWater()
    {
        // Still water all along a row, and four fast voxels: the fastest two within a metre of each other, the others
        // 8 and 14 m off; a fast film too thin to hear.
        var view = new BrickView(0, 0, 0, 1f);
        for (int x = 0; x < Brick.Size; x++)
        {
            view.Water[Brick.Index(x, 2, 5)] = 0.5f;
        }

        // (Half full: their flow over a face is half their speed.)
        Fast(view, 1, 1.5f);
        Fast(view, 2, 1.45f);
        Fast(view, 9, 1f);
        Fast(view, 15, 0.75f);
        int film = Brick.Index(12, 2, 5);
        view.Water[film] = 0.02f;
        view.VelocityX[film] = 5f;

        List<Spot> spots = SoundSpots.Of(view);

        Assert.Equal([1.5f, 9.5f, 15.5f], spots.Select(s => s.X));
        Assert.All(spots, s => Assert.Equal(2.5f, s.Y, 4));
        Assert.All(spots, s => Assert.Equal(5.5f, s.Z, 4));
        Assert.Equal(1.5f, spots[0].Flow, 4);
        Assert.Single(SoundSpots.Of(Slow(view)));
    }

    [Fact]
    public void AFallIsHeardSteep()
    {
        // A tenth of a voxel falling, its flow 0.6 m/s over a face: the water falls at 6 m/s, 0.6 m² passing a second.
        var view = new BrickView(0, 0, 0, 1f);
        int i = Brick.Index(5, 8, 5);
        view.Water[i] = 0.1f;
        view.VelocityY[i] = -0.6f;

        Spot spot = Assert.Single(SoundSpots.Of(view));

        Assert.Equal(SoundSpots.Steepest, spot.Steep);
        Assert.Equal(0.6f, spot.Flow, 4);
    }

    [Fact]
    public void TheStrongestWithinEarshotSoundAtMostSoManyAndNoTwoClose()
    {
        // Ten places 10 m apart along x, scores rising with x; one strong place 40 m off; one 5 m from the strongest.
        var candidates = Enumerable.Range(0, 10).Select(i => new Spot(i * 10f, 0f, 0f, 0.1f * (i + 1), 0f)).ToList();
        candidates.Add(new Spot(0f, 0f, 40f, 9f, 0f));
        candidates.Add(new Spot(90f, 0f, 5f, 0.95f, 0f));
        var chosen = new List<Spot>();

        SoundSpots.Pick(candidates, (45f, 0f, 0f), 35f, 3, chosen);

        Assert.Equal([80f, 70f, 60f], chosen.Select(s => s.X));
    }

    [Fact]
    public void APickForgetsTheLastOne()
    {
        var chosen = new List<Spot> { new(1f, 1f, 1f, 1f, 0f) };

        SoundSpots.Pick([new Spot(0f, 0f, 0f, 1f, 0f)], null, float.PositiveInfinity, 2, chosen);

        Assert.Single(chosen);
        Assert.Equal(0f, chosen[0].X);
    }

    private static void Fast(BrickView view, int x, float flow) => view.VelocityX[Brick.Index(x, 2, 5)] = flow;

    // The same brick with all but its fastest voxel's flow under 0.6 m/s: half full, slower than 1.2 m/s.
    private static BrickView Slow(BrickView view)
    {
        var slow = new BrickView(0, 0, 0, 1f);
        System.Array.Copy(view.Water, slow.Water, Brick.Count);
        System.Array.Copy(view.VelocityX, slow.VelocityX, Brick.Count);
        foreach (int x in new[] { 2, 9, 15 })
        {
            slow.VelocityX[Brick.Index(x, 2, 5)] = 0.59f;
        }

        return slow;
    }
}
