using System;

namespace FlowingWater.Volume;

/// <summary>
/// The water a player's game draws of a brick, without the game: eased from what it drew towards the server's newest
/// data over <see cref="Seconds"/>, instead of jumping at each change, and without water too thin to draw steadily (a
/// voxel's water shows from <see cref="ShowDepth"/> deep and stays shown down to <see cref="KeepDepth"/>), so that a
/// thin sheet whose water comes and goes by a few millimetres between changes does not flicker.
/// </summary>
internal static class WaterEase
{
    /// <summary>Seconds the water drawn takes to go from what it was to the newest data.</summary>
    public const float Seconds = 0.4f;

    /// <summary>The least water drawn in a voxel that was not drawn, metres deep.</summary>
    public const float ShowDepth = 0.03f;

    /// <summary>The least water kept drawn in a voxel that was, metres deep.</summary>
    public const float KeepDepth = 0.01f;

    /// <summary>How far an ease has gone after this many seconds: 0 at its start, 1 once done.</summary>
    public static float Share(float seconds) => Math.Max(0f, Math.Min(1f, seconds / Seconds));

    /// <summary>
    /// The water a share of the way from <paramref name="from"/> to <paramref name="to"/>, as a new view: each voxel's
    /// water and velocity in between, its floor the newest where it has water, else the one it had (none where neither
    /// has water, so the blend never makes water where both have none). The newest itself when there is nothing to
    /// ease from (none, or another brick or grid) or the ease is done.
    /// </summary>
    public static BrickView Blend(BrickView from, BrickView to, float share)
    {
        if (from == null || share >= 1f || !Alike(from, to))
        {
            return to;
        }

        share = Math.Max(0f, share);
        var blend = new BrickView(to.X, to.Y, to.Z, to.VoxelSize);
        for (int i = 0; i < Brick.Count; i++)
        {
            blend.Water[i] = Between(from.Water[i], to.Water[i], share);
            blend.Solid[i] = to.Water[i] > 0f ? to.Solid[i] : from.Solid[i];
            blend.VelocityX[i] = Between(from.VelocityX[i], to.VelocityX[i], share);
            blend.VelocityY[i] = Between(from.VelocityY[i], to.VelocityY[i], share);
            blend.VelocityZ[i] = Between(from.VelocityZ[i], to.VelocityZ[i], share);
        }

        return blend;
    }

    /// <summary>
    /// The water of <paramref name="water"/> to draw: each voxel's where it is at least <see cref="ShowDepth"/> deep,
    /// or <see cref="KeepDepth"/> where <paramref name="drawn"/> (the view drawn before, if any) had water; none
    /// elsewhere. A new view, or <paramref name="water"/> itself when all its water is drawn.
    /// </summary>
    public static BrickView Show(BrickView water, BrickView drawn)
    {
        // What was drawn of another grid says nothing of this one.
        drawn = drawn != null && Alike(drawn, water) ? drawn : null;
        bool all = true;
        for (int i = 0; i < Brick.Count && all; i++)
        {
            all = water.Water[i] <= 0f || Shows(water, drawn, i);
        }

        if (all)
        {
            return water;
        }

        var shown = new BrickView(water.X, water.Y, water.Z, water.VoxelSize);
        for (int i = 0; i < Brick.Count; i++)
        {
            if (Shows(water, drawn, i))
            {
                shown.Water[i] = water.Water[i];
                shown.Solid[i] = water.Solid[i];
                shown.VelocityX[i] = water.VelocityX[i];
                shown.VelocityY[i] = water.VelocityY[i];
                shown.VelocityZ[i] = water.VelocityZ[i];
            }
        }

        return shown;
    }

    // Whether a voxel's water is deep enough to draw: ShowDepth, or KeepDepth where it was drawn.
    private static bool Shows(BrickView water, BrickView drawn, int i) =>
        water.Water[i] * water.VoxelSize >= (drawn != null && drawn.Water[i] > 0f ? KeepDepth : ShowDepth);

    // Whether two views are of the same brick of the same grid.
    private static bool Alike(BrickView a, BrickView b) =>
        a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.VoxelSize.Equals(b.VoxelSize);

    private static float Between(float a, float b, float share) => a + ((b - a) * share);
}
