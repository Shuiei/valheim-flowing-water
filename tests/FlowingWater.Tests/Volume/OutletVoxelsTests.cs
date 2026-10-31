using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// The voxels a facing source's outlet spans in the volume water, opened in the card's solids: a point's two cells
/// across within its voxel; a wide outlet across every voxel it spans; up as high as twice the slot its rate needs and
/// as the ground under its cells may rise.
/// </summary>
public class OutletVoxelsTests
{
    private const float Voxel = 1f, Cell = 0.25f;

    [Fact]
    public void APointsOutletIsTheColumnItStandsIn()
    {
        // A point facing east, 2 m³/s at 12 m/s through two cells across: a slot 0.33 m high, opened to 0.67 m and the
        // ground's rise over it, 1.5 m more: rows 5 to 7 of its own column.
        var point = new SourceShape(false, 90f, 0f, 0f, 30f);

        var voxels = OutletVoxels.Of(new Vec3(10.5f, 5.2f, 10.5f), point, 2f, Voxel, Cell);

        Assert.Equal([(10, 5, 10), (10, 6, 10), (10, 7, 10)], voxels.OrderBy(v => v.Y));
    }

    [Fact]
    public void AWideOutletSpansEveryVoxelAcrossIt()
    {
        // 4 m wide facing north: across it east and west, x 8.5 to 12.5.
        var wide = new SourceShape(false, 0f, 4f, 0f, 30f);

        var voxels = OutletVoxels.Of(new Vec3(10.5f, 5.2f, 10.5f), wide, 8f, Voxel, Cell);

        Assert.Equal([8, 9, 10, 11, 12], voxels.Select(v => v.X).Distinct().OrderBy(x => x));
        Assert.All(voxels, v => Assert.Equal(10, v.Z));
    }

    [Fact]
    public void AnOutletBesideAVoxelsEdgeTakesTheVoxelAlongItsWayToo()
    {
        // Facing east a hair west of a voxel's east edge: the cell along its way about the marker reaches over it.
        var point = new SourceShape(false, 90f, 0f, 0f, 30f);

        var voxels = OutletVoxels.Of(new Vec3(10.95f, 5.2f, 10.5f), point, 2f, Voxel, Cell);

        Assert.Contains((10, 5, 10), voxels);
        Assert.Contains((11, 5, 10), voxels);
    }

    [Fact]
    public void AStrongSlimJetsSlotRisesAsItsRateNeeds()
    {
        // 20 m³/s through a point at 12 m/s, two cells across: a slot 3.3 m high, twice that opened and the ground's rise.
        var point = new SourceShape(false, 90f, 0f, 0f, 30f);

        var voxels = OutletVoxels.Of(new Vec3(10.5f, 5.2f, 10.5f), point, 20f, Voxel, Cell);

        Assert.Equal(5, voxels.Min(v => v.Y));
        Assert.Equal(13, voxels.Max(v => v.Y));
    }
}
