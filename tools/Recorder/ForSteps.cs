using System;
using System.Collections;

namespace FlowingWater.Recorder;

/// <summary>A setting changed for some of a run's steps only.</summary>
internal static class ForSteps
{
    /// <summary>
    /// The steps, run with the setting (read by <paramref name="get"/>, written by <paramref name="set"/>) at
    /// <paramref name="value"/>, and put back to what it was after them: when they end, when one throws, and when the
    /// run is stopped (the steps disposed).
    /// </summary>
    public static IEnumerator Set<T>(Func<T> get, Action<T> set, T value, IEnumerator steps)
    {
        T was = get();
        set(value);
        try
        {
            while (steps.MoveNext())
            {
                yield return steps.Current;
            }
        }
        finally
        {
            set(was);
        }
    }
}
