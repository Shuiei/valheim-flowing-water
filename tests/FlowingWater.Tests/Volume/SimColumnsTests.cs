using System;
using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// The server's water as the card's water near the camera takes it: a pool resting on the ground is drawn by the card
/// once the ground under it is known, water falling through the air and a pool under another are left to the bricks,
/// faces closed by pieces are kept, and the ground is cast from over the water, or well over the water near it.
/// </summary>
public class SimColumnsTests
{
    private const int Columns = 16;

    private const int PerVoxel = 2;

    private const int OpenAll = 63, OpenDown = 8;

    [Fact]
    public void APoolOnTheGroundIsDrawnByTheCardOnceItsGroundIsKnown()
    {
        // Ground half way up row 0, water to half way up row 2.
        BrickView brick = Pool(3, 3, groundShare: 0.5f, top: 2.5f);

        float[] unknown = Build(brick, Bed(float.NaN), out _);
        float[] known = Build(brick, Bed(0.5f), out _);

        Assert.Equal(2.5f, Top(known, 3, 3), 3);
        Assert.Equal(2f, Depth(known, 3, 3), 3);
        Assert.Equal(0f, Drawn(unknown, 3, 3));
        Assert.Equal(1f, Drawn(known, 3, 3));
        Assert.Equal(SimColumns.DryTop, Top(known, 4, 3));
    }

    [Fact]
    public void TheEdgeOfASheetOnASlopeIsTheCardsThoughItsUphillGroundIsOverIt()
    {
        // Two centimetres over the ground at the column's downhill side, the slope rising 8 cm across it.
        var brick = new BrickView(0, 0, 0, 1f);
        Wet(brick, 3, 0, 3, 0.02f, solid: 0.5f, open: OpenAll);
        float[] bed = Bed(0.5f);
        for (int j = 0; j < PerVoxel; j++)
        {
            bed[(((3 * PerVoxel) + j) * Columns * PerVoxel) + (3 * PerVoxel) + 1] = 0.58f;
        }

        float[] texels = Build(brick, bed, out _);

        Assert.Equal(0.52f, Top(texels, 3, 3), 3);
        Assert.Equal(1f, Drawn(texels, 3, 3));
    }

    [Fact]
    public void WaterUnderARoofTheGroundWasCastFromIsLeftToTheBricks()
    {
        BrickView brick = Pool(3, 3, groundShare: 0.5f, top: 2.5f);

        float[] texels = Build(brick, Bed(2.8f), out _);

        Assert.Equal(2.5f, Top(texels, 3, 3), 3);
        Assert.Equal(0f, Drawn(texels, 3, 3));
    }

    [Fact]
    public void WaterFallingThroughTheAirHoldsNoWaterOnTheCard()
    {
        // Rows 4 to 6 half full, nothing under them (the lowest's down face is open), the ground 4 m under: a fall.
        var brick = new BrickView(0, 0, 0, 1f);
        for (int y = 4; y <= 6; y++)
        {
            Wet(brick, 3, y, 3, 0.5f, solid: 0f, open: OpenAll);
        }

        float[] texels = Build(brick, Bed(0f), out _);

        Assert.Equal(SimColumns.DryTop, Top(texels, 3, 3));
        Assert.Equal(0f, Drawn(texels, 3, 3));
    }

    [Fact]
    public void WaterFallingFromHighIsTheCardsFallWhenItDrawsFalls()
    {
        // Rows 4 to 6 half full over the air, the ground 4 m under: a fall from 6.5 m.
        var brick = new BrickView(0, 0, 0, 1f);
        for (int y = 4; y <= 6; y++)
        {
            Wet(brick, 3, y, 3, 0.5f, solid: 0f, open: OpenAll);
        }

        float[] texels = Falls(brick, Bed(0f), out float[] layers);

        Assert.Equal(SimColumns.DryTop, Top(texels, 3, 3));
        Assert.Equal(SimColumns.Sheet, Drawn(texels, 3, 3));
        Assert.Equal(6.5f, FallTop(layers, 3, 3), 3);
    }

    [Fact]
    public void WaterFallingHalfOverALedgeFallsByTheLowerGround()
    {
        // A sliver hanging in row 4, half the column's cells under it on a ledge's top at 3.9 m, half on the ground at 0.
        var brick = new BrickView(0, 0, 0, 1f);
        Wet(brick, 3, 4, 3, 0.2f, solid: 0f, open: OpenAll);
        float[] bed = Bed(0f);
        for (int j = 0; j < PerVoxel; j++)
        {
            bed[(((3 * PerVoxel) + j) * Columns * PerVoxel) + (3 * PerVoxel)] = 3.9f;
        }

        float[] texels = Falls(brick, bed, out float[] layers);

        Assert.Equal(SimColumns.Sheet, Drawn(texels, 3, 3));
        Assert.Equal(4.2f, FallTop(layers, 3, 3), 3);
    }

    [Fact]
    public void WaterFallingIntoAPoolIsTheCardsPoolUnderItsFallWhenItDrawsFalls()
    {
        // A pool to half way up row 1, water falling into it from rows 2 to 4 (not full): the pool's water kept, the
        // fall thrown from the top of row 4.
        BrickView brick = Pool(3, 3, groundShare: 0.5f, top: 1.5f);
        Wet(brick, 3, 2, 3, 0.3f, solid: 0f, open: OpenAll);
        Wet(brick, 3, 3, 3, 0.3f, solid: 0f, open: OpenAll);
        Wet(brick, 3, 4, 3, 0.3f, solid: 0f, open: OpenAll);

        float[] texels = Falls(brick, Bed(0.5f), out float[] layers);

        Assert.Equal(1.5f, Top(texels, 3, 3), 3);
        Assert.Equal(SimColumns.Sheet, Drawn(texels, 3, 3));
        Assert.Equal(1f, Depth(texels, 3, 3), 3);
        Assert.Equal(4.3f, FallTop(layers, 3, 3), 3);
        Assert.Equal(SimColumns.DryTop, layers[(((3 * Columns) + 3) * SimColumns.LayerTexel) + 1]);
    }

    [Fact]
    public void AFallFromHighKeepsThePoolItFallsIntoWhenItDrawsFalls()
    {
        // Rows 4 to 6 half full over the air, and under them, apart, a pool to 0.8 m on ground at 0.5 m.
        BrickView brick = Pool(3, 3, groundShare: 0.5f, top: 0.8f);
        for (int y = 4; y <= 6; y++)
        {
            Wet(brick, 3, y, 3, 0.5f, solid: 0f, open: OpenAll);
        }

        float[] texels = Falls(brick, Bed(0.5f), out float[] layers);

        Assert.Equal(0.8f, Top(texels, 3, 3), 3);
        Assert.Equal(SimColumns.Sheet, Drawn(texels, 3, 3));
        Assert.Equal(0.3f, Depth(texels, 3, 3), 3);
        Assert.Equal(6.5f, FallTop(layers, 3, 3), 3);
    }

    [Fact]
    public void WaterOnALedgeAndThePoolAtItsFootAreEachTheCardsAtTheirOwnLevels()
    {
        // A floor half way up row 3 holding water to 3.7 m, a trickle falling through row 1, a pool to 0.8 m on the
        // ground under it all; the floor's edge crosses the column: half its cells on the floor, half on the ground.
        var brick = new BrickView(0, 0, 0, 1f);
        Wet(brick, 3, 3, 3, 0.2f, solid: 0.5f, open: OpenAll);
        Wet(brick, 3, 1, 3, 0.05f, solid: 0f, open: OpenAll);
        Wet(brick, 3, 0, 3, 0.3f, solid: 0.5f, open: OpenAll);
        float[] bed = Bed(0.5f);
        for (int j = 0; j < PerVoxel; j++)
        {
            bed[(((3 * PerVoxel) + j) * Columns * PerVoxel) + (3 * PerVoxel)] = 3.5f;
        }

        float[] texels = Falls(brick, bed, out float[] layers);
        float[] bricks = Build(brick, bed, out _);

        Assert.Equal(3.7f, Top(texels, 3, 3), 3);
        Assert.Equal(SimColumns.Sheet, Drawn(texels, 3, 3));
        Assert.Equal(0.25f, Depth(texels, 3, 3), 3);
        Assert.Equal(0.8f, layers[((3 * Columns) + 3) * SimColumns.LayerTexel], 3);
        Assert.Equal(2.75f, layers[(((3 * Columns) + 3) * SimColumns.LayerTexel) + 1], 3);
        Assert.Equal(1.05f, FallTop(layers, 3, 3), 3);
        Assert.Equal(0f, Drawn(bricks, 3, 3));
    }

    [Fact]
    public void CellsOffALedgeTheWaterOnItCrossesAreNotUnderIt()
    {
        // Water to 3.7 m on a ledge half way up row 3 (as the server's voxels have it, all the column), but half the
        // column's cells cast on the ground at its foot, with no water there: none is kept for them.
        var brick = new BrickView(0, 0, 0, 1f);
        Wet(brick, 3, 3, 3, 0.2f, solid: 0.5f, open: OpenAll);
        float[] bed = Bed(0.5f);
        for (int j = 0; j < PerVoxel; j++)
        {
            bed[(((3 * PerVoxel) + j) * Columns * PerVoxel) + (3 * PerVoxel)] = 3.5f;
        }

        float[] texels = Falls(brick, bed, out float[] layers);

        Assert.Equal(3.7f, Top(texels, 3, 3), 3);
        Assert.Equal(SimColumns.Sheet, Drawn(texels, 3, 3));
        Assert.Equal(0.2f, Depth(texels, 3, 3), 3);
        Assert.Equal(SimColumns.DryTop, layers[((3 * Columns) + 3) * SimColumns.LayerTexel]);
        Assert.Equal(2.75f, layers[(((3 * Columns) + 3) * SimColumns.LayerTexel) + 1], 3);
    }

    [Fact]
    public void APoolUnderAnotherIsLeftToTheBricksAndTheCardKeepsTheHigher()
    {
        BrickView brick = Pool(3, 3, groundShare: 0.5f, top: 1.5f);

        // A second pool on a floor at row 5 (a piece closes the face under it).
        Wet(brick, 3, 5, 3, 0.4f, solid: 0f, open: OpenAll & ~OpenDown);

        float[] texels = Build(brick, Bed(0.5f), out _);

        Assert.Equal(5.4f, Top(texels, 3, 3), 3);
        Assert.Equal(0f, Drawn(texels, 3, 3));
    }

    [Fact]
    public void WaterRunningDownAStepIsDrawnByTheCardAsDeepAsItIsOnTheGroundUnderIt()
    {
        // A sliver of water in row 2 over the air, the ground cast under it at 1.6 m (a stream down a steep slope).
        var brick = new BrickView(0, 0, 0, 1f);
        Wet(brick, 3, 2, 3, 0.1f, solid: 0f, open: OpenAll);

        float[] texels = Build(brick, Bed(1.6f), out _);

        Assert.Equal(1.7f, Top(texels, 3, 3), 3);
        Assert.Equal(0.1f, Depth(texels, 3, 3), 3);
        Assert.Equal(1f, Drawn(texels, 3, 3));
    }

    [Fact]
    public void FoamOfWaterFallingIsKeptBesideTheClosedFaces()
    {
        // A sliver running down a step at 2 m/s down, its east face closed: half its foam (2 / 4) beside the bit.
        var brick = new BrickView(0, 0, 0, 1f);
        Wet(brick, 3, 2, 3, 0.1f, solid: 0f, open: OpenAll & ~1);
        brick.VelocityY[Brick.Index(3, 2, 3)] = -2f;

        float[] texels = Build(brick, Bed(1.6f), out _);

        Assert.Equal(1f, Closed(texels, 3, 3));
        Assert.Equal(1.25f, texels[(((3 * Columns) + 3) * SimColumns.Texel) + 3], 3);
    }

    [Fact]
    public void WaterSpillingIntoAPoolOverAStepIsDrawnByTheCardOnThePoolsFloor()
    {
        // A pool to half way up row 1, and a sheet spilling into it through row 2.
        BrickView brick = Pool(3, 3, groundShare: 0.5f, top: 1.5f);
        Wet(brick, 3, 2, 3, 0.2f, solid: 0f, open: OpenAll);

        float[] texels = Build(brick, Bed(0.5f), out _);

        Assert.Equal(1.7f, Top(texels, 3, 3), 3);
        Assert.Equal(1f, Drawn(texels, 3, 3));
    }

    [Fact]
    public void WaterUnderAWallOrRoofCastIsNoStep()
    {
        var brick = new BrickView(0, 0, 0, 1f);
        Wet(brick, 3, 2, 3, 0.1f, solid: 0f, open: OpenAll);

        float[] texels = Build(brick, Bed(10000f), out _);

        Assert.Equal(SimColumns.DryTop, Top(texels, 3, 3));
        Assert.Equal(0f, Drawn(texels, 3, 3));
    }

    [Fact]
    public void WaterFallingIntoAPoolIsLeftToTheBricksAtThePoolsLevel()
    {
        // A pool to half way up row 1, and water falling into it from rows 2 to 4 (not full): the card keeps the pool's
        // water only (a metre), not the falling water's.
        BrickView brick = Pool(3, 3, groundShare: 0.5f, top: 1.5f);
        Wet(brick, 3, 2, 3, 0.3f, solid: 0f, open: OpenAll);
        Wet(brick, 3, 3, 3, 0.3f, solid: 0f, open: OpenAll);
        Wet(brick, 3, 4, 3, 0.3f, solid: 0f, open: OpenAll);

        float[] texels = Build(brick, Bed(0.5f), out _);

        Assert.Equal(1.5f, Top(texels, 3, 3), 3);
        Assert.Equal(0f, Drawn(texels, 3, 3));
        Assert.Equal(1f, Depth(texels, 3, 3), 3);
    }

    [Fact]
    public void FacesPiecesCloseAtTheWatersLevelAreKeptFromEitherSide()
    {
        // A pool whose top voxel's east face is closed, and one beside it (east) whose west face is.
        BrickView brick = Pool(3, 3, groundShare: 0.5f, top: 1.5f, open: OpenAll & ~1);
        Wet(brick, 7, 0, 3, 1f, solid: 0.5f, open: OpenAll);
        Wet(brick, 7, 1, 3, 0.5f, solid: 0f, open: OpenAll & ~2);

        float[] texels = Build(brick, Bed(0.5f), out _);

        Assert.Equal(1f, Closed(texels, 3, 3));
        Assert.Equal(1f, Closed(texels, 6, 3));
        Assert.Equal(0f, Closed(texels, 7, 3));
    }

    [Fact]
    public void AFaceClosedOntoSolidGroundIsNoWallForTheCard()
    {
        // A pool's top voxel in row 1 whose east face is closed: the ground cast east of it rises over that voxel's top
        // (a slope's next step), so the face is the ground's, not a piece's.
        BrickView brick = Pool(3, 3, groundShare: 0.5f, top: 1.5f, open: OpenAll & ~1);
        float[] bed = Bed(0.5f);
        for (int j = 0; j < PerVoxel; j++)
        {
            for (int i = 0; i < PerVoxel; i++)
            {
                bed[(((3 * PerVoxel) + j) * Columns * PerVoxel) + (4 * PerVoxel) + i] = 2.1f;
            }
        }

        float[] texels = Build(brick, bed, out _);

        Assert.Equal(0f, Closed(texels, 3, 3));
    }

    [Fact]
    public void TheGroundIsCastFromOverTheWaterOrWellOverTheWaterNearIt()
    {
        BrickView brick = Pool(3, 3, groundShare: 0.5f, top: 2.5f);

        Build(brick, Bed(0.5f), out float[] cast);

        Assert.Equal(2.5f + SimColumns.CastOverWater, cast[(3 * Columns) + 3], 3);
        Assert.Equal(2.5f + SimColumns.CastOverNear, cast[(3 * Columns) + 9], 3);
    }

    [Fact]
    public void GroundFarFromAnyWaterIsNotCast()
    {
        // Voxels of a quarter metre: the reach is 48 voxels, more than the square.
        var brick = new BrickView(0, 0, 0, 0.25f);
        Wet(brick, 1, 0, 1, 0.5f, solid: 0.5f, open: OpenAll);
        float[] texels = SimColumns.Build([brick], 0, 0, 64, 0.25f, [.. Enumerable.Repeat(0f, 64 * 64)], 1, out float[] cast, out _);

        Assert.Equal(64 * 64 * SimColumns.Texel, texels.Length);
        Assert.False(float.IsNaN(cast[(10 * 64) + 10]));
        Assert.False(float.IsNaN(cast[(49 * 64) + 49]));
        Assert.True(float.IsNaN(cast[(50 * 64) + 50]));
    }

    [Fact]
    public void BricksOutsideTheSquareAndColumnsWithoutBricksAreDry()
    {
        BrickView far = Pool(3, 3, groundShare: 0.5f, top: 1.5f);
        var moved = new BrickView(5, 0, 5, 1f);
        Array.Copy(far.Water, moved.Water, Brick.Count);

        float[] texels = Build(moved, Bed(0f), out float[] cast);

        Assert.All(Enumerable.Range(0, Columns * Columns), k => Assert.Equal(SimColumns.DryTop, texels[k * SimColumns.Texel]));
        Assert.All(cast, c => Assert.True(float.IsNaN(c)));
    }

    // Water from the ground (filling the lowest voxel to groundShare) up to top (in voxels), in one column of a brick at
    // the origin, of metre voxels; its top voxel's faces open as given.
    private static BrickView Pool(int x, int z, float groundShare, float top, int open = OpenAll)
    {
        var brick = new BrickView(0, 0, 0, 1f);
        for (int y = 0; y < Math.Ceiling(top); y++)
        {
            float solid = y == 0 ? groundShare : 0f;
            float water = Math.Min(1f, top - y) - solid;
            Wet(brick, x, y, z, water, solid, y == (int)Math.Ceiling(top) - 1 ? open : OpenAll & ~OpenDown);
        }

        return brick;
    }

    private static void Wet(BrickView brick, int x, int y, int z, float water, float solid, int open)
    {
        int i = Brick.Index(x, y, z);
        brick.Water[i] = water;
        brick.Solid[i] = solid;
        brick.Open[i] = (byte)open;
    }

    private static float[] Bed(float height) => [.. Enumerable.Repeat(height, Columns * PerVoxel * Columns * PerVoxel)];

    private static float[] Build(BrickView brick, float[] bed, out float[] cast) => SimColumns.Build([brick], 0, 0, Columns, 1f, bed, PerVoxel, out cast, out _);

    // Built for a card that draws falls.
    private static float[] Falls(BrickView brick, float[] bed, out float[] layers) => SimColumns.Build([brick], 0, 0, Columns, 1f, bed, PerVoxel, out _, out layers, falls: true);

    private static float FallTop(float[] layers, int x, int z) => layers[(((z * Columns) + x) * SimColumns.LayerTexel) + 2];

    private static float Top(float[] texels, int x, int z) => texels[((z * Columns) + x) * SimColumns.Texel];

    private static float Drawn(float[] texels, int x, int z) => texels[(((z * Columns) + x) * SimColumns.Texel) + 1];

    private static float Depth(float[] texels, int x, int z) => texels[(((z * Columns) + x) * SimColumns.Texel) + 2];

    // The faces closed (the whole part; half the foam is the rest).
    private static float Closed(float[] texels, int x, int z) => (float)Math.Floor(texels[(((z * Columns) + x) * SimColumns.Texel) + 3]);
}
