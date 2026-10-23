using System.Collections.Generic;

namespace FlowingWater.Server;

/// <summary>
/// What the server keeps per zone (heights, pieces, solid boxes) is let go once no brick is near the zone: kept, it
/// would grow with every zone the water ever reached, and so would the work done over it.
/// </summary>
internal static class ZoneCache
{
    /// <summary>Removes every zone not among these from a dictionary by zone key.</summary>
    /// <returns>How many were removed.</returns>
    public static int Keep<T>(Dictionary<long, T> byZone, HashSet<long> zones)
    {
        List<long> gone = null;
        foreach (long key in byZone.Keys)
        {
            if (!zones.Contains(key))
            {
                (gone ??= []).Add(key);
            }
        }

        if (gone == null)
        {
            return 0;
        }

        foreach (long key in gone)
        {
            byZone.Remove(key);
        }

        return gone.Count;
    }
}
