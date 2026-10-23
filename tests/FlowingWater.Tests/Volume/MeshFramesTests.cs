using System;
using System.Collections.Generic;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// A surface's normals, tangents and bounds as the game's mesh would work them out: level water faces up with its
/// tangent east and w -1 (as the game's flat meshes), a vertex shared by two faces takes their mean (smooth, each face
/// by its area), a face drawn the other way faces down, and the bounds hold every vertex.
/// </summary>
public class MeshFramesTests
{
    // Two triangles of a 1 m square on level water at height 2, clockwise from above.
    private static readonly List<Vec3> Square = [new(0f, 2f, 0f), new(0f, 2f, 1f), new(1f, 2f, 1f), new(1f, 2f, 0f)];

    private static readonly List<int> SquareTriangles = [0, 1, 2, 0, 2, 3];

    [Fact]
    public void LevelWaterFacesUpWithItsTangentEast()
    {
        Vec3[] normals = MeshFrames.Normals(Square, SquareTriangles);
        (Vec3 Tangent, float W)[] tangents = MeshFrames.Tangents(Square, SquareTriangles, normals, Uvs(Square));

        Assert.All(normals, n => Close(new Vec3(0f, 1f, 0f), n));
        Assert.All(tangents, t =>
        {
            Close(new Vec3(1f, 0f, 0f), t.Tangent);
            Assert.Equal(-1f, t.W);
        });
    }

    [Fact]
    public void AFaceDrawnTheOtherWayFacesDown()
    {
        Vec3[] normals = MeshFrames.Normals(Square, [0, 2, 1]);

        Close(new Vec3(0f, -1f, 0f), normals[0]);
        Assert.Equal(default, normals[3]);
    }

    [Fact]
    public void AVertexSharedByTwoFacesTakesTheirMean()
    {
        // A ridge along z at x = 1, its two slopes at 45 degrees, each 1 m by 1 m in plan and cut alike: the ridge's
        // vertices face straight up, the foot of each slope the slope's own way.
        List<Vec3> ridge = [new(0f, 0f, 0f), new(0f, 0f, 1f), new(1f, 1f, 1f), new(1f, 1f, 0f), new(2f, 0f, 1f), new(2f, 0f, 0f)];
        List<int> triangles = [0, 1, 2, 0, 2, 3, 2, 4, 5, 2, 5, 3];

        Vec3[] normals = MeshFrames.Normals(ridge, triangles);

        float s = (float)Math.Sqrt(0.5);
        Close(new Vec3(0f, 1f, 0f), normals[2]);
        Close(new Vec3(0f, 1f, 0f), normals[3]);
        Close(new Vec3(-s, s, 0f), normals[0]);
        Close(new Vec3(s, s, 0f), normals[5]);

        // The tangent follows the slope (u east), square to the normal.
        (Vec3 Tangent, float W)[] tangents = MeshFrames.Tangents(ridge, triangles, normals, Uvs(ridge));
        Close(new Vec3(s, s, 0f), tangents[0].Tangent);
        Assert.Equal(0f, tangents[0].Tangent.Dot(normals[0]), 5);
    }

    [Fact]
    public void ABiggerFaceCountsMore()
    {
        // A vertex shared by a level face 4 m² and a vertical one 1 m² (facing south): the normal leans up.
        List<Vec3> vertices = [new(0f, 0f, 0f), new(0f, 0f, 2f), new(2f, 0f, 2f), new(1f, 1f, 0f), new(1f, 0f, 0f)];
        Vec3[] normals = MeshFrames.Normals(vertices, [0, 1, 2, 0, 3, 4]);

        Assert.True(normals[0].Y > -normals[0].Z * 3f, $"{normals[0].X}, {normals[0].Y}, {normals[0].Z}");
        Assert.Equal(1f, (float)Math.Sqrt(normals[0].Dot(normals[0])), 5);
    }

    [Fact]
    public void TheBoundsHoldEveryVertex()
    {
        (Vec3 min, Vec3 max) = MeshFrames.Bounds([new(1f, -2f, 3f), new(-4f, 5f, 0f), new(2f, 0f, -1f)]);

        Assert.Equal(new Vec3(-4f, -2f, -1f), min);
        Assert.Equal(new Vec3(2f, 5f, 3f), max);
        Assert.Equal((default(Vec3), default(Vec3)), MeshFrames.Bounds([]));
    }

    // Texture coordinates as the bricks give them: east and north, shifted with height.
    private static List<(float U, float V)> Uvs(List<Vec3> vertices) => vertices.ConvertAll(v => (v.X, v.Z));

    private static void Close(Vec3 expected, Vec3 actual)
    {
        Assert.Equal(expected.X, actual.X, 4);
        Assert.Equal(expected.Y, actual.Y, 4);
        Assert.Equal(expected.Z, actual.Z, 4);
    }
}
