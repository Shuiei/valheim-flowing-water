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
    private const int East = 1, West = 2, Up = 4, Down = 8;

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
        Assert.Equal([0f, 1f, 1.5f], corners.Select(c => c.Y + 8f).Distinct().OrderBy(y => y).Select(y => (float)System.Math.Round(y, 3)));
    }

    [Fact]
    public void WaterBesideWaterShowsItsSideOnlyOverTheWaterBeside()
    {
        // Two voxels on a floor, open toward each other: the deeper one shows its side above the shallower one's level.
        var view = new BrickView(0, 0, 0, 1f);
        Wet(view, 5, 0, 5, 0.8f, East | Up);
        Wet(view, 6, 0, 5, 0.3f, West | Up);

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal(1, shell.Faces);
        Assert.Equal([0.3f, 0.8f], Corners(shell).Select(c => (float)System.Math.Round(c.Y + 8f, 3)).Distinct().OrderBy(y => y));
    }

    [Fact]
    public void AFallIsANarrowColumnThroughEveryVoxelCappedAtBothEnds()
    {
        // Four voxels of thin water over one another in the air, a tenth of a voxel each.
        var view = new BrickView(0, 0, 0, 1f);
        for (int y = 4; y < 8; y++)
        {
            Wet(view, 8, y, 8, 0.09f, BrickData.AllOpen);
            view.VelocityY[Brick.Index(8, y, 8)] = -4f;
        }

        var shell = VoxelShell.Build(Field(view), 0, 0, 0);

        Assert.Equal((4 * 4) + 2, shell.Faces);
        var corners = Corners(shell).ToList();
        Assert.Equal(4f - 8f, corners.Min(c => c.Y), 3);
        Assert.Equal(8f - 8f, corners.Max(c => c.Y), 3);
        Assert.Equal(0.15f, corners.Max(c => c.X) - 0.5f, 3);
        Assert.All(Enumerable.Range(0, shell.Water.Count / 2), k => Assert.Equal(1f, shell.Water[2 * k], 3));
    }

    [Fact]
    public void AFilmTooThinToDrawShowsNothing()
    {
        var view = new BrickView(0, 0, 0, 1f);
        Wet(view, 3, 0, 3, 0.005f, BrickData.AllOpen & ~Down);

        Assert.Equal(0, VoxelShell.Build(Field(view), 0, 0, 0).Faces);
    }

    private static void Wet(BrickView view, int x, int y, int z, float water, int open)
    {
        int i = Brick.Index(x, y, z);
        view.Water[i] = water;
        view.Open[i] = (byte)open;
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
