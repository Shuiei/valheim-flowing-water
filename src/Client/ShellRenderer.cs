using System;
using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlowingWater.Client;

/// <summary>
/// A brick's water drawn as voxels where it meets the air (<see cref="VoxelShell"/>), beside the surface the graphics
/// card shapes: its own mesh on an object under the brick's, with the mod's shell material.
/// </summary>
internal sealed class ShellRenderer : IDisposable
{
    private readonly GameObject _object;
    private readonly MeshRenderer _renderer;
    private readonly Mesh _mesh = new() { name = "FlowingWater shell" };
    private readonly List<Vector3> _vectors = [];
    private readonly List<Vector4> _tangents = [];
    private readonly List<Vector2> _pairs = [];
    private readonly List<Vector4> _fours = [];

    /// <summary>An object for the shell under the brick's, drawn nothing yet.</summary>
    public ShellRenderer(Transform brick)
    {
        _object = new GameObject("FlowingWater shell") { layer = brick.gameObject.layer };
        _object.transform.SetParent(brick, false);
        _object.AddComponent<MeshFilter>().sharedMesh = _mesh;
        _renderer = _object.AddComponent<MeshRenderer>();
        _renderer.shadowCastingMode = ShadowCastingMode.Off;
        _renderer.receiveShadows = false;
        _renderer.enabled = false;
        _mesh.MarkDynamic();
    }

    /// <summary>Draws this shell (nothing when it has no face), lifted that many metres for testing.</summary>
    public void Show(VoxelShell shell, Material material, float lift)
    {
        if (shell.Faces == 0)
        {
            Hide();
            return;
        }

        int corners = shell.Positions.Count / 3;
        _mesh.Clear();
        _mesh.indexFormat = corners > 65000 ? IndexFormat.UInt32 : IndexFormat.UInt16;
        _mesh.SetVertices(Vectors(shell.Positions));
        _mesh.SetNormals(Vectors(shell.Normals));
        _tangents.Clear();
        for (int k = 0; k < corners; k++)
        {
            // Along the face's first place direction: across a side, east on a top or an underside (a film's sides, lit as
            // one sheet tilted up, across that sheet).
            float nx = shell.Normals[3 * k], ny = shell.Normals[(3 * k) + 1], nz = shell.Normals[(3 * k) + 2];
            float flat = Mathf.Max((float)Math.Sqrt((nx * nx) + (nz * nz)), 1e-6f);
            _tangents.Add(Math.Abs(ny) > 0.5f ? new Vector4(1f, 0f, 0f, ny > 0f ? -1f : 1f) : new Vector4(-nz / flat, 0f, nx / flat, -1f));
        }

        _mesh.SetTangents(_tangents);
        _mesh.SetUVs(0, Pairs(shell.Places));
        _mesh.SetUVs(1, Pairs(shell.Water));
        _mesh.SetUVs(2, Pairs(shell.Flow));
        _mesh.SetUVs(3, Fours(shell.Edges));
        _mesh.SetTriangles(shell.Triangles, 0, true);
        _object.transform.localPosition = new Vector3(0f, lift, 0f);
        _renderer.sharedMaterial = material;
        _renderer.enabled = true;
    }

    /// <summary>Draws nothing.</summary>
    public void Hide() => _renderer.enabled = false;

    public void Dispose()
    {
        UnityEngine.Object.Destroy(_mesh);
        UnityEngine.Object.Destroy(_object);
    }

    private List<Vector3> Vectors(List<float> values)
    {
        _vectors.Clear();
        for (int k = 0; k + 2 < values.Count; k += 3)
        {
            _vectors.Add(new Vector3(values[k], values[k + 1], values[k + 2]));
        }

        return _vectors;
    }

    private List<Vector4> Fours(List<float> values)
    {
        _fours.Clear();
        for (int k = 0; k + 3 < values.Count; k += 4)
        {
            _fours.Add(new Vector4(values[k], values[k + 1], values[k + 2], values[k + 3]));
        }

        return _fours;
    }

    private List<Vector2> Pairs(List<float> values)
    {
        _pairs.Clear();
        for (int k = 0; k + 1 < values.Count; k += 2)
        {
            _pairs.Add(new Vector2(values[k], values[k + 1]));
        }

        return _pairs;
    }
}
