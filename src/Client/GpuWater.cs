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

    // The particle water (a prototype): poured by the console, kept out of the card's ground, drawn as water over the
    // scene by the camera.
    private ParticleWater _particles;

    // The volume water (Client.VolumeWater): the water near the camera as one body of fluid, in place of the sheet.
    private VolumeWater _volume;

    private bool _volumeSeeded;

    // Cells of the square's sides to seed from the server's water at the next frame (the square moved: its strip is
    // poured once the volume has the moved square's ground and solids, not before).
    private int _volumeReseed;

    private readonly List<(Vector3 At, float Rate, bool Drain, float Radius)> _markers = [];

    private readonly List<VolumeWater.Spring> _springs = [];

    private readonly List<(Vector3 At, float Rate)> _sources = [];

    private readonly List<ParticleWater.Spout> _spouts = [];

    private static readonly List<ParticleWater.Spout> NoSpouts = [];

    private ParticleSurface _surface;

    private readonly BedCast _cast;

    // The ground under each cell as cast (NaN where not yet), and how many times a cast changed it.
    private readonly float[] _ground;

    private int _groundVersion;

    private readonly GameObject _object;

    private readonly MeshRenderer _renderer;

    private readonly MaterialPropertyBlock _properties = new();

    // The build of the server's water for the card under way, and what it was built from.
    private Task<(float[] Texels, float[] Layers, float[] Cast, byte[] Solids, bool AnySolid)> _build;

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

    // What players built, for the particle water: the bricks' solid voxels round the camera's height (the row the
    // last build started from; built again once the camera is 8 m from it), as the build gathers them.
    private readonly ParticleSolids _solids = new();

    private readonly List<ParticleWater.Body> _bodies = new(ParticleWater.MostBodies);

    private Ship[] _ships = [];

    private readonly List<(Rigidbody Body, Collider[] Colliders)> _rigidbodies = [];

    private int _shipsLookedFor = -60;

    private int _buildingBase;

    private int _builtBase = int.MinValue;

    // The square the solids were taken for: the particles get them only while it is the square (the square moved on,
    // they would stand the move's columns away from the pieces until the next build).
    private SimWindow _solidsWindow;

    private float _cameraY;

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

        if (_water._volume != null)
        {
            float edge = Mathf.Pow(Mathf.Max(cubicMetres, 0.001f), 1f / 3f);
            var low = new Vector3(at.x - (edge / 2f), at.y + 1.5f, at.z - (edge / 2f));
            int made = _water._volume.Add(low, low + new Vector3(edge, edge, edge));
            return FormattableString.Invariant($"poured {made} particles ({made * _water._volume.Litre:0.##} m³) into the volume water in a block {edge:0.##} m on a side, 1.5 m over you; {_water._volume.Count} of {_water._volume.Capacity} in all");
        }

        if (!Materials.Particles(out Material material, out _, out ComputeShader compute))
        {
            return "no particle water: its shaders did not load (see the log)";
        }

        _water._particles ??= new ParticleWater(compute, material, FlowingWaterPlugin.Settings.ParticleCapacity.Value, FlowingWaterPlugin.Settings.ParticleSize.Value);
        if (_water._particles.Count >= _water._particles.Capacity)
        {
            return FormattableString.Invariant($"no room: {_water._particles.Capacity} particles already (flowingwater particles clear)");
        }
        float side = Mathf.Pow(Mathf.Max(cubicMetres, 0.001f), 1f / 3f);
        var min = new Vector3(at.x - (side / 2f), at.y + 1.5f, at.z - (side / 2f));
        int added = _water._particles.Add(min, min + new Vector3(side, side, side));
        return FormattableString.Invariant($"poured {added} particles ({added * _water._particles.Litre:0.##} m³) in a block {side:0.##} m on a side, 1.5 m over you; {_water._particles.Count} of {_water._particles.Capacity} in all{(_water._particles.Owned ? " (held to the server's water: it drains away where the server has none)" : string.Empty)}");
    }

    /// <summary>Every particle of the particle water gone.</summary>
    public static string ClearParticles()
    {
        _water?._particles?.Clear();
        _water?._volume?.Clear();
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
        // With the volume water, the card's square is the volume's: no wider than its range.
        float range = settings.VolumeWater.Value ? Math.Min(settings.GpuWaterRange.Value, settings.VolumeRange.Value) : settings.GpuWaterRange.Value;
        var window = SimWindow.Around(camera.x, camera.z, VoxelWater.Field.VoxelSize, range, settings.GpuWaterCell.Value);
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
        _water._cameraY = camera.y;
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
        _surface?.Dispose();
        _particles?.Dispose();
        _volume?.Dispose();
        _solids.Dispose();
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

        // The volume water, or the particle water, as the water near the camera: the sheet is neither moved nor drawn
        // then, nor its spray; the bricks still draw beyond the square and where the card draws nothing (the texels say
        // where).
        if (!FlowingWaterPlugin.Settings.VolumeWater.Value && _volume != null)
        {
            // Switched off: its memory given back, and its last drawing taken off the camera (the particle water, if
            // any, makes the surface again as it needs).
            _volume.Dispose();
            _volume = null;
            if (_particles == null)
            {
                _surface?.Dispose();
                _surface = null;
            }
        }

        bool own = FlowingWaterPlugin.Settings.VolumeWater.Value ? Volume() : Particles();
        if (own)
        {
            if (_spray != null)
            {
                _spray.Dispose();
                _spray = null;
                _grid.ClearThrown();
            }

            Shader.SetGlobalFloat(OnId, 1f);
            Shader.SetGlobalVector(OriginId, new Vector2(_window.X0 * _window.VoxelSize, _window.Z0 * _window.VoxelSize));
            Shader.SetGlobalFloat(SizeId, _window.Size);
            Shader.SetGlobalTexture(TargetId, _grid.Target);
            _renderer.enabled = false;
            return;
        }

        _grid.Throws = _spray != null;
        _grid.Step(Time.deltaTime);
        Spray();
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

    // The particle water: with Client.ParticleWater on, the water near the camera itself (made then, if not yet by the
    // console, held to the server's water); else what the console poured, if any. Kept out of the card's ground as
    // cast now (a square that moved keeps the particles where they are in the world), pushed aside by the bodies in it,
    // moved, and drawn as water by the game's camera. Whether the particles own the water near the camera.
    private bool Particles()
    {
        bool own = FlowingWaterPlugin.Settings.ParticleWater.Value;
        if (_particles == null && own && Materials.Particles(out Material material, out _, out ComputeShader compute))
        {
            _particles = new ParticleWater(compute, material, FlowingWaterPlugin.Settings.ParticleCapacity.Value, FlowingWaterPlugin.Settings.ParticleSize.Value);
        }

        if (_particles == null)
        {
            return false;
        }

        var origin = new Vector2(_window.X0 * _window.VoxelSize, _window.Z0 * _window.VoxelSize);
        _particles.SetGround(_grid.Bed, _window.Cells, _window.CellSize, origin, 10000f);
        _particles.SetOwner(own ? _grid.Target : null, _window.PerVoxel, SimGrid.BorderCells, _grid.SeaLevel);
        bool solidsFit = _solids.Any && _solidsWindow.Equals(_window);
        _particles.SetSolids(solidsFit ? _solids.Texture : null, _solids.Columns, ParticleSolids.Rows, _solids.VoxelSize, _solids.BaseY);
        _particles.SetBodies(Bodies(origin));
        // The sources' water comes out as particles only where the particles are the water (not beside the sheet's own).
        _particles.SetSpouts(own ? Spouts(origin) : NoSpouts);

        _particles.Step(Math.Min(Time.deltaTime, 1f / 15f));
        Camera camera = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
        if (camera != null && Materials.Particles(out _, out Material surface, out _))
        {
            _surface ??= new ParticleSurface();
            _surface.Render(camera, _particles, surface);
        }

        return own;
    }

    // The volume water as the water near the camera (made when first wanted, with the server's water poured in once it
    // is there; the square's origin follows the window, the grid's height the camera): the ground, what is built, the
    // bodies, the sources and drains in the square given it, moved, and drawn by the game's camera. Whether it is on.
    private bool Volume()
    {
        if (_volume == null)
        {
            if (!Materials.Volume(out Material material, out _, out Material froth, out ComputeShader compute))
            {
                return false;
            }

            Settings settings = FlowingWaterPlugin.Settings;
            int rows = VolumeFollow.Rows(settings.VolumeHeight.Value, _window.CellSize);
            _volume = new VolumeWater(compute, material, froth, settings.VolumeCapacity.Value, _window.Cells, rows, _window.Cells, _window.CellSize)
            {
                OpenSides = true,
                // Two metres of the square's edge take the server's water each step, whatever the cells' size.
                EdgeBand = VolumeFollow.Band(_window.CellSize),
                Origin = new Vector3(_window.X0 * _window.VoxelSize, VolumeFollow.BaseY(_cameraY, rows, _window.CellSize), _window.Z0 * _window.VoxelSize),
            };
            _volumeSeeded = false;
            FlowingWaterPlugin.Log.LogInfo(FormattableString.Invariant($"Volume water: {_window.Cells} x {rows} x {_window.Cells} cells of {_window.CellSize} m, {settings.VolumeCapacity.Value} particles at most"));
        }

        var origin = new Vector2(_window.X0 * _window.VoxelSize, _window.Z0 * _window.VoxelSize);
        if (VolumeFollow.Rebase(_cameraY, _volume.Origin.y, _volume.CellsY, _volume.CellSize))
        {
            // The camera climbed or fell past the grid's middle half: the grid follows (water out of it is the server's).
            _volume.Origin = new Vector3(origin.x, VolumeFollow.BaseY(_cameraY, _volume.CellsY, _volume.CellSize), origin.y);
            _volumeSeeded = false;
        }
        else
        {
            _volume.Origin = new Vector3(origin.x, _volume.Origin.y, origin.y);
        }

        _volume.SetGround(_grid.Bed, _window.Cells, _window.CellSize, origin);
        _volume.SetServer(_grid.Target, _window.PerVoxel);
        bool solidsFit = _solids.Any && _solidsWindow.Equals(_window);
        _volume.SetSolids(solidsFit ? _solids.Texture : null, _solids.Columns, ParticleSolids.Rows, _solids.VoxelSize, origin, _solids.BaseY);
        _volume.SetBodies(Bodies(origin));
        _volume.SetSprings(Springs(origin));
        if (!_volumeSeeded)
        {
            _volume.SeedFromServer();
            _volumeSeeded = true;
        }
        else if (_volumeReseed > 0)
        {
            // The square moved this frame: its new strip takes the server's water, now that the volume has the moved
            // square's ground, water and solids.
            _volume.SeedFromServer(1f, _volumeReseed);
        }

        _volumeReseed = 0;

        _volume.FrothRate = FlowingWaterPlugin.Settings.VolumeFroth.Value;
        _volume.Step(Math.Min(Time.deltaTime, 1f / 15f));
        Camera camera = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
        if (camera != null && Materials.Particles(out _, out Material surface, out _))
        {
            _surface ??= new ParticleSurface();
            _surface.Render(camera, _volume, surface, _volume.Froth);
        }

        return true;
    }

    // The placed sources and drains in the square, for the volume water (the server's fixed ones have no marker to
    // see: their water comes in at the square's edge as the server's other water does).
    private List<VolumeWater.Spring> Springs(Vector2 origin)
    {
        Marker.Markers(_markers);
        _springs.Clear();
        float size = _window.Cells * _window.CellSize;
        foreach ((Vector3 at, float rate, bool drain, float radius) in _markers)
        {
            if (at.x >= origin.x && at.z >= origin.y && at.x < origin.x + size && at.z < origin.y + size && _springs.Count < VolumeWater.MostSprings)
            {
                _springs.Add(drain ? VolumeWater.Spring.Drain(at + (Vector3.up * Prefabs.MarkerRadius), radius, rate) : VolumeWater.Spring.SourceOn(at, rate));
            }
        }

        return _springs;
    }

    // The placed sources in the square, their water to come out of them (the server's fixed sources have no marker to
    // see: their water comes as the square's other water does).
    private List<ParticleWater.Spout> Spouts(Vector2 origin)
    {
        Marker.Sources(_sources);
        _spouts.Clear();
        float size = _window.Cells * _window.CellSize;
        foreach ((Vector3 at, float rate) in _sources)
        {
            if (at.x >= origin.x && at.z >= origin.y && at.x < origin.x + size && at.z < origin.y + size && _spouts.Count < ParticleWater.MostSpouts)
            {
                _spouts.Add(ParticleWater.Spout.At(at + (Vector3.up * Prefabs.MarkerRadius), Prefabs.MarkerRadius, rate, 0.5f * _particles.Spacing));
            }
        }

        return _spouts;
    }

    // The server's water built for the card, handed to it: the first one seeds it.
    private void TakeBuild()
    {
        if (_build == null || !_build.IsCompleted)
        {
            return;
        }

        Task<(float[] Texels, float[] Layers, float[] Cast, byte[] Solids, bool AnySolid)> build = _build;
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
        if (build.Result.Solids != null)
        {
            _solids.Take(build.Result.Solids, _window.Columns, _window.VoxelSize, _buildingBase, build.Result.AnySolid);
            _builtBase = _buildingBase;
            _solidsWindow = _buildingWindow;
        }
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
        bool solids = _particles != null || _volume != null || FlowingWaterPlugin.Settings.ParticleWater.Value || FlowingWaterPlugin.Settings.VolumeWater.Value;
        int baseRow = (int)Math.Floor(_cameraY / _window.VoxelSize) - (ParticleSolids.Rows / 2);
        bool rebased = solids && (_builtBase == int.MinValue || Math.Abs(baseRow - _builtBase) > 8);
        if (_build != null || Time.time - _builtAt < BuildSeconds || (_groundVersion == _builtGround && falls == _builtFalls && !rebased && _builtBricks != null && near.SequenceEqual(_builtBricks)))
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
        _buildingBase = rebased ? baseRow : _builtBase;
        int solidsBase = _buildingBase;
        _builtAt = Time.time;
        _build = Task.Run(() =>
        {
            float[] texels = SimColumns.Build(bricks, w.X0, w.Z0, w.Columns, w.VoxelSize, ground, w.PerVoxel, out float[] cast, out float[] layers, falls);
            bool any = false;
            byte[] built = solids ? Solids(bricks, w.X0, w.Z0, w.Columns, solidsBase, out any) : null;
            return (texels, layers, cast, built, any);
        });
    }

    // The bodies in the square this frame, for the particle water, as many as fit in this order: every ship as the box
    // its hull floats by (the game's own; the hull is what the water is for, so ships come first), every character
    // (players, creatures) as the box of its capsule, and every other rigid body (carts, things dropped, felled trees)
    // as the box round each of its colliders.
    private List<ParticleWater.Body> Bodies(Vector2 origin)
    {
        _bodies.Clear();
        float size = _window.Size;
        var square = new Bounds(new Vector3(origin.x + (size / 2f), 0f, origin.y + (size / 2f)), new Vector3(size, 100000f, size));
        LookForBodies();
        AddShips(square);
        AddCharacters(square);
        AddRigidbodies(square);
        return _bodies;
    }

    // The ships and the other rigid bodies (with their colliders), looked for once a second (they come and go slowly).
    private void LookForBodies()
    {
        if (Time.frameCount - _shipsLookedFor < 60)
        {
            return;
        }

        _shipsLookedFor = Time.frameCount;
        _ships = UnityEngine.Object.FindObjectsByType<Ship>(FindObjectsSortMode.None);
        _rigidbodies.Clear();
        foreach (Rigidbody body in UnityEngine.Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
        {
            if (!body.isKinematic && body.GetComponentInParent<Ship>() == null && body.GetComponentInParent<Character>() == null)
            {
                _rigidbodies.Add((body, body.GetComponentsInChildren<Collider>()));
            }
        }
    }

    private bool RoomForBodies() => _bodies.Count < ParticleWater.MostBodies;

    private void AddShips(Bounds square)
    {
        foreach (Ship ship in _ships)
        {
            BoxCollider hull = ship?.m_floatCollider;
            if (hull == null || !hull.bounds.Intersects(square) || !RoomForBodies())
            {
                continue;
            }

            Transform t = hull.transform;
            _bodies.Add(ParticleWater.Body.Box(t.TransformPoint(hull.center), Vector3.Scale(hull.size, t.lossyScale) / 2f, t.rotation));
        }
    }

    private void AddCharacters(Bounds square)
    {
        foreach (Character character in Character.GetAllCharacters())
        {
            if (character == null || !RoomForBodies())
            {
                continue;
            }

            Transform t = character.transform;
            if (!square.Contains(new Vector3(t.position.x, 0f, t.position.z)))
            {
                continue;
            }

            CapsuleCollider capsule = character.GetComponent<CapsuleCollider>();
            Vector3 centre = capsule != null ? capsule.transform.TransformPoint(capsule.center) : t.position + (Vector3.up * 0.9f);
            float radius = capsule != null ? capsule.radius * Mathf.Max(t.lossyScale.x, t.lossyScale.z) : 0.4f;
            float height = capsule != null ? capsule.height * t.lossyScale.y : 1.8f;
            _bodies.Add(ParticleWater.Body.Box(centre, new Vector3(radius, height / 2f, radius), t.rotation));
        }
    }

    // Each solid collider of the rest (a cart is several) that reaches into the square and is not too small to matter
    // (under 10 cm across).
    private void AddRigidbodies(Bounds square)
    {
        foreach ((Rigidbody body, Collider[] colliders) in _rigidbodies)
        {
            if (body == null)
            {
                continue;
            }

            foreach (Collider collider in colliders)
            {
                if (collider == null || collider.isTrigger || !RoomForBodies())
                {
                    continue;
                }

                Bounds bounds = collider.bounds;
                if (bounds.Intersects(square) && (bounds.size.x >= 0.1f || bounds.size.z >= 0.1f))
                {
                    _bodies.Add(ParticleWater.Body.Box(bounds.center, bounds.extents, Quaternion.identity));
                }
            }
        }
    }

    // What these bricks hold of pieces (the server's reading of their colliders) over the square from voxel column x0,
    // z0, columns a side, from voxel row baseRow up ParticleSolids.Rows rows, laid out by ParticleSolids.Index: a byte a
    // voxel, ParticleSolids.Solid where the voxel is more than half solid, and the bits of the faces a piece closes
    // (BrickView.Open's six, inverted: a wall thinner than a voxel); and whether any is.
    private static byte[] Solids(List<BrickView> bricks, int x0, int z0, int columns, int baseRow, out bool any)
    {
        byte[] solids = new byte[columns * ParticleSolids.Rows * columns];
        any = false;
        foreach (BrickView brick in bricks)
        {
            int bx = (brick.X * Brick.Size) - x0, by = (brick.Y * Brick.Size) - baseRow, bz = (brick.Z * Brick.Size) - z0;
            if (bx + Brick.Size <= 0 || bx >= columns || bz + Brick.Size <= 0 || bz >= columns || by + Brick.Size <= 0 || by >= ParticleSolids.Rows)
            {
                continue;
            }

            for (int y = Math.Max(0, -by); y < Brick.Size && by + y < ParticleSolids.Rows; y++)
            {
                for (int z = Math.Max(0, -bz); z < Brick.Size && bz + z < columns; z++)
                {
                    for (int x = Math.Max(0, -bx); x < Brick.Size && bx + x < columns; x++)
                    {
                        int i = Brick.Index(x, y, z);
                        int closed = ~brick.Open[i] & BrickData.AllOpen;
                        byte built = (byte)((brick.Solid[i] > 0.5f ? ParticleSolids.Solid : 0) | closed);
                        if (built != 0)
                        {
                            solids[ParticleSolids.Index(columns, bx + x, by + y, bz + z)] = built;
                            any = true;
                        }
                    }
                }
            }
        }

        return solids;
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
        if (_volume != null)
        {
            // The square's new strip takes the server's water at the next frame, once the volume is bound to the moved
            // square (the volume's own water stays where it is in the world).
            _volume.Origin = new Vector3(window.X0 * window.VoxelSize, _volume.Origin.y, window.Z0 * window.VoxelSize);
            _volumeReseed = Math.Max(_volumeReseed, VolumeFollow.Reseed(dx, dz, per));
        }
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
