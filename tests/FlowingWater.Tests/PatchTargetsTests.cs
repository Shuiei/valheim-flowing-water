using System;
using System.Collections.Generic;
using System.Reflection;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The game's methods a source and drain patch changes, after a game update: those gone are named in one error and left
/// out, the others still patched; with none found the patch is left out; with all found nothing is logged.
/// </summary>
public class PatchTargetsTests
{
    private static readonly MethodBase First = typeof(PatchTargetsTests).GetMethod(nameof(Here), BindingFlags.NonPublic | BindingFlags.Static);

    private static readonly MethodBase Second = typeof(PatchTargetsTests).GetMethod(nameof(AlsoHere), BindingFlags.NonPublic | BindingFlags.Static);

    private readonly List<string> _errors = [];

    [Fact]
    public void WithAllFoundThePatchIsAppliedToThemAndNothingIsLogged()
    {
        PatchTargets targets = Targets(("Player.First", () => First), ("Player.Second", () => Second));

        Assert.True(targets.Prepare(null));

        Assert.Equal([First, Second], targets.Found);
        Assert.Empty(_errors);
    }

    [Fact]
    public void ThoseGoneAreNamedInOneErrorAndOnlyThoseFoundArePatched()
    {
        PatchTargets targets = Targets(("Player.Gone", () => null), ("Player.First", () => First), ("ObjectDB.AlsoGone", () => null));

        Assert.True(targets.Prepare(null));

        Assert.Equal([First], targets.Found);
        string error = Assert.Single(_errors);
        Assert.Contains("Player.Gone, ObjectDB.AlsoGone", error, StringComparison.Ordinal);
        Assert.Contains("TestPatch leaves them alone", error, StringComparison.Ordinal);
        Assert.DoesNotContain("Player.First", error, StringComparison.Ordinal);
    }

    [Fact]
    public void AMethodWithATwinOfItsNameCountsAsNotFound()
    {
        PatchTargets targets = Targets(("Player.Twins", () => throw new AmbiguousMatchException("two")), ("Player.First", () => First));

        Assert.True(targets.Prepare(null));

        Assert.Equal([First], targets.Found);
        string error = Assert.Single(_errors);
        Assert.Contains("Player.Twins was not found", error, StringComparison.Ordinal);
    }

    [Fact]
    public void WithNoneFoundThePatchIsLeftOutWithAnErrorNamingThem()
    {
        PatchTargets targets = Targets(("Player.Gone", () => null), ("Player.AlsoGone", () => null));

        Assert.False(targets.Prepare(null));

        Assert.Empty(targets.Found);
        string error = Assert.Single(_errors);
        Assert.Contains("Player.Gone, Player.AlsoGone", error, StringComparison.Ordinal);
        Assert.Contains("TestPatch is left out", error, StringComparison.Ordinal);
    }

    [Fact]
    public void EachMethodFoundIsPreparedWithoutLookingAgain()
    {
        int looks = 0;
        MethodBase Look()
        {
            looks++;
            return First;
        }

        PatchTargets targets = Targets(("Player.First", Look));
        Assert.True(targets.Prepare(null));

        // Harmony prepares the patch again for each method it patches.
        Assert.True(targets.Prepare(First));

        Assert.Equal(1, looks);
        Assert.Empty(_errors);
    }

    private PatchTargets Targets(params (string Name, Func<MethodBase> Find)[] methods) => new("TestPatch", _errors.Add, methods);

    private static void Here()
    {
    }

    private static void AlsoHere()
    {
    }
}
