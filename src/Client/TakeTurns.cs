using System;
using System.Collections.Generic;

namespace FlowingWater.Client;

/// <summary>
/// Things waiting for a turn, served in the order they came: when a frame's time runs out, the ones not served
/// keep their place and go first next time, so none waits for ever behind others that keep asking.
/// </summary>
/// <typeparam name="T">What waits.</typeparam>
internal sealed class TakeTurns<T>
{
    private readonly Queue<T> _waiting = new();

    private readonly HashSet<T> _queued = [];

    /// <summary>How many wait.</summary>
    public int Count => _waiting.Count;

    /// <summary>Puts this at the back, unless it waits already (then it keeps its place).</summary>
    /// <param name="item">What waits.</param>
    public void Add(T item)
    {
        if (_queued.Add(item))
        {
            _waiting.Enqueue(item);
        }
    }

    /// <summary>
    /// Serves the waiting ones in the order they came, each at most once, while <paramref name="more"/> says time
    /// is left (the first ready one always). Those not ready yet go to the back and wait on.
    /// </summary>
    /// <param name="ready">Whether one may be served now.</param>
    /// <param name="more">Whether there is time for another.</param>
    /// <param name="serve">Serves one; it no longer waits.</param>
    public void Serve(Func<T, bool> ready, Func<bool> more, Action<T> serve)
    {
        bool served = false;
        for (int left = _waiting.Count; left > 0 && (!served || more()); left--)
        {
            T item = _waiting.Dequeue();
            if (!ready(item))
            {
                _waiting.Enqueue(item);
                continue;
            }

            _queued.Remove(item);
            served = true;
            serve(item);
        }
    }
}
