using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// A brick of the 3D water on a player's game: reads the brick's data when the server changes it, keeps it in
/// <see cref="VoxelWater.Field"/>, and draws the water's surface over it (<see cref="VoxelSurface"/>) with the
/// water's material. When its water changes, the bricks round it are drawn again too (their sides and corners
/// depend on it).
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

    private static int _frame = -1;

    private static readonly Dictionary<long, WaterBrick> ByKey = [];

    private static bool _settingWatched;

    private ZNetView _nview;

    private MeshFilter _filter;

    private MeshRenderer _renderer;

    private Mesh _mesh;

    private BrickView _view;

    private uint _revision = uint.MaxValue;

    private float _drawnAt;

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
        }

        if (_mesh != null)
        {
            Destroy(_mesh);
        }
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

    // Once a frame (from the first brick's LateUpdate), the bricks waiting are drawn, longest waiting first, while the
    // frame's drawing time lasts. One drawn less than RedrawSeconds ago waits on; one gone is dropped.
    private static void DrawWaiting()
    {
        if (Time.frameCount == _frame)
        {
            return;
        }

        _frame = Time.frameCount;
        FrameDrawing.Reset();
        Waiting.Serve(
            brick => brick == null || Time.time - brick._drawnAt >= RedrawSeconds,
            () => FrameDrawing.Elapsed.TotalMilliseconds < FrameBudget,
            brick =>
            {
                if (brick != null)
                {
                    FrameDrawing.Start();
                    brick.Draw();
                    FrameDrawing.Stop();
                }
            });
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

    private void Draw()
    {
        _drawnAt = Time.time;
        Material material = Material();
        if (_view == null || material == null)
        {
            return;
        }

        SurfaceMesh surface = VoxelSurface.Build(VoxelWater.Field, _view);
        Vector3 origin = transform.position;
        var vertices = new List<Vector3>(surface.Vertices.Count);
        var world = new List<Vector2>(surface.Vertices.Count);
        var water = new List<Vector4>(surface.Vertices.Count);
        var current = new List<Vector2>(surface.Vertices.Count);
        for (int i = 0; i < surface.Vertices.Count; i++)
        {
            Vec3 v = surface.Vertices[i];
            vertices.Add(new Vector3(v.X, v.Y, v.Z) - origin);

            // Ripples on the sides run up them: their texture shifts with height.
            world.Add(new Vector2(v.X + (v.Y * 0.5f), v.Z + (v.Y * 0.5f)));
            water.Add(new Vector4(surface.Depths[i], 0f, surface.Shores[i], 0f));
            current.Add(new Vector2(surface.Velocities[i].X, surface.Velocities[i].Z));
        }

        _mesh.Clear();
        _mesh.indexFormat = vertices.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
        _mesh.SetVertices(vertices);
        _mesh.SetUVs(0, world);
        _mesh.SetUVs(1, water);
        _mesh.SetUVs(2, current);
        _mesh.SetTriangles(surface.Triangles, 0);
        _mesh.RecalculateNormals();
        _mesh.RecalculateTangents();
        _mesh.RecalculateBounds();
        _renderer.sharedMaterial = material;
    }

    private static Material Material() =>
        FlowingWaterPlugin.Settings.CustomShader.Value && Materials.Custom() is { } custom ? custom : Materials.Still();
}
