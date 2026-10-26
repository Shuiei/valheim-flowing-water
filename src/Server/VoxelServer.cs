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
/// <see cref="SolidsWatch"/>). Sources pour at their point, drains take within their reach (<see cref="MarkerScan"/>
/// finds them), the sea takes what reaches it. Each brick with water is kept in a FlowingWater_Brick object, which the
/// game saves with the world and sends to the players near it. The water saved is read back as the world opens
/// (<see cref="WorldLoad"/>), the column tiles of before (FlowingWater_Tile) moved into voxels once.
/// </summary>
internal sealed class VoxelServer : IBrickWorld<ZDOID>
{
    // Longest frame stepped at once, seconds: a hitch is let go rather than stepped in one long go.
    private const float LongestFrame = 0.25f;

    // Fewest bricks a step works through between two looks at the frame's budget (more with many bricks: each part is
    // handed to the server's processors at once, which costs a little each time).
    private const int Chunk = 32;

    // Most water time owed to the water while the server cannot keep up, seconds: past it, time is given up.
    private const float MostOwed = 0.5f;

    private const float TerrainRefreshSeconds = 2f;

    // The most of a frame packing bricks to send may take, milliseconds (a brick at least).
    private const double SendBudget = 2.0;

    // The debug map draws one character per this many metres.
    private const float MapMetres = 2f;

    // Where each frame's time goes, told in the debug log.
    private readonly FrameParts _parts = new();

    private readonly ZoneTerrain _terrain;

    private readonly SolidsWatch _watch;

    private readonly BrickObjects<ZDOID> _objects;

    private readonly MarkerScan _markers;

    private readonly List<ZDO> _zdos = [];

    // The voxels a drain takes from, the list kept from call to call (a drain looks every frame, most often finding
    // none) but emptied after each: it holds no brick a removed drain or a grid made anew would leave reachable.
    private readonly List<(Brick Brick, int Index)> _drained = [];

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

    private WorldLoad _load;

    private float _sendTimer;

    private float _terrainTimer;

    private float _debugTimer;

    private bool _loaded;

    // The game's sending out of the objects destroyed (see IBrickWorld.Flush), found by name.
    private readonly LateDestroys _lateDestroys;

    private bool _capWarned;

    private bool _slowWarned;

    private bool _notNumbersWarned;

    private float _droppedInAll;

    // The points water was poured at that had no room, each told of once.
    private readonly NoRoom<Vector3> _noRoom = new(FlowingWaterPlugin.Log.LogWarning);

    // A voxel size refused because some water would have found no room (0: none), not tried again until it changes.
    private float _refusedSize;

    /// <summary>A server with an empty grid; the world's water is read on the first update.</summary>
    /// <param name="rightsFile">Where the sources and drains the server allowed are kept (<see cref="MarkerScan"/>).</param>
    public VoxelServer(string rightsFile)
    {
        _terrain = new ZoneTerrain(_parts);
        Solids = new GameSolids(_terrain, _parts);
        _watch = new SolidsWatch(Solids, _terrain);
        _timeLeft = TimeLeft;
        _sendTimeLeft = () => _sendClock.Elapsed.TotalMilliseconds < SendBudget;
        _markers = new MarkerScan(rightsFile, _watch.Pinned, _terrain.HeightAt);
        _objects = new BrickObjects<ZDOID>(this);
        Action<ZDOMan> sendDestroyed = LateDestroys.Find<Action<ZDOMan>>(typeof(ZDOMan), "SendDestroyed", out string missing);
        _lateDestroys = new LateDestroys(sendDestroyed == null ? null : () => sendDestroyed(ZDOMan.instance), missing, FlowingWaterPlugin.Log.LogError);
        NewGrid(SettingVoxelSize());
    }

    /// <summary>What the water sees as solid: the ground and the pieces, as read so far.</summary>
    public GameSolids Solids { get; }

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
        if (_markers.Update(dt, _parts))
        {
            _noRoom.Pouring(point => _markers.Markers.Any(m => !m.Drain && PourPoint(m) == point));
        }

        _parts.Into(WaterPart.Terrain);
        _terrainTimer += dt;
        if (_terrainTimer >= TerrainRefreshSeconds)
        {
            _terrainTimer = 0f;
            _terrain.Refresh(_watch.GroundChanged);
        }

        _parts.Into(WaterPart.Other);
        dt = Mathf.Min(dt, LongestFrame);

        // The solids watch and forgetting dry bricks wait for a step under way (nothing else changes the grid during
        // one), their time with them: they have their turn between steps.
        _waited += dt;
        _grid.MaxBricks = FlowingWaterPlugin.Settings.MaxBricks.Value;
        _solver.SeaLevel = ZoneSystem.instance.m_waterLevel;
        Step(dt);
        WarnCapped();
        WarnNotNumbers();

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
        foreach (Source marker in _markers.Markers)
        {
            WaterPart was = _parts.Into(marker.Drain ? WaterPart.Drain : WaterPart.Pour);
            acted |= marker.Drain ? Drain(marker, dt) : Pour(marker, dt);
            _parts.Into(was);
        }

        return acted;
    }

    private bool TimeLeft() => _pacer.Left((float)_frameWatch.Elapsed.TotalMilliseconds) > 0f;

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
            Ended(_solver.Advance(_length, static () => true, int.MaxValue));
        }
    }

    // The water's steps this frame: as many as the frame owes, a part at a time while the budget's credit lasts. What
    // waited for a step under way has its turn once that step is done, whatever the credit (a frame over its budget
    // must not keep a wall just built from the water), and again between two steps of the frame, so that the frame's
    // credit is not given up with a step's end (it was, and the water fell behind the budget with every step that ran
    // over a frame's end).
    private void Step(float dt)
    {
        float budget = FlowingWaterPlugin.Settings.StepBudget.Value;
        _pacer.Add(dt, budget);
        _stepWatch.Start();
        _frameWatch.Restart();
        if (!_solver.InStep && _waited > 0f)
        {
            BetweenSteps();
        }

        int chunk = Math.Max(Chunk, _grid.Bricks.Count / Environment.ProcessorCount);
        while (TimeLeft())
        {
            if (!_solver.InStep)
            {
                if (_waited > 0f)
                {
                    BetweenSteps();
                }

                float step = _solver.MaxStep;
                if (!_pacer.Take(step))
                {
                    break;
                }

                _length = step;
            }

            Ended(_solver.Advance(_length, _timeLeft, chunk));
        }

        float spent = (float)_frameWatch.Elapsed.TotalMilliseconds;
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

    // With no step under way: the solids' changes (pieces built or broken, the ground changed) and the dry bricks
    // forgotten, for the time they waited.
    private void BetweenSteps()
    {
        _parts.Into(WaterPart.Watch);
        _refilled += _watch.Update(_waited, _grid);
        _parts.Into(WaterPart.ForgetDry);
        _grid.ForgetDry(_waited, FlowingWaterPlugin.Settings.EmptyTileSeconds.Value);
        _parts.Into(WaterPart.Other);
        _waited = 0f;
    }

    /// <summary>
    /// Sends every changed brick now, all as they are at once and exactly (the world is about to be saved: what it keeps
    /// is the water the server finds at its next start), and forgets dry ones;
    /// a send spread over frames under way is given up for it.
    /// </summary>
    public void Publish()
    {
        FinishStep();
        _sendWatch.Start();
        _sentCount += _objects.Publish(_grid.Bricks, _grid.VoxelSize, exact: true);
        _sendWatch.Stop();
    }

    /// <summary>
    /// All the mod's water gone from the world, and every placed source and drain with it: the grid empty, the bricks'
    /// objects destroyed (and any old column tile), the markers' objects destroyed and forgotten at once (the config's
    /// fixed sources and drains stay, and pour again). The players' games see the objects go as the game sends their
    /// destruction out.
    /// </summary>
    /// <returns>The cubic metres gone, and the sources and drains removed.</returns>
    public (float Water, int Markers) Wipe()
    {
        FinishStep();
        float water = _grid.TotalWater();
        int markers = 0;
        foreach (string prefab in new[] { Prefabs.SourceName, Prefabs.DrainName })
        {
            foreach (ZDO zdo in All(prefab))
            {
                DestroyZdo(zdo);
                markers++;
            }
        }

        foreach (ZDO zdo in All(Prefabs.TileName))
        {
            DestroyZdo(zdo);
        }

        _objects.Drop();
        foreach (ZDO zdo in All(Prefabs.BrickName))
        {
            DestroyZdo(zdo);
        }

        _drained.Clear();
        NewGrid(_grid.VoxelSize);
        _markers.DropPlaced();
        FlowingWaterPlugin.Log.LogInfo(FormattableString.Invariant($"Water: wiped, {water:0.0} m³ and {markers} sources and drains gone"));
        return (water, markers);
    }

    /// <summary>Stops listening to the world (it closes).</summary>
    public void Close() => _watch.Close();

    private static float SettingVoxelSize() => (float)TileData.ZoneMetres / TileData.SizeFor(FlowingWaterPlugin.Settings.CellSize.Value);

    private void NewGrid(float voxelSize)
    {
        _grid = new VoxelGrid(voxelSize, Solids) { MaxBricks = FlowingWaterPlugin.Settings.MaxBricks.Value };
        _solver = new VoxelSolver(_grid, _parts);
        _load = new WorldLoad(_grid);
    }

    // A ZDO kept from frame to frame may be someone else's by now: the game hands a destroyed object's ZDO out again.
    // Its id is not: the game never gives out an id twice, so a destroyed object's id finds none. Its prefab is looked
    // at too, should anything have made the object something else.
    bool IBrickWorld<ZDOID>.IsBrick(ZDOID id) => ZDOMan.instance.GetZDO(id) is { } zdo && zdo.GetPrefab() == Prefabs.BrickHash;

    // The game destroys an object only once it sends the objects destroyed out, once a frame (the server's own at once):
    // a save taken in between keeps them. Sent out now, before the save takes the world's objects. Not found, or of
    // another shape (the game changed), said once (LateDestroys): the objects destroyed just before a save may then be
    // read back at the next start.
    void IBrickWorld<ZDOID>.Flush() => _lateDestroys.Flush();

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

    // Each saved brick's water into the grid. A brick's object of the grid's own voxel size, whose brick is in the grid,
    // is kept as that brick's (its next send writes it, unless it holds the brick as it is): made anew at every start,
    // the whole water was sent twice, and one the server could not read into the grid was left beside the one it made
    // later, drawn for ever and its water doubled at the next start. Every other object is destroyed once read: of
    // another size, unreadable, past the grid's reach, with no room for its water, or a second one at a brick's place
    // (whose water is not poured again).
    private float LoadBricks()
    {
        float total = 0f;
        var adopted = new HashSet<long>();
        foreach (ZDO zdo in All(Prefabs.BrickName))
        {
            BrickView view = Read(zdo);
            if (view != null)
            {
                bool fits = Array.IndexOf(TileData.CellSizes, view.VoxelSize) >= 0 && Math.Abs((long)view.X) <= VoxelGrid.MaxBrick && Math.Abs((long)view.Y) <= VoxelGrid.MaxBrick && Math.Abs((long)view.Z) <= VoxelGrid.MaxBrick;
                bool same = fits && Mathf.Approximately(view.VoxelSize, _grid.VoxelSize);
                long key = same ? VoxelGrid.Key(view.X, view.Y, view.Z) : 0L;
                if (!fits)
                {
                    FlowingWaterPlugin.Log.LogWarning(FormattableString.Invariant($"Water brick {zdo.m_uid}: brick {view.X},{view.Y},{view.Z} of {view.VoxelSize} m voxels cannot be read into the grid; removed, its water is lost"));
                }
                else if (same && adopted.Contains(key))
                {
                    FlowingWaterPlugin.Log.LogWarning(FormattableString.Invariant($"Water brick {zdo.m_uid}: another object holds the brick at {view.X},{view.Y},{view.Z} already; removed, its water not poured again"));
                }
                else if (_load.PourView(view) is { } poured)
                {
                    total += poured;
                }
                else
                {
                    FlowingWaterPlugin.Log.LogWarning(FormattableString.Invariant($"Water brick {zdo.m_uid}: some of its water finds no room in the grid (solid all the way up, or MaxBricks reached); removed, its water is lost"));
                }

                if (same && _grid.Find(view.X, view.Y, view.Z) is { } brick && adopted.Add(key))
                {
                    _objects.Adopt(key, zdo.m_uid, brick, zdo.GetByteArray(ZdoKeys.Brick), _grid.VoxelSize);
                    continue;
                }
            }

            DestroyZdo(zdo);
        }

        return total;
    }

    private static BrickView Read(ZDO zdo)
    {
        byte[] data = zdo.GetByteArray(ZdoKeys.Brick);
        if (data == null)
        {
            FlowingWaterPlugin.Log.LogInfo($"Water brick {zdo.m_uid}: empty, removed");
            return null;
        }

        try
        {
            return BrickData.Decode(data);
        }
        catch (InvalidDataException e)
        {
            FlowingWaterPlugin.Log.LogWarning($"Water brick {zdo.m_uid}: unreadable, removed; its water is lost ({e.Message})");
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
                if (_load.PourAll(water) is not { } poured)
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
    private List<(Vec3 Point, float Volume)> TileWater(ZDO zdo, byte[] bytes)
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
        return WorldLoad.TileWater(depths, side, TileData.OriginX(zone), TileData.OriginZ(zone), TileData.ZoneMetres, _terrain.HeightAt);
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
        WorldLoad oldLoad = _load;
        NewGrid(size);
        float total = 0f, lost = 0f;
        foreach (BrickView view in views)
        {
            if (_load.PourView(view) is { } poured)
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
            _load = oldLoad;
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
        if (_grid.Room(point.x, point.y, point.z, out int x, out int y, out int z))
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

    private bool Pour(Source source, float dt) => PourAt(PourPoint(source), source.Rate * dt);

    // A source pours a little over what it stands on, so its water starts on that and not in it or under it.
    private static Vector3 PourPoint(Source source) => source.Point + new Vector3(0f, 0.3f, 0f);

    // A drain takes from every voxel within its reach (its own voxel at least): all of it with no rate, else at most
    // its rate, from each in proportion to its water. Only bricks there are looked in. False when it took nothing.
    private bool Drain(Source drain, float dt)
    {
        try
        {
            return DrainFrom(drain, dt);
        }
        finally
        {
            _drained.Clear();
        }
    }

    private bool DrainFrom(Source drain, float dt)
    {
        float s = _grid.VoxelSize;
        int reach = Mathf.FloorToInt(drain.Radius / s);
        int cx = Mathf.FloorToInt(drain.Point.x / s);
        int cy = Mathf.FloorToInt((drain.Point.y + 0.05f) / s);
        int cz = Mathf.FloorToInt(drain.Point.z / s);
        float there = 0f;

        // The voxels within its reach, by the bricks holding them (looked up; a drain of the default reach has eight at
        // most round it, however many bricks the world has).
        for (int bx = Brick.Of(cx - reach); bx <= Brick.Of(cx + reach); bx++)
        {
            for (int by = Brick.Of(cy - reach); by <= Brick.Of(cy + reach); by++)
            {
                for (int bz = Brick.Of(cz - reach); bz <= Brick.Of(cz + reach); bz++)
                {
                    if (!VoxelGrid.Within(bx, by, bz) || _grid.Find(bx, by, bz) is not { } brick)
                    {
                        continue;
                    }

                    int x0 = Math.Max(bx * Brick.Size, cx - reach), x1 = Math.Min((bx * Brick.Size) + Brick.Size - 1, cx + reach);
                    int y0 = Math.Max(by * Brick.Size, cy - reach), y1 = Math.Min((by * Brick.Size) + Brick.Size - 1, cy + reach);
                    int z0 = Math.Max(bz * Brick.Size, cz - reach), z1 = Math.Min((bz * Brick.Size) + Brick.Size - 1, cz + reach);
                    for (int y = y0; y <= y1; y++)
                    {
                        for (int z = z0; z <= z1; z++)
                        {
                            for (int x = x0; x <= x1; x++)
                            {
                                int dx = x - cx, dy = y - cy, dz = z - cz;
                                bool within = ((dx * dx) + (dy * dy) + (dz * dz)) * s * s <= drain.Radius * drain.Radius || (dx == 0 && dy == 0 && dz == 0);
                                int i = Brick.Index(Brick.In(x), Brick.In(y), Brick.In(z));
                                if (within && brick.Water[i] > 0f)
                                {
                                    _drained.Add((brick, i));
                                    there += brick.Water[i];
                                }
                            }
                        }
                    }
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
        foreach ((Brick brick, int i) in _drained)
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

    // Water that is not a number is emptied by the solver as it finds it (counted once a frame here); told of once.
    private void WarnNotNumbers()
    {
        int emptied = _solver.TakeNotNumbers();
        if (emptied > 0 && !_notNumbersWarned)
        {
            _notNumbersWarned = true;
            FlowingWaterPlugin.Log.LogWarning($"Water: {emptied} voxels held water that is not a number (a ground height that is none, or a brick saved so); they were emptied and the water round them goes on. No more such are told of");
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
        text.Append(FormattableString.Invariant($"Water: {_grid.Bricks.Count} bricks of {s} m voxels ({_solver.Awake} awake, looked over {_solver.TakeLooks()} times), {_grid.TotalWater():0.0} m³, {_markers.Markers.Count} markers; stepping took {_stepWatch.Elapsed.TotalMilliseconds:0} ms (at most {_longestFrame:0.0} ms in a frame), sending {_sendWatch.Elapsed.TotalMilliseconds:0} ms ({_sentCount} bricks sent), {_dropped:0.0} s of water time given up, {_refilled} bricks read their solids again"));
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
}
