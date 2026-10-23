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
