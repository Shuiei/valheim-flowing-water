using System;

namespace FlowingWater.Volume;

/// <summary>
/// The ground soaking up water lying thin on it, and such water drying off, without the game. Nothing else ever took
/// water out of the world but a drain or the sea: a source left running spread its water as a sheet over every slope
/// and flat below it for as long as it ran, the bricks holding it piling up until the server fell behind (a test world
/// held 121,523 m³ from three sources). A voxel whose water stands on the ground (the voxel holds some, or a piece or
/// the ground closes it under) with no more than <see cref="SheetDeepest"/> of water over that ground in its column
/// loses the rate's metres of it a second: a sheet stops spreading where what it loses matches what feeds it, a stream
/// ends where it has soaked away, and a puddle left behind dries up. Deeper water (a pool, a river, a lake) is kept
/// whatever the rate: only its shallow edge soaks.
/// </summary>
internal static class Soaking
{
    /// <summary>
    /// The deepest water the ground soaks up, metres: over this in its column (the voxel's water and the wet voxels'
    /// over it), water is kept.
    /// </summary>
    public const float SheetDeepest = 0.1f;

    // Rows over a voxel its column's water is measured through (enough to pass SheetDeepest at any voxel size).
    private const int Over = 2;

    /// <summary>
    /// Takes <paramref name="metres"/> of water off each voxel of a brick lying thin on the ground (at most what it
    /// holds), voxels of <paramref name="voxelSize"/>; its change counted, so it is sent, but the brick not woken (a
    /// sheet drying needs no step). What was taken, in shares of a voxel.
    /// </summary>
    public static float Soak(Brick brick, float metres, float voxelSize)
    {
        if (!(metres > 0f) || brick.WetHigh.X < 0)
        {
            return 0f;
        }

        float share = metres / voxelSize, deepest = SheetDeepest / voxelSize, taken = 0f;
        float[] water = brick.Water;
        (int lx, int ly, int lz) = brick.WetLow;
        (int hx, int hy, int hz) = brick.WetHigh;
        for (int y = ly; y <= hy; y++)
        {
            for (int z = lz; z <= hz; z++)
            {
                for (int x = lx; x <= hx; x++)
                {
                    int i = Brick.Index(x, y, z);
                    float w = water[i];
                    if (w <= Brick.Dry || brick.Solid[i] >= 1f || !OnGround(brick, i) || Column(brick, i, deepest) > deepest)
                    {
                        continue;
                    }

                    float take = Math.Min(w, share);
                    water[i] = w - take <= Brick.Dry ? 0f : w - take;
                    taken += w - water[i];
                }
            }
        }

        if (taken > 0f)
        {
            brick.Changes++;
        }

        return taken;
    }

    // Whether the water in voxel i stands on the ground: the voxel holds some, or the one under it does, or a piece
    // closes it under (the face under it is that voxel's upper face); in a brick's lowest row with no brick under it,
    // the face under it shut against the brick forgotten there (solid, or closed).
    private static bool OnGround(Brick brick, int i)
    {
        if (brick.Solid[i] >= BrickData.HoldsGround)
        {
            return true;
        }

        return brick.Neighbour(i, 3, out Brick under, out int j)
            ? under.Solid[j] >= BrickData.HoldsGround || (under.Closed[j] & Brick.ClosedY) != 0
            : (i >> 8) == 0 && brick.Shuts(3, i);
    }

    // The water in voxel i's column from it up, in shares of a voxel: its own and the wet voxels' over it, up to Over
    // rows, the measuring stopped once past the deepest soaked.
    private static float Column(Brick brick, int i, float deepest)
    {
        float depth = brick.Water[i];
        Brick at = brick;
        int k = i;
        for (int row = 0; row < Over && depth <= deepest; row++)
        {
            if (!at.Neighbour(k, 2, out Brick over, out int j) || over.Water[j] <= Brick.Dry)
            {
                break;
            }

            depth += over.Water[j];
            at = over;
            k = j;
        }

        return depth;
    }
}
