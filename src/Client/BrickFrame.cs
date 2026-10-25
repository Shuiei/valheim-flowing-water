using System;
using System.Diagnostics;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The bricks' share of each of the game's frames: the water read is handed to its bricks, the surfaces built are
/// handed to their meshes, and the bricks waiting to be drawn again are built, each for a part of a budget of
/// milliseconds, those waiting longest first (<see cref="TakeTurns{T}"/>), so that much water changing at once does not
/// stall a frame. The time the bricks take is measured for the in-game recorder (<see cref="BrickMeasures"/>).
/// </summary>
internal static class BrickFrame
{
    // Shortest time between two drawings of a brick, seconds.
    private const float RedrawSeconds = 0.1f;

    // Longest time handing read water to the bricks and drawing them may take in a frame, milliseconds: the others
    // wait for the next frame (one of each is always served).
    private const double FrameBudget = 2.0;

    // Of it, the time the read water may take, first: what is left is the drawing's, so that a burst of changed bricks
    // does not hold back the drawing of those already read, nor wait behind it.
    private const double ReadBudget = FrameBudget / 2;

    // Most milliseconds of a frame the bricks may take starting their reads (each hands its data to the thread pool,
    // which a busy pool can make slow); the bricks left start theirs next frame, still reading their newest data.
    private const double StartBudget = 0.5;

    private static readonly Stopwatch StartingFrame = new();

    private static readonly Stopwatch FrameDrawing = new();

    private static int _startingFrame = -1;

    private static int _frame = -1;

    // All the time the bricks took in the game's frames since the game started (reading their data, animating,
    // drawing): its cost to the player. Started and stopped a few times a frame: well under a microsecond.
    private static readonly Stopwatch Spent = new();

    // Where that time went: reading the bricks' data as it comes (taking it, and handing what was read to the brick:
    // the reading itself is on another thread), starting their surfaces' builds, handing built surfaces to their
    // meshes (the recorder looks for the frames one of them takes long).
    private static readonly Stopwatch Reading = new();

    // Of Reading, the part starting the bricks' reads in their Update (taking the data, handing it to the pool):
    // unbudgeted, once a brick's data changed.
    private static readonly Stopwatch Starting = new();

    private static readonly Stopwatch Building = new();

    private static readonly Stopwatch Taking = new();

    // The longest a single brick took starting its read since last asked, milliseconds. Only the game's main thread
    // comes here (the bricks' Update, the recorder's measures), so a plain max is enough, nothing atomic.
    private static double _longestStart;

    /// <summary>Bricks to draw again, those waiting longest first: a brick whose water keeps changing does not hold back the others.</summary>
    public static TakeTurns<WaterBrick> Waiting { get; } = new();

    /// <summary>Bricks whose surface is being built, handed to their meshes once built, those waiting longest first.</summary>
    public static TakeTurns<WaterBrick> Built { get; } = new();

    /// <summary>Bricks whose data is being read, handed their water once read, those waiting longest first.</summary>
    public static TakeTurns<WaterBrick> Read { get; } = new();

    /// <summary>All the milliseconds the bricks took in the game's frames since the game started.</summary>
    public static double SpentMilliseconds => Spent.Elapsed.TotalMilliseconds;

    /// <summary>The parts of <see cref="SpentMilliseconds"/>: reading data, starting builds, handing meshes over, starting reads.</summary>
    public static double[] SpentParts => [Reading.Elapsed.TotalMilliseconds, Building.Elapsed.TotalMilliseconds, Taking.Elapsed.TotalMilliseconds, Starting.Elapsed.TotalMilliseconds];

    /// <summary>The longest a single brick took starting its read since last asked, milliseconds (for measures).</summary>
    public static double TakeLongestStart()
    {
        double longest = _longestStart;
        _longestStart = 0.0;
        return longest;
    }

    /// <summary>A brick's own frame work (its Update), counted in the bricks' time.</summary>
    public static void Count(Action work)
    {
        Spent.Start();
        try
        {
            work();
        }
        finally
        {
            Spent.Stop();
        }
    }

    /// <summary>Whether this frame's time for starting reads has some left (the first brick of a frame always may).</summary>
    public static bool MayStart()
    {
        if (_startingFrame != Time.frameCount)
        {
            _startingFrame = Time.frameCount;
            StartingFrame.Reset();
        }

        return StartingFrame.Elapsed.TotalMilliseconds < StartBudget;
    }

    /// <summary>A brick starts reading its data (<paramref name="start"/>), its time counted and the longest kept.</summary>
    public static void StartRead(Action start)
    {
        Reading.Start();
        Starting.Start();
        StartingFrame.Start();
        long began = Stopwatch.GetTimestamp();
        try
        {
            start();
        }
        finally
        {
            _longestStart = Math.Max(_longestStart, (Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency);
            StartingFrame.Stop();
            Starting.Stop();
            Reading.Stop();
        }
    }

    /// <summary>
    /// Once a frame (from the first brick's LateUpdate), while the frame's time lasts: the water read is handed to its
    /// bricks (for at most half the budget), then, where the water is drawn, the surfaces built are handed to their
    /// meshes, and the bricks waiting are built again, longest waiting first. One still reading or building, or drawn
    /// less than a tenth of a second ago, waits on; one gone is dropped.
    /// </summary>
    public static void Serve(bool drawn)
    {
        if (Time.frameCount == _frame)
        {
            return;
        }

        _frame = Time.frameCount;
        Spent.Start();
        try
        {
            FrameDrawing.Reset();
            TakeReads();
            if (drawn)
            {
                Draw();
            }
        }
        finally
        {
            Spent.Stop();
        }
    }

    private static void TakeReads()
    {
        Reading.Start();
        try
        {
            Read.Serve(brick => brick == null || brick.ReadDone, () => FrameDrawing.Elapsed.TotalMilliseconds < ReadBudget, brick => Timed(brick, b => b.TakeRead()));
        }
        finally
        {
            Reading.Stop();
        }
    }

    private static void Draw()
    {
        Taking.Start();
        try
        {
            Built.Serve(brick => brick == null || brick.AnyBuildDone, InBudget, brick => Timed(brick, b => b.Take()));
        }
        finally
        {
            Taking.Stop();
        }

        // A build reads the bricks round it in the frame: none once the meshes took the frame's time.
        if (InBudget())
        {
            Building.Start();
            try
            {
                Waiting.Serve(
                    brick => brick == null || (!brick.AnyBuildBusy && Time.time - brick.DrawnAt >= RedrawSeconds),
                    InBudget,
                    brick => Timed(brick, b => b.Build()));
            }
            finally
            {
                Building.Stop();
            }
        }
    }

    // Whether this frame's time has some left.
    private static bool InBudget() => FrameDrawing.Elapsed.TotalMilliseconds < FrameBudget;

    // Does this for a brick still there, on the frame's time.
    private static void Timed(WaterBrick brick, Action<WaterBrick> work)
    {
        if (brick != null)
        {
            FrameDrawing.Start();
            work(brick);
            FrameDrawing.Stop();
        }
    }
}
