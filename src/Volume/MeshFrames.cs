using System;
using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// A drawn surface's normals, tangents and bounds, without the game, as the game's mesh would work them out from
/// its triangles (so that they can be worked out away from its frame): a vertex's normal is the mean of the faces
/// sharing it, each counting by its area (a surface whose triangles share their vertices is smooth); its tangent
/// runs the way the texture's u grows, square to the normal, its w the side v grows on (-1 for u east and v north
/// on level water, as the game's flat meshes have it).
/// </summary>
internal static class MeshFrames
{
    /// <summary>Each vertex's normal (length 1; none, 0, for a vertex of no triangle).</summary>
    /// <param name="vertices">The positions.</param>
    /// <param name="triangles">Three vertex indices per triangle, clockwise seen from the side drawn.</param>
    public static Vec3[] Normals(IReadOnlyList<Vec3> vertices, IReadOnlyList<int> triangles)
    {
        var sums = new Vec3[vertices.Count];
        for (int t = 0; t + 2 < triangles.Count; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];

            // Clockwise, the face's cross product points at the side drawn; its length is twice its area.
            Vec3 face = Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
            sums[a] += face;
            sums[b] += face;
            sums[c] += face;
        }

        for (int i = 0; i < sums.Length; i++)
        {
            sums[i] = Unit(sums[i]);
        }

        return sums;
    }

    /// <summary>Each vertex's tangent and its w (the side v grows on, 1 or -1).</summary>
    /// <param name="vertices">The positions.</param>
    /// <param name="triangles">Three vertex indices per triangle.</param>
    /// <param name="normals">The normals (<see cref="Normals"/>).</param>
    /// <param name="uvs">Each vertex's texture coordinates.</param>
    public static (Vec3 Tangent, float W)[] Tangents(IReadOnlyList<Vec3> vertices, IReadOnlyList<int> triangles, IReadOnlyList<Vec3> normals, IReadOnlyList<(float U, float V)> uvs)
    {
        var alongU = new Vec3[vertices.Count];
        var alongV = new Vec3[vertices.Count];
        for (int t = 0; t + 2 < triangles.Count; t += 3)
        {
            int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
            Vec3 e1 = vertices[b] - vertices[a], e2 = vertices[c] - vertices[a];
            float u1 = uvs[b].U - uvs[a].U, v1 = uvs[b].V - uvs[a].V;
            float u2 = uvs[c].U - uvs[a].U, v2 = uvs[c].V - uvs[a].V;
            float area = (u1 * v2) - (u2 * v1);

            // A face whose texture has no area (seen edge on) tells no way.
            if (Math.Abs(area) < 1e-12f)
            {
                continue;
            }

            float r = 1f / area;
            Vec3 u = ((e1 * v2) - (e2 * v1)) * r;
            Vec3 v = ((e2 * u1) - (e1 * u2)) * r;
            alongU[a] += u;
            alongU[b] += u;
            alongU[c] += u;
            alongV[a] += v;
            alongV[b] += v;
            alongV[c] += v;
        }

        var tangents = new (Vec3 Tangent, float W)[vertices.Count];
        for (int i = 0; i < tangents.Length; i++)
        {
            Vec3 n = normals[i];
            Vec3 t = Unit(alongU[i] - (n * n.Dot(alongU[i])));
            tangents[i] = (t, Cross(n, t).Dot(alongV[i]) < 0f ? -1f : 1f);
        }

        return tangents;
    }

    /// <summary>The box round the vertices, lowest and highest corners (both 0 with none).</summary>
    public static (Vec3 Min, Vec3 Max) Bounds(IReadOnlyList<Vec3> vertices)
    {
        if (vertices.Count == 0)
        {
            return (default, default);
        }

        float x0 = float.MaxValue, y0 = float.MaxValue, z0 = float.MaxValue;
        float x1 = float.MinValue, y1 = float.MinValue, z1 = float.MinValue;
        foreach (Vec3 p in vertices)
        {
            x0 = Math.Min(x0, p.X);
            y0 = Math.Min(y0, p.Y);
            z0 = Math.Min(z0, p.Z);
            x1 = Math.Max(x1, p.X);
            y1 = Math.Max(y1, p.Y);
            z1 = Math.Max(z1, p.Z);
        }

        return (new Vec3(x0, y0, z0), new Vec3(x1, y1, z1));
    }

    private static Vec3 Cross(Vec3 a, Vec3 b) => new((a.Y * b.Z) - (a.Z * b.Y), (a.Z * b.X) - (a.X * b.Z), (a.X * b.Y) - (a.Y * b.X));

    private static Vec3 Unit(Vec3 v)
    {
        float length = (float)Math.Sqrt(v.Dot(v));
        return length > 1e-12f ? v * (1f / length) : default;
    }
}
