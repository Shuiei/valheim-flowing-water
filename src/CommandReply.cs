using System;

namespace FlowingWater;

/// <summary>
/// What a flowingwater command says on the console, line by line; with General.Debug on, each line is written to
/// BepInEx/LogOutput.log too (with the command asked), so a session's commands and their answers can be read back.
/// Reports long enough to want reading back there, and a command's failure, are logged whatever Debug says.
/// </summary>
internal sealed class CommandReply
{
    private readonly Action<string> _console;

    private readonly Action<string> _info;

    private readonly Action<string> _error;

    private readonly Func<bool> _debug;

    /// <summary>
    /// A reply putting its lines on a console, logging them as information or an error, and Debug as it is now (read
    /// at each line).
    /// </summary>
    public CommandReply(Action<string> console, Action<string> info, Action<string> error, Func<bool> debug)
    {
        _console = console;
        _info = info;
        _error = error;
        _debug = debug;
    }

    /// <summary>The command asked (its words after flowingwater), in the log with Debug on.</summary>
    public void Asked(string args) => Logged($"flowingwater {args}".TrimEnd() + " (asked)");

    /// <summary>A line in the log with Debug on, on no console.</summary>
    public void Logged(string line)
    {
        if (_debug())
        {
            _info(line);
        }
    }

    /// <summary>A line on the console (and in the log with Debug on).</summary>
    public void Say(string line)
    {
        _console(line);
        Logged(line);
    }

    /// <summary>A line on the console and always in the log.</summary>
    public void SayLogged(string line)
    {
        _console(line);
        _info(line);
    }

    /// <summary>
    /// The command (its words after flowingwater) threw: the exception logged as an error, and its message said on the
    /// console as any line is (so in the log with Debug on).
    /// </summary>
    public void Failed(string args, Exception e)
    {
        _error($"flowingwater {args}: {e}");
        Say($"flowingwater: failed, {e.GetType().Name}: {e.Message} (see BepInEx/LogOutput.log)");
    }
}
