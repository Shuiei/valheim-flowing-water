using System;
using System.Collections.Generic;

namespace FlowingWater.Server;

/// <summary>
/// The sources and drains left out (a fixed one not written as it should be, a rate that is no use) and the points
/// whose water found no room, each told of once, not at every look for markers or every pour: a point is told of
/// again only after it had room, or after no source poured there any more.
/// </summary>
/// <typeparam name="TPoint">A point water is poured at.</typeparam>
internal sealed class LeftOut<TPoint>
{
    private readonly Action<string> _warn;

    // The markers (by object) and fixed sources and drains (by their text) told of: they are looked for again at each
    // look, as they were.
    private readonly HashSet<string> _ignored = [];

    private readonly HashSet<TPoint> _noRoom = [];

    /// <summary>What is left out, told of with this warning.</summary>
    public LeftOut(Action<string> warn)
    {
        _warn = warn;
    }

    /// <summary>A fixed source or drain whose text is not x,z,rate (a drain's may add a radius).</summary>
    public void BadText(string text, bool drain)
    {
        if (_ignored.Add(text))
        {
            _warn($"Fixed {(drain ? "drain" : "source")} \"{text}\" is not {(drain ? "x,z,rate or x,z,rate,radius" : "x,z,rate")}; ignored");
        }
    }

    /// <summary>A source or drain (known by <paramref name="key"/>: its object, or its text) whose rate is no use.</summary>
    public void BadRate(string key, string where, float rate, bool drain)
    {
        if (_ignored.Add(key))
        {
            _warn(FormattableString.Invariant($"{(drain ? "Drain" : "Source")} at {where}: rate {rate} is not usable; ignored"));
        }
    }

    /// <summary>Water poured at a point found no room: solid all the way up, or past the grid's reach.</summary>
    public void NoRoom(TPoint point)
    {
        if (_noRoom.Add(point))
        {
            _warn(FormattableString.Invariant($"Water: no room over {point} (solid all the way up, or past the grid's reach); its water is dropped"));
        }
    }

    /// <summary>Water poured at a point found room.</summary>
    public void Room(TPoint point)
    {
        if (_noRoom.Count > 0)
        {
            _noRoom.Remove(point);
        }
    }

    /// <summary>After a look for sources: the points no source pours at any more are forgotten.</summary>
    public void Pouring(Predicate<TPoint> poured) => _noRoom.RemoveWhere(point => !poured(point));
}
