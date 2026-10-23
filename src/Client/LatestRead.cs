using System;
using System.Threading.Tasks;

namespace FlowingWater.Client;

/// <summary>
/// Data that keeps changing, read away from the game's frame (<see cref="BackgroundBuild{T}"/>), only its newest kept:
/// each revision is read once, one read at a time. A revision that comes while one is being read waits for that read,
/// whose result is then dropped and the newest read next, so an older result never comes after a newer one. A failure
/// is always given, whichever revision it read.
/// </summary>
/// <typeparam name="T">What the data is read into.</typeparam>
internal sealed class LatestRead<T>
{
    private readonly BackgroundBuild<T> _read = new();

    private uint _newest;

    // The revision being read, or read last; none before the first.
    private uint? _started;

    /// <summary>Whether a read was started and not taken yet.</summary>
    public bool Busy => _read.Busy;

    /// <summary>Whether a read is done and waits to be taken.</summary>
    public bool Done => _read.Done;

    /// <summary>Notes the data's revision now.</summary>
    /// <returns>Whether to read it now: it was not read, and no read is under way or waiting to be taken.</returns>
    public bool Wants(uint revision)
    {
        _newest = revision;
        return !_read.Busy && _started != revision;
    }

    /// <summary>Reads the newest revision noted on another thread.</summary>
    /// <exception cref="InvalidOperationException">A read is under way or not taken yet.</exception>
    public void Start(Func<T> read)
    {
        if (!_read.TryStart(read))
        {
            throw new InvalidOperationException("a read is under way or not taken yet");
        }

        _started = _newest;
    }

    /// <summary>Takes the read once done: its result if its revision is still the newest, or the exception that stopped it.</summary>
    /// <returns>False while it runs, when none was started, and when a newer revision came meanwhile (the result is dropped).</returns>
    public bool TryTake(out T result, out Exception failure)
    {
        if (!_read.TryTake(out result, out failure))
        {
            return false;
        }

        if (failure == null && _started != _newest)
        {
            result = default;
            return false;
        }

        return true;
    }

    /// <summary>Leaves the read under way behind: its result is dropped, a failure told to <paramref name="failed"/>.</summary>
    public void Abandon(Action<Exception> failed, TaskScheduler scheduler) => _read.Abandon(failed, scheduler);
}
