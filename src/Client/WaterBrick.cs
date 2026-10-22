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

    private static int _frame = -1;

    private static readonly Dictionary<long, WaterBrick> ByKey = [];

    private static bool _settingWatched;

    private ZNetView _nview;

    private MeshFilter _filter;

    private MeshRenderer _renderer;

    private Mesh _mesh;

    private BrickView _view;

    private uint _revision = uint.MaxValue;

    private bool _dirty;

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
                    brick._dirty = true;
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

        if (_building.Done && InBudget() && _building.TryTake(out Shape shape, out Exception failure))
        {
            FrameDrawing.Start();
            Apply(shape, failure);
            FrameDrawing.Stop();
        }

        if (_dirty && !_building.Busy && Time.time - _drawnAt >= RedrawSeconds)
        {
            Build();
        }
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        if (_view != null)
        {
            VoxelWater.Field.Remove(_view.X, _view.Y, _view.Z);
            ByKey.Remove(VoxelGrid.Key(_view.X, _view.Y, _view.Z));
            Neighbours(_view, VoxelSurface.Round);
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

        _dirty = true;
        Neighbours(_view, VoxelSurface.Touched(was, _view));
    }

    // Whether this frame's drawing time has some left (a new frame starts it again).
    private static bool InBudget()
    {
        if (Time.frameCount != _frame)
        {
            _frame = Time.frameCount;
            FrameDrawing.Reset();
        }

        return FrameDrawing.Elapsed.TotalMilliseconds < FrameBudget;
    }

    // These bricks round this one are drawn again: their surface reads its water.
    private static void Neighbours(BrickView view, IEnumerable<(int X, int Y, int Z)> round)
    {
        foreach ((int dx, int dy, int dz) in round)
        {
            if (ByKey.TryGetValue(VoxelGrid.Key(view.X + dx, view.Y + dy, view.Z + dz), out WaterBrick next) && next != null)
            {
                next._dirty = true;
            }
        }
    }

    // The surface over the brick, built on another thread from the bricks round it as they are now.
    private void Build()
    {
        _dirty = false;
        _drawnAt = Time.time;
        if (_view == null)
        {
            return;
        }

        BrickView view = _view;
        VoxelField near = VoxelWater.Field.Near(view);
        Vector3 origin = transform.position;
        _building.TryStart(() => Shape.Of(near, view, origin));
    }

    // A built surface handed to the mesh.
    private void Apply(Shape shape, Exception failure)
    {
        if (failure != null)
        {
            Failed(failure, transform.position);
            return;
        }

        Material material = Material();
        if (material == null)
        {
            return;
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

    private static Material Material() =>
        FlowingWaterPlugin.Settings.CustomShader.Value && Materials.Custom() is { } custom ? custom : Materials.Still();

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
