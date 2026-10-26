using System;
using System.Collections.Concurrent;
using System.Threading;

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

    /// <summary>Voxels in a layer of a brick (one row of index steps up).</summary>
    public const int Layer = Size * Size;

    /// <summary>Less water than this share of a voxel is none: it cannot flow, and a brick holding no more is dry.</summary>
    public const float Dry = 1e-5f;

    /// <summary>Bits of <see cref="Closed"/>: a piece closes the voxel's east, upper or north face.</summary>
    public const byte ClosedX = 1, ClosedY = 2, ClosedZ = 4;

    // What a sleeping brick reads for its flows, heads, shares and part: all nothing, shared by every sleeping brick
    // and never written (a sleeping brick is not stepped).
    private static readonly float[] Nothing = new float[Count];

    private static readonly int[] NoPart = new int[Count];

    // The arrays bricks let go of as they fall asleep, kept for those that wake: a brick waking every second or so
    // beside moving water would otherwise have 96 KB made for it each time. Only so many bricks' worth are kept (the
    // churn of waking and sleeping, not every brick of a pool gone still: that memory is what falling asleep gives
    // back); the rest go to the collector.
    private const int MostSpare = 64;

    private static readonly ConcurrentBag<float[]> SpareFloats = [];

    private static readonly ConcurrentBag<int[]> SpareParts = [];

    private static int _spareCount;

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

    /// <summary>
    /// The flows through each voxel's east, upper and north faces once the water has carried its momentum over the step
    /// being worked out, before the heads push on them (as <see cref="FlowX"/>).
    /// </summary>
    public float[] CarriedX { get; private set; } = new float[Count];

    /// <inheritdoc cref="CarriedX"/>
    public float[] CarriedY { get; private set; } = new float[Count];

    /// <inheritdoc cref="CarriedX"/>
    public float[] CarriedZ { get; private set; } = new float[Count];

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

    /// <summary>The grid's <see cref="VoxelGrid.Version"/> when it was made: later than a version, it was made since.</summary>
    public int MadeAt { get; set; }

    /// <summary>
    /// Counts the changes to its water: each step that works on it (or lets its flows go as it falls asleep), and
    /// water or solids put in from outside a step. While it stays the same, so does the brick's data.
    /// </summary>
    public int Changes { get; set; }

    /// <summary>Whether it rests: asleep, holding only its water and solids (its flows read as none).</summary>
    public bool Resting => FlowX == Nothing;

    /// <summary>
    /// Lets go of what only stepping uses, as it falls asleep (about three quarters of its memory): its flows, carried
    /// flows, heads, shares and part read as none until it wakes. What it lets go of is kept for the next brick to wake.
    /// </summary>
    public void Rest()
    {
        if (Resting)
        {
            return;
        }

        if (Interlocked.Increment(ref _spareCount) <= MostSpare)
        {
            foreach (float[] floats in new[] { FlowX, FlowY, FlowZ, CarriedX, CarriedY, CarriedZ, Head, Share })
            {
                SpareFloats.Add(floats);
            }

            SpareParts.Add(Part);
        }
        else
        {
            Interlocked.Decrement(ref _spareCount);
        }

        FlowX = FlowY = FlowZ = CarriedX = CarriedY = CarriedZ = Head = Share = Nothing;
        Part = NoPart;
    }

    /// <summary>Takes back what stepping uses, cleared, as it wakes from a rest.</summary>
    public void Wake()
    {
        if (!Resting)
        {
            return;
        }

        if (SpareParts.TryTake(out int[] part))
        {
            Interlocked.Decrement(ref _spareCount);
            Part = part;
            FlowX = Spare();
            FlowY = Spare();
            FlowZ = Spare();
            CarriedX = Spare();
            CarriedY = Spare();
            CarriedZ = Spare();
            Head = Spare();
            Share = Spare();
            return;
        }

        FlowX = new float[Count];
        FlowY = new float[Count];
        FlowZ = new float[Count];
        CarriedX = new float[Count];
        CarriedY = new float[Count];
        CarriedZ = new float[Count];
        Head = new float[Count];
        Share = new float[Count];
        Part = new int[Count];
    }

    // A spare array of floats, cleared, or a new one.
    private static float[] Spare()
    {
        if (!SpareFloats.TryTake(out float[] spare))
        {
            return new float[Count];
        }

        Array.Clear(spare, 0, Count);
        return spare;
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

    /// <summary>
    /// The bricks beside it water asked for this step, a bit a side as <see cref="Beside"/>: each is asked for once a
    /// step, not once a border voxel. Cleared as the step's part is found.
    /// </summary>
    public byte Asked { get; set; }

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

    /// <summary>
    /// Whether water came into it since last looked at (<see cref="VoxelGrid.ForgetDry"/>), the sea's or not: a brick
    /// the sea takes every drop from holds none at a frame's end, yet is not dry.
    /// </summary>
    public bool Fed { get; set; }

    // Whether it held water when last looked, and its count of changes then: a brick unchanged since (asleep) is not
    // looked over again.
    private bool _heldWater;

    private int _heldAt;

    private bool _held;

    /// <summary>
    /// Whether any voxel holds more than <see cref="Dry"/>: looked over only when its water changed since (a sleeping
    /// brick's answer is kept), and only within its wet voxels' box, which every way water comes in keeps (a step's part,
    /// a pour, a settle into a wet voxel); <see cref="HoldsAnyWater"/> looks over every voxel.
    /// </summary>
    public bool HoldsWater()
    {
        if (_held && _heldAt == Changes)
        {
            return _heldWater;
        }

        _held = true;
        _heldAt = Changes;
        _heldWater = WetInBox();
        return _heldWater;
    }

    /// <summary>
    /// Whether any voxel at all holds more than <see cref="Dry"/>, outside the wet voxels' box too (every voxel looked
    /// at: for a brick about to be forgotten, not every frame). Water found outside the box is taken into it.
    /// </summary>
    public bool HoldsAnyWater()
    {
        if (HoldsWater())
        {
            return true;
        }

        int i = Array.FindIndex(Water, w => w > Dry);
        if (i < 0)
        {
            return false;
        }

        Wets(i);
        return true;
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
            int own = LayerIndex(axis, far ? Size - 1 : 0, a, b);
            int other = LayerIndex(axis, far ? 0 : Size - 1, a, b);

            // A face belongs to the voxel west, under or south of it.
            bool closed = ((far ? Closed[own] : next.Closed[other]) & bit) != 0;
            if (closed || next.Solid[other] >= 1f)
            {
                _shut[(side * 4) + (face >> 6)] |= 1UL << (face & 63);
            }
        }
    }

    /// <summary>
    /// The faces of a voxel water could leave it by, a bit per side as <see cref="Beside"/> (east, west, up, down,
    /// north, south): those no piece closes, not onto a solid voxel, and not shut against a brick forgotten there. A
    /// face onto a brick there is none of is open. Besides, <see cref="BrickData.GroundUnder"/> where the voxel under it
    /// holds ground, and <see cref="BrickData.GroundBeside"/> where a side open toward water a voxel lower has ground in
    /// the dry voxel beside it.
    /// </summary>
    public byte OpenFaces(int i)
    {
        int open = 0;
        for (int side = 0; side < 6; side++)
        {
            int axis = side >> 1;
            bool far = (side & 1) == 0;
            int bit = 1 << axis;
            bool closed;
            if (!Neighbour(i, side, out Brick next, out int j))
            {
                // Its own face (east, up, north) closed by a piece, or shut against a brick forgotten there.
                int face = Face(axis, i);
                closed = (far && (Closed[i] & bit) != 0) || (_shut[(side * 4) + (face >> 6)] & (1UL << (face & 63))) != 0;
            }
            else
            {
                // A face belongs to the voxel west, under or south of it.
                closed = ((far ? Closed[i] : next.Closed[j]) & bit) != 0 || next.Solid[j] >= 1f;
            }

            if (!closed)
            {
                open |= 1 << side;
                if (side is not (2 or 3) && GroundBesideLowerWater(i, side))
                {
                    open |= BrickData.GroundBeside;
                }
            }
        }

        if (Neighbour(i, 3, out Brick under, out int k) && under.Solid[k] >= BrickData.HoldsGround)
        {
            open |= BrickData.GroundUnder;
        }

        return (byte)open;
    }

    // Whether the voxel beside voxel i on this side is dry as the players' games see it, holds some ground, and has water
    // under it. Dry there is under a step of the bytes sent (with what the steps before carry, a film up to one step may
    // be sent as none; one sent wet is drawn as water beside, the bit unread).
    private bool GroundBesideLowerWater(int i, int side) =>
        Neighbour(i, side, out Brick next, out int j) && next.Water[j] < 1f / BrickData.Full && next.Solid[j] >= BrickData.HoldsGround
        && next.Neighbour(j, 3, out Brick under, out int k) && under.Water[k] > 0f;

    /// <summary>Forgets which border faces were shut (its solids are read again: they may have opened).</summary>
    public void Unshut() => Array.Clear(_shut, 0, _shut.Length);

    /// <summary>
    /// The voxel beside voxel i on this side (0 east, 1 west, 2 up, 3 down, 4 north, 5 south): in this brick, or in
    /// the brick beside when i is on the border that way. False, with no brick, when there is none there. The solver's
    /// hot loops have their own, each one way (<c>East</c>, <c>FromWest</c>...).
    /// </summary>
    public bool Neighbour(int i, int side, out Brick other, out int j)
    {
        int axis = side >> 1;
        bool far = (side & 1) == 0;
        int along = axis == 0 ? i & (Size - 1) : axis == 1 ? i >> 8 : (i >> 4) & (Size - 1);
        int step = axis == 0 ? 1 : axis == 1 ? Layer : Size;
        bool inside = far ? along < Size - 1 : along > 0;
        other = inside ? this : Beside[side];
        j = inside ? (far ? i + step : i - step) : (far ? i - ((Size - 1) * step) : i + ((Size - 1) * step));
        return other != null;
    }

    // A border face's number on its side: the voxel's place across the axis (y then z across x, z then x across y,
    // y then x across z).
    private static int Face(int axis, int i) => axis == 0 ? i >> 4 : axis == 1 ? i & (Layer - 1) : ((i >> 8) << 4) | (i & (Size - 1));

    // The voxel at this layer along the axis and place (a, b) across it, as Face numbers it.
    private static int LayerIndex(int axis, int layer, int a, int b) => axis == 0 ? Index(layer, a, b) : axis == 1 ? Index(b, layer, a) : Index(b, a, layer);

    /// <summary>A voxel's index in the brick's arrays: rows along x, then z, then layers up.</summary>
    public static int Index(int x, int y, int z) => (y * Layer) + (z * Size) + x;

    /// <summary>The voxel at an index, in the brick (0 to 15 each).</summary>
    public static (int X, int Y, int Z) Place(int index) => (index & (Size - 1), index >> 8, (index >> 4) & (Size - 1));

    /// <summary>The brick coordinate a voxel coordinate (world voxels along an axis) falls in.</summary>
    public static int Of(int voxel) => voxel >> 4;

    /// <summary>A voxel coordinate's place in its brick (0 to 15).</summary>
    public static int In(int voxel) => voxel & (Size - 1);
}
