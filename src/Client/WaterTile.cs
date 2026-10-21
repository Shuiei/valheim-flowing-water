using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using FlowingWater.Server;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlowingWater.Client;

/// <summary>
/// A zone's water as a player sees it: the depths the server last sent, eased towards over a
/// moment, drawn as a mesh over the ground (still water and running streams as two materials),
/// and asked for the water surface and the current at any point by the game's water checks.
/// </summary>
internal sealed class WaterTile : MonoBehaviour
{
    private const float NoWater = -10000f;

    private const float WetDepth = 0.02f;

    private const float DrySink = 0.4f;

    private const float EaseRate = 5f;

    private const float HeightSampleSeconds = 1f;

    // While the water only eases towards the last data, the mesh is rebuilt at most this often.
    private const float RebuildSeconds = 0.05f;

    // The game's heightmap: a vertex every metre.
    private const int GroundPitch = TileData.ZoneMetres + 1;

    private const float MinDrawnDepth = 0.1f;

    // How far over its own ground a smoothed shore vertex may be drawn.
    private const float ShoreCreep = 0.05f;

    private const float ShoreDip = 0.02f;

    private const float StreamTextureScale = 0.5f;

    // The water speed at which the stream texture runs at its material's own speed; slower water
    // gets a denser texture that moves slower, faster water a stretched one that moves faster.
    private const float ReferenceSpeed = 1f;

    private const float StillTextureScale = 0.125f;

    // White water from the surface's slope: none below the first, all of it from the second (rise
    // over run): rapids and falls foam, a stream down a gentle slope does not.
    private const float SteepFoamFrom = 0.45f;

    private const float SteepFoamFull = 1.2f;

    // White water (sound, and spray where steep) where the water is this deep and runs this fast, at
    // most this many places a tile, this far apart, looked for this often.
    private const float SpotDepth = 0.03f, SpotSpeed = 1.2f;

    private const int MaxSpots = 6;

    private const float SpotSpacing = 6f, SpotSeconds = 0.5f;

    private static readonly Dictionary<long, WaterTile> ByZone = [];

    // The four vertices beside a vertex, as grid steps.
    private static readonly (int X, int Z)[] Sides = [(1, 0), (-1, 0), (0, 1), (0, -1)];

    // The eight tiles around: each reads this one's edge (its border heights, and the heights next to
    // the border for its normals).
    private static readonly (int X, int Z)[] Around = [(-1, -1), (0, -1), (1, -1), (-1, 0), (1, 0), (-1, 1), (0, 1), (1, 1)];

    private static readonly int WaterTimeId = Shader.PropertyToID("_WaterTime");

    private static readonly int VisibleMaxDistanceId = Shader.PropertyToID("_VisibleMaxDistance");

    private readonly List<int> _stillTriangles = [];

    private readonly List<int> _streamTriangles = [];

    private ZNetView _nview;

    // Cells per side, as the server last sent them; every array below is sized for it.
    private int _size;

    private float _cell;

    private float[] _depth;

    private float[] _drawDepth;

    private float[] _vertexY;

    private float[] _vertexYRaw;

    private bool[] _vertexSunk;

    private float[] _velX;

    private float[] _velZ;

    private float[] _height;

    private bool[] _vertexWet;

    // Each grid vertex's place in the mesh, which holds only the vertices of quads with water.
    private int[] _meshIndex;

    private readonly float[] _ground = new float[GroundPitch * GroundPitch];

    private readonly List<Vector3> _vertices = [];

    private readonly List<Vector2> _uvs = [];

    private readonly List<Vector3> _normals = [];

    private readonly List<Vector4> _tangents = [];

    // What the tiles around last read from this one's edge, to tell them only when it changed.
    private double _edgeSeen = double.NaN;

    private float _rebuildTimer;

    private WaterEffects _effects;

    private float _spotTimer;

    private readonly List<WaterSpot> _spots = [];

    private readonly List<(float Score, WaterSpot Spot)> _candidates = [];

    private TileData _target;

    private Mesh _mesh;

    private MeshRenderer _renderer;

    private BoxCollider _trigger;

    private MaterialPropertyBlock _properties;

    private uint _revision = uint.MaxValue;

    private bool _haveData;

    private bool _haveHeights;

    private bool _dirty;

    private float _heightTimer;

    private bool _flatApplied;

    // Drawn with the mod's shader, which reads depth, shore and current from the vertices.
    private bool _customShader;

    // The CustomShader setting the materials were last chosen with; a change in the config applies at once.
    private bool _customApplied;

    private readonly List<Vector4> _water = [];

    private readonly List<Vector2> _current = [];

    private float _liftApplied;

    /// <summary>Metres the drawn water is raised by, for testing (console `flowingwater lift`).</summary>
    internal static float Lift { get; set; }

    /// <summary>Draw with plain opaque colours instead of the water materials, for testing.</summary>
    internal static bool Flat { get; set; }

    public Vector2s Zone { get; private set; }

    public bool Ready => _haveData && _haveHeights;

    private int Pitch => _size + 1;

    /// <summary>The water surface over a point, as drawn; well under the ground where there is none, or -10000 with no tile.</summary>
    public static float SurfaceAt(Vector3 point)
    {
        if (!Find(point, out WaterTile tile, out int x, out int z, out float fx, out float fz))
        {
            return NoWater;
        }

        int pitch = tile.Pitch;
        return Bilinear(tile._vertexY[(z * pitch) + x], tile._vertexY[(z * pitch) + x + 1], tile._vertexY[((z + 1) * pitch) + x], tile._vertexY[((z + 1) * pitch) + x + 1], fx, fz);
    }

    /// <summary>The water's velocity at a point; false where there is none or it is still.</summary>
    public static bool CurrentAt(Vector3 point, out Vector3 current)
    {
        current = Vector3.zero;
        if (!Find(point, out WaterTile tile, out int x, out int z, out float fx, out float fz))
        {
            return false;
        }

        tile.VertexVelocity(x, z, out float ax, out float az);
        tile.VertexVelocity(x + 1, z, out float bx, out float bz);
        tile.VertexVelocity(x, z + 1, out float cx, out float cz);
        tile.VertexVelocity(x + 1, z + 1, out float dx, out float dz);
        current = new Vector3(Bilinear(ax, bx, cx, dx, fx, fz), 0f, Bilinear(az, bz, cz, dz, fx, fz));
        return current.sqrMagnitude > 0.0025f;
    }

    /// <summary>What is under a point, for the console.</summary>
    public static string Describe(Vector3 point)
    {
        Vector2s zone = ZoneSystem.GetZone(point);
        if (!ByZone.TryGetValue(TileData.Key(zone), out WaterTile tile))
        {
            return $"zone {zone}: no water tile";
        }

        if (!tile.Ready)
        {
            return $"zone {zone}: water tile not ready (data {tile._haveData}, ground {tile._haveHeights})";
        }

        TileData.CellOf(point, tile._size, out int x, out int z);
        int i = tile.Index(x, z);
        float total = 0f;
        int wet = 0;
        for (int c = 0; c < tile._depth.Length; c++)
        {
            total += tile._depth[c];
            if (tile._depth[c] > WetDepth)
            {
                wet++;
            }
        }

        total *= tile._cell * tile._cell;
        return FormattableString.Invariant($"zone {zone} cell {x},{z} of {tile._cell} m: ground {tile._height[(z * tile.Pitch) + x]:0.00} depth {tile._depth[i]:0.00} m, current {tile._velX[i]:0.00},{tile._velZ[i]:0.00} m/s; tile {total:0} m³ over {wet} cells");
    }

    private static bool Find(Vector3 point, out WaterTile tile, out int x, out int z, out float fx, out float fz)
    {
        x = 0;
        z = 0;
        fx = 0f;
        fz = 0f;
        Vector2s zone = ZoneSystem.GetZone(point);
        if (!ByZone.TryGetValue(TileData.Key(zone), out tile) || !tile.Ready)
        {
            return false;
        }

        TileData.CellOf(point, tile._size, out x, out z);
        fx = Mathf.Clamp01((point.x - TileData.OriginX(zone) - (x * tile._cell)) / tile._cell);
        fz = Mathf.Clamp01((point.z - TileData.OriginZ(zone) - (z * tile._cell)) / tile._cell);
        return true;
    }

    private static float Bilinear(float a, float b, float c, float d, float fx, float fz)
    {
        return Mathf.Lerp(Mathf.Lerp(a, b, fx), Mathf.Lerp(c, d, fx), fz);
    }

    private static float Ease(float shown, float target, float k, ref bool moving)
    {
        if (Mathf.Abs(shown - target) <= 0.001f)
        {
            return target;
        }

        moving = true;
        return shown + ((target - shown) * k);
    }

    private int Index(int x, int z)
    {
        return (z * _size) + x;
    }

    // The depth shown at a vertex; the last row and column belong to the next zone's tile.
    private float VertexDepth(int x, int z)
    {
        if (x < _size && z < _size)
        {
            return _drawDepth[Index(x, z)];
        }

        WaterTile other = Beside(ref x, ref z);
        return other == null ? 0f : other._drawDepth[other.Index(x, z)];
    }

    // The depth drawn at each cell: the water surface averaged with the wet cells around it, so
    // the step-by-step spreading's one-cell ripples do not show as terraces. Dry cells stay dry.
    private void SmoothForDrawing()
    {
        for (int z = 0; z < _size; z++)
        {
            for (int x = 0; x < _size; x++)
            {
                int i = Index(x, z);
                if (_depth[i] <= WetDepth)
                {
                    _drawDepth[i] = 0f;
                    continue;
                }

                float surface = 0f;
                int count = 0;
                int wetAround = 0;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        int nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= _size || nz >= _size)
                        {
                            // The next tile's cells are not at hand: count them wet, no thinning there.
                            wetAround++;
                            continue;
                        }

                        int j = Index(nx, nz);
                        if (_depth[j] > WetDepth)
                        {
                            surface += _height[(nz * Pitch) + nx] + _depth[j];
                            count++;
                            wetAround++;
                        }
                    }
                }

                // Thinner towards the water's edge (fewer wet neighbours), so the shore fades out.
                float least = MinDrawnDepth * (wetAround - 1) / 8f;
                _drawDepth[i] = Mathf.Max((surface / count) - _height[(z * Pitch) + x], least);
            }
        }
    }

    private void VertexVelocity(int x, int z, out float vx, out float vz)
    {
        if (x < _size && z < _size)
        {
            int i = Index(x, z);
            vx = _velX[i];
            vz = _velZ[i];
            return;
        }

        WaterTile other = Beside(ref x, ref z);
        if (other == null)
        {
            vx = 0f;
            vz = 0f;
            return;
        }

        int j = other.Index(x, z);
        vx = other._velX[j];
        vz = other._velZ[j];
    }

    // The tile holding a point of this tile's grid that lies in the next zone or the one before,
    // and the point's place on that tile's grid.
    private WaterTile Beside(ref int x, ref int z)
    {
        int zx = Zone.x + Shift(ref x);
        int zy = Zone.y + Shift(ref z);

        // A neighbour still on another grid (the server's changed a moment ago) joins up once it is sent again.
        return ByZone.TryGetValue(TileData.Key(new Vector2s(zx, zy)), out WaterTile other) && other.Ready && other._size == _size ? other : null;
    }

    private int Shift(ref int v)
    {
        if (v >= _size)
        {
            v -= _size;
            return 1;
        }

        if (v < 0)
        {
            v += _size;
            return -1;
        }

        return 0;
    }

    // A wet vertex sits on its water (never thinner than MinDrawnDepth, so a stream shows). A dry
    // vertex beside a wet one carries that surface on, just under its own ground, so the surface
    // runs into the ground and the shader fades it out there: the shore. Other dry vertices are
    // sunk out of sight.
    private float VertexY(int x, int z, out bool sunk)
    {
        sunk = false;
        float depth = VertexDepth(x, z);
        float ground = _height[(z * Pitch) + x];
        if (depth > WetDepth)
        {
            return ground + depth;
        }

        float carried = float.MinValue;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                int nx = x + dx;
                int nz = z + dz;
                if ((dx == 0 && dz == 0) || nx < 0 || nz < 0 || nx >= Pitch || nz >= Pitch)
                {
                    continue;
                }

                float neighbourDepth = VertexDepth(nx, nz);
                if (neighbourDepth > WetDepth)
                {
                    carried = Mathf.Max(carried, _height[(nz * Pitch) + nx] + neighbourDepth);
                }
            }
        }

        sunk = carried == float.MinValue;
        return sunk ? ground - DrySink : Mathf.Min(carried, ground - ShoreDip);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Awake()
    {
        _nview = GetComponent<ZNetView>();
        if (_nview == null || !_nview.IsValid() || (ZNet.instance != null && ZNet.instance.IsDedicated()))
        {
            enabled = false;
            return;
        }

        Zone = ZoneSystem.GetZone(transform.position);
        _target = new TileData(Zone);
        Allocate(_target.Size);
        _renderer = GetComponent<MeshRenderer>();
        _trigger = GetComponentInChildren<BoxCollider>();
        _mesh = new Mesh { name = "FlowingWater tile" };
        _mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = _mesh;
        ApplyMaterials();
        _effects = gameObject.AddComponent<WaterEffects>();
        ByZone[TileData.Key(Zone)] = this;
    }

    // Arrays for a grid of this many cells per side; what was shown is dropped (the new data is shown
    // at once, the ground sampled again).
    private void Allocate(int size)
    {
        _size = size;
        _cell = (float)TileData.ZoneMetres / size;
        int cells = size * size;
        int vertices = Pitch * Pitch;
        _depth = new float[cells];
        _drawDepth = new float[cells];
        _velX = new float[cells];
        _velZ = new float[cells];
        _vertexY = new float[vertices];
        _vertexYRaw = new float[vertices];
        _vertexSunk = new bool[vertices];
        _height = new float[vertices];
        _vertexWet = new bool[vertices];
        _meshIndex = new int[vertices];

        // Nothing drawn until the first rebuild (a neighbour may read it before).
        for (int i = 0; i < vertices; i++)
        {
            _meshIndex[i] = -1;
        }
        _haveHeights = false;
        _heightTimer = 0f;
        _dirty = true;
        // Not made yet when called from Awake.
        _mesh?.Clear(false);
    }

    // The water materials, or in flat mode (for testing) plain opaque colours: blue still, green stream.
    private void ApplyMaterials()
    {
        _flatApplied = Flat;
        _customApplied = FlowingWaterPlugin.Settings.CustomShader.Value;

        // The two kinds of material read different vertex data: the mesh is rebuilt either way.
        _dirty = true;
        Material custom = !Flat && FlowingWaterPlugin.Settings.CustomShader.Value ? Materials.Custom() : null;
        _customShader = custom != null;
        if (_customShader)
        {
            _renderer.sharedMaterials = [custom, custom];
            _properties = new MaterialPropertyBlock();
            return;
        }

        Material still = Flat ? Materials.Plain("FlowingWater flat still", new Color(0.1f, 0.3f, 0.9f)) : Materials.Still();
        Material stream = Flat ? Materials.Plain("FlowingWater flat stream", new Color(0.1f, 0.9f, 0.3f)) : Materials.Flow();
        if (still == null && stream == null)
        {
            FlowingWaterPlugin.Log.LogError("No water material: flowing water is simulated but invisible");
            _renderer.enabled = false;
            _properties = null;
            return;
        }

        _renderer.sharedMaterials = [still ?? stream, stream ?? still];
        _properties = new MaterialPropertyBlock();
        _properties.SetFloat(VisibleMaxDistanceId, ZNet.instance.GetSyncedSimulationDistance().NearSimulationDistance * ZoneSystem.c_ZoneSize);
    }

    /// <summary>What the tile under a point draws, for the console.</summary>
    public static string DebugInfo(Vector3 point)
    {
        Vector2s zone = ZoneSystem.GetZone(point);
        if (!ByZone.TryGetValue(TileData.Key(zone), out WaterTile tile))
        {
            return $"zone {zone}: no water tile";
        }

        var text = new System.Text.StringBuilder();
        text.Append(FormattableString.Invariant($"zone {zone}: at {tile.transform.position}, ready {tile.Ready}, renderer {tile._renderer.enabled}, lift {Lift}, flat {Flat}"));
        text.Append(FormattableString.Invariant($"; mesh {tile._mesh.vertexCount} vertices, still {tile._stillTriangles.Count / 3} stream {tile._streamTriangles.Count / 3} triangles, bounds {tile._mesh.bounds.min} to {tile._mesh.bounds.max}"));
        foreach (Material material in tile._renderer.sharedMaterials)
        {
            text.Append(material == null
                ? "; material none"
                : FormattableString.Invariant($"; material {material.name} shader {material.shader.name} supported {material.shader.isSupported} queue {material.renderQueue} keywords {string.Join(" ", material.shaderKeywords)}"));
        }

        return text.ToString();
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Update()
    {
        if (!_nview.IsValid())
        {
            return;
        }

        float dt = Time.deltaTime;
        ZDO zdo = _nview.GetZDO();
        if (zdo.DataRevision != _revision)
        {
            _revision = zdo.DataRevision;
            byte[] data = zdo.GetByteArray(WaterServer.DataHash);
            if (data != null && _target.Decode(data))
            {
                if (_target.Size != _size)
                {
                    Allocate(_target.Size);
                    Array.Copy(_target.Depth, _depth, _depth.Length);
                    Array.Copy(_target.VelX, _velX, _velX.Length);
                    Array.Copy(_target.VelZ, _velZ, _velZ.Length);
                }

                _haveData = true;
                _dirty = true;
            }
        }

        _heightTimer -= dt;
        if (_heightTimer <= 0f)
        {
            _heightTimer = HeightSampleSeconds;
            SampleHeights();
        }

        if (!Ready)
        {
            _renderer.enabled = false;
            return;
        }

        if (_flatApplied != Flat || _customApplied != FlowingWaterPlugin.Settings.CustomShader.Value)
        {
            ApplyMaterials();
        }

        if (_liftApplied != Lift)
        {
            _liftApplied = Lift;
            _dirty = true;
        }

        float k = 1f - Mathf.Exp(-EaseRate * dt);
        bool moving = false;
        for (int i = 0; i < _depth.Length; i++)
        {
            _depth[i] = Ease(_depth[i], _target.Depth[i], k, ref moving);
            _velX[i] = Ease(_velX[i], _target.VelX[i], k, ref moving);
            _velZ[i] = Ease(_velZ[i], _target.VelZ[i], k, ref moving);
        }

        _rebuildTimer -= dt;
        if (_dirty || (moving && _rebuildTimer <= 0f))
        {
            _dirty = false;
            _rebuildTimer = RebuildSeconds;
            Rebuild();
        }

        _spotTimer -= dt;
        if (_spotTimer <= 0f)
        {
            _spotTimer = SpotSeconds;
            ShowWhiteWater();
        }

        if (_properties != null)
        {
            _renderer.enabled = true;
            _properties.SetFloat(WaterTimeId, ZNet.instance.GetWrappedDayTimeSeconds());
            _renderer.SetPropertyBlock(_properties);
        }
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        if (ByZone.TryGetValue(TileData.Key(Zone), out WaterTile tile) && tile == this)
        {
            ByZone.Remove(TileData.Key(Zone));
        }

        if (_mesh != null)
        {
            Destroy(_mesh);
        }
    }

    // How far inside the water a vertex is, 0 at its very edge to 1 among wet vertices only: the
    // share of its eight neighbours that are wet. The shader fades the edge out by it. Neighbours
    // beyond the tile count as wet, so tile borders do not fade.
    private float Shore(int x, int z)
    {
        if (VertexDepth(x, z) <= WetDepth)
        {
            return 0f;
        }

        int wet = 0;
        for (int dz = -1; dz <= 1; dz++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (dx == 0 && dz == 0)
                {
                    continue;
                }

                int nx = x + dx;
                int nz = z + dz;
                if (nx < 0 || nz < 0 || nx >= Pitch || nz >= Pitch || VertexDepth(nx, nz) > WetDepth)
                {
                    wet++;
                }
            }
        }

        return wet / 8f;
    }

    // The ground under every vertex: the game's heightmap read at its own one-metre vertices, and
    // between them as the game does (bilinear), so a finer grid costs no more lookups.
    private void SampleHeights()
    {
        bool complete = true;
        bool changed = false;
        var point = new Vector3(0f, 0f, 0f);
        float originX = TileData.OriginX(Zone);
        float originZ = TileData.OriginZ(Zone);
        for (int z = 0; z < GroundPitch; z++)
        {
            point.z = originZ + z;
            for (int x = 0; x < GroundPitch; x++)
            {
                point.x = originX + x;
                int i = (z * GroundPitch) + x;
                if (Heightmap.GetHeight(point, out float height))
                {
                    changed |= height != _ground[i];
                    _ground[i] = height;
                }
                else if (x < TileData.ZoneMetres && z < TileData.ZoneMetres)
                {
                    complete = false;
                }
                else
                {
                    // The next zone's ground is not loaded: carry on the edge.
                    _ground[i] = _ground[x == TileData.ZoneMetres ? i - 1 : i - GroundPitch];
                }
            }
        }

        if (changed || !_haveHeights)
        {
            for (int z = 0; z < Pitch; z++)
            {
                for (int x = 0; x < Pitch; x++)
                {
                    _height[(z * Pitch) + x] = GroundAt(x * _cell, z * _cell);
                }
            }
        }

        _haveHeights = complete;
        _dirty |= changed;
    }

    private float GroundAt(float x, float z)
    {
        int x0 = Mathf.Min(Mathf.FloorToInt(x), GroundPitch - 2);
        int z0 = Mathf.Min(Mathf.FloorToInt(z), GroundPitch - 2);
        float fx = x - x0;
        float fz = z - z0;
        int i = (z0 * GroundPitch) + x0;
        return Bilinear(_ground[i], _ground[i + 1], _ground[i + GroundPitch], _ground[i + GroundPitch + 1], fx, fz);
    }

    // Which vertices have water, and which are in the mesh: the corners of every quad with a wet
    // corner. The rest are sunk and not drawn.
    private void MarkVertices()
    {
        for (int z = 0; z < Pitch; z++)
        {
            for (int x = 0; x < Pitch; x++)
            {
                int i = (z * Pitch) + x;
                _vertexWet[i] = VertexDepth(x, z) > WetDepth;
                _meshIndex[i] = -1;
            }
        }

        for (int z = 0; z < _size; z++)
        {
            for (int x = 0; x < _size; x++)
            {
                int a = (z * Pitch) + x;
                if (_vertexWet[a] || _vertexWet[a + 1] || _vertexWet[a + Pitch] || _vertexWet[a + Pitch + 1])
                {
                    _meshIndex[a] = 0;
                    _meshIndex[a + 1] = 0;
                    _meshIndex[a + Pitch] = 0;
                    _meshIndex[a + Pitch + 1] = 0;
                }
            }
        }

        // Numbered in grid order, the order the mesh is filled in.
        int count = 0;
        for (int i = 0; i < _meshIndex.Length; i++)
        {
            if (_meshIndex[i] == 0)
            {
                _meshIndex[i] = count++;
            }
        }
    }

    // The height of every vertex: the water's, carried on just under the ground beside it, sunk
    // elsewhere; then the vertices in the mesh are averaged with their neighbours in it, which
    // rounds the steps of the shore. A dry vertex may rise at most ShoreCreep over its ground that way.
    private void ComputeVertexHeights()
    {
        for (int z = 0; z < Pitch; z++)
        {
            for (int x = 0; x < Pitch; x++)
            {
                int i = (z * Pitch) + x;
                bool drawn = _meshIndex[i] >= 0;
                _vertexYRaw[i] = drawn ? VertexY(x, z, out _) : _height[i] - DrySink;
                _vertexSunk[i] = !drawn;
            }
        }

        for (int z = 0; z < Pitch; z++)
        {
            for (int x = 0; x < Pitch; x++)
            {
                int i = (z * Pitch) + x;
                if (_vertexSunk[i])
                {
                    _vertexY[i] = _vertexYRaw[i];
                    continue;
                }

                float sum = 0f;
                int count = 0;
                for (int dz = -1; dz <= 1; dz++)
                {
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int nx = x + dx;
                        int nz = z + dz;
                        if (nx < 0 || nz < 0 || nx >= Pitch || nz >= Pitch)
                        {
                            continue;
                        }

                        int j = (nz * Pitch) + nx;
                        if (!_vertexSunk[j])
                        {
                            sum += _vertexYRaw[j];
                            count++;
                        }
                    }
                }

                float y = sum / count;
                if (!_vertexWet[i])
                {
                    y = Mathf.Min(y, _height[i] + ShoreCreep);
                }

                _vertexY[i] = y;
            }
        }
    }

    // How steep the drawn water surface is at a wet vertex, as a share of white water (0 to 1), from
    // the wet vertices beside it, on this tile or the next. A vertex of the last row or column is the
    // next tile's, so the two draw it with the same foam.
    private float SteepFoam(int x, int z)
    {
        int ox = x;
        int oz = z;
        WaterTile owner = Beside(ref ox, ref oz);
        return owner != null ? owner.OwnSteepFoam(ox, oz) : OwnSteepFoam(x, z);
    }

    private float OwnSteepFoam(int x, int z) => Mathf.Clamp01((Slope(x, z) - SteepFoamFrom) / (SteepFoamFull - SteepFoamFrom));

    // The drawn water surface's slope at a vertex (rise over run), from the wet vertices beside it, on
    // this tile or the next; 0 where dry.
    private float Slope(int x, int z)
    {
        if (!WetSurface(x, z, out float y))
        {
            return 0f;
        }

        float steepest = 0f;
        foreach ((int dx, int dz) in Sides)
        {
            if (WetSurface(x + dx, z + dz, out float ny))
            {
                steepest = Mathf.Max(steepest, Mathf.Abs(ny - y) / _cell);
            }
        }

        return steepest;
    }

    // White water at the tile's spots; with spray off and no sound, none (and the spots are not looked for).
    private void ShowWhiteWater()
    {
        if (FlowingWaterPlugin.Settings.Spray.Value || FlowingWaterPlugin.Settings.WaterVolume.Value > 0f)
        {
            FindSpots();
        }
        else if (_spots.Count == 0)
        {
            return;
        }
        else
        {
            _spots.Clear();
        }

        _effects.Show(_spots);
    }

    // The places where most water runs fastest and steepest, the strongest first and never two close
    // together: where the tile sounds and sprays.
    private void FindSpots()
    {
        _spots.Clear();
        _candidates.Clear();
        float originX = TileData.OriginX(Zone);
        float originZ = TileData.OriginZ(Zone);
        for (int z = 0; z < _size; z++)
        {
            for (int x = 0; x < _size; x++)
            {
                int i = Index(x, z);
                float speed = Mathf.Sqrt((_velX[i] * _velX[i]) + (_velZ[i] * _velZ[i]));
                if (_depth[i] < SpotDepth || speed < SpotSpeed)
                {
                    continue;
                }

                float slope = Slope(x, z);
                float flow = speed * _depth[i];
                var at = new Vector3(originX + (x * _cell), _vertexY[(z * Pitch) + x] + Lift, originZ + (z * _cell));
                _candidates.Add((flow * (1f + (2f * slope)), new WaterSpot(at, new Vector3(_velX[i], 0f, _velZ[i]) / speed, flow, slope)));
            }
        }

        _candidates.Sort((a, b) => b.Score.CompareTo(a.Score));
        foreach ((float _, WaterSpot spot) in _candidates)
        {
            if (_spots.Count >= MaxSpots)
            {
                break;
            }

            if (_spots.TrueForAll(s => (s.Position - spot.Position).sqrMagnitude >= SpotSpacing * SpotSpacing))
            {
                _spots.Add(spot);
            }
        }
    }

    // The drawn surface's height at a vertex of this tile's grid, read from the tile beside when it
    // lies beyond this one; false when the vertex is dry or its tile not shown.
    private bool WetSurface(int x, int z, out float y)
    {
        WaterTile tile = x < 0 || z < 0 || x >= Pitch || z >= Pitch ? Beside(ref x, ref z) : this;
        int i = (z * Pitch) + x;
        bool wet = tile != null && tile._vertexWet[i];
        y = wet ? tile._vertexY[i] : 0f;
        return wet;
    }

    // The vertices of the last row and column are the next tiles' (their x or z is 0 there): where that
    // tile draws one, its height is taken from it, so the two meshes meet without a step.
    private void TakeBorderHeights()
    {
        for (int z = 0; z < Pitch; z++)
        {
            for (int x = 0; x < Pitch; x++)
            {
                if (x < _size && z < _size)
                {
                    continue;
                }

                int ox = x;
                int oz = z;
                WaterTile owner = Beside(ref ox, ref oz);
                int j = (oz * Pitch) + ox;
                if (owner != null && owner._meshIndex[j] >= 0)
                {
                    _vertexY[(z * Pitch) + x] = owner._vertexY[j];
                }
            }
        }
    }

    // The drawn surface's height at a vertex of this tile's grid or just beyond it (read from the tile
    // there); false where nothing is drawn.
    private bool DrawnY(int x, int z, out float y)
    {
        WaterTile tile = x < 0 || z < 0 || x >= Pitch || z >= Pitch ? Beside(ref x, ref z) : this;
        int i = (z * Pitch) + x;
        bool drawn = tile != null && tile._meshIndex[i] >= 0;
        y = drawn ? tile._vertexY[i] : 0f;
        return drawn;
    }

    // The surface's slope along x and z at a vertex, from the drawn vertices either side (across tiles,
    // so two tiles light their shared border alike): the normal and the tangent come from it.
    private void SurfaceFrame(int x, int z, out Vector3 normal, out Vector4 tangent)
    {
        float y = _vertexY[(z * Pitch) + x];
        float slopeX = Gradient(x, z, 1, 0, y);
        float slopeZ = Gradient(x, z, 0, 1, y);
        normal = new Vector3(-slopeX, 1f, -slopeZ).normalized;

        // u runs along x and v along z (world coordinates): the bitangent cross(normal, tangent) * w along +z.
        Vector3 along = new Vector3(1f, slopeX, 0f).normalized;
        tangent = new Vector4(along.x, along.y, along.z, -1f);
    }

    // Rise over run through a vertex in one direction: from both sides where both are drawn, else the one that is.
    private float Gradient(int x, int z, int dx, int dz, float y)
    {
        bool ahead = DrawnY(x + dx, z + dz, out float yAhead);
        bool behind = DrawnY(x - dx, z - dz, out float yBehind);
        return ahead && behind ? (yAhead - yBehind) / (2f * _cell)
            : ahead ? (yAhead - y) / _cell
            : behind ? (y - yBehind) / _cell
            : 0f;
    }

    // When this tile's edge changed (the two outer rows and columns: what the tiles around read for
    // their border heights and normals), they are rebuilt with it; only then, so that two tiles never
    // keep rebuilding each other.
    private void EdgeChanged()
    {
        double edge = 0.0;
        for (int z = 0; z < Pitch; z++)
        {
            for (int x = 0; x < Pitch; x++)
            {
                if (x > 1 && z > 1 && x < _size - 1 && z < _size - 1)
                {
                    continue;
                }

                int i = (z * Pitch) + x;
                edge += (i + 1) * (_meshIndex[i] >= 0 ? _vertexY[i] + 1000.0 : 1.0);
            }
        }

        if (edge == _edgeSeen)
        {
            return;
        }

        _edgeSeen = edge;
        foreach ((int dx, int dz) in Around)
        {
            if (ByZone.TryGetValue(TileData.Key(new Vector2s(Zone.x + dx, Zone.y + dz)), out WaterTile tile) && tile.Ready)
            {
                tile._dirty = true;
            }
        }
    }

    private void Rebuild()
    {
        SmoothForDrawing();
        MarkVertices();
        ComputeVertexHeights();
        TakeBorderHeights();
        _vertices.Clear();
        _normals.Clear();
        _tangents.Clear();
        _uvs.Clear();
        _water.Clear();
        _current.Clear();
        float streamSpeed = FlowingWaterPlugin.Settings.FlowVisualMinSpeed.Value;
        Vector3 origin = transform.position;
        float originX = TileData.OriginX(Zone);
        float originZ = TileData.OriginZ(Zone);
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int z = 0; z < Pitch; z++)
        {
            for (int x = 0; x < Pitch; x++)
            {
                int i = (z * Pitch) + x;
                if (_meshIndex[i] < 0)
                {
                    continue;
                }

                float wx = originX + (x * _cell);
                float wz = originZ + (z * _cell);
                float y = _vertexY[i] + Lift;
                _vertices.Add(new Vector3(wx - origin.x, y - origin.y, wz - origin.z));
                SurfaceFrame(x, z, out Vector3 normal, out Vector4 tangent);
                _normals.Add(normal);
                _tangents.Add(tangent);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
                VertexVelocity(x, z, out float vx, out float vz);
                float speed = Mathf.Sqrt((vx * vx) + (vz * vz));
                if (_customShader)
                {
                    _uvs.Add(new Vector2(wx, wz));
                    _water.Add(new Vector4(VertexDepth(x, z), SteepFoam(x, z), Shore(x, z), 0f));
                    _current.Add(new Vector2(vx, vz));
                }
                else if (speed >= streamSpeed)
                {
                    // Along the flow for the scrolling stream texture: v runs downstream.
                    vx /= speed;
                    vz /= speed;
                    float along = Mathf.Clamp(ReferenceSpeed / speed, 0.25f, 4f);
                    _uvs.Add(new Vector2(((wx * vz) - (wz * vx)) * StreamTextureScale, ((wx * vx) + (wz * vz)) * StreamTextureScale * along));
                }
                else
                {
                    _uvs.Add(new Vector2(wx * StillTextureScale, wz * StillTextureScale));
                }
            }
        }

        _stillTriangles.Clear();
        _streamTriangles.Clear();
        for (int z = 0; z < _size; z++)
        {
            for (int x = 0; x < _size; x++)
            {
                int a = (z * Pitch) + x;
                if (!_vertexWet[a] && !_vertexWet[a + 1] && !_vertexWet[a + Pitch] && !_vertexWet[a + Pitch + 1])
                {
                    continue;
                }

                int i = Index(x, z);
                float speed = Mathf.Sqrt((_velX[i] * _velX[i]) + (_velZ[i] * _velZ[i]));
                List<int> triangles = speed >= streamSpeed ? _streamTriangles : _stillTriangles;
                int b = _meshIndex[a + 1];
                int c = _meshIndex[a + Pitch];
                int d = _meshIndex[a + Pitch + 1];
                a = _meshIndex[a];
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(d);
            }
        }

        _mesh.Clear(false);
        _mesh.indexFormat = _vertices.Count > ushort.MaxValue ? IndexFormat.UInt32 : IndexFormat.UInt16;
        _mesh.subMeshCount = 2;
        _mesh.SetVertices(_vertices);
        _mesh.SetUVs(0, _uvs);
        if (_customShader)
        {
            _mesh.SetUVs(1, _water);
            _mesh.SetUVs(2, _current);
        }

        _mesh.SetTriangles(_stillTriangles, 0, false);
        _mesh.SetTriangles(_streamTriangles, 1, false);

        // Normals and tangents from the height grid, not from this mesh's triangles alone, which would
        // light each side of a tile border differently (a seam along it).
        _mesh.SetNormals(_normals);
        _mesh.SetTangents(_tangents);
        _mesh.RecalculateBounds();
        EdgeChanged();

        if (_trigger != null && _vertices.Count > 0)
        {
            float bottom = minY - origin.y - 1f;
            float top = maxY - origin.y + 1f;
            _trigger.center = new Vector3(0f, (bottom + top) * 0.5f, 0f);
            _trigger.size = new Vector3(TileData.ZoneMetres, top - bottom, TileData.ZoneMetres);
        }
    }
}
