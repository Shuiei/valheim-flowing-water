using System;
using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// A brick's water surface as the graphics card shapes it (the mod's own shader): one fixed grid for every brick of a
/// voxel size, raised by the shader to the brick's <see cref="SurfaceGrid"/>, held in two small textures per state (the
/// previous and the next), blended over the time between the server's changes. A new grid coming part way is blended
/// to from the surface as it is drawn then, so the water never jumps; only the textures change, nothing is built in the
/// game's frame.
/// </summary>
internal sealed class GpuSurface : IDisposable
{
    // Grid points along a column: the grid's spacing is a fraction of a voxel, so the surface bends smoothly.
    private const int PointsPerColumn = 2;

    private static readonly int SurfacePrevId = Shader.PropertyToID("_SurfacePrev");
    private static readonly int SurfaceNextId = Shader.PropertyToID("_SurfaceNext");
    private static readonly int FlowPrevId = Shader.PropertyToID("_FlowPrev");
    private static readonly int FlowNextId = Shader.PropertyToID("_FlowNext");
    private static readonly int BlendStartId = Shader.PropertyToID("_BlendStart");
    private static readonly int BlendTimeId = Shader.PropertyToID("_BlendTime");
    private static readonly int VoxelSizeId = Shader.PropertyToID("_VoxelSize");
    private static readonly int LiftId = Shader.PropertyToID("_Lift");

    // The grid of each voxel size (the server's setting, the same for every brick).
    private static readonly Dictionary<float, Mesh> Grids = [];

    private readonly Texture2D _surfacePrev = Texture("prev surface");
    private readonly Texture2D _surfaceNext = Texture("next surface");
    private readonly Texture2D _flowPrev = Texture("prev current");
    private readonly Texture2D _flowNext = Texture("next current");
    private readonly float[] _pixels = new float[SurfaceGrid.Side * SurfaceGrid.Side * 4];

    private SurfaceGrid _from;
    private SurfaceGrid _to;
    private float _start;
    private float _time = 1f;

    /// <summary>What the brick's renderer hands the shader.</summary>
    public MaterialPropertyBlock Properties { get; } = new();

    /// <summary>The grid every brick of this voxel size draws: its points' places in columns as their first uv.</summary>
    public static Mesh Grid(float voxelSize)
    {
        if (Grids.TryGetValue(voxelSize, out Mesh made) && made != null)
        {
            return made;
        }

        int points = (Brick.Size * PointsPerColumn) + 1;
        float span = Brick.Size * voxelSize;
        var vertices = new Vector3[points * points];
        var cells = new Vector2[points * points];
        for (int j = 0; j < points; j++)
        {
            for (int i = 0; i < points; i++)
            {
                float u = (float)i / PointsPerColumn;
                float v = (float)j / PointsPerColumn;
                vertices[(j * points) + i] = new Vector3(((u + 0.5f) * voxelSize) - (span / 2f), 0f, ((v + 0.5f) * voxelSize) - (span / 2f));
                cells[(j * points) + i] = new Vector2(u, v);
            }
        }

        int[] triangles = new int[(points - 1) * (points - 1) * 6];
        int t = 0;
        for (int j = 0; j < points - 1; j++)
        {
            for (int i = 0; i < points - 1; i++)
            {
                int a = (j * points) + i;
                int b = a + points;
                triangles[t++] = a;
                triangles[t++] = b;
                triangles[t++] = b + 1;
                triangles[t++] = a;
                triangles[t++] = b + 1;
                triangles[t++] = a + 1;
            }
        }

        var mesh = new Mesh { name = $"FlowingWater grid {voxelSize} m" };
        mesh.SetVertices(vertices);
        mesh.SetUVs(0, cells);
        mesh.SetTriangles(triangles, 0, false);

        // The shader raises the points anywhere in the brick, a little over and under it (a column's top may be a few
        // rows out of it): the bounds take all of that, not the flat grid's.
        mesh.bounds = new Bounds(Vector3.zero, new Vector3(span + (2f * voxelSize), span + (2f * (SurfaceGrid.Window + 1) * voxelSize), span + (2f * voxelSize)));
        mesh.UploadMeshData(true);
        Grids[voxelSize] = mesh;
        return mesh;
    }

    /// <summary>
    /// A new grid for the brick, blended to over this long from the surface as it is drawn now.
    /// </summary>
    /// <param name="grid">The brick's new surface.</param>
    /// <param name="now">The time now, as the shader has it (seconds since the scene loaded).</param>
    /// <param name="time">How long the blend takes, seconds.</param>
    /// <param name="lift">Metres the surface is drawn higher, for testing.</param>
    public void Show(SurfaceGrid grid, float now, float time, float lift)
    {
        _from = _to == null ? grid : SurfaceGrid.Blend(_from, _to, (now - _start) / _time);
        _to = grid;
        _start = now;
        _time = time;
        Upload(_from, _surfacePrev, _flowPrev);
        Upload(_to, _surfaceNext, _flowNext);
        Properties.SetTexture(SurfacePrevId, _surfacePrev);
        Properties.SetTexture(SurfaceNextId, _surfaceNext);
        Properties.SetTexture(FlowPrevId, _flowPrev);
        Properties.SetTexture(FlowNextId, _flowNext);
        Properties.SetFloat(BlendStartId, now);
        Properties.SetFloat(BlendTimeId, time);
        Properties.SetFloat(VoxelSizeId, grid.VoxelSize);
        Properties.SetFloat(LiftId, lift);
    }

    public void Dispose()
    {
        UnityEngine.Object.Destroy(_surfacePrev);
        UnityEngine.Object.Destroy(_surfaceNext);
        UnityEngine.Object.Destroy(_flowPrev);
        UnityEngine.Object.Destroy(_flowNext);
    }

    private static Texture2D Texture(string name) => new(SurfaceGrid.Side, SurfaceGrid.Side, TextureFormat.RGBAFloat, false, true)
    {
        name = "FlowingWater " + name,
        filterMode = FilterMode.Bilinear,
        wrapMode = TextureWrapMode.Clamp,
    };

    private void Upload(SurfaceGrid grid, Texture2D surface, Texture2D flow)
    {
        for (int k = 0; k < SurfaceGrid.Side * SurfaceGrid.Side; k++)
        {
            _pixels[(k * 4) + 0] = grid.Height[k];
            _pixels[(k * 4) + 1] = grid.Depth[k];
            _pixels[(k * 4) + 2] = grid.Shown[k];
            _pixels[(k * 4) + 3] = grid.Own[k];
        }

        surface.SetPixelData(_pixels, 0);
        surface.Apply(false);
        for (int k = 0; k < SurfaceGrid.Side * SurfaceGrid.Side; k++)
        {
            _pixels[(k * 4) + 0] = grid.CurrentX[k];
            _pixels[(k * 4) + 1] = grid.CurrentZ[k];
            _pixels[(k * 4) + 2] = grid.Foam[k];
            _pixels[(k * 4) + 3] = 0f;
        }

        flow.SetPixelData(_pixels, 0);
        flow.Apply(false);
    }
}
