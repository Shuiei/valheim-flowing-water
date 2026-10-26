using System.Collections.Generic;

namespace FlowingWater.Volume;

/// <summary>
/// A solid shape in the world, without the game: a collider of a building piece, a rock, a tree or a place's ruins.
/// Water cannot be in a voxel whose middle it holds, nor pass between two voxels whose middles it separates. What the
/// raster asks of every shape: how far it reaches, the parts of a voxel column's middle line inside it, and whether
/// it crosses the way between two voxels' middles.
/// </summary>
internal abstract class Solid
{
    /// <summary>The smallest box along the world's axes holding it: its lowest and highest corners.</summary>
    public abstract (Vec3 Min, Vec3 Max) Bounds();

    /// <summary>
    /// Whether it may meet the box along the world's axes from <paramref name="min"/> to <paramref name="max"/>: never
    /// false when they meet, but may be true when they do not.
    /// </summary>
    public abstract bool Overlaps(Vec3 min, Vec3 max);

    /// <summary>Whether the segment from one point to the other passes through the shape.</summary>
    public abstract bool Crosses(Vec3 from, Vec3 to);

    /// <summary>
    /// The parts of the vertical line at (<paramref name="x"/>, <paramref name="z"/>) between the heights
    /// <paramref name="bottom"/> and <paramref name="top"/> that are inside the shape, added as (low, high) heights,
    /// metres, from the top down; none when the line misses it.
    /// </summary>
    public abstract void Column(float x, float z, float bottom, float top, List<(float Low, float High)> spans);
}
