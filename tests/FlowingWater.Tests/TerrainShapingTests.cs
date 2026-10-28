using System;
using System.Linq;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// A zone's ground shaped by the game's terrain modifiers: levelled to a modifier's height over a square or a circle of
/// vertices round it, smoothed toward it the more the nearer, in the game's order, a player's shaping piece held near
/// the ground it found; one beyond its reach of the zone changes nothing, one in the zone beside reaches over the edge.
/// </summary>
public class TerrainShapingTests
{
    private const int Side = TerrainShaping.Width + 1;

    // A zone centred at the world's origin: vertex (32, 32) is at x = 0, z = 0.
    private const float Centre = 0f;

    [Fact]
    public void ASquareLevelSetsEveryVertexWithinItsReachToItsHeight()
    {
        float[] heights = Flat(10f);

        TerrainShaping.Shape(heights, Centre, Centre, [Level(x: 0f, z: 0f, y: 14f, radius: 2f, square: true, offset: 0.5f)]);

        Assert.Equal(14.5f, At(heights, 32, 32));
        Assert.Equal(14.5f, At(heights, 34, 34));
        Assert.Equal(14.5f, At(heights, 30, 34));
        Assert.Equal(10f, At(heights, 35, 32));
        Assert.Equal(10f, At(heights, 32, 29));
    }

    [Fact]
    public void ARoundLevelLeavesTheSquaresCorners()
    {
        float[] heights = Flat(10f);

        TerrainShaping.Shape(heights, Centre, Centre, [Level(x: 0f, z: 0f, y: 14f, radius: 2f, square: false)]);

        Assert.Equal(14f, At(heights, 34, 32));
        Assert.Equal(10f, At(heights, 34, 34));
    }

    [Fact]
    public void SmoothingDrawsTowardTheHeightWhollyAtTheModifierAndNotAtTheEdgeOfItsReach()
    {
        float[] heights = Flat(10f);
        var smooth = new TerrainShaping.Modifier { Y = 20f, Smooth = true, SmoothRadius = 4f, SmoothPower = 3f, Radius = 4f };

        TerrainShaping.Shape(heights, Centre, Centre, [smooth]);

        Assert.Equal(20f, At(heights, 32, 32));
        // Two vertices away: half the reach, an eighth of the way from it (the cube), so seven eighths toward.
        Assert.Equal(10f + (10f * (1f - 0.125f)), At(heights, 34, 32), 4);
        Assert.Equal(10f, At(heights, 36, 32), 4);
        Assert.Equal(10f, At(heights, 37, 32));
    }

    [Fact]
    public void ModifiersGoInTheGamesOrderTheLaterOverTheEarlier()
    {
        float[] heights = Flat(10f);
        TerrainShaping.Modifier late = Level(x: 0f, z: 0f, y: 30f, radius: 1f, square: true);
        late.SortOrder = 1;
        TerrainShaping.Modifier early = Level(x: 0f, z: 0f, y: 20f, radius: 1f, square: true);
        TerrainShaping.Modifier earlier = Level(x: 0f, z: 0f, y: 25f, radius: 1f, square: true);
        earlier.Created = -1;

        // Whatever the list's order: by sort order first, then by when made.
        TerrainShaping.Shape(heights, Centre, Centre, [late, early, earlier]);

        Assert.Equal(30f, At(heights, 32, 32));
        heights = Flat(10f);
        TerrainShaping.Shape(heights, Centre, Centre, [early, earlier]);
        Assert.Equal(20f, At(heights, 32, 32));
    }

    [Fact]
    public void APlayersShapingPieceIsHeldNearTheGroundItFoundAndGoesAfterTheLocations()
    {
        float[] heights = Flat(10f);
        TerrainShaping.Modifier piece = Level(x: 0f, z: 0f, y: 40f, radius: 1f, square: true);
        piece.PlayerMade = true;
        piece.Smooth = true;
        piece.SmoothRadius = 3f;
        piece.Radius = 3f;
        TerrainShaping.Modifier yard = Level(x: 0f, z: 0f, y: 12f, radius: 4f, square: true);

        TerrainShaping.Shape(heights, Centre, Centre, [piece, yard]);

        // The yard first (12 m), then the piece: levelled to 40 m but held within 8 m of the 12 m it found (20 m),
        // then smoothed toward 40 m but held within a metre of what its levelling left (21 m); outside its levelled
        // square, within a metre of the yard's 12 m.
        Assert.Equal(21f, At(heights, 32, 32));
        Assert.Equal(13f, At(heights, 34, 32), 4);
    }

    [Fact]
    public void AModifierBeyondItsReachOfTheZoneChangesNothingAndOneBesideReachesOverTheEdge()
    {
        float[] heights = Flat(10f);
        // Its 2 m levelling and the game's 4 m margin end short of the zone's edge at x = 32.
        TerrainShaping.Modifier far = Level(x: 38.5f, z: 0f, y: 30f, radius: 2f, square: true);
        TerrainShaping.Modifier near = Level(x: 33f, z: 0f, y: 30f, radius: 2f, square: true);

        TerrainShaping.Shape(heights, Centre, Centre, [far]);
        Assert.All(heights, h => Assert.Equal(10f, h));

        TerrainShaping.Shape(heights, Centre, Centre, [near]);
        Assert.Equal(30f, At(heights, 64, 32));
        Assert.Equal(30f, At(heights, 63, 32));
        Assert.Equal(10f, At(heights, 62, 32));
    }

    [Fact]
    public void HeightsOfAnotherSizeAreRefused()
    {
        Assert.Throws<ArgumentException>(() => TerrainShaping.Shape(new float[10], Centre, Centre, []));
    }

    private static TerrainShaping.Modifier Level(float x, float z, float y, float radius, bool square, float offset = 0f) => new()
    {
        X = x,
        Y = y,
        Z = z,
        LevelOffset = offset,
        Level = true,
        LevelRadius = radius,
        Square = square,
        Radius = radius,
    };

    private static float[] Flat(float height) => [.. Enumerable.Repeat(height, Side * Side)];

    private static float At(float[] heights, int x, int z) => heights[(z * Side) + x];
}
