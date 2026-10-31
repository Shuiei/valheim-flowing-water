using System.Collections.Generic;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// A source facing a way pours out of its outlet (<see cref="VoxelGrid.PourOutlet"/>): its rate shared out among the
/// voxels across it at the marker, none ahead of it, each share leaving the way it faces; an outlet partly against a
/// wall pours all its rate out of the rest, and one with no room at all pours nothing; a jet thrown up pours where it
/// lands (on the floor out ahead, at the foot of a wall it meets, below a cliff it leaps), not at its outlet.
/// </summary>
public class VoxelOutletPourTests
{
    // The server's metre voxels; a 4 m outlet facing east, its middle at (10.5, 0.3, 10.5), so across it (south) it
    // spans the voxels z 12 down to 9 of column x 10.
    private const float S = 1f, Rate = 2f, Dt = 0.5f;
    private static readonly SourceShape River = new(false, 90f, 4f, 0f, 0f);
    private static readonly int[] Across = [12, 11, 10, 9];

    // A wide jet's rate: through its 4 m slot, it leaves at 8 m/s.
    private const float WideRate = 8f;

    [Fact]
    public void AnOutletSharesItsRateAmongTheVoxelsAcrossItAtTheMarkerEachSentTheWayItFaces()
    {
        var grid = new VoxelGrid(S, new TestSolids(0));

        Assert.True(grid.PourOutlet(10.5f, 0.3f, 10.5f, River, Rate, Dt));

        foreach (int z in Across)
        {
            Assert.Equal(Rate * Dt / 4f, grid.WaterAt(10, 0, z), 4);
            Assert.True(grid.Locate(10, 0, z, out Brick brick, out int i));
            Assert.Equal(Rate / 4f, brick.FlowX[i], 4);
            Assert.Equal(0f, brick.FlowZ[i], 4);
        }

        Assert.Equal(0f, grid.WaterAt(11, 0, 10));
        Assert.Equal(Rate * Dt, grid.TotalWater(), 4);
    }

    [Fact]
    public void AnOutletPartlyAgainstAWallPoursAllItsRateOutOfTheRest()
    {
        // Ground standing past what Room looks up through over the outlet's two northern voxels.
        var grid = new VoxelGrid(S, new TestSolids(0, ground: (x, z) => x == 10 && z >= 11 ? 200f : 0f));

        Assert.True(grid.PourOutlet(10.5f, 0.3f, 10.5f, River, Rate, Dt));

        Assert.Equal(Rate * Dt / 2f, grid.WaterAt(10, 0, 10), 4);
        Assert.Equal(Rate * Dt / 2f, grid.WaterAt(10, 0, 9), 4);
        Assert.Equal(Rate * Dt, grid.TotalWater(), 4);
    }

    [Fact]
    public void AnOutletWithNoRoomPoursNothing()
    {
        var grid = new VoxelGrid(S, new TestSolids(0)) { MaxBricks = 0 };

        Assert.False(grid.PourOutlet(10.5f, 0.3f, 10.5f, River, Rate, Dt));
        Assert.Equal(0f, grid.TotalWater());
    }

    [Fact]
    public void AJetUnderWaterPoursAtItsOutletThrownUpAndOn()
    {
        // Water standing over the outlet already: the jet cannot fly, and leaves its voxel the way it was aimed.
        var grid = new VoxelGrid(S, new TestSolids(0));
        grid.Pour(10, 0, 10, 1f);

        Assert.True(grid.PourOutlet(10.5f, 0.3f, 10.5f, SourceShape.Jet(0f), Rate, Dt));

        Assert.Equal(1f + (Rate * Dt), grid.WaterAt(10, 0, 10), 4);
        Assert.True(grid.Locate(10, 0, 10, out Brick brick, out int i));
        Assert.True(brick.FlowZ[i] > 0f && brick.FlowY[i] > 0f, $"north {brick.FlowZ[i]}, up {brick.FlowY[i]}");
    }

    [Fact]
    public void AJetThrownUpPoursWhereItLandsAndNoneAtTheOutletsFoot()
    {
        // A floor one voxel thick under row 1; a point's jet facing east, thrown up 45 degrees at 12 m/s (2 m³/s through
        // a point's slot is faster than that), which lands about 14.7 m out, a little more from 0.3 m over the floor.
        var grid = new VoxelGrid(S, new TestSolids(1));
        var jet = new SourceShape(false, 90f, 0f, 0f, 45f);

        Assert.True(grid.PourOutlet(10.5f, 1.3f, 10.5f, jet, Rate, Dt));

        Assert.Equal(0f, grid.WaterAt(10, 1, 10));
        float lands = 10.5f + (SourceShape.MostSpeed * SourceShape.MostSpeed / VoxelSolver.Gravity);
        Assert.Equal(Rate * Dt, grid.WaterAt((int)lands, 1, 10), 4);
        Assert.True(grid.Locate((int)lands, 1, 10, out Brick brick, out int i));
        Assert.True(brick.FlowX[i] > 0f, $"east {brick.FlowX[i]}");
        Assert.Equal(0f, brick.FlowY[i]);
        Assert.Equal(Rate * Dt, grid.TotalWater(), 4);
    }

    [Fact]
    public void AJetMeetingAWallPoursAtItsFoot()
    {
        // Ground standing 20 m high from 5 m east of the source: the jet meets it rising, and its water falls there.
        var grid = new VoxelGrid(S, new TestSolids(0, ground: (x, z) => x >= 15 ? 20f : 1f));
        var jet = new SourceShape(false, 90f, 0f, 0f, 45f);

        Assert.True(grid.PourOutlet(10.5f, 1.3f, 10.5f, jet, Rate, Dt));

        Assert.Equal(0f, grid.WaterAt(10, 1, 10));
        Assert.Equal(Rate * Dt, grid.WaterAt(14, 5, 10) + grid.WaterAt(14, 4, 10) + grid.WaterAt(14, 6, 10), 4);
    }

    [Fact]
    public void AJetOffACliffLandsAtTheCliffsFootFartherOut()
    {
        // The source on a ledge 10 m over the ground below, the ledge ending a metre east of it.
        var grid = new VoxelGrid(S, new TestSolids(0, ground: (x, z) => x <= 11 ? 11f : 1f));
        var jet = new SourceShape(false, 90f, 0f, 0f, 20f);
        (float X, float Y, float Z)? landing = grid.Lands(10.5f, 11.3f, 10.5f, new Vec3(4f * 0.94f, 4f * 0.342f, 0f));

        Assert.NotNull(landing);
        Assert.InRange(landing.Value.Y, 1f, 2f);
        Assert.InRange(landing.Value.X, 13f, 18f);
        Assert.True(grid.PourOutlet(10.5f, 11.3f, 10.5f, jet, 0.25f, Dt));
        Assert.Equal(0f, grid.WaterAt(10, 11, 10));
    }

    [Fact]
    public void EveryPlaceOfAWideJetLandsAsFarOut()
    {
        // The 4 m outlet thrown up 45 degrees, 8 m³/s through its slot leaving at 8 m/s: each of its four places flies
        // from the outlet's own height, about 6.5 m out, none from where the one before it came down.
        var grid = new VoxelGrid(S, new TestSolids(1));
        var jet = new SourceShape(false, 90f, 4f, 0f, 45f);

        Assert.True(grid.PourOutlet(10.5f, 1.3f, 10.5f, jet, WideRate, Dt));

        int lands = (int)(10.5f + (8f * 8f / VoxelSolver.Gravity));
        foreach (int z in Across)
        {
            Assert.Equal(0f, grid.WaterAt(10, 1, z));
            Assert.Equal(WideRate * Dt / 4f, grid.WaterAt(lands, 1, z), 4);
        }

        Assert.Equal(WideRate * Dt, grid.TotalWater(), 4);
    }

    [Fact]
    public void EachShareOfASpreadJetPoursWhereItsOwnWayLands()
    {
        // The same outlet fanned out 60 degrees across: each place's share flies its own way and lands in its own
        // voxel (where, looked at before any of it is poured, so none lands on another's water).
        var grid = new VoxelGrid(S, new TestSolids(1));
        var jet = new SourceShape(false, 90f, 4f, 60f, 45f);
        float speed = jet.Speed(WideRate);
        (float rightX, float rightZ) = jet.Right;
        var landed = new HashSet<(int, int)>();
        foreach ((float across, float offset) in jet.Outlet(S))
        {
            Vec3 way = jet.Direction(across);
            (float X, float Y, float Z)? landing = grid.Lands(10.5f + (rightX * offset), 1.3f, 10.5f + (rightZ * offset), new Vec3(way.X * speed, way.Y * speed, way.Z * speed));
            Assert.NotNull(landing);
            Assert.True(landed.Add(((int)landing.Value.X, (int)landing.Value.Z)), $"two shares land at {landing}");
        }

        Assert.True(grid.PourOutlet(10.5f, 1.3f, 10.5f, jet, WideRate, Dt));

        foreach ((int x, int z) in landed)
        {
            Assert.Equal(WideRate * Dt / 4f, grid.WaterAt(x, 1, z), 4);
        }

        Assert.Equal(WideRate * Dt, grid.TotalWater(), 4);
    }

    [Fact]
    public void AJetComesDownOnTheSolidLowerShareOfAVoxel()
    {
        // Ground half a voxel up row 1 from x 12 on: water leaving 1.9 m up flat east at 6 m/s comes down on it at
        // 1.5 m, not through it to the floor under the row.
        var grid = new VoxelGrid(S, new TestSolids(0, ground: (x, z) => x >= 12 ? 1.5f : 1f));

        (float X, float Y, float Z)? landing = grid.Lands(10.5f, 1.9f, 10.5f, new Vec3(6f, 0f, 0f));

        Assert.NotNull(landing);
        Assert.InRange(landing.Value.Y, 1.5f, 1.6f);
        Assert.InRange(landing.Value.X, 12f, 13f);
    }

    [Fact]
    public void AJetFliesOutOfItsOutletsOwnVoxelThoughItsGroundIsCountedHigher()
    {
        // A rock's curved top: its outlet's voxel is counted solid to 1.8 m (the rock's height at the column's middle),
        // the outlet 1.3 m up on the rock where it stands. The jet still flies, and lands out on the floor east.
        var grid = new VoxelGrid(S, new TestSolids(0, ground: (x, _) => x is >= 10 and < 11 ? 1.8f : 1f));

        (float X, float Y, float Z)? landing = grid.Lands(10.5f, 1.3f, 10.5f, new Vec3(6f, 3f, 0f));

        Assert.NotNull(landing);
        Assert.True(landing.Value.X > 12f, $"lands at x {landing.Value.X}");
        Assert.InRange(landing.Value.Y, 1f, 1.2f);
    }

    [Fact]
    public void AJetWhoseWayPassesTheBrickCapPoursAtItsOutletThrownUp()
    {
        // Only the outlet's own brick may be made: the jet's way out to x 25 needs the next one, so it pours where it is.
        var grid = new VoxelGrid(S, new TestSolids(1)) { MaxBricks = 1 };
        var jet = new SourceShape(false, 90f, 0f, 0f, 45f);

        Assert.True(grid.PourOutlet(10.5f, 1.3f, 10.5f, jet, Rate, Dt));

        Assert.Equal(Rate * Dt, grid.WaterAt(10, 1, 10), 4);
        Assert.True(grid.Locate(10, 1, 10, out Brick brick, out int i));
        Assert.True(brick.FlowY[i] > 0f, $"up {brick.FlowY[i]}");
    }

    [Fact]
    public void AJetPoursAtItsOutletTillTheGroundItFliesToCanBeRead()
    {
        // The bricks east of x 16 cannot read their solids yet: the jet's way is not looked at there (no brick made,
        // nothing read in wait), and it pours at its outlet; once they can, it lands out there.
        var solids = new TestSolids(1) { Waiting = (x, _, _) => x >= 1 };
        var grid = new VoxelGrid(S, solids);
        var jet = new SourceShape(false, 90f, 0f, 0f, 45f);
        var velocity = new Vec3(8.5f, 8.5f, 0f);

        Assert.True(grid.PourOutlet(10.5f, 1.3f, 10.5f, jet, Rate, Dt));
        Assert.Equal(Rate * Dt, grid.WaterAt(10, 1, 10), 4);
        Assert.Null(grid.Find(1, 0, 0));

        solids.Waiting = null;
        (float X, float Y, float Z)? landing = grid.Lands(10.5f, 2.3f, 10.5f, velocity);
        Assert.NotNull(landing);
        Assert.InRange(landing.Value.X, 24f, 27f);
    }

    [Fact]
    public void ABrickAJetFliesThroughIsKeptWhileItFlies()
    {
        // The jet's flight crosses into brick x 1 before it lands there: kept while the jet flies (its solids read once),
        // forgotten once dry with no jet through it.
        var grid = new VoxelGrid(S, new TestSolids(1));
        grid.Lands(10.5f, 1.3f, 10.5f, new Vec3(8.5f, 8.5f, 0f));
        Assert.NotNull(grid.Find(1, 0, 0));

        for (int k = 0; k < 3; k++)
        {
            grid.Lands(10.5f, 1.3f, 10.5f, new Vec3(8.5f, 8.5f, 0f));
            grid.ForgetDry(10f, 5f);
            Assert.NotNull(grid.Find(1, 0, 0));
        }

        grid.ForgetDry(10f, 5f);
        Assert.Null(grid.Find(1, 0, 0));
    }

    [Fact]
    public void OnTheServerAJetOffALedgeRunsOnBelowItAndNoneDownTheLedgesFace()
    {
        // The source on a ledge 10 m over the ground, its edge a metre east; its jet thrown 20 degrees up, poured and
        // moved by the solver for 8 s as the server does: the water is below the ledge, run on east, none on the ledge
        // or down its face under the source (where all of it ran, poured at the outlet's foot).
        var grid = new VoxelGrid(S, new TestSolids(0, ground: (x, z) => x <= 11 ? 11f : 1f));
        var solver = new VoxelSolver(grid);
        var jet = new SourceShape(false, 90f, 0f, 0f, 20f);
        const float Step = 0.05f, LedgeRate = 0.25f;
        for (int k = 0; k < 8f / Step; k++)
        {
            Assert.True(grid.PourOutlet(10.5f, 11.3f, 10.5f, jet, LedgeRate, Step));
            grid.MakeWanted();
            solver.Step(Step);
        }

        float onLedge = 0f, underIt = 0f, beyond = 0f;
        for (int x = 0; x < 48; x++)
        {
            for (int z = 0; z < 32; z++)
            {
                for (int y = 0; y < 16; y++)
                {
                    float water = grid.WaterAt(x, y, z);
                    if (x <= 11)
                    {
                        onLedge += water;
                    }
                    else if (x <= 12)
                    {
                        underIt += water;
                    }
                    else
                    {
                        beyond += water;
                    }
                }
            }
        }

        float total = grid.TotalWater();
        Assert.True(total > 0.9f * LedgeRate * 8f, $"{total} m³ of {LedgeRate * 8f}");
        Assert.True(onLedge < 0.01f * total, $"{onLedge} m³ on the ledge of {total}");
        Assert.True(beyond > 0.9f * total, $"{beyond} m³ beyond the ledge's foot of {total} ({underIt} at its foot)");
    }

    [Fact]
    public void ARiverAlongTheGroundStillPoursAtItsOutlet()
    {
        var grid = new VoxelGrid(S, new TestSolids(1));

        Assert.True(grid.PourOutlet(10.5f, 1.3f, 10.5f, new SourceShape(false, 90f, 0f, 0f, 0f), Rate, Dt));

        Assert.Equal(Rate * Dt, grid.WaterAt(10, 1, 10), 4);
    }
}
