using System;

namespace FlowingWater;

/// <summary>
/// A part of the mod run each frame beside the water, kept from stopping it: a fault in it is logged once in full, the
/// part is turned off (put away, so it holds nothing of the game's) and not run again until <see cref="Reset"/> gives it
/// a fresh try, at the next world. Without the game, so that it is tested.
/// </summary>
internal sealed class FaultGate
{
    private readonly string _what;

    private readonly Action<string> _logError;

    private readonly Action _turnOff;

    /// <summary>A part run through the gate.</summary>
    /// <param name="what">What failed, for the log: the exception follows it.</param>
    /// <param name="logError">Where the fault is logged.</param>
    /// <param name="turnOff">Puts the part away after a fault.</param>
    public FaultGate(string what, Action<string> logError, Action turnOff)
    {
        _what = what;
        _logError = logError;
        _turnOff = turnOff;
    }

    /// <summary>Whether the part runs: true until a fault in it, and again after <see cref="Reset"/>.</summary>
    public bool On { get; private set; } = true;

    /// <summary>
    /// The part's frame, while it is on. A fault in it is logged with the exception in full (its type, message and
    /// stack), and the part turned off. A fault in turning it off is not caught: there is nothing left to keep it right.
    /// </summary>
    public void Run(Action part)
    {
        if (!On)
        {
            return;
        }

        try
        {
            part();
        }
        catch (Exception e)
        {
            On = false;
            _logError($"{_what}: {e}");
            _turnOff();
        }
    }

    /// <summary>The part on again, at a new world: its next fault is logged too.</summary>
    public void Reset() => On = true;
}
