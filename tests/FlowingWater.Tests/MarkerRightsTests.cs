using System;
using System.Collections.Generic;
using System.Linq;
using FlowingWater.Server;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// Which placed sources and drains the server simulates: everyone's while CommandsForEveryone is on; else those placed
/// by a player who may (checked while they are on the server), kept allowed in the server's file; the others wait or
/// are refused, ignored and told of once.
/// </summary>
public class MarkerRightsTests
{
    private readonly List<string> _warnings = [];

    private readonly MarkerRights _rights;

    public MarkerRightsTests() => _rights = new MarkerRights(_warnings.Add);

    [Fact]
    public void WithEveryoneAllowedEveryMarkerIsAllowedAndKept()
    {
        Assert.Equal(MarkerRights.Verdict.Allowed, _rights.Decide("1:5", true, 0L, null, () => "Source at (1, 2, 3)"));

        Assert.True(_rights.Changed);
        Assert.Equal(["1:5"], _rights.Lines);
        Assert.False(_rights.Changed);
        Assert.Empty(_warnings);
    }

    [Fact]
    public void AMarkerPlacedByAPlayerWhoMayIsAllowedAndStaysAllowedOnceTheyAreGone()
    {
        Assert.Equal(MarkerRights.Verdict.Allowed, _rights.Decide("1:5", false, 42L, true, () => "Source at (1, 2, 3)"));

        Assert.Equal(MarkerRights.Verdict.Allowed, _rights.Decide("1:5", false, 42L, null, () => "Source at (1, 2, 3)"));
        Assert.Empty(_warnings);
    }

    [Fact]
    public void AMarkerWhosePlacerIsNotOnTheServerWaitsAndIsToldOfOnce()
    {
        Assert.Equal(MarkerRights.Verdict.Waiting, _rights.Decide("1:5", false, 42L, null, () => "Drain at (1, 2, 3)"));
        Assert.Equal(MarkerRights.Verdict.Waiting, _rights.Decide("1:5", false, 42L, null, () => "Drain at (1, 2, 3)"));

        Assert.False(_rights.Changed);
        string warning = Assert.Single(_warnings);
        Assert.StartsWith("Drain at (1, 2, 3): its placer is not on the server", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void AMarkerPlacedByAPlayerWhoMayNotIsRefusedAndToldOfOnce()
    {
        Assert.Equal(MarkerRights.Verdict.Refused, _rights.Decide("1:5", false, 42L, false, () => "Source at (1, 2, 3)"));
        Assert.Equal(MarkerRights.Verdict.Refused, _rights.Decide("1:5", false, 42L, false, () => "Source at (1, 2, 3)"));

        string warning = Assert.Single(_warnings);
        Assert.Contains("may not place one", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void AMarkerWithNoPlacerWrittenWaitsWithAWordOnHowToAllowIt()
    {
        Assert.Equal(MarkerRights.Verdict.Waiting, _rights.Decide("1:5", false, 0L, null, () => "Source at (1, 2, 3)"));

        string warning = Assert.Single(_warnings);
        Assert.Contains("older version's console", warning, StringComparison.Ordinal);
        Assert.Contains("turn it on for a moment", warning, StringComparison.Ordinal);
    }

    [Fact]
    public void TheMarkersAllowedBeforeAreReadFromTheFileAndThoseGoneForgotten()
    {
        _rights.Read([" 1:5 ", "", "1:7"]);

        Assert.Equal(MarkerRights.Verdict.Allowed, _rights.Decide("1:5", false, 42L, null, () => "Source"));
        Assert.False(_rights.Changed);

        _rights.Keep(["1:5"]);

        Assert.True(_rights.Changed);
        Assert.Equal(["1:5"], _rights.Lines.OrderBy(l => l, StringComparer.Ordinal));
    }

    [Fact]
    public void AMarkerAllowedAfterWaitingIsToldOfAgainShouldItWaitAgain()
    {
        _rights.Decide("1:5", false, 42L, null, () => "Source");
        _rights.Decide("1:5", false, 42L, true, () => "Source");
        _rights.Keep([]);

        _rights.Decide("1:5", false, 42L, null, () => "Source");

        Assert.Equal(2, _warnings.Count);
    }
}
