using System.Collections.Generic;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// Sources and drains left out, and points whose water found no room, are told of once across the looks for
/// markers and the pours, each by what it is (a marker's object, a fixed one's text, a point); a point again only
/// after it had room or after no source poured there any more.
/// </summary>
public class LeftOutTests
{
    [Fact]
    public void AMarkerWithARateOfNoUseIsToldOfOnceAcrossLooks()
    {
        var told = new List<string>();
        var leftOut = new LeftOut<(int, int)>(told.Add);

        for (int look = 0; look < 3; look++)
        {
            leftOut.BadRate("marker 1", "(1, 2, 3)", float.NaN, drain: false);
            leftOut.BadRate("marker 2", "(4, 5, 6)", -1.5f, drain: true);
        }

        Assert.Equal(["Source at (1, 2, 3): rate NaN is not usable; ignored", "Drain at (4, 5, 6): rate -1.5 is not usable; ignored"], told);
    }

    [Fact]
    public void AFixedEntryBadlyWrittenIsToldOfOnceAcrossLooksAndOnlyOnce()
    {
        var told = new List<string>();
        var leftOut = new LeftOut<(int, int)>(told.Add);

        for (int look = 0; look < 3; look++)
        {
            leftOut.BadText("1,2", drain: false);
            leftOut.BadText("1,2,x,4", drain: true);

            // Read well but with a rate of no use: the same text is the same entry, already told of.
            leftOut.BadRate("1,2", "1,2", 0f, drain: false);
        }

        Assert.Equal(["Fixed source \"1,2\" is not x,z,rate; ignored", "Fixed drain \"1,2,x,4\" is not x,z,rate or x,z,rate,radius; ignored"], told);
    }

    [Fact]
    public void APointWithNoRoomIsToldOfOnceUntilItHasRoomAgain()
    {
        var told = new List<string>();
        var leftOut = new LeftOut<(int, int)>(told.Add);

        leftOut.NoRoom((1, 2));
        leftOut.NoRoom((1, 2));
        leftOut.Room((3, 4));
        leftOut.NoRoom((1, 2));
        Assert.Single(told);

        leftOut.Room((1, 2));
        leftOut.NoRoom((1, 2));

        Assert.Equal(2, told.Count);
        Assert.Equal("Water: no room over (1, 2) (solid all the way up, or past the grid's reach); its water is dropped", told[0]);
    }

    [Fact]
    public void APointNoSourcePoursAtAnyMoreIsForgottenAfterALook()
    {
        var told = new List<string>();
        var leftOut = new LeftOut<(int, int)>(told.Add);
        leftOut.NoRoom((1, 2));
        leftOut.NoRoom((5, 6));

        // The source at (5, 6) is still there, the one at (1, 2) was removed (and then put back).
        leftOut.Pouring(point => point == (5, 6));
        leftOut.NoRoom((5, 6));
        leftOut.NoRoom((1, 2));

        Assert.Equal(3, told.Count);
        Assert.StartsWith("Water: no room over (1, 2)", told[2]);
    }
}
