using System.Collections.Generic;
using FlowingWater.Server;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// What changed in the water's solids, and which bricks read theirs again: only those a changed piece (where it was
/// and where it is) or a changed zone's ground reaches, with the voxel round them; a piece broken while no one heard
/// (the water switched off) is found gone at the next look, one heard of is not found gone again; the first look at a
/// zone, or one let go, changes nothing.
/// </summary>
public class SolidsChangesTests
{
    // Bricks of 1 m voxels along x, 16 m each: 0 (0 to 16 m), 1 (16 to 32), 3 (48 to 64) and 6 (96 to 112).
    private static readonly int[] Row = [0, 1, 3, 6];

    [Fact]
    public void AZonesGroundChangedRefillsOnlyTheBricksOverItAndForgetsOnlyIt()
    {
        (VoxelGrid grid, Fills fills) = Grid();
        var changes = new SolidsChanges<int, int>(z => z);
        var forgotten = new List<int>();

        // Zone 7 from x = 30 to 50: bricks 1 and 3; brick 0 ends 14 m short of it.
        changes.Ground(7, new Vec3(30f, -5e4f, -10f), new Vec3(50f, 5e4f, 10f));

        Assert.Equal(2, changes.Refill(grid, forgotten.Add));
        Assert.Equal([1, 3], fills.Bricks);
        Assert.Equal([7], forgotten);
        Assert.False(changes.Any);
        Assert.Equal(0, changes.Refill(grid, forgotten.Add));
    }

    [Fact]
    public void ARefillIsSpreadOverCallsWhileTheFramesTimeIsShort()
    {
        (VoxelGrid grid, Fills fills) = Grid();
        var changes = new SolidsChanges<int, int>(z => z);
        changes.All();

        Assert.Equal(1, changes.Refill(grid, _ => { }, () => false));
        Assert.True(changes.Any);
        Assert.Equal(3, changes.Refill(grid, _ => { }, () => true));

        Assert.False(changes.Any);
        Assert.Equal(4, fills.Bricks.Count);
    }

    [Fact]
    public void ThePlaceTakesInTheVoxelRoundABrick()
    {
        // A piece of 0.5 m reach standing 1.2 m past brick 1's east face: within the voxel round it.
        (VoxelGrid grid, Fills fills) = Grid();
        var changes = new SolidsChanges<int, int>(z => z);
        changes.Found(1, new Vec3(33.2f, 2f, 2f), 0.5f, 0);
        changes.Looked(0);

        changes.Destroyed(0, 1, new Vec3(33.2f, 2f, 2f), 0.5f);
        changes.Refill(grid, _ => { });

        Assert.Equal([1], fills.Bricks);
    }

    [Fact]
    public void TheFirstLookAtAZoneChangesNothingAndAnUnchangedPieceNeither()
    {
        (VoxelGrid grid, Fills fills) = Grid();
        var changes = new SolidsChanges<int, int>(z => z);

        Look(changes, 0, (1, new Vec3(8f, 1f, 2f), 1f, 5));
        Assert.False(changes.Any);
        Look(changes, 0, (1, new Vec3(8f, 1f, 2f), 1f, 5));

        Assert.Equal(0, changes.Refill(grid, _ => { }));
        Assert.Empty(fills.Bricks);
    }

    [Fact]
    public void APieceMovedRefillsTheBricksWhereItWasAndWhereItIs()
    {
        (VoxelGrid grid, Fills fills) = Grid();
        var changes = new SolidsChanges<int, int>(z => z);
        Look(changes, 0, (1, new Vec3(8f, 1f, 2f), 1f, 5), (2, new Vec3(100f, 1f, 2f), 1f, 5));

        Look(changes, 0, (1, new Vec3(56f, 1f, 2f), 1f, 5), (2, new Vec3(100f, 1f, 2f), 1f, 5));
        changes.Refill(grid, _ => { });

        Assert.Equal([0, 3], fills.Bricks);
    }

    [Theory]
    [InlineData(6, 1f)]
    [InlineData(5, 3f)]
    public void APieceTurnedOpenedOrGrownRefillsTheBricksItReaches(int look, float reach)
    {
        (VoxelGrid grid, Fills fills) = Grid();
        var changes = new SolidsChanges<int, int>(z => z);
        Look(changes, 0, (1, new Vec3(56f, 1f, 2f), 1f, 5));

        Look(changes, 0, (1, new Vec3(56f, 1f, 2f), reach, look));
        changes.Refill(grid, _ => { });

        Assert.Equal([3], fills.Bricks);
    }

    [Fact]
    public void APieceBrokenWhileNoOneHeardIsFoundGoneAtTheNextLook()
    {
        // The water switched off hears of no broken piece: the look once it is on again finds it gone.
        (VoxelGrid grid, Fills fills) = Grid();
        var changes = new SolidsChanges<int, int>(z => z);
        var forgotten = new List<int>();
        Look(changes, 0, (1, new Vec3(56f, 1f, 2f), 1f, 5), (2, new Vec3(100f, 1f, 2f), 1f, 5));

        Look(changes, 0, (2, new Vec3(100f, 1f, 2f), 1f, 5));
        changes.Refill(grid, forgotten.Add);

        Assert.Equal([3], fills.Bricks);
        Assert.Equal([0], forgotten);
    }

    [Fact]
    public void APieceHeardOfAsBrokenRefillsAtOnceWhereItIsAndWasAndIsNotFoundGoneAgain()
    {
        (VoxelGrid grid, Fills fills) = Grid();
        var changes = new SolidsChanges<int, int>(z => z);
        Look(changes, 0, (1, new Vec3(8f, 1f, 2f), 1f, 5));

        // It moved to brick 3 since the look, then broke.
        changes.Destroyed(0, 1, new Vec3(56f, 1f, 2f), 1f);
        changes.Refill(grid, _ => { });
        Assert.Equal([0, 3], fills.Bricks);

        fills.Bricks.Clear();
        Look(changes, 0);
        Assert.Equal(0, changes.Refill(grid, _ => { }));
        Assert.Empty(fills.Bricks);
    }

    [Fact]
    public void APieceHeardOfInAZoneNeverLookedAtRefillsWhereItIs()
    {
        (VoxelGrid grid, Fills fills) = Grid();
        var changes = new SolidsChanges<int, int>(z => z);

        changes.Destroyed(4, 1, new Vec3(100f, 1f, 2f), 1f);
        changes.Refill(grid, _ => { });

        Assert.Equal([6], fills.Bricks);
    }

    [Fact]
    public void AZoneLetGoIsLookedAtAsIfForTheFirstTime()
    {
        (VoxelGrid grid, _) = Grid();
        var changes = new SolidsChanges<int, int>(z => z);
        Look(changes, 0, (1, new Vec3(8f, 1f, 2f), 1f, 5));
        Look(changes, 1, (2, new Vec3(100f, 1f, 2f), 1f, 5));

        changes.Keep([1]);
        Look(changes, 0);
        Look(changes, 1);

        Assert.Equal(1, changes.Refill(grid, _ => { }));
    }

    [Fact]
    public void AllRefillsEveryBrick()
    {
        (VoxelGrid grid, Fills fills) = Grid();
        var changes = new SolidsChanges<int, int>(z => z);

        changes.All();

        Assert.Equal(Row.Length, changes.Refill(grid, _ => { }));
        Assert.Equal(Row, fills.Bricks);
    }

    private static void Look(SolidsChanges<int, int> changes, int zone, params (int Id, Vec3 At, float Reach, int Look)[] pieces)
    {
        foreach ((int id, Vec3 at, float reach, int look) in pieces)
        {
            changes.Found(id, at, reach, look);
        }

        changes.Looked(zone);
    }

    // The row of bricks, and which of them read their solids again since made (sorted).
    private static (VoxelGrid Grid, Fills Fills) Grid()
    {
        var fills = new Fills();
        var grid = new VoxelGrid(1f, fills);
        foreach (int x in Row)
        {
            grid.Get(x, 0, 0);
        }

        fills.Bricks.Clear();
        return (grid, fills);
    }

    // Nothing solid; the bricks along x whose solids were read.
    private sealed class Fills : ISolids
    {
        public SortedSet<int> Bricks { get; } = [];

        public void Fill(Brick brick, float voxelSize) => Bricks.Add(brick.X);

        public bool Ready(int x, int y, int z, float voxelSize) => true;
    }
}
