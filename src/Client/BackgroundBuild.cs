using System;
using System.Threading;
using System.Threading.Tasks;

namespace FlowingWater.Client;

/// <summary>
/// Something built away from the game's frame, one at a time: started when none is being built, its result (or its
/// failure) taken once, when done. One left behind (its owner gone) still has its failure told, on the scheduler
/// given, so that the telling is where it may be (the game's frame for its log).
/// </summary>
/// <typeparam name="T">What is built.</typeparam>
internal sealed class BackgroundBuild<T>
{
    private Task<T> _task;

    /// <summary>Whether a build was started and not taken yet.</summary>
    public bool Busy => _task != null;

    /// <summary>Whether a build is done and waits to be taken.</summary>
    public bool Done => _task is { IsCompleted: true };

    /// <summary>Starts a build on another thread, unless one is under way or not taken yet.</summary>
    /// <returns>Whether it started.</returns>
    public bool TryStart(Func<T> build)
    {
        if (_task != null)
        {
            return false;
        }

        _task = Task.Run(build);
        return true;
    }

    /// <summary>Takes the build once done: its result, or the exception that stopped it (and no result).</summary>
    /// <returns>False while it runs or when none was started.</returns>
    public bool TryTake(out T result, out Exception failure)
    {
        result = default;
        failure = null;
        if (_task is not { IsCompleted: true } done)
        {
            return false;
        }

        _task = null;
        if (done.Status == TaskStatus.RanToCompletion)
        {
            result = done.Result;
        }
        else
        {
            failure = done.Exception?.GetBaseException() ?? new TaskCanceledException(done);
        }

        return true;
    }

    /// <summary>Leaves the build under way behind: its result is dropped, a failure told to <paramref name="failed"/>.</summary>
    public void Abandon(Action<Exception> failed, TaskScheduler scheduler)
    {
        _task?.ContinueWith(task => failed(task.Exception?.GetBaseException()), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, scheduler);
        _task = null;
    }
}
