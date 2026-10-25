using System;
using System.Collections.Generic;
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
/// handing the water read to the brick and the surface to the mesh are done in the game's frame, as its time allows
/// (<see cref="BrickFrame"/>). On a dedicated server (whose scene holds the bricks round the world's zero) it only
/// keeps the water, as the game's own water volumes work there: its trigger and the game's water checks then see it
/// for the creatures the server simulates; nothing is drawn.
/// </summary>
internal sealed class WaterBrick : MonoBehaviour
{
    private static readonly Dictionary<long, WaterBrick> ByKey = [];

    // The bricks drawing on the card whose surface or shell reads a brick's water: the grid's readers and the six beside.
    private static readonly (int X, int Y, int Z)[] CardReaders = [.. SurfaceGrid.Readers.Union(VoxelShell.Readers)];

    // When the bricks' blends on the card began and how long they take: one clock, so bricks side by side move as one.
    // On the game's clock since the world's scene loaded (Time.timeSinceLevelLoad), which starts again at each world.
    private static readonly BlendClock Blends = new();

    private static bool _settingWatched;

    private static bool _flat;

    private static float _lift;

    // Whether a brick without its trigger, and a brick with data this version cannot read, were told of: once, as every
    // brick would be.
    private static bool _triggerMissingTold;

    private static bool _unreadableTold;

    private ZNetView _nview;

    // Whether it is drawn: not on a dedicated server.
    private bool _drawn;

    private Mesh _mesh;

    // The trigger box the game's water checks find, sized to the brick's voxels when they are known.
    private BoxCollider _trigger;

    private float _triggerVoxelSize;

    // What the game's water material reads from each renderer (its waves' time, how far it is drawn); none with the
    // mod's own.
    private MaterialPropertyBlock _properties;

    // The water drawn, and what it was when the newest data came, eased from since then (none once the ease is
    // done); whether data came since the brick was last built.
    private BrickView _drawnWater;

    private BrickView _easedFrom;

    private float _easeStart;

    private bool _newData;

    // The brick's data as the server last changed it, read into its water.
    private readonly LatestRead<BrickView> _reading = new();

    private readonly BackgroundBuild<SurfaceShape> _building = new();

    // With the mod's own shader the graphics card shapes the surface: built here is only the brick's grid of tops, and
    // the card blends from one to the next over the bricks' shared clock.
    private readonly BackgroundBuild<SurfaceGrid> _gridBuilding = new();

    private GpuSurface _gpu;

    // On the card, the water's voxels where it meets the air too (its sides, falls), built beside its grid.
    private readonly BackgroundBuild<VoxelShell> _shellBuilding = new();

    private ShellRenderer _shell;

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

    /// <summary>The bricks this game has (some may be gone, destroyed: null).</summary>
    internal static IEnumerable<WaterBrick> All => ByKey.Values;

    /// <summary>A world opens: its scene's clock starts from nothing, and so does the bricks' blend.</summary>
    internal static void WorldOpened() => Blends.Reset();

    /// <summary>The brick's data as last read; null before the first.</summary>
    internal BrickView View { get; private set; }

    internal MeshFilter Filter { get; private set; }

    internal MeshRenderer Renderer { get; private set; }

    /// <summary>When it was last built (drawn), seconds of the game's time.</summary>
    internal float DrawnAt { get; private set; }

    /// <summary>Whether its data read is done and waits to be taken.</summary>
    internal bool ReadDone => _reading.Done;

    /// <summary>Whether a surface, grid or shell build of its is done and waits to be taken.</summary>
    internal bool AnyBuildDone => _building.Done || _gridBuilding.Done || _shellBuilding.Done;

    /// <summary>Whether any build of its is under way or not taken yet.</summary>
    internal bool AnyBuildBusy => _building.Busy || _gridBuilding.Busy || _shellBuilding.Busy;

    /// <summary>Whether the graphics card shapes what it draws now (the shared grid, not its own mesh).</summary>
    internal bool ShapedOnCard => _gpu != null && Filter.sharedMesh != _mesh;

    // Whether this brick is the one at its place: a brick made again at the same place (dry, then wet again) may have
    // come before this one went, and then the water and the drawing there are its, not this one's.
    private bool Current => View != null && ByKey.TryGetValue(VoxelGrid.Key(View.X, View.Y, View.Z), out WaterBrick current) && current == this;

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
                BrickFrame.Waiting.Add(brick);
            }
        }
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Awake()
    {
        WatchSetting();
        _nview = GetComponent<ZNetView>();
        Filter = GetComponent<MeshFilter>();
        Renderer = GetComponent<MeshRenderer>();
        if (_nview == null || !_nview.IsValid())
        {
            enabled = false;
            return;
        }

        BrickTrigger trigger = GetComponentInChildren<BrickTrigger>(true);
        if (trigger == null)
        {
            TriggerMissing(transform.position);
        }
        else
        {
            _trigger = trigger.GetComponent<BoxCollider>();
        }

        _drawn = ZNet.instance == null || !ZNet.instance.IsDedicated();
        if (!_drawn)
        {
            return;
        }

        _mesh = new Mesh { name = "FlowingWater brick" };
        _mesh.MarkDynamic();
        Filter.sharedMesh = _mesh;
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
    private void Update() => BrickFrame.Count(Tick);

    private void Tick()
    {
        ZDO zdo = _nview.GetZDO();
        if (zdo != null && _reading.Wants(zdo.DataRevision) && BrickFrame.MayStart())
        {
            byte[] data = zdo.GetByteArray(ZdoKeys.Brick);
            BrickFrame.StartRead(() => StartReading(data));
        }

        // Its place in the queue is kept: the queue may have been let go (the last water gone) meanwhile.
        if (_reading.Busy)
        {
            BrickFrame.Read.Add(this);
        }

        if (_properties != null)
        {
            Materials.Animate(Renderer, _properties);
        }
    }

    // After every brick has taken its data this frame.
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void LateUpdate() => BrickFrame.Serve(_drawn);

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        // A brick made again at the same place may have come before this one went: its water and its place in the
        // lookup are its own, left alone.
        if (Current)
        {
            VoxelWater.Field.Remove(View.X, View.Y, View.Z);
            VoxelWater.Drawn.Remove(View.X, View.Y, View.Z);
            ByKey.Remove(VoxelGrid.Key(View.X, View.Y, View.Z));
            if (_drawn)
            {
                Neighbours(View, OnCard() ? CardReaders : VoxelSurface.Round);
            }
        }

        // The last water gone (the world left): no brick is left to serve the queue, which would keep the bricks
        // gone with it.
        if (ByKey.Count == 0)
        {
            BrickFrame.Waiting.Clear();
            BrickFrame.Built.Clear();
            BrickFrame.Read.Clear();
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
    private void StartReading(byte[] data)
    {
        _reading.Start(() => data == null ? null : BrickData.Decode(data));
        BrickFrame.Read.Add(this);
    }

    /// <summary>
    /// The water read, handed to the brick: kept, and drawn again (easing to it from what is drawn). A read whose data
    /// changed again meanwhile is dropped (the newer one is read next).
    /// </summary>
    internal void TakeRead()
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

        View = read;
        VoxelWater.Field.Set(View);
        ByKey[VoxelGrid.Key(View.X, View.Y, View.Z)] = this;
        if (_trigger != null && !_triggerVoxelSize.Equals(View.VoxelSize))
        {
            _triggerVoxelSize = View.VoxelSize;
            _trigger.size = Vector3.one * (Brick.Size * View.VoxelSize);
        }

        if (_drawn)
        {
            _newData = true;
            BrickFrame.Waiting.Add(this);
            Blends.Came(Time.timeSinceLevelLoad);

            // The grids round it read its columns at their edges, its tops near theirs and its water under theirs.
            if (OnCard())
            {
                Neighbours(View, CardReaders);
            }
        }
    }

    // Whether the graphics card shapes the surface: the mod's own shader draws it (not the game's material, nor the
    // flat colour for testing).
    private static bool OnCard() => !Flat && FlowingWaterPlugin.Settings.CustomShader.Value && Materials.Custom() != null;

    // Unreadable data is the server's (another version of the mod): told once, as every brick's would be, and the brick
    // keeps the water it had. Any other failure is the mod's own.
    private static void ReadFailed(Exception failure, Vector3 at)
    {
        if (failure is InvalidDataException)
        {
            if (!_unreadableTold)
            {
                _unreadableTold = true;
                FlowingWaterPlugin.Log.LogWarning($"Water brick at {at}: data unreadable, not drawn ({failure.Message}); the server runs another version of FlowingWater, whose water this one cannot draw (told once)");
            }
        }
        else
        {
            FlowingWaterPlugin.Log.LogError($"Water brick at {at}: its data could not be read, left as it was ({failure})");
        }
    }

    // These bricks round this one are drawn again: their surface reads its water.
    private static void Neighbours(BrickView view, IEnumerable<(int X, int Y, int Z)> round)
    {
        foreach ((int dx, int dy, int dz) in round)
        {
            if (ByKey.TryGetValue(VoxelGrid.Key(view.X + dx, view.Y + dy, view.Z + dz), out WaterBrick next) && next != null)
            {
                BrickFrame.Waiting.Add(next);
            }
        }
    }

    /// <summary>
    /// The surface over the brick, built on another thread from the bricks round it as they are drawn now, and its
    /// water eased as far as it is by now. New data starts a new ease, from the water drawn. A brick no longer the one
    /// at its place draws nothing more.
    /// </summary>
    internal void Build()
    {
        DrawnAt = Time.time;
        if (!Current)
        {
            return;
        }

        if (OnCard())
        {
            // The card blends from the surface drawn to the newest: the water drawn is the newest.
            _newData = false;
            _easedFrom = null;
            _drawnWater = View;
            VoxelWater.Drawn.Set(View);
            VoxelField latest = VoxelWater.Field.Near(View);
            (int x, int y, int z) = (View.X, View.Y, View.Z);
            bool started = _gridBuilding.TryStart(() => SurfaceGrid.Build(latest, x, y, z));
            started |= _shellBuilding.TryStart(() => VoxelShell.Build(latest, x, y, z));
            if (started)
            {
                BrickFrame.Built.Add(this);
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
        BrickView to = View;
        BrickView drawn = _drawnWater;
        float share = from == null ? 1f : WaterEase.Share(Time.time - _easeStart);
        VoxelField near = VoxelWater.Drawn.Near(to);
        // Lifted, the surface is drawn that much higher over the object.
        Vector3 origin = transform.position - new Vector3(0f, Lift, 0f);
        if (_building.TryStart(() => SurfaceShape.Of(near, WaterEase.Show(WaterEase.Blend(from, to, share), drawn), drawn, share < 1f, origin)))
        {
            BrickFrame.Built.Add(this);
        }
    }

    /// <summary>
    /// The surface built: its water kept as drawn, the bricks round it whose surface that changes drawn again (and
    /// this one, until its ease is done), and the surface handed to the mesh.
    /// </summary>
    internal void Take()
    {
        TakeShell();
        TakeSurface();

        // A build still under way (one finished before it): the brick waits to be served again for it.
        if (AnyBuildBusy)
        {
            BrickFrame.Built.Add(this);
        }
    }

    // What a build made, once done: false while it runs, when none was started, and when it failed (told, from where
    // the brick is).
    private bool Took<T>(BackgroundBuild<T> build, out T result)
    {
        if (!build.TryTake(out result, out Exception failure))
        {
            return false;
        }

        if (failure != null)
        {
            Failed(failure, transform.position);
            return false;
        }

        return true;
    }

    private void TakeSurface()
    {
        if (_gridBuilding.Done)
        {
            if (!Took(_gridBuilding, out SurfaceGrid grid) || !Current)
            {
                return;
            }

            if (OnCard())
            {
                ShowOnCard(grid);
            }
            else
            {
                // The way it is drawn changed while the grid was built: the mesh is built instead.
                BrickFrame.Waiting.Add(this);
            }

            return;
        }

        if (!Took(_building, out SurfaceShape shape) || !Current)
        {
            return;
        }

        if (OnCard())
        {
            // The way it is drawn changed while the mesh was built: the grid is built instead.
            BrickFrame.Waiting.Add(this);
            return;
        }

        _drawnWater = shape.Drawn;
        VoxelWater.Drawn.Set(_drawnWater);
        Neighbours(_drawnWater, shape.Touched);
        if (shape.Easing)
        {
            BrickFrame.Waiting.Add(this);
        }
        else
        {
            _easedFrom = null;
        }

        Apply(shape);
    }

    // A built surface handed to the mesh.
    private void Apply(SurfaceShape shape)
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
        Filter.sharedMesh = _mesh;
        Renderer.enabled = true;
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
        Renderer.sharedMaterial = material;
    }

    // The water's voxels where it meets the air, built: drawn on the card, else not (the mesh draws its own sides).
    private void TakeShell()
    {
        if (!Took(_shellBuilding, out VoxelShell shell) || !Current)
        {
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
    // to it from what is drawn now, on the bricks' shared clock. A brick with no top of its own to show draws nothing.
    private void ShowOnCard(SurfaceGrid grid)
    {
        Material material = Materials.Custom();
        if (material == null)
        {
            return;
        }

        UseGameProperties(false);
        _gpu ??= new GpuSurface();
        _gpu.Show(grid, Time.timeSinceLevelLoad, Blends.Start, Blends.Seconds, Lift);
        Filter.sharedMesh = GpuSurface.Grid(grid.VoxelSize);
        Renderer.sharedMaterial = material;
        Renderer.SetPropertyBlock(_gpu.Properties);
        Renderer.enabled = _gpu.Draws;
    }

    // The game's material is animated and drawn only as far as each renderer says, as the game's own water does; the
    // mod's own reads nothing from the renderer, and the card's properties go with the card's drawing: with any other,
    // the renderer is given none.
    private void UseGameProperties(bool game)
    {
        if (game)
        {
            _properties ??= Materials.GameMaterialProperties();
            return;
        }

        _properties = null;
        Renderer.SetPropertyBlock(null);
    }

    private static void Failed(Exception failure, Vector3 at) =>
        FlowingWaterPlugin.Log.LogError($"Water brick at {at}: its surface could not be built, left as it was ({failure})");
}
