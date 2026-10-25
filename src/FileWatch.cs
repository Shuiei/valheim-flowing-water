using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowingWater;

/// <summary>
/// Whether a file was written since it was last read, without the game: its write time looked at no more than once
/// every <see cref="Every"/> seconds. A new write time counts once it has held still from one look to the next, so a
/// file still being written is not read half way. It stays to be read, told again at each look, until the reader says
/// it has read it (<see cref="Read"/>), so a read that failed is tried again.
/// </summary>
internal sealed class FileWatch
{
    /// <summary>Seconds between looks at the file.</summary>
    public const float Every = 1f;

    private readonly Func<DateTime> _written;
    private DateTime _read;
    private DateTime _seen;
    private DateTime _told;
    private float _next;

    /// <summary>A watch on a file as it is now (taken as read).</summary>
    /// <param name="written">The file's write time now.</param>
    public FileWatch(Func<DateTime> written)
    {
        _written = written;
        _read = _seen = _told = written();
    }

    /// <summary>Whether the write last told was told before: reading it failed then.</summary>
    public bool Again { get; private set; }

    /// <summary>Whether the file should be read: written since it was last read, and not being written now.</summary>
    /// <param name="now">Seconds, from any start, going on.</param>
    public bool Changed(float now)
    {
        if (now < _next)
        {
            return false;
        }

        _next = now + Every;
        DateTime written = _written();
        bool still = written == _seen;
        _seen = written;
        if (!still || written == _read)
        {
            return false;
        }

        Again = written == _told;
        _told = written;
        return true;
    }

    /// <summary>The write last told has been read.</summary>
    public void Read() => _read = _told;

    /// <summary>The settings whose value differs from one reading to the next, as "name: was → now", in name order.</summary>
    public static List<string> Changes(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after) =>
        [.. after.Where(s => !before.TryGetValue(s.Key, out string was) || was != s.Value)
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => $"{s.Key}: {(before.TryGetValue(s.Key, out string was) ? was : "(none)")} → {s.Value}")];
}
