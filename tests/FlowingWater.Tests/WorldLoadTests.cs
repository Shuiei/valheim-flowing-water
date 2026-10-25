using System.Collections.Generic;
using FlowingWater.Server;
using FlowingWater.Tests.Volume;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The water saved with the world back into the grid as it opens: a brick's into its own voxels when the voxels are the
/// same size, poured into the new grid's when not; all of it or none; the bricks made only to look for room forgotten.
/// </summary>
public class WorldLoadTests
{
    [Fact]
    public void ABrickOfTheSameSizeGoesBackIntoItsOwnVoxels()
    {
        // Ground at 3 m: the voxel at y = 3 has a solid share 0 but the one at y = 2 is solid. Water saved in voxel
        // (1, 3, 1) goes back there, under a ceiling at its very middle or not.
        var grid = new VoxelGrid(1f, new TestSolids(3));
        var view = new BrickView(0, 0, 0, 1f);
        view.Water[Brick.Index(1, 3, 1)] = 0.4f;
        view.Water[Brick.Index(5, 3, 5)] = 0.6f;

        float? poured = new WorldLoad(grid).PourView(view);

        Assert.Equal(1f, poured.Value, 4);
        Assert.Equal(0.4f, grid.WaterAt(1, 3, 1), 4);
        Assert.Equal(0.6f, grid.WaterAt(5, 3, 5), 4);
        Assert.Single(grid.Bricks);
    }

    [Fact]
    public void WaterSavedInAVoxelNowSolidIsPouredIntoTheFirstOpenOneOverIt()
    {
        // The ground rose to 5 m since the brick was saved (a terrain change): the water saved at y = 3 goes to y = 5.
        var grid = new VoxelGrid(1f, new TestSolids(5));
        var view = new BrickView(0, 0, 0, 1f);
        view.Water[Brick.Index(1, 3, 1)] = 0.4f;

        float? poured = new WorldLoad(grid).PourView(view);

        Assert.Equal(0.4f, poured.Value, 4);
        Assert.Equal(0f, grid.WaterAt(1, 3, 1));
        Assert.Equal(0.4f, grid.WaterAt(1, 5, 1), 4);
    }

    [Fact]
    public void ABrickOfAnotherSizeIsPouredAtEachVoxelsMiddle()
    {
        // Half-metre voxels into metre ones: voxel (2, 6, 2) of 0.5 m (0.125 m³ when full) is at 1.25, 3.25, 1.25 m.
        var grid = new VoxelGrid(1f, new TestSolids(3));
        var view = new BrickView(0, 0, 0, 0.5f);
        view.Water[Brick.Index(2, 6, 2)] = 1f;

        float? poured = new WorldLoad(grid).PourView(view);

        Assert.Equal(0.125f, poured.Value, 4);
        Assert.Equal(0.125f, grid.WaterAt(1, 3, 1), 4);
    }

    [Fact]
    public void NothingIsPouredAndTheBricksMadeToLookAreForgottenWhenSomeWaterFindsNoRoom()
    {
        // One brick allowed: the water at (1, 3, 1) fits; the water 20 voxels east needs a second brick, refused.
        var grid = new VoxelGrid(1f, new TestSolids(3)) { MaxBricks = 1 };
        var water = new List<(Vec3 Point, float Volume)> { (new Vec3(1.5f, 3.5f, 1.5f), 0.5f), (new Vec3(20.5f, 3.5f, 1.5f), 0.5f) };

        float? poured = new WorldLoad(grid).PourAll(water);

        Assert.Null(poured);
        Assert.Empty(grid.Bricks);
        Assert.Equal(0f, grid.TotalWater());
    }

    [Fact]
    public void ATilesWaterIsPouredAtTheMiddleOfEachCellOnTheGround()
    {
        // A 64-cell tile over a zone starting at (-32, -32): cell (3, 0) is 1 m wide, its middle at x = -28.5, z = -31.5;
        // 0.2 m deep is 0.2 m³.
        float[] depths = new float[64 * 64];
        depths[3] = 0.2f;

        List<(Vec3 Point, float Volume)> water = WorldLoad.TileWater(depths, 64, -32f, -32f, 64f, (_, _) => 3f);

        (Vec3 point, float volume) = Assert.Single(water);
        Assert.Equal(-28.5f, point.X, 4);
        Assert.Equal(3.01f, point.Y, 4);
        Assert.Equal(-31.5f, point.Z, 4);
        Assert.Equal(0.2f, volume, 4);
    }
}
