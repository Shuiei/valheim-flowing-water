using System.Collections.Generic;
using System.Linq;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The server's ground per zone: the generated heights asked for once a zone, the players' changes applied over them
/// (again, from the generated heights, each time they move on) and the zones told of then, only those; a zone let
/// go is made anew; a broken change or generated heights that are not numbers are told of and left out.
/// </summary>
public class ZoneHeightsTests
{
    private const int Vertices = 65 * 65;

    [Fact]
    public void AZonesGeneratedHeightsAreAskedForOnceAndItsChangesAppliedOverThem()
    {
        var ground = new Ground();
        ground.Raise(1, 0, 2f, revision: 1);
        var heights = new ZoneHeights<int, (int, int)>(ground, Vertices);

        Assert.Equal(12f, heights.Heights(1)[0], 4);
        Assert.Equal(10f, heights.Heights(1)[1]);
        Assert.Equal(20f, heights.Heights(2)[0]);
        Assert.Equal([1, 2], ground.Generations);
    }

    [Fact]
    public void ChangesMovedOnAreAppliedAgainOverTheGeneratedHeightsAndTheirZoneToldOf()
    {
        var ground = new Ground();
        ground.Raise(1, 0, 2f, revision: 1);
        var heights = new ZoneHeights<int, (int, int)>(ground, Vertices);
        heights.Heights(1);
        heights.Heights(2);
        var told = new List<int>();

        heights.Refresh(told.Add);
        Assert.Empty(told);

        // Raised 3 m now (not 2 + 3): the change is the whole of it, over the generated ground.
        ground.Raise(1, 0, 3f, revision: 2);
        heights.Refresh(told.Add);

        Assert.Equal([1], told);
        Assert.Equal(13f, heights.Heights(1)[0], 4);
        Assert.Equal([1, 2], ground.Generations);

        // The same revision of another object (the zone's changes made again) is a change too, and so is none.
        ground.Changes[1] = ((9, 2), ground.Changes[1].Data);
        heights.Refresh(told.Add);
        ground.Changes.Remove(1);
        heights.Refresh(told.Add);

        Assert.Equal([1, 1, 1], told);
        Assert.Equal(10f, heights.Heights(1)[0]);
    }

    [Fact]
    public void AZoneLetGoIsMadeAnewWhenReadAgainAndNotRefreshedMeanwhile()
    {
        var ground = new Ground();
        var heights = new ZoneHeights<int, (int, int)>(ground, Vertices);
        heights.Heights(1);
        heights.Heights(2);

        heights.Keep([2]);
        ground.Raise(1, 0, 1f, revision: 1);
        var told = new List<int>();
        heights.Refresh(told.Add);

        Assert.Empty(told);
        Assert.Equal(11f, heights.Heights(1)[0], 4);
        Assert.Equal([1, 2, 1], ground.Generations);
    }

    [Fact]
    public void BrokenChangesLeaveTheGeneratedGroundAndAreToldOf()
    {
        var ground = new Ground();
        ground.Changes[1] = ((1, 1), [1, 2, 3]);
        var heights = new ZoneHeights<int, (int, int)>(ground, Vertices);

        Assert.All(heights.Heights(1), h => Assert.Equal(10f, h));
        Assert.Single(ground.Warnings);
        Assert.StartsWith("1: terrain changes unreadable", ground.Warnings[0]);
    }

    [Fact]
    public void GeneratedHeightsThatAreNotNumbersAreTakenAt0()
    {
        var ground = new Ground { Broken = [float.NaN, float.NegativeInfinity] };
        var heights = new ZoneHeights<int, (int, int)>(ground, Vertices);

        float[] made = heights.Heights(1);

        Assert.Equal(0f, made[0]);
        Assert.Equal(0f, made[1]);
        Assert.Equal(10f, made[2]);
        Assert.Equal(["1: the heightmap builder gave 2 heights that are not numbers; the water takes the ground there at 0 m"], ground.Warnings);
    }

    [Fact]
    public void WithoutGeneratedHeightsTheGroundIsAt0UntilTheChangesMoveOn()
    {
        var ground = new Ground { Count = 10 };
        var heights = new ZoneHeights<int, (int, int)>(ground, Vertices);

        Assert.All(heights.Heights(1), h => Assert.Equal(0f, h));
        Assert.Equal([$"1: the heightmap builder gave 10 vertices, expected {Vertices}"], ground.Warnings);

        // Asked for again (and told of) once the zone's changes move on, here once the game gives them.
        ground.Count = Vertices;
        ground.Raise(1, 0, 1f, revision: 1);
        var told = new List<int>();
        heights.Refresh(told.Add);

        Assert.Equal([1], told);
        Assert.Equal(11f, heights.Heights(1)[0], 4);
    }

    [Fact]
    public void AZoneWithChangesWhoseHeightsCannotBeGeneratedIsAskedForAgainOnlyOnceTheChangesMoveOn()
    {
        var ground = new Ground { Count = 10 };
        ground.Raise(1, 0, 1f, revision: 1);
        var heights = new ZoneHeights<int, (int, int)>(ground, Vertices);
        heights.Heights(1);
        var told = new List<int>();

        // The changes as they were: the zone is neither made again nor told of, look after look.
        heights.Refresh(told.Add);
        heights.Refresh(told.Add);

        Assert.Empty(told);
        Assert.Equal([1], ground.Generations);
        Assert.Single(ground.Warnings);

        // Once they move on, it is asked for again.
        ground.Raise(1, 0, 1f, revision: 2);
        heights.Refresh(told.Add);

        Assert.Equal([1], told);
        Assert.Equal([1, 1], ground.Generations);
    }

    [Fact]
    public void AZoneIsReadyOnceKeptOrOnceTheGameCanGenerateItWithoutWaiting()
    {
        var ground = new Ground();
        ground.Pending.Add(2);
        var heights = new ZoneHeights<int, (int, int)>(ground, Vertices);
        heights.Heights(1);

        // Kept: ready without asking. Not generated yet: asked for, and not generated by asking.
        Assert.True(heights.Ready(1));
        Assert.False(heights.Ready(2));
        Assert.Equal([2], ground.Asked);
        Assert.Equal([1], ground.Generations);

        ground.Pending.Clear();
        Assert.True(heights.Ready(2));
        Assert.Equal(20f, heights.Heights(2)[0]);

        // Let go, a zone is asked for again.
        heights.Keep([1]);
        ground.Pending.Add(2);
        Assert.False(heights.Ready(2));
    }

    // Zones whose generated ground is flat at ten times their number, with changes by zone.
    private sealed class Ground : IZoneGround<int, (int, int)>
    {
        public Dictionary<int, ((int, int) Stamp, byte[] Data)> Changes { get; } = [];

        public List<int> Generations { get; } = [];

        public List<string> Warnings { get; } = [];

        // How many generated heights the game gives, and the first few of them in place of the zone's own.
        public int Count { get; set; } = Vertices;

        public float[] Broken { get; set; } = [];

        // The zone's change: this vertex raised this much, at this revision of its object (the zone's number).
        public void Raise(int zone, int vertex, float metres, int revision) =>
            Changes[zone] = ((zone, revision), TerrainChangesTests.Changes(new Dictionary<int, (float, float)> { [vertex] = (metres, 0f) }));

        // Zones the game has not generated yet, and those asked whether it has.
        public HashSet<int> Pending { get; } = [];

        public List<int> Asked { get; } = [];

        public long Key(int zone) => zone;

        public bool CanGenerate(int zone)
        {
            Asked.Add(zone);
            return !Pending.Contains(zone);
        }

        public float[] Generated(int zone)
        {
            Generations.Add(zone);
            float[] heights = [.. Enumerable.Repeat(zone * 10f, Count)];
            Broken.CopyTo(heights, 0);
            return heights;
        }

        ((int, int) Stamp, byte[] Data) IZoneGround<int, (int, int)>.Changes(int zone) =>
            Changes.TryGetValue(zone, out ((int, int) Stamp, byte[] Data) changes) ? changes : (default, null);

        public void Warn(int zone, string problem) => Warnings.Add($"{zone}: {problem}");
    }
}
