using System;
using System.Collections.Generic;
using System.Linq;

namespace FlowingWater;

/// <summary>
/// Whether a file was written since it was last read, without the game: its write time looked at no more than once
/// every <see cref="Every"/> seconds. A new write time counts once it has held still from one look to the next, so a
/// file still being written is not read half way.
/// </summary>
internal sealed class FileWatch
{
    /// <summary>Seconds between looks at the file.</summary>
    public const float Every = 1f;

    private readonly Func<DateTime> _written;
    private DateTime _read;
    private DateTime _seen;
    private float _next;

    /// <summary>A watch on a file as it is now (taken as read).</summary>
    /// <param name="written">The file's write time now.</param>
    public FileWatch(Func<DateTime> written)
    {
        _written = written;
        _read = _seen = written();
    }

    /// <summary>Whether the file should be read again: written since it was last read, and not being written now.</summary>
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

        _read = written;
        return true;
    }

    /// <summary>The settings whose value differs from one reading to the next, as "name: was → now", in name order.</summary>
    public static List<string> Changes(IReadOnlyDictionary<string, string> before, IReadOnlyDictionary<string, string> after) =>
        [.. after.Where(s => !before.TryGetValue(s.Key, out string was) || was != s.Value)
            .OrderBy(s => s.Key, StringComparer.Ordinal)
            .Select(s => $"{s.Key}: {(before.TryGetValue(s.Key, out string was) ? was : "(none)")} → {s.Value}")];
}
