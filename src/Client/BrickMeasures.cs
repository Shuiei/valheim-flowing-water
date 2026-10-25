using System;
using System.Linq;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>What the in-game recorder and the console measure of the bricks: their count and what one draws (their cost is <see cref="BrickFrame"/>'s).</summary>
internal static class BrickMeasures
{
    /// <summary>The bricks this game has, and of them those drawn with a surface and its vertices.</summary>
    public static int Census(out int drawn, out int vertices)
    {
        drawn = 0;
        vertices = 0;
        int bricks = 0;
        foreach (WaterBrick brick in WaterBrick.All)
        {
            if (brick == null)
            {
                continue;
            }

            bricks++;
            Mesh drawnMesh = brick.Filter.sharedMesh;
            if (drawnMesh != null && drawnMesh.vertexCount > 0 && brick.Renderer.enabled)
            {
                drawn++;
                vertices += drawnMesh.vertexCount;
            }
        }

        return bricks;
    }

    /// <summary>What the brick at a point draws (else the nearest in the column there), for the console.</summary>
    public static string DebugInfo(Vector3 point)
    {
        WaterBrick found = BrickPick.Nearest(WaterBrick.All.Where(b => b != null && b.View != null), b => b.View, point.x, point.y, point.z, out float nearest);
        if (found == null)
        {
            return "no water brick in the column here";
        }

        BrickView view = found.View;
        Mesh mesh = found.Filter.sharedMesh;
        var text = new System.Text.StringBuilder();
        text.Append(FormattableString.Invariant($"brick {view.X},{view.Y},{view.Z} of {view.VoxelSize} m voxels at {found.transform.position}, {nearest:0.#} m from you: renderer {found.Renderer.enabled}, lift {WaterBrick.Lift}, flat {WaterBrick.Flat}"));
        text.Append(FormattableString.Invariant($"; mesh {mesh.vertexCount} vertices, {(mesh.subMeshCount > 0 ? mesh.GetIndexCount(0) / 3 : 0)} triangles, bounds {mesh.bounds.min} to {mesh.bounds.max}; drawn {Time.time - found.DrawnAt:0.0} s ago, building {found.AnyBuildBusy}, shaped on the card {found.ShapedOnCard}"));
        Material material = found.Renderer.sharedMaterial;
        text.Append(material == null
            ? "; material none"
            : FormattableString.Invariant($"; material {material.name} shader {material.shader.name} supported {material.shader.isSupported} queue {material.renderQueue} keywords {string.Join(" ", material.shaderKeywords)}"));
        return text.ToString();
    }
}
