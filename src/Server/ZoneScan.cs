using System;
using System.Collections.Generic;

namespace FlowingWater.Server;

/// <summary>
/// Looks over zones a part at a time, so that a zone with many objects does not stall a frame: a zone's objects are
/// found (a part), then read a few at a time (a part each), then the zone is done. A new round starts only once the
/// last is over.
/// </summary>
/// <typeparam name="TZone">A zone.</typeparam>
/// <typeparam name="TObject">An object in it.</typeparam>
internal sealed class ZoneScan<TZone, TObject>
{
    private readonly Action<TZone, List<TObject>> _find;

    private readonly Action<TObject> _read;

    private readonly Action<TZone> _done;

    private readonly int _perPart;

    private readonly Queue<TZone> _zones = new();

    // The objects of the zone being looked at, as they were when found, and how many are read (-1: none found yet).
    private readonly List<TObject> _found = [];

    private int _at = -1;

    private TZone _zone;

    /// <summary>A scan that finds a zone's objects into a list, reads each, and is told when a zone is done.</summary>
    /// <param name="find">Adds a zone's objects to the list given.</param>
    /// <param name="read">Reads one object (it may have gone since it was found: the reader checks).</param>
    /// <param name="done">A zone's objects are all read.</param>
    /// <param name="perPart">Objects read in a part.</param>
    public ZoneScan(Action<TZone, List<TObject>> find, Action<TObject> read, Action<TZone> done, int perPart)
    {
        _find = find;
        _read = read;
        _done = done;
        _perPart = perPart;
    }

    /// <summary>Whether a round is under way (zones left, or one being looked at).</summary>
    public bool Busy => _at >= 0 || _zones.Count > 0;

    /// <summary>Starts a round over these zones, unless one is under way.</summary>
    /// <returns>Whether it started.</returns>
    public bool Start(IEnumerable<TZone> zones)
    {
        if (Busy)
        {
            return false;
        }

        foreach (TZone zone in zones)
        {
            _zones.Enqueue(zone);
        }

        return true;
    }

    /// <summary>Takes parts while the round goes on and <paramref name="timeLeft"/> says so (one part at least).</summary>
    public void Run(Func<bool> timeLeft)
    {
        do
        {
            Part();
        }
        while (Busy && timeLeft());
    }

    /// <summary>One part: the next zone's objects found, or the next of them read, or the zone done.</summary>
    public void Part()
    {
        if (_at < 0)
        {
            if (_zones.Count == 0)
            {
                return;
            }

            _zone = _zones.Dequeue();
            _found.Clear();
            _find(_zone, _found);
            _at = 0;
            return;
        }

        int end = Math.Min(_found.Count, _at + _perPart);
        for (; _at < end; _at++)
        {
            _read(_found[_at]);
        }

        if (_at == _found.Count)
        {
            _at = -1;
            _done(_zone);
        }
    }
}
