using System;
using System.Collections.Generic;
using FlowingWater.Client;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// What is built, read by the player's game for the water on its card: a wall or a roof thinner than a voxel closes the
/// face it stands in on both voxels it parts, across a brick's edge and the texture's edge too; a block fills its
/// voxels; nothing built gives nothing; bits already written stay.
/// </summary>
public class BuiltVoxelsTests
{
    private const int Columns = 16;

    [Fact]
    public void AThinWallClosesTheFaceItStandsInOnBothSides()
    {
        // 20 cm thick, on the face between voxel columns 3 and 4, 3 m high, across the brick.
        var wall = SolidBox.Between(new Vec3(3.9f, 0f, 0f), new Vec3(4.1f, 3f, 16f));

        byte[] solids = Written(new() { [(0, 0, 0)] = BuiltVoxels.Raster(0, 0, 0, 1f, [wall]) }, x0: 0, baseRow: 0);

        for (int y = 0; y < 3; y++)
        {
            Assert.Equal(SolidsLayout.ClosedEast, solids[SolidsLayout.Index(Columns, 3, y, 8)]);
            Assert.Equal(SolidsLayout.ClosedWest, solids[SolidsLayout.Index(Columns, 4, y, 8)]);
        }

        Assert.Equal(0, solids[SolidsLayout.Index(Columns, 2, 1, 8)]);
        Assert.Equal(0, solids[SolidsLayout.Index(Columns, 5, 1, 8)]);
        Assert.Equal(0, solids[SolidsLayout.Index(Columns, 3, 4, 8)]);
    }

    [Fact]
    public void AThinRoofClosesTheFaceOverTheVoxelsUnderIt()
    {
        var roof = SolidBox.Between(new Vec3(2f, 2.9f, 2f), new Vec3(6f, 3.1f, 6f));

        byte[] solids = Written(new() { [(0, 0, 0)] = BuiltVoxels.Raster(0, 0, 0, 1f, [roof]) }, x0: 0, baseRow: 0);

        Assert.Equal(SolidsLayout.ClosedUp, solids[SolidsLayout.Index(Columns, 4, 2, 4)] & SolidsLayout.ClosedUp);
        Assert.Equal(SolidsLayout.ClosedDown, solids[SolidsLayout.Index(Columns, 4, 3, 4)] & SolidsLayout.ClosedDown);
        Assert.Equal(0, solids[SolidsLayout.Index(Columns, 4, 3, 4)] & SolidsLayout.Solid);
        Assert.Equal(0, solids[SolidsLayout.Index(Columns, 8, 2, 8)]);
    }

    [Fact]
    public void AWallOnABricksEdgeClosesTheFaceIntoTheNextBrick()
    {
        // On the face between voxel 15 (brick 0) and voxel 16 (brick 1); the texture starts at voxel 8.
        var wall = SolidBox.Between(new Vec3(15.9f, 0f, 0f), new Vec3(16.1f, 2f, 16f));
        var bricks = new Dictionary<(int X, int Y, int Z), byte[]>
        {
            [(0, 0, 0)] = BuiltVoxels.Raster(0, 0, 0, 1f, [wall]),
            [(1, 0, 0)] = BuiltVoxels.Raster(1, 0, 0, 1f, [wall]),
        };

        byte[] solids = Written(bricks, x0: 8, baseRow: 0);

        Assert.Equal(SolidsLayout.ClosedEast, solids[SolidsLayout.Index(Columns, 7, 1, 3)]);
        Assert.Equal(SolidsLayout.ClosedWest, solids[SolidsLayout.Index(Columns, 8, 1, 3)]);
    }

    [Fact]
    public void AWallJustWestOfTheTextureClosesItsFirstColumnsWestFace()
    {
        var wall = SolidBox.Between(new Vec3(3.9f, 0f, 0f), new Vec3(4.1f, 2f, 16f));

        byte[] solids = Written(new() { [(0, 0, 0)] = BuiltVoxels.Raster(0, 0, 0, 1f, [wall]) }, x0: 4, baseRow: 0);

        Assert.Equal(SolidsLayout.ClosedWest, solids[SolidsLayout.Index(Columns, 0, 1, 5)]);
    }

    [Fact]
    public void TheTexturesRowsStartAtItsBaseRow()
    {
        // A roof between rows 18 and 19 (brick 1 up), the texture from row 10: its rows 8 and 9.
        var roof = SolidBox.Between(new Vec3(2f, 18.9f, 2f), new Vec3(6f, 19.1f, 6f));

        byte[] solids = Written(new() { [(0, 1, 0)] = BuiltVoxels.Raster(0, 1, 0, 1f, [roof]) }, x0: 0, baseRow: 10);

        Assert.NotEqual(0, solids[SolidsLayout.Index(Columns, 4, 8, 4)] & SolidsLayout.ClosedUp);
        Assert.NotEqual(0, solids[SolidsLayout.Index(Columns, 4, 9, 4)] & SolidsLayout.ClosedDown);
    }

    [Fact]
    public void ABlockFillsItsVoxels()
    {
        var block = SolidBox.Between(new Vec3(2f, 1f, 2f), new Vec3(4f, 3f, 4f));

        byte[] solids = Written(new() { [(0, 0, 0)] = BuiltVoxels.Raster(0, 0, 0, 1f, [block]) }, x0: 0, baseRow: 0);

        Assert.NotEqual(0, solids[SolidsLayout.Index(Columns, 3, 2, 3)] & SolidsLayout.Solid);
        Assert.Equal(0, solids[SolidsLayout.Index(Columns, 3, 0, 3)] & SolidsLayout.Solid);
        Assert.Equal(0, solids[SolidsLayout.Index(Columns, 5, 2, 3)] & SolidsLayout.Solid);
    }

    [Fact]
    public void NothingBuiltRastersAsNothing()
    {
        Assert.Null(BuiltVoxels.Raster(0, 0, 0, 1f, []));
        Assert.Null(BuiltVoxels.Raster(0, 0, 0, 1f, [SolidBox.Between(new Vec3(40f, 0f, 40f), new Vec3(41f, 1f, 41f))]));
    }

    [Fact]
    public void BitsAlreadyWrittenStay()
    {
        byte[] solids = new byte[Columns * SolidsLayout.Rows * Columns];
        solids[SolidsLayout.Index(Columns, 1, 1, 1)] = SolidsLayout.ClosedNorth;
        var wall = SolidBox.Between(new Vec3(1.9f, 0f, 0f), new Vec3(2.1f, 3f, 16f));

        bool any = BuiltVoxels.Write(new Dictionary<(int X, int Y, int Z), byte[]> { [(0, 0, 0)] = BuiltVoxels.Raster(0, 0, 0, 1f, [wall]) }, solids, 0, 0, Columns, 0);

        Assert.True(any);
        Assert.Equal(SolidsLayout.ClosedNorth | SolidsLayout.ClosedEast, solids[SolidsLayout.Index(Columns, 1, 1, 1)]);
    }

    [Fact]
    public void NoBricksWriteNothing()
    {
        byte[] solids = new byte[Columns * SolidsLayout.Rows * Columns];

        Assert.False(BuiltVoxels.Write(new Dictionary<(int X, int Y, int Z), byte[]> { [(0, 0, 0)] = null }, solids, 0, 0, Columns, 0));
        Assert.All(solids, b => Assert.Equal(0, b));
    }

    [Fact]
    public void SolidsOfTheWrongSizeAreRefused() =>
        Assert.Throws<ArgumentException>(() => BuiltVoxels.Write(new Dictionary<(int X, int Y, int Z), byte[]>(), new byte[10], 0, 0, Columns, 0));

    [Theory]
    [InlineData(0, 0)]
    [InlineData(15, 0)]
    [InlineData(16, 1)]
    [InlineData(-1, -1)]
    [InlineData(-16, -1)]
    [InlineData(-17, -2)]
    public void AVoxelsBrickIsItsCoordinateOverSixteenRoundedDown(int voxel, int brick) => Assert.Equal(brick, BuiltVoxels.BrickOf(voxel));

    private static byte[] Written(Dictionary<(int X, int Y, int Z), byte[]> bricks, int x0, int baseRow)
    {
        byte[] solids = new byte[Columns * SolidsLayout.Rows * Columns];
        BuiltVoxels.Write(bricks, solids, x0, 0, Columns, baseRow);
        return solids;
    }
}
