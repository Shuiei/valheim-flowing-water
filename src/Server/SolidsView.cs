using System.Collections.Generic;
using System.Text;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Server;

/// <summary>
/// What the water sees as solid round a point, as text for the console: the voxel row at the point (solid voxels
/// and the side faces pieces close) and the faces under it (a floor), north up.
/// </summary>
internal static class SolidsView
{
    private const int Reach = 12;

    /// <summary>The two maps round a point, line by line, with the counts of what they show.</summary>
    public static IEnumerable<string> Around(Vector3 point, float voxelSize)
    {
        var solids = new GameSolids(new ZoneTerrain());
        var grid = new VoxelGrid(voxelSize, solids);
        int cx = Mathf.FloorToInt(point.x / voxelSize);
        int cy = Mathf.FloorToInt((point.y + 0.1f) / voxelSize);
        int cz = Mathf.FloorToInt(point.z / voxelSize);
        var row = new StringBuilder();
        var under = new StringBuilder();
        int solid = 0;
        int sides = 0;
        int floors = 0;
        yield return FormattableString($"water's solids round {point.x:0.0}, {point.y:0.0}, {point.z:0.0} ({voxelSize:0.##} m voxels), row {cy}: '#' solid, '|' east face closed, '_' north face closed, '+' both; under it '=' floor");
        var lines = new List<string>();
        for (int z = cz + Reach; z >= cz - Reach; z--)
        {
            row.Clear();
            under.Clear();
            for (int x = cx - Reach; x <= cx + Reach; x++)
            {
                Brick brick = grid.Get(x >> 4, cy >> 4, z >> 4);
                int i = Brick.Index(x & 15, cy & 15, z & 15);
                Brick below = grid.Get(x >> 4, (cy - 1) >> 4, z >> 4);
                int j = Brick.Index(x & 15, (cy - 1) & 15, z & 15);
                byte closed = brick.Closed[i];
                bool full = brick.Solid[i] >= 1f;
                bool floor = (below.Closed[j] & Brick.ClosedY) != 0;
                solid += full ? 1 : 0;
                sides += (closed & (Brick.ClosedX | Brick.ClosedZ)) != 0 ? 1 : 0;
                floors += floor ? 1 : 0;
                row.Append(full ? '#' : Face(closed));
                under.Append(floor ? '=' : below.Solid[j] >= 1f ? '#' : '.');
            }

            lines.Add(row + "   " + under);
        }

        foreach (string line in lines)
        {
            yield return line;
        }

        yield return FormattableString($"{solid} solid voxels, {sides} with a side face closed, {floors} floor faces under the row");
    }

    private static char Face(byte closed) => (closed & (Brick.ClosedX | Brick.ClosedZ)) switch
    {
        Brick.ClosedX => '|',
        Brick.ClosedZ => '_',
        Brick.ClosedX | Brick.ClosedZ => '+',
        _ => '.',
    };

    private static string FormattableString(System.FormattableString text) => System.FormattableString.Invariant(text);
}
