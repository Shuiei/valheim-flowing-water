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
    public void AFloorClosesTheFacesUnderIt()
    {
        var brick = new Brick(0, 0, 0);

        SolidRaster.Pieces(brick, 1f, [SolidBox.Between(new Vec3(0f, 4.9f, 0f), new Vec3(2f, 5.1f, 2f))]);

        Assert.Equal(Brick.ClosedY, brick.Closed[Brick.Index(1, 4, 1)]);
        Assert.Equal(0, brick.Closed[Brick.Index(1, 5, 1)]);
        Assert.Equal(0, brick.Closed[Brick.Index(2, 4, 1)]);
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

    private static float Inside(VoxelGrid grid) =>
        Enumerable.Range(2, 6).Sum(x => Enumerable.Range(2, 6).Sum(z => Enumerable.Range(5, 4).Sum(y => grid.WaterAt(x, y, z))));

    private static void Run(VoxelSolver solver, float seconds)
    {
        for (float t = 0f; t < seconds; t += 0.1f)
        {
            solver.Step(0.1f);
        }
    }

    // The voxels of row y = 4 within lo..hi on x and z reached from a start through open x and z faces.
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
                if (open && nx >= lo && nx <= hi && nz >= lo && nz <= hi && reached.Add((nx, nz)))
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
            SolidRaster.Pieces(brick, voxelSize, _pieces);
        }
    }
}
