using System;
using System.Collections.Generic;
using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Collider meshes as solids: a cube mesh rasters as the same box, however it is wound or turned; an arch leaves
/// room under it; a cone open at its foot is solid down to the ground; a thin triangle wall closes the faces it
/// crosses; a mesh is placed by its matrix; and a big mesh is tested quickly.
/// </summary>
public class SolidMeshTests
{
    [Fact]
    public void ACubeMeshRastersAsTheSameBox()
    {
        var box = SolidBox.Between(new Vec3(2f, 0f, 2f), new Vec3(4f, 2.3f, 4f));
        var withBox = new Brick(0, 0, 0);
        SolidRaster.Pieces(withBox, 1f, [box]);
        var withMesh = new Brick(0, 0, 0);

        SolidRaster.Pieces(withMesh, 1f, [Cube(new Vec3(2f, 0f, 2f), new Vec3(4f, 2.3f, 4f))]);

        Assert.Equal(1f, withMesh.Solid[Brick.Index(3, 1, 3)]);
        Assert.Equal(0.3f, withMesh.Solid[Brick.Index(3, 2, 3)], 4);
        Assert.Equal(withBox.Closed, withMesh.Closed);
        for (int i = 0; i < Brick.Count; i++)
        {
            Assert.Equal(withBox.Solid[i], withMesh.Solid[i], 4);
            Assert.Equal(withBox.Ceiling[i], withMesh.Ceiling[i], 4);
        }
    }

    [Fact]
    public void ACubeWoundInsideOutIsSolidInsideAllTheSame()
    {
        var brick = new Brick(0, 0, 0);

        SolidRaster.Pieces(brick, 1f, [Cube(new Vec3(2f, 0f, 2f), new Vec3(4f, 2.3f, 4f), insideOut: true)]);

        Assert.Equal(1f, brick.Solid[Brick.Index(3, 1, 3)]);
        Assert.Equal(0.3f, brick.Solid[Brick.Index(3, 2, 3)], 4);
        Assert.Equal(0f, brick.Solid[Brick.Index(5, 1, 3)]);
    }

    [Fact]
    public void AThinFloorMeshInAVoxelsUpperHalfIsItsCeiling()
    {
        // The same plank as the box tests' floor: top 0.6 m into row 7.
        var brick = new Brick(0, 0, 0);

        SolidRaster.Pieces(brick, 1f, [Cube(new Vec3(2f, 7.47f, 2f), new Vec3(4f, 7.6f, 4f))]);

        Assert.Equal(0.6f, brick.Solid[Brick.Index(3, 7, 3)], 3);
        Assert.Equal(Brick.ClosedY, brick.Closed[Brick.Index(3, 6, 3)] & Brick.ClosedY);
        Assert.Equal(0f, brick.Solid[Brick.Index(3, 8, 3)]);
    }

    [Fact]
    public void AnArchLeavesRoomUnderItAndIsACeilingThere()
    {
        // Two piers 2 m high at x 2..3 and 6..7, and a lintel from 2 to 3 m over the gap between them.
        var brick = new Brick(0, 0, 0);
        SolidRaster.Ground(brick, 1f, (_, _) => 0f);

        SolidRaster.Pieces(brick, 1f, [
            Cube(new Vec3(2f, 0f, 2f), new Vec3(3f, 2f, 6f)),
            Cube(new Vec3(6f, 0f, 2f), new Vec3(7f, 2f, 6f)),
            Cube(new Vec3(2f, 2f, 2f), new Vec3(7f, 3f, 6f)),
        ], (_, _) => 0f);

        // Under the lintel: open voxels in rows 0 and 1, the lintel solid in row 2, air over it.
        Assert.Equal(0f, brick.Solid[Brick.Index(4, 0, 4)]);
        Assert.Equal(0f, brick.Solid[Brick.Index(4, 1, 4)]);
        Assert.Equal(1f, brick.Solid[Brick.Index(4, 2, 4)]);
        Assert.Equal(0f, brick.Solid[Brick.Index(4, 3, 4)]);
        Assert.Equal(1f, brick.Solid[Brick.Index(2, 1, 4)]);

        // The way under the arch, from west of it to east of it, is open; the way up into the lintel is closed.
        Assert.Equal(0, brick.Closed[Brick.Index(3, 0, 4)] & Brick.ClosedX);
        Assert.Equal(0, brick.Closed[Brick.Index(4, 0, 4)] & Brick.ClosedX);
        Assert.Equal(Brick.ClosedY, brick.Closed[Brick.Index(4, 1, 4)] & Brick.ClosedY);
    }

    [Fact]
    public void AnOverhangIsACeilingWhereItsUndersideIsInAVoxelsUpperHalf()
    {
        // A slab from 4.7 to 6 m: in row 4 only its upper half holds the slab (a ceiling at 0.7), row 5 is solid.
        var brick = new Brick(0, 0, 0);

        SolidRaster.Pieces(brick, 1f, [Cube(new Vec3(2f, 4.7f, 2f), new Vec3(4f, 6f, 4f))]);

        Assert.Equal(0f, brick.Solid[Brick.Index(3, 4, 3)]);
        Assert.Equal(0.7f, brick.Ceiling[Brick.Index(3, 4, 3)], 3);
        Assert.Equal(1f, brick.Solid[Brick.Index(3, 5, 3)]);
    }

    [Fact]
    public void AConeOpenAtItsFootIsSolidDownToTheGround()
    {
        // A tree trunk as its collider often is: a cone's side with no bottom face, tip 6 m up over (5, 5), 2 m wide at
        // its foot at 1 m. Under its side the column is solid down to the brick's bottom.
        var brick = new Brick(0, 0, 0);

        SolidRaster.Pieces(brick, 1f, [Cone(new Vec3(5.5f, 1f, 5.5f), 1f, 5f)]);

        // The column at the tip: solid from the tip down; no voxel below is left open.
        Assert.True(brick.Solid[Brick.Index(5, 5, 5)] > 0.9f, $"row 5: {brick.Solid[Brick.Index(5, 5, 5)]}");
        for (int y = 0; y < 5; y++)
        {
            Assert.Equal(1f, brick.Solid[Brick.Index(5, y, 5)]);
        }

        Assert.Equal(0f, brick.Solid[Brick.Index(5, 6, 5)], 4);
        Assert.Equal(0f, brick.Solid[Brick.Index(8, 1, 5)]);
    }

    [Fact]
    public void AThinTriangleWallClosesTheFacesItCrossesWithoutFillingVoxels()
    {
        // A single two-sided triangle standing on x = 10 from z 8 to 12, 4 m high: no thickness, so no voxel is filled,
        // but the faces across it are closed.
        var brick = new Brick(0, 0, 0);
        float[] xyz = [10f, 0f, 8f, 10f, 0f, 12f, 10f, 4f, 10f];
        var wall = new SolidMeshInstance(new SolidMesh(xyz, [0, 1, 2, 0, 2, 1]), Identity());

        SolidRaster.Pieces(brick, 1f, [wall]);

        Assert.Equal(Brick.ClosedX, brick.Closed[Brick.Index(9, 0, 9)] & Brick.ClosedX);
        Assert.Equal(Brick.ClosedX, brick.Closed[Brick.Index(9, 1, 10)] & Brick.ClosedX);
        Assert.Equal(0, brick.Closed[Brick.Index(9, 3, 8)] & Brick.ClosedX);
        Assert.All(brick.Solid, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void AMeshIsPlacedByItsMatrixTurnedAndScaled()
    {
        // A unit cube about its own origin, scaled 2 by 3 by 2, turned 90 degrees about y and put at (5, 1, 5): a box
        // from (4, -0.5, 4) to (6, 2.5, 6) in the world, whichever way the turn goes.
        var brick = new Brick(0, 0, 0);
        float[] toWorld = [0f, 0f, 2f, 5f, 0f, 3f, 0f, 1f, -2f, 0f, 0f, 5f];
        var cube = new SolidMeshInstance(Unit(), toWorld);
        (Vec3 min, Vec3 max) = cube.Bounds();

        SolidRaster.Pieces(brick, 1f, [cube]);

        Assert.Equal(4f, min.X, 2);
        Assert.Equal(-0.5f, min.Y, 2);
        Assert.Equal(4f, min.Z, 2);
        Assert.Equal(6f, max.X, 2);
        Assert.Equal(2.5f, max.Y, 2);
        Assert.Equal(6f, max.Z, 2);
        Assert.Equal(1f, brick.Solid[Brick.Index(4, 1, 5)]);
        Assert.Equal(0.5f, brick.Solid[Brick.Index(5, 2, 5)], 4);
        Assert.Equal(0f, brick.Solid[Brick.Index(6, 1, 5)]);
        Assert.Equal(0f, brick.Solid[Brick.Index(5, 1, 6)]);
    }

    [Fact]
    public void AMatrixThatFlattensTheMeshIsRefused()
    {
        float[] flat = [1f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f];

        Assert.Throws<ArgumentException>(() => new SolidMeshInstance(Unit(), flat));
    }

    [Fact]
    public void ABadMeshIsRefused()
    {
        Assert.Throws<ArgumentException>(() => new SolidMesh([0f, 0f, 0f, 1f, 0f, 0f, 0f, 1f, 0f], [0, 1, 3]));
        Assert.Throws<ArgumentException>(() => new SolidMesh([0f, 0f, 0f, 1f, 0f, 0f], [0, 1, 0]));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TwoShellsSharingAFaceAreSolidThroughBothWhicheverIsListedFirst(bool lowerFirst)
    {
        // A cube from 0 to 1 m under one from 1 to 2 m, one mesh: down the column the upper's bottom face and the
        // lower's top face lie at the same height, and the column is solid from 0 to 2 whichever the mesh lists first.
        (float[] lowerXyz, int[] lowerIndices) = CubeData(new Vec3(2f, 0f, 2f), new Vec3(4f, 1f, 4f), false);
        (float[] upperXyz, int[] upperIndices) = CubeData(new Vec3(2f, 1f, 2f), new Vec3(4f, 2f, 4f), false);
        (float[] firstXyz, int[] firstIndices) = lowerFirst ? (lowerXyz, lowerIndices) : (upperXyz, upperIndices);
        (float[] secondXyz, int[] secondIndices) = lowerFirst ? (upperXyz, upperIndices) : (lowerXyz, lowerIndices);
        float[] xyz = new float[48];
        int[] indices = new int[72];
        firstXyz.CopyTo(xyz, 0);
        secondXyz.CopyTo(xyz, 24);
        firstIndices.CopyTo(indices, 0);
        for (int i = 0; i < 36; i++)
        {
            indices[36 + i] = secondIndices[i] + 8;
        }

        var both = new SolidMeshInstance(new SolidMesh(xyz, indices), Identity());
        var spans = new List<(float Low, float High)>();

        both.Column(3f, 3f, 0f, 16f, spans);

        Assert.Equal(2f, spans.Sum(s => s.High - s.Low), 4);
        Assert.Equal(0f, spans.Min(s => s.Low), 4);
        Assert.Equal(2f, spans.Max(s => s.High), 4);
    }

    [Fact]
    public void AnOpenConeIsSolidInsideWhateverItsOriginIs()
    {
        // The cone's side only, below and away from the mesh's own origin (as a collider's mesh may be modelled): the
        // winding is judged from inside the cone, so it is still solid under its side.
        float[] toWorld = [1f, 0f, 0f, 20f, 0f, 1f, 0f, 30f, 0f, 0f, 1f, 20f];
        SolidMeshInstance cone = Cone(new Vec3(-14.5f, -29f, -14.5f), 1f, 5f, toWorld);
        var spans = new List<(float Low, float High)>();

        cone.Column(5.5f, 5.5f, 0f, 16f, spans);

        Assert.Single(spans);
        Assert.Equal(0f, spans[0].Low, 3);
        Assert.Equal(6f, spans[0].High, 2);
    }

    [Fact]
    public void AColumnThroughTwoSeparatePartsGivesBothSpans()
    {
        // A cube from 1 to 2 m over another from 0 to 0.5 m at the same place, one mesh: two spans down the column.
        float[] xyz = new float[48];
        int[] indices = new int[72];
        (float[] lowerXyz, int[] lowerIndices) = CubeData(new Vec3(2f, 0f, 2f), new Vec3(4f, 0.5f, 4f), false);
        (float[] upperXyz, int[] upperIndices) = CubeData(new Vec3(2f, 1f, 2f), new Vec3(4f, 2f, 4f), false);
        lowerXyz.CopyTo(xyz, 0);
        upperXyz.CopyTo(xyz, 24);
        lowerIndices.CopyTo(indices, 0);
        for (int i = 0; i < 36; i++)
        {
            indices[36 + i] = upperIndices[i] + 8;
        }

        var both = new SolidMeshInstance(new SolidMesh(xyz, indices), Identity());
        var spans = new List<(float Low, float High)>();

        both.Column(3f, 3f, 0f, 16f, spans);

        Assert.Equal(2, spans.Count);
        Assert.Equal((1f, 2f), spans[0]);
        Assert.Equal((0f, 0.5f), spans[1]);
    }

    [Fact]
    public void ABigMeshRastersABrickQuickly()
    {
        // A rough hill of 20 000 triangles over the brick: the raster of a brick under it, columns and faces, takes a
        // small fraction of a second.
        int n = 100;
        float[] xyz = new float[(n + 1) * (n + 1) * 3];
        for (int z = 0; z <= n; z++)
        {
            for (int x = 0; x <= n; x++)
            {
                int v = ((z * (n + 1)) + x) * 3;
                xyz[v] = x * 0.2f;
                xyz[v + 1] = 4f + (2f * MathF.Sin(x * 0.3f) * MathF.Cos(z * 0.2f));
                xyz[v + 2] = z * 0.2f;
            }
        }

        int[] indices = new int[n * n * 6];
        for (int z = 0; z < n; z++)
        {
            for (int x = 0; x < n; x++)
            {
                int a = (z * (n + 1)) + x;
                int i = ((z * n) + x) * 6;
                indices[i] = a;
                indices[i + 1] = a + n + 1;
                indices[i + 2] = a + 1;
                indices[i + 3] = a + 1;
                indices[i + 4] = a + n + 1;
                indices[i + 5] = a + n + 2;
            }
        }

        var hill = new SolidMeshInstance(new SolidMesh(xyz, indices), Identity());

        // Once warm (the first call pays for the test host's own first-time costs), then timed: about 20 ms on a
        // desktop; without the mesh's grid every face test would try every triangle, seconds.
        SolidRaster.Pieces(new Brick(0, 0, 0), 1f, [hill], (_, _) => 0f);
        var brick = new Brick(0, 0, 0);
        var watch = System.Diagnostics.Stopwatch.StartNew();

        SolidRaster.Pieces(brick, 1f, [hill], (_, _) => 0f);

        watch.Stop();
        Assert.True(watch.ElapsedMilliseconds < 500, $"{watch.ElapsedMilliseconds} ms");
        Assert.True(brick.Solid[Brick.Index(5, 1, 5)] == 1f, $"solid under the hill's top surface, open at its foot: {brick.Solid[Brick.Index(5, 1, 5)]}");
        Assert.True(brick.Solid.Count(s => s == 1f) > 600, $"most voxels under the surface (about 4 m up) are solid: {brick.Solid.Count(s => s == 1f)}");
    }

    private static float[] Identity() => [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];

    private static SolidMesh Unit()
    {
        (float[] xyz, int[] indices) = CubeData(new Vec3(-0.5f, -0.5f, -0.5f), new Vec3(0.5f, 0.5f, 0.5f), false);
        return new SolidMesh(xyz, indices);
    }

    private static SolidMeshInstance Cube(Vec3 min, Vec3 max, bool insideOut = false)
    {
        (float[] xyz, int[] indices) = CubeData(min, max, insideOut);
        return new SolidMeshInstance(new SolidMesh(xyz, indices), Identity());
    }

    // A closed cube's 8 vertices and 12 triangles, wound with their outsides out (or in).
    private static (float[] Xyz, int[] Indices) CubeData(Vec3 min, Vec3 max, bool insideOut)
    {
        float[] xyz = new float[24];
        for (int c = 0; c < 8; c++)
        {
            xyz[c * 3] = (c & 1) == 0 ? min.X : max.X;
            xyz[(c * 3) + 1] = (c & 2) == 0 ? min.Y : max.Y;
            xyz[(c * 3) + 2] = (c & 4) == 0 ? min.Z : max.Z;
        }

        // Each face as two triangles, counter-clockwise seen from outside (a right-handed outward normal).
        int[] faces =
        [
            0, 2, 3, 0, 3, 1, // south (z min): normal -z
            4, 5, 7, 4, 7, 6, // north
            0, 4, 6, 0, 6, 2, // west (x min)
            1, 3, 7, 1, 7, 5, // east
            0, 1, 5, 0, 5, 4, // bottom
            2, 6, 7, 2, 7, 3, // top
        ];
        if (insideOut)
        {
            for (int t = 0; t < faces.Length; t += 3)
            {
                (faces[t + 1], faces[t + 2]) = (faces[t + 2], faces[t + 1]);
            }
        }

        return (xyz, faces);
    }

    // A cone's side only (no base): its tip over the foot's middle, the foot a ring of 16 vertices.
    private static SolidMeshInstance Cone(Vec3 footMiddle, float radius, float height, float[] toWorld = null)
    {
        const int Sides = 16;
        float[] xyz = new float[(Sides + 1) * 3];
        xyz[0] = footMiddle.X;
        xyz[1] = footMiddle.Y + height;
        xyz[2] = footMiddle.Z;
        for (int i = 0; i < Sides; i++)
        {
            double a = 2 * Math.PI * i / Sides;
            xyz[(i + 1) * 3] = footMiddle.X + (radius * (float)Math.Cos(a));
            xyz[((i + 1) * 3) + 1] = footMiddle.Y;
            xyz[((i + 1) * 3) + 2] = footMiddle.Z + (radius * (float)Math.Sin(a));
        }

        int[] indices = new int[Sides * 3];
        for (int i = 0; i < Sides; i++)
        {
            indices[i * 3] = 0;
            indices[(i * 3) + 1] = 1 + ((i + 1) % Sides);
            indices[(i * 3) + 2] = 1 + i;
        }

        return new SolidMeshInstance(new SolidMesh(xyz, indices), toWorld ?? Identity());
    }
}
