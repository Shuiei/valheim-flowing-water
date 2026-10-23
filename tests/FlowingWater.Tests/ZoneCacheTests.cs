using System.Collections.Generic;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>What is kept per zone is let go for the zones no brick is near, and kept for the others.</summary>
public class ZoneCacheTests
{
    [Fact]
    public void OnlyTheZonesStillNeededAreKept()
    {
        var byZone = new Dictionary<long, string> { [1] = "a", [2] = "b", [3] = "c" };

        Assert.Equal(2, ZoneCache.Keep(byZone, [2, 4]));
        Assert.Equal(new Dictionary<long, string> { [2] = "b" }, byZone);

        Assert.Equal(0, ZoneCache.Keep(byZone, [2]));
        Assert.Single(byZone);

        Assert.Equal(1, ZoneCache.Keep(byZone, []));
        Assert.Empty(byZone);
    }
}
