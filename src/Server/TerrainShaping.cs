using System;
using System.Collections.Generic;

namespace FlowingWater.Server;

/// <summary>
/// What the game's terrain modifiers make of a zone's generated ground before the players' changes go over it: a
/// location's levelled yard and the smoothing round it, or an old shaping piece's. Each, in the game's order, sets
/// the vertices within its levelling reach (a square or a circle of vertices round the one nearest it) to its height,
/// then draws those within its smoothing reach toward that height, the more the nearer it they are. A shaping piece
/// a player placed keeps the ground within 8 m of what it was before the players' shaping began, and its smoothing
/// within a metre of what the levelling left. Needs no game, so the unit tests check it.
/// </summary>
internal static class TerrainShaping
{
    /// <summary>A zone's vertices a side, less one: one a metre.</summary>
    public const int Width = 64;

    /// <summary>How far past its own reach the game takes a modifier as touching a zone, metres.</summary>
    public const float Margin = 4f;

    // How far a player's shaping piece may take the ground from before the players' shaping, and its smoothing from
    // what the levelling left, metres.
    private const float PlayerLevelMost = 8f;

    private const float PlayerSmoothMost = 1f;

    private const int Side = Width + 1;

    /// <summary>
    /// Shapes a zone's heights (<see cref="Side"/> by <see cref="Side"/> vertices, index z * 65 + x, a metre apart,
    /// the zone's centre at <paramref name="centreX"/>, <paramref name="centreZ"/>) with the modifiers, sorted in the
    /// game's order first (the list is sorted in place); those not touching the zone change nothing.
    /// </summary>
    public static void Shape(float[] heights, float centreX, float centreZ, List<Modifier> modifiers)
    {
        if (heights.Length != Side * Side)
        {
            throw new ArgumentException($"a zone's heights are {Side * Side}, not {heights.Length}", nameof(heights));
        }

        modifiers.Sort(Order);
        float[] before = null;
        float[] levelled = null;
        foreach (Modifier modifier in modifiers)
        {
            if (!Touches(modifier, centreX, centreZ))
            {
                continue;
            }

            if (modifier.PlayerMade && before == null)
            {
                // The ground before the players' shaping, and as each levelling leaves it (what smoothing is held to).
                before = (float[])heights.Clone();
                levelled = (float[])heights.Clone();
            }

            float y = modifier.Y + modifier.LevelOffset;
            if (modifier.Level)
            {
                Level(heights, centreX, centreZ, modifier, y, before, levelled);
            }

            if (modifier.Smooth)
            {
                SmoothToward(heights, centreX, centreZ, modifier, y, levelled);
            }
        }
    }

    /// <summary>Whether a modifier reaches within <see cref="Margin"/> of the zone's square (the game's test).</summary>
    public static bool Touches(Modifier modifier, float centreX, float centreZ)
    {
        float reach = modifier.Radius + Margin, half = Width * 0.5f;
        return modifier.X + reach >= centreX - half && modifier.X - reach <= centreX + half
            && modifier.Z + reach >= centreZ - half && modifier.Z - reach <= centreZ + half;
    }

    // The game's order: those no player placed first, then by sort order, by when made, and by distance from the
    // world's centre.
    private static int Order(Modifier a, Modifier b)
    {
        if (a.PlayerMade != b.PlayerMade)
        {
            return a.PlayerMade.CompareTo(b.PlayerMade);
        }

        if (a.SortOrder != b.SortOrder)
        {
            return a.SortOrder.CompareTo(b.SortOrder);
        }

        if (a.Created != b.Created)
        {
            return a.Created.CompareTo(b.Created);
        }

        float da = (a.X * a.X) + (a.Y * a.Y) + (a.Z * a.Z), db = (b.X * b.X) + (b.Y * b.Y) + (b.Z * b.Z);
        return da.CompareTo(db);
    }

    // The vertex nearest a point, its column and row in the zone (outside it where the point is).
    private static (int X, int Z) Nearest(float x, float z, float centreX, float centreZ) =>
        ((int)Math.Floor(x - centreX + 0.5f) + (Width / 2), (int)Math.Floor(z - centreZ + 0.5f) + (Width / 2));

    private static void Level(float[] heights, float centreX, float centreZ, Modifier modifier, float y, float[] before, float[] levelled)
    {
        (int cx, int cz) = Nearest(modifier.X, modifier.Z, centreX, centreZ);
        int reach = (int)Math.Ceiling(modifier.LevelRadius);
        for (int z = Math.Max(cz - reach, 0); z <= Math.Min(cz + reach, Width); z++)
        {
            for (int x = Math.Max(cx - reach, 0); x <= Math.Min(cx + reach, Width); x++)
            {
                if (!modifier.Square && Apart(x - cx, z - cz) > modifier.LevelRadius)
                {
                    continue;
                }

                int i = (z * Side) + x;
                float height = y;
                if (modifier.PlayerMade)
                {
                    height = Math.Min(Math.Max(height, before[i] - PlayerLevelMost), before[i] + PlayerLevelMost);
                    levelled[i] = height;
                }

                heights[i] = height;
            }
        }
    }

    private static void SmoothToward(float[] heights, float centreX, float centreZ, Modifier modifier, float y, float[] levelled)
    {
        (int cx, int cz) = Nearest(modifier.X, modifier.Z, centreX, centreZ);
        float radius = modifier.SmoothRadius;
        int reach = (int)Math.Ceiling(radius);
        for (int z = Math.Max(cz - reach, 0); z <= Math.Min(cz + reach, Width); z++)
        {
            for (int x = Math.Max(cx - reach, 0); x <= Math.Min(cx + reach, Width); x++)
            {
                float apart = Apart(x - cx, z - cz);
                if (apart > radius)
                {
                    continue;
                }

                // Toward the modifier's height, wholly at it, not at all at the reach's edge.
                float share = apart / radius;
                share = (float)Math.Pow(share, modifier.SmoothPower);
                int i = (z * Side) + x;
                float height = heights[i] + ((y - heights[i]) * (1f - share));
                if (modifier.PlayerMade)
                {
                    height = Math.Min(Math.Max(height, levelled[i] - PlayerSmoothMost), levelled[i] + PlayerSmoothMost);
                }

                heights[i] = height;
            }
        }
    }

    private static float Apart(int dx, int dz) => (float)Math.Sqrt((dx * dx) + (dz * dz));

    /// <summary>A terrain modifier where it stands (world, before its level offset) and what it does.</summary>
    internal sealed class Modifier
    {
        public float X { get; set; }

        public float Y { get; set; }

        public float Z { get; set; }

        /// <summary>Metres over its place it levels and smooths to.</summary>
        public float LevelOffset { get; set; }

        public bool Level { get; set; }

        public float LevelRadius { get; set; }

        /// <summary>Levels a square of vertices, not a circle.</summary>
        public bool Square { get; set; }

        public bool Smooth { get; set; }

        public float SmoothRadius { get; set; }

        public float SmoothPower { get; set; } = 3f;

        /// <summary>The farthest it reaches, metres (its paint's reach counts, as the game counts it).</summary>
        public float Radius { get; set; }

        public int SortOrder { get; set; }

        /// <summary>When it was made (0: with its location, no object of its own).</summary>
        public long Created { get; set; }

        /// <summary>A shaping piece a player placed, not a location's.</summary>
        public bool PlayerMade { get; set; }
    }
}
