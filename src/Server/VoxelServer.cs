using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// The water on the server, in 3D: a <see cref="VoxelGrid"/> of the CellSize setting, moved by a
/// <see cref="VoxelSolver"/>, over the ground and building pieces (<see cref="GameSolids"/>, kept up to date by a
/// <see cref="SolidsWatch"/>). Sources pour at their point, drains take within their reach, the sea takes what
/// reaches it. Each brick with water is kept in a FlowingWater_Brick object, which the game saves with the world and
/// sends to the players near it. The water of the column tiles before (FlowingWater_Tile) is moved into voxels once.
/// </summary>
internal sealed class VoxelServer : IBrickWorld<ZDOID>
{
    // Longest frame stepped at once, seconds: a hitch is let go rather than stepped in one long go.
    private const float LongestFrame = 0.25f;

    // Bricks a step works through between two looks at the frame's budget.
    private const int Chunk = 32;

    // Most water time owed to the water while the server cannot keep up, seconds: past it, time is given up.
    private const float MostOwed = 0.5f;

    private const float TerrainRefreshSeconds = 2f;

    // The most of a frame looking for sources and drains may take, milliseconds (a part at least).
    private const double MarkerBudget = 0.5;

    // The most of a frame packing bricks to send may take, milliseconds (a brick at least).
    private const double SendBudget = 2.0;

    // The debug map draws one character per this many metres.
    private const float MapMetres = 2f;

    // Where each frame's time goes, told in the debug log.
    private readonly FrameParts _parts = new();

    private readonly ZoneTerrain _terrain;

    private readonly GameSolids _solids;

    private readonly SolidsWatch _watch;

    private readonly BrickObjects<ZDOID> _objects;

    private readonly List<Source> _markers = [];

    private readonly List<ZDO> _zdos = [];

    // The look for sources and drains, over the whole world, spread over frames.
    private readonly PrefabPass<ZDO> _markerPass;

    private readonly Stopwatch _markerWatch = new();

    private readonly HashSet<ZDOID> _markersSeen = [];

    private readonly Stopwatch _stepWatch = new();

    private readonly Stopwatch _frameWatch = new();

    private readonly Func<bool> _timeLeft;

    // The length of the step under way, seconds.
    private float _length;

    // Seconds the solids watch and forgetting dry bricks have waited for a step under way.
    private float _waited;

    // Bricks that read their solids again (a piece or the ground changed by them) since last logged.
    private int _refilled;

    private readonly StepPacer _pacer = new(MostOwed);

    private float _dropped;

    // The most a frame spent on the water's steps since last logged, milliseconds.
    private double _longestFrame;

    private readonly Stopwatch _sendWatch = new();

    private readonly Stopwatch _sendClock = new();

    private readonly Func<bool> _sendTimeLeft;

    private int _sentCount;

    private VoxelGrid _grid;

    private VoxelSolver _solver;

    private float _scanTimer = float.MaxValue;

    private float _sendTimer;

    private float _terrainTimer;

    private float _debugTimer;

    private bool _loaded;

    private bool _capWarned;

    private bool _slowWarned;

    private float _droppedInAll;

    // The points water was poured at that had no room, each told of once.
    private readonly NoRoom<Vector3> _noRoom = new(FlowingWaterPlugin.Log.LogWarning);

    // What a source's or drain's rate and reach may be, each one held or left out told of once.
    private readonly MarkerRules _rates = new(FlowingWaterPlugin.Log.LogWarning);

    // A voxel size refused because some water would have found no room (0: none), not tried again until it changes.
    private float _refusedSize;

    /// <summary>A server with an empty grid; the world's water is read on the first update.</summary>
    public VoxelServer()
    {
        _terrain = new ZoneTerrain(_parts);
        _solids = new GameSolids(_terrain, _parts);
        _watch = new SolidsWatch(_solids, _terrain);
        _timeLeft = TimeLeft;
        _sendTimeLeft = () => _sendClock.Elapsed.TotalMilliseconds < SendBudget;
        _markerPass = new PrefabPass<ZDO>([Prefabs.SourceName, Prefabs.DrainName], ZDOMan.instance.GetAllZDOsWithPrefabIterative);
        _objects = new BrickObjects<ZDOID>(this);
        NewGrid(SettingVoxelSize());
    }

    /// <summary>One frame: sources and drains, the solids' changes, the water moved, the bricks sent.</summary>
    public void Update(float dt)
    {
        _parts.Begin(WaterPart.Other);
        try
        {
            Frame(dt);
        }
        finally
        {
            _parts.End();
        }
    }

    // The frame's work, each part timed.
    private void Frame(float dt)
    {
        if (!_loaded)
        {
            Load();
            _loaded = true;
        }

        ApplyVoxelSize();
        // The markers found are swapped in once the look is over the whole world.
        _scanTimer += dt;
        if (_scanTimer >= FlowingWaterPlugin.Settings.MarkerScanSeconds.Value && _markerPass.Start())
        {
            _scanTimer = 0f;
        }

        _parts.Into(WaterPart.Markers);
        _markerWatch.Restart();
        if (_markerPass.Run(() => _markerWatch.Elapsed.TotalMilliseconds < MarkerBudget))
        {
            ScanMarkers();
        }

        _parts.Into(WaterPart.Terrain);
        _terrainTimer += dt;
        if (_terrainTimer >= TerrainRefreshSeconds)
        {
            _terrainTimer = 0f;
            _terrain.Refresh(_watch.GroundChanged);
        }

        _parts.Into(WaterPart.Other);

        // While a step is under way (it stopped at the frame's budget), nothing else changes the grid or reads it.
        dt = Mathf.Min(dt, LongestFrame);
        // Their time waits with them.
        _waited += dt;
        if (!_solver.InStep)
        {
            _parts.Into(WaterPart.Watch);
            _refilled += _watch.Update(_waited, _grid);
            _parts.Into(WaterPart.ForgetDry);
            _grid.ForgetDry(_waited, FlowingWaterPlugin.Settings.EmptyTileSeconds.Value);
            _parts.Into(WaterPart.Other);
            _waited = 0f;
        }

        _grid.MaxBricks = FlowingWaterPlugin.Settings.MaxBricks.Value;
        _solver.SeaLevel = ZoneSystem.instance.m_waterLevel;
        // Work that waited for a step under way gets its end: no new step is begun.
        Step(dt, _waited > 0f);
        WarnCapped();

        // A send is spread over frames, a few bricks packed in each, and may go on while a step is under way (a brick
        // whose water the step changes after it was packed counts a change, and is sent again next time).
        _sendTimer += dt;
        if (_sendTimer >= FlowingWaterPlugin.Settings.SendSeconds.Value && !_objects.Sending)
        {
            _sendTimer = 0f;
            _objects.Start(_grid.Bricks);
        }

        if (_objects.Sending)
        {
            _parts.Into(WaterPart.Send);
            _sendWatch.Start();
            _sendClock.Restart();
            _sentCount += _objects.Send(_grid.VoxelSize, _sendTimeLeft);
            _sendWatch.Stop();
            _parts.Into(WaterPart.Other);
        }

        float debug = FlowingWaterPlugin.Settings.DebugSeconds.Value;
        if (debug > 0f)
        {
            _debugTimer += dt;
            if (_debugTimer >= debug && !_solver.InStep)
            {
                _debugTimer = 0f;
                _parts.Into(WaterPart.Log);
                LogMaps();
            }
        }
    }

    // Sources pour and drains take for this much of the water's time (slowed down, the water's time is not the
    // server's). False when none poured or took anything.
    private bool Markers(float dt)
    {
        bool acted = false;
        foreach (Source marker in _markers)
        {
            WaterPart was = _parts.Into(marker.Drain ? WaterPart.Drain : WaterPart.Pour);
            acted |= marker.Drain ? Drain(marker, dt) : Pour(marker, dt);
            _parts.Into(was);
        }

        return acted;
    }

    private bool TimeLeft() => _pacer.Left(_frameWatch.Elapsed.TotalMilliseconds) > 0.0;

    // A step's state after going on with it: done, its sources and drains for its time; not done, its time owed again;
    // nothing to step, its sources and drains still act for its time, and if none did, the rest of the time owed
    // passes at once (not a short step at a time, each looking over every brick); if one did, the water it woke is
    // stepped with the rest.
    private void Ended(StepState state)
    {
        if (state == StepState.Done)
        {
            Markers(_length);
        }
        else if (state == StepState.Idle && !Markers(_length))
        {
            _pacer.TakeAll();
        }
        else if (state == StepState.Again)
        {
            _pacer.Return(_length);
        }
    }

    // The step under way finished now, whatever the budget (the world is saved, or the grid made again).
    private void FinishStep()
    {
        while (_solver.InStep)
        {
            Ended(_solver.Advance(_length, () => true, int.MaxValue));
        }
    }

    // The water's steps this frame: as many as the frame owes, a part at a time while the budget's credit lasts; with
    // noNewStep, only the one under way is gone on with.
    private void Step(float dt, bool noNewStep)
    {
        double budget = FlowingWaterPlugin.Settings.StepBudget.Value;
        _pacer.Add(dt, budget);
        _stepWatch.Start();
        _frameWatch.Restart();
        while (TimeLeft())
        {
            if (!_solver.InStep)
            {
                float step = _solver.MaxStep;
                if (noNewStep || !_pacer.Take(step))
                {
                    break;
                }

                _length = step;
            }

            Ended(_solver.Advance(_length, _timeLeft, Chunk));
        }

        double spent = _frameWatch.Elapsed.TotalMilliseconds;
        _pacer.Spent(spent);
        _longestFrame = Math.Max(_longestFrame, spent);
        _stepWatch.Stop();
        // A hitch gives up a little time now and then; a second of it in all is the water needing more than its
        // budget.
        float dropped = _pacer.TakeDropped();
        _dropped += dropped;
        _droppedInAll += dropped;
        if (_droppedInAll >= 1f && !_slowWarned)
        {
            _slowWarned = true;
            FlowingWaterPlugin.Log.LogWarning(FormattableString.Invariant($"Water: the water needs more than StepBudget ({budget:0} ms a frame) and slows down to keep the server's frames short"));
        }
    }

    /// <summary>
    /// Sends every changed brick now, all as they are at once (the world is about to be saved), and forgets dry ones;
    /// a send spread over frames under way is given up for it.
    /// </summary>
    public void Publish()
    {
        FinishStep();
        _sendWatch.Start();
        _sentCount += _objects.Publish(_grid.Bricks, _grid.VoxelSize);
        _sendWatch.Stop();
    }

    /// <summary>Stops listening to the world (it closes).</summary>
    public void Close() => _watch.Close();

    private static float SettingVoxelSize() => (float)TileData.ZoneMetres / TileData.SizeFor(FlowingWaterPlugin.Settings.CellSize.Value);

    private void NewGrid(float voxelSize)
    {
        _grid = new VoxelGrid(voxelSize, _solids) { MaxBricks = FlowingWaterPlugin.Settings.MaxBricks.Value };
        _solver = new VoxelSolver(_grid, _parts);
    }

    // A ZDO kept from frame to frame may be someone else's by now: the game hands a destroyed object's ZDO out again.
    // Its id is not: the game never gives out an id twice, so a destroyed object's id finds none. Its prefab is looked
    // at too, should anything have made the object something else.
    bool IBrickWorld<ZDOID>.IsBrick(ZDOID id) => ZDOMan.instance.GetZDO(id) is { } zdo && zdo.GetPrefab() == Prefabs.BrickHash;

    ZDOID IBrickWorld<ZDOID>.Make(Brick brick)
    {
        float span = Brick.Size * _grid.VoxelSize;
        return NewZdo(new Vector3((brick.X + 0.5f) * span, (brick.Y + 0.5f) * span, (brick.Z + 0.5f) * span), Prefabs.BrickHash).m_uid;
    }

    // The game gives a brick's object to a player near it, as it does every object; it is written as it is: the game
    // sends what the server writes to every player near, its owner too, by the data's revision, and the brick's owner
    // never writes it. Taking it back each time made it go to and fro, the whole object sent again each time it
    // changed hands.
    void IBrickWorld<ZDOID>.Write(ZDOID id, byte[] data) => ZDOMan.instance.GetZDO(id).Set(ZdoKeys.Brick, data);

    void IBrickWorld<ZDOID>.Destroy(ZDOID id) => DestroyZdo(ZDOMan.instance.GetZDO(id));

    // As ZNetView sets up a new object's ZDO: CreateNewZDO leaves the prefab unset, and a ZDO without one is saved
    // but can never be found again by prefab.
    private static ZDO NewZdo(Vector3 position, int prefab)
    {
        ZDO zdo = ZDOMan.instance.CreateNewZDO(position, prefab);
        zdo.SetPrefab(prefab);
        zdo.Persistent = true;
        zdo.SetType(ZDO.ObjectType.Default);
        zdo.SetDistant(false);
        return zdo;
    }

    // Only the owner can destroy an object.
    private static void DestroyZdo(ZDO zdo)
    {
        zdo.SetOwner(ZDOMan.GetSessionID());
        ZDOMan.instance.DestroyZDO(zdo);
    }

    // The bricks saved with the world, and the column tiles of before, moved into voxels.
    private void Load()
    {
        float bricks = LoadBricks();
        float tiles = LoadTiles();
        FlowingWaterPlugin.Log.LogInfo(FormattableString.Invariant($"Water: {_grid.Bricks.Count} bricks of {_grid.VoxelSize} m voxels, {bricks:0.0} m³ read from bricks, {tiles:0.0} m³ moved over from column tiles"));
    }

    private float LoadBricks()
    {
        float total = 0f;
        foreach (ZDO zdo in All(Prefabs.BrickName))
        {
            BrickView view = Read(zdo);
            if (view == null)
            {
                continue;
            }

            // A brick of a voxel size the setting cannot have, or beyond the grid's reach, is left as it was.
            if (Array.IndexOf(TileData.CellSizes, view.VoxelSize) < 0 || Math.Abs((long)view.X) > VoxelGrid.MaxBrick || Math.Abs((long)view.Y) > VoxelGrid.MaxBrick || Math.Abs((long)view.Z) > VoxelGrid.MaxBrick)
            {
                FlowingWaterPlugin.Log.LogWarning(FormattableString.Invariant($"Water brick {zdo.m_uid}: brick {view.X},{view.Y},{view.Z} of {view.VoxelSize} m voxels cannot be read into the grid; left as it is"));
                continue;
            }

            // Only a brick whose every voxel of water found room is taken over; another is kept as it is.
            if (PourView(view) is not { } poured)
            {
                FlowingWaterPlugin.Log.LogWarning(FormattableString.Invariant($"Water brick {zdo.m_uid}: some of its water finds no room in the grid (solid all the way up, or MaxBricks reached); left as it is"));
                continue;
            }

            total += poured;
            DestroyZdo(zdo);
        }

        return total;
    }

    // A saved brick's water into the grid: as it was when the voxels are the same size, else poured at each voxel's
    // middle into the new grid's voxels. Nothing is poured (null) unless every voxel of water finds room.
    private float? PourView(BrickView view)
    {
        float s = view.VoxelSize;
        float volume = s * s * s;
        var water = new List<(Vector3 Point, float Volume)>();
        for (int i = 0; i < Brick.Count; i++)
        {
            if (view.Water[i] > Brick.Dry)
            {
                (int x, int y, int z) = Brick.Place(i);
                water.Add((new Vector3(((view.X * Brick.Size) + x + 0.5f) * s, ((view.Y * Brick.Size) + y + 0.5f) * s, ((view.Z * Brick.Size) + z + 0.5f) * s), view.Water[i] * volume));
            }
        }

        return PourAll(water);
    }

    // Water poured at points, all of it or none: null (nothing poured) unless each finds room, under the brick cap.
    private float? PourAll(List<(Vector3 Point, float Volume)> water)
    {
        // Bricks made only to look for room are forgotten again when the water does not go in: they would hold
        // places under MaxBricks for nothing.
        var before = new HashSet<Brick>(_grid.Bricks);
        var pours = new List<(int X, int Y, int Z, float Volume)>();
        foreach ((Vector3 point, float volume) in water)
        {
            if (!Room(point, out int x, out int y, out int z))
            {
                foreach (Brick made in _grid.Bricks.Where(b => !before.Contains(b)).ToList())
                {
                    _grid.Forget(made);
                }

                return null;
            }

            pours.Add((x, y, z, volume));
        }

        foreach ((int x, int y, int z, float cubicMetres) in pours)
        {
            _grid.Pour(x, y, z, cubicMetres);
        }

        return pours.Sum(p => p.Volume);
    }

    private static BrickView Read(ZDO zdo)
    {
        byte[] data = zdo.GetByteArray(ZdoKeys.Brick);
        if (data == null)
        {
            return null;
        }

        try
        {
            return BrickData.Decode(data);
        }
        catch (InvalidDataException e)
        {
            FlowingWaterPlugin.Log.LogWarning($"Water brick {zdo.m_uid}: unreadable, its water is lost ({e.Message})");
            return null;
        }
    }

    // Each column tile's water poured on the ground of its cells, then the tile removed.
    private float LoadTiles()
    {
        float total = 0f;
        foreach (ZDO zdo in All(Prefabs.TileName))
        {
            byte[] bytes = zdo.GetByteArray(ZdoKeys.Data);
            if (bytes == null)
            {
                FlowingWaterPlugin.Log.LogInfo($"Water tile {zdo.m_uid}: empty, removed");
            }
            else if (TileWater(zdo, bytes) is { } water)
            {
                // Like a saved brick: a tile whose water cannot all find room is left as it is, to try again.
                if (PourAll(water) is not { } poured)
                {
                    FlowingWaterPlugin.Log.LogWarning(FormattableString.Invariant($"Water tile {zdo.m_uid}: some of its water finds no room in the grid (solid all the way up, or MaxBricks reached); kept, not drawn, to try again at the next start"));
                    continue;
                }

                total += poured;
            }

            // An unreadable tile (TileWater told of it) is removed too: its water is lost.
            DestroyZdo(zdo);
        }

        return total;
    }

    // An old column tile's water: each cell's, at the ground in its middle; null (told of) when its data is unreadable.
    private List<(Vector3 Point, float Volume)> TileWater(ZDO zdo, byte[] bytes)
    {
        float[] depths;
        int side;
        try
        {
            depths = ColumnTile.Depths(bytes, out side);
        }
        catch (InvalidDataException e)
        {
            FlowingWaterPlugin.Log.LogWarning($"Water tile {zdo.m_uid}: unreadable ({e.Message}), removed; its water is lost");
            return null;
        }

        Vector2s zone = ZoneSystem.GetZone(zdo.GetPosition());
        float cell = (float)TileData.ZoneMetres / side;
        var water = new List<(Vector3 Point, float Volume)>();
        for (int z = 0; z < side; z++)
        {
            for (int x = 0; x < side; x++)
            {
                float depth = depths[(z * side) + x];
                if (depth > 0f)
                {
                    float wx = TileData.OriginX(zone) + (x * cell);
                    float wz = TileData.OriginZ(zone) + (z * cell);
                    water.Add((new Vector3(wx, Ground(wx, wz) + 0.01f, wz), depth * cell * cell));
                }
            }
        }

        return water;
    }

    private List<ZDO> All(string prefab)
    {
        int index = 0;
        _zdos.Clear();
        while (!ZDOMan.instance.GetAllZDOsWithPrefabIterative(prefab, _zdos, ref index))
        {
        }

        return [.. _zdos];
    }

    // The CellSize setting changed: the water is poured into a grid of the new size.
    private void ApplyVoxelSize()
    {
        float size = SettingVoxelSize();
        if (Mathf.Approximately(size, _grid.VoxelSize))
        {
            // Back to the size the water is in: a size refused before may be tried again when chosen again.
            _refusedSize = 0f;
            return;
        }

        if (Mathf.Approximately(size, _refusedSize))
        {
            return;
        }

        FinishStep();
        float s = _grid.VoxelSize;
        List<BrickView> views = [.. _grid.Bricks.Select(b => BrickData.Decode(BrickData.Encode(b, s)))];
        VoxelGrid oldGrid = _grid;
        VoxelSolver oldSolver = _solver;
        NewGrid(size);
        float total = 0f, lost = 0f;
        foreach (BrickView view in views)
        {
            if (PourView(view) is { } poured)
            {
                total += poured;
            }
            else
            {
                lost += view.Water.Sum() * s * s * s;
            }
        }

        // Water that would find no room in the new voxels (solid all the way up, or MaxBricks reached) is not given
        // up: the grid stays as it was, until the setting changes again.
        if (lost > 0f)
        {
            _grid = oldGrid;
            _solver = oldSolver;
            _refusedSize = size;
            FlowingWaterPlugin.Log.LogWarning(FormattableString.Invariant($"Water: voxels of {size} m refused: {lost:0.0} m³ would find no room in them; the water stays in voxels of {s} m"));
            return;
        }

        // The bricks are cut differently now: a key of the old grid is another place in the new one.
        _refusedSize = 0f;
        _objects.Drop();
        FlowingWaterPlugin.Log.LogInfo(FormattableString.Invariant($"Water: voxels of {size} m now, {total:0.0} m³ moved over"));
    }

    // Water poured at a point: into its voxel, or the first open one over it when the point is in a solid.
    private bool PourAt(Vector3 point, float cubicMetres)
    {
        if (Room(point, out int x, out int y, out int z))
        {
            _noRoom.Room(point);
            return _grid.Pour(x, y, z, cubicMetres);
        }

        // The brick cap is told of once (WarnCapped); a point solid all the way up (or past the grid's reach), once
        // until it has room again.
        if (!_grid.Capped)
        {
            _noRoom.Add(point);
        }

        return false;
    }

    // The voxel water poured at a point goes into: its own, or the first open one over it (within four bricks); false
    // when none is, a brick it needs cannot be made (MaxBricks), or the point is past the grid's reach.
    private bool Room(Vector3 point, out int x, out int y, out int z)
    {
        float s = _grid.VoxelSize;
        x = Mathf.FloorToInt(point.x / s);
        y = Mathf.FloorToInt(point.y / s);
        z = Mathf.FloorToInt(point.z / s);
        for (int up = 0; up < Brick.Size * 4; up++, y++)
        {
            if (!VoxelGrid.Within(x >> 4, y >> 4, z >> 4) || _grid.Make(x >> 4, y >> 4, z >> 4) is not { } brick)
            {
                return false;
            }

            if (brick.Solid[Brick.Index(x & 15, y & 15, z & 15)] < 1f)
            {
                return true;
            }
        }

        return false;
    }

    private bool Pour(Source source, float dt) => PourAt(PourPoint(source), source.Rate * dt);

    private static Vector3 PourPoint(Source source) => source.Point + new Vector3(0f, 0.05f, 0f);

    // A drain takes from every voxel within its reach (its own voxel at least): all of it with no rate, else at most
    // its rate, from each in proportion to its water. Only bricks there are looked in. False when it took nothing.
    private bool Drain(Source drain, float dt)
    {
        float s = _grid.VoxelSize;
        int reach = Mathf.FloorToInt(drain.Radius / s);
        int cx = Mathf.FloorToInt(drain.Point.x / s);
        int cy = Mathf.FloorToInt((drain.Point.y + 0.05f) / s);
        int cz = Mathf.FloorToInt(drain.Point.z / s);
        float there = 0f;
        var found = new List<(Brick Brick, int Index)>();
        foreach (Brick brick in _grid.Bricks)
        {
            int x0 = brick.X * Brick.Size;
            int y0 = brick.Y * Brick.Size;
            int z0 = brick.Z * Brick.Size;
            if (x0 > cx + reach || x0 + Brick.Size <= cx - reach || y0 > cy + reach || y0 + Brick.Size <= cy - reach || z0 > cz + reach || z0 + Brick.Size <= cz - reach)
            {
                continue;
            }

            for (int i = 0; i < Brick.Count; i++)
            {
                (int x, int y, int z) = Brick.Place(i);
                int dx = x0 + x - cx;
                int dy = y0 + y - cy;
                int dz = z0 + z - cz;
                bool within = ((dx * dx) + (dy * dy) + (dz * dz)) * s * s <= drain.Radius * drain.Radius || (dx == 0 && dy == 0 && dz == 0);
                if (within && brick.Water[i] > 0f)
                {
                    found.Add((brick, i));
                    there += brick.Water[i];
                }
            }
        }

        float volume = s * s * s;
        float share = there <= 0f ? 0f : drain.Rate <= 0f ? 1f : Mathf.Min(1f, drain.Rate * dt / (there * volume));
        if (share <= 0f)
        {
            return false;
        }

        // Drained, a sleeping brick wakes: the water round the drain flows in.
        foreach ((Brick brick, int i) in found)
        {
            brick.Water[i] -= brick.Water[i] * share;
            brick.Wets(i);
        }

        return true;
    }

    private void WarnCapped()
    {
        if (_grid.Capped && !_capWarned)
        {
            _capWarned = true;
            FlowingWaterPlugin.Log.LogWarning($"Water: {FlowingWaterPlugin.Settings.MaxBricks.Value} bricks (MaxBricks) reached; the water stops spreading further");
        }
    }

    private float Ground(float x, float z)
    {
        Vector2s zone = ZoneSystem.GetZone(new Vector3(x, 0f, z));
        return _terrain.HeightAt(zone, x - TileData.OriginX(zone), z - TileData.OriginZ(zone));
    }

    // The sources and drains a whole look found, and the config's, in place of the last ones.
    private void ScanMarkers()
    {
        int before = _markers.Count;
        _markers.Clear();
        _markersSeen.Clear();
        _watch.Pinned.Clear();
        Collect(Prefabs.SourceName, false, FlowingWaterPlugin.Settings.DefaultSourceRate.Value);
        Collect(Prefabs.DrainName, true, FlowingWaterPlugin.Settings.DefaultDrainRate.Value);
        CollectFixed(FlowingWaterPlugin.Settings.FixedSources.Value, false);
        CollectFixed(FlowingWaterPlugin.Settings.FixedDrains.Value, true);
        _noRoom.Pouring(point => _markers.Exists(m => !m.Drain && PourPoint(m) == point));
        if (_markers.Count != before)
        {
            FlowingWaterPlugin.Log.LogInfo($"Water: {_markers.Count} sources and drains");
        }
    }

    private void Collect(string prefab, bool drain, float defaultRate)
    {
        float defaultRadius = FlowingWaterPlugin.Settings.DefaultDrainRadius.Value;
        int hash = prefab.GetStableHashCode();
        foreach (ZDO zdo in _markerPass.Found(prefab))
        {
            // One gone during the look, or found twice (gone, then made again as another object), is left out.
            if (!zdo.IsValid() || zdo.GetPrefab() != hash || !_markersSeen.Add(zdo.m_uid))
            {
                continue;
            }

            float radius = drain ? MarkerRules.Reach(zdo.GetFloat(ZdoKeys.Radius, defaultRadius)) : 0f;
            if (_rates.TryRate(zdo.GetFloat(ZdoKeys.Rate, defaultRate), drain, zdo.GetPosition().ToString(), zdo.m_uid.ToString(), out float rate))
            {
                _markers.Add(new Source(zdo.GetPosition(), rate, drain, radius));
            }
        }
    }

    // The config's fixed sources or drains, on the ground there: its zone's heights are kept for the next look, though
    // no brick may be near (an idle drain).
    private void CollectFixed(string list, bool drain)
    {
        foreach ((float x, float z, float rate, float radius) in _rates.Fixed(list, drain, FlowingWaterPlugin.Settings.DefaultDrainRadius.Value))
        {
            _watch.Pinned.Add(ZoneSystem.GetZone(new Vector3(x, 0f, z)));
            _markers.Add(new Source(new Vector3(x, Ground(x, z), z), rate, drain, radius));
        }
    }

    // A top-down map per zone, a character per 2 by 2 metres: '.' dry, then deeper and deeper (all the water over
    // the column, metres).
    private void LogMaps()
    {
        float s = _grid.VoxelSize;
        var depths = new Dictionary<long, Dictionary<(int X, int Z), float>>();
        foreach (Brick brick in _grid.Bricks)
        {
            for (int i = 0; i < Brick.Count; i++)
            {
                if (brick.Water[i] <= Brick.Dry)
                {
                    continue;
                }

                (int x, int _, int z) = Brick.Place(i);
                float wx = ((brick.X * Brick.Size) + x + 0.5f) * s;
                float wz = ((brick.Z * Brick.Size) + z + 0.5f) * s;
                Vector2s zone = ZoneSystem.GetZone(new Vector3(wx, 0f, wz));
                long key = TileData.Key(zone);
                if (!depths.TryGetValue(key, out Dictionary<(int X, int Z), float> map))
                {
                    depths[key] = map = [];
                }

                var cell = ((int)((wx - TileData.OriginX(zone)) / MapMetres), (int)((wz - TileData.OriginZ(zone)) / MapMetres));
                map[cell] = (map.TryGetValue(cell, out float d) ? d : 0f) + (brick.Water[i] * s * s * s / (MapMetres * MapMetres));
            }
        }

        var text = new StringBuilder();
        text.Append(FormattableString.Invariant($"Water: {_grid.Bricks.Count} bricks of {s} m voxels, {_grid.TotalWater():0.0} m³, {_markers.Count} markers; stepping took {_stepWatch.Elapsed.TotalMilliseconds:0} ms (at most {_longestFrame:0.0} ms in a frame), sending {_sendWatch.Elapsed.TotalMilliseconds:0} ms ({_sentCount} bricks sent), {_dropped:0.0} s of water time given up, {_refilled} bricks read their solids again"));
        text.Append('\n').Append(_parts.Report());
        _stepWatch.Reset();
        _sendWatch.Reset();
        _sentCount = 0;
        _dropped = 0f;
        _refilled = 0;
        _longestFrame = 0.0;
        int cells = (int)(TileData.ZoneMetres / MapMetres);
        foreach (KeyValuePair<long, Dictionary<(int X, int Z), float>> zone in depths)
        {
            text.Append(FormattableString.Invariant($"\nzone {zone.Key >> 32},{(int)zone.Key}: {zone.Value.Values.Sum() * MapMetres * MapMetres:0.0} m³"));
            for (int z = cells - 1; z >= 0; z--)
            {
                text.Append('\n');
                for (int x = 0; x < cells; x++)
                {
                    float depth = zone.Value.TryGetValue((x, z), out float d) ? d : 0f;
                    text.Append(depth <= 0.01f ? '.' : depth < 0.1f ? ':' : depth < 0.5f ? 'o' : depth < 2f ? 'O' : '#');
                }
            }
        }

        FlowingWaterPlugin.Log.LogInfo(text.ToString());
    }

    // A source or drain: where, how much a second, and a drain's reach.
    private sealed class Source
    {
        public Source(Vector3 point, float rate, bool drain, float radius)
        {
            Point = point;
            Rate = rate;
            Drain = drain;
            Radius = radius;
        }

        public Vector3 Point { get; }

        public float Rate { get; }

        public bool Drain { get; }

        public float Radius { get; }
    }
}
