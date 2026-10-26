using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// The triangles of a collider's mesh in the mesh's own space, without the game: kept once for every object of its
/// kind, with a grid of cells over it so that a segment is tested against the few triangles near its way, and placed
/// in the world by each <see cref="SolidMeshInstance"/>. Which side of a triangle is its outside is taken from the
/// mesh as a whole (the sign of its volume), so a mesh wound either way is solid inside. Main thread only: a query
/// marks the triangles it tested, and hands back a list it keeps.
/// </summary>
internal sealed class SolidMesh
{
    // The most cells along an axis of the grid, and the least.
    private const int MaxCells = 64;

    // A triangle this close to parallel with a segment (the determinant of the test) is not crossed by it.
    private const float Parallel = 1e-9f;

    private readonly float[] _xyz;

    private readonly int[] _indices;

    private readonly int _nx, _ny, _nz;

    private readonly float _cx, _cy, _cz;

    // Each cell's triangles: those of cell c are _items[_start[c] .. _start[c + 1]).
    private readonly int[] _start;

    private readonly int[] _items;

    // The query each triangle was last tested in, so that one lying in several cells is tested once.
    private readonly int[] _tested;

    private readonly List<(float T, bool Enter)> _hits = [];

    private readonly bool _flipped;

    private int _query;

    /// <summary>A mesh from its vertices (x, y, z each) and the vertex indices of its triangles (three each).</summary>
    public SolidMesh(float[] xyz, int[] indices)
    {
        if (xyz is not { Length: >= 9 } || indices is not { Length: >= 3 } || xyz.Length % 3 != 0 || indices.Length % 3 != 0)
        {
            throw new ArgumentException("a mesh needs vertices of three numbers and triangles of three vertices");
        }

        _xyz = xyz;
        _indices = indices;
        Triangles = indices.Length / 3;
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        for (int i = 0; i < xyz.Length; i += 3)
        {
            minX = Math.Min(minX, xyz[i]);
            maxX = Math.Max(maxX, xyz[i]);
            minY = Math.Min(minY, xyz[i + 1]);
            maxY = Math.Max(maxY, xyz[i + 1]);
            minZ = Math.Min(minZ, xyz[i + 2]);
            maxZ = Math.Max(maxZ, xyz[i + 2]);
        }

        foreach (int index in indices)
        {
            if (index < 0 || index * 3 >= xyz.Length)
            {
                throw new ArgumentException($"triangle vertex {index} is not among the {xyz.Length / 3} vertices");
            }
        }

        // A little room round the mesh: a triangle on its very edge lies within the cells.
        float room = 1e-3f * Math.Max(1f, Math.Max(maxX - minX, Math.Max(maxY - minY, maxZ - minZ)));
        Min = new Vec3(minX - room, minY - room, minZ - room);
        Max = new Vec3(maxX + room, maxY + room, maxZ + room);

        // Cells about the size that gives a triangle a cell: cube-root of the room per triangle, no more than
        // MaxCells along an axis.
        Vec3 size = Max - Min;
        float cell = (float)Math.Pow(Math.Max(1e-6, (double)size.X * size.Y * size.Z / Triangles), 1.0 / 3.0);
        _nx = Cells(size.X, cell);
        _ny = Cells(size.Y, cell);
        _nz = Cells(size.Z, cell);
        _cx = size.X / _nx;
        _cy = size.Y / _ny;
        _cz = size.Z / _nz;
        _tested = new int[Triangles];

        // The triangles of each cell: counted, then placed (each in every cell its box reaches).
        int cells = _nx * _ny * _nz;
        _start = new int[cells + 1];
        for (int t = 0; t < Triangles; t++)
        {
            Over(t, (c) => _start[c + 1]++);
        }

        for (int c = 1; c < _start.Length; c++)
        {
            _start[c] += _start[c - 1];
        }

        _items = new int[_start[cells]];
        int[] fill = new int[_start.Length - 1];
        for (int t = 0; t < Triangles; t++)
        {
            int triangle = t;
            Over(t, (c) => _items[_start[c] + fill[c]++] = triangle);
        }

        _flipped = SignedVolume() < 0f;
    }

    /// <summary>How many triangles it has.</summary>
    public int Triangles { get; }

    /// <summary>Its lowest corner in its own space.</summary>
    public Vec3 Min { get; }

    /// <summary>Its highest corner in its own space.</summary>
    public Vec3 Max { get; }

    /// <summary>Whether the segment from one point to the other (in the mesh's space) crosses any of its triangles.</summary>
    public bool Any(Vec3 from, Vec3 to) => Walk(from, to, true);

    /// <summary>
    /// Where the segment from one point to the other (in the mesh's space) crosses its triangles: how far along it
    /// (0 at the first point, 1 at the second), nearest first, and whether it goes in there (through a triangle's
    /// outside) or out. The list is the mesh's own, good until its next query.
    /// </summary>
    public List<(float T, bool Enter)> Hits(Vec3 from, Vec3 to)
    {
        Walk(from, to, false);

        // Nearest first; at one place, going out before going in (two shells of a mesh sharing a face: the one left is
        // left before the next is entered).
        _hits.Sort(static (a, b) =>
        {
            int byPlace = a.T.CompareTo(b.T);
            return byPlace != 0 ? byPlace : a.Enter.CompareTo(b.Enter);
        });
        return _hits;
    }

    private static int Cells(float extent, float cell) => Math.Max(1, Math.Min(MaxCells, (int)Math.Ceiling(extent / Math.Max(cell, 1e-6f))));

    private int Cell(int x, int y, int z) => (((y * _nz) + z) * _nx) + x;

    private Vec3 Vertex(int index) => new(_xyz[index * 3], _xyz[(index * 3) + 1], _xyz[(index * 3) + 2]);

    // The cells a triangle's box reaches.
    private void Over(int t, Action<int> each)
    {
        Vec3 a = Vertex(_indices[t * 3]), b = Vertex(_indices[(t * 3) + 1]), c = Vertex(_indices[(t * 3) + 2]);
        int x0 = Index(Math.Min(a.X, Math.Min(b.X, c.X)), Min.X, _cx, _nx), x1 = Index(Math.Max(a.X, Math.Max(b.X, c.X)), Min.X, _cx, _nx);
        int y0 = Index(Math.Min(a.Y, Math.Min(b.Y, c.Y)), Min.Y, _cy, _ny), y1 = Index(Math.Max(a.Y, Math.Max(b.Y, c.Y)), Min.Y, _cy, _ny);
        int z0 = Index(Math.Min(a.Z, Math.Min(b.Z, c.Z)), Min.Z, _cz, _nz), z1 = Index(Math.Max(a.Z, Math.Max(b.Z, c.Z)), Min.Z, _cz, _nz);
        for (int y = y0; y <= y1; y++)
        {
            for (int z = z0; z <= z1; z++)
            {
                for (int x = x0; x <= x1; x++)
                {
                    each(Cell(x, y, z));
                }
            }
        }
    }

    private static int Index(float v, float min, float cell, int n) => Math.Max(0, Math.Min(n - 1, (int)Math.Floor((v - min) / cell)));

    // Six times the volume the triangles enclose, signed: negative when they are wound with their outsides inward.
    // Taken about the middle of the mesh's box, so that a mesh that is not closed (a trunk open at its foot) is
    // judged from a point inside it, wherever its origin is.
    private float SignedVolume()
    {
        Vec3 middle = (Min + Max) * 0.5f;
        double sum = 0;
        for (int t = 0; t < Triangles; t++)
        {
            Vec3 a = Vertex(_indices[t * 3]) - middle, b = Vertex(_indices[(t * 3) + 1]) - middle, c = Vertex(_indices[(t * 3) + 2]) - middle;
            sum += a.Dot(Cross(b, c));
        }

        return (float)sum;
    }

    // Walks the cells along the segment, testing their triangles; with anyHit, stops at the first and tells of it,
    // else gathers them all in _hits.
    private bool Walk(Vec3 from, Vec3 to, bool anyHit)
    {
        _hits.Clear();
        _query++;
        Vec3 d = to - from;

        // The part of the segment inside the grid's box.
        float t0 = 0f, t1 = 1f;
        if (!Clip(from.X, d.X, Min.X, Max.X, ref t0, ref t1) || !Clip(from.Y, d.Y, Min.Y, Max.Y, ref t0, ref t1) || !Clip(from.Z, d.Z, Min.Z, Max.Z, ref t0, ref t1))
        {
            return false;
        }

        Vec3 start = from + (d * t0);
        int x = Index(start.X, Min.X, _cx, _nx), y = Index(start.Y, Min.Y, _cy, _ny), z = Index(start.Z, Min.Z, _cz, _nz);
        int sx = Math.Sign(d.X), sy = Math.Sign(d.Y), sz = Math.Sign(d.Z);
        float tx = Next(start.X, d.X, Min.X, _cx, x), ty = Next(start.Y, d.Y, Min.Y, _cy, y), tz = Next(start.Z, d.Z, Min.Z, _cz, z);
        float dx = Step(d.X, _cx), dy = Step(d.Y, _cy), dz = Step(d.Z, _cz);
        float left = t1 - t0;
        while (true)
        {
            int c = Cell(x, y, z);
            for (int k = _start[c]; k < _start[c + 1]; k++)
            {
                int t = _items[k];
                if (_tested[t] == _query)
                {
                    continue;
                }

                _tested[t] = _query;
                if (Hit(t, from, d, out float at, out bool enter))
                {
                    if (anyHit)
                    {
                        return true;
                    }

                    _hits.Add((at, enter));
                }
            }

            // On to the next cell along the way, or done past the segment's end or the grid.
            if (tx <= ty && tx <= tz)
            {
                if (tx > left || (x += sx) < 0 || x >= _nx)
                {
                    break;
                }

                tx += dx;
            }
            else if (ty <= tz)
            {
                if (ty > left || (y += sy) < 0 || y >= _ny)
                {
                    break;
                }

                ty += dy;
            }
            else
            {
                if (tz > left || (z += sz) < 0 || z >= _nz)
                {
                    break;
                }

                tz += dz;
            }
        }

        return _hits.Count > 0;
    }

    // One axis of clipping a segment (start + t * along) to min..max.
    private static bool Clip(float start, float along, float min, float max, ref float t0, ref float t1)
    {
        if (Math.Abs(along) < 1e-12f)
        {
            return start >= min && start <= max;
        }

        float a = (min - start) / along, b = (max - start) / along;
        t0 = Math.Max(t0, Math.Min(a, b));
        t1 = Math.Min(t1, Math.Max(a, b));
        return t0 <= t1;
    }

    // How far along the segment (as a share of its whole length) the next cell border on an axis is from the start.
    private static float Next(float start, float along, float min, float cell, int index)
    {
        if (Math.Abs(along) < 1e-12f)
        {
            return float.PositiveInfinity;
        }

        float border = min + ((along > 0 ? index + 1 : index) * cell);
        return (border - start) / along;
    }

    private static float Step(float along, float cell) => Math.Abs(along) < 1e-12f ? float.PositiveInfinity : Math.Abs(cell / along);

    // Whether the segment (from, along d) crosses triangle t: where (0 to 1) and whether it goes in through its outside.
    private bool Hit(int t, Vec3 from, Vec3 d, out float at, out bool enter)
    {
        at = 0f;
        enter = false;
        Vec3 a = Vertex(_indices[t * 3]);
        Vec3 e1 = Vertex(_indices[(t * 3) + 1]) - a;
        Vec3 e2 = Vertex(_indices[(t * 3) + 2]) - a;
        Vec3 p = Cross(d, e2);
        float det = e1.Dot(p);
        if (Math.Abs(det) < Parallel)
        {
            return false;
        }

        float inv = 1f / det;
        Vec3 s = from - a;
        float u = s.Dot(p) * inv;
        if (u is < 0f or > 1f)
        {
            return false;
        }

        Vec3 q = Cross(s, e1);
        float v = d.Dot(q) * inv;
        if (v < 0f || u + v > 1f)
        {
            return false;
        }

        at = e2.Dot(q) * inv;
        if (at is < 0f or > 1f)
        {
            return false;
        }

        // Going against the triangle's normal is going in; the mesh's winding may put the normals inward.
        enter = (Cross(e1, e2).Dot(d) < 0f) != _flipped;
        return true;
    }

    private static Vec3 Cross(Vec3 a, Vec3 b) => new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));
}

/// <summary>
/// A <see cref="SolidMesh"/> standing in the world: moved, turned and scaled by a matrix. Down a vertical line it is
/// solid from each triangle whose outside faces up to the next whose outside faces down, or to the bottom of the
/// line where there is none (a trunk open at its foot is solid down to the ground; a rock's overhang ends where its
/// underside does, and an arch lets water under it).
/// </summary>
internal sealed class SolidMeshInstance : Solid
{
    // How far over the mesh's highest point a column's walk starts: no triangle is over it, so it starts outside.
    private const float Over = 0.01f;

    // Mesh to world and world to mesh, each three rows of four (the last the translation).
    private readonly float[] _toWorld, _toMesh;

    private readonly Vec3 _min, _max;

    /// <summary>
    /// The mesh placed by <paramref name="toWorld"/>: the first three rows of a matrix taking a point of the mesh's
    /// space to the world, row by row (m00 m01 m02 m03, m10 ... m23).
    /// </summary>
    /// <exception cref="ArgumentException">The matrix flattens the mesh (a scale of 0): it has no inside.</exception>
    public SolidMeshInstance(SolidMesh mesh, float[] toWorld)
    {
        if (toWorld is not { Length: 12 })
        {
            throw new ArgumentException("a placing matrix is three rows of four numbers");
        }

        Mesh = mesh;
        _toWorld = (float[])toWorld.Clone();
        _toMesh = Inverse(_toWorld) ?? throw new ArgumentException("the placing matrix flattens the mesh");
        float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
        float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;
        for (int corner = 0; corner < 8; corner++)
        {
            Vec3 p = Apply(_toWorld, new Vec3((corner & 1) == 0 ? mesh.Min.X : mesh.Max.X, (corner & 2) == 0 ? mesh.Min.Y : mesh.Max.Y, (corner & 4) == 0 ? mesh.Min.Z : mesh.Max.Z));
            minX = Math.Min(minX, p.X);
            maxX = Math.Max(maxX, p.X);
            minY = Math.Min(minY, p.Y);
            maxY = Math.Max(maxY, p.Y);
            minZ = Math.Min(minZ, p.Z);
            maxZ = Math.Max(maxZ, p.Z);
        }

        _min = new Vec3(minX, minY, minZ);
        _max = new Vec3(maxX, maxY, maxZ);
    }

    /// <summary>The mesh it places.</summary>
    public SolidMesh Mesh { get; }

    /// <inheritdoc/>
    public override (Vec3 Min, Vec3 Max) Bounds() => (_min, _max);

    /// <inheritdoc/>
    public override bool Overlaps(Vec3 min, Vec3 max) =>
        min.X <= _max.X && max.X >= _min.X && min.Y <= _max.Y && max.Y >= _min.Y && min.Z <= _max.Z && max.Z >= _min.Z;

    /// <inheritdoc/>
    public override bool Crosses(Vec3 from, Vec3 to) => Mesh.Any(Apply(_toMesh, from), Apply(_toMesh, to));

    /// <inheritdoc/>
    public override void Column(float x, float z, float bottom, float top, List<(float Low, float High)> spans)
    {
        if (x < _min.X || x > _max.X || z < _min.Z || z > _max.Z || bottom > _max.Y || top < _min.Y)
        {
            return;
        }

        // Walked from over the mesh's top, where the line is outside it, down to the bottom asked for.
        float start = Math.Max(top, _max.Y + Over);
        List<(float T, bool Enter)> hits = Mesh.Hits(Apply(_toMesh, new Vec3(x, start, z)), Apply(_toMesh, new Vec3(x, bottom, z)));
        bool inside = false;
        float high = start;
        foreach ((float t, bool enter) in hits)
        {
            float y = start + (t * (bottom - start));
            if (enter && !inside)
            {
                inside = true;
                high = y;
            }
            else if (!enter && inside)
            {
                inside = false;
                Add(spans, y, high, bottom, top);
            }
        }

        if (inside)
        {
            Add(spans, bottom, high, bottom, top);
        }
    }

    private static void Add(List<(float Low, float High)> spans, float low, float high, float bottom, float top)
    {
        low = Math.Max(low, bottom);
        high = Math.Min(high, top);
        if (high > low)
        {
            spans.Add((low, high));
        }
    }

    private static Vec3 Apply(float[] m, Vec3 p) => new(
        (m[0] * p.X) + (m[1] * p.Y) + (m[2] * p.Z) + m[3],
        (m[4] * p.X) + (m[5] * p.Y) + (m[6] * p.Z) + m[7],
        (m[8] * p.X) + (m[9] * p.Y) + (m[10] * p.Z) + m[11]);

    // The inverse of a matrix of three rows of four (its last row 0 0 0 1); null when it has none.
    private static float[] Inverse(float[] m)
    {
        float a = m[0], b = m[1], c = m[2], d = m[4], e = m[5], f = m[6], g = m[8], h = m[9], i = m[10];
        float ca = (e * i) - (f * h), cb = (f * g) - (d * i), cc = (d * h) - (e * g);
        float det = (a * ca) + (b * cb) + (c * cc);
        if (Math.Abs(det) < 1e-12f || float.IsNaN(det))
        {
            return null;
        }

        float inv = 1f / det;
        float[] r =
        [
            ca * inv, ((c * h) - (b * i)) * inv, ((b * f) - (c * e)) * inv, 0f,
            cb * inv, ((a * i) - (c * g)) * inv, ((c * d) - (a * f)) * inv, 0f,
            cc * inv, ((b * g) - (a * h)) * inv, ((a * e) - (b * d)) * inv, 0f,
        ];
        r[3] = -((r[0] * m[3]) + (r[1] * m[7]) + (r[2] * m[11]));
        r[7] = -((r[4] * m[3]) + (r[5] * m[7]) + (r[6] * m[11]));
        r[11] = -((r[8] * m[3]) + (r[9] * m[7]) + (r[10] * m[11]));
        return r;
    }
}
