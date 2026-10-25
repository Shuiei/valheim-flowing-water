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

    /// <summary>
    /// Each voxel's ceiling: the underside of the lowest piece over its upper half, a share of the voxel from its bottom
    /// (the water on that piece is in the voxel above); 0 where there is none.
    /// </summary>
    public float[] Ceiling { get; } = new float[Count];

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

    // Its border voxels' faces water cannot cross into a brick forgotten beside it, as that brick was: a bit per face,
    // 256 a side (four words), sides as Beside's.
    private readonly ulong[] _shut = new ulong[6 * 4];

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

    /// <summary>Seconds in a row its water has barely moved (every flow under the solver's sleeping flow).</summary>
    public float Quiet { get; set; }

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

    /// <summary>Whether its wet voxels, as last found, reach its border on this side (0 east ... 5 south).</summary>
    public bool WetTo(int side) => WetHigh.X >= 0 && side switch
    {
        0 => WetHigh.X == Size - 1,
        1 => WetLow.X == 0,
        2 => WetHigh.Y == Size - 1,
        3 => WetLow.Y == 0,
        4 => WetHigh.Z == Size - 1,
        _ => WetLow.Z == 0,
    };

    /// <summary>Seconds it has held no water, to forget it.</summary>
    public float EmptyFor { get; set; }

    // Whether it held water when last looked, and its count of changes then: a brick unchanged since (asleep) is not
    // looked over again.
    private bool _heldWater;

    private int _heldAt;

    private bool _held;

    /// <summary>
    /// Whether any voxel holds more than <see cref="Dry"/>: looked over only when its water changed since (a sleeping
    /// brick's answer is kept), its wet voxels' box first.
    /// </summary>
    public bool HoldsWater()
    {
        if (_held && _heldAt == Changes)
        {
            return _heldWater;
        }

        _held = true;
        _heldAt = Changes;
        _heldWater = WetInBox() || Array.Exists(Water, w => w > Dry);
        return _heldWater;
    }

    // Whether a voxel of the wet voxels' box holds water.
    private bool WetInBox()
    {
        for (int y = WetLow.Y; y <= WetHigh.Y; y++)
        {
            for (int z = WetLow.Z; z <= WetHigh.Z; z++)
            {
                for (int x = WetLow.X; x <= WetHigh.X; x++)
                {
                    if (Water[Index(x, y, z)] > Dry)
                    {
                        return true;
                    }
                }
            }
        }

        return false;
    }

    /// <summary>
    /// Whether water in border voxel i cannot cross its face on this side (0 east ... 5 south): the brick closes it, or
    /// the brick last there (forgotten since) was solid or closed it there.
    /// </summary>
    public bool Shuts(int side, int i)
    {
        if (side % 2 == 0 && (Closed[i] & (1 << (side / 2))) != 0)
        {
            return true;
        }

        int face = Face(side / 2, i);
        return (_shut[(side * 4) + (face >> 6)] & (1UL << (face & 63))) != 0;
    }

    /// <summary>
    /// Keeps which of its border faces on this side water cannot cross into the brick there, about to be forgotten:
    /// that voxel is solid, or a piece closes the face.
    /// </summary>
    public void ShutBy(int side, Brick next)
    {
        int axis = side / 2;
        bool far = side % 2 == 0;
        byte bit = (byte)(1 << axis);
        Array.Clear(_shut, side * 4, 4);
        for (int face = 0; face < Size * Size; face++)
        {
            int a = face >> 4, b = face & (Size - 1);
            int own = Index(axis, far ? Size - 1 : 0, a, b);
            int other = Index(axis, far ? 0 : Size - 1, a, b);

            // A face belongs to the voxel west, under or south of it.
            bool closed = ((far ? Closed[own] : next.Closed[other]) & bit) != 0;
            if (closed || next.Solid[other] >= 1f)
            {
                _shut[(side * 4) + (face >> 6)] |= 1UL << (face & 63);
            }
        }
    }

    /// <summary>Forgets which border faces were shut (its solids are read again: they may have opened).</summary>
    public void Unshut() => Array.Clear(_shut, 0, _shut.Length);

    // A border face's number on its side: the voxel's place across the axis (y then z across x, z then x across y,
    // y then x across z).
    private static int Face(int axis, int i) => axis == 0 ? i >> 4 : axis == 1 ? i & ((Size * Size) - 1) : ((i >> 8) << 4) | (i & (Size - 1));

    // The voxel at this layer along the axis and place (a, b) across it, as Face numbers it.
    private static int Index(int axis, int layer, int a, int b) => axis == 0 ? Index(layer, a, b) : axis == 1 ? Index(b, layer, a) : Index(b, a, layer);

    /// <summary>A voxel's index in the brick's arrays: rows along x, then z, then layers up.</summary>
    public static int Index(int x, int y, int z) => (y * Size * Size) + (z * Size) + x;

    /// <summary>The voxel at an index, in the brick (0 to 15 each).</summary>
    public static (int X, int Y, int Z) Place(int index) => (index & (Size - 1), index >> 8, (index >> 4) & (Size - 1));
}
