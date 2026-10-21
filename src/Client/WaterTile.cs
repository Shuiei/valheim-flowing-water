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

    private const float MinDrawnDepth = 0.1f;

    // How far over its own ground a smoothed shore vertex may be drawn.
    private const float ShoreCreep = 0.05f;

    private const float ShoreDip = 0.02f;

    private const float StreamTextureScale = 0.5f;

    // The water speed at which the stream texture runs at its material's own speed; slower water
    // gets a denser texture that moves slower, faster water a stretched one that moves faster.
    private const float ReferenceSpeed = 1f;

    private const float StillTextureScale = 0.125f;

    private const int Pitch = TileData.VertexPitch;

    private const int VertexCount = Pitch * Pitch;

    private static readonly Dictionary<long, WaterTile> ByZone = [];

    private static readonly int WaterTimeId = Shader.PropertyToID("_WaterTime");

    private static readonly int VisibleMaxDistanceId = Shader.PropertyToID("_VisibleMaxDistance");

    private readonly float[] _depth = new float[TileData.Cells];

    private readonly float[] _drawDepth = new float[TileData.Cells];

    private readonly float[] _vertexY = new float[VertexCount];

    private readonly float[] _vertexYRaw = new float[VertexCount];

    private readonly bool[] _vertexSunk = new bool[VertexCount];

    private readonly float[] _velX = new float[TileData.Cells];

    private readonly float[] _velZ = new float[TileData.Cells];

    private readonly float[] _height = new float[VertexCount];

    private readonly Vector3[] _vertices = new Vector3[VertexCount];

    private readonly Vector2[] _uvs = new Vector2[VertexCount];

    private readonly List<int> _stillTriangles = [];

    private readonly List<int> _streamTriangles = [];

    private ZNetView _nview;

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

    private float _liftApplied;

    /// <summary>Metres the drawn water is raised by, for testing (console `flowingwater lift`).</summary>
    internal static float Lift { get; set; }

    /// <summary>Draw with plain opaque colours instead of the water materials, for testing.</summary>
    internal static bool Flat { get; set; }

    public Vector2s Zone { get; private set; }

    public bool Ready => _haveData && _haveHeights;

    /// <summary>The water surface over a point, as drawn; well under the ground where there is none, or -10000 with no tile.</summary>
    public static float SurfaceAt(Vector3 point)
    {
        if (!Find(point, out WaterTile tile, out int x, out int z, out float fx, out float fz))
        {
            return NoWater;
        }

        return Bilinear(tile._vertexY[(z * Pitch) + x], tile._vertexY[(z * Pitch) + x + 1], tile._vertexY[((z + 1) * Pitch) + x], tile._vertexY[((z + 1) * Pitch) + x + 1], fx, fz);
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
        Vector2s zone = TileData.CellOf(point, out int x, out int z);
        if (!ByZone.TryGetValue(TileData.Key(zone), out WaterTile tile))
        {
            return $"zone {zone}: no water tile";
        }

        if (!tile.Ready)
        {
            return $"zone {zone}: water tile not ready (data {tile._haveData}, ground {tile._haveHeights})";
        }

        int i = TileData.Index(x, z);
        float total = 0f;
        int wet = 0;
        for (int c = 0; c < TileData.Cells; c++)
        {
            total += tile._depth[c];
            if (tile._depth[c] > WetDepth)
            {
                wet++;
            }
        }

        return $"zone {zone} cell {x},{z}: ground {tile._height[(z * Pitch) + x]:0.00} depth {tile._depth[i]:0.00} m, current {tile._velX[i]:0.00},{tile._velZ[i]:0.00} m/s; tile {total:0} m³ over {wet} cells";
    }

    private static bool Find(Vector3 point, out WaterTile tile, out int x, out int z, out float fx, out float fz)
    {
        Vector2s zone = TileData.CellOf(point, out x, out z);
        fx = Mathf.Clamp01(point.x - TileData.WorldX(zone, x));
        fz = Mathf.Clamp01(point.z - TileData.WorldZ(zone, z));
        return ByZone.TryGetValue(TileData.Key(zone), out tile) && tile.Ready;
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

    // The depth shown at a vertex; the last row and column belong to the next zone's tile.
    private float VertexDepth(int x, int z)
    {
        if (x < TileData.Size && z < TileData.Size)
        {
            return _drawDepth[TileData.Index(x, z)];
        }

        WaterTile other = Beside(ref x, ref z);
        return other == null ? 0f : other._drawDepth[TileData.Index(x, z)];
    }

    // The depth drawn at each cell: the water surface averaged with the wet cells around it, so
    // the step-by-step spreading's one-cell ripples do not show as terraces. Dry cells stay dry.
    private void SmoothForDrawing()
    {
        for (int z = 0; z < TileData.Size; z++)
        {
            for (int x = 0; x < TileData.Size; x++)
            {
                int i = TileData.Index(x, z);
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
                        if (nx < 0 || nz < 0 || nx >= TileData.Size || nz >= TileData.Size)
                        {
                            // The next tile's cells are not at hand: count them wet, no thinning there.
                            wetAround++;
                            continue;
                        }

                        int j = TileData.Index(nx, nz);
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
        if (x < TileData.Size && z < TileData.Size)
        {
            int i = TileData.Index(x, z);
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

        int j = TileData.Index(x, z);
        vx = other._velX[j];
        vz = other._velZ[j];
    }

    private WaterTile Beside(ref int x, ref int z)
    {
        int zx = Zone.x;
        int zy = Zone.y;
        if (x >= TileData.Size)
        {
            zx++;
            x -= TileData.Size;
        }

        if (z >= TileData.Size)
        {
            zy++;
            z -= TileData.Size;
        }

        return ByZone.TryGetValue(TileData.Key(new Vector2s(zx, zy)), out WaterTile other) && other.Ready ? other : null;
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
        _renderer = GetComponent<MeshRenderer>();
        _trigger = GetComponentInChildren<BoxCollider>();
        _mesh = new Mesh { name = "FlowingWater tile", indexFormat = IndexFormat.UInt16 };
        _mesh.MarkDynamic();
        GetComponent<MeshFilter>().sharedMesh = _mesh;
        ApplyMaterials();
        ByZone[TileData.Key(Zone)] = this;
    }

    // The water materials, or in flat mode (for testing) plain opaque colours: blue still, green stream.
    private void ApplyMaterials()
    {
        _flatApplied = Flat;
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
        _heightTimer -= dt;
        if (_heightTimer <= 0f)
        {
            _heightTimer = HeightSampleSeconds;
            SampleHeights();
        }

        ZDO zdo = _nview.GetZDO();
        if (zdo.DataRevision != _revision)
        {
            _revision = zdo.DataRevision;
            byte[] data = zdo.GetByteArray(WaterServer.DataHash);
            if (data != null && _target.Decode(data))
            {
                _haveData = true;
                _dirty = true;
            }
        }

        if (!Ready)
        {
            _renderer.enabled = false;
            return;
        }

        if (_flatApplied != Flat)
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
        for (int i = 0; i < TileData.Cells; i++)
        {
            _depth[i] = Ease(_depth[i], _target.Depth[i], k, ref moving);
            _velX[i] = Ease(_velX[i], _target.VelX[i], k, ref moving);
            _velZ[i] = Ease(_velZ[i], _target.VelZ[i], k, ref moving);
        }

        if (moving || _dirty)
        {
            _dirty = false;
            Rebuild();
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

    private void SampleHeights()
    {
        bool complete = true;
        bool changed = false;
        var point = new Vector3(0f, 0f, 0f);
        for (int z = 0; z < Pitch; z++)
        {
            point.z = TileData.WorldZ(Zone, z);
            for (int x = 0; x < Pitch; x++)
            {
                point.x = TileData.WorldX(Zone, x);
                int i = (z * Pitch) + x;
                if (Heightmap.GetHeight(point, out float height))
                {
                    changed |= height != _height[i];
                    _height[i] = height;
                }
                else if (x < TileData.Size && z < TileData.Size)
                {
                    complete = false;
                }
                else
                {
                    // The next zone's ground is not loaded: carry on the edge.
                    _height[i] = _height[x == TileData.Size ? i - 1 : i - Pitch];
                }
            }
        }

        _haveHeights = complete;
        _dirty |= changed;
    }

    // The height of every vertex: the water's, carried on just under the ground beside it, sunk
    // elsewhere; then the vertices that are not sunk are averaged with their neighbours, which
    // rounds the one-metre steps of the shore. A dry vertex may rise at most ShoreCreep over its
    // ground that way.
    private void ComputeVertexHeights()
    {
        for (int z = 0; z < Pitch; z++)
        {
            for (int x = 0; x < Pitch; x++)
            {
                int i = (z * Pitch) + x;
                _vertexYRaw[i] = VertexY(x, z, out bool sunk);
                _vertexSunk[i] = sunk;
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
                if (VertexDepth(x, z) <= WetDepth)
                {
                    y = Mathf.Min(y, _height[i] + ShoreCreep);
                }

                _vertexY[i] = y;
            }
        }
    }

    private void Rebuild()
    {
        SmoothForDrawing();
        ComputeVertexHeights();
        float streamSpeed = FlowingWaterPlugin.Settings.FlowVisualMinSpeed.Value;
        Vector3 origin = transform.position;
        float minY = float.MaxValue;
        float maxY = float.MinValue;
        for (int z = 0; z < Pitch; z++)
        {
            for (int x = 0; x < Pitch; x++)
            {
                int i = (z * Pitch) + x;
                float wx = TileData.WorldX(Zone, x);
                float wz = TileData.WorldZ(Zone, z);
                float y = _vertexY[i] + Lift;
                _vertices[i] = new Vector3(wx - origin.x, y - origin.y, wz - origin.z);
                minY = Mathf.Min(minY, y);
                maxY = Mathf.Max(maxY, y);
                VertexVelocity(x, z, out float vx, out float vz);
                float speed = Mathf.Sqrt((vx * vx) + (vz * vz));
                if (speed >= streamSpeed)
                {
                    // Along the flow for the scrolling stream texture: v runs downstream.
                    vx /= speed;
                    vz /= speed;
                    float along = Mathf.Clamp(ReferenceSpeed / speed, 0.25f, 4f);
                    _uvs[i] = new Vector2(((wx * vz) - (wz * vx)) * StreamTextureScale, ((wx * vx) + (wz * vz)) * StreamTextureScale * along);
                }
                else
                {
                    _uvs[i] = new Vector2(wx * StillTextureScale, wz * StillTextureScale);
                }
            }
        }

        _stillTriangles.Clear();
        _streamTriangles.Clear();
        for (int z = 0; z < TileData.Size; z++)
        {
            for (int x = 0; x < TileData.Size; x++)
            {
                if (VertexDepth(x, z) <= WetDepth && VertexDepth(x + 1, z) <= WetDepth && VertexDepth(x, z + 1) <= WetDepth && VertexDepth(x + 1, z + 1) <= WetDepth)
                {
                    continue;
                }

                int i = TileData.Index(x, z);
                float speed = Mathf.Sqrt((_velX[i] * _velX[i]) + (_velZ[i] * _velZ[i]));
                List<int> triangles = speed >= streamSpeed ? _streamTriangles : _stillTriangles;
                int a = (z * Pitch) + x;
                int b = a + 1;
                int c = a + Pitch;
                int d = c + 1;
                triangles.Add(a);
                triangles.Add(c);
                triangles.Add(b);
                triangles.Add(b);
                triangles.Add(c);
                triangles.Add(d);
            }
        }

        _mesh.Clear(false);
        _mesh.subMeshCount = 2;
        _mesh.SetVertices(_vertices);
        _mesh.SetUVs(0, _uvs);
        _mesh.SetTriangles(_stillTriangles, 0, false);
        _mesh.SetTriangles(_streamTriangles, 1, false);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();

        if (_trigger != null)
        {
            float bottom = minY - origin.y - 1f;
            float top = maxY - origin.y + 1f;
            _trigger.center = new Vector3(0f, (bottom + top) * 0.5f, 0f);
            _trigger.size = new Vector3(TileData.Size, top - bottom, TileData.Size);
        }
    }
}
