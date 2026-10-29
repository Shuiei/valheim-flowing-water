using System;
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
    public void EveryVoxelOfABrickReadsBackAsItsBytesKeepIt()
    {
        // A third of the voxels dry, the others with water on the byte's steps (n/200 of a voxel: nothing carried on),
        // a few pressed in past its reach, solids on their own steps (n/255) and flows of all speeds both ways.
        const float Size = 0.5f;
        var random = new Scrambled(52);
        var brick = new Brick(4, -1, 9);
        for (int i = 0; i < Brick.Count; i++)
        {
            brick.FlowX[i] = (float)((random.NextDouble() * 6.0) - 3.0);
            brick.FlowY[i] = (float)((random.NextDouble() * 6.0) - 3.0);
            brick.FlowZ[i] = (float)((random.NextDouble() * 6.0) - 3.0);
            if (random.Next(0, 3) == 0)
            {
                continue;
            }

            brick.Water[i] = i % 97 == 0 ? 1.3f + (i / 10000f) : random.Next(1, 255) / 200f;
            brick.Solid[i] = random.Next(0, 256) / 255f;
        }

        BrickView view = BrickData.Decode(BrickData.Encode(brick, Size));

        Assert.Equal((4, -1, 9, Size), (view.X, view.Y, view.Z, view.VoxelSize));
        for (int i = 0; i < Brick.Count; i++)
        {
            bool wet = brick.Water[i] > 0f;
            (float x, float y, float z) = wet ? BrickData.Velocity(brick, i, Size * Size) : (0f, 0f, 0f);
            Assert.Equal(brick.Water[i], view.Water[i]);
            Assert.Equal(brick.Solid[i], view.Solid[i]);
            Assert.InRange(view.VelocityX[i] - Kept(x), -1e-5f, 1e-5f);
            Assert.InRange(view.VelocityY[i] - Kept(y), -1e-5f, 1e-5f);
            Assert.InRange(view.VelocityZ[i] - Kept(z), -1e-5f, 1e-5f);
        }
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
    public void TheLookRoundsTheWaterAsItIsSent()
    {
        // A trace before a half-full voxel: what the trace rounds off is carried into the next voxel when sent, and the
        // look carries it too, so the half-full one's change (100 to 103 steps sent) is seen as it is sent.
        var before = new Brick(0, 0, 0);
        var after = new Brick(0, 0, 0);
        (before.Water[0], before.Water[1]) = (0.0026f, 0.502f);
        (after.Water[0], after.Water[1]) = (0.0024f, 0.512f);

        byte[] was = BrickData.Look(before, 1f);
        byte[] now = BrickData.Look(after, 1f);

        // A look holds a record for each voxel with water (the trace rounds to one step before, to none after).
        int wasAt = Record(was, 1), nowAt = Record(now, 1);
        Assert.Equal(2 * BrickData.LookBytes, was.Length);
        Assert.Equal(BrickData.LookBytes, now.Length);
        Assert.Equal(100, was[wasAt + BrickData.WaterByte]);
        Assert.Equal(103, now[nowAt + BrickData.WaterByte]);
        Assert.Equal(BrickData.Decode(BrickData.Encode(after, 1f)).Water[1], now[nowAt + BrickData.WaterByte] / 200f, 4);
        Assert.True(BrickData.Shows(was, now, 2, 3, BrickData.Steps(0.01f, 1f)));
    }

    [Fact]
    public void AThinFilmKeepsItsVolumeToWithinAStepAndWetsNoDryVoxel()
    {
        // Every other voxel holds a film of 0.002 to 0.012 of a voxel (spread evenly over that range), a few pressed in
        // past the byte's reach: alone, each rounded to 1/200, the film would lose or gain up to half a step a voxel,
        // and those under 0.0025 vanish.
        var brick = new Brick(0, 0, 0);
        double before = 0.0;
        for (int i = 0; i < Brick.Count; i += 2)
        {
            brick.Water[i] = i % 512 == 0 ? 1.4f : 0.002f + (0.01f * ((i / 2 * 37) % 100) / 100f);
            before += brick.Water[i];
        }

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 1f));

        double after = 0.0;
        for (int i = 0; i < Brick.Count; i++)
        {
            after += view.Water[i];
            if (i % 2 != 0)
            {
                Assert.Equal(0f, view.Water[i]);
            }
        }

        Assert.True(Math.Abs(after - before) <= 1.0 / 200.0, $"{before} before, {after} after");
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
    public void AVoxelsOpenFacesAreSentWithItsWater()
    {
        // Water in voxel (5, 5, 5) with a wall on its east face, the voxel west of it closing the face between them,
        // and solid ground under it: it could leave only up, north and south. One at the brick's east edge, with no
        // brick beside: every face open but its floor's.
        var brick = new Brick(0, 0, 0);
        int i = Brick.Index(5, 5, 5);
        brick.Water[i] = 0.5f;
        brick.Closed[i] = Brick.ClosedX;
        brick.Closed[Brick.Index(4, 5, 5)] = Brick.ClosedX;
        brick.Solid[Brick.Index(5, 4, 5)] = 1f;
        int edge = Brick.Index(15, 5, 9);
        brick.Water[edge] = 0.5f;
        brick.Solid[Brick.Index(15, 4, 9)] = 1f;

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 1f));

        Assert.Equal((1 << 2) | (1 << 4) | (1 << 5) | BrickData.GroundUnder, view.Open[i]);
        Assert.Equal((BrickData.AllOpen & ~(1 << 3)) | BrickData.GroundUnder, view.Open[edge]);

        // Its own east face closed by a piece, with still no brick beside it: closed.
        brick.Closed[edge] = Brick.ClosedX;
        Assert.Equal((BrickData.AllOpen & ~(1 << 3) & ~(1 << 0)) | BrickData.GroundUnder, BrickData.Decode(BrickData.Encode(brick, 1f)).Open[edge]);
        Assert.Equal(0, view.Open[Brick.Index(6, 5, 5)]);
    }

    [Fact]
    public void WaterOverAVoxelHoldingSomeGroundIsSentSo()
    {
        // Water hanging over a voxel a slope cuts through (some ground in it, its face open), and over one with none.
        var brick = new Brick(0, 0, 0);
        int onSlope = Brick.Index(5, 5, 5);
        brick.Water[onSlope] = 0.1f;
        brick.Solid[Brick.Index(5, 4, 5)] = 0.3f;
        int overAir = Brick.Index(9, 5, 5);
        brick.Water[overAir] = 0.1f;

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 1f));

        Assert.Equal(BrickData.AllOpen | BrickData.GroundUnder, view.Open[onSlope]);
        Assert.Equal(BrickData.AllOpen, view.Open[overAir]);
    }

    [Fact]
    public void GroundBesideASideTowardLowerWaterIsSentSo()
    {
        // Water open east toward a dry voxel holding some ground, with water under that voxel; and the same without the
        // ground.
        var brick = new Brick(0, 0, 0);
        int high = Brick.Index(5, 5, 5);
        brick.Water[high] = 0.9f;
        brick.Solid[Brick.Index(6, 5, 5)] = 0.4f;
        brick.Water[Brick.Index(6, 4, 5)] = 0.9f;
        int bare = Brick.Index(9, 5, 5);
        brick.Water[bare] = 0.9f;
        brick.Water[Brick.Index(10, 4, 5)] = 0.9f;

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 1f));

        Assert.Equal(1 << 0, view.GroundBeside[high]);
        Assert.Equal(0, view.GroundBeside[bare]);
        Assert.Equal(0, view.Open[high] & BrickData.GroundBeside);
    }

    [Fact]
    public void GroundBesideIsSentForTheSideItIsOnOnly()
    {
        // Water open east toward a dry voxel holding some ground over water, and north toward a bare dry voxel over
        // water: ground beside east only.
        var brick = new Brick(0, 0, 0);
        int high = Brick.Index(5, 5, 5);
        brick.Water[high] = 0.9f;
        brick.Solid[Brick.Index(6, 5, 5)] = 0.4f;
        brick.Water[Brick.Index(6, 4, 5)] = 0.9f;
        brick.Water[Brick.Index(5, 4, 6)] = 0.9f;

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 1f));

        Assert.Equal(1 << 0, view.GroundBeside[high]);
    }

    [Fact]
    public void ABrickSavedWithGroundBesideAnySideIsReadWithItOnEachOpenSide()
    {
        // Version 3 (a world saved then): one bit for any side, taken for each open side across; not kept as a face.
        byte open = (1 << 0) | (1 << 2) | (1 << 4) | BrickData.GroundUnder | BrickData.GroundBeside;
        byte[] stored = Packed.Write(writer =>
        {
            writer.Write((byte)3);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(1f);
            byte[] water = new byte[Brick.Count];
            water[7] = 100;
            writer.Write(water);
            // Its solid share, speeds and open faces.
            byte[] wet = [0, 0, 0, 0, open];
            writer.Write(wet);
        });

        BrickView view = BrickData.Decode(stored);

        Assert.Equal((1 << 0) | (1 << 2) | (1 << 4) | BrickData.GroundUnder, view.Open[7]);
        Assert.Equal((1 << 0) | (1 << 4), view.GroundBeside[7]);
    }

    [Fact]
    public void GroundBesideWithAFilmTooThinToBeSentIsSentSo()
    {
        // The voxel beside holds some ground and a film of water under a step of the bytes: dry to the players' games.
        var brick = new Brick(0, 0, 0);
        int high = Brick.Index(5, 5, 5);
        brick.Water[high] = 0.9f;
        brick.Solid[Brick.Index(6, 5, 5)] = 0.4f;
        brick.Water[Brick.Index(6, 5, 5)] = 0.002f;
        brick.Water[Brick.Index(6, 4, 5)] = 0.9f;

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 1f));

        Assert.Equal(1 << 0, view.GroundBeside[high]);
    }

    [Fact]
    public void ABrickFromBeforeTheOpenFacesIsReadWithEveryFaceOpen()
    {
        byte[] stored = Packed.Write(writer =>
        {
            writer.Write((byte)2);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(1f);
            byte[] water = new byte[Brick.Count];
            water[7] = 100;
            writer.Write(water);
            // Its solid share and speeds, and nothing after them.
            byte[] wet = [0, 0, 0, 0];
            writer.Write(wet);
        });

        BrickView view = BrickData.Decode(stored);

        Assert.Equal(0.5f, view.Water[7]);
        Assert.Equal(BrickData.AllOpen, view.Open[7]);
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

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.PositiveInfinity)]
    [InlineData(-2f)]
    public void WaterThatIsNotANumberOrBelowNothingIsRefused(float share)
    {
        // A brick whose first voxel is pressed past the byte's reach, its exact share then made bad.
        var brick = new Brick(0, 0, 0);
        brick.Water[0] = 1.5f;
        BrickView good = BrickData.Decode(BrickData.Encode(brick, 1f));
        Assert.Equal(1.5f, good.Water[0]);

        byte[] stored = Packed.Write(writer =>
        {
            writer.Write((byte)2);
            writer.Write(0);
            writer.Write(0);
            writer.Write(0);
            writer.Write(1f);
            byte[] water = new byte[Brick.Count];
            water[0] = 255;
            writer.Write(water);
            writer.Write(new byte[4]);
            writer.Write(share);
        });

        Assert.Throws<InvalidDataException>(() => BrickData.Decode(stored));
    }

    [Theory]
    [InlineData(float.NaN)]
    [InlineData(float.NegativeInfinity)]
    [InlineData(0f)]
    public void AVoxelSizeThatIsNotOneIsRefused(float size)
    {
        Assert.Throws<InvalidDataException>(() => BrickData.Decode(BrickData.Encode(new Brick(0, 0, 0), size)));
    }

    [Fact]
    public void WaterThatIsNotANumberIsKeptAsNoneWithoutSpoilingTheRest()
    {
        var brick = new Brick(0, 0, 0);
        brick.Water[0] = float.NaN;
        brick.Water[1] = float.PositiveInfinity;
        brick.Water[2] = 0.5f;

        BrickView view = BrickData.Decode(BrickData.Encode(brick, 1f));

        Assert.Equal(0f, view.Water[0]);
        Assert.Equal(0f, view.Water[1]);
        Assert.Equal(0.5f, view.Water[2]);
    }

    [Fact]
    public void BytesThatAreNotABrickAreRefused()
    {
        byte[] stored = BrickData.Encode(new Brick(0, 0, 0), 1f);

        Assert.ThrowsAny<InvalidDataException>(() => BrickData.Decode([1, 2, 3]));
        Assert.Throws<InvalidDataException>(() => BrickData.Decode(stored[..(stored.Length / 3)]));
    }

    // A speed as its byte keeps it: the nearest of 127 square-root steps to 16 m/s, either way.
    private static float Kept(float speed)
    {
        float step = (float)Math.Round((float)Math.Sqrt(Math.Min(Math.Abs(speed), BrickData.MaxSpeed) / BrickData.MaxSpeed) * 127f) / 127f;
        return Math.Sign(speed) * step * step * BrickData.MaxSpeed;
    }

    // Where a voxel's record starts in a look; fails the test when it has none.
    private static int Record(byte[] look, int voxel)
    {
        for (int k = 0; k < look.Length; k += BrickData.LookBytes)
        {
            if (BrickData.Voxel(look, k) == voxel)
            {
                return k;
            }
        }

        Assert.Fail($"voxel {voxel} has no record in the look");
        return -1;
    }
}
