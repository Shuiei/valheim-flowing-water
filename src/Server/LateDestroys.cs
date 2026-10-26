using System;
using System.Reflection;

namespace FlowingWater.Server;

/// <summary>
/// The objects the game was asked to destroy made gone now (it destroys them only once it sends them out, once a frame:
/// a save taken in between keeps them), by the game's own sending found by name. When it is not found, or is not of
/// the shape expected (the game changed), nothing is done and it is said once, the water going on as before.
/// </summary>
internal sealed class LateDestroys
{
    private readonly Action _sendOut;

    private readonly string _missing;

    private readonly Action<string> _logError;

    private bool _told;

    /// <summary>
    /// Sends out with <paramref name="sendOut"/>; when that is null, tells <paramref name="missing"/> (why) once through
    /// <paramref name="logError"/>.
    /// </summary>
    public LateDestroys(Action sendOut, string missing, Action<string> logError)
    {
        _sendOut = sendOut;
        _missing = missing;
        _logError = logError;
    }

    /// <summary>
    /// A method of <paramref name="type"/> by its name (of any access, instance or static) as a delegate of this shape
    /// (an instance method's object first); null when there is none, more than one, or it is of another shape, with why
    /// in <paramref name="missing"/>. Never throws for any of those: a game update must not stop the water.
    /// </summary>
    public static TDelegate Find<TDelegate>(Type type, string name, out string missing)
        where TDelegate : Delegate
    {
        MethodInfo method;
        try
        {
            method = type.GetMethod(name, BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        }
        catch (AmbiguousMatchException)
        {
            missing = $"the game's {type.Name}.{name} is found more than once (the game changed?)";
            return null;
        }

        if (method == null)
        {
            missing = $"the game's {type.Name}.{name} was not found (the game changed?)";
            return null;
        }

        var found = (TDelegate)Delegate.CreateDelegate(typeof(TDelegate), method, throwOnBindFailure: false);
        missing = found == null ? $"the game's {type.Name}.{name} takes or gives other things than it did (the game changed?)" : null;
        return found;
    }

    /// <summary>The objects destroyed so far gone now, if the game's sending was found; else said once.</summary>
    public void Flush()
    {
        if (_sendOut != null)
        {
            _sendOut();
        }
        else if (!_told)
        {
            _told = true;
            _logError($"Water: {_missing}; bricks' objects removed just before a save may be saved with the world and read back at the next start");
        }
    }
}
