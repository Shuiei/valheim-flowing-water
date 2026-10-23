using System;
using System.Collections.Generic;

namespace FlowingWater.Server;

/// <summary>
/// The points whose water found no room, each told of once, not at every pour: a point is told of again only after it
/// had room, or after no source poured there any more.
/// </summary>
/// <typeparam name="TPoint">A point water is poured at.</typeparam>
internal sealed class NoRoom<TPoint>
{
    private readonly Action<string> _warn;

    private readonly HashSet<TPoint> _points = [];

    /// <summary>The points with no room, told of with this warning.</summary>
    public NoRoom(Action<string> warn)
    {
        _warn = warn;
    }

    /// <summary>Water poured at a point found no room: solid all the way up, or past the grid's reach.</summary>
    public void Add(TPoint point)
    {
        if (_points.Add(point))
        {
            _warn(FormattableString.Invariant($"Water: no room over {point} (solid all the way up, or past the grid's reach); its water is dropped"));
        }
    }

    /// <summary>Water poured at a point found room.</summary>
    public void Room(TPoint point)
    {
        if (_points.Count > 0)
        {
            _points.Remove(point);
        }
    }

    /// <summary>After a look for sources: the points no source pours at any more are forgotten.</summary>
    public void Pouring(Predicate<TPoint> poured) => _points.RemoveWhere(point => !poured(point));
}
