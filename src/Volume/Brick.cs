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
    public float[] FlowX { get; } = new float[Count];

    /// <summary>Through its upper face (positive: going up).</summary>
    public float[] FlowY { get; } = new float[Count];

    /// <summary>Through its north face (positive: going north).</summary>
    public float[] FlowZ { get; } = new float[Count];

    /// <summary>Each voxel's head for the step being worked out: the level its water pushes to, metres.</summary>
    public float[] Head { get; } = new float[Count];

    /// <summary>The share of each voxel's outflow it can give this step (less than 1 when it has less water).</summary>
    public float[] Share { get; } = new float[Count];

    /// <summary>The bricks beside it, east, west, up, down, north and south (null where there is none).</summary>
    public Brick[] Beside { get; } = new Brick[6];

    /// <summary>Seconds it has held no water, to forget it.</summary>
    public float EmptyFor { get; set; }

    /// <summary>A voxel's index in the brick's arrays: rows along x, then z, then layers up.</summary>
    public static int Index(int x, int y, int z) => (y * Size * Size) + (z * Size) + x;

    /// <summary>The voxel at an index, in the brick (0 to 15 each).</summary>
    public static (int X, int Y, int Z) Place(int index) => (index % Size, index / (Size * Size), index / Size % Size);
}
