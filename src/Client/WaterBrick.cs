using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Threading.Tasks;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// A brick of the 3D water on a player's game: reads the brick's data when the server changes it, keeps it in
/// <see cref="VoxelWater.Field"/>, and draws the water's surface over it (<see cref="VoxelSurface"/>) with the
/// water's material. When its water changes, the bricks round it are drawn again too (their sides and corners
/// depend on it). The surface is built on another thread, from the bricks it reads as they were then; only handing
/// it to the mesh is done in the game's frame.
/// </summary>
internal sealed class WaterBrick : MonoBehaviour
{
    // Shortest time between two drawings of a brick, seconds.
    private const float RedrawSeconds = 0.1f;

    // Longest time drawing bricks may take in a frame, milliseconds: the others wait for the next frame (one is
    // always drawn), so that much water changing at once does not stall a frame.
    private const double FrameBudget = 2.0;

    private static readonly Stopwatch FrameDrawing = new();

    // Bricks to draw again, those waiting longest first: a brick whose water keeps changing does not hold back
    // the others.
    private static readonly TakeTurns<WaterBrick> Waiting = new();

    // Bricks whose surface is being built, handed to their meshes once built, those waiting longest first.
    private static readonly TakeTurns<WaterBrick> Built = new();

    private static int _frame = -1;

    private static readonly Dictionary<long, WaterBrick> ByKey = [];

    private static bool _settingWatched;

    private ZNetView _nview;

    private MeshFilter _filter;

    private MeshRenderer _renderer;

    private Mesh _mesh;

    // What the game's water material reads from each renderer (its waves' time, how far it is drawn); none with the
    // mod's own.
    private MaterialPropertyBlock _properties;

    private BrickView _view;

    private uint _revision = uint.MaxValue;

    private float _drawnAt;

    private readonly BackgroundBuild<Shape> _building = new();

    // Every brick is drawn again with the other material when the shader setting changes.
    private static void WatchSetting()
    {
        if (_settingWatched)
        {
            return;
        }

        _settingWatched = true;
        FlowingWaterPlugin.Settings.CustomShader.SettingChanged += (_, _) =>
        {
            foreach (WaterBrick brick in ByKey.Values)
            {
                if (brick != null)
                {
                    Waiting.Add(brick);
                }
            }
        };
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

        _mesh = new Mesh { name = "FlowingWater brick" };
        _mesh.MarkDynamic();
        _filter.sharedMesh = _mesh;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Update()
    {
        ZDO zdo = _nview.GetZDO();
        if (zdo != null && zdo.DataRevision != _revision)
        {
            _revision = zdo.DataRevision;
            Read(zdo.GetByteArray(ZdoKeys.Brick));
        }

        if (_properties != null)
        {
            WaterTile.Animate(_renderer, _properties);
        }
    }

    // After every brick has read its data this frame.
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Unity calls only instance methods.")]
    private void LateUpdate() => DrawWaiting();

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        // A brick made again at the same place (dry, then wet again) may have come before this one went: its water
        // and its place in the lookup are its own, left alone.
        long key = _view == null ? 0L : VoxelGrid.Key(_view.X, _view.Y, _view.Z);
        if (_view != null && ByKey.TryGetValue(key, out WaterBrick current) && current == this)
        {
            VoxelWater.Field.Remove(_view.X, _view.Y, _view.Z);
            ByKey.Remove(key);
            Neighbours(_view, VoxelSurface.Round);
        }

        // The last water gone (the world left): no brick is left to serve the queue, which would keep the bricks
        // gone with it.
        if (ByKey.Count == 0)
        {
            Waiting.Clear();
            Built.Clear();
        }

        if (_mesh != null)
        {
            Destroy(_mesh);
        }

        // A surface still being built is not drawn; a failure to build it is still told, in the game's frame (its log
        // is the game's), from where the brick was.
        Vector3 at = transform.position;
        _building.Abandon(failure => Failed(failure, at), TaskScheduler.FromCurrentSynchronizationContext());
    }

    private void Read(byte[] data)
    {
        if (data == null)
        {
            return;
        }

        BrickView was = _view;
        try
        {
            _view = BrickData.Decode(data);
        }
        catch (InvalidDataException e)
        {
            FlowingWaterPlugin.Log.LogWarning($"Water brick at {transform.position}: data unreadable, not drawn ({e.Message})");
            return;
        }

        VoxelWater.Field.Set(_view);
        ByKey[VoxelGrid.Key(_view.X, _view.Y, _view.Z)] = this;
        if (GetComponentInChildren<BoxCollider>() is { } trigger)
        {
            trigger.size = Vector3.one * (Brick.Size * _view.VoxelSize);
        }

        Waiting.Add(this);
        Neighbours(_view, VoxelSurface.Touched(was, _view));
    }

    // Once a frame (from the first brick's LateUpdate), while the frame's drawing time lasts: the surfaces built are
    // handed to their meshes, then the bricks waiting are built again, longest waiting first. One still building, or
    // drawn less than RedrawSeconds ago, waits on; one gone is dropped.
    private static void DrawWaiting()
    {
        if (Time.frameCount == _frame)
        {
            return;
        }

        _frame = Time.frameCount;
        FrameDrawing.Reset();
        Built.Serve(brick => brick == null || brick._building.Done, InBudget, brick => Timed(brick, b => b.Take()));

        // A build reads the bricks round it in the frame: none once the meshes took the frame's time.
        if (InBudget())
        {
            Waiting.Serve(
                brick => brick == null || (!brick._building.Busy && Time.time - brick._drawnAt >= RedrawSeconds),
                InBudget,
                brick => Timed(brick, b => b.Build()));
        }
    }

    // Whether this frame's drawing time has some left.
    private static bool InBudget() => FrameDrawing.Elapsed.TotalMilliseconds < FrameBudget;

    // Does this for a brick still there, on the frame's drawing time.
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

    // The surface over the brick, built on another thread from the bricks round it as they are now.
    private void Build()
    {
        _drawnAt = Time.time;
        if (_view == null)
        {
            return;
        }

        BrickView view = _view;
        VoxelField near = VoxelWater.Field.Near(view);
        Vector3 origin = transform.position;
        if (_building.TryStart(() => Shape.Of(near, view, origin)))
        {
            Built.Add(this);
        }
    }

    // The surface built, handed to the mesh.
    private void Take()
    {
        if (_building.TryTake(out Shape shape, out Exception failure))
        {
            Apply(shape, failure);
        }
    }

    // A built surface handed to the mesh.
    private void Apply(Shape shape, Exception failure)
    {
        if (failure != null)
        {
            Failed(failure, transform.position);
            return;
        }

        Material custom = FlowingWaterPlugin.Settings.CustomShader.Value ? Materials.Custom() : null;
        Material material = custom ?? Materials.Still();
        if (material == null)
        {
            return;
        }

        // The game's material is animated and drawn only as far as each renderer says, as the game's own water does.
        if (custom == null)
        {
            _properties ??= WaterTile.GameMaterialProperties();
        }
        else if (_properties != null)
        {
            _properties = null;
            _renderer.SetPropertyBlock(null);
        }

        _mesh.Clear();
        _mesh.indexFormat = shape.Vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        _mesh.SetVertices(shape.Vertices);
        _mesh.SetUVs(0, shape.World);
        _mesh.SetUVs(1, shape.Water);
        _mesh.SetUVs(2, shape.Current);
        _mesh.SetTriangles(shape.Triangles, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateTangents();
        _mesh.RecalculateBounds();
        _renderer.sharedMaterial = material;
    }

    private static void Failed(Exception failure, Vector3 at) =>
        FlowingWaterPlugin.Log.LogError($"Water brick at {at}: its surface could not be built, left as it was ({failure})");

    // A surface as the mesh takes it: positions from the brick's origin, and what each vertex carries.
    private sealed class Shape
    {
        public List<Vector3> Vertices { get; } = [];

        public List<Vector2> World { get; } = [];

        public List<Vector4> Water { get; } = [];

        public List<Vector2> Current { get; } = [];

        public List<int> Triangles { get; private set; } = [];

        public static Shape Of(VoxelField field, BrickView view, Vector3 origin)
        {
            SurfaceMesh surface = VoxelSurface.Build(field, view);
            var shape = new Shape { Triangles = surface.Triangles };
            for (int i = 0; i < surface.Vertices.Count; i++)
            {
                Vec3 v = surface.Vertices[i];
                shape.Vertices.Add(new Vector3(v.X, v.Y, v.Z) - origin);

                // Ripples on the sides run up them: their texture shifts with height.
                shape.World.Add(new Vector2(v.X + (v.Y * 0.5f), v.Z + (v.Y * 0.5f)));
                shape.Water.Add(new Vector4(surface.Depths[i], 0f, surface.Shores[i], 0f));
                shape.Current.Add(new Vector2(surface.Velocities[i].X, surface.Velocities[i].Z));
            }

            return shape;
        }
    }
}
