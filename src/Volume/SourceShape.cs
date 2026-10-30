using System;

namespace FlowingWater.Volume;

/// <summary>
/// How a source's water leaves it: all round (out of every side of it, as every source did before 0.46.0 and as one
/// with no shape written still does), or the way it faces (its piece's turn, a heading from north), out of an outlet
/// <see cref="Width"/> wide (a point: <see cref="LeastWidth"/>), fanning out by <see cref="Spread"/> degrees across
/// it (0: straight on, a river; more, a jet spraying out), and thrown up by <see cref="Throw"/> degrees (0: along the
/// ground). Its water leaves the outlet as fast as it must to pass the rate through a slot <see cref="Nozzle"/> high,
/// <see cref="MostSpeed"/> at the most (the slot standing higher then): a slim strong jet runs fast, a wide river slow.
/// Shared by the server's water and the player's own; needs no game, so the unit tests check the arithmetic.
/// </summary>
internal readonly struct SourceShape : IEquatable<SourceShape>
{
    /// <summary>The widest outlet, metres.</summary>
    public const float MostWidth = 10f;

    /// <summary>A point's outlet, metres across: the narrowest water leaves through.</summary>
    public const float LeastWidth = 0.25f;

    /// <summary>The most a jet fans out, degrees from one side to the other.</summary>
    public const float MostSpread = 90f;

    /// <summary>The most a jet is thrown up, degrees over the ground.</summary>
    public const float MostThrow = 60f;

    /// <summary>How high the outlet's slot is, metres, while the water through it is under <see cref="MostSpeed"/>.</summary>
    public const float Nozzle = 0.25f;

    /// <summary>The fastest water leaves an outlet, metres a second.</summary>
    public const float MostSpeed = 12f;

    /// <summary>A shape as asked: each value held to its range, the heading to 0 up to 360 degrees.</summary>
    public SourceShape(bool allRound, float heading, float width, float spread, float throwUp)
    {
        AllRound = allRound;
        Heading = Wrap(heading);
        Width = Clamp(width, 0f, MostWidth);
        Spread = Clamp(spread, 0f, MostSpread);
        Throw = Clamp(throwUp, 0f, MostThrow);
    }

    /// <summary>All round: no facing, the water out of every side (the shape of a source with none written).</summary>
    public static SourceShape Round { get; } = new(true, 0f, 0f, 0f, 0f);

    /// <summary>A new source's shape facing a heading: a slim jet, a point fanning out 20 degrees, thrown up 30.</summary>
    public static SourceShape Jet(float heading) => new(false, heading, 0f, 20f, 30f);

    /// <summary>
    /// A shape as a source's object keeps it: facing a way when its flag is 1 (<see cref="Flag"/>), else all round (no
    /// flag written: placed before 0.46.0), its width, spread and throw kept either way, so one made all round faces
    /// with them again when turned back.
    /// </summary>
    public static SourceShape Kept(int flag, float heading, float width, float spread, float throwUp) => new(flag != 1, heading, width, spread, throwUp);

    /// <summary>The way a piece faces whose forward points (east, north), degrees clockwise from north (0 to 360; north for none).</summary>
    public static float HeadingOf(float forwardX, float forwardZ) => Wrap((float)(Math.Atan2(forwardX, forwardZ) * 180.0 / Math.PI));

    /// <summary>Out of every side, not the way it faces.</summary>
    public bool AllRound { get; }

    /// <summary>The way it faces, degrees clockwise from north (z).</summary>
    public float Heading { get; }

    /// <summary>The outlet's width as asked, metres (0: a point).</summary>
    public float Width { get; }

    /// <summary>How far it fans out, degrees from one side to the other.</summary>
    public float Spread { get; }

    /// <summary>How far up it is thrown, degrees over the ground.</summary>
    public float Throw { get; }

    /// <summary>The flag a source's object keeps for it (<see cref="Kept"/>): 1 facing a way, 0 all round.</summary>
    public int Flag => AllRound ? 0 : 1;

    /// <summary>The shape its water takes: all round, <see cref="Round"/> (the values kept for a turn back pour nothing), else itself.</summary>
    public SourceShape Poured => AllRound ? Round : this;

    /// <summary>Whether its water flies, thrown up out of the outlet (2 degrees at least), and lands farther out; else it runs out along the ground.</summary>
    public bool Flies => !AllRound && Throw >= 2f;

    /// <summary>The outlet's width the water leaves through, metres: <see cref="LeastWidth"/> at least.</summary>
    public float OutletWidth => Math.Max(Width, LeastWidth);

    /// <summary>Across the outlet, to the right of the way it faces: (east, north), a unit.</summary>
    public (float X, float Z) Right
    {
        get
        {
            double h = Heading * Math.PI / 180.0;
            return ((float)Math.Cos(h), (float)-Math.Sin(h));
        }
    }

    public static bool operator ==(SourceShape a, SourceShape b) => a.Equals(b);

    public static bool operator !=(SourceShape a, SourceShape b) => !a.Equals(b);

    /// <summary>
    /// The way its water goes from a place across the outlet (-1 its left end, 0 its middle, 1 its right end), a unit
    /// (x east, y up, z north): turned out by that share of half the spread, and thrown up.
    /// </summary>
    public Vec3 Direction(float across)
    {
        double turn = (Heading + (Clamp(across, -1f, 1f) * Spread / 2f)) * Math.PI / 180.0;
        double up = Throw * Math.PI / 180.0;
        double flat = Math.Cos(up);
        return new Vec3((float)(Math.Sin(turn) * flat), (float)Math.Sin(up), (float)(Math.Cos(turn) * flat));
    }

    /// <summary>How fast its water leaves the outlet for a rate (m³/s): the rate through the slot, <see cref="MostSpeed"/> at the most.</summary>
    public float Speed(float rate) => rate > 0f ? Math.Min(MostSpeed, rate / (OutletWidth * Nozzle)) : 0f;

    /// <summary>How high the outlet stands for a rate, metres: the slot, higher where the rate would be faster than <see cref="MostSpeed"/> through it.</summary>
    public float OutletHeight(float rate)
    {
        float speed = Speed(rate);
        return speed > 0f ? Math.Max(Nozzle, rate / (OutletWidth * speed)) : Nozzle;
    }

    /// <summary>
    /// The places across the outlet for cells (or voxels) this wide: as many as it spans (one at least), each its share
    /// across it (-1 to 1, as <see cref="Direction"/> takes it) and how far from the middle, metres.
    /// </summary>
    public (float Across, float Offset)[] Outlet(float cell)
    {
        int n = Math.Max(1, (int)Math.Round(OutletWidth / Math.Max(cell, 1e-3f)));
        var places = new (float Across, float Offset)[n];
        for (int k = 0; k < n; k++)
        {
            float share = n == 1 ? 0f : ((k + 0.5f) / n * 2f) - 1f;
            places[k] = (share, share * OutletWidth / 2f);
        }

        return places;
    }

    // Exact, as the hash is: the same values held to their ranges, not near ones.
    public bool Equals(SourceShape other) => AllRound == other.AllRound && Heading.Equals(other.Heading) && Width.Equals(other.Width) && Spread.Equals(other.Spread) && Throw.Equals(other.Throw);

    public override bool Equals(object obj) => obj is SourceShape other && Equals(other);

    public override int GetHashCode() => (AllRound, Heading, Width, Spread, Throw).GetHashCode();

    private static float Clamp(float value, float low, float high) => float.IsNaN(value) ? low : Math.Min(high, Math.Max(low, value));

    private static float Wrap(float degrees)
    {
        if (float.IsNaN(degrees) || float.IsInfinity(degrees))
        {
            return 0f;
        }

        float h = degrees % 360f;
        return h < 0f ? h + 360f : h;
    }
}
