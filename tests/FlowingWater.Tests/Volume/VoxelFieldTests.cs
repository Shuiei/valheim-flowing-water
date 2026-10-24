using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// The water a player's game knows, and the surface it draws: the level a swimmer floats at (in a stack, just over
/// it, on a floor), the velocity carrying them, and the triangles of a pool, a raised pool and a fall.
/// </summary>
public class VoxelFieldTests
{
    [Fact]
    public void APointInOrJustOverAStackFloatsAtItsTop()
    {
        VoxelField field = Field((3, 0, 3, 1f, 0f), (3, 1, 3, 1f, 0f), (3, 2, 3, 0.4f, 0f));

        Assert.Equal(2.4f, field.SurfaceAt(3.5f, 0.2f, 3.5f), 4);
        Assert.Equal(2.4f, field.SurfaceAt(3.5f, 2.9f, 3.5f), 4);
        Assert.Equal(2.4f, field.SurfaceAt(3.5f, 3.3f, 3.5f), 4);
        Assert.Equal(float.NegativeInfinity, field.SurfaceAt(3.5f, 4.2f, 3.5f));
        Assert.Equal(float.NegativeInfinity, field.SurfaceAt(4.5f, 1f, 3.5f));
    }

    [Fact]
    public void WaterOnAFloorStandsOnItsSolidShare()
    {
        // A raised floor's top 0.6 m into row 7, with 0.3 of the voxel of water over it.
        VoxelField field = Field((3, 7, 3, 0.3f, 0.6f));

        Assert.Equal(7.9f, field.SurfaceAt(3.5f, 7.7f, 3.5f), 4);
        Assert.Equal(7.6f, field.Floor(3, 7, 3), 4);
    }

    [Fact]
    public void WaterUnderARaisedFloorFloatsAtItsOwnLevelNotThePoolsOnTheFloor()
    {
        // Water filling row 6 under a floor whose top is 0.6 m into row 7, and a pool over the floor.
        VoxelField field = Field((3, 6, 3, 1f, 0f), (3, 7, 3, 0.3f, 0.6f), (3, 8, 3, 0.5f, 0f));

        Assert.Equal(7f, field.SurfaceAt(3.5f, 6.4f, 3.5f), 4);
        Assert.Equal(8.5f, field.SurfaceAt(3.5f, 7.8f, 3.5f), 4);

        // Both are drawn: a top for the water under the floor, and one for the pool.
        AssertDrawnAt(VoxelSurface.Build(field, field.Find(0, 0, 0)), 7f, 8.5f);
    }

    [Fact]
    public void ARayDownOntoAPoolMeetsItsSurface()
    {
        // A pool two voxels across, 1.5 deep (level 1.5), and a ray from 5 m over it, down at 45 degrees towards it.
        VoxelField field = Field((3, 0, 3, 1f, 0f), (3, 1, 3, 0.5f, 0f), (4, 0, 3, 1f, 0f), (4, 1, 3, 0.5f, 0f));
        float d = (float)System.Math.Sqrt(0.5);

        Assert.True(field.RayToSurface(0.5f, 5f, 3.5f, d, -d, 0f, 20f, out float distance));

        // Down 3.5 m to the level: as far along, x 4 (in the pool).
        Assert.Equal(3.5f / d, distance, 3);
    }

    [Fact]
    public void ARayMissingTheWaterOrStoppingShortMeetsNothing()
    {
        VoxelField field = Field((3, 0, 3, 1f, 0f), (3, 1, 3, 0.5f, 0f));
        float d = (float)System.Math.Sqrt(0.5);

        Assert.False(field.RayToSurface(0.5f, 5f, 8.5f, d, -d, 0f, 20f, out _));
        Assert.False(field.RayToSurface(0.5f, 5f, 3.5f, d, -d, 0f, 4f, out _));
    }

    [Fact]
    public void ARayMeetsASheetThinnerThanItsStep()
    {
        // 0.3 of a voxel of water on a floor 0.6 into row 7 (surface 7.9): straight down from 8.05, the next step
        // (7.55) is under the floor, in no water.
        VoxelField field = Field((3, 7, 3, 0.3f, 0.6f));

        Assert.True(field.RayToSurface(3.5f, 8.05f, 3.5f, 0f, -1f, 0f, 5f, out float distance));
        Assert.Equal(0.15f, distance, 3);
    }

    [Fact]
    public void ARayMeetsWaterInItsLastShortStep()
    {
        // A pool with its level at 1.5, a ray straight down from 2.1 stopping at 1.45: its steps at 2.1 and 1.6 are
        // over the water, its last, shorter one (0.15) reaches it.
        VoxelField field = Field((3, 0, 3, 1f, 0f), (3, 1, 3, 0.5f, 0f));

        Assert.True(field.RayToSurface(3.5f, 2.1f, 3.5f, 0f, -1f, 0f, 0.65f, out float distance));
        Assert.Equal(0.6f, distance, 3);
    }

    [Fact]
    public void ARayGoingIntoAPoolThroughItsSideUnderItsLevelMeetsNothing()
    {
        // A pool with its level at 1.5, rays from beside it under that level: level, and gently down.
        VoxelField field = Field((3, 0, 3, 1f, 0f), (3, 1, 3, 0.5f, 0f), (4, 0, 3, 1f, 0f), (4, 1, 3, 0.5f, 0f));
        float length = (float)System.Math.Sqrt(1.01);

        Assert.False(field.RayToSurface(0.5f, 1f, 3.5f, 1f, 0f, 0f, 10f, out _));
        Assert.False(field.RayToSurface(0.5f, 1.4f, 3.5f, 1f / length, -0.1f / length, 0f, 10f, out _));
    }

    [Fact]
    public void ARayOverAHighPoolOntoALowerOneMeetsTheLowerOnesSurface()
    {
        // A pool with its level at 1.5 and beside it, east, one at 1.0: a ray from over the high one down past its level
        // only once over the low one meets the low one at its own level.
        VoxelField field = Field((3, 0, 3, 1f, 0f), (3, 1, 3, 0.5f, 0f), (4, 0, 3, 1f, 0f), (5, 0, 3, 1f, 0f), (6, 0, 3, 1f, 0f));
        float length = (float)System.Math.Sqrt(1.25);

        Assert.True(field.RayToSurface(3.9f, 1.6f, 3.5f, 1f / length, -0.5f / length, 0f, 10f, out float distance));
        Assert.Equal(0.6f * length / 0.5f, distance, 3);
    }

    [Fact]
    public void ARayStartingInTheWaterMeetsItOnlyWhereItGoesBackIn()
    {
        // From inside the pool, straight up and out of it: never back in.
        VoxelField field = Field((3, 0, 3, 1f, 0f), (3, 1, 3, 0.5f, 0f));

        Assert.False(field.RayToSurface(3.5f, 0.5f, 3.5f, 0f, 1f, 0f, 10f, out _));
    }

    [Fact]
    public void TheVelocityIsTheVoxelsTheSwimmerIsIn()
    {
        VoxelField field = Current(1.5f, -0.5f);

        Assert.True(field.VelocityAt(2.5f, 0.5f, 2.5f, out float vx, out _, out float vz));
        Assert.Equal((1.5f, -0.5f), (vx, vz));
        Assert.False(field.VelocityAt(5.5f, 0.5f, 2.5f, out _, out _, out _));
    }

    [Fact]
    public void APointInsideARaisedFloorIsNotInTheWaterOnIt()
    {
        // The floor's top is 0.6 m into row 7, the water over it to 0.9: a point 0.2 m into the row is in the floor.
        VoxelField field = Field((3, 7, 3, 0.3f, 0.6f));

        Assert.Equal(float.NegativeInfinity, field.SurfaceAt(3.5f, 7.2f, 3.5f));
        Assert.False(field.VelocityAt(3.5f, 7.2f, 3.5f, out _, out _, out _));
    }

    [Fact]
    public void NoCurrentCarriesAPointOverTheWater()
    {
        // A voxel of water with a current: a point a third of a voxel over its surface floats there, unmoved.
        VoxelField field = Current(1.5f, 0f);

        Assert.Equal(1f, field.SurfaceAt(2.5f, 1.3f, 2.5f), 4);
        Assert.False(field.VelocityAt(2.5f, 1.3f, 2.5f, out _, out _, out _));
        Assert.True(field.VelocityAt(2.5f, 0.9f, 2.5f, out _, out _, out _));
    }

    [Fact]
    public void AWaterTopUnderAFloorIsDrawnAtItsOwnLevel()
    {
        // Two by two columns full in row 6, under a floor half into row 7 with a little water on it, nothing over:
        // the lower water's top is drawn at 7, not at the level of the water on the floor.
        var cells = (from x in Enumerable.Range(3, 2) from z in Enumerable.Range(3, 2) from c in new[] { (x, 6, z, 1f, 0f), (x, 7, z, 0.3f, 0.5f) } select c).ToArray();
        VoxelField field = Field(cells);

        AssertDrawnAt(VoxelSurface.Build(field, field.Find(0, 0, 0)), 7f, 7.8f);
    }

    [Fact]
    public void AStillPoolIsAFlatTopWithSidesOnlyWhereItEnds()
    {
        // A 3 by 3 pool, half a voxel deep, in a pit (its sides are the pit's: drawn, then hidden by the ground).
        var cells = (from x in Enumerable.Range(2, 3) from z in Enumerable.Range(2, 3) select (x, 0, z, 0.5f, 0f)).ToArray();
        VoxelField field = Field(cells);

        SurfaceMesh mesh = VoxelSurface.Build(field, field.Find(0, 0, 0));

        float[] tops = [.. mesh.Vertices.Where(v => v.Y > 0.01f).Select(v => v.Y)];
        Assert.All(tops, y => Assert.Equal(0.5f, y, 4));

        // Nine tops (two triangles each) and twelve sides round the edge.
        Assert.Equal((9 + 12) * 6, mesh.Triangles.Count);

        // The middle corner is among water all round, an outer one at the shore.
        int middle = mesh.Vertices.FindIndex(v => v.X == 3f && v.Z == 3f && v.Y > 0.01f);
        int outer = mesh.Vertices.FindIndex(v => v.X == 2f && v.Z == 2f && v.Y > 0.01f);
        Assert.Equal(0.5f, mesh.Depths[middle], 4);
        Assert.Equal(1f, mesh.Shores[middle]);
        Assert.Equal(0.25f, mesh.Shores[outer]);
    }

    [Fact]
    public void AStepDownIsASmoothSlopeNotAStair()
    {
        // Two columns side by side, the east one's top half a voxel lower.
        VoxelField field = Field((2, 0, 2, 1f, 0f), (2, 1, 2, 0.5f, 0f), (3, 0, 2, 1f, 0f));

        SurfaceMesh mesh = VoxelSurface.Build(field, field.Find(0, 0, 0));

        // The corners they share are at the mean of 1.5 and 1.0, and so are the tops of the sides there: no side
        // sticks out above the surface drawn.
        int shared = mesh.Vertices.FindIndex(v => v.X == 3f && v.Z == 2f && v.Y > 1.1f && v.Y < 1.4f);
        Assert.True(shared >= 0);
        Assert.Equal(1.25f, mesh.Vertices[shared].Y, 4);
        Assert.DoesNotContain(mesh.Vertices, v => v.X == 3f && System.Math.Abs(v.Y - 1.5f) < 0.01f);
    }

    [Fact]
    public void AFallOverALedgeHasItsSidesDrawnDownToItsFloor()
    {
        // A single column two voxels tall standing in the air (falling water).
        VoxelField field = Field((5, 3, 5, 1f, 0f), (5, 4, 5, 1f, 0f));

        SurfaceMesh mesh = VoxelSurface.Build(field, field.Find(0, 0, 0));

        // One top and four sides on each of its two voxels.
        Assert.Equal((1 + 8) * 6, mesh.Triangles.Count);
        Assert.Equal(3f, mesh.Vertices.Min(v => v.Y), 4);
        Assert.Equal(5f, mesh.Vertices.Max(v => v.Y), 4);
    }

    // Some vertex of the surface lies at each of these heights.
    private static void AssertDrawnAt(SurfaceMesh mesh, params float[] heights)
    {
        foreach (float height in heights)
        {
            Assert.Contains(mesh.Vertices, v => System.Math.Abs(v.Y - height) < 0.01f);
        }
    }

    // A full voxel at (2, 0, 2) running this fast east and north.
    private static VoxelField Current(float vx, float vz)
    {
        var view = new BrickView(0, 0, 0, 1f);
        int i = Brick.Index(2, 0, 2);
        view.Water[i] = 1f;
        view.VelocityX[i] = vx;
        view.VelocityZ[i] = vz;
        var field = new VoxelField();
        field.Set(view);
        return field;
    }

    private static VoxelField Field(params (int X, int Y, int Z, float Water, float Solid)[] voxels)
    {
        var view = new BrickView(0, 0, 0, 1f);
        foreach ((int x, int y, int z, float water, float solid) in voxels)
        {
            int i = Brick.Index(x, y, z);
            view.Water[i] = water;
            view.Solid[i] = solid;
        }

        var field = new VoxelField();
        field.Set(view);
        return field;
    }
}
