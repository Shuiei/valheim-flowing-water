using System;
using System.Collections.Generic;
using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// Ground and pieces in a brick: the ground fills voxels from below, a thin wall closes the faces it stands across,
/// a thick block fills voxels, a floor closes the faces under it, a wall turned 45 degrees lets no water past, and a
/// raised box of thin walls holds its water until its bottom wall piece breaks.
/// </summary>
public class SolidRasterTests
{
    [Fact]
    public void TheGroundFillsVoxelsFromBelowUpToItsHeight()
    {
        var brick = new Brick(0, 0, 0);

        SolidRaster.Ground(brick, 1f, (_, _) => 3.25f);

        Assert.Equal(1f, brick.Solid[Brick.Index(2, 2, 2)]);
        Assert.Equal(0.25f, brick.Solid[Brick.Index(2, 3, 2)], 4);
        Assert.Equal(0f, brick.Solid[Brick.Index(2, 4, 2)]);
    }

    [Fact]
    public void AThinWallClosesTheFacesItStandsAcrossWithoutFillingVoxels()
    {
        var brick = new Brick(0, 0, 0);

        SolidRaster.Pieces(brick, 1f, [SolidBox.Between(new Vec3(9.9f, 0f, 8f), new Vec3(10.1f, 4f, 12f))]);

        for (int z = 8; z < 12; z++)
        {
            for (int y = 0; y < 4; y++)
            {
                Assert.Equal(Brick.ClosedX, brick.Closed[Brick.Index(9, y, z)] & Brick.ClosedX);
            }
        }

        Assert.Equal(0, brick.Closed[Brick.Index(9, 4, 9)] & Brick.ClosedX);
        Assert.Equal(0, brick.Closed[Brick.Index(9, 2, 12)] & Brick.ClosedX);
        Assert.Equal(0, brick.Closed[Brick.Index(10, 2, 9)] & Brick.ClosedX);
        Assert.All(brick.Solid, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void AThickBlockFillsTheVoxelsItHolds()
    {
        var brick = new Brick(0, 0, 0);

        SolidRaster.Pieces(brick, 1f, [SolidBox.Between(new Vec3(2f, 0f, 2f), new Vec3(4f, 2f, 4f))]);

        Assert.Equal(1f, brick.Solid[Brick.Index(2, 0, 2)]);
        Assert.Equal(1f, brick.Solid[Brick.Index(3, 1, 3)]);
        Assert.Equal(0f, brick.Solid[Brick.Index(4, 1, 3)]);
        Assert.Equal(0f, brick.Solid[Brick.Index(3, 2, 3)]);
    }

    [Fact]
    public void AFloorThinnerThanAVoxelPutsTheWatersFloorAtItsTop()
    {
        var brick = new Brick(0, 0, 0);

        // A 0.13 m floor whose top is 0.6 m into voxel row 7, as a raised box's floor is in game.
        SolidRaster.Pieces(brick, 1f, [SolidBox.Between(new Vec3(2f, 7.47f, 2f), new Vec3(4f, 7.6f, 4f))]);

        Assert.Equal(0.6f, brick.Solid[Brick.Index(3, 7, 3)], 3);
        Assert.Equal(Brick.ClosedY, brick.Closed[Brick.Index(3, 6, 3)] & Brick.ClosedY);
        Assert.Equal(0f, brick.Solid[Brick.Index(3, 8, 3)]);
        Assert.Equal(0f, brick.Solid[Brick.Index(4, 7, 3)]);
    }

    [Fact]
    public void AFloorAcrossTwoVoxelsClosesTheFaceUnderItAndFillsOnlyTheVoxelItRisesFrom()
    {
        var brick = new Brick(0, 0, 0);

        SolidRaster.Pieces(brick, 1f, [SolidBox.Between(new Vec3(0f, 4.9f, 0f), new Vec3(2f, 5.1f, 2f))]);

        // Its underside is in the top of row 4 (a ceiling there), its top in the bottom of row 5 (a floor).
        Assert.Equal(0f, brick.Solid[Brick.Index(1, 4, 1)]);
        Assert.Equal(0.1f, brick.Solid[Brick.Index(1, 5, 1)], 3);
        Assert.Equal(Brick.ClosedY, brick.Closed[Brick.Index(1, 4, 1)]);
        Assert.Equal(0, brick.Closed[Brick.Index(1, 5, 1)]);
        Assert.Equal(0, brick.Closed[Brick.Index(2, 4, 1)]);
    }

    [Fact]
    public void ARaisedFloorLeavesItsVoxelsOpenUpwardsAndAWallOnItClosesTheWayOverIt()
    {
        var brick = new Brick(0, 0, 0);

        // A raised box's floor (top 0.6 m into row 7) and a wall standing on it at x = 6, as in game.
        SolidRaster.Pieces(brick, 1f, [
            SolidBox.Between(new Vec3(1f, 7.47f, 1f), new Vec3(6f, 7.6f, 6f)),
            SolidBox.Between(new Vec3(5.85f, 7.6f, 1f), new Vec3(6.15f, 9.6f, 6f)),
        ]);

        int onFloor = Brick.Index(3, 7, 3);
        Assert.Equal(0, brick.Closed[onFloor]);
        Assert.Equal(Brick.ClosedY, brick.Closed[Brick.Index(3, 6, 3)]);
        Assert.Equal(Brick.ClosedX, brick.Closed[Brick.Index(5, 7, 3)] & Brick.ClosedX);
        Assert.Equal(Brick.ClosedX, brick.Closed[Brick.Index(5, 8, 3)] & Brick.ClosedX);
    }

    [Fact]
    public void AWallOnGroundPastABricksEdgeIsTestedOverThatGround()
    {
        // The ground steps up from 7.0 to 7.6 m at x = 16, the next brick's edge; a wall stands on it there.
        var brick = new Brick(0, 0, 0);
        SolidRaster.Ground(brick, 1f, (x, _) => x < 16f ? 7f : 7.6f);

        SolidRaster.Pieces(brick, 1f, [SolidBox.Between(new Vec3(15.85f, 7.6f, 1f), new Vec3(16.15f, 9.6f, 6f))], (x, _) => x < 16f ? 7f : 7.6f);

        // Voxel x = 15 has no ground in row 7, but the face east of it is tested over the ground beyond (7.8 m, not
        // the voxel's middle 7.5 m under the wall): the wall closes it.
        Assert.Equal(0f, brick.Solid[Brick.Index(15, 7, 3)]);
        Assert.Equal(Brick.ClosedX, brick.Closed[Brick.Index(15, 7, 3)] & Brick.ClosedX);
    }

    [Fact]
    public void AWallTurned45DegreesLetsNoWaterPast()
    {
        // A wall 0.2 m thick along x = z, from (1, 1) to (13, 13), as tall as the brick.
        float half = MathF.Sqrt(0.5f);
        var wall = new SolidBox(new Vec3(7f, 8f, 7f), new Vec3(half, 0f, half), new Vec3(0f, 1f, 0f), new Vec3(-half, 0f, half), new Vec3(6f * MathF.Sqrt(2f), 8f, 0.1f));
        var brick = new Brick(0, 0, 0);
        SolidRaster.Pieces(brick, 1f, [wall]);

        // From the voxel south-east of the wall's middle, moving only through open faces in the square the wall
        // cuts in two, the voxel north-west of it cannot be reached.
        HashSet<(int X, int Z)> reached = Flood(brick, (9, 5), 2, 12);

        Assert.DoesNotContain((5, 9), reached);
        Assert.Contains((11, 3), reached);
    }

    [Fact]
    public void ARaisedBoxOfThinWallsHoldsItsWaterUntilItsBottomWallPieceBreaks()
    {
        // A 6 by 6 box with a floor at y = 5 over open ground at 0; its east wall in two pieces, 5 to 7 and 7 to 9 m.
        var pieces = new List<SolidBox>
        {
            SolidBox.Between(new Vec3(2f, 4.9f, 2f), new Vec3(8f, 5.1f, 8f)),
            SolidBox.Between(new Vec3(1.9f, 5f, 2f), new Vec3(2.1f, 9f, 8f)),
            SolidBox.Between(new Vec3(2f, 5f, 1.9f), new Vec3(8f, 9f, 2.1f)),
            SolidBox.Between(new Vec3(2f, 5f, 7.9f), new Vec3(8f, 9f, 8.1f)),
            SolidBox.Between(new Vec3(7.9f, 7f, 2f), new Vec3(8.1f, 9f, 8f)),
        };
        var lower = SolidBox.Between(new Vec3(7.9f, 5f, 2f), new Vec3(8.1f, 7f, 8f));
        pieces.Add(lower);
        var grid = new VoxelGrid(1f, new BoxSolids(pieces));
        for (int z = 2; z < 8; z++)
        {
            for (int x = 2; x < 8; x++)
            {
                grid.Pour(x, 5, z, 1f);
                grid.Pour(x, 6, z, 1f);
                grid.Pour(x, 7, z, 0.5f);
            }
        }

        var solver = new VoxelSolver(grid);
        Run(solver, 15f);
        float held = Inside(grid);
        Assert.Equal(36f * 2.5f, held, 0);

        // The lower piece of the east wall breaks: its bricks read their solids again.
        pieces.Remove(lower);
        foreach (Brick brick in grid.Bricks.ToList())
        {
            grid.Refill(brick);
        }

        Run(solver, 15f);

        Assert.True(Inside(grid) < held * 0.6f, $"left in the box {Inside(grid)} of {held}");
        Assert.Equal(36f * 2.5f, grid.TotalWater(), 0);
    }

    [Theory]
    [InlineData(0f)]
    [InlineData(30f)]
    [InlineData(55f)]
    public void WaterRunningDownAThinSlopingChuteStaysOnItsBed(float heading)
    {
        // A chute of 2 m planks 0.13 m thick falling 1 m in 5, with 2 m side walls and closed ends, 8 m up over open
        // ground, turned across the grid: along it the planks pass in and out of the upper and lower halves of the
        // voxels they cross. Water poured at its head stays on its bed; none reaches the ground.
        double a = heading * Math.PI / 180;
        var along = Norm(new Vec3((float)Math.Cos(a), -0.2f, (float)Math.Sin(a)));
        var across = new Vec3((float)-Math.Sin(a), 0f, (float)Math.Cos(a));
        var up = Cross(across, along);
        var head = new Vec3(2.5f, 8f, 4.5f);
        var pieces = new List<SolidBox>();
        for (int k = 0; k < 6; k++)
        {
            Vec3 middle = head + (along * ((2 * k) + 1));
            pieces.Add(new SolidBox(middle - (up * 0.065f), along, up, across, new Vec3(1f, 0.065f, 1f)));
            foreach (float side in new[] { -1.15f, 1.15f })
            {
                pieces.Add(new SolidBox(middle + (up * 1f) + (across * side), along, up, across, new Vec3(1f, 1f, 0.15f)));
            }
        }

        foreach (Vec3 end in new[] { head - (along * 0.15f), head + (along * 12.15f) })
        {
            pieces.Add(new SolidBox(end + (up * 1f), along, up, across, new Vec3(0.15f, 1f, 1.3f)));
        }

        var grid = new VoxelGrid(1f, new BoxSolids(pieces));
        var solver = new VoxelSolver(grid);
        Vec3 pour = head + (along * 1.5f) + (up * 0.6f);
        for (float t = 0f; t < 10f; t += 0.1f)
        {
            grid.Pour((int)Math.Floor(pour.X), (int)Math.Floor(pour.Y), (int)Math.Floor(pour.Z), 0.1f);
            solver.Step(0.1f);
        }

        Run(solver, 10f);

        float fallen = grid.Bricks.Sum(b => Enumerable.Range(0, Brick.Count).Where(i => (b.Y * Brick.Size) + Brick.Place(i).Y < 4).Sum(i => b.Water[i]));
        Assert.Equal(0f, fallen, 3);
        Assert.Equal(10f, grid.TotalWater(), 1);
    }

    [Fact]
    public void ASourceOnAFloorHighInItsVoxelPoursOntoTheFloorNotUnderIt()
    {
        // A plank floor from 5.67 to 5.8 m: in row 5's upper half, row 5's ceiling; a source on it pours a little over it.
        var grid = new VoxelGrid(1f, new BoxSolids([SolidBox.Between(new Vec3(2f, 5.67f, 2f), new Vec3(6f, 5.8f, 6f))]));

        Assert.True(grid.Room(3.5f, 5.85f, 3.5f, out int x, out int y, out int z));

        Assert.Equal((3, 6, 3), (x, y, z));
    }

    [Fact]
    public void ASourceUnderASlabWithRoomUnderItPoursThere()
    {
        // Ground 0.3 m into row 5 and a slab whose underside is 0.9 m into it: a source on the ground pours at 5.6 m,
        // under the slab, in row 5.
        var grid = new VoxelGrid(1f, new BoxSolids([
            SolidBox.Between(new Vec3(0f, 0f, 0f), new Vec3(8f, 5.3f, 8f)),
            SolidBox.Between(new Vec3(0f, 5.9f, 0f), new Vec3(8f, 6.1f, 8f)),
        ]));

        Assert.True(grid.Room(3.5f, 5.6f, 3.5f, out int x, out int y, out int z));

        Assert.Equal((3, 5, 3), (x, y, z));
    }

    [Fact]
    public void WaterPouredInASolidGoesToTheFirstOpenVoxelOverIt()
    {
        var grid = new VoxelGrid(1f, new BoxSolids([SolidBox.Between(new Vec3(0f, 0f, 0f), new Vec3(8f, 3f, 8f))]));

        Assert.True(grid.Room(3.5f, 1.5f, 3.5f, out _, out int y, out _));

        Assert.Equal(3, y);
    }

    [Fact]
    public void VoxelsSideBySideWithNoRoomAtASameHeightAreClosedBetween()
    {
        // In row 5: a plank over the upper half of voxel 3 (its ceiling at 0.7), a block filling voxel 4 to 0.75.
        var brick = new Brick(0, 0, 0);
        SolidRaster.Pieces(brick, 1f, [
            SolidBox.Between(new Vec3(3f, 5.7f, 3f), new Vec3(4f, 5.8f, 4f)),
            SolidBox.Between(new Vec3(4f, 5f, 3f), new Vec3(5f, 5.75f, 4f)),
        ]);

        Assert.Equal(0.7f, brick.Ceiling[Brick.Index(3, 5, 3)], 3);
        Assert.Equal(0.75f, brick.Solid[Brick.Index(4, 5, 3)], 3);
        Assert.Equal(Brick.ClosedX, brick.Closed[Brick.Index(3, 5, 3)] & Brick.ClosedX);
    }

    [Fact]
    public void VoxelsSideBySideWithRoomAtASameHeightAndNothingBetweenStayOpen()
    {
        // The same plank, the block beside it only 0.3 high: both have room from 0.3 to 0.7, and nothing crosses there.
        var brick = new Brick(0, 0, 0);
        SolidRaster.Pieces(brick, 1f, [
            SolidBox.Between(new Vec3(3f, 5.7f, 3f), new Vec3(4f, 5.8f, 4f)),
            SolidBox.Between(new Vec3(4f, 5f, 3f), new Vec3(5f, 5.3f, 4f)),
        ]);

        Assert.Equal(0, brick.Closed[Brick.Index(3, 5, 3)] & Brick.ClosedX);
    }

    private static Vec3 Norm(Vec3 v) => v * (1f / (float)Math.Sqrt(v.Dot(v)));

    private static Vec3 Cross(Vec3 a, Vec3 b) => new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    private static float Inside(VoxelGrid grid) =>
        Enumerable.Range(2, 6).Sum(x => Enumerable.Range(2, 6).Sum(z => Enumerable.Range(5, 4).Sum(y => grid.WaterAt(x, y, z))));

    private static void Run(VoxelSolver solver, float seconds)
    {
        for (float t = 0f; t < seconds; t += 0.1f)
        {
            solver.Step(0.1f);
        }
    }

    // The voxels of row y = 4 within lo..hi on x and z reached from a start through open x and z faces, as the
    // solver lets water through: never into a solid voxel.
    private static HashSet<(int X, int Z)> Flood(Brick brick, (int X, int Z) start, int lo, int hi)
    {
        var reached = new HashSet<(int X, int Z)> { start };
        var queue = new Queue<(int X, int Z)>([start]);
        while (queue.Count > 0)
        {
            (int x, int z) = queue.Dequeue();
            foreach ((int nx, int nz, bool open) in new[]
            {
                (x + 1, z, (brick.Closed[Brick.Index(x, 4, z)] & Brick.ClosedX) == 0),
                (x - 1, z, x > 0 && (brick.Closed[Brick.Index(x - 1, 4, z)] & Brick.ClosedX) == 0),
                (x, z + 1, (brick.Closed[Brick.Index(x, 4, z)] & Brick.ClosedZ) == 0),
                (x, z - 1, z > 0 && (brick.Closed[Brick.Index(x, 4, z - 1)] & Brick.ClosedZ) == 0),
            })
            {
                if (open && nx >= lo && nx <= hi && nz >= lo && nz <= hi && brick.Solid[Brick.Index(nx, 4, nz)] < 1f && reached.Add((nx, nz)))
                {
                    queue.Enqueue((nx, nz));
                }
            }
        }

        return reached;
    }

    // Open ground at 0 and these pieces.
    private sealed class BoxSolids : ISolids
    {
        private readonly List<SolidBox> _pieces;

        public BoxSolids(List<SolidBox> pieces)
        {
            _pieces = pieces;
        }

        public void Fill(Brick brick, float voxelSize)
        {
            SolidRaster.Ground(brick, voxelSize, (_, _) => 0f);
            SolidRaster.Pieces(brick, voxelSize, _pieces, (_, _) => 0f);
        }

        public bool Ready(int x, int y, int z, float voxelSize) => true;
    }

    [Fact]
    public void TurnedThinAndEdgeCrossingPiecesRasterAsBeforeTheyWereSkipped()
    {
        // A 45-degree thin wall crossing the brick's east edge, a thin floor, a turned thick block and a wall just past
        // the north edge. The checksum of every voxel's solid share and closed faces was taken from the raster before
        // it skipped the voxels a box cannot meet: skipping must not change a thing.
        float r = MathF.Sqrt(0.5f);
        var boxes = new List<SolidBox>
        {
            new(new Vec3(15.5f, 3f, 8f), new Vec3(r, 0f, r), new Vec3(0f, 1f, 0f), new Vec3(-r, 0f, r), new Vec3(3f, 2f, 0.1f)),
            SolidBox.Between(new Vec3(2f, 5.8f, 2f), new Vec3(9f, 6f, 9f)),
            new(new Vec3(5f, 9f, 12f), new Vec3(0.8f, 0f, 0.6f), new Vec3(0f, 1f, 0f), new Vec3(-0.6f, 0f, 0.8f), new Vec3(1.5f, 1f, 1f)),
            SolidBox.Between(new Vec3(4f, 1f, 16.05f), new Vec3(12f, 4f, 16.25f)),
        };
        var brick = new Brick(0, 0, 0);
        SolidRaster.Ground(brick, 1f, (x, _) => 0.5f + (0.05f * x));
        SolidRaster.Pieces(brick, 1f, boxes, (x, _) => 0.5f + (0.05f * x));

        double sum = 0;
        for (int i = 0; i < Brick.Count; i++)
        {
            sum += (brick.Solid[i] * ((i % 89) + 1)) + (brick.Closed[i] * ((i % 13) + 1));
        }

        Assert.Equal(12758.375200033188, sum, 6);
    }
}
