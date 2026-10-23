using System;
using System.Collections.Generic;

namespace FlowingWater.Server;

/// <summary>
/// Finds every object of a few prefabs over several frames, a part at a time (the game's world-wide search walks a
/// few hundred sectors a call), one prefab after the other. What a pass found is handed over only once it is whole:
/// until then the last pass's stands.
/// </summary>
/// <typeparam name="TObject">An object.</typeparam>
internal sealed class PrefabPass<TObject>
{
    private readonly string[] _prefabs;

    private readonly FindPart _find;

    private readonly List<TObject>[] _found;

    // The prefab being looked for (-1: no pass under way), and where its search is.
    private int _prefab = -1;

    private int _index;

    /// <summary>A pass over these prefabs with this finder.</summary>
    /// <param name="prefabs">The prefabs looked for, in turn.</param>
    /// <param name="find">One part of a prefab's search: adds what it finds, moves the index on, true once done.</param>
    public PrefabPass(string[] prefabs, FindPart find)
    {
        _prefabs = prefabs;
        _find = find;
        _found = new List<TObject>[prefabs.Length];
        for (int i = 0; i < prefabs.Length; i++)
        {
            _found[i] = [];
        }
    }

    /// <summary>One part of a prefab's search, as the game's GetAllZDOsWithPrefabIterative.</summary>
    /// <returns>Whether the search is done.</returns>
    public delegate bool FindPart(string prefab, List<TObject> found, ref int index);

    /// <summary>Whether a pass is under way.</summary>
    public bool Busy => _prefab >= 0;

    /// <summary>Starts a pass, unless one is under way.</summary>
    /// <returns>Whether it started.</returns>
    public bool Start()
    {
        if (Busy)
        {
            return false;
        }

        foreach (List<TObject> found in _found)
        {
            found.Clear();
        }

        _prefab = 0;
        _index = 0;
        return true;
    }

    /// <summary>Takes parts while the pass goes on and <paramref name="timeLeft"/> says so (one part at least).</summary>
    /// <returns>Whether the pass ended now: <see cref="Found"/> then holds all it found.</returns>
    public bool Run(Func<bool> timeLeft)
    {
        if (!Busy)
        {
            return false;
        }

        do
        {
            if (_find(_prefabs[_prefab], _found[_prefab], ref _index))
            {
                _index = 0;
                if (++_prefab == _prefabs.Length)
                {
                    _prefab = -1;
                    return true;
                }
            }
        }
        while (timeLeft());

        return false;
    }

    /// <summary>
    /// The objects of a prefab the pass found, once it ended and until the next starts. Some may have gone while it
    /// went on, and the game may have used a gone one again for another object: the reader checks.
    /// </summary>
    public IReadOnlyList<TObject> Found(string prefab) => _found[Array.IndexOf(_prefabs, prefab)];
}
