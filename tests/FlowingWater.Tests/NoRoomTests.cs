using System.Collections.Generic;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// Points whose water found no room are told of once across the pours; a point again only after it had room or after
/// no source poured there any more.
/// </summary>
public class NoRoomTests
{
    [Fact]
    public void APointWithNoRoomIsToldOfOnceUntilItHasRoomAgain()
    {
        var told = new List<string>();
        var noRoom = new NoRoom<(int, int)>(told.Add);

        noRoom.Add((1, 2));
        noRoom.Add((1, 2));
        noRoom.Room((3, 4));
        noRoom.Add((1, 2));
        Assert.Single(told);

        noRoom.Room((1, 2));
        noRoom.Add((1, 2));

        Assert.Equal(2, told.Count);
        Assert.Equal("Water: no room over (1, 2) (solid all the way up, or past the grid's reach); its water is dropped", told[0]);
    }

    [Fact]
    public void APointNoSourcePoursAtAnyMoreIsForgottenAfterALook()
    {
        var told = new List<string>();
        var noRoom = new NoRoom<(int, int)>(told.Add);
        noRoom.Add((1, 2));
        noRoom.Add((5, 6));

        // The source at (5, 6) is still there, the one at (1, 2) was removed (and then put back).
        noRoom.Pouring(point => point == (5, 6));
        noRoom.Add((5, 6));
        noRoom.Add((1, 2));

        Assert.Equal(3, told.Count);
        Assert.StartsWith("Water: no room over (1, 2)", told[2]);
    }
}
