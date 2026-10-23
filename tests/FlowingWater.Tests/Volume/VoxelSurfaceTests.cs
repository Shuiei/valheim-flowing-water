using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Which bricks round one must be drawn again when its water changes: all when it is new, the one over it for a
/// change inside, those beside it only for a change on its border facing them, the one under it for a change in its
/// bottom two layers, none for no change.
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
