using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// The server's look for sources and drains: the placed markers over the whole world, a part of it each frame
/// (<see cref="PrefabPass{TObject}"/>), and the config's fixed ones; the markers found are swapped in once a look is
/// over, and the next look begins <c>MarkerScanSeconds</c> after. Each marker's rate and reach are taken as
/// <see cref="MarkerRules"/> say, and only the markers the server allows are kept (<see cref="MarkerRights"/>: who
/// placed them), the allowed ones written in a file of the server's.
/// </summary>
internal sealed class MarkerScan
{
    // The most of a frame looking for sources and drains may take, milliseconds (a part at least).
    private const double MarkerBudget = 0.5;

    private readonly PrefabPass<ZDO> _pass;

    private readonly Stopwatch _watch = new();

    private readonly Func<bool> _timeLeft;

    private readonly LookWait _wait = new();

    private readonly HashSet<ZDOID> _seen = [];

    private readonly HashSet<string> _ids = [];

    private readonly List<Source> _markers = [];

    // What a source's or drain's rate and reach may be, each one held or left out told of once.
    private readonly MarkerRules _rates = new(FlowingWaterPlugin.Log.LogWarning);

    private readonly MarkerRights _rights = new(FlowingWaterPlugin.Log.LogWarning);

    private readonly string _rightsFile;

    private bool _rightsFileTold;

    private readonly HashSet<Vector2s> _pinned;

    private readonly Func<float, float, float> _ground;

    /// <summary>A look over the world's markers.</summary>
    /// <param name="rightsFile">Where the markers the server allowed are kept, one an id a line.</param>
    /// <param name="pinned">The zones whose ground is kept for the config's fixed sources and drains (<see cref="SolidsWatch.Pinned"/>).</param>
    /// <param name="ground">The ground's height at a world point (x, z), for the fixed ones.</param>
    public MarkerScan(string rightsFile, HashSet<Vector2s> pinned, Func<float, float, float> ground)
    {
        _rightsFile = rightsFile;
        _pinned = pinned;
        _ground = ground;
        _pass = new PrefabPass<ZDO>([Prefabs.SourceName, Prefabs.DrainName], ZDOMan.instance.GetAllZDOsWithPrefabIterative);
        _timeLeft = () => _watch.Elapsed.TotalMilliseconds < MarkerBudget;
        ReadRights();
    }

    /// <summary>The sources and drains as the last whole look found them (and the config's).</summary>
    public IReadOnlyList<Source> Markers => _markers;

    /// <summary>One frame of looking; true when a look ended and <see cref="Markers"/> was swapped in anew.</summary>
    public bool Update(float dt, FrameParts parts)
    {
        if (_wait.Due(dt, FlowingWaterPlugin.Settings.MarkerScanSeconds.Value, _pass.Busy) && _pass.Start())
        {
            _wait.Began();
        }

        WaterPart was = parts.Into(WaterPart.Markers);
        _watch.Restart();
        bool over = _pass.Run(_timeLeft);
        if (over)
        {
            Collect();
        }

        parts.Into(was);
        return over;
    }

    // The sources and drains a whole look found, and the config's, in place of the last ones.
    private void Collect()
    {
        int before = _markers.Count;
        _markers.Clear();
        _seen.Clear();
        _ids.Clear();
        _pinned.Clear();
        bool everyone = FlowingWaterPlugin.Settings.CommandsForEveryone.Value;
        Collect(Prefabs.SourceName, Prefabs.SourceHash, false, FlowingWaterPlugin.Settings.DefaultSourceRate.Value, everyone);
        Collect(Prefabs.DrainName, Prefabs.DrainHash, true, FlowingWaterPlugin.Settings.DefaultDrainRate.Value, everyone);
        _rights.Keep(_ids);
        WriteRights();
        CollectFixed(FlowingWaterPlugin.Settings.FixedSources.Value, false);
        CollectFixed(FlowingWaterPlugin.Settings.FixedDrains.Value, true);
        if (_markers.Count != before)
        {
            FlowingWaterPlugin.Log.LogInfo($"Water: {_markers.Count} sources and drains");
        }
    }

    private void Collect(string prefab, int hash, bool drain, float defaultRate, bool everyone)
    {
        float defaultRadius = FlowingWaterPlugin.Settings.DefaultDrainRadius.Value;
        foreach (ZDO zdo in _pass.Found(prefab))
        {
            // One gone during the look, or found twice (gone, then made again as another object), is left out.
            if (!zdo.IsValid() || zdo.GetPrefab() != hash || !_seen.Add(zdo.m_uid))
            {
                continue;
            }

            string id = zdo.m_uid.ToString();
            _ids.Add(id);
            long creator = zdo.GetLong(ZDOVars.s_creator, 0L);
            string Where() => $"{(drain ? "Drain" : "Source")} at {zdo.GetPosition()}";
            if (_rights.Decide(id, everyone, creator, creator == 0L ? null : MayPlace(creator, zdo.m_uid.UserID), Where) != MarkerRights.Verdict.Allowed)
            {
                continue;
            }

            float radius = drain ? MarkerRules.Reach(zdo.GetFloat(ZdoKeys.Radius, defaultRadius)) : 0f;
            if (_rates.TryRate(zdo.GetFloat(ZdoKeys.Rate, defaultRate), drain, () => zdo.GetPosition().ToString(), id, out float rate))
            {
                _markers.Add(new Source(zdo.GetPosition(), rate, drain, radius));
            }
        }
    }

    // Whether the player of this id (the character's, as the game writes it into what they place) may place a source
    // or drain: the host, or one of the server's admins; null when no player of the id is on the server now, or the
    // marker was not made by that player's game (its object's id is given out by the game that made it: a marker
    // naming a placer whose game did not make it is not theirs, and waits). Both the placer and the maker are what a
    // player's game writes: one changed to write a false pair gets past this, as it would past any check the game's
    // world allows, so this keeps honest games to the setting rather than stopping a cheat.
    private static bool? MayPlace(long creator, long madeBy)
    {
        if (Player.m_localPlayer != null && Player.m_localPlayer.GetPlayerID() == creator)
        {
            return madeBy == ZDOMan.GetSessionID() ? true : null;
        }

        foreach (ZNetPeer peer in ZNet.instance.GetPeers())
        {
            if (peer.IsReady() && ZDOMan.instance.GetZDO(peer.m_characterID) is { } character && character.GetLong(ZDOVars.s_playerID, 0L) == creator)
            {
                return peer.m_uid == madeBy ? ZNet.instance.IsAdmin(peer.m_socket.GetHostName()) : null;
            }
        }

        return null;
    }

    // The config's fixed sources or drains, on the ground there: its zone's heights are kept for the next look, though
    // no brick may be near (an idle drain).
    private void CollectFixed(string list, bool drain)
    {
        foreach ((float x, float z, float rate, float radius) in _rates.Fixed(list, drain, FlowingWaterPlugin.Settings.DefaultDrainRadius.Value))
        {
            _pinned.Add(ZoneSystem.GetZone(new Vector3(x, 0f, z)));
            _markers.Add(new Source(new Vector3(x, _ground(x, z), z), rate, drain, radius));
        }
    }

    // The markers allowed before, from the file: none when there is no file yet. A file that cannot be read is told of,
    // and the markers wait to be checked again.
    private void ReadRights()
    {
        try
        {
            if (File.Exists(_rightsFile))
            {
                _rights.Read(File.ReadAllLines(_rightsFile));
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            FlowingWaterPlugin.Log.LogError($"Water: the allowed sources and drains could not be read from {_rightsFile}; each is checked again as its placer joins: {e.Message}");
        }
    }

    // The markers allowed, into the file, when they changed. A file that cannot be written is told of once: the rights
    // hold until the server stops.
    private void WriteRights()
    {
        if (!_rights.Changed)
        {
            return;
        }

        try
        {
            File.WriteAllLines(_rightsFile, _rights.Lines);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            if (!_rightsFileTold)
            {
                _rightsFileTold = true;
                FlowingWaterPlugin.Log.LogError($"Water: the allowed sources and drains could not be written to {_rightsFile}; they hold until the server stops: {e.Message}");
            }
        }
    }
}

/// <summary>A source or drain: where, how much a second, and a drain's reach.</summary>
internal sealed class Source
{
    /// <summary>A source (radius 0) or drain at a point.</summary>
    public Source(Vector3 point, float rate, bool drain, float radius)
    {
        Point = point;
        Rate = rate;
        Drain = drain;
        Radius = radius;
    }

    /// <summary>Where it is, world metres.</summary>
    public Vector3 Point { get; }

    /// <summary>Cubic metres a second (a drain's 0: everything that reaches it).</summary>
    public float Rate { get; }

    /// <summary>A drain, else a source.</summary>
    public bool Drain { get; }

    /// <summary>A drain's reach, metres.</summary>
    public float Radius { get; }
}
