using System;
using System.Collections.Generic;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// What a flowingwater command says (<see cref="CommandReply"/>): each line on the console; with Debug on, the command
/// asked and every line it says (a server's answer among them) in the log too, none of it with Debug off; reports and a
/// command's failure in the log whatever Debug says.
/// </summary>
public class CommandReplyTests
{
    private readonly List<string> _console = [];

    private readonly List<string> _info = [];

    private readonly List<string> _error = [];

    private bool _debug;

    [Fact]
    public void WithDebugOnTheCommandAndEachLineItSaysAreLogged()
    {
        _debug = true;
        CommandReply reply = Reply();

        reply.Asked("drain 2 ");
        reply.Say("flowingwater: drain placed");
        reply.Say("flowingwater: wiped 3 m³ of water and removed 1 sources and drains");

        Assert.Equal(["flowingwater: drain placed", "flowingwater: wiped 3 m³ of water and removed 1 sources and drains"], _console);
        Assert.Equal(["flowingwater drain 2 (asked)", .. _console], _info);
        Assert.Empty(_error);
    }

    [Fact]
    public void WithDebugOffOnlyTheConsoleHearsACommand()
    {
        CommandReply reply = Reply();

        reply.Asked("info");
        reply.Say("flowingwater: no water here");
        reply.Logged("flowingwater: a server's answer, on no console");

        Assert.Equal(["flowingwater: no water here"], _console);
        Assert.Empty(_info);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AReportIsLoggedWhateverDebugSays(bool debug)
    {
        _debug = debug;
        CommandReply reply = Reply();

        reply.SayLogged("flowingwater: column (3, 4) ground 12.5 m");

        Assert.Equal(["flowingwater: column (3, 4) ground 12.5 m"], _console);
        Assert.Equal(_console, _info);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AFailureIsLoggedAsAnErrorAndSaidOnTheConsole(bool debug)
    {
        _debug = debug;
        CommandReply reply = Reply();

        reply.Failed("gpu", new InvalidOperationException("no card"));

        string error = Assert.Single(_error);
        Assert.StartsWith("flowingwater gpu: System.InvalidOperationException: no card", error, StringComparison.Ordinal);
        Assert.Equal(["flowingwater: failed, InvalidOperationException: no card (see BepInEx/LogOutput.log)"], _console);
        Assert.Equal(debug ? _console : [], _info);
    }

    [Fact]
    public void DebugIsReadAtEachLine()
    {
        CommandReply reply = Reply();

        reply.Say("before");
        _debug = true;
        reply.Say("after");

        Assert.Equal(["after"], _info);
    }

    private CommandReply Reply() => new(_console.Add, _info.Add, _error.Add, () => _debug);
}
