using System.Collections.Generic;
using System.Linq;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// A world-wide look for a few prefabs spread over frames: each prefab's search goes on part by part, one prefab after
/// the other, what was found is handed over only once the pass is whole, the time given is respected (one part at
/// least), and a new pass starts only once the last is over, from nothing.
/// </summary>
public class PrefabPassTests
{
    // A world of 10 sectors, searched 3 a part: the objects of a prefab are its name and the sector.
    private static bool Find(string prefab, List<string> found, ref int index)
    {
        int end = System.Math.Min(10, index + 3);
        for (; index < end; index++)
        {
            found.Add($"{prefab}{index}");
        }

        return index == 10;
    }

    [Fact]
    public void APassGoesOnPartByPartPrefabAfterPrefab()
    {
        var pass = new PrefabPass<string>(["source", "drain"], Find);
        Assert.True(pass.Start());

        // 4 parts a prefab (3, 3, 3, 1 sectors): the pass ends with the eighth.
        int parts = 0;
        bool ended = false;
        while (!ended)
        {
            ended = pass.Run(() => false);
            parts++;
            Assert.Equal(!ended, pass.Busy);
        }

        Assert.Equal(8, parts);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"source{i}"), pass.Found("source"));
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"drain{i}"), pass.Found("drain"));
    }

    [Fact]
    public void ARunTakesPartsWhileTimeIsLeftButOneAtLeast()
    {
        int calls = 0;
        var pass = new PrefabPass<string>(["source"], (string prefab, List<string> found, ref int index) =>
        {
            calls++;
            return Find(prefab, found, ref index);
        });
        pass.Start();

        pass.Run(() => false);
        Assert.Equal(1, calls);

        int asked = 0;
        Assert.False(pass.Run(() => ++asked < 2));
        Assert.Equal(3, calls);

        Assert.True(pass.Run(() => true));
        Assert.Equal(4, calls);
        Assert.False(pass.Busy);
    }

    [Fact]
    public void NothingIsSearchedWithNoPassUnderWay()
    {
        int calls = 0;
        var pass = new PrefabPass<string>(["source"], (string prefab, List<string> found, ref int index) =>
        {
            calls++;
            return true;
        });

        Assert.False(pass.Run(() => true));
        Assert.Equal(0, calls);
    }

    [Fact]
    public void ANewPassStartsOnlyOnceTheLastIsOverFromNothing()
    {
        var pass = new PrefabPass<string>(["source"], Find);
        Assert.True(pass.Start());
        pass.Run(() => false);
        Assert.False(pass.Start());
        pass.Run(() => true);

        Assert.True(pass.Start());
        pass.Run(() => true);

        Assert.Equal(10, pass.Found("source").Count);
    }
}
