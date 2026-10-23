using System.Collections.Generic;
using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Which bricks round one must be drawn again when its water changes: all when it is new, those over and under it for
/// any change, those beside it only for a change on its border facing them, none for no change; a surface built from
/// only the bricks near it is the one built from all; and the surface's shape: a slope's water one smooth sheet, a
/// fall's sheet drawn, no side into the ground, none along a thin sheet.
/// </summary>
public class VoxelSurfaceTests
{
    [Fact]
    public void ANewBrickTouchesEveryBrickRoundIt()
    {
        // Three by three, from one under to five over, less itself.
        Assert.Equal((3 * 3 * 7) - 1, VoxelSurface.Touched(null, View()).Count);
    }

    [Fact]
    public void NoChangeTouchesNothing()
    {
        Assert.Empty(VoxelSurface.Touched(View((5, 5, 5, 0.5f)), View((5, 5, 5, 0.5f))));
    }

    [Fact]
    public void AChangeOfSolidAloneTouchesToo()
    {
        // The same water on a floor raised by a piece: the brick beside it sees the water stand higher.
        BrickView was = View((15, 5, 5, 0.5f));
        BrickView now = View((15, 5, 5, 0.5f));
        now.Solid[Brick.Index(15, 5, 5)] = 0.4f;

        Assert.Contains((1, 0, 0), VoxelSurface.Touched(was, now));
    }

    [Fact]
    public void AChangeInsideTouchesOnlyTheBricksOverAndUnderIt()
    {
        // The depth under water up to five bricks over it is measured down through it; the water of a top in the
        // brick under it joins tops up to MaxStep over it, and theirs others, round a corner.
        var touched = VoxelSurface.Touched(View((5, 5, 5, 0.5f)), View((5, 5, 5, 0.6f))).OrderBy(o => o.Y).ToArray();

        Assert.Equal([(0, -1, 0), .. Up(0, 0)], touched);
    }

    [Fact]
    public void AChangeInTheTopLayerTouchesTheBrickUnderItToo()
    {
        var touched = VoxelSurface.Touched(View(), View((5, 15, 5, 0.5f))).OrderBy(o => o).ToArray();

        Assert.Equal([(0, -1, 0), .. Up(0, 0)], touched);
    }

    [Fact]
    public void AChangeOnTheEastBorderTouchesTheBricksEastOfIt()
    {
        var touched = VoxelSurface.Touched(View(), View((15, 5, 5, 0.5f))).OrderBy(o => o).ToArray();

        Assert.Equal([(0, -1, 0), .. Up(0, 0), (1, -1, 0), (1, 0, 0), .. Up(1, 0)], touched);
    }

    [Fact]
    public void AChangeInTheBottomCornerTouchesTheBricksUnderAndBesideIt()
    {
        var touched = VoxelSurface.Touched(View(), View((0, 0, 0, 0.5f)));

        // Every brick west and south of it, from one under to five over (2 x 2 x 7 less itself), none east or north.
        Assert.Equal((2 * 2 * 7) - 1, touched.Count);
        Assert.Contains((-1, -1, -1), touched);
        Assert.Contains((0, -1, 0), touched);
        Assert.DoesNotContain(touched, o => o.X > 0 || o.Z > 0);
    }

    [Fact]
    public void ASurfaceBuiltFromTheBricksNearItIsTheSameAsFromTheWholeField()
    {
        // A column of water 40 voxels deep through three bricks, with a brick of water beside the top one and others
        // far away that the surface does not read.
        var field = new VoxelField();
        for (int by = 0; by < 3; by++)
        {
            var column = new BrickView(0, by, 0, 1f);
            for (int y = 0; y < Brick.Size && (by * Brick.Size) + y < 40; y++)
            {
                column.Water[Brick.Index(15, y, 15)] = 1f;
                column.Water[Brick.Index(3, y, 3)] = (by * Brick.Size) + y == 39 ? 0.5f : 1f;
            }

            field.Set(column);
        }

        var beside = new BrickView(1, 2, 1, 1f);
        beside.Water[Brick.Index(0, 7, 0)] = 0.7f;
        field.Set(beside);
        field.Set(View(9, 0, 9));
        field.Set(View(0, 9, 0));
        BrickView top = field.Find(0, 2, 0);

        VoxelField near = field.Near(top);
        SurfaceMesh whole = VoxelSurface.Build(field, top);
        SurfaceMesh fromNear = VoxelSurface.Build(near, top);

        Assert.Equal(4, near.Bricks.Count);
        Assert.Equal(whole.Vertices, fromNear.Vertices);
        Assert.Equal(whole.Depths, fromNear.Depths);
        Assert.Equal(whole.Shores, fromNear.Shores);
        Assert.Equal(whole.Triangles, fromNear.Triangles);
        Assert.Contains(whole.Depths, d => d > 30f);

        // So is the one under it, whose water goes on up into it: its sides reach the surface over it there.
        BrickView middle = field.Find(0, 1, 0);
        SurfaceMesh middleWhole = VoxelSurface.Build(field, middle);
        Assert.Equal(middleWhole.Vertices, VoxelSurface.Build(field.Near(middle), middle).Vertices);
        Assert.Equal(32f, middleWhole.Vertices.Max(v => v.Y), 4);
    }

    [Fact]
    public void TheBricksNearOneAreThoseItsSurfaceMayRead()
    {
        // A column of bricks from seven under to two over, and one two east: only five under to one over are near.
        var field = new VoxelField();
        for (int y = -7; y <= 2; y++)
        {
            field.Set(View(0, y, 0));
        }

        field.Set(View(2, 0, 0));
        VoxelField near = field.Near(field.Find(0, 0, 0));

        Assert.Equal(7, near.Bricks.Count);
        Assert.NotNull(near.Find(0, -5, 0));
        Assert.NotNull(near.Find(0, 1, 0));
        Assert.Null(near.Find(0, -6, 0));
        Assert.Null(near.Find(0, 2, 0));
        Assert.Null(near.Find(2, 0, 0));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(VoxelSurface.MaxStep)]
    public void AStreamDownASteepSlopeIsOneSmoothSheet(int drop)
    {
        // Five columns east by four north, each a thin sheet (0.1 of a voxel on ground 0.3 into it) drop voxels lower
        // than the one west of it.
        VoxelField field = Slope(drop, 5);

        SurfaceMesh mesh = VoxelSurface.Build(field, field.Find(0, 0, 0));

        // One vertex per corner, shared by every tile round it: no step, no wall, no seam.
        Assert.Equal(6 * 5, mesh.Vertices.Count);
        Assert.Equal(5 * 4 * 6, mesh.Triangles.Count);
        Assert.Equal(mesh.Vertices.Count, mesh.Vertices.Select(v => (v.X, v.Z)).Distinct().Count());

        // A corner between two columns lies halfway between their levels, and the sheet goes down all the way.
        Vec3 between = mesh.Vertices.Single(v => v.X == 3f && v.Z == 5f);
        Assert.Equal(((SlopeRow(drop, 5, 2) + SlopeRow(drop, 5, 3)) / 2f) + 0.4f, between.Y, 4);
        float[] down = [.. Enumerable.Range(2, 6).Select(x => mesh.Vertices.Single(v => v.X == x && v.Z == 5f).Y)];
        Assert.Equal(down.OrderByDescending(y => y), down);
    }

    [Fact]
    public void ASlopeSteeperThanTheSheetBridgesIsDrawnAsLedges()
    {
        // Each column MaxStep + 1 voxels under the one west of it: not one water, each strip north its own.
        VoxelField field = Slope(VoxelSurface.MaxStep + 1, 3);

        SurfaceMesh mesh = VoxelSurface.Build(field, field.Find(0, 0, 0));

        // Three strips of four tiles, their corners their own (two per corner between strips), no sides (thin).
        Assert.Equal(3 * 2 * 5, mesh.Vertices.Count);
        Assert.Equal(3 * 4 * 6, mesh.Triangles.Count);
    }

    [Fact]
    public void AFallFromALedgeKeepsItsSheet()
    {
        // A thin sheet on a ledge (row 10, x 2 to 4) running into a column of falling water from row 10 down to 3.
        var cells = new List<(int X, int Y, int Z, float Water, float Solid)>();
        cells.AddRange(Enumerable.Range(2, 3).Select(x => (x, 10, 5, 0.2f, 0.5f)));
        cells.AddRange(Enumerable.Range(3, 8).Select(y => (5, y, 5, 0.3f, 0f)));
        VoxelField field = Field([.. cells]);

        SurfaceMesh mesh = VoxelSurface.Build(field, field.Find(0, 0, 0));

        // Four tops; the fall's sides east, north and south over its eight voxels; none west, against the cliff under
        // the ledge whose water it is, and none along the sheet.
        Assert.Equal((4 + (3 * 8)) * 6, mesh.Triangles.Count);

        // Its sheet facing away from the cliff goes from its floor up to its top, its top joined to the ledge's.
        Vec3[] front = [.. mesh.Vertices.Where(v => v.X == 6f)];
        Assert.Equal(3f, front.Min(v => v.Y), 4);
        Assert.Equal(10.3f, front.Max(v => v.Y), 4);
        Assert.Contains(mesh.Vertices, v => v.X == 5f && v.Z == 5f && System.Math.Abs(v.Y - 10.5f) < 0.001f);
    }

    [Fact]
    public void ASideAgainstTheGroundIsNotDrawn()
    {
        // A pool two voxels deep (x 5 and 6), and the same with a sheet standing on a cliff west of it (row 9): the
        // pool's west side is against the cliff's ground.
        (int, int, int, float, float)[] pool = [(5, 0, 5, 1f, 0f), (5, 1, 5, 1f, 0f), (6, 0, 5, 1f, 0f), (6, 1, 5, 1f, 0f)];
        VoxelField open = Field(pool);
        VoxelField cliff = Field([.. pool, (4, 9, 5, 0.2f, 0.5f)]);

        SurfaceMesh alone = VoxelSurface.Build(open, open.Find(0, 0, 0));
        SurfaceMesh against = VoxelSurface.Build(cliff, cliff.Find(0, 0, 0));

        // Two tops and twelve sides; against the cliff, two sides fewer and the sheet's top.
        Assert.Equal((2 + 12) * 6, alone.Triangles.Count);
        Assert.Equal((3 + 10) * 6, against.Triangles.Count);
    }

    [Fact]
    public void AThinSheetHasNoSidesAndADeeperOneHas()
    {
        // 0.2 of a voxel of water on ground half into it, then 0.5 on the same.
        VoxelField thin = Field((5, 5, 5, 0.2f, 0.5f));
        VoxelField deep = Field((5, 5, 5, 0.5f, 0.5f));

        Assert.Equal(6, VoxelSurface.Build(thin, thin.Find(0, 0, 0)).Triangles.Count);
        Assert.Equal(5 * 6, VoxelSurface.Build(deep, deep.Find(0, 0, 0)).Triangles.Count);
    }

    [Fact]
    public void ASideStopsAtTheSurfaceOverIt()
    {
        // Water three voxels deep (rows 4 to 6, its level 7) joined to a sheet three rows lower east and west of it
        // (row 3, level 3.6): their shared corners are at 5.3, under the deep water's top voxel's floor.
        VoxelField field = Field((5, 4, 5, 1f, 0f), (5, 5, 5, 1f, 0f), (5, 6, 5, 1f, 0f), (6, 3, 5, 0.1f, 0.5f), (4, 3, 5, 0.1f, 0.5f));

        SurfaceMesh mesh = VoxelSurface.Build(field, field.Find(0, 0, 0));

        // The deep water's north side reaches the corners it shares with the sheets and no higher: its top voxel has
        // none there, those under it reach 5.3.
        Vec3[] north = [.. mesh.Vertices.Where(v => v.Z == 6f)];
        Assert.Equal(5.3f, north.Max(v => v.Y), 4);
        Assert.Contains(north, v => v.X == 6f && v.Y == 4f);

        // No side between them, up the deep water's east face: nothing there over the corners they share.
        Assert.DoesNotContain(mesh.Vertices, v => v.X == 6f && v.Y > 5.31f);
    }

    private static BrickView View(int x, int y, int z) => new(x, y, z, 1f);

    // The row of column x of a slope dropping this many voxels a voxel east, columns from x 2, the last at row 1.
    private static int SlopeRow(int drop, int columns, int x) => 1 + (drop * (columns + 1 - x));

    // A slope of thin sheets, columns x from 2 (this many east) and z 4 to 7.
    private static VoxelField Slope(int drop, int columns)
    {
        var cells = from x in Enumerable.Range(2, columns) from z in Enumerable.Range(4, 4) select (x, SlopeRow(drop, columns, x), z, 0.1f, 0.3f);
        return Field([.. cells]);
    }

    private static VoxelField Field(params (int X, int Y, int Z, float Water, float Solid)[] voxels)
    {
        var view = new BrickView(0, 0, 0, 1f);
        foreach ((int x, int y, int z, float water, float solid) in voxels)
        {
            view.Water[Brick.Index(x, y, z)] = water;
            view.Solid[Brick.Index(x, y, z)] = solid;
        }

        var field = new VoxelField();
        field.Set(view);
        return field;
    }

    // The bricks one to five over (x, 0, z).
    private static (int X, int Y, int Z)[] Up(int x, int z) => [.. Enumerable.Range(1, 5).Select(y => (x, y, z))];

    private static BrickView View(params (int X, int Y, int Z, float Water)[] voxels)
    {
        var view = new BrickView(0, 0, 0, 1f);
        foreach ((int x, int y, int z, float water) in voxels)
        {
            view.Water[Brick.Index(x, y, z)] = water;
        }

        return view;
    }
}
