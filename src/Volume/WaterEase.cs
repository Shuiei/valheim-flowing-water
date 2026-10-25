using System;

namespace FlowingWater.Volume;

/// <summary>
/// The water a player's game draws of a brick, without the game: eased from what it drew towards the server's newest
/// data over <see cref="Seconds"/>, instead of jumping at each change, and without water too thin to draw steadily (a
/// voxel's water shows from <see cref="Drawing.ShowDepth"/> deep and stays shown down to <see cref="Drawing.HideDepth"/>),
/// so that a thin sheet whose water comes and goes by a few millimetres between changes does not flicker.
/// </summary>
internal static class WaterEase
{
    /// <summary>Seconds the water drawn takes to go from what it was to the newest data.</summary>
    public const float Seconds = 0.4f;

    /// <summary>How far an ease has gone after this many seconds: 0 at its start, 1 once done.</summary>
    public static float Share(float seconds) => Math.Max(0f, Math.Min(1f, seconds / Seconds));

    /// <summary>
    /// The water a share of the way from <paramref name="from"/> to <paramref name="to"/>, as a new view: each voxel's
    /// water and velocity in between, its floor and open faces the newest where it has water, else the ones it had (none
    /// where neither has water, so the blend never makes water where both have none). The newest itself when there is
    /// nothing to ease from (none, or another brick or grid) or the ease is done.
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
            BrickView floor = to.Water[i] > 0f ? to : from;
            blend.Water[i] = Between(from.Water[i], to.Water[i], share);
            blend.Solid[i] = floor.Solid[i];
            blend.Open[i] = floor.Open[i];
            blend.VelocityX[i] = Between(from.VelocityX[i], to.VelocityX[i], share);
            blend.VelocityY[i] = Between(from.VelocityY[i], to.VelocityY[i], share);
            blend.VelocityZ[i] = Between(from.VelocityZ[i], to.VelocityZ[i], share);
        }

        return blend;
    }

    /// <summary>
    /// The water of <paramref name="water"/> to draw: each voxel's where it is at least <see cref="Drawing.ShowDepth"/>
    /// deep, or <see cref="Drawing.HideDepth"/> where <paramref name="drawn"/> (the view drawn before, if any) had
    /// water; none elsewhere. A new view, or <paramref name="water"/> itself when all its water is drawn.
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
                Copy(water, shown, i);
            }
        }

        return shown;
    }

    // A voxel as it is in one view into another: everything the view keeps of it.
    private static void Copy(BrickView from, BrickView into, int i)
    {
        into.Water[i] = from.Water[i];
        into.Solid[i] = from.Solid[i];
        into.Open[i] = from.Open[i];
        into.VelocityX[i] = from.VelocityX[i];
        into.VelocityY[i] = from.VelocityY[i];
        into.VelocityZ[i] = from.VelocityZ[i];
    }

    // Whether a voxel's water is deep enough to draw: ShowDepth, or HideDepth where it was drawn.
    private static bool Shows(BrickView water, BrickView drawn, int i) =>
        water.Water[i] * water.VoxelSize >= (drawn != null && drawn.Water[i] > 0f ? Drawing.HideDepth : Drawing.ShowDepth);

    // Whether two views are of the same brick of the same grid.
    private static bool Alike(BrickView a, BrickView b) =>
        a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.VoxelSize.Equals(b.VoxelSize);

    private static float Between(float a, float b, float share) => a + ((b - a) * share);
}
