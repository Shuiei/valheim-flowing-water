using System.Collections.Generic;
using System.Linq;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// Zones looked over a part at a time: a zone's objects found in one part and read a few per part, an empty zone done
/// at once, the time given respected (one part at least), a new round only once the last is over, and the objects
/// read as they were found though the zone changes meanwhile.
/// </summary>
public class ZoneScanTests
{
    [Fact]
    public void AZoneIsFoundThenReadAFewObjectsAPart()
    {
        var zones = new Dictionary<string, List<int>> { ["big"] = [.. Enumerable.Range(0, 150)], ["empty"] = [] };
        var read = new List<int>();
        var done = new List<string>();
        var scan = new ZoneScan<string, int>((zone, found) => found.AddRange(zones[zone]), read.Add, done.Add, 64);
        scan.Start(["big", "empty"]);

        int parts = 0;
        while (scan.Busy)
        {
            scan.Part();
            parts++;
            if (parts == 1)
            {
                Assert.Empty(read);
            }
            else if (parts == 2)
            {
                Assert.Equal(64, read.Count);
            }
        }

        // big: found, 64, 64, 22 and done; empty: found, then done.
        Assert.Equal(6, parts);
        Assert.Equal(Enumerable.Range(0, 150), read);
        Assert.Equal(["big", "empty"], done);
    }

    [Fact]
    public void ARunTakesPartsWhileTimeIsLeftButOneAtLeast()
    {
        int read = 0;
        var scan = new ZoneScan<int, int>((_, found) => found.AddRange(Enumerable.Range(0, 1000)), _ => read++, _ => { }, 64);
        scan.Start([1]);

        // No time left: one part each run (the zone's objects found, then 64 read).
        scan.Run(() => false);
        Assert.Equal(0, read);
        scan.Run(() => false);
        Assert.Equal(64, read);

        // Time for two more after the first.
        int calls = 0;
        scan.Run(() => ++calls < 3);
        Assert.Equal(64 * 4, read);
        Assert.True(scan.Busy);
    }

    [Fact]
    public void ANewRoundStartsOnlyOnceTheLastIsOver()
    {
        var done = new List<int>();
        var scan = new ZoneScan<int, int>((_, found) => found.Add(0), _ => { }, done.Add, 64);

        Assert.True(scan.Start([1, 2]));
        scan.Part();
        Assert.False(scan.Start([3]));
        while (scan.Busy)
        {
            scan.Part();
        }

        Assert.True(scan.Start([3]));
        scan.Run(() => true);

        Assert.Equal([1, 2, 3], done);
    }

    [Fact]
    public void ObjectsAreReadAsTheyWereFoundThoughTheZoneChanges()
    {
        // Objects gone from the zone between parts (destroyed) are still handed to the reader, which checks them;
        // the next zone is unaffected.
        var world = new Dictionary<int, List<string>> { [1] = [.. Enumerable.Range(0, 100).Select(k => $"a{k}")], [2] = ["b0"] };
        var read = new List<string>();
        var scan = new ZoneScan<int, string>((zone, found) => found.AddRange(world[zone]), read.Add, _ => { }, 64);
        scan.Start([1, 2]);

        scan.Part();
        scan.Part();
        world[1].Clear();
        scan.Run(() => true);

        Assert.Equal(101, read.Count);
        Assert.Equal("b0", read[^1]);
    }
}
