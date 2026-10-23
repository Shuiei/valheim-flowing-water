using System;
using System.Diagnostics;
using System.Linq;
using System.Text;

namespace FlowingWater.Volume;

/// <summary>The parts of the water's work in a server frame, as <see cref="FrameParts"/> tells them apart.</summary>
internal enum WaterPart
{
    /// <summary>The frame's own bookkeeping, and the pacer's between steps.</summary>
    Other,

    /// <summary>Looking for sources and drains.</summary>
    Markers,

    /// <summary>The ground's changes looked for (every few seconds).</summary>
    Terrain,

    /// <summary>The solids watch: the zones holding water looked over, changed bricks reading their solids again.</summary>
    Watch,

    /// <summary>Dry bricks forgotten.</summary>
    ForgetDry,

    /// <summary>A step begun: the bricks and columns gathered, each brick's part found.</summary>
    Begin,

    /// <summary>The heads of the step under way, a few columns at a time (the phases are taken a few bricks at a time).</summary>
    Heads,

    /// <summary>The flows through the faces.</summary>
    Flows,

    /// <summary>What each voxel can give.</summary>
    Shares,

    /// <summary>The flows scaled by it.</summary>
    Scale,

    /// <summary>The water moved.</summary>
    Apply,

    /// <summary>The bricks water asked for, made after a step.</summary>
    Made,

    /// <summary>Sources pouring.</summary>
    Pour,

    /// <summary>Drains taking.</summary>
    Drain,

    /// <summary>A brick reading its solids (made, or a solid changed by it), wherever it was asked for.</summary>
    Solids,

    /// <summary>A zone's generated heights asked of the game, wherever they were first needed.</summary>
    Heightmap,

    /// <summary>The bricks sent.</summary>
    Send,

    /// <summary>The debug log written (the last part).</summary>
    Log,
}

/// <summary>
/// Where the water's time goes, frame by frame: a frame is cut into <see cref="WaterPart"/>s, the time from one switch
/// to the next going to the part it was in (a part inside another switches to it and back). Over a period it keeps
/// each part's time in all, in its longest frame and in its longest go; the longest frame's parts; and the frames in
/// which the runtime collected garbage (a collection stops every thread, in whatever part it falls). A switch reads
/// the clock once: cheap enough for every frame.
/// </summary>
internal sealed class FrameParts
{
    // Each part's name for the log, the last part being the debug log's.
    private static readonly string[] Names = [.. Enumerable.Range(0, (int)WaterPart.Log + 1).Select(p => ((WaterPart)p).ToString().ToLowerInvariant())];

    private readonly Func<long> _clock;

    private readonly double _ticksPerMillisecond;

    private readonly Func<int> _collections;

    // This frame's milliseconds per part, and the period's: in all, the most in a frame, the most in one go, and the
    // longest frame's.
    private readonly double[] _frame = new double[Names.Length];

    private readonly double[] _total = new double[Names.Length];

    private readonly double[] _most = new double[Names.Length];

    private readonly double[] _mostAtOnce = new double[Names.Length];

    private readonly double[] _longest = new double[Names.Length];

    private bool _inFrame;

    private WaterPart _part;

    private long _since;

    private int _collectionsBefore;

    /// <summary>Parts timed by the stopwatch's clock, with the runtime's garbage collections.</summary>
    public FrameParts()
        : this(Stopwatch.GetTimestamp, Stopwatch.Frequency, () => GC.CollectionCount(0))
    {
    }

    /// <summary>Parts timed by this clock, with these collections counted.</summary>
    /// <param name="clock">The time now, in ticks.</param>
    /// <param name="ticksPerSecond">The clock's ticks in a second.</param>
    /// <param name="collections">How many garbage collections ran so far.</param>
    public FrameParts(Func<long> clock, long ticksPerSecond, Func<int> collections)
    {
        _clock = clock;
        _ticksPerMillisecond = ticksPerSecond / 1000.0;
        _collections = collections;
    }

    /// <summary>Frames since the last report.</summary>
    public int Frames { get; private set; }

    /// <summary>The longest frame since the last report, milliseconds.</summary>
    public double LongestFrame { get; private set; }

    /// <summary>Whether a garbage collection ran in the longest frame.</summary>
    public bool LongestCollected { get; private set; }

    /// <summary>Frames since the last report in which a garbage collection ran.</summary>
    public int CollectedFrames { get; private set; }

    /// <summary>A part's milliseconds since the last report.</summary>
    public double Total(WaterPart part) => _total[(int)part];

    /// <summary>A part's most milliseconds in one frame since the last report.</summary>
    public double Most(WaterPart part) => _most[(int)part];

    /// <summary>A part's most milliseconds in one go (with no switch away and back) since the last report.</summary>
    public double MostAtOnce(WaterPart part) => _mostAtOnce[(int)part];

    /// <summary>A part's milliseconds in the longest frame since the last report.</summary>
    public double InLongest(WaterPart part) => _longest[(int)part];

    /// <summary>A frame begins, in this part.</summary>
    public void Begin(WaterPart part)
    {
        Array.Clear(_frame, 0, _frame.Length);
        _inFrame = true;
        _part = part;
        _collectionsBefore = _collections();
        _since = _clock();
    }

    /// <summary>The time from now on is this part's (outside a frame, nothing is timed).</summary>
    /// <returns>The part it was in, to switch back to.</returns>
    public WaterPart Into(WaterPart part)
    {
        if (!_inFrame)
        {
            return part;
        }

        long now = _clock();
        double spent = (now - _since) / _ticksPerMillisecond;
        _frame[(int)_part] += spent;
        _mostAtOnce[(int)_part] = Math.Max(_mostAtOnce[(int)_part], spent);
        _since = now;
        WaterPart was = _part;
        _part = part;
        return was;
    }

    /// <summary>The frame is over: its parts count into the period's.</summary>
    public void End()
    {
        if (!_inFrame)
        {
            return;
        }

        Into(_part);
        _inFrame = false;
        bool collected = _collections() != _collectionsBefore;
        double length = 0.0;
        for (int p = 0; p < _frame.Length; p++)
        {
            _total[p] += _frame[p];
            _most[p] = Math.Max(_most[p], _frame[p]);
            length += _frame[p];
        }

        Frames++;
        CollectedFrames += collected ? 1 : 0;
        if (length > LongestFrame)
        {
            LongestFrame = length;
            LongestCollected = collected;
            Array.Copy(_frame, _longest, _frame.Length);
        }
    }

    /// <summary>
    /// The period's parts, for the log: each part that took any time, its time in all, the most in a frame and in one
    /// go; then the longest frame and its parts, the longest first. The period is then forgotten.
    /// </summary>
    public string Report()
    {
        var text = new StringBuilder();
        text.Append(FormattableString.Invariant($"Water's parts over {Frames} frames, ms in all/most in a frame/most at once:"));
        for (int p = 0; p < Names.Length; p++)
        {
            if (_total[p] >= 0.05)
            {
                text.Append(FormattableString.Invariant($" {Names[p]} {_total[p]:0.0}/{_most[p]:0.0}/{_mostAtOnce[p]:0.0}"));
            }
        }

        text.Append(FormattableString.Invariant($"; longest frame {LongestFrame:0.0} ms:"));
        foreach (int p in Enumerable.Range(0, Names.Length).Where(p => _longest[p] >= 0.05).OrderByDescending(p => _longest[p]))
        {
            text.Append(FormattableString.Invariant($" {Names[p]} {_longest[p]:0.0}"));
        }

        text.Append(LongestCollected ? ", a garbage collection in it" : ", no garbage collection in it");
        text.Append(FormattableString.Invariant($"; garbage collected in {CollectedFrames} frames"));
        Array.Clear(_total, 0, _total.Length);
        Array.Clear(_most, 0, _most.Length);
        Array.Clear(_mostAtOnce, 0, _mostAtOnce.Length);
        Array.Clear(_longest, 0, _longest.Length);
        Frames = 0;
        LongestFrame = 0.0;
        LongestCollected = false;
        CollectedFrames = 0;
        return text.ToString();
    }
}
