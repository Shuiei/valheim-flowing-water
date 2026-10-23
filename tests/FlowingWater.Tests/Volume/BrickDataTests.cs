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
        brick.Solid[i] = 0.3f;
        brick.Water[Brick.Index(0, 0, 0)] = 1.1f;
        brick.Solid[Brick.Index(1, 0, 0)] = 1f;
        brick.FlowX[i] = 2f;
        brick.FlowX[i - 1] = 2f;
        brick.FlowY[i] = -1f;
        brick.FlowY[i - (Brick.Size * Brick.Size)] = -1f;

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 0.5f));

        Assert.Equal((-3, 2, 7, 0.5f), (view.X, view.Y, view.Z, view.VoxelSize));
        Assert.Equal(0.5f, view.Water[i], 2);
        Assert.Equal(1.1f, view.Water[Brick.Index(0, 0, 0)], 2);
        Assert.Equal(0f, view.Water[Brick.Index(1, 0, 0)]);

        // The water's floor where there is water; nothing kept where there is none.
        Assert.Equal(0.3f, view.Solid[i], 2);
        Assert.Equal(0f, view.Solid[Brick.Index(1, 0, 0)]);

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
    public void ATraceOfWaterTooSmallToKeepDoesNotShiftTheVelocitiesAfterIt()
    {
        var brick = new Brick(0, 0, 0);
        brick.Water[0] = 0.001f;
        brick.FlowX[0] = 5f;
        brick.Water[1] = 0.8f;
        brick.FlowX[1] = 2f;
        brick.FlowX[0] = 2f;

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 1f));

        Assert.Equal(0f, view.Water[0]);
        Assert.Equal(0f, view.VelocityX[0]);
        Assert.Equal(0.8f, view.Water[1], 2);
        Assert.Equal(2f, view.VelocityX[1], 1);
    }

    [Fact]
    public void AThinFilmKeepsItsVolumeToWithinAStepAndWetsNoDryVoxel()
    {
        // Every other voxel holds a film of 0.002 to 0.012 of a voxel, a few pressed in past the byte's reach: alone,
        // each rounded to 1/200, the film would lose or gain up to half a step a voxel, and those under 0.0025 vanish.
        var brick = new Brick(0, 0, 0);
        var random = new System.Random(7);
        double before = 0.0;
        for (int i = 0; i < Brick.Count; i += 2)
        {
            brick.Water[i] = i % 512 == 0 ? 1.4f : 0.002f + (0.01f * (float)random.NextDouble());
            before += brick.Water[i];
        }

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 1f));

        double after = 0.0;
        for (int i = 0; i < Brick.Count; i++)
        {
            after += view.Water[i];
            if (i % 2 == 1)
            {
                Assert.Equal(0f, view.Water[i]);
            }
        }

        Assert.True(System.Math.Abs(after - before) <= 1.0 / 200.0, $"{before} before, {after} after");
    }

    [Fact]
    public void WaterPressedPastTheBytesReachIsKeptExactlyAndSpeedsUpTo16()
    {
        // Eight times a voxel (a full voxel poured into one of half its side) and just past the byte's reach.
        var brick = new Brick(0, 0, 0);
        brick.Water[0] = 8f;
        brick.Water[1] = 1.3f;
        brick.Water[2] = 1.2f;
        brick.FlowX[0] = 100f;

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 1f));

        Assert.Equal(8f, view.Water[0]);
        Assert.Equal(1.3f, view.Water[1]);
        Assert.Equal(1.2f, view.Water[2], 3);
        Assert.Equal(BrickData.MaxSpeed, view.VelocityX[0], 1);
    }

    [Fact]
    public void AFirstVersionBrickIsStillRead()
    {
        // Version 1: no exact share after a water byte of 255 (it is 1.275 of a voxel).
        byte[] stored = Packed.Write(writer =>
        {
            writer.Write((byte)1);
            writer.Write(2);
            writer.Write(-3);
            writer.Write(4);
            writer.Write(0.5f);
            byte[] water = new byte[Brick.Count];
            water[0] = 255;
            water[1] = 100;
            writer.Write(water);

            // Each wet voxel's solid share and speeds: the first none, the second a fifth solid.
            byte[] wet = [0, 0, 0, 0, 51, 0, 0, 0];
            writer.Write(wet);
        });

        BrickView view = BrickData.Decode(stored);

        Assert.Equal((2, -3, 4, 0.5f), (view.X, view.Y, view.Z, view.VoxelSize));
        Assert.Equal(1.275f, view.Water[0], 3);
        Assert.Equal(0.5f, view.Water[1], 3);
        Assert.Equal(0.2f, view.Solid[1], 3);
    }

    [Fact]
    public void BytesThatAreNotABrickAreRefused()
    {
        byte[] stored = BrickData.Encode(new Brick(0, 0, 0), 1f);

        Assert.ThrowsAny<InvalidDataException>(() => BrickData.Decode([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => BrickData.Decode(stored[..(stored.Length / 3)]));
    }
}
