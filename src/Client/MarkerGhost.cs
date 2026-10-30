using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// What the hoe's ghost of a source shows over the ground while it is placed: an arrow the way it faces (the ghost's own
/// turn, which the game's rotate keys change) and the outline of where its water goes, as the panel's values for the
/// next one have it: its outlet's width, fanned out by its spread, as far as a jet lands (6 m along the ground); all
/// round, a ring round it. Drawn in the ghost's own space, so it turns with it; made again when the values change.
/// </summary>
internal sealed class MarkerGhost : MonoBehaviour
{
    // How far out the outline goes along the ground, metres, and at the most for a jet's landing; how thick its lines
    // are and how far over the ground (over the game's grass: laid on the ground, it was hidden in it).
    private const float Along = 6f, MostOut = 20f, Line = 0.12f, Lift = 0.35f;

    private static readonly Color Outline = new(0.55f, 0.85f, 0.95f), Arrow = new(1f, 0.75f, 0.3f);

    private readonly List<Vector3> _vertices = [];

    private readonly List<int> _outline = [];

    private readonly List<int> _arrow = [];

    private Mesh _mesh;

    private SourceShape _drawn;

    private float _drawnRate = float.NaN;

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Awake()
    {
        Material outline = Materials.Plain("FlowingWater source ghost outline", Outline);
        Material arrow = Materials.Plain("FlowingWater source ghost arrow", Arrow);
        if (outline == null || arrow == null)
        {
            enabled = false;
            return;
        }

        var drawing = new GameObject("Water's way", typeof(MeshFilter), typeof(MeshRenderer));
        drawing.transform.SetParent(transform, false);
        _mesh = new Mesh { name = "FlowingWater source ghost" };
        _mesh.MarkDynamic();
        drawing.GetComponent<MeshFilter>().sharedMesh = _mesh;
        MeshRenderer renderer = drawing.GetComponent<MeshRenderer>();
        renderer.sharedMaterials = [outline, arrow];
        renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        renderer.receiveShadows = false;
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Update()
    {
        NextPlacement next = HoePieces.Next;
        SourceShape shape = next.Shape;
        float rate = next.Rate(false);
        if (shape == _drawn && rate == _drawnRate)
        {
            return;
        }

        _drawn = shape;
        _drawnRate = rate;
        Build(shape, rate);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        if (_mesh != null)
        {
            Destroy(_mesh);
        }
    }

    // The outline and the arrow for a shape, in the ghost's space (forward +z, right +x, as SourceShape faces north).
    private void Build(SourceShape shape, float rate)
    {
        _vertices.Clear();
        _outline.Clear();
        _arrow.Clear();
        if (shape.AllRound)
        {
            const int Sides = 32;
            for (int k = 0; k < Sides; k++)
            {
                float a0 = k * 2f * Mathf.PI / Sides, a1 = (k + 1) * 2f * Mathf.PI / Sides;
                Strip(_outline, new Vector2(Mathf.Sin(a0), Mathf.Cos(a0)) * 1.5f, new Vector2(Mathf.Sin(a1), Mathf.Cos(a1)) * 1.5f);
            }
        }
        else
        {
            float half = shape.OutletWidth / 2f;
            float far = Mathf.Min(MostOut, SourcePreview.Lands(shape, rate) ?? Along);
            float fan = Mathf.Tan(shape.Spread / 2f * Mathf.Deg2Rad) * far;
            Vector2 leftNear = new(-half, 0f), rightNear = new(half, 0f), leftFar = new(-half - fan, far), rightFar = new(half + fan, far);
            Strip(_outline, leftNear, rightNear);
            Strip(_outline, leftNear, leftFar);
            Strip(_outline, rightNear, rightFar);
            Strip(_outline, leftFar, rightFar);

            // The arrow: a shaft out of the outlet's middle and a head.
            float length = Mathf.Min(2.5f, far * 0.6f);
            Strip(_arrow, Vector2.zero, new Vector2(0f, length), 2.5f);
            Triangle(_arrow, new Vector2(-0.35f, length), new Vector2(0.35f, length), new Vector2(0f, length + 0.6f));
        }

        _mesh.Clear();
        _mesh.SetVertices(_vertices);
        _mesh.subMeshCount = 2;
        _mesh.SetTriangles(_outline, 0);
        _mesh.SetTriangles(_arrow, 1);
        _mesh.RecalculateNormals();
        _mesh.RecalculateBounds();
    }

    // A flat strip on the ground between two points (x right, y forward), this many lines thick.
    private void Strip(List<int> into, Vector2 from, Vector2 to, float thick = 1f)
    {
        Vector2 way = to - from;
        if (way.sqrMagnitude < 1e-6f)
        {
            return;
        }

        Vector2 side = new Vector2(-way.y, way.x).normalized * (Line * thick / 2f);
        int first = _vertices.Count;
        foreach (Vector2 corner in new[] { from - side, from + side, to + side, to - side })
        {
            _vertices.Add(new Vector3(corner.x, Lift, corner.y));
        }

        into.AddRange([first, first + 1, first + 2, first, first + 2, first + 3]);
    }

    private void Triangle(List<int> into, Vector2 a, Vector2 b, Vector2 c)
    {
        int first = _vertices.Count;
        _vertices.Add(new Vector3(a.x, Lift, a.y));
        _vertices.Add(new Vector3(c.x, Lift, c.y));
        _vertices.Add(new Vector3(b.x, Lift, b.y));
        into.AddRange([first, first + 1, first + 2]);
    }
}
