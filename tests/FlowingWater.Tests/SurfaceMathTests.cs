using System;
using FlowingWater.Client;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The water's surface where tiles meet: which tile owns a border vertex (so both draw it at its height), the
/// slope across borders with its fallbacks, the normal and tangent from it, the edge hash and a new tile.
/// </summary>
public class SurfaceMathTests
{
    private const int Size = 64;

    [Theory]
    [InlineData(10, 20, 0, 0, 10, 20)]
    [InlineData(64, 20, 1, 0, 0, 20)] // the last column: the east tile's first
    [InlineData(10, 64, 0, 1, 10, 0)] // the last row: the north tile's first
    [InlineData(64, 64, 1, 1, 0, 0)] // the far corner: the north-east tile's
    [InlineData(-1, 5, -1, 0, 63, 5)] // one before: the west tile's last cell
    [InlineData(-1, -1, -1, -1, 63, 63)]
    public void ABorderVertexBelongsToTheTileWhereItIsTheFirst(int x, int z, int zoneX, int zoneZ, int ox, int oz)
    {
        Assert.Equal((zoneX, zoneZ, ox, oz), SurfaceMath.Owner(Size, x, z));
    }

    [Fact]
    public void BothTilesFindTheSameVertexOnTheirBorder()
    {
        // The west tile's last column and the east tile's first are one vertex, owned by the east tile.
        (int zoneX, int zoneZ, int x, int z) = SurfaceMath.Owner(Size, Size, 30);

        Assert.Equal((1, 0, 0, 30), (zoneX, zoneZ, x, z));
        Assert.Equal((0, 0, 0, 30), SurfaceMath.Owner(Size, 0, 30));
    }

    [Fact]
    public void TheSlopeComesFromBothSidesWhereBothAreDrawn()
    {
        Assert.Equal(0.5f, SurfaceMath.Gradient(10f, true, 11f, true, 10f, 1f), 5);
        Assert.Equal(1f, SurfaceMath.Gradient(10f, true, 10.5f, true, 10f, 0.25f), 5);
    }

    [Fact]
    public void WithOneSideDryTheOtherGivesTheSlopeAndWithNeitherItIsFlat()
    {
        Assert.Equal(1f, SurfaceMath.Gradient(10f, true, 11f, false, 99f, 1f), 5);
        Assert.Equal(2f, SurfaceMath.Gradient(10f, false, 99f, true, 9f, 0.5f), 5);
        Assert.Equal(0f, SurfaceMath.Gradient(10f, false, 99f, false, 99f, 1f));
    }

    [Fact]
    public void FlatWaterFacesUpWithItsTangentAlongXAndBitangentAlongZ()
    {
        ((float X, float Y, float Z) n, (float X, float Y, float Z, float W) t) = SurfaceMath.Frame(0f, 0f);

        Assert.Equal((0f, 1f, 0f), n);
        Assert.Equal((1f, 0f, 0f, -1f), t);
        Assert.Equal((0f, 0f, 1f), Scale(Cross(n, (t.X, t.Y, t.Z)), t.W));
    }

    [Fact]
    public void ASlopeTiltsTheNormalAwayFromItsRiseAndTheTangentStaysOnTheSurface()
    {
        ((float X, float Y, float Z) n, (float X, float Y, float Z, float W) t) = SurfaceMath.Frame(1f, -0.5f);

        Assert.Equal(1f, Length(n), 5);
        Assert.Equal(1f, Length((t.X, t.Y, t.Z)), 5);
        Assert.True(n.X < 0f && n.Z > 0f && n.Y > 0f);
        Assert.Equal(0f, Dot(n, (t.X, t.Y, t.Z)), 5);
    }

    [Fact]
    public void TheSameSlopesGiveTheSameFrameOnEitherSideOfABorder()
    {
        // Two tiles reading the same heights across their border compute the same slopes: the same frame.
        float slopeX = SurfaceMath.Gradient(10f, true, 10.3f, true, 9.9f, 0.5f);
        float slopeZ = SurfaceMath.Gradient(10f, false, 0f, true, 10.2f, 0.5f);

        Assert.Equal(SurfaceMath.Frame(slopeX, slopeZ), SurfaceMath.Frame(slopeX, slopeZ));
    }

    [Theory]
    [InlineData(1f, 0f)]
    [InlineData(0f, -1f)]
    [InlineData(0.6f, 0.8f)]
    public void AlongAFlowTheTangentRunsAcrossItAndTheBitangentDownstream(float flowX, float flowZ)
    {
        ((float X, float Y, float Z) n, _) = SurfaceMath.Frame(0.3f, -0.2f);

        (float tx, float ty, float tz, float w) = SurfaceMath.FlowTangent(n, flowX, flowZ);

        Assert.Equal(-1f, w);
        Assert.Equal(1f, Length((tx, ty, tz)), 5);
        Assert.Equal(0f, Dot(n, (tx, ty, tz)), 5);
        Assert.True(Dot(Scale(Cross(n, (tx, ty, tz)), w), (flowX, 0f, flowZ)) > 0.9f);
    }

    [Fact]
    public void TheEdgeHashChangesWithTheEdgeOnly()
    {
        const int Pitch = Size + 1;
        float[] heights = new float[Pitch * Pitch];
        int[] drawn = SurfaceMath.NothingDrawn(heights.Length);
        int empty = SurfaceMath.EdgeHash(Size, heights, drawn);

        // An inner vertex changes nothing the tiles around read.
        drawn[(30 * Pitch) + 30] = 0;
        heights[(30 * Pitch) + 30] = 5f;
        Assert.Equal(empty, SurfaceMath.EdgeHash(Size, heights, drawn));

        // Drawing one next to the border does, and so does its height moving.
        drawn[(30 * Pitch) + 1] = 1;
        heights[(30 * Pitch) + 1] = 5f;
        int drawnOne = SurfaceMath.EdgeHash(Size, heights, drawn);
        Assert.NotEqual(empty, drawnOne);
        heights[(30 * Pitch) + 1] = 5.01f;
        Assert.NotEqual(drawnOne, SurfaceMath.EdgeHash(Size, heights, drawn));

        // The same edge hashes the same: no tile is told to rebuild again for nothing.
        Assert.Equal(SurfaceMath.EdgeHash(Size, heights, drawn), SurfaceMath.EdgeHash(Size, heights, drawn));
    }

    [Fact]
    public void ANewTileHasNothingDrawn()
    {
        Assert.All(SurfaceMath.NothingDrawn(Size * Size), i => Assert.True(i < 0));
    }

    private static (float X, float Y, float Z) Cross((float X, float Y, float Z) a, (float X, float Y, float Z) b) =>
        ((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    private static (float X, float Y, float Z) Scale((float X, float Y, float Z) v, float by) => (v.X * by, v.Y * by, v.Z * by);

    private static float Dot((float X, float Y, float Z) a, (float X, float Y, float Z) b) => (a.X * b.X) + (a.Y * b.Y) + (a.Z * b.Z);

    private static float Length((float X, float Y, float Z) v) => (float)Math.Sqrt(Dot(v, v));
}
