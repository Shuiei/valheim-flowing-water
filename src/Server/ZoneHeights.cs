using System;
using System.Collections.Generic;

namespace FlowingWater.Server;

/// <summary>
/// What the game gives of a zone's ground: its generated heights, and its terrain-changes object as it is now.
/// </summary>
/// <typeparam name="TZone">A zone.</typeparam>
/// <typeparam name="TStamp">What tells one state of a zone's changes from another (its object and revision).</typeparam>
internal interface IZoneGround<TZone, TStamp>
{
    /// <summary>The zone's key (<see cref="ZoneCache"/>).</summary>
    long Key(TZone zone);

    /// <summary>The zone's generated heights, one a vertex.</summary>
    float[] Generated(TZone zone);

    /// <summary>The zone's changes now: their stamp, and their bytes (null when the zone has none).</summary>
    (TStamp Stamp, byte[] Data) Changes(TZone zone);

    /// <summary>Something is wrong with a zone's ground, for the log.</summary>
    void Warn(TZone zone, string problem);
}

/// <summary>
/// Ground heights per zone: the generated heights, made once a zone (asking the game for them waits on its thread),
/// with the players' terrain changes applied over them, again whenever the changes move on. Zones no water is near
/// are let go (<see cref="Keep"/>).
/// </summary>
/// <typeparam name="TZone">A zone.</typeparam>
/// <typeparam name="TStamp">What tells one state of a zone's changes from another.</typeparam>
internal sealed class ZoneHeights<TZone, TStamp>
{
    private readonly IZoneGround<TZone, TStamp> _ground;

    private readonly int _vertices;

    private readonly Dictionary<long, Entry> _zones = [];

    /// <summary>Heights of zones of this many vertices, from this ground.</summary>
    public ZoneHeights(IZoneGround<TZone, TStamp> ground, int vertices)
    {
        _ground = ground;
        _vertices = vertices;
    }

    /// <summary>A zone's heights, made when first asked for.</summary>
    public float[] Heights(TZone zone)
    {
        long key = _ground.Key(zone);
        if (!_zones.TryGetValue(key, out Entry entry))
        {
            entry = new Entry(zone, _vertices);
            _zones[key] = entry;
            Build(entry, _ground.Changes(zone));
        }

        return entry.Heights;
    }

    /// <summary>Forgets the zones not among these (by key): they are made again when read.</summary>
    public void Keep(HashSet<long> zones) => ZoneCache.Keep(_zones, zones);

    /// <summary>Makes again the heights of the zones whose changes moved on since they were read, and tells of each.</summary>
    /// <param name="changed">A zone's heights changed: what was read of them before is out of date.</param>
    public void Refresh(Action<TZone> changed)
    {
        foreach (Entry entry in _zones.Values)
        {
            (TStamp Stamp, byte[] Data) changes = _ground.Changes(entry.Zone);
            if (!EqualityComparer<TStamp>.Default.Equals(changes.Stamp, entry.Stamp))
            {
                Build(entry, changes);
                changed(entry.Zone);
            }
        }
    }

    // The zone's heights: generated (once), and the changes over them. While the game's generated heights are not
    // usable the ground is taken at 0 m, and they are asked for again once the changes move on (the changes are
    // taken as read either way: not asked for, and told of, at every look).
    private void Build(Entry entry, (TStamp Stamp, byte[] Data) changes)
    {
        entry.Stamp = changes.Stamp;
        if (!entry.Generated && !Generate(entry))
        {
            Array.Clear(entry.Heights, 0, _vertices);
            return;
        }

        if (changes.Data == null)
        {
            Array.Copy(entry.Base, entry.Heights, _vertices);
        }
        else if (TerrainChanges.Apply(changes.Data, entry.Base, entry.Heights) is { } problem)
        {
            _ground.Warn(entry.Zone, problem);
        }
    }

    // The generated heights kept; a height that is not a number taken at 0 m. False when the game gave too few or too
    // many.
    private bool Generate(Entry entry)
    {
        float[] generated = _ground.Generated(entry.Zone);
        if (generated.Length != _vertices)
        {
            _ground.Warn(entry.Zone, $"the heightmap builder gave {generated.Length} vertices, expected {_vertices}");
            return false;
        }

        int bad = 0;
        for (int i = 0; i < _vertices; i++)
        {
            bool finite = TerrainChanges.IsFinite(generated[i]);
            entry.Base[i] = finite ? generated[i] : 0f;
            bad += finite ? 0 : 1;
        }

        if (bad > 0)
        {
            _ground.Warn(entry.Zone, $"the heightmap builder gave {bad} heights that are not numbers; the water takes the ground there at 0 m");
        }

        entry.Generated = true;
        return true;
    }

    private sealed class Entry
    {
        public Entry(TZone zone, int vertices)
        {
            Zone = zone;
            Heights = new float[vertices];
            Base = new float[vertices];
        }

        public TZone Zone { get; }

        public float[] Heights { get; }

        // The generated heights, once made.
        public float[] Base { get; }

        public bool Generated { get; set; }

        // The changes' stamp when last read (the default before: a zone with no changes is read once).
        public TStamp Stamp { get; set; }
    }
}
