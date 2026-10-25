using System;
using System.Collections.Generic;
using System.Reflection;

namespace FlowingWater;

/// <summary>
/// The game's methods a patch for the sources and drains changes, looked up when Harmony prepares the patch: those not
/// found (a game update changed them, or gave one a twin of the same name, which a look-up by name alone cannot tell
/// apart) are named in one error and left out, the others still patched, and a patch with none found is not applied.
/// Harmony would otherwise throw at it, and every patch of the plugin after it, the water's among them, would be left
/// out too. Without the game (each method is looked up by a function given), so that it is tested.
/// </summary>
internal sealed class PatchTargets
{
    private readonly string _patch;

    private readonly Action<string> _logError;

    private readonly (string Name, Func<MethodBase> Find)[] _methods;

    private MethodBase[] _found = [];

    /// <summary>The methods a patch changes.</summary>
    /// <param name="patch">The patch, for the error.</param>
    /// <param name="logError">Where the methods not found are logged.</param>
    /// <param name="methods">Each method's name, for the error, and its look-up: null when it is not found.</param>
    public PatchTargets(string patch, Action<string> logError, params (string Name, Func<MethodBase> Find)[] methods)
    {
        _patch = patch;
        _logError = logError;
        _methods = methods;
    }

    /// <summary>The methods found, for the patch's TargetMethods.</summary>
    public IEnumerable<MethodBase> Found => _found;

    /// <summary>
    /// For the patch's Prepare: Harmony calls it once before the patch (no original), then once for each method found.
    /// </summary>
    /// <returns>Whether to apply the patch: one of its methods at least was found.</returns>
    public bool Prepare(MethodBase original)
    {
        if (original != null)
        {
            return true;
        }

        var found = new List<MethodBase>();
        var missing = new List<string>();
        foreach ((string name, Func<MethodBase> find) in _methods)
        {
            MethodBase method;
            try
            {
                method = find();
            }
            catch (AmbiguousMatchException)
            {
                // More than one method of the name now: as good as none, told of as such.
                method = null;
            }

            if (method != null)
            {
                found.Add(method);
            }
            else
            {
                missing.Add(name);
            }
        }

        _found = [.. found];
        if (missing.Count > 0)
        {
            string what = missing.Count == 1 ? $"The game's {missing[0]} was" : $"The game's {string.Join(", ", missing)} were";
            _logError(_found.Length > 0
                ? $"{what} not found (a game update?): {_patch} leaves {(missing.Count == 1 ? "it" : "them")} alone"
                : $"{what} not found (a game update?): {_patch} is left out");
        }

        return _found.Length > 0;
    }
}
