using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FlowingWater.Volume;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlowingWater.Client;

/// <summary>
/// The water near the camera, moved on the player's graphics card (<see cref="SimGrid"/>) and drawn from it: in a
/// square round the camera (<see cref="SimWindow"/>), the server's water (as its bricks came) says how much water is
/// where (<see cref="SimColumns"/>, built on another thread when they change), the ground under it is cast a few rows a
/// frame (<see cref="BedCast"/>), and the card moves the water between the server's changes as water moves: a surge
/// runs out and up the walls, a pool sloshes, waves cross. It draws where the server's water is one layer resting on
/// the ground (the bricks' shader leaves those columns to it); elsewhere, and on a card or setting without it, the
/// bricks draw as they do. Nothing of it is sent anywhere: swimming, floating and the game's water checks read the
/// server's water as before. When the camera goes a quarter of the square from its middle, the square is made again
/// round it.
/// </summary>
internal sealed class GpuWater : IDisposable
{
    // Rows of cells cast a frame while the square's ground is first found, then to keep it up to date (what was built,
    // a door opened).
    private const int FirstRows = 32;

    private const int FreshRows = 4;

    // Shortest time between two builds of the server's water for the card, seconds.
    private const float BuildSeconds = 0.1f;

    private static readonly int OnId = Shader.PropertyToID("_FWSimOn");
    private static readonly int OriginId = Shader.PropertyToID("_FWSimOrigin");
    private static readonly int SizeId = Shader.PropertyToID("_FWSimSize");
    private static readonly int TargetId = Shader.PropertyToID("_FWSimTarget");
    private static readonly int DepthId = Shader.PropertyToID("_SimH");
    private static readonly int BedId = Shader.PropertyToID("_SimBed");
    private static readonly int EastId = Shader.PropertyToID("_SimU");
    private static readonly int NorthId = Shader.PropertyToID("_SimV");
    private static readonly int CellsId = Shader.PropertyToID("_SimCells");
    private static readonly int CellSizeId = Shader.PropertyToID("_SimDx");
    private static readonly int LiftId = Shader.PropertyToID("_Lift");

    // The grids drawn, by cells and cell size: each square of that shape draws the same.
    private static readonly Dictionary<(int, float), Mesh> Grids = [];

    // Its frames, kept from stopping the water: after a fault it is put away and off until a world opens.
    private static readonly FaultGate Gate = new(
        "The water moved on the graphics card failed; it is off until a world opens, the water is drawn as the server sends it",
        FlowingWaterPlugin.Log.LogError,
        () => Stop("off after a fault (see the log)"));

    private static GpuWater _water;

    private static string _off = "not started";

    private readonly SimWindow _window;

    private readonly SimGrid _grid;

    private readonly BedCast _cast;

    // The ground under each cell as cast (NaN where not yet), and how many times rows of it were cast.
    private readonly float[] _ground;

    private int _groundVersion;

    private readonly GameObject _object;

    private readonly MeshRenderer _renderer;

    private readonly MaterialPropertyBlock _properties = new();

    // The build of the server's water for the card under way, and what it was built from.
    private Task<(float[] Texels, float[] Cast)> _build;

    private int _buildingVersion;

    private int _buildingGround;

    private int _builtVersion = -1;

    private int _builtGround = -1;

    private float _builtAt = float.NegativeInfinity;

    // The heights each voxel column's ground is cast from (from the last build), the next row to cast, whether the
    // square's ground was all cast once.
    private float[] _castFrom;

    private int _nextRow;

    private bool _castOnce;

    private bool _seeded;

    private GpuWater(SimWindow window, Material material, ComputeShader compute)
    {
        _window = window;
        _grid = new SimGrid(compute, window.Cells, window.CellSize, window.PerVoxel);
        if (ZoneSystem.instance != null)
        {
            _grid.SeaLevel = ZoneSystem.instance.m_waterLevel;
        }

        _cast = new BedCast(window.Cells);
        _ground = new float[window.Cells * window.Cells];
        for (int k = 0; k < _ground.Length; k++)
        {
            _ground[k] = float.NaN;
        }

        _object = new GameObject("FlowingWater near water");
        UnityEngine.Object.DontDestroyOnLoad(_object);
        _object.transform.position = new Vector3(window.X0 * window.VoxelSize, 0f, window.Z0 * window.VoxelSize);
        _object.AddComponent<MeshFilter>().sharedMesh = Grid(window.Cells, window.CellSize);
        _renderer = _object.AddComponent<MeshRenderer>();
        _renderer.sharedMaterial = material;
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.enabled = false;
    }

    /// <summary>
    /// Each frame, after the bricks: the water near the camera moved and drawn on the card, while it may be (a world
    /// with water, a player's game, the setting and the mod's shader on, a card that runs it); else let go. A fault in
    /// it is logged once and turns it off, until a world opens; the bricks draw the water meanwhile.
    /// </summary>
    public static void Tick() => Gate.Run(TickOn);

    /// <summary>A world opens: off after a fault in the last one, it has a fresh try.</summary>
    public static void WorldOpened() => Gate.Reset();

    /// <summary>What it does now, for the console.</summary>
    public static string Describe()
    {
        if (!Gate.On)
        {
            return "water on the graphics card: off after a fault (see the log), until a world opens";
        }

        if (_water == null)
        {
            return $"water on the graphics card: off ({_off})";
        }

        SimWindow w = _water._window;
        int cast = 0;
        foreach (float ground in _water._ground)
        {
            cast += float.IsNaN(ground) ? 0 : 1;
        }

        return FormattableString.Invariant(
            $"water on the graphics card: {w.Size:0} m square from {w.X0 * w.VoxelSize:0}, {w.Z0 * w.VoxelSize:0}, {w.Cells} cells of {w.CellSize:0.###} m; ground cast under {100f * cast / _water._ground.Length:0}% of it; {_water._grid.Substeps} steps a frame; {(_water._seeded ? "drawn" : "waiting for the server's water")}");
    }

    private static void TickOn()
    {
        if (!Wanted(out string why, out Material material, out ComputeShader compute, out Vector3 camera))
        {
            Stop(why);
            return;
        }

        Settings settings = FlowingWaterPlugin.Settings;
        var window = SimWindow.Around(camera.x, camera.z, VoxelWater.Field.VoxelSize, settings.GpuWaterRange.Value, settings.GpuWaterCell.Value);
        if (_water != null && (_water._window.VoxelSize != window.VoxelSize || _water._window.Columns != window.Columns || _water._window.PerVoxel != window.PerVoxel || !_water._window.Keeps(camera.x, camera.z)))
        {
            Stop("moved");
        }

        _water ??= new GpuWater(window, material, compute);
        _water.Frame();
    }

    // Whether the water near the camera is moved on the card now, and with what; else why not.
    private static bool Wanted(out string why, out Material material, out ComputeShader compute, out Vector3 camera)
    {
        material = null;
        compute = null;
        camera = default;
        Settings settings = FlowingWaterPlugin.Settings;
        why = ZNet.instance == null ? "no world"
            : ZNet.instance.IsDedicated() ? "a dedicated server draws nothing"
            : !settings.Enabled.Value ? "the mod is turned off"
            : !settings.GpuWater.Value ? "turned off in the settings (Client.GpuWater)"
            : !settings.CustomShader.Value ? "the mod's shader is turned off (Client.CustomShader)"
            : WaterBrick.Flat ? "the water is drawn flat (flowingwater flat)"
            : VoxelWater.Field.Bricks.Count == 0 ? "no water here"
            : null;
        if (why != null)
        {
            return false;
        }

        if (!Materials.Sim(out material, out compute))
        {
            why = "this graphics card or platform cannot run it (see the log)";
            return false;
        }

        Camera main = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
        if (main == null)
        {
            why = "no camera";
            return false;
        }

        camera = main.transform.position;
        return true;
    }

    // Let go: the bricks draw all the water again.
    private static void Stop(string why)
    {
        _off = why;
        Shader.SetGlobalFloat(OnId, 0f);
        _water?.Dispose();
        _water = null;
    }

    public void Dispose()
    {
        _cast.Dispose();
        _grid.Dispose();
        UnityEngine.Object.Destroy(_object);

        // A build still under way is left to finish on its own; a failure in it is told.
        _build?.ContinueWith(t => FlowingWaterPlugin.Log.LogError($"The water for the graphics card could not be built: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
    }

    // A frame: the ground cast taken in and more cast, the server's water taken in and built again if it changed, the
    // water moved, and drawn.
    private void Frame()
    {
        if (_cast.Finish(_ground, out int row, out int rows))
        {
            _grid.WriteBed(row, _ground, rows);
            _groundVersion++;
        }

        TakeBuild();
        StartBuild();
        StartCast();
        if (!_seeded)
        {
            return;
        }

        _grid.Step(Time.deltaTime);
        Shader.SetGlobalFloat(OnId, 1f);
        Shader.SetGlobalVector(OriginId, new Vector2(_window.X0 * _window.VoxelSize, _window.Z0 * _window.VoxelSize));
        Shader.SetGlobalFloat(SizeId, _window.Size);
        Shader.SetGlobalTexture(TargetId, _grid.Target);
        _properties.SetTexture(DepthId, _grid.Depth);
        _properties.SetTexture(BedId, _grid.Bed);
        _properties.SetTexture(EastId, _grid.SpeedEast);
        _properties.SetTexture(NorthId, _grid.SpeedNorth);
        _properties.SetFloat(CellsId, _window.Cells);
        _properties.SetFloat(CellSizeId, _window.CellSize);
        _properties.SetFloat(LiftId, WaterBrick.Lift);
        _renderer.SetPropertyBlock(_properties);
        _renderer.enabled = true;
    }

    // The server's water built for the card, handed to it: the first one seeds it.
    private void TakeBuild()
    {
        if (_build == null || !_build.IsCompleted)
        {
            return;
        }

        Task<(float[] Texels, float[] Cast)> build = _build;
        _build = null;
        if (build.IsFaulted)
        {
            throw new InvalidOperationException("the server's water could not be built for the graphics card", build.Exception);
        }

        _grid.SetTarget(build.Result.Texels);
        _castFrom = build.Result.Cast;
        _builtVersion = _buildingVersion;
        _builtGround = _buildingGround;
        if (!_seeded)
        {
            _grid.Seed();
            _seeded = true;
        }
    }

    // The server's water built again for the card when it or the ground under it changed, on another thread from the
    // bricks in the square as they are now (a brick's water is never changed in place: new data is a new view).
    private void StartBuild()
    {
        if (_build != null || Time.time - _builtAt < BuildSeconds || (VoxelWater.Field.Version == _builtVersion && _groundVersion == _builtGround))
        {
            return;
        }

        var bricks = new List<BrickView>();
        int low = _window.X0 >> 4, high = (_window.X0 + _window.Columns - 1) >> 4;
        int south = _window.Z0 >> 4, north = (_window.Z0 + _window.Columns - 1) >> 4;
        foreach (BrickView brick in VoxelWater.Field.Bricks)
        {
            if (brick.X >= low && brick.X <= high && brick.Z >= south && brick.Z <= north && brick.VoxelSize == _window.VoxelSize)
            {
                bricks.Add(brick);
            }
        }

        float[] ground = (float[])_ground.Clone();
        SimWindow w = _window;
        _buildingVersion = VoxelWater.Field.Version;
        _buildingGround = _groundVersion;
        _builtAt = Time.time;
        _build = Task.Run(() =>
        {
            float[] texels = SimColumns.Build(bricks, w.X0, w.Z0, w.Columns, w.VoxelSize, ground, w.PerVoxel, out float[] cast);
            return (texels, cast);
        });
    }

    // The next rows' ground cast, from the heights the last build gave: quickly until the square's ground was all cast
    // once, then a few rows a frame, round and round.
    private void StartCast()
    {
        if (_cast.Busy || _castFrom == null)
        {
            return;
        }

        int rows = Math.Min(_castOnce ? FreshRows : FirstRows, _window.Cells - _nextRow);
        float[] from = _castFrom;
        int perVoxel = _window.PerVoxel, columns = _window.Columns;
        _cast.Start(_nextRow, rows, (x, z) => from[((z / perVoxel) * columns) + (x / perVoxel)], new Vector2(_window.X0 * _window.VoxelSize, _window.Z0 * _window.VoxelSize), _window.CellSize);
        _nextRow += rows;
        if (_nextRow >= _window.Cells)
        {
            _nextRow = 0;
            _castOnce = true;
        }
    }

    // The grid of a square: a point at each cell's centre, its place in cells as its first uv; bounds as high as any
    // water (the shader raises it).
    private static Mesh Grid(int cells, float cellSize)
    {
        if (Grids.TryGetValue((cells, cellSize), out Mesh made) && made != null)
        {
            return made;
        }

        var vertices = new Vector3[cells * cells];
        var uvs = new Vector2[cells * cells];
        for (int j = 0; j < cells; j++)
        {
            for (int i = 0; i < cells; i++)
            {
                vertices[(j * cells) + i] = new Vector3((i + 0.5f) * cellSize, 0f, (j + 0.5f) * cellSize);
                uvs[(j * cells) + i] = new Vector2(i + 0.5f, j + 0.5f);
            }
        }

        int[] triangles = new int[(cells - 1) * (cells - 1) * 6];
        int t = 0;
        for (int j = 0; j < cells - 1; j++)
        {
            for (int i = 0; i < cells - 1; i++)
            {
                int a = (j * cells) + i;
                int b = a + cells;
                triangles[t++] = a;
                triangles[t++] = b;
                triangles[t++] = b + 1;
                triangles[t++] = a;
                triangles[t++] = b + 1;
                triangles[t++] = a + 1;
            }
        }

        float size = cells * cellSize;
        var mesh = new Mesh { name = $"FlowingWater near water {cells} x {cellSize} m", indexFormat = IndexFormat.UInt32 };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(triangles, 0, false);
        mesh.bounds = new Bounds(new Vector3(size / 2f, 0f, size / 2f), new Vector3(size, 4000f, size));
        mesh.UploadMeshData(true);
        Grids[(cells, cellSize)] = mesh;
        return mesh;
    }
}
