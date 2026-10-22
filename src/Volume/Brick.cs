using System;

namespace FlowingWater.Volume;

/// <summary>
/// A block of 16 by 16 by 16 voxels of the water grid, without the game. Each voxel holds its water (a share of
/// the voxel's volume), how much of it is solid (ground or a piece, filling it from its bottom), which of its faces
/// a piece closes, and the flow through its east, upper and north faces (the others belong to its neighbours).
/// </summary>
internal sealed class Brick
{
    /// <summary>Voxels along each side.</summary>
    public const int Size = 16;

    /// <summary>Voxels in a brick.</summary>
    public const int Count = Size * Size * Size;

    /// <summary>Less water than this share of a voxel is none: it cannot flow, and a brick holding no more is dry.</summary>
    public const float Dry = 1e-5f;

    /// <summary>Bits of <see cref="Closed"/>: a piece closes the voxel's east, upper or north face.</summary>
    public const byte ClosedX = 1, ClosedY = 2, ClosedZ = 4;

    // What a sleeping brick reads for its flows, heads, shares and part: all nothing, shared by every sleeping brick
    // and never written (a sleeping brick is not stepped).
    private static readonly float[] Nothing = new float[Count];

    private static readonly int[] NoPart = new int[Count];

    /// <summary>A brick at these brick coordinates (voxel coordinates over 16).</summary>
    public Brick(int x, int y, int z)
    {
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>The brick's place in bricks: its first voxel is at 16 times these.</summary>
    public int X { get; }

    /// <inheritdoc cref="X"/>
    public int Y { get; }

    /// <inheritdoc cref="X"/>
    public int Z { get; }

    /// <summary>Each voxel's water, as a share of its volume (a little over what it can hold when pressed).</summary>
    public float[] Water { get; } = new float[Count];

    /// <summary>Each voxel's solid share (ground or a piece, from its bottom up; 1: all solid).</summary>
    public float[] Solid { get; } = new float[Count];

    /// <summary>Each voxel's faces closed by a piece (<see cref="ClosedX"/>, <see cref="ClosedY"/>, <see cref="ClosedZ"/>).</summary>
    public byte[] Closed { get; } = new byte[Count];

    /// <summary>Water through each voxel's east face, cubic metres a second (positive: going east).</summary>
    public float[] FlowX { get; private set; } = new float[Count];

    /// <summary>Through its upper face (positive: going up).</summary>
    public float[] FlowY { get; private set; } = new float[Count];

    /// <summary>Through its north face (positive: going north).</summary>
    public float[] FlowZ { get; private set; } = new float[Count];

    /// <summary>Each voxel's head for the step being worked out: the level its water pushes to, metres.</summary>
    public float[] Head { get; private set; } = new float[Count];

    /// <summary>The share of each voxel's outflow it can give this step (less than 1 when it has less water).</summary>
    public float[] Share { get; private set; } = new float[Count];

    /// <summary>The bricks beside it, east, west, up, down, north and south (null where there is none).</summary>
    public Brick[] Beside { get; } = new Brick[6];

    /// <summary>The part of the brick a step works on: its lowest voxel (x, y, z), past its highest when it has none.</summary>
    public (int X, int Y, int Z) Low { get; set; }

    /// <summary>The part's highest voxel.</summary>
    public (int X, int Y, int Z) High { get; set; } = (-1, -1, -1);

    /// <summary>The part worked on last step (its flows there were last set then).</summary>
    public (int X, int Y, int Z) LastLow { get; set; }

    /// <summary>Its highest voxel.</summary>
    public (int X, int Y, int Z) LastHigh { get; set; } = (-1, -1, -1);

    /// <summary>The wet voxels' lowest corner, as last found; past the highest when it holds none.</summary>
    public (int X, int Y, int Z) WetLow { get; set; }

    /// <summary>Their highest corner.</summary>
    public (int X, int Y, int Z) WetHigh { get; set; } = (-1, -1, -1);

    /// <summary>The voxel indices of the part a step works on (the first <see cref="PartCount"/>), in array order.</summary>
    public int[] Part { get; private set; } = new int[Count];

    /// <summary>How many voxels the part holds.</summary>
    public int PartCount { get; set; }

    /// <summary>Steps in a row its water has barely moved (every flow under the solver's sleeping flow).</summary>
    public int Quiet { get; set; }

    /// <summary>Whether it sleeps this step: skipped, and no water crosses its faces.</summary>
    public bool Asleep { get; set; }

    /// <summary>
    /// Counts the changes to its water: each step that works on it (or lets its flows go as it falls asleep), and
    /// water or solids put in from outside a step. While it stays the same, so does the brick's data.
    /// </summary>
    public int Changes { get; set; }

    /// <summary>Whether it rests: asleep, holding only its water and solids (its flows read as none).</summary>
    public bool Resting => FlowX == Nothing;

    /// <summary>
    /// Lets go of what only stepping uses, as it falls asleep (about three quarters of its memory): its flows, heads,
    /// shares and part read as none until it wakes.
    /// </summary>
    public void Rest()
    {
        FlowX = FlowY = FlowZ = Head = Share = Nothing;
        Part = NoPart;
    }

    /// <summary>Takes back what stepping uses, cleared, as it wakes from a rest.</summary>
    public void Wake()
    {
        if (!Resting)
        {
            return;
        }

        FlowX = new float[Count];
        FlowY = new float[Count];
        FlowZ = new float[Count];
        Head = new float[Count];
        Share = new float[Count];
        Part = new int[Count];
    }

    /// <summary>Takes voxel i into the box of the wet voxels (water was put there from outside a step).</summary>
    public void Wets(int i)
    {
        Changes++;
        (int x, int y, int z) = Place(i);
        bool none = WetHigh.X < 0;
        WetLow = none ? (x, y, z) : (Math.Min(WetLow.X, x), Math.Min(WetLow.Y, y), Math.Min(WetLow.Z, z));
        WetHigh = none ? (x, y, z) : (Math.Max(WetHigh.X, x), Math.Max(WetHigh.Y, y), Math.Max(WetHigh.Z, z));
        Quiet = 0;
    }

    /// <summary>Seconds it has held no water, to forget it.</summary>
    public float EmptyFor { get; set; }

    /// <summary>A voxel's index in the brick's arrays: rows along x, then z, then layers up.</summary>
    public static int Index(int x, int y, int z) => (y * Size * Size) + (z * Size) + x;

    /// <summary>The voxel at an index, in the brick (0 to 15 each).</summary>
    public static (int X, int Y, int Z) Place(int index) => (index & (Size - 1), index >> 8, (index >> 4) & (Size - 1));
}
