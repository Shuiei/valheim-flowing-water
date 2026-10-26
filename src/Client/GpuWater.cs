using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
/// server's water as before. When the camera goes a quarter of the square from its middle, the square moves on round
/// it, its water kept where both squares have it (waves and currents go on); its new edge's is the server's.
/// </summary>
internal sealed class GpuWater : IDisposable
{
    // Rows of cells cast a frame while the square's ground is first found, then to keep it up to date (what was built,
    // a door opened).
    private const int FirstRows = 32;

    private const int FreshRows = 4;

    // A row whose water came this far from the height its ground was cast from, metres, is cast again first.
    private const float CastMoved = 0.25f;

    // Shortest time between two builds of the server's water for the card, seconds.
    private const float BuildSeconds = 0.1f;

    private static readonly int OnId = Shader.PropertyToID("_FWSimOn");
    private static readonly int OriginId = Shader.PropertyToID("_FWSimOrigin");
    private static readonly int SizeId = Shader.PropertyToID("_FWSimSize");
    private static readonly int TargetId = Shader.PropertyToID("_FWSimTarget");
    private static readonly int DepthId = Shader.PropertyToID("_SimH");
    private static readonly int BedId = Shader.PropertyToID("_SimBed");
    private static readonly int ChurnId = Shader.PropertyToID("_SimChurn");
    private static readonly int EastId = Shader.PropertyToID("_SimU");
    private static readonly int NorthId = Shader.PropertyToID("_SimV");
    private static readonly int CellsId = Shader.PropertyToID("_SimCells");
    private static readonly int CellSizeId = Shader.PropertyToID("_SimDx");
    private static readonly int LiftId = Shader.PropertyToID("_Lift");
    private static readonly int FadeCliffsId = Shader.PropertyToID("_FadeCliffs");
    private static readonly int DebugColoursId = Shader.PropertyToID("_FWDebugColours");

    // The grid drawn, and its shape (cells and cell size): each square of that shape draws the same; one of another shape
    // (the settings changed) takes its place.
    private static Mesh _gridMesh;

    private static (int Cells, float CellSize) _gridShape;

    // Its frames, kept from stopping the water: after a fault it is put away and off until a world opens.
    private static readonly FaultGate Gate = new(
        "The water moved on the graphics card failed; it is off until a world opens, the water is drawn as the server sends it",
        FlowingWaterPlugin.Log.LogError,
        () => Stop("off after a fault (see the log)"));

    private static GpuWater _water;

    // The bricks in the square this frame (kept, not made again each frame).
    private static readonly List<BrickView> NearBricks = [];

    private static string _off = "not started";

    // The square, moved on with the camera (Move).
    private SimWindow _window;

    private readonly SimGrid _grid;

    // Its falls, splashes and spray (Client.Spray), made when first wanted.
    private SprayParticles _spray;

    // The particle water (a prototype): poured by the console, kept out of the card's ground, drawn as spheres.
    private ParticleWater _particles;

    private readonly BedCast _cast;

    // The ground under each cell as cast (NaN where not yet), and how many times a cast changed it.
    private readonly float[] _ground;

    private int _groundVersion;

    private readonly GameObject _object;

    private readonly MeshRenderer _renderer;

    private readonly MaterialPropertyBlock _properties = new();

    // The build of the server's water for the card under way, and what it was built from.
    private Task<(float[] Texels, float[] Layers, float[] Cast)> _build;

    // The bricks in the square the build under way, and the last one, were made from (a brick's water is never changed
    // in place: new data is a new view, so the same views are the same water); the ground's version each was made with.
    private List<BrickView> _buildingBricks = [];

    // The square the build under way is for (it is not taken if the square moved since).
    private SimWindow _buildingWindow;

    private List<BrickView> _builtBricks;

    private int _buildingGround;

    // Whether the build under way, and the last one, mark falls for the card's spray (Client.Spray).
    private bool _buildingFalls;

    private bool _builtFalls;

    private int _builtGround = -1;

    private float _builtAt = float.NegativeInfinity;

    // The heights each voxel column's ground is cast from (from the last build), the next row to cast, whether the
    // square's ground was all cast once. For each row of cells and voxel column, the height it was last cast from (NaN:
    // not cast, no water near), and the rows to cast first: water came near them, or rose or fell from that height.
    private float[] _castFrom;

    private readonly float[] _castUsed;

    private readonly bool[] _castFirst;

    private int _nextRow;

    private bool _castOnce;

    private bool _seeded;

    // The last build's texels (for the console).
    private float[] _texels;

    // The last build's ledges and falls (moved with the square).
    private float[] _layers;

    // Which columns the card draws of those the build lets it (none along the square's edge, a new one once its water
    // settled), the build as handed to the card so (again when a column waiting is done).
    private readonly SimHandover _handover;

    private readonly float[] _drawnTexels;

    private readonly float[] _drawnLayers;

    private GpuWater(SimWindow window, Material material, ComputeShader compute)
    {
        _window = window;
        _grid = new SimGrid(compute, window.Cells, window.CellSize, window.PerVoxel);
        if (ZoneSystem.instance != null)
        {
            _grid.SeaLevel = ZoneSystem.instance.m_waterLevel;
        }

        _cast = new BedCast(window.Cells);
        _handover = new SimHandover(window.Columns, window.VoxelSize);
        _drawnTexels = new float[window.Columns * window.Columns * SimColumns.Texel];
        _drawnLayers = new float[window.Columns * window.Columns * SimColumns.LayerTexel];
        _castUsed = new float[window.Cells * window.Columns];
        _castFirst = new bool[window.Cells];
        for (int k = 0; k < _castUsed.Length; k++)
        {
            _castUsed[k] = float.NaN;
        }

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

    /// <summary>
    /// Draws each part of the water in a colour of its own, for testing (console "flowingwater gpu colours"): the card's
    /// water blue, a brick's surface red, the sides and falls yellow.
    /// </summary>
    public static bool DebugColours
    {
        get => Shader.GetGlobalFloat(DebugColoursId) > 0.5f;
        set => Shader.SetGlobalFloat(DebugColoursId, value ? 1f : 0f);
    }

    /// <summary>
    /// Pours a block of particle water, so many cubic metres, 1.5 m over a place (the console's "flowingwater particles",
    /// for testing the prototype): what was poured, or why nothing was.
    /// </summary>
    public static string Pour(Vector3 at, float cubicMetres)
    {
        if (_water == null)
        {
            return "no particle water: " + Describe();
        }

        if (!Materials.Particles(out Material material, out ComputeShader compute))
        {
            return "no particle water: its shaders did not load (see the log)";
        }

        _water._particles ??= new ParticleWater(compute, material, 65536);
        float side = Mathf.Pow(Mathf.Max(cubicMetres, 0.001f), 1f / 3f);
        var min = new Vector3(at.x - (side / 2f), at.y + 1.5f, at.z - (side / 2f));
        int added = _water._particles.Add(min, min + new Vector3(side, side, side));
        return FormattableString.Invariant($"poured {added} particles ({added * ParticleWater.Spacing * ParticleWater.Spacing * ParticleWater.Spacing:0.##} m³) in a block {side:0.##} m on a side, 1.5 m over you; {_water._particles.Count} of {_water._particles.Capacity} in all");
    }

    /// <summary>Every particle of the particle water gone.</summary>
    public static string ClearParticles()
    {
        _water?._particles?.Clear();
        return "the particle water is gone";
    }

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
        // Ground found: cast, and met (not a wall: not cast, no water near, or the ray met nothing).
        int found = 0;
        foreach (float ground in _water._ground)
        {
            found += float.IsNaN(ground) || ground >= BedCast.Wall ? 0 : 1;
        }

        return FormattableString.Invariant(
            $"water on the graphics card: {w.Size:0} m square from {w.X0 * w.VoxelSize:0}, {w.Z0 * w.VoxelSize:0}, {w.Cells} cells of {w.CellSize:0.###} m; ground found under {100f * found / _water._ground.Length:0}% of it; {_water._grid.Substeps} steps a frame; {(_water._seeded ? "drawn" : "waiting for the server's water")}");
    }

    /// <summary>
    /// The nine voxel columns round a place, for the console ("flowingwater gpu columns"): each one's texel as last built
    /// for the card, the ground cast under its cells, and its voxels holding water or part solid within 8 m of it.
    /// </summary>
    public static IEnumerable<string> Columns(Vector3 point)
    {
        if (_water?._texels == null)
        {
            yield return "flowingwater: " + Describe();
            yield break;
        }

        SimWindow w = _water._window;
        int cx = (int)Math.Floor(point.x / w.VoxelSize), cz = (int)Math.Floor(point.z / w.VoxelSize);
        int cy = (int)Math.Floor(point.y / w.VoxelSize), reach = (int)Math.Ceiling(8f / w.VoxelSize);

        // Who draws each column with water within 8 voxels up or down, 8 voxels round (north up): '.' no water, 'c' the
        // card, 'w' the bricks while the card's water there settles or along the square's edge, 'b' the bricks; '@' here.
        for (int z = cz + 8; z >= cz - 8; z--)
        {
            var line = new System.Text.StringBuilder(FormattableString.Invariant($"  z {z,5}: "));
            for (int x = cx - 8; x <= cx + 8; x++)
            {
                int vx = x - w.X0, vz = z - w.Z0;
                int k = (vz * w.Columns) + vx;
                bool inside = vx >= 0 && vz >= 0 && vx < w.Columns && vz < w.Columns;
                bool wet = false;
                for (int y = cy - reach; y <= cy + reach && inside && !wet; y++)
                {
                    wet = VoxelWater.Field.IsWet(x, y, z);
                }

                line.Append(x == cx && z == cz ? '@' : !wet ? '.' : _water._drawnTexels[(k * SimColumns.Texel) + 1] > 0.5f ? 'c' : _water._texels[(k * SimColumns.Texel) + 1] > 0.5f ? 'w' : 'b');
            }

            yield return line.ToString();
        }
        for (int x = cx - 1; x <= cx + 1; x++)
        {
            for (int z = cz - 1; z <= cz + 1; z++)
            {
                int vx = x - w.X0, vz = z - w.Z0;
                if (vx < 0 || vz < 0 || vx >= w.Columns || vz >= w.Columns)
                {
                    continue;
                }

                int k = (vz * w.Columns) + vx;
                float[] t = _water._texels;
                var beds = new List<string>();
                for (int j = 0; j < w.PerVoxel; j++)
                {
                    for (int i = 0; i < w.PerVoxel; i++)
                    {
                        beds.Add(_water._ground[(((vz * w.PerVoxel) + j) * w.Cells) + (vx * w.PerVoxel) + i].ToString("0.00", CultureInfo.InvariantCulture));
                    }
                }

                yield return FormattableString.Invariant($"column {x},{z}: top {t[k * SimColumns.Texel]:0.00} the card's {t[(k * SimColumns.Texel) + 1]} drawn {_water._drawnTexels[(k * SimColumns.Texel) + 1]} depth {t[(k * SimColumns.Texel) + 2]:0.00} w {t[(k * SimColumns.Texel) + 3]:0.00}; ground {string.Join(" ", beds)}");
                for (int y = cy + reach; y >= cy - reach; y--)
                {
                    if (VoxelWater.Field.Voxel(x, y, z, out BrickView view, out int index) && (view.Water[index] > VoxelField.Wet || (view.Solid[index] > 0f && view.Solid[index] < 1f)))
                    {
                        yield return FormattableString.Invariant($"  row {y} ({y * w.VoxelSize:0.0} m): solid {view.Solid[index]:0.00} water {view.Water[index]:0.00} open {view.Open[index]}");
                    }
                }
            }
        }
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
        bool reshaped = _water != null && (_water._window.VoxelSize != window.VoxelSize || _water._window.Columns != window.Columns || _water._window.PerVoxel != window.PerVoxel);
        if (_water != null && (reshaped || !_water._window.Keeps(camera.x, camera.z)))
        {
            // The camera went on: the square goes with it, its water kept where both squares have it (waves and currents
            // go on); cut otherwise, or too far to share any, it is made again.
            if (!reshaped && window.ShiftedFrom(_water._window, out int dx, out int dz))
            {
                _water.Move(window, dx, dz);
            }
            else
            {
                Stop("moved");
            }
        }

        // No water in the square: nothing to move or draw (water far off does not keep the card busy).
        if (Near(window, NearBricks).Count == 0)
        {
            Stop("no water near you");
            return;
        }

        _water ??= Made(window, material, compute);
        _water.Frame(NearBricks);
    }

    // The water near the camera made: the only one, kept in _water (none is made elsewhere).
    private static GpuWater Made(SimWindow window, Material material, ComputeShader compute) => new(window, material, compute);

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
        _spray?.Dispose();
        _particles?.Dispose();
        _grid.Dispose();
        UnityEngine.Object.Destroy(_object);

        // A build still under way is left to finish on its own; a failure in it is told.
        _build?.ContinueWith(t => FlowingWaterPlugin.Log.LogError($"The water for the graphics card could not be built: {t.Exception}"), TaskContinuationOptions.OnlyOnFaulted);
    }

    // A frame: the ground cast taken in and more cast, the server's water taken in and built again if it changed, the
    // water moved, and drawn.
    private void Frame(List<BrickView> near)
    {
        // The water is built again for ground that changed only (the rows are cast round and round, mostly as they were).
        if (_cast.Finish(_ground, out int row, out int rows, out bool changed))
        {
            _grid.WriteBed(row, _ground, rows);
            _groundVersion += changed ? 1 : 0;
        }

        TakeBuild();
        if (Time.time >= _handover.NextReady)
        {
            Hand();
        }

        StartBuild(near);
        StartCast();
        if (!_seeded)
        {
            return;
        }

        _grid.Throws = _spray != null;
        _grid.Step(Time.deltaTime);
        Spray();
        Particles();
        Shader.SetGlobalFloat(OnId, 1f);
        Shader.SetGlobalVector(OriginId, new Vector2(_window.X0 * _window.VoxelSize, _window.Z0 * _window.VoxelSize));
        Shader.SetGlobalFloat(SizeId, _window.Size);
        Shader.SetGlobalTexture(TargetId, _grid.Target);
        _grid.Shape();
        _properties.SetTexture(DepthId, _grid.Drawn);
        _properties.SetTexture(BedId, _grid.Bed);
        _properties.SetTexture(ChurnId, _grid.Churn);
        _properties.SetTexture(EastId, _grid.SpeedEast);
        _properties.SetTexture(NorthId, _grid.SpeedNorth);
        _properties.SetFloat(CellsId, _window.Cells);
        _properties.SetFloat(CellSizeId, _window.CellSize);
        _properties.SetFloat(LiftId, WaterBrick.Lift);
        _properties.SetFloat(FadeCliffsId, _spray != null ? 1f : 0f);
        _renderer.SetPropertyBlock(_properties);
        _renderer.enabled = true;
    }

    // The falls, splashes and spray, while Client.Spray is on: thrown from the water just moved, moved, and drawn over
    // the square (as high as any water).
    private void Spray()
    {
        if (!FlowingWaterPlugin.Settings.Spray.Value || !Materials.Spray(out Material material, out ComputeShader compute))
        {
            // Gone: the water it would have carried or landed is let go (the server's pulls the card's back to it).
            if (_spray != null)
            {
                _spray.Dispose();
                _spray = null;
                _grid.ClearThrown();
            }

            return;
        }

        var origin = new Vector2(_window.X0 * _window.VoxelSize, _window.Z0 * _window.VoxelSize);
        _spray ??= new SprayParticles(compute, material);
        _spray.Curtain = Materials.Falls;
        _spray.Step(_grid, origin, Math.Min(Time.deltaTime, 1f / 15f));
        _spray.Draw(new Bounds(new Vector3(origin.x + (_window.Size / 2f), 0f, origin.y + (_window.Size / 2f)), new Vector3(_window.Size + 20f, 4000f, _window.Size + 20f)));
    }

    // The particle water poured by the console, if any: kept out of the card's ground as cast now (a square that moved
    // keeps the particles where they are in the world), pushed aside by the player, moved, and drawn over the square.
    private void Particles()
    {
        if (_particles == null || _particles.Count == 0)
        {
            return;
        }

        var origin = new Vector2(_window.X0 * _window.VoxelSize, _window.Z0 * _window.VoxelSize);
        _particles.SetGround(_grid.Bed, _window.Cells, _window.CellSize, origin, 10000f);
        Player player = Player.m_localPlayer;
        if (player != null)
        {
            _particles.SetSphere(player.transform.position + new Vector3(0f, 0.9f, 0f), 0.45f);
        }

        _particles.Step(Math.Min(Time.deltaTime, 1f / 15f));
        _particles.Draw(new Bounds(new Vector3(origin.x + (_window.Size / 2f), 0f, origin.y + (_window.Size / 2f)), new Vector3(_window.Size + 20f, 4000f, _window.Size + 20f)));
    }

    // The server's water built for the card, handed to it: the first one seeds it.
    private void TakeBuild()
    {
        if (_build == null || !_build.IsCompleted)
        {
            return;
        }

        Task<(float[] Texels, float[] Layers, float[] Cast)> build = _build;
        _build = null;

        // Built for the square before it moved: not taken (the next is built for the square as it is); had it failed,
        // that is told, the water going on (a fault of its own is the current square's own build's to throw).
        if (!_buildingWindow.Equals(_window))
        {
            if (build.IsFaulted)
            {
                FlowingWaterPlugin.Log.LogError($"The water for the graphics card could not be built (for the square before it moved): {build.Exception}");
            }

            return;
        }

        if (build.IsFaulted)
        {
            throw new InvalidOperationException("the server's water could not be built for the graphics card", build.Exception);
        }

        _texels = build.Result.Texels;
        _layers = build.Result.Layers;
        Hand();
        _castFrom = build.Result.Cast;
        MarkCastFirst();
        _builtBricks = _buildingBricks;
        _builtGround = _buildingGround;
        _builtFalls = _buildingFalls;
        if (!_seeded)
        {
            _grid.Seed();
            _seeded = true;
        }
    }

    // The server's water built again for the card when the bricks in the square (as they are now) or the ground under
    // it changed, on another thread (water changing elsewhere in the world builds nothing).
    private void StartBuild(List<BrickView> near)
    {
        bool falls = FlowingWaterPlugin.Settings.Spray.Value && Materials.Spray(out _, out _);
        if (_build != null || Time.time - _builtAt < BuildSeconds || (_groundVersion == _builtGround && falls == _builtFalls && _builtBricks != null && near.SequenceEqual(_builtBricks)))
        {
            return;
        }

        var bricks = new List<BrickView>(near);
        float[] ground = (float[])_ground.Clone();
        SimWindow w = _window;
        _buildingBricks = bricks;
        _buildingWindow = w;
        _buildingGround = _groundVersion;
        _buildingFalls = falls;
        _builtAt = Time.time;
        _build = Task.Run(() =>
        {
            float[] texels = SimColumns.Build(bricks, w.X0, w.Z0, w.Columns, w.VoxelSize, ground, w.PerVoxel, out float[] cast, out float[] layers, falls);
            return (texels, layers, cast);
        });
    }

    // The server's water as last built handed to the card, but for the columns it does not draw yet (SimHandover).
    private void Hand()
    {
        _handover.Apply(_texels, _layers, Time.time, _drawnTexels, _drawnLayers);
        _grid.SetTarget(_drawnTexels, _drawnLayers);
    }

    // The square moved on by dx, dz voxel columns (the camera went on): the card's water, the server's as last built and
    // the ground found kept where both squares have them, the new edge's to be found and built; any cast or build under
    // way for the old square let go.
    private void Move(SimWindow window, int dx, int dz)
    {
        int per = window.PerVoxel;
        _cast.Drop();
        _grid.Shift(dx * per, dz * per);
        window.Move(dx, dz, _ground, _castUsed, _castFirst, ref _texels, ref _layers);
        _handover.Shift(dx, dz);
        if (_texels != null)
        {
            Hand();
        }

        _castFrom = null;
        _nextRow = 0;
        _builtBricks = null;
        _groundVersion++;
        _window = window;
        _object.transform.position = new Vector3(window.X0 * window.VoxelSize, 0f, window.Z0 * window.VoxelSize);
    }

    // The bricks (of the square's voxel size) in a square, into a list, which it gives back.
    private static List<BrickView> Near(SimWindow window, List<BrickView> into)
    {
        into.Clear();
        int low = window.X0 >> 4, high = (window.X0 + window.Columns - 1) >> 4;
        int south = window.Z0 >> 4, north = (window.Z0 + window.Columns - 1) >> 4;
        foreach (BrickView brick in VoxelWater.Field.Bricks)
        {
            if (brick.X >= low && brick.X <= high && brick.Z >= south && brick.Z <= north && brick.VoxelSize == window.VoxelSize)
            {
                into.Add(brick);
            }
        }

        return into;
    }

    // The rows whose ground is cast again first: in one of their voxel columns, water came near (cast from no height
    // before), or its water's height moved more than CastMoved from the one it was cast from (risen over it, it may
    // stand on what that cast missed; fallen, a floor may lie between).
    private void MarkCastFirst()
    {
        int perVoxel = _window.PerVoxel, columns = _window.Columns;
        for (int row = 0; row < _window.Cells; row++)
        {
            for (int vx = 0; vx < columns && !_castFirst[row]; vx++)
            {
                float from = _castFrom[((row / perVoxel) * columns) + vx];
                float used = _castUsed[(row * columns) + vx];
                _castFirst[row] = !float.IsNaN(from) && (float.IsNaN(used) || Math.Abs(from - used) > CastMoved);
            }
        }
    }

    // The next rows' ground cast, from the heights the last build gave: those to cast first, else quickly until the
    // square's ground was all cast once, then a few rows a frame, round and round.
    private void StartCast()
    {
        if (_cast.Busy || _castFrom == null)
        {
            return;
        }

        int first = Array.IndexOf(_castFirst, true);
        int row, rows;
        if (first >= 0)
        {
            row = first;
            rows = 1;
            while (rows < FirstRows && row + rows < _window.Cells && _castFirst[row + rows])
            {
                rows++;
            }
        }
        else
        {
            row = _nextRow;
            rows = Math.Min(_castOnce ? FreshRows : FirstRows, _window.Cells - _nextRow);
            _nextRow += rows;
            if (_nextRow >= _window.Cells)
            {
                _nextRow = 0;
                _castOnce = true;
            }
        }

        float[] from = _castFrom;
        float[] used = _castUsed;
        int perVoxel = _window.PerVoxel, columns = _window.Columns;
        for (int r = row; r < row + rows; r++)
        {
            _castFirst[r] = false;
            for (int vx = 0; vx < columns; vx++)
            {
                used[(r * columns) + vx] = from[((r / perVoxel) * columns) + vx];
            }
        }

        _cast.Start(row, rows, (x, z) => from[((z / perVoxel) * columns) + (x / perVoxel)], new Vector2(_window.X0 * _window.VoxelSize, _window.Z0 * _window.VoxelSize), _window.CellSize);
    }

    // The grid of a square: a point at each cell's centre, its place in cells as its first uv; bounds as high as any
    // water (the shader raises it).
    private static Mesh Grid(int cells, float cellSize)
    {
        if (_gridMesh != null && _gridShape == (cells, cellSize))
        {
            return _gridMesh;
        }

        if (_gridMesh != null)
        {
            UnityEngine.Object.Destroy(_gridMesh);
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
        _gridMesh = mesh;
        _gridShape = (cells, cellSize);
        return mesh;
    }
}
