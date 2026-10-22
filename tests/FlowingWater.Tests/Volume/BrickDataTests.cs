using System.IO;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>A brick's water as kept and sent: read back as written, its velocities from the flows, bad bytes refused.</summary>
public class BrickDataTests
{
    [Fact]
    public void WaterAndVelocitiesReadBackAsWritten()
    {
        var brick = new Brick(-3, 2, 7);
        int i = Brick.Index(5, 6, 7);
        brick.Water[i] = 0.5f;
        brick.Water[Brick.Index(0, 0, 0)] = 1.1f;
        brick.FlowX[i] = 2f;
        brick.FlowX[i - 1] = 2f;
        brick.FlowY[i] = -1f;
        brick.FlowY[i - (Brick.Size * Brick.Size)] = -1f;

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 0.5f));

        Assert.Equal((-3, 2, 7, 0.5f), (view.X, view.Y, view.Z, view.VoxelSize));
        Assert.Equal(0.5f, view.Water[i], 2);
        Assert.Equal(1.1f, view.Water[Brick.Index(0, 0, 0)], 2);
        Assert.Equal(0f, view.Water[Brick.Index(1, 0, 0)]);

        // 2 m³/s through both of a 0.5 m voxel's east and west faces: 8 m/s.
        Assert.Equal(8f, view.VelocityX[i], 0);
        Assert.Equal(-4f, view.VelocityY[i], 0);
        Assert.Equal(0f, view.VelocityZ[i]);
    }

    [Fact]
    public void AVelocityTakesTheFlowsOfTheBrickBesideAtItsEdge()
    {
        var brick = new Brick(0, 0, 0);
        var west = new Brick(-1, 0, 0);
        brick.Beside[1] = west;
        west.FlowX[Brick.Index(15, 3, 3)] = 1f;
        brick.FlowX[Brick.Index(0, 3, 3)] = 3f;

        (float x, _, _) = BrickData.Velocity(brick, Brick.Index(0, 3, 3), 1f);

        Assert.Equal(2f, x);
    }

    [Fact]
    public void WaterPressedInIsKeptUpToTheBytesReachAndSpeedsUpTo16()
    {
        var brick = new Brick(0, 0, 0);
        brick.Water[0] = 3f;
        brick.FlowX[0] = 100f;

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 1f));

        Assert.Equal(255f / 200f, view.Water[0], 3);
        Assert.Equal(BrickData.MaxSpeed, view.VelocityX[0], 1);
    }

    [Fact]
    public void BytesThatAreNotABrickAreRefused()
    {
        byte[] stored = BrickData.Encode(new Brick(0, 0, 0), 1f);

        Assert.ThrowsAny<InvalidDataException>(() => BrickData.Decode([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => BrickData.Decode(stored[..(stored.Length / 3)]));
    }
}
