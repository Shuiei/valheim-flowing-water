using System;
using System.Collections.Generic;
using System.Linq;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// The water drawn as voxels where it meets the air: a pool closed all round shows no side, water open on a side shows
/// that side from its floor to its level, over the water beside it only, and a fall is a narrow column through every
/// voxel it falls through, capped at both ends.
/// </summary>
public class VoxelShellTests
{
    private const int East = 1, West = 2, Up = 4, Down = 8, North = 16;

    [Fact]
    public void APoolClosedAllRoundShowsNoSide()
    {
        var view = new BrickView(0, 0, 0, 1f);
        foreach ((int x, int y, int z) in Block(4, 5, 0, 1, 4, 5))
        {
            Wet(view, x, y, z, y == 1 ? 0.5f : 1f, y == 1 ? Up : 0);
        }

        Assert.Equal(0, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Fact]
    public void WaterOpenToTheAirOnASideShowsThatSideFromItsFloorToItsLevel()
    {
        // A column 1.5 deep, open east: its full voxel's side up to 1, the half one's from 1 to 1.5.
        var view = new BrickView(0, 0, 0, 1f);
        Wet(view, 5, 0, 5, 1f, East);
        Wet(view, 5, 1, 5, 0.5f, East | Up);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal(2, shell.Faces);
        var corners = Corners(shell).ToList();
        Assert.All(corners, c => Assert.Equal(6f - 8f, c.X, 3));
        Assert.All(corners, c => Assert.Equal((1f, 0f, 0f), c.Normal));
        Assert.Equal([0f, 1f, 1.5f], corners.Select(c => c.Y + 8f).Distinct().OrderBy(y => y).Select(y => (float)Math.Round(y, 3)));
    }

    [Fact]
    public void ASideUnderAFilmGoesUpToTheWaterLaidFlatOnly()
    {
        // 0.3 of water on ground 0.2 into its voxel, open east, a film of 0.05 over it: one side, from the ground up to
        // 0.55, as the surface stands.
        var view = new BrickView(0, 0, 0, 1f);
        view.Solid[Brick.Index(5, 0, 5)] = 0.2f;
        Wet(view, 5, 0, 5, 0.3f, East);
        Wet(view, 5, 1, 5, 0.05f, East | Up);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal(1, shell.Faces);
        Assert.Equal([0.2f, 0.55f], Heights(shell));
    }

    [Fact]
    public void AShellIsReadAgainWhenTheBrickUnderTheColumnBesideChanges()
    {
        // A side looks down the column beside, into the brick under it there: that brick's readers hold this one.
        Assert.Contains((1, 1, 0), VoxelShell.Readers);
        Assert.Contains((0, 1, -1), VoxelShell.Readers);
        Assert.Contains((0, -1, 0), VoxelShell.Readers);
    }

    [Fact]
    public void AShellIsReadAgainWhenTheBrickAcrossItsCornerChanges()
    {
        // A fall joined to the one beside asks whether the surface takes that one (DrawnFall, SurfaceTakes), which looks
        // at its neighbours along the ground: at a brick's corner, one of them is in the brick across the corner.
        Assert.Contains((1, 0, 1), VoxelShell.Readers);
        Assert.Contains((1, 0, -1), VoxelShell.Readers);
        Assert.Contains((-1, 0, 1), VoxelShell.Readers);
        Assert.Contains((-1, 0, -1), VoxelShell.Readers);
        Assert.Equal(VoxelShell.Readers.Count, VoxelShell.Readers.Distinct().Count());
    }

    [Fact]
    public void WaterOverTheColumnBesideInTheBrickUpThereChangesASideInABricksTopRow()
    {
        // Water in the top row of brick 0, open east toward lower water in brick (1, 0, 0): the surface joins them. With
        // water on a floor a row up that column, in brick (1, 1, 0), the surface joins that instead and the side shows,
        // so brick (1, 1, 0)'s readers hold brick 0.
        var here = new BrickView(0, 0, 0, 1f);
        var beside = new BrickView(1, 0, 0, 1f);
        var over = new BrickView(1, 1, 0, 1f);
        Wet(here, 15, 15, 5, 0.6f, East | Up);
        Wet(beside, 0, 15, 5, 0.2f, West | Up);
        Assert.Equal(0, VoxelShell.Build(Field(here, beside, over), 0, 0, 0).Faces);

        over.Solid[Brick.Index(0, 0, 5)] = 0.3f;
        Wet(over, 0, 0, 5, 0.3f, Up);
        Assert.Equal(1, VoxelShell.Build(Field(here, beside, over), 0, 0, 0).Faces);
        Assert.Contains((-1, -1, 0), VoxelShell.Readers);
    }

    [Fact]
    public void AFallsSprayOverTheColumnBesideKeepsNoSideBetweenTwoPoolColumns()
    {
        // Two columns of one pool, 0.5 into row 1, open toward each other; a fall's spray over the second, rows 3 and 4,
        // falling through the air: it is no water standing higher up that column, so the surface joins them.
        var view = new BrickView(0, 0, 0, 1f);
        foreach (int x in new[] { 5, 6 })
        {
            Wet(view, x, 0, 5, 1f, BrickData.AllOpen & ~Down);
            Wet(view, x, 1, 5, 0.5f, BrickData.AllOpen);
        }

        Wet(view, 6, 3, 5, 0.02f, BrickData.AllOpen);
        Wet(view, 6, 4, 5, 0.02f, BrickData.AllOpen);
        view.Water[Brick.Index(6, 1, 5)] = 0.45f;

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.DoesNotContain(Corners(shell), c => Math.Abs(c.X + 8f - 6f) < 1e-3f && c.Normal == (1f, 0f, 0f));
    }

    [Fact]
    public void WaterBesideWaterUnderOtherWaterShowsItsSideOnlyOverTheWaterBeside()
    {
        // Two voxels on a floor, open toward each other, the shallower one under water on a floor up its column (the
        // surface joins that): the deeper one shows its side above the shallower one's level.
        var view = new BrickView(0, 0, 0, 1f);
        Wet(view, 5, 0, 5, 0.8f, East | Up);
        Wet(view, 6, 0, 5, 0.3f, West | Up);
        Wet(view, 6, 3, 5, 0.3f, Up);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal(1, shell.Faces);
        Assert.Equal([0.3f, 0.8f], Heights(shell));
    }

    [Fact]
    public void WaterBesideWaterNearlyAsHighShowsNoSide()
    {
        // A sheet down a gentle slope: each voxel's water a little higher than the next one's, all joined by the surface.
        var view = new BrickView(0, 0, 0, 1f);
        Wet(view, 5, 0, 5, 0.4f, East | Up);
        Wet(view, 6, 0, 5, 0.3f, West | East | Up);
        Wet(view, 7, 0, 5, 0.2f, West | Up);

        Assert.Equal(0, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Theory]
    [InlineData(0.5f, 0.45f)]
    [InlineData(0.5f, 0.14f)]
    [InlineData(0.8f, 0.3f)]
    [InlineData(0.9f, 0.05f)]
    public void WaterBesideWaterShowsNoSideHoweverLowerTheWaterBesideIs(float here, float beside)
    {
        // The surface runs from one column's top to the other's, however far apart in height: a side between them
        // would stand under it or through it.
        var view = new BrickView(0, 0, 0, 1f);
        Wet(view, 5, 0, 5, here, East | Up);
        Wet(view, 6, 0, 5, beside, West | Up);

        Assert.Equal(0, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Fact]
    public void ASheetDownASlopeShowsNoSideBetweenItsColumns()
    {
        // Ground 0.8 into row 0 under one column and 0.2 under the next: a sheet 0.3 deep over both crosses row 0 high in
        // the first (and goes on up into row 1) and low in the second. Their levels in row 0 differ by half a voxel,
        // the ground's step, not the water's: no side between them.
        var view = new BrickView(0, 0, 0, 1f);
        int a = Brick.Index(5, 0, 5), b = Brick.Index(6, 0, 5);
        view.Solid[a] = 0.8f;
        Wet(view, 5, 0, 5, 0.2f, East);
        Wet(view, 5, 1, 5, 0.1f, East | Up);
        view.Solid[b] = 0.2f;
        Wet(view, 6, 0, 5, 0.3f, West | Up);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.DoesNotContain(Corners(shell), c => c.Normal == (1f, 0f, 0f) && Math.Abs(c.X + 8f - 6f) < 1e-3f && c.Y + 8f < 1f);
    }

    [Fact]
    public void ASheetDownASlopeTwoVoxelsAVoxelShowsNoSideTowardTheColumnBelow()
    {
        // A sheet 0.3 deep on ground in row 2 here and in row 0 beside, open to the air between: the surface's slope
        // joins them, and a pool as high beside but 0.9 deep all the same; four rows lower, past the surface's reach,
        // the water beside is under a fall's edge, and the side shows.
        var view = new BrickView(0, 0, 0, 1f);
        view.Solid[Brick.Index(5, 2, 5)] = 0.5f;
        Wet(view, 5, 2, 5, 0.3f, East | Up);
        view.Solid[Brick.Index(6, 0, 5)] = 0.5f;
        Wet(view, 6, 0, 5, 0.3f, Up);
        Assert.Equal(0, VoxelShell.Build(Field(view), 0, 0, 0).Faces);

        view.Solid[Brick.Index(6, 0, 5)] = 0f;
        Wet(view, 6, 0, 5, 0.9f, Up);
        Assert.Equal(0, VoxelShell.Build(Field(view), 0, 0, 0).Faces);

        Wet(view, 6, 0, 5, 0f, 0);
        view.Solid[Brick.Index(5, 2, 5)] = 0f;
        Wet(view, 5, 2, 5, 0f, 0);
        view.Solid[Brick.Index(5, 4, 5)] = 0.5f;
        Wet(view, 5, 4, 5, 0.3f, East | Up);
        Wet(view, 6, 0, 5, 0.9f, Up);
        Assert.Equal(1, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Fact]
    public void ASheetDownADiagonalSlopeTwoVoxelsAColumnBothWaysShowsNoSide()
    {
        // Three columns by three, a sheet 0.3 deep on ground dropping two rows a column east and two north: each
        // column joins the ones below it as one sheet, the surface's (which draws a rise of two a column either way).
        Assert.Equal(0, VoxelShell.Build(Field(DiagonalSheet(VoxelSurface.MaxStep)), 0, 0, 0).Faces);
    }

    [Fact]
    public void ASheetDownRockSteeperThanTheSurfaceJoinsKeepsItsSides()
    {
        // The same sheet dropping a row more a column: past the surface's reach (it draws no water on rock so steep),
        // each column ends with its sides toward the ones below.
        Assert.True(VoxelShell.Build(Field(DiagonalSheet(VoxelSurface.MaxStep + 1)), 0, 0, 0).Faces > 0);
    }

    [Fact]
    public void WaterSteppingDownToWaterAVoxelLowerShowsNoSide()
    {
        // Thin water on a step, open east toward dry air over water a voxel lower: the surface runs down to it.
        BrickView view = StepDown(0.2f, East | Up);

        Assert.Equal(0, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Fact]
    public void WaterBesideWaterAVoxelLowerUnderSomeGroundKeepsItsSide()
    {
        // As above, but the dry voxel beside holds some ground (the server says so): the side shows over it.
        BrickView view = StepDown(0.9f, East | Up);
        view.GroundBeside[Brick.Index(5, 1, 5)] = East;

        Assert.Equal(1, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Fact]
    public void GroundBesideOneSideKeepsThatSideOnly()
    {
        // Open east and north toward water a voxel lower both ways, ground beside north only: the north side shows over
        // it, the surface joins the water east.
        BrickView view = StepDown(0.9f, East | North | Up);
        view.GroundBeside[Brick.Index(5, 1, 5)] = North;
        Wet(view, 5, 0, 6, 0.9f, Up);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal(1, shell.Faces);
        Assert.All(Corners(shell), c => Assert.Equal((0f, 0f, 1f), c.Normal));
    }

    [Fact]
    public void WaterBesideWaterAVoxelLowerUnderAFloorWithWaterOnItKeepsItsSide()
    {
        // As above, but the column beside holds water on a floor higher up too: the surface joins that, not the water a
        // voxel lower, so the side stays.
        BrickView view = StepDown(0.2f, East | Up);
        Wet(view, 6, 2, 5, 0.3f, Up);

        Assert.Equal(1, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Fact]
    public void WaterBesideWaterAVoxelLowerWithWaterHigherUpThatColumnKeepsItsSide()
    {
        // As above, the water on the floor three voxels up: the surface joins that all the same.
        BrickView view = StepDown(0.2f, East | Up);
        Wet(view, 6, 4, 5, 0.3f, Up);

        Assert.Equal(1, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Fact]
    public void WaterBesideWaterNearlyAsHighWithWaterHigherUpThatColumnKeepsItsSide()
    {
        // A sheet beside, a little lower, under a floor with water on it: the surface joins the floor's, so the side stays.
        var view = new BrickView(0, 0, 0, 1f);
        Wet(view, 5, 0, 5, 0.4f, East | Up);
        Wet(view, 6, 0, 5, 0.2f, West | Up);
        Wet(view, 6, 3, 5, 0.3f, Up);

        Assert.Equal(1, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Fact]
    public void AFallIsANarrowColumnThroughEveryVoxelCappedAtBothEnds()
    {
        // Four voxels of thin water over one another in the air, a tenth of a voxel each.
        BrickView view = Falls(0.09f, 4, 5, 6, 7);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal((4 * 4) + 2, shell.Faces);
        var corners = Corners(shell).ToList();
        // From 4 m to 8 m, measured from the brick's middle at 8 m.
        Assert.Equal(-4f, corners.Min(c => c.Y), 3);
        Assert.Equal(0f, corners.Max(c => c.Y), 3);
        Assert.Equal(0.15f, corners.Max(c => c.X) - 0.5f, 3);
        Assert.All(Enumerable.Range(0, shell.Water.Count / 2), k => Assert.Equal(1f, shell.Water[2 * k], 3));
    }

    [Fact]
    public void AFallOfUnevenWaterKeepsOneWidthDownItsLength()
    {
        // A fall whose voxels hold a tenth and half a tenth of a voxel by turns: as wide as the most of each voxel and
        // the ones over and under it, so one width all down (as wide as each voxel's own, it was a dashed line).
        var view = new BrickView(0, 0, 0, 1f);
        for (int y = 4; y < 8; y++)
        {
            Falling(view, y, y % 2 == 0 ? 0.09f : 0.04f);
        }

        var corners = ShellCorners(view);

        Assert.All(corners, c => Assert.Equal(0.15f, Math.Abs(c.X - 0.5f), 3));
    }

    [Fact]
    public void AVoxelAFallLeavesDryIsTakenInByTheVoxelOverItUncapped()
    {
        // Falling water at rows 7, 6, 4 and 3, row 5 left dry between: one column from 3 m to 8 m, capped at its ends only.
        BrickView view = Falls(0.09f, 3, 4, 6, 7);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal((4 * 4) + 2, shell.Faces);
        var corners = Corners(shell).ToList();
        Assert.Equal(-5f, corners.Min(c => c.Y), 3);
        Assert.Equal(0f, corners.Max(c => c.Y), 3);
        Assert.Contains(corners, c => Math.Abs(c.Y + 3f) < 1e-3f);
    }

    [Fact]
    public void AFallKeepsItsWidthAcrossAVoxelItLeavesDry()
    {
        // A tenth of a voxel at row 4 under the dry row 5, half a tenth over it: the column through the gap is as wide as
        // the water it meets past the gap (as wide as its own over it, it narrowed at the gap).
        var view = new BrickView(0, 0, 0, 1f);
        Falling(view, 7, 0.04f);
        Falling(view, 6, 0.04f);
        Falling(view, 4, 0.09f);
        Falling(view, 3, 0.04f);

        var corners = ShellCorners(view);

        // Where the column through the gap meets row 4's, at 5 m (3 m under the brick's middle at 8 m).
        var join = corners.Where(c => Math.Abs(c.Y + 3f) < 1e-3f).ToList();
        Assert.NotEmpty(join);
        Assert.All(join, c => Assert.Equal(0.15f, Math.Abs(c.X - 0.5f), 3));
    }

    [Fact]
    public void TwoFallsSideBySideAreOneSheet()
    {
        // Columns x 8 and 9 falling, rows 4 to 7: each reaches the face they share and shows no side there, so the two
        // are one sheet 1.3 voxels wide (two poles 0.3 wide, side by side, they were).
        BrickView view = TwoFallsSideBySide(BrickData.AllOpen);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);
        var corners = Corners(shell).ToList();

        Assert.Equal((4 * 2 * 3) + 4, shell.Faces);
        Assert.DoesNotContain(corners, c => c.Normal == (1f, 0f, 0f) && c.X < 1.5f);
        Assert.DoesNotContain(corners, c => c.Normal == (-1f, 0f, 0f) && c.X > 0.5f);
        Assert.Equal(0.35f, corners.Min(c => c.X), 3);
        Assert.Equal(1.65f, corners.Max(c => c.X), 3);
        Assert.Contains(corners, c => c.Normal == (0f, 0f, 1f) && Math.Abs(c.X - 1f) < 1e-3f);
    }

    [Fact]
    public void FallsSideBySideStayApartWhereAPieceClosesTheFaceBetweenThem()
    {
        // Columns x 8 and 9 falling, the face between them closed from x 9's side (a piece there): two columns, each
        // with its own side toward the other (as one sheet, the sheet ran through the piece and left the face bare).
        BrickView view = TwoFallsSideBySide(BrickData.AllOpen & ~West);

        var corners = ShellCorners(view);

        Assert.Contains(corners, c => c.Normal == (1f, 0f, 0f) && Math.Abs(c.X - 0.65f) < 1e-3f);
        Assert.DoesNotContain(corners, c => c.Normal == (0f, 0f, 1f) && Math.Abs(c.X - 1f) < 1e-3f);
    }

    [Fact]
    public void ACapsReachIsItsSidesReach()
    {
        // Two falls side by side, rows 4 to 7, the top one capped over and the bottom one under: each cap spans the
        // column's sides, out to the face the columns share (cut at the column's own width, the sheet's caps left a gap).
        BrickView view = TwoFallsSideBySide(BrickData.AllOpen);

        var corners = ShellCorners(view);
        var sides = corners.Where(c => c.Normal == (0f, 0f, 1f)).ToList();

        foreach ((float, float, float) cap in new[] { (0f, 1f, 0f), (0f, -1f, 0f) })
        {
            var caps = corners.Where(c => c.Normal == cap).ToList();
            Assert.Equal(sides.Min(c => c.X), caps.Min(c => c.X), 3);
            Assert.Equal(sides.Max(c => c.X), caps.Max(c => c.X), 3);
            Assert.Contains(caps, c => Math.Abs(c.X - 1f) < 1e-3f);
        }
    }

    [Fact]
    public void AStripOfThinFallsDownAFaceIsOneCurtain()
    {
        // Five columns side by side, each a twentieth of a voxel falling down rows 2 to 9: one curtain 5 voxels wide, its
        // two ends' sides and its front and back only (a row of five poles, it was).
        var view = new BrickView(0, 0, 0, 1f);
        for (int x = 6; x <= 10; x++)
        {
            for (int y = 2; y < 10; y++)
            {
                Falling(view, x, y, 0.05f);
            }
        }

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal((8 * ((5 * 2) + 2)) + (2 * 5), shell.Faces);
    }

    [Fact]
    public void AFallSteppingAsideDownAFaceMeetsAtItsSteps()
    {
        // Falling water at (8, 7) and (8, 6), then a voxel east at (9, 5) and (9, 4): each part reaches the face between
        // the columns where the fall steps aside, so they meet there (two boxes 0.7 m apart, it was).
        var view = new BrickView(0, 0, 0, 1f);
        Falling(view, 8, 7, 0.09f);
        Falling(view, 8, 6, 0.09f);
        Falling(view, 9, 5, 0.09f);
        Falling(view, 9, 4, 0.09f);

        var corners = ShellCorners(view);

        // Both reach x 9 m (1 m east of the brick's middle) at the step: the upper part's east and the lower part's west.
        // (Each part's own corners only: the upper part's over the step at -2 m, the lower part's under it.)
        Assert.Contains(corners, c => c.Normal == (0f, 0f, 1f) && Math.Abs(c.X - 1f) < 1e-3f && c.Y > -1.999f);
        Assert.Contains(corners, c => c.Normal == (0f, 0f, 1f) && Math.Abs(c.X - 1f) < 1e-3f && c.Y < -2.001f);
    }

    [Fact]
    public void AFallRunsThroughTwoVoxelsItLeavesDry()
    {
        // Falling water at rows 7, 6, 3 and 2, rows 5 and 4 dry between (falling water fills every other voxel): one
        // column from 2 m to 8 m, capped at its ends only.
        BrickView view = Falls(0.09f, 2, 3, 6, 7);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);
        var corners = Corners(shell).ToList();

        Assert.Equal((4 * 4) + 2, shell.Faces);
        Assert.Equal(-6f, corners.Min(c => c.Y), 3);
        Assert.Equal(0f, corners.Max(c => c.Y), 3);
    }

    [Fact]
    public void AFallRunsThroughWaterTooThinToDraw()
    {
        // Row 5 holds falling water too thin to draw between rows 7, 6 and 4, 3: one column from 3 m to 8 m.
        BrickView view = Falls(0.09f, 3, 4, 6, 7);

        Falling(view, 5, 0.005f);
        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal((4 * 4) + 2, shell.Faces);
        Assert.Equal(-5f, Corners(shell).Min(c => c.Y), 3);
    }

    [Fact]
    public void AFallIsNotTakenThroughAGapToWaterTooThinToDraw()
    {
        // Under the dry row 5 only a film at row 4, too thin to draw: the fall from rows 7 and 6 ends at 6 m, capped.
        var view = new BrickView(0, 0, 0, 1f);
        Falling(view, 7, 0.09f);
        Falling(view, 6, 0.09f);
        Falling(view, 4, 0.005f);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal((2 * 4) + 2, shell.Faces);
        Assert.Equal(-2f, Corners(shell).Min(c => c.Y), 3);
    }

    [Fact]
    public void AFallIsNotTakenThroughADryVoxelHoldingGround()
    {
        // Row 5 dry, the water over it saying it holds ground (a ledge's lip, its ground not sent): two falls, each
        // capped at both ends, none drawn into the rock.
        var view = new BrickView(0, 0, 0, 1f);
        Falling(view, 7, 0.09f);
        Falling(view, 6, 0.09f, BrickData.AllOpen | BrickData.GroundUnder);
        Falling(view, 4, 0.09f);
        Falling(view, 3, 0.09f);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal((4 * 4) + 4, shell.Faces);
        Assert.DoesNotContain(Corners(shell), c => c.Y is < -2.001f and > -2.999f);
    }

    [Fact]
    public void WaterRunningDownASteepSlopeVoxelToVoxelIsNoFall()
    {
        // Thin water hanging over voxels that hold ground (as the server says: the ground under dry voxels is not
        // sent), a voxel lower each step: the surface draws it, no columns.
        var view = new BrickView(0, 0, 0, 1f);
        for (int x = 4; x < 8; x++)
        {
            Wet(view, x, 12 - x, 8, 0.1f, BrickData.AllOpen | BrickData.GroundUnder);
        }

        Assert.Equal(0, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Fact]
    public void WaterOnGroundOverRockTooSteepForTheSurfaceIsAFall()
    {
        // Water on ground over a dry voxel (the server says so), no water falling into it, open east toward a column
        // whose water is four rows lower: past the surface's reach, so the surface does not draw it, and the shell does
        // (a step of a sheet down a slope, it was left to the surface, and drawn by nothing).
        var view = new BrickView(0, 0, 0, 1f);
        Wet(view, 5, 5, 5, 0.3f, BrickData.AllOpen | BrickData.GroundUnder);
        view.VelocityY[Brick.Index(5, 5, 5)] = -2f;
        view.Solid[Brick.Index(6, 0, 5)] = 0.5f;
        Wet(view, 6, 1, 5, 0.3f, Up);

        Assert.True(Corners(VoxelShell.Build(Field(view), 0, 0, 0)).Any(c => c.Y > -3.001f), "no shell for the water at row 5");
    }

    [Fact]
    public void ASheetCrossingARowOverGroundWithWaterOnItIsNoFall()
    {
        // A sheet down a slope crossing from row 0 into row 1: ground 0.6 and water 0.3 in row 0, the sheet's top 0.2
        // in row 1 over it. The water in row 1 stands on the water on the ground under it: no fall's column.
        var view = new BrickView(0, 0, 0, 1f);
        view.Solid[Brick.Index(5, 0, 5)] = 0.6f;
        Wet(view, 5, 0, 5, 0.3f, 0);
        Wet(view, 5, 1, 5, 0.2f, Up | Down);

        Assert.Equal(0, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Fact]
    public void WaterFallingDownACliffFaceOverGroundIsStillAFallDownToTheGround()
    {
        // A stack of thin falling water against a cliff whose face cuts into every voxel under it: a fall all the same,
        // its foot (over the ground, the voxel under it dry) too.
        var view = new BrickView(0, 0, 0, 1f);
        for (int y = 4; y < 8; y++)
        {
            Wet(view, 8, y, 8, 0.09f, BrickData.AllOpen | BrickData.GroundUnder);
        }

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal((4 * 4) + 2, shell.Faces);
        Assert.Equal(-4f, Corners(shell).Min(c => c.Y), 3);
    }

    [Fact]
    public void AFallAgainstAWallShowsNoSideOrCapOnTheFacesTheWallCloses()
    {
        // Two voxels of falling water with a wall on their east faces, the upper one's top closed by a floor over it.
        var view = new BrickView(0, 0, 0, 1f);
        int allButEast = BrickData.AllOpen & ~East;
        Wet(view, 8, 4, 8, 0.09f, allButEast);
        Wet(view, 8, 5, 8, 0.09f, allButEast & ~Up);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        // Three sides each, the bottom cap, no top cap, nothing facing east.
        Assert.Equal((2 * 3) + 1, shell.Faces);
        Assert.DoesNotContain(Corners(shell), c => c.Normal == (1f, 0f, 0f));
        Assert.DoesNotContain(Corners(shell), c => c.Normal == (0f, 1f, 0f));
    }

    [Fact]
    public void AFilmTooThinToDrawShowsNothing()
    {
        var view = new BrickView(0, 0, 0, 1f);
        Wet(view, 3, 0, 3, 0.005f, BrickData.AllOpen & ~Down);

        Assert.Equal(0, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    [Fact]
    public void WaterKeptInsideASolidVoxelShowsNothing()
    {
        // A piece built over a pool with nowhere for its water to go: the server keeps the water in the solid voxel,
        // and drawing it would show water inside the piece.
        var view = new BrickView(0, 0, 0, 1f);
        Wet(view, 3, 0, 3, 0.5f, BrickData.AllOpen);
        view.Solid[Brick.Index(3, 0, 3)] = 1f;

        Assert.Equal(0, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    private static void Wet(BrickView view, int x, int y, int z, float water, int open)
    {
        int i = Brick.Index(x, y, z);
        view.Water[i] = water;
        view.Open[i] = (byte)open;
    }

    // Three columns by three, a sheet 0.3 deep on ground dropping this many rows a column east and as many north.
    private static BrickView DiagonalSheet(int drop)
    {
        var view = new BrickView(0, 0, 0, 1f);
        for (int z = 5; z <= 7; z++)
        {
            for (int x = 5; x <= 7; x++)
            {
                int row = (drop * (7 - x)) + (drop * (7 - z));
                view.Solid[Brick.Index(x, row, z)] = 0.5f;
                Wet(view, x, row, z, 0.3f, Up | (x < 7 ? East : 0) | (z < 7 ? North : 0));
            }
        }

        return view;
    }

    // Water on a step a voxel up (row 1 over a solid voxel), so much of it, open so, and water 0.9 deep beside it east
    // in row 0.
    private static BrickView StepDown(float water, int open)
    {
        var view = new BrickView(0, 0, 0, 1f);
        Wet(view, 5, 1, 5, water, open);
        view.Solid[Brick.Index(5, 0, 5)] = 1f;
        Wet(view, 6, 0, 5, 0.9f, Up);
        return view;
    }

    // A fall in column (8, 8), so much water falling in each of these rows.
    private static BrickView Falls(float water, params int[] rows)
    {
        var view = new BrickView(0, 0, 0, 1f);
        foreach (int y in rows)
        {
            Falling(view, y, water);
        }

        return view;
    }

    // Two falls side by side, columns x 8 and 9, rows 4 to 7, x 9's faces open so.
    private static BrickView TwoFallsSideBySide(int eastOpen)
    {
        var view = new BrickView(0, 0, 0, 1f);
        for (int y = 4; y < 8; y++)
        {
            Falling(view, 8, y, 0.09f);
            Falling(view, 9, y, 0.09f, eastOpen);
        }

        return view;
    }

    // The corners of a brick's shell, from its view alone.
    private static List<(float X, float Y, float Z, (float, float, float) Normal)> ShellCorners(BrickView view) =>
        [.. Corners(VoxelShell.Build(Field(view), 0, 0, 0))];

    // The heights of a shell's corners over the brick's floor, each once, lowest first.
    private static IEnumerable<float> Heights(VoxelShell shell) =>
        Corners(shell).Select(c => (float)Math.Round(c.Y + 8f, 3)).Distinct().OrderBy(y => y);

    // Water falling at 4 m/s in voxel (8, y, 8).
    private static void Falling(BrickView view, int y, float water, int open = BrickData.AllOpen) => Falling(view, 8, y, water, open);

    // Water falling at 4 m/s in voxel (x, y, 8).
    private static void Falling(BrickView view, int x, int y, float water, int open = BrickData.AllOpen)
    {
        Wet(view, x, y, 8, water, open);
        view.VelocityY[Brick.Index(x, y, 8)] = -4f;
    }

    private static IEnumerable<(int X, int Y, int Z)> Block(int x0, int x1, int y0, int y1, int z0, int z1) =>
        from y in Enumerable.Range(y0, y1 - y0 + 1)
        from z in Enumerable.Range(z0, z1 - z0 + 1)
        from x in Enumerable.Range(x0, x1 - x0 + 1)
        select (x, y, z);

    private static IEnumerable<(float X, float Y, float Z, (float, float, float) Normal)> Corners(VoxelShell shell) =>
        Enumerable.Range(0, shell.Positions.Count / 3).Select(k => (
            shell.Positions[3 * k], shell.Positions[(3 * k) + 1], shell.Positions[(3 * k) + 2],
            (shell.Normals[3 * k], shell.Normals[(3 * k) + 1], shell.Normals[(3 * k) + 2])));

    private static VoxelField Field(params BrickView[] views)
    {
        var field = new VoxelField();
        foreach (BrickView view in views)
        {
            field.Set(view);
        }

        return field;
    }
}
