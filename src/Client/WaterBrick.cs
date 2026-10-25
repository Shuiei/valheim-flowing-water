using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// A brick of the 3D water on a player's game: reads the brick's data when the server changes it, keeps it in
/// <see cref="VoxelWater.Field"/>, and draws the water's surface over it (<see cref="VoxelSurface"/>) with the
/// water's material. What it draws eases from what it drew to the newest data over a moment, without water too thin
/// to draw steadily (<see cref="WaterEase"/>), and is kept in <see cref="VoxelWater.Drawn"/>; when that changes, the
/// bricks round it are drawn again too (their sides and corners depend on it). The data is read on another thread,
/// and the surface built on another thread from the bricks it reads as they were drawn then; only taking the data,
/// handing the water read to the brick and the surface to the mesh are done in the game's frame, as its time allows.
/// On a dedicated server (whose scene holds the bricks round the world's zero) it only keeps the water, as the game's
/// own water volumes work there: its trigger and the game's water checks then see it for the creatures the server
/// simulates; nothing is drawn.
/// </summary>
internal sealed class WaterBrick : MonoBehaviour
{
    // Shortest time between two drawings of a brick, seconds.
    private const float RedrawSeconds = 0.1f;

    // Longest time handing read water to the bricks and drawing them may take in a frame, milliseconds: the others
    // wait for the next frame (one of each is always served), so that much water changing at once does not stall a
    // frame.
    private const double FrameBudget = 2.0;

    // Of it, the time the read water may take, first: what is left is the drawing's, so that a burst of changed bricks
    // does not hold back the drawing of those already read, nor wait behind it.
    private const double ReadBudget = FrameBudget / 2;

    // Most milliseconds of a frame the bricks may take starting their reads (each hands its data to the thread pool,
    // which a busy pool can make slow); the bricks left start theirs next frame, still reading their newest data.
    private const double StartBudget = 0.5;

    private static readonly Stopwatch StartingFrame = new();

    private static int _startingFrame = -1;

    private static double _longestStart;

    private static readonly Stopwatch FrameDrawing = new();

    // All the time the bricks took in the game's frames since the game started (reading their data, animating,
    // drawing): its cost to the player, read by the in-game recorder's measures. Started and stopped a few times a
    // frame: well under a microsecond.
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

    // Bricks to draw again, those waiting longest first: a brick whose water keeps changing does not hold back
    // the others.
    private static readonly TakeTurns<WaterBrick> Waiting = new();

    // Bricks whose surface is being built, handed to their meshes once built, those waiting longest first.
    private static readonly TakeTurns<WaterBrick> Built = new();

    // Bricks whose data is being read, handed their water once read, those waiting longest first.
    private static readonly TakeTurns<WaterBrick> Read = new();

    private static int _frame = -1;

    private static readonly Dictionary<long, WaterBrick> ByKey = [];

    private static bool _settingWatched;

    private static bool _flat;

    private static float _lift;

    // Whether a brick without its trigger was told of: once, as every brick would be.
    private static bool _triggerMissingTold;

    private ZNetView _nview;

    // Whether it is drawn: not on a dedicated server.
    private bool _drawn;

    private MeshFilter _filter;

    private MeshRenderer _renderer;

    private Mesh _mesh;

    // What the game's water material reads from each renderer (its waves' time, how far it is drawn); none with the
    // mod's own.
    private MaterialPropertyBlock _properties;

    private BrickView _view;

    // The water drawn, and what it was when the newest data came, eased from since then (none once the ease is
    // done); whether data came since the brick was last built.
    private BrickView _drawnWater;

    private BrickView _easedFrom;

    private float _easeStart;

    private bool _newData;

    // The brick's data as the server last changed it, read into its water.
    private readonly LatestRead<BrickView> _reading = new();

    private float _drawnAt;

    private readonly BackgroundBuild<Shape> _building = new();

    // With the mod's own shader the graphics card shapes the surface: built here is only the brick's grid of tops, and
    // the card blends from one to the next over the time between them (measured, for this brick).
    private readonly BackgroundBuild<SurfaceGrid> _gridBuilding = new();

    private GpuSurface _gpu;

    // On the card, the water's voxels where it meets the air too (its sides, falls), built beside its grid.
    private readonly BackgroundBuild<VoxelShell> _shellBuilding = new();

    private ShellRenderer _shell;

    private float _lastData = -1f;

    private float _dataInterval = 1f;

    /// <summary>Draw with a plain opaque colour instead of the water's material, for testing (console `flowingwater flat`).</summary>
    internal static bool Flat
    {
        get => _flat;
        set
        {
            _flat = value;
            DrawAll();
        }
    }

    /// <summary>Metres the water is drawn higher than it is, for testing (console `flowingwater lift`).</summary>
    internal static float Lift
    {
        get => _lift;
        set
        {
            _lift = value;
            DrawAll();
        }
    }

    // Every brick is drawn again with the other material when the shader setting changes.
    private static void WatchSetting()
    {
        if (_settingWatched)
        {
            return;
        }

        _settingWatched = true;
        FlowingWaterPlugin.Settings.CustomShader.SettingChanged += (_, _) => DrawAll();
    }

    private static void DrawAll()
    {
        foreach (WaterBrick brick in ByKey.Values)
        {
            if (brick != null)
            {
                Waiting.Add(brick);
            }
        }
    }

    /// <summary>
    /// All the milliseconds the bricks took in the game's frames since the game started: the water's cost to the
    /// player, a frame's being the difference over it.
    /// </summary>
    internal static double SpentMilliseconds => Spent.Elapsed.TotalMilliseconds;

    /// <summary>The parts of <see cref="SpentMilliseconds"/>: reading data, starting builds, handing meshes over.</summary>
    /// <summary>The longest a single brick took starting its read since last asked, milliseconds (for measures).</summary>
    internal static double TakeLongestStart()
    {
        double longest = _longestStart;
        _longestStart = 0.0;
        return longest;
    }

    // A brick's read started in this long, milliseconds: kept if the longest. Only the game's main thread comes here and
    // to TakeLongestStart (the bricks' Update, the recorder's measures), so a plain max is enough, nothing atomic.
    private static void NoteStart(double milliseconds) => _longestStart = Math.Max(_longestStart, milliseconds);

    internal static double[] SpentParts => [Reading.Elapsed.TotalMilliseconds, Building.Elapsed.TotalMilliseconds, Taking.Elapsed.TotalMilliseconds, Starting.Elapsed.TotalMilliseconds];

    /// <summary>The bricks this game has, and of them those drawn with a surface and its vertices, for measures.</summary>
    internal static int Census(out int drawn, out int vertices)
    {
        drawn = 0;
        vertices = 0;
        int bricks = 0;
        foreach (WaterBrick brick in ByKey.Values)
        {
            if (brick == null)
            {
                continue;
            }

            bricks++;
            Mesh drawnMesh = brick._filter.sharedMesh;
            if (drawnMesh != null && drawnMesh.vertexCount > 0 && brick._renderer.enabled)
            {
                drawn++;
                vertices += drawnMesh.vertexCount;
            }
        }

        return bricks;
    }

    /// <summary>What the brick at a point draws (else the nearest in the column there), for the console.</summary>
    public static string DebugInfo(Vector3 point)
    {
        WaterBrick found = BrickPick.Nearest(ByKey.Values.Where(b => b != null && b._view != null), b => b._view, point.x, point.y, point.z, out float nearest);
        if (found == null)
        {
            return "no water brick in the column here";
        }

        BrickView view = found._view;
        Mesh mesh = found._filter.sharedMesh;
        var text = new System.Text.StringBuilder();
        text.Append(FormattableString.Invariant($"brick {view.X},{view.Y},{view.Z} of {view.VoxelSize} m voxels at {found.transform.position}, {nearest:0.#} m from you: renderer {found._renderer.enabled}, lift {Lift}, flat {Flat}"));
        text.Append(FormattableString.Invariant($"; mesh {mesh.vertexCount} vertices, {(mesh.subMeshCount > 0 ? mesh.GetIndexCount(0) / 3 : 0)} triangles, bounds {mesh.bounds.min} to {mesh.bounds.max}; drawn {Time.time - found._drawnAt:0.0} s ago, building {found._building.Busy || found._gridBuilding.Busy}, shaped on the card {found._gpu != null && mesh != found._mesh}"));
        Material material = found._renderer.sharedMaterial;
        text.Append(material == null
            ? "; material none"
            : FormattableString.Invariant($"; material {material.name} shader {material.shader.name} supported {material.shader.isSupported} queue {material.renderQueue} keywords {string.Join(" ", material.shaderKeywords)}"));
        return text.ToString();
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Awake()
    {
        WatchSetting();
        _nview = GetComponent<ZNetView>();
        _filter = GetComponent<MeshFilter>();
        _renderer = GetComponent<MeshRenderer>();
        if (_nview == null || !_nview.IsValid())
        {
            enabled = false;
            return;
        }

        if (GetComponentInChildren<BrickTrigger>(true) == null)
        {
            TriggerMissing(transform.position);
        }

        _drawn = ZNet.instance == null || !ZNet.instance.IsDedicated();
        if (!_drawn)
        {
            return;
        }

        _mesh = new Mesh { name = "FlowingWater brick" };
        _mesh.MarkDynamic();
        _filter.sharedMesh = _mesh;
    }

    // A brick came without its trigger: told once, as every brick would (Unity wakes them on the game's thread only).
    private static void TriggerMissing(Vector3 at)
    {
        if (_triggerMissingTold)
        {
            return;
        }

        _triggerMissingTold = true;
        FlowingWaterPlugin.Log.LogError($"Water brick at {at} came without its trigger: no one can swim or float in the flowing water");
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Update()
    {
        Spent.Start();
        try
        {
            ZDO zdo = _nview.GetZDO();
            if (zdo != null && _reading.Wants(zdo.DataRevision) && MayStart())
            {
                Reading.Start();
                Starting.Start();
                StartingFrame.Start();
                long began = Stopwatch.GetTimestamp();
                try
                {
                    StartReading(zdo.GetByteArray(ZdoKeys.Brick));
                }
                finally
                {
                    NoteStart((Stopwatch.GetTimestamp() - began) * 1000.0 / Stopwatch.Frequency);
                    StartingFrame.Stop();
                    Starting.Stop();
                    Reading.Stop();
                }
            }

            // Its place in the queue is kept: the queue may have been let go (the last water gone) meanwhile.
            if (_reading.Busy)
            {
                Read.Add(this);
            }

            if (_properties != null)
            {
                Materials.Animate(_renderer, _properties);
            }
        }
        finally
        {
            Spent.Stop();
        }
    }

    // After every brick has taken its data this frame.
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void LateUpdate() => ServeFrame(_drawn);

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        // A brick made again at the same place (dry, then wet again) may have come before this one went: its water
        // and its place in the lookup are its own, left alone.
        long key = _view == null ? 0L : VoxelGrid.Key(_view.X, _view.Y, _view.Z);
        if (_view != null && ByKey.TryGetValue(key, out WaterBrick current) && current == this)
        {
            VoxelWater.Field.Remove(_view.X, _view.Y, _view.Z);
            VoxelWater.Drawn.Remove(_view.X, _view.Y, _view.Z);
            ByKey.Remove(key);
            if (_drawn)
            {
                Neighbours(_view, OnCard() ? CardReaders : VoxelSurface.Round);
            }
        }

        // The last water gone (the world left): no brick is left to serve the queue, which would keep the bricks
        // gone with it.
        if (ByKey.Count == 0)
        {
            Waiting.Clear();
            Built.Clear();
            Read.Clear();
        }

        if (_mesh != null)
        {
            Destroy(_mesh);
        }

        _gpu?.Dispose();
        _shell?.Dispose();

        // A surface still being built is not drawn, nor data still being read kept; a failure to build or read it is
        // still told, in the game's frame (its log is the game's), from where the brick was.
        Vector3 at = transform.position;
        var frame = TaskScheduler.FromCurrentSynchronizationContext();
        _building.Abandon(failure => Failed(failure, at), frame);
        _gridBuilding.Abandon(failure => Failed(failure, at), frame);
        _shellBuilding.Abandon(failure => Failed(failure, at), frame);
        _reading.Abandon(failure => ReadFailed(failure, at), frame);
    }

    // The brick's data, taken in the frame (the game's objects are the frame's), read on another thread. The bytes are
    // the object's own, never changed in place (new data comes as new bytes); the water it had is not changed until
    // this read is handed back, as only one read runs at a time, so what changed is worked out there too.
    // Whether this frame's time for starting reads has some left (the first brick of a frame always may).
    private static bool MayStart()
    {
        if (_startingFrame != Time.frameCount)
        {
            _startingFrame = Time.frameCount;
            StartingFrame.Reset();
        }

        return StartingFrame.Elapsed.TotalMilliseconds < StartBudget;
    }

    private void StartReading(byte[] data)
    {
        _reading.Start(() => data == null ? null : BrickData.Decode(data));
        Read.Add(this);
    }

    // The water read, handed to the brick: kept, and drawn again (easing to it from what is drawn). A read whose data
    // changed again meanwhile is dropped (the newer one is read next).
    private void TakeRead()
    {
        if (!_reading.TryTake(out BrickView read, out Exception failure))
        {
            return;
        }

        if (failure != null)
        {
            ReadFailed(failure, transform.position);
            return;
        }

        if (read == null)
        {
            return;
        }

        _view = read;
        VoxelWater.Field.Set(_view);
        ByKey[VoxelGrid.Key(_view.X, _view.Y, _view.Z)] = this;
        if (GetComponentInChildren<BoxCollider>() is { } trigger)
        {
            trigger.size = Vector3.one * (Brick.Size * _view.VoxelSize);
        }

        if (_drawn)
        {
            _newData = true;
            Waiting.Add(this);
            if (_lastData >= 0f)
            {
                _dataInterval = Mathf.Clamp(Time.time - _lastData, 0.25f, 2f);
            }

            _lastData = Time.time;

            // The grids round it read its columns at their edges, its tops near theirs and its water under theirs.
            if (OnCard())
            {
                Neighbours(_view, CardReaders);
            }
        }
    }

    // The bricks drawing on the card whose surface or shell reads a brick's water: the grid's readers and the six beside.
    private static readonly (int X, int Y, int Z)[] CardReaders = [.. SurfaceGrid.Readers.Union(VoxelShell.Readers)];

    // Whether the graphics card shapes the surface: the mod's own shader draws it (not the game's material, nor the
    // flat colour for testing).
    private static bool OnCard() => !Flat && FlowingWaterPlugin.Settings.CustomShader.Value && Materials.Custom() != null;

    // Unreadable data is the server's (another version of the mod): told, and the brick keeps the water it had. Any
    // other failure is the mod's own.
    private static void ReadFailed(Exception failure, Vector3 at)
    {
        if (failure is InvalidDataException)
        {
            FlowingWaterPlugin.Log.LogWarning($"Water brick at {at}: data unreadable, not drawn ({failure.Message})");
        }
        else
        {
            FlowingWaterPlugin.Log.LogError($"Water brick at {at}: its data could not be read, left as it was ({failure})");
        }
    }

    // Once a frame (from the first brick's LateUpdate), while the frame's time lasts: the water read is handed to its
    // bricks (for at most ReadBudget), then, where the water is drawn, the surfaces built are handed to their meshes,
    // and the bricks waiting are built again, longest waiting first. One still reading or building, or drawn less than
    // RedrawSeconds ago, waits on; one gone is dropped.
    private static void ServeFrame(bool drawn)
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
            Read.Serve(brick => brick == null || brick._reading.Done, () => FrameDrawing.Elapsed.TotalMilliseconds < ReadBudget, brick => Timed(brick, b => b.TakeRead()));
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
            Built.Serve(brick => brick == null || brick._building.Done || brick._gridBuilding.Done || brick._shellBuilding.Done, InBudget, brick => Timed(brick, b => b.Take()));
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
                    brick => brick == null || (!brick._building.Busy && !brick._gridBuilding.Busy && !brick._shellBuilding.Busy && Time.time - brick._drawnAt >= RedrawSeconds),
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

    // These bricks round this one are drawn again: their surface reads its water.
    private static void Neighbours(BrickView view, IEnumerable<(int X, int Y, int Z)> round)
    {
        foreach ((int dx, int dy, int dz) in round)
        {
            if (ByKey.TryGetValue(VoxelGrid.Key(view.X + dx, view.Y + dy, view.Z + dz), out WaterBrick next) && next != null)
            {
                Waiting.Add(next);
            }
        }
    }

    // The surface over the brick, built on another thread from the bricks round it as they are drawn now, and its
    // water eased as far as it is by now. New data starts a new ease, from the water drawn.
    private void Build()
    {
        _drawnAt = Time.time;
        if (_view == null)
        {
            return;
        }

        if (OnCard())
        {
            // The card blends from the surface drawn to the newest: the water drawn is the newest.
            _newData = false;
            _easedFrom = null;
            _drawnWater = _view;
            VoxelWater.Drawn.Set(_view);
            VoxelField latest = VoxelWater.Field.Near(_view);
            (int x, int y, int z) = (_view.X, _view.Y, _view.Z);
            bool started = _gridBuilding.TryStart(() => SurfaceGrid.Build(latest, x, y, z));
            started |= _shellBuilding.TryStart(() => VoxelShell.Build(latest, x, y, z));
            if (started)
            {
                Built.Add(this);
            }

            return;
        }

        if (_newData)
        {
            _newData = false;
            _easedFrom = _drawnWater;
            _easeStart = Time.time;
        }

        BrickView from = _easedFrom;
        BrickView to = _view;
        BrickView drawn = _drawnWater;
        float share = from == null ? 1f : WaterEase.Share(Time.time - _easeStart);
        VoxelField near = VoxelWater.Drawn.Near(to);
        // Lifted, the surface is drawn that much higher over the object.
        Vector3 origin = transform.position - new Vector3(0f, Lift, 0f);
        if (_building.TryStart(() => Shape.Of(near, WaterEase.Show(WaterEase.Blend(from, to, share), drawn), drawn, share < 1f, origin)))
        {
            Built.Add(this);
        }
    }

    // The surface built: its water kept as drawn, the bricks round it whose surface that changes drawn again (and
    // this one, until its ease is done), and the surface handed to the mesh.
    private void Take()
    {
        TakeShell();
        TakeSurface();

        // A build still under way (one finished before it): the brick waits to be served again for it.
        if (_building.Busy || _gridBuilding.Busy || _shellBuilding.Busy)
        {
            Built.Add(this);
        }
    }

    private void TakeSurface()
    {
        if (_gridBuilding.TryTake(out SurfaceGrid grid, out Exception gridFailure))
        {
            if (gridFailure != null)
            {
                Failed(gridFailure, transform.position);
            }
            else if (OnCard())
            {
                ShowOnCard(grid);
            }
            else
            {
                // The way it is drawn changed while the grid was built: the mesh is built instead.
                Waiting.Add(this);
            }

            return;
        }

        if (!_building.TryTake(out Shape shape, out Exception failure))
        {
            return;
        }

        if (failure != null)
        {
            Failed(failure, transform.position);
            return;
        }

        if (OnCard())
        {
            // The way it is drawn changed while the mesh was built: the grid is built instead.
            Waiting.Add(this);
            return;
        }

        _drawnWater = shape.Drawn;
        VoxelWater.Drawn.Set(_drawnWater);
        Neighbours(_drawnWater, shape.Touched);
        if (shape.Easing)
        {
            Waiting.Add(this);
        }
        else
        {
            _easedFrom = null;
        }

        Apply(shape);
    }

    // A built surface handed to the mesh.
    private void Apply(Shape shape)
    {
        _shell?.Hide();
        // Plain colours (flat), else the game's material: the mod's own shader draws only the card's grid.
        Material own = Flat ? Materials.Plain("FlowingWater flat", new Color(0.1f, 0.3f, 0.9f)) : null;
        Material material = own ?? Materials.Still();
        if (material == null)
        {
            return;
        }

        UseGameProperties(own == null);
        _filter.sharedMesh = _mesh;
        _renderer.enabled = true;
        _mesh.Clear();
        _mesh.indexFormat = shape.Vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        _mesh.SetVertices(shape.Vertices);
        _mesh.SetUVs(0, shape.World);
        _mesh.SetUVs(1, shape.Water);
        _mesh.SetUVs(2, shape.Current);
        _mesh.SetNormals(shape.Normals);
        _mesh.SetTangents(shape.Tangents);
        _mesh.SetTriangles(shape.Triangles, 0, false);
        _mesh.bounds = shape.Bounds;
        _renderer.sharedMaterial = material;
    }

    // The water's voxels where it meets the air, built: drawn on the card, else not (the mesh draws its own sides).
    private void TakeShell()
    {
        if (!_shellBuilding.TryTake(out VoxelShell shell, out Exception failure))
        {
            return;
        }

        if (failure != null)
        {
            Failed(failure, transform.position);
            return;
        }

        Material material = OnCard() ? Materials.Shell() : null;
        if (material == null)
        {
            _shell?.Hide();
            return;
        }

        _shell ??= new ShellRenderer(transform);
        _shell.Show(shell, material, Lift);
    }

    // A grid built, handed to the card: the shared grid drawn with the mod's own shader, the brick's textures blending
    // to it from what is drawn now. A brick with no top of its own to show draws nothing.
    private void ShowOnCard(SurfaceGrid grid)
    {
        Material material = Materials.Custom();
        if (material == null)
        {
            return;
        }

        UseGameProperties(false);
        _gpu ??= new GpuSurface();
        _gpu.Show(grid, Time.timeSinceLevelLoad, _dataInterval, Lift);
        _filter.sharedMesh = GpuSurface.Grid(grid.VoxelSize);
        _renderer.sharedMaterial = material;
        _renderer.SetPropertyBlock(_gpu.Properties);
        _renderer.enabled = _gpu.Draws;
    }

    // The game's material is animated and drawn only as far as each renderer says, as the game's own water does; the
    // mod's own reads nothing from the renderer.
    private void UseGameProperties(bool game)
    {
        if (game)
        {
            _properties ??= Materials.GameMaterialProperties();
        }
        else if (_properties != null)
        {
            _properties = null;
            _renderer.SetPropertyBlock(null);
        }
    }

    private static void Failed(Exception failure, Vector3 at) =>
        FlowingWaterPlugin.Log.LogError($"Water brick at {at}: its surface could not be built, left as it was ({failure})");

    // A surface as the mesh takes it: positions from the brick's origin, and what each vertex carries. Its normals,
    // tangents and bounds are worked out with it, away from the game's frame, as the mesh would.
    private sealed class Shape
    {
        public List<Vector3> Vertices { get; } = [];

        public List<Vector3> Normals { get; } = [];

        public List<Vector4> Tangents { get; } = [];

        public Bounds Bounds { get; private set; }

        public List<Vector2> World { get; } = [];

        public List<Vector4> Water { get; } = [];

        public List<Vector2> Current { get; } = [];

        public List<int> Triangles { get; private set; } = [];

        // The water it was built from, as drawn.
        public BrickView Drawn { get; private set; }

        // The bricks round it whose surface changes with it, from what was drawn before.
        public ICollection<(int X, int Y, int Z)> Touched { get; private set; }

        // Whether the water is still easing to the newest data.
        public bool Easing { get; private set; }

        // The surface of the water drawn, over the bricks round it as drawn (that field is this build's own).
        public static Shape Of(VoxelField field, BrickView water, BrickView before, bool easing, Vector3 origin)
        {
            field.Set(water);
            SurfaceMesh surface = VoxelSurface.Build(field, water);
            var shape = new Shape
            {
                Triangles = surface.Triangles,
                Drawn = water,
                Touched = VoxelSurface.Touched(before, water),
                Easing = easing,
            };
            var uvs = new (float U, float V)[surface.Vertices.Count];
            for (int i = 0; i < surface.Vertices.Count; i++)
            {
                Vec3 v = surface.Vertices[i];
                shape.Vertices.Add(new Vector3(v.X, v.Y, v.Z) - origin);

                // Ripples on the sides run up them: their texture shifts with height.
                uvs[i] = (v.X + (v.Y * 0.5f), v.Z + (v.Y * 0.5f));
                shape.World.Add(new Vector2(uvs[i].U, uvs[i].V));
                shape.Water.Add(new Vector4(surface.Depths[i], 0f, surface.Shores[i], 0f));
                shape.Current.Add(new Vector2(surface.Velocities[i].X, surface.Velocities[i].Z));
            }

            Vec3[] normals = MeshFrames.Normals(surface.Vertices, surface.Triangles);
            (Vec3 Tangent, float W)[] tangents = MeshFrames.Tangents(surface.Vertices, surface.Triangles, normals, uvs);
            for (int i = 0; i < normals.Length; i++)
            {
                shape.Normals.Add(new Vector3(normals[i].X, normals[i].Y, normals[i].Z));
                shape.Tangents.Add(new Vector4(tangents[i].Tangent.X, tangents[i].Tangent.Y, tangents[i].Tangent.Z, tangents[i].W));
            }

            // The bounds of the positions as drawn (from the brick's origin); none with no surface.
            if (surface.Vertices.Count > 0)
            {
                (Vec3 low, Vec3 high) = MeshFrames.Bounds(surface.Vertices);
                var bounds = new Bounds();
                bounds.SetMinMax(new Vector3(low.X, low.Y, low.Z) - origin, new Vector3(high.X, high.Y, high.Z) - origin);
                shape.Bounds = bounds;
            }

            return shape;
        }
    }
}
