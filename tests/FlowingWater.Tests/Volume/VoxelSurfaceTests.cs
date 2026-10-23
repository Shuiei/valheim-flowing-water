using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Which bricks round one must be drawn again when its water changes: all when it is new, the one over it for a
/// change inside, those beside it only for a change on its border facing them, the one under it for a change in its
/// bottom two layers, none for no change; and a surface built from only the bricks near it is the one built from all.
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
    public void AChangeInsideTouchesOnlyTheBricksOverIt()
    {
        // The depth under water up to five bricks over it is measured down through it.
        Assert.Equal(Up(0, 0), VoxelSurface.Touched(View((5, 5, 5, 0.5f)), View((5, 5, 5, 0.6f))).OrderBy(o => o.Y).ToArray());
    }

    [Fact]
    public void AChangeInTheSecondLayerTouchesTheBrickUnderIt()
    {
        // The voxel under it, in the bottom layer, is a top for the brick below only when no water goes on up.
        var touched = VoxelSurface.Touched(View(), View((5, 1, 5, 0.5f))).OrderBy(o => o).ToArray();

        Assert.Equal([(0, -1, 0), .. Up(0, 0)], touched);
    }

    [Fact]
    public void AChangeOnTheEastBorderTouchesTheBricksEastOfIt()
    {
        var touched = VoxelSurface.Touched(View(), View((15, 5, 5, 0.5f))).OrderBy(o => o).ToArray();

        Assert.Equal([.. Up(0, 0), (1, 0, 0), .. Up(1, 0)], touched);
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

    private static BrickView View(int x, int y, int z) => new(x, y, z, 1f);

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
