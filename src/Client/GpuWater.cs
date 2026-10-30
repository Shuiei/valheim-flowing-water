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

    // How far round a source, and from how high over it, the ground is cast where the server's water is not yet
    // (CastAroundSources), metres: a jet's outline's farthest, and over the highest it throws (60 degrees at its
    // fastest, about 5.5 m).
    private const float SourceReach = 20f, SourceRise = 8f;

    private static readonly int OnId = Shader.PropertyToID("_FWSimOn");
    private static readonly int OriginId = Shader.PropertyToID("_FWSimOrigin");
    private static readonly int SizeId = Shader.PropertyToID("_FWSimSize");
    private static readonly int WholeId = Shader.PropertyToID("_FWSimWhole");
    private static readonly int EdgeId = Shader.PropertyToID("_FWSimEdge");
    private static readonly int FilledId = Shader.PropertyToID("_FWSimFilled");
    private static readonly int BottomId = Shader.PropertyToID("_FWSimBottom");
    private static readonly int TopId = Shader.PropertyToID("_FWSimTop");
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

    // What is built round the square, as this game reads it (kept from one square to the next, until a world opens).
    private static BuiltSolids _built;

    // The bricks in the square this frame (kept, not made again each frame).
    private static readonly List<BrickView> NearBricks = [];

    // The placed sources, for whether one pours in the square.
    private static readonly List<(Vector3 At, float Rate)> NearSources = [];

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

    // Cells of the square's sides to seed from the server's water (the square moved: its strip is poured once the
    // moved square's water is built and the ground under it cast, ReseedReady; poured the next frame, the strip had
    // none of the server's water yet and stood empty until its water ran in from the side).
    private int _volumeReseed;

    // The part of the square the volume has filled, world x, z of its south-west and north-east corners: the square,
    // but for a strip a move uncovered until it is poured (the bricks draw it meanwhile, _FWSimFilled).
    private Vector4 _volumeFilled;

    // When the square last moved (Time.time), for the log's time to pour its strip; and the ground's casts taken in
    // since (a build of the moved square before any left its strip's ground unknown, so its water dry).
    private float _movedAt;

    private int _castsSinceMove;

    // Whether the square was found too wide for the volume (told once).
    private bool _volumeTooWide;

    private readonly List<(Vector3 At, float Rate, bool Drain, float Radius, SourceShape Shape)> _markers = [];

    private readonly List<VolumeWater.Spring> _springs = [];

    private readonly List<(Vector3 At, float Rate)> _sources = [];

    private readonly List<ParticleWater.Spout> _spouts = [];

    private static readonly List<ParticleWater.Spout> NoSpouts = [];

    private ParticleSurface _surface;

    // The volume water's own renderer.
    private VolumeSurface _volumeSurface;

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

    // The solids last taken onto the card, as laid out for it (for the console).
    private byte[] _solidsTaken;

    private readonly List<ParticleWater.Body> _bodies = new(ParticleWater.MostBodies);

    private Ship[] _ships = [];

    private readonly List<(Rigidbody Body, Collider[] Colliders)> _rigidbodies = [];

    private int _shipsLookedFor = -60;

    private int _buildingBase;

    // The version of what this game read as built (BuiltSolids) the build under way, and the last one, were made with.
    private int _buildingBuilt;

    private int _builtBuilt = -1;

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

    // The server's columns' tops and depths, for how much of deep water the volume moves.
    private float[] _deepTops;

    private float[] _deepDepths;

    // The depth of deep water last told of in the log (0: all of it moved).
    private float _deepTold;

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
    public static void WorldOpened()
    {
        Gate.Reset();
        _built = null;
    }

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

        string volume = _water._volume == null ? string.Empty : FormattableString.Invariant($"; the volume water from y = {_water._volume.Origin.y:0.0} to {_water._volume.Origin.y + _water._volume.Size.y:0.0} ({_water._volume.LastCount} particles, {_water._volume.FrothCount} of froth, the camera at {_water._cameraY:0.0}); filled from {_water._volumeFilled.x:0}, {_water._volumeFilled.y:0} to {_water._volumeFilled.z:0}, {_water._volumeFilled.w:0}{(_water._volumeReseed > 0 ? $", {_water._volumeReseed} cells of its sides to pour" : string.Empty)}");
        string built = _built == null ? string.Empty : FormattableString.Invariant($"; what is built round it: {_built.Objects} solid objects, {_built.Bricks.Count} blocks of voxels holding some, looked over in {_built.LookMilliseconds:0.0} ms");
        return FormattableString.Invariant(
            $"water on the graphics card: {w.Size:0} m square from {w.X0 * w.VoxelSize:0}, {w.Z0 * w.VoxelSize:0}, {w.Cells} cells of {w.CellSize:0.###} m; ground found under {100f * found / _water._ground.Length:0}% of it; {_water._grid.Substeps} steps a frame; {(_water._seeded ? "drawn" : "waiting for the server's water")}{volume}{built}");
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
        if (VolumeColumn(_water._volume, point) is { } column)
        {
            yield return column;
        }

        if (_water.SolidsColumn(point) is { } built)
        {
            yield return built;
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

        // No water in the square and no source pouring in it: nothing to move or draw (water far off does not keep the
        // card busy; a source does, though the server's water is not there: a jet's lands out of the square, and the
        // water near you flies it out). The bricks are
        // the square's the water is in, kept while the camera stays near its middle: the square round the camera as it is
        // now may be a brick row off it, and built from that one's, the kept square's far edge had none of the server's
        // water (the volume water there took the side for dry, its water ran out, and the pool stood a metre low).
        SimWindow kept = _water?._window ?? window;
        if (Near(kept, NearBricks).Count == 0 && !SourceIn(kept))
        {
            Stop(VoxelWater.Field.Bricks.Count == 0 ? "no water here" : "no water near you");
            return;
        }

        _water ??= Made(window, material, compute);
        _water._cameraY = camera.y;
        _water.Frame(NearBricks);
    }

    // What is built round the square, as this game reads it: the one reader, made when first asked for (let go when a
    // world opens). Main thread only, as every GpuWater call.
    private static BuiltSolids Built() => _built ??= new BuiltSolids();

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

        // The square and the grid follow the player, at the head (the camera when there is none: the menu's world):
        // a free camera, or a look from far off, does not drag the water away from where the player is.
        camera = Player.m_localPlayer is { } player ? player.GetHeadPoint() : main.transform.position;
        return true;
    }

    // Let go: the bricks draw all the water again.
    private static void Stop(string why)
    {
        _off = why;
        Shader.SetGlobalFloat(OnId, 0f);
        Shader.SetGlobalFloat(WholeId, 0f);
        _water?.Dispose();
        _water = null;
    }

    public void Dispose()
    {
        _cast.Dispose();
        _spray?.Dispose();
        _surface?.Dispose();
        _volumeSurface?.Dispose();
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
            _castsSinceMove++;
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
            _volumeSurface?.Dispose();
            _volumeSurface = null;
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
            // The volume draws the whole square itself (its water is its own, whatever the server's columns say), but
            // for the band along its sides where it fades into the bricks.
            Shader.SetGlobalFloat(WholeId, _volume != null ? 1f : 0f);
            Shader.SetGlobalFloat(EdgeId, VolumeFollow.BandMetres);
            Shader.SetGlobalVector(FilledId, _volume != null ? _volumeFilled : Square());
            // Within the grid's height only (it follows the camera): water in the square above or below it is the
            // bricks' to draw, as the water beyond the square is.
            Shader.SetGlobalFloat(BottomId, _volume != null ? _volume.Origin.y : 0f);
            Shader.SetGlobalFloat(TopId, _volume != null ? _volume.Origin.y + _volume.Size.y : 0f);
            _renderer.enabled = false;
            return;
        }

        _grid.Throws = _spray != null;
        _grid.Step(Time.deltaTime);
        Spray();
        Shader.SetGlobalFloat(OnId, 1f);
        Shader.SetGlobalFloat(WholeId, 0f);
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
            if (!Materials.Volume(out Materials.VolumeKit kit))
            {
                return false;
            }

            Settings settings = FlowingWaterPlugin.Settings;
            int rows = VolumeFollow.RowsWithin(VolumeFollow.Rows(settings.VolumeHeight.Value, _window.CellSize), _window.Cells);
            // And within what the solids' texture covers, with room for the grid to move before it is built again.
            rows = VolumeFollow.RowsWithinSolids(rows, _window.CellSize, _window.VoxelSize, ParticleSolids.Rows);
            if (rows < VolumeFollow.LeastRows)
            {
                if (!_volumeTooWide)
                {
                    _volumeTooWide = true;
                    FlowingWaterPlugin.Log.LogError(FormattableString.Invariant($"Volume water: a square of {_window.Cells} cells a side is too wide for the card ({VolumeFollow.MostCells} cells at most): the water near you stays a sheet; lower Client.VolumeRange or raise Client.GpuWaterCell"));
                }

                return false;
            }

            _volume = new VolumeWater(kit.Compute, kit.Particles, kit.Froth, settings.VolumeCapacity.Value, _window.Cells, rows, _window.Cells, _window.CellSize)
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
            _volumeReseed = 0;
            _volumeFilled = Square();
        }
        else if (_volumeReseed > 0 && ReseedReady())
        {
            // The square moved: its new strip takes the server's water, now that the moved square's water is built
            // and the ground under it cast.
            _volume.SeedFromServer(1f, _volumeReseed);
            if (FlowingWaterPlugin.Settings.Debug.Value)
            {
                FlowingWaterPlugin.Log.LogInfo(FormattableString.Invariant($"Volume water: the moved square's sides poured {_volumeReseed} cells wide, {Time.time - _movedAt:0.00} s after it moved"));
            }

            _volumeReseed = 0;
            _volumeFilled = Square();
        }

        _volume.FrothRate = FlowingWaterPlugin.Settings.VolumeFroth.Value;
        _volume.DeepKeep = DeepKeep();
        _volume.Step(Math.Min(Time.deltaTime, 1f / 15f));
        Camera camera = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
        if (camera != null && Materials.Volume(out Materials.VolumeKit look))
        {
            if (ParticleSurface.Spheres && Materials.Particles(out _, out Material surface, out _))
            {
                // As spheres, for testing (the console's "flowingwater particles spheres"): the particle renderer.
                _volumeSurface?.Detach();
                _surface ??= new ParticleSurface();
                _surface.Render(camera, _volume, surface, _volume.Froth);
            }
            else
            {
                _surface?.Detach();
                _volumeSurface ??= new VolumeSurface();
                _volumeSurface.Render(camera, _volume, look.March, look.Surface, _volume.Froth);
            }
        }

        return true;
    }

    // How much of the server's deep water the volume moves (VolumeFollow.DeepKeep), from the server's columns as last
    // built: all of it where the square's water in the grid fits the share of the particles' room it may take.
    private float DeepKeep()
    {
        if (_texels == null)
        {
            return _volume.DeepKeep;
        }

        int columns = _window.Columns * _window.Columns;
        if (_deepTops == null || _deepTops.Length != columns)
        {
            _deepTops = new float[columns];
            _deepDepths = new float[columns];
        }

        for (int k = 0; k < columns; k++)
        {
            _deepTops[k] = _texels[k * SimColumns.Texel];
            _deepDepths[k] = _texels[(k * SimColumns.Texel) + 2];
        }

        float bottom = _volume.Origin.y, top = bottom + (_volume.CellsY * _volume.CellSize);
        float budget = VolumeFollow.DeepShare * _volume.Capacity * _volume.Litre;
        float keep = VolumeFollow.DeepKeep(_deepTops, _deepDepths, _window.VoxelSize * _window.VoxelSize, bottom, top, budget, _volume.DeepKeep);
        // Told when the floor comes or goes, or moves half a metre since last told.
        if ((keep > 0f) != (_deepTold > 0f) || Math.Abs(keep - _deepTold) >= 0.5f)
        {
            _deepTold = keep;
            FlowingWaterPlugin.Log.LogInfo(keep > 0f
                ? FormattableString.Invariant($"Volume water: the square's water does not fit {_volume.Capacity} particles; it moves the top {keep:0.0} m of deep water, the server's water holding the rest")
                : "Volume water: the square's water fits its particles again; it moves all of it");
        }

        return keep;
    }

    // The placed sources and drains in the square, for the volume water (the server's fixed ones have no marker to
    // see: their water comes in at the square's edge as the server's other water does).
    private List<VolumeWater.Spring> Springs(Vector2 origin)
    {
        Marker.Markers(_markers);
        _springs.Clear();
        float size = _window.Cells * _window.CellSize;
        foreach ((Vector3 at, float rate, bool drain, float radius, SourceShape shape) in _markers)
        {
            if (at.x >= origin.x && at.z >= origin.y && at.x < origin.x + size && at.z < origin.y + size && _springs.Count < VolumeWater.MostSprings)
            {
                _springs.Add(drain ? VolumeWater.Spring.Drain(at + (Vector3.up * Prefabs.MarkerRadius), radius, rate) : shape.AllRound ? VolumeWater.Spring.SourceOn(at, rate) : Aimed(at, rate, shape, _window.CellSize));
            }
        }

        return _springs;
    }

    // A source facing a way, for the volume water: its outlet in front of its marker, as the shape has it, at its speed
    // (the server's, which lands its jet where this one does); its slot as high as passes the rate at that speed
    // across the cells it fills, two at least (FlowingWaterVolume's InSource), Regulate scaling its height to give the
    // rate. Sized for a point's quarter metre instead, and Regulate scaling its speed, the slot of a strong point's jet
    // was twice what passed its rate: the jet left at about half its speed and fell a few metres out, heaped there.
    private static VolumeWater.Spring Aimed(Vector3 foot, float rate, SourceShape shape, float cell)
    {
        Vec3 way = shape.Direction(0f);
        (float rightX, float rightZ) = shape.Right;
        float speed = shape.Speed(rate);
        float height = speed > 0f ? rate / (Math.Max(shape.OutletWidth, 2f * cell) * speed) : SourceShape.Nozzle;
        return VolumeWater.Spring.Aimed(foot, rate, new Vector3(way.X, way.Y, way.Z), new Vector2(rightX, rightZ), shape.OutletWidth, height, speed, shape.Spread * 0.5f * Mathf.Deg2Rad);
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
        CastAroundSources();
        MarkCastFirst();
        _builtBricks = _buildingBricks;
        _builtGround = _buildingGround;
        _builtFalls = _buildingFalls;
        if (build.Result.Solids != null)
        {
            _solids.Take(build.Result.Solids, _window.Columns, _window.VoxelSize, _buildingBase, build.Result.AnySolid);
            _solidsTaken = build.Result.Solids;
            _builtBase = _buildingBase;
            _builtBuilt = _buildingBuilt;
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
        // The solids round the volume's grid when there is one (built again once the grid leaves what they cover:
        // what is built past the texture's rows is ignored), else round the camera.
        int baseRow;
        bool rebased;
        if (_volume != null)
        {
            int bottom = (int)Math.Floor(_volume.Origin.y / _window.VoxelSize);
            int top = (int)Math.Ceiling((_volume.Origin.y + (_volume.CellsY * _volume.CellSize)) / _window.VoxelSize);
            baseRow = VolumeFollow.SolidsBase(bottom, top, ParticleSolids.Rows);
            rebased = solids && (_builtBase == int.MinValue || !VolumeFollow.SolidsCover(_builtBase, bottom, top, ParticleSolids.Rows));
        }
        else
        {
            baseRow = (int)Math.Floor(_cameraY / _window.VoxelSize) - (ParticleSolids.Rows / 2);
            rebased = solids && (_builtBase == int.MinValue || Math.Abs(baseRow - _builtBase) > 8);
        }

        // What is built round the square, as this game reads it: the server says it only where it has water.
        IReadOnlyDictionary<(int X, int Y, int Z), byte[]> built = null;
        int builtVersion = _builtBuilt;
        if (solids)
        {
            BuiltSolids reader = Built();
            reader.Update(_window.X0, _window.Z0, _window.Columns, rebased ? baseRow : _builtBase, _window.VoxelSize);
            built = reader.Bricks;
            builtVersion = reader.Version;
        }

        if (_build != null || Time.time - _builtAt < BuildSeconds || (_groundVersion == _builtGround && falls == _builtFalls && !rebased && builtVersion == _builtBuilt && _builtBricks != null && near.SequenceEqual(_builtBricks)))
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
        _buildingBuilt = builtVersion;
        int solidsBase = _buildingBase;
        _builtAt = Time.time;
        _build = Task.Run(() =>
        {
            float[] texels = SimColumns.Build(bricks, w.X0, w.Z0, w.Columns, w.VoxelSize, ground, w.PerVoxel, out float[] cast, out float[] layers, falls);
            bool any = false;
            byte[] voxels = null;
            if (solids)
            {
                voxels = Solids(bricks, w.X0, w.Z0, w.Columns, solidsBase, out any);
                any |= BuiltVoxels.Write(built, voxels, w.X0, w.Z0, w.Columns, solidsBase);
            }

            return (texels, layers, cast, voxels, any);
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
    // (BrickView.Open's six, inverted: a wall thinner than a voxel); and whether any is. Only a voxel with water says
    // either: a dry one is sent as nothing (its open faces 0, not "all closed"), and read as all closed it would put a
    // lid over the server's water wherever the voxel above it is dry, water pushed up under it piling there.
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
                        int closed = brick.Water[i] > 0f ? ~brick.Open[i] & BrickData.AllOpen : 0;
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
            _volumeReseed = Math.Min(window.Cells / 2, _volumeReseed + VolumeFollow.Reseed(dx, dz, per));
            _movedAt = Time.time;
            _castsSinceMove = 0;
            Vector4 square = Square();
            _volumeFilled = new Vector4(Math.Max(_volumeFilled.x, square.x), Math.Max(_volumeFilled.y, square.y), Math.Min(_volumeFilled.z, square.z), Math.Min(_volumeFilled.w, square.w));
        }
        _object.transform.position = new Vector3(window.X0 * window.VoxelSize, 0f, window.Z0 * window.VoxelSize);
    }

    // Round a source in the square, the ground is looked for where the server's water is not yet: from SourceRise
    // over the source, SourceReach round it (as far as a jet's outline goes), so the water near you flies a jet out
    // onto ground it knows and lands it there (cast only from the server's water, it knew none past the source's foot,
    // and a jet's water fell through the ground it did not know). Where the server's water is, it is cast from that.
    private void CastAroundSources()
    {
        Marker.Sources(NearSources);
        int columns = _window.Columns, reach = (int)Math.Ceiling(SourceReach / _window.VoxelSize);
        foreach ((Vector3 at, _) in NearSources)
        {
            int cx = (int)Math.Floor(at.x / _window.VoxelSize) - _window.X0, cz = (int)Math.Floor(at.z / _window.VoxelSize) - _window.Z0;
            for (int vz = Math.Max(0, cz - reach); vz <= Math.Min(columns - 1, cz + reach); vz++)
            {
                for (int vx = Math.Max(0, cx - reach); vx <= Math.Min(columns - 1, cx + reach); vx++)
                {
                    int k = (vz * columns) + vx;
                    if (float.IsNaN(_castFrom[k]) && ((vx - cx) * (vx - cx)) + ((vz - cz) * (vz - cz)) <= reach * reach)
                    {
                        _castFrom[k] = at.y + SourceRise;
                    }
                }
            }
        }
    }

    // The square, world x, z of its south-west and north-east corners.
    private Vector4 Square()
    {
        float x = _window.X0 * _window.VoxelSize, z = _window.Z0 * _window.VoxelSize;
        return new Vector4(x, z, x + _window.Size, z + _window.Size);
    }

    // Whether a moved square's strip may be poured: its water built for the square as it is (taken since the move),
    // from the ground as it is now and cast since the move, and no row with water still waiting for its ground. The
    // first build after a move knew no ground under the strip: its water was dry there, and the strip poured nothing.
    private bool ReseedReady() => _castFrom != null && _castsSinceMove > 0 && _builtGround == _groundVersion && Array.IndexOf(_castFirst, true) < 0;

    // Whether a placed source stands in the square.
    private static bool SourceIn(SimWindow window)
    {
        Marker.Sources(NearSources);
        float west = window.X0 * window.VoxelSize, south = window.Z0 * window.VoxelSize, side = window.Columns * window.VoxelSize;
        foreach ((Vector3 at, _) in NearSources)
        {
            if (at.x >= west && at.x < west + side && at.z >= south && at.z < south + side)
            {
                return true;
            }
        }

        return false;
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

        var corner = new Vector2(_window.X0 * _window.VoxelSize, _window.Z0 * _window.VoxelSize);
        float cell = _window.CellSize;
        float[] found = _ground;
        int cells = _window.Cells;
        _cast.Start(row, rows, (x, z) => OverTerrain(from[((z / perVoxel) * columns) + (x / perVoxel)], found[(z * cells) + x], corner.x + ((x + 0.5f) * cell), corner.y + ((z + 0.5f) * cell)), corner, cell);
    }

    // What the card takes as built in the voxel column at a point, for the console: each row holding any, with its
    // letters (S solid; faces closed: E east, W west, U up, D down, N north, s south); null with none taken there.
    private string SolidsColumn(Vector3 point)
    {
        if (_solidsTaken == null || !_solidsWindow.Equals(_window))
        {
            return null;
        }

        SimWindow w = _window;
        int x = (int)Math.Floor(point.x / w.VoxelSize) - w.X0, z = (int)Math.Floor(point.z / w.VoxelSize) - w.Z0;
        if (x < 0 || z < 0 || x >= w.Columns || z >= w.Columns)
        {
            return null;
        }

        var rows = new System.Text.StringBuilder();
        for (int y = 0; y < SolidsLayout.Rows; y++)
        {
            byte b = _solidsTaken[SolidsLayout.Index(w.Columns, x, y, z)];
            if (b == 0)
            {
                continue;
            }

            rows.Append(FormattableString.Invariant($" {(_builtBase + y) * w.VoxelSize:0.0}:"));
            rows.Append((b & SolidsLayout.Solid) != 0 ? "S" : string.Empty).Append((b & SolidsLayout.ClosedEast) != 0 ? "E" : string.Empty)
                .Append((b & SolidsLayout.ClosedWest) != 0 ? "W" : string.Empty).Append((b & SolidsLayout.ClosedUp) != 0 ? "U" : string.Empty)
                .Append((b & SolidsLayout.ClosedDown) != 0 ? "D" : string.Empty).Append((b & SolidsLayout.ClosedNorth) != 0 ? "N" : string.Empty)
                .Append((b & SolidsLayout.ClosedSouth) != 0 ? "s" : string.Empty);
        }

        return FormattableString.Invariant($"the card's solids in the voxel column at {point.x:0.0},{point.z:0.0}, by row bottom (m):{(rows.Length == 0 ? " none" : rows.ToString())}");
    }

    // The volume water's own column at a point, for the console: what the drawing takes as the water standing on the
    // ground there, and each cell's particles; null with no volume, or outside its grid.
    private static string VolumeColumn(VolumeWater volume, Vector3 point)
    {
        if (volume == null || !volume.ReadColumn(point, out float top, out float depth, out int[] counts))
        {
            return null;
        }

        var rows = new System.Text.StringBuilder();
        for (int y = 0; y < counts.Length; y++)
        {
            if (counts[y] != 0)
            {
                rows.Append(FormattableString.Invariant($" {volume.Origin.y + (y * volume.CellSize):0.0}:{counts[y]}"));
            }
        }

        return FormattableString.Invariant($"volume column at {point.x:0.0},{point.z:0.0}: sheet top {volume.Origin.y + top:0.00} depth {depth:0.00}; particles by cell bottom (m):{rows}");
    }

    // A ray's start (NaN: not cast) no lower than just over the terrain as this game sees it at x, z: the server's water
    // gives the height, and where its ground is not the game's (a terrain edit it has not read) that water lies inside
    // the hill, the ray would start in the ground, meet nothing and make the column a wall, with any water in it held
    // there. The terrain has no overhang, so a ray from over it still finds every floor and roof under its start. The
    // terrain is asked for (a raycast of its own, on this thread) only where it can matter: a cell whose ground is not
    // found yet (none cast, or its last ray met nothing), or a start under the ground its last ray found (which was
    // over the terrain, or on it).
    private static float OverTerrain(float from, float lastFound, float x, float z)
    {
        bool known = !float.IsNaN(lastFound) && lastFound < BedCast.Wall;
        if (float.IsNaN(from) || (known && from >= lastFound) || ZoneSystem.instance == null || !ZoneSystem.instance.GetGroundHeight(new Vector3(x, from, z), out float terrain))
        {
            return from;
        }

        return Math.Max(from, terrain + SimColumns.CastOverWater);
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
