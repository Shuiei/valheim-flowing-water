using System.Collections.Generic;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// A brick's water surface as its mesh takes it (the game's material, not the mod's shader): positions from the brick's
/// origin, and what each vertex carries. Its normals, tangents and bounds are worked out with it, away from the game's
/// frame, as the mesh would.
/// </summary>
internal sealed class SurfaceShape
{
    public List<Vector3> Vertices { get; } = [];

    public List<Vector3> Normals { get; } = [];

    public List<Vector4> Tangents { get; } = [];

    public Bounds Bounds { get; private set; }

    public List<Vector2> World { get; } = [];

    public List<Vector4> Water { get; } = [];

    public List<Vector2> Current { get; } = [];

    public List<int> Triangles { get; private set; } = [];

    /// <summary>The water it was built from, as drawn.</summary>
    public BrickView Drawn { get; private set; }

    /// <summary>The bricks round it whose surface changes with it, from what was drawn before.</summary>
    public ICollection<(int X, int Y, int Z)> Touched { get; private set; }

    /// <summary>Whether the water is still easing to the newest data.</summary>
    public bool Easing { get; private set; }

    /// <summary>The surface of the water drawn, over the bricks round it as drawn (that field is this build's own).</summary>
    public static SurfaceShape Of(VoxelField field, BrickView water, BrickView before, bool easing, Vector3 origin)
    {
        field.Set(water);
        SurfaceMesh surface = VoxelSurface.Build(field, water);
        var shape = new SurfaceShape
        {
            Triangles = surface.Triangles,
            Drawn = water,
            Touched = VoxelSurface.Touched(before, water),
            Easing = easing,
        };
        var uvs = new (float U, float V)[surface.Vertices.Count];
        for (int i = 0; i < surface.Vertices.Count; i++)
        {
            Vec3 v = surface.Vertices[i];
            shape.Vertices.Add(new Vector3(v.X, v.Y, v.Z) - origin);

            // Ripples on the sides run up them: their texture shifts with height.
            uvs[i] = (v.X + (v.Y * 0.5f), v.Z + (v.Y * 0.5f));
            shape.World.Add(new Vector2(uvs[i].U, uvs[i].V));
            shape.Water.Add(new Vector4(surface.Depths[i], 0f, surface.Shores[i], 0f));
            shape.Current.Add(new Vector2(surface.Velocities[i].X, surface.Velocities[i].Z));
        }

        Vec3[] normals = MeshFrames.Normals(surface.Vertices, surface.Triangles);
        (Vec3 Tangent, float W)[] tangents = MeshFrames.Tangents(surface.Vertices, surface.Triangles, normals, uvs);
        for (int i = 0; i < normals.Length; i++)
        {
            shape.Normals.Add(new Vector3(normals[i].X, normals[i].Y, normals[i].Z));
            shape.Tangents.Add(new Vector4(tangents[i].Tangent.X, tangents[i].Tangent.Y, tangents[i].Tangent.Z, tangents[i].W));
        }

        // The bounds of the positions as drawn (from the brick's origin); none with no surface.
        if (surface.Vertices.Count > 0)
        {
            (Vec3 low, Vec3 high) = MeshFrames.Bounds(surface.Vertices);
            var bounds = new Bounds();
            bounds.SetMinMax(new Vector3(low.X, low.Y, low.Z) - origin, new Vector3(high.X, high.Y, high.Z) - origin);
            shape.Bounds = bounds;
        }

        return shape;
    }
}
