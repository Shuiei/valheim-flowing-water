using System;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace FlowingWater.Volume;

/// <summary>
/// Runs a loop's bodies on the calling thread and a few helper threads of its own, each body once, whichever thread
/// takes it first. The calling thread waits only for bodies a helper has taken and not finished, never for a helper
/// that has not woken yet. (Parallel.For queues its helpers as thread-pool tasks and waits for them: on a busy server a
/// late one held the whole frame, 10 to 50 ms.) A body's exception is thrown again on the calling thread once the loop
/// is over.
/// </summary>
internal sealed class Crew
{
    // The loop under way, read by the helpers when woken; a helper woken for a loop already over finds no body left.
    private Loop _loop;

    // What the helpers wait on for a new loop, and how many loops were begun (a helper back from one goes on to the
    // next without waiting when it was begun meanwhile).
    private readonly object _gate = new();

    private int _begun;

    /// <summary>A crew of <paramref name="helpers"/> helper threads (0: the calling thread does everything).</summary>
    public Crew(int helpers)
    {
        Helpers = helpers;
        for (int h = 0; h < helpers; h++)
        {
            var thread = new Thread(Help) { IsBackground = true, Name = "FlowingWater crew " + h };
            thread.Start();
        }
    }

    /// <summary>
    /// The crew the water's steps share: 3 helpers at most (as fast as every processor for the water's steps, and the
    /// fewer threads the fewer to wait for), fewer on fewer processors.
    /// </summary>
    public static Crew Shared { get; } = new(Math.Max(0, Math.Min(3, Environment.ProcessorCount - 1)));

    /// <summary>The helper threads.</summary>
    public int Helpers { get; }

    /// <summary>Runs <paramref name="body"/> for each index from <paramref name="from"/> to before <paramref name="to"/>.</summary>
    public void For(int from, int to, Action<int> body)
    {
        if (to - from <= 0)
        {
            return;
        }

        var loop = new Loop(from, to, body);
        if (Helpers > 0 && to - from > 1)
        {
            lock (_gate)
            {
                _loop = loop;
                _begun++;
                Monitor.PulseAll(_gate);
            }
        }

        loop.Work();

        // Only bodies taken by a helper are left, each under way: spun for, then yielded to (never slept on, which
        // would cost a millisecond at least).
        for (int spins = 0; !loop.Over; spins++)
        {
            if (spins < 64)
            {
                Thread.SpinWait(16);
            }
            else
            {
                Thread.Yield();
            }
        }

        loop.Thrown?.Throw();
    }

    private void Help()
    {
        int seen = 0;
        while (true)
        {
            Loop loop;
            lock (_gate)
            {
                while (_begun == seen)
                {
                    Monitor.Wait(_gate);
                }

                seen = _begun;
                loop = _loop;
            }

            loop.Work();
        }
    }

    // One loop: the next index to take, and how many bodies are finished.
    private sealed class Loop
    {
        private readonly int _to;

        private readonly int _count;

        private readonly Action<int> _body;

        private int _next;

        private int _finished;

        public Loop(int from, int to, Action<int> body)
        {
            _next = from;
            _to = to;
            _count = to - from;
            _body = body;
        }

        public bool Over => Volatile.Read(ref _finished) == _count;

        private ExceptionDispatchInfo _thrown;

        // The first exception a body threw.
        public ExceptionDispatchInfo Thrown => Volatile.Read(ref _thrown);

        // Takes bodies until none is left.
        public void Work()
        {
            int i;
            while ((i = Interlocked.Increment(ref _next) - 1) < _to)
            {
                try
                {
                    _body(i);
                }
                catch (Exception e)
                {
                    // Kept for the calling thread, which throws it again: a helper thread has no one to tell.
                    Interlocked.CompareExchange(ref _thrown, ExceptionDispatchInfo.Capture(e), null);
                }
                finally
                {
                    Interlocked.Increment(ref _finished);
                }
            }
        }
    }
}
