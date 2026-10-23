using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// Who may use the flowingwater command: the host and admins always; other players as the server says, refused until
/// it has said; what it says reaching every player's game that has joined, when they join and when it changes.
/// </summary>
public class CommandRightsTests
{
    [Fact]
    public void APlayerIsRefusedUntilTheServerHasSaid()
    {
        var rights = new CommandRights();

        Assert.Null(rights.ServerSaysEveryone);
        Assert.Contains("has not said", rights.Refusal(adminOrHost: false));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(false)]
    [InlineData(true)]
    public void TheHostAndAdminsAlwaysMay(bool? everyone)
    {
        var rights = new CommandRights();
        if (everyone is { } said)
        {
            rights.Told(said);
        }

        Assert.Null(rights.Refusal(adminOrHost: true));
    }

    [Fact]
    public void APlayerMayWhileTheServerSaysEveryoneMay()
    {
        var rights = new CommandRights();
        rights.Told(true);

        Assert.Null(rights.Refusal(adminOrHost: false));
    }

    [Fact]
    public void APlayerIsRefusedWhenTheServerSaysOnlyAdmins()
    {
        var rights = new CommandRights();
        rights.Told(false);

        Assert.Equal("flowingwater: only the server's admins may use it (CommandsForEveryone is off on the server)", rights.Refusal(adminOrHost: false));
    }

    [Fact]
    public void JoiningAnotherServerForgetsWhatTheLastOneSaid()
    {
        var rights = new CommandRights();
        rights.Told(true);

        rights.Joining();

        Assert.Null(rights.ServerSaysEveryone);
        Assert.NotNull(rights.Refusal(adminOrHost: false));
    }

    [Theory]
    [InlineData(true, null, (int)CommandRights.MarkerUse.Place)]
    [InlineData(false, false, (int)CommandRights.MarkerUse.Change)]
    [InlineData(false, true, (int)CommandRights.MarkerUse.Remove)]
    public void TheHostAndAdminsMayAlwaysPlaceChangeAndRemoveWaterWhereTheModIs(bool isServer, bool? everyone, int use)
    {
        var rights = new CommandRights();
        if (everyone is { } said)
        {
            rights.Told(said);
        }

        Assert.Null(rights.MarkerRefusal(isServer, adminOrHost: true, (CommandRights.MarkerUse)use));
    }

    [Theory]
    [InlineData((int)CommandRights.MarkerUse.Place)]
    [InlineData((int)CommandRights.MarkerUse.Change)]
    [InlineData((int)CommandRights.MarkerUse.Remove)]
    public void APlayerMayUseTheWaterWhileTheServerSaysEveryoneMay(int use)
    {
        var rights = new CommandRights();
        rights.Told(true);

        Assert.Null(rights.MarkerRefusal(isServer: false, adminOrHost: false, (CommandRights.MarkerUse)use));
    }

    [Theory]
    [InlineData((int)CommandRights.MarkerUse.Place, "Only the server's admins may place sources and drains (CommandsForEveryone is off on the server)")]
    [InlineData((int)CommandRights.MarkerUse.Change, "Only the server's admins may change sources and drains (CommandsForEveryone is off on the server)")]
    [InlineData((int)CommandRights.MarkerUse.Remove, "Only the server's admins may remove sources and drains (CommandsForEveryone is off on the server)")]
    public void APlayerIsRefusedTheWaterWhenTheServerSaysOnlyAdmins(int use, string refusal)
    {
        var rights = new CommandRights();
        rights.Told(false);

        Assert.Equal(refusal, rights.MarkerRefusal(isServer: false, adminOrHost: false, (CommandRights.MarkerUse)use));
    }

    // A server that said nothing has no FlowingWater: a source or drain would do nothing there, whoever placed it.
    [Theory]
    [InlineData(false, (int)CommandRights.MarkerUse.Place, "FlowingWater is not on this server: no one may place sources and drains here")]
    [InlineData(true, (int)CommandRights.MarkerUse.Change, "FlowingWater is not on this server: no one may change sources and drains here")]
    [InlineData(true, (int)CommandRights.MarkerUse.Remove, "FlowingWater is not on this server: no one may remove sources and drains here")]
    public void NoOneMayUseTheWaterOnAServerWithoutTheMod(bool adminOrHost, int use, string refusal)
    {
        var rights = new CommandRights();

        Assert.Equal(refusal, rights.MarkerRefusal(isServer: false, adminOrHost, (CommandRights.MarkerUse)use));
    }

    [Theory]
    [InlineData(true, null, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    [InlineData(false, null, false)]
    public void TheModIsHereAloneHostingOrOnAServerThatSaidSo(bool isServer, bool? everyone, bool here)
    {
        var rights = new CommandRights();
        if (everyone is { } said)
        {
            rights.Told(said);
        }

        Assert.Equal(here, rights.ModHere(isServer));
    }

    [Fact]
    public void JoiningAnotherServerForgetsThatTheLastOneHadTheMod()
    {
        var rights = new CommandRights();
        rights.Told(true);
        rights.Joining();

        Assert.False(rights.ModHere(isServer: false));
    }

    // Where the mod is, the hoe and the command go together: whoever may use one may use the other.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void WhereTheModIsTheWaterAndTheCommandAreRefusedToTheSamePlayers(bool everyone, bool adminOrHost)
    {
        var rights = new CommandRights();
        rights.Told(everyone);

        Assert.Equal(rights.Refusal(adminOrHost) == null, rights.MarkerRefusal(isServer: false, adminOrHost, CommandRights.MarkerUse.Place) == null);
    }

    [Fact]
    public void AChangeReachesEveryPlayerWhoHasJoinedOnce()
    {
        bool everyone = true;
        var players = new List<Player> { new() { Ready = true }, new(), new() { Ready = true } };
        void Tell() => CommandRights.Tell(players, p => p.Ready, p => p.Told(everyone));

        Tell();
        Assert.All(players.Where(p => p.Ready), p => Assert.Null(p.Rights.Refusal(adminOrHost: false)));

        everyone = false;
        Tell();

        Assert.All(players.Where(p => p.Ready), p => Assert.NotNull(p.Rights.Refusal(adminOrHost: false)));
        Assert.Equal([2, 0, 2], players.Select(p => p.TimesTold));
        Assert.Null(players[1].Rights.ServerSaysEveryone);
    }

    [Fact]
    public void APlayerJoiningIsToldOnlyOnceReady()
    {
        var joining = new Player();
        CommandRights.Tell([joining], p => p.Ready, p => p.Told(true));
        Assert.Equal(0, joining.TimesTold);

        joining.Ready = true;
        CommandRights.Tell([joining], p => p.Ready, p => p.Told(true));

        Assert.Equal(1, joining.TimesTold);
        Assert.True(joining.Rights.ServerSaysEveryone);
    }

    // A player's game as the server sees it: whether it has joined, and what it was told.
    private sealed class Player
    {
        public bool Ready { get; set; }

        public CommandRights Rights { get; } = new();

        public int TimesTold { get; private set; }

        public void Told(bool everyone)
        {
            TimesTold++;
            Rights.Told(everyone);
        }
    }
}
