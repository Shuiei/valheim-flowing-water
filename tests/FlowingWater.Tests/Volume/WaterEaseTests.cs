using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests.Volume;

/// <summary>
/// The water a player's game draws between two of the server's changes: halfway through the ease, halfway between
/// them, never water where neither had any; and water too thin to draw left out, a voxel drawn kept until its water
/// is clearly under that, so a thin sheet does not flicker.
/// </summary>
public class WaterEaseTests
{
    [Fact]
    public void TheEaseGoesFromNothingToAllOverItsSeconds()
    {
        Assert.Equal(0f, WaterEase.Share(-1f));
        Assert.Equal(0f, WaterEase.Share(0f));
        Assert.Equal(0.5f, WaterEase.Share(WaterEase.Seconds / 2f), 4);
        Assert.Equal(1f, WaterEase.Share(WaterEase.Seconds));
        Assert.Equal(1f, WaterEase.Share(10f));
    }

    [Fact]
    public void HalfwayThroughTheEaseTheLevelsAreHalfway()
    {
        // A voxel's water rising from 0.2 to 0.6 on a floor 0.3 into it, its current turning from east to north.
        BrickView from = View(1f, (5, 5, 5, 0.2f, 0.3f));
        from.VelocityX[Brick.Index(5, 5, 5)] = 2f;
        BrickView to = View(1f, (5, 5, 5, 0.6f, 0.3f));
        to.VelocityZ[Brick.Index(5, 5, 5)] = 2f;

        BrickView half = WaterEase.Blend(from, to, 0.5f);
        var field = new VoxelField();
        field.Set(half);

        int i = Brick.Index(5, 5, 5);
        Assert.Equal(0.4f, half.Water[i], 4);
        Assert.Equal((1f, 0f, 1f), (half.VelocityX[i], half.VelocityY[i], half.VelocityZ[i]));
        Assert.Equal(5.7f, field.Level(5, 5, 5), 4);

        // The ends are left as they were: the blend is a view of its own.
        Assert.Equal(0.2f, from.Water[i]);
        Assert.Equal(0.6f, to.Water[i]);
    }

    [Fact]
    public void TheBlendNeverMakesWaterWhereBothEndsHaveNone()
    {
        // Water here and there in each, at scrambled places and amounts, some voxels in both, some in neither.
        var scrambled = new Scrambled(7);
        BrickView from = View(1f);
        BrickView to = View(1f);
        for (int k = 0; k < 600; k++)
        {
            BrickView end = k % 2 == 0 ? from : to;
            int i = scrambled.Next(0, Brick.Count);
            end.Water[i] = (float)scrambled.NextDouble();
            end.Solid[i] = (float)scrambled.NextDouble();
        }

        foreach (float share in new[] { 0f, 0.1f, 0.5f, 0.9f })
        {
            BrickView blend = WaterEase.Blend(from, to, share);
            for (int i = 0; i < Brick.Count; i++)
            {
                if (from.Water[i] == 0f && to.Water[i] == 0f)
                {
                    Assert.Equal(0f, blend.Water[i]);
                }
                else
                {
                    Assert.InRange(blend.Water[i], System.Math.Min(from.Water[i], to.Water[i]), System.Math.Max(from.Water[i], to.Water[i]));
                }
            }
        }
    }

    [Fact]
    public void TheBlendAndWhatIsShownKeepEachVoxelsOpenFaces()
    {
        // The open faces are the newest where the newest has water, else those it had: never zeroed, as the sides and
        // falls are drawn from them.
        BrickView from = View(1f, (5, 5, 5, 0.4f, 0f), (6, 5, 5, 0.4f, 0f));
        from.Open[Brick.Index(5, 5, 5)] = 63;
        from.Open[Brick.Index(6, 5, 5)] = 9;
        BrickView to = View(1f, (5, 5, 5, 0.6f, 0f));
        to.Open[Brick.Index(5, 5, 5)] = 5;

        BrickView half = WaterEase.Blend(from, to, 0.5f);
        Assert.Equal(5, half.Open[Brick.Index(5, 5, 5)]);
        Assert.Equal(9, half.Open[Brick.Index(6, 5, 5)]);

        BrickView shown = WaterEase.Show(View(1f, (5, 5, 5, 1f, 0f), (5, 6, 5, 0.01f, 0f)), null);
        Assert.Equal(0, shown.Open[Brick.Index(5, 5, 5)]);
        BrickView open = View(1f, (5, 5, 5, 1f, 0f), (5, 6, 5, 0.01f, 0f));
        open.Open[Brick.Index(5, 5, 5)] = 63;
        Assert.Equal(63, WaterEase.Show(open, null).Open[Brick.Index(5, 5, 5)]);
    }

    [Fact]
    public void WaterThatGoesKeepsItsFloorWhileItDrains()
    {
        // A voxel on a floor 0.5 into it whose water is gone in the newest data: its floor is the one it had.
        BrickView from = View(1f, (5, 5, 5, 0.4f, 0.5f));
        BrickView to = View(1f);

        BrickView half = WaterEase.Blend(from, to, 0.5f);

        Assert.Equal(0.2f, half.Water[Brick.Index(5, 5, 5)], 4);
        Assert.Equal(0.5f, half.Solid[Brick.Index(5, 5, 5)]);
    }

    [Fact]
    public void WithNothingToEaseFromTheNewestIsDrawn()
    {
        BrickView to = View(1f, (5, 5, 5, 0.4f, 0f));

        Assert.Same(to, WaterEase.Blend(null, to, 0.5f));
        Assert.Same(to, WaterEase.Blend(View(1f), to, 1f));
        Assert.Same(to, WaterEase.Blend(View(0.5f), to, 0.5f));
        Assert.Same(to, WaterEase.Blend(new BrickView(1, 0, 0, 1f), to, 0.5f));
        Assert.Equal(0f, WaterEase.Blend(View(1f), to, -1f).Water[Brick.Index(5, 5, 5)]);
    }

    [Fact]
    public void WaterTooThinIsNotDrawn()
    {
        // Two centimetres of water in a 1 m voxel, then in a 0.5 m one (four hundredths of it): under ShowDepth.
        BrickView thin = WaterEase.Show(View(1f, (5, 5, 5, 0.02f, 0.5f)), null);
        BrickView small = WaterEase.Show(View(0.5f, (5, 5, 5, 0.04f, 0.5f)), null);
        BrickView deep = WaterEase.Show(View(1f, (5, 5, 5, 0.05f, 0.5f)), null);

        Assert.Equal(0f, thin.Water[Brick.Index(5, 5, 5)]);
        Assert.Equal(0f, thin.Solid[Brick.Index(5, 5, 5)]);
        Assert.Equal(0f, small.Water[Brick.Index(5, 5, 5)]);
        Assert.Equal(0.05f, deep.Water[Brick.Index(5, 5, 5)]);
        Assert.Equal(0.5f, deep.Solid[Brick.Index(5, 5, 5)]);

        // Water all drawn is drawn as it is, not copied; with some left out, the rest is drawn as it was.
        BrickView pool = View(1f, (5, 5, 5, 1f, 0f), (5, 6, 5, 0.5f, 0f));
        Assert.Same(pool, WaterEase.Show(pool, null));
        BrickView some = WaterEase.Show(View(1f, (5, 5, 5, 1f, 0f), (5, 6, 5, 0.01f, 0f)), null);
        Assert.Equal((1f, 0f), (some.Water[Brick.Index(5, 5, 5)], some.Water[Brick.Index(5, 6, 5)]));

        // Nor is its surface.
        var field = new VoxelField();
        field.Set(thin);
        Assert.Empty(VoxelSurface.Build(field, thin).Triangles);
    }

    [Fact]
    public void AVoxelDrawnStaysDrawnUntilItsWaterIsClearlyUnder()
    {
        // A sheet 5 cm deep, drawn; then 2 cm (kept: it was drawn), then 2 cm where it was not (not shown), then
        // under a centimetre (gone), then 2 cm again (not back until ShowDepth).
        BrickView drawn = WaterEase.Show(View(1f, (5, 5, 5, 0.05f, 0.5f)), null);
        BrickView kept = WaterEase.Show(View(1f, (5, 5, 5, 0.02f, 0.5f)), drawn);
        BrickView notShown = WaterEase.Show(View(1f, (5, 5, 5, 0.02f, 0.5f)), View(1f));
        BrickView gone = WaterEase.Show(View(1f, (5, 5, 5, 0.005f, 0.5f)), kept);
        BrickView back = WaterEase.Show(View(1f, (5, 5, 5, 0.02f, 0.5f)), gone);

        int i = Brick.Index(5, 5, 5);
        Assert.Equal(0.02f, kept.Water[i]);
        Assert.Equal(0f, notShown.Water[i]);
        Assert.Equal(0f, gone.Water[i]);
        Assert.Equal(0f, back.Water[i]);

        // What was drawn of another grid says nothing of this one.
        Assert.Equal(0f, WaterEase.Show(View(1f, (5, 5, 5, 0.02f, 0.5f)), WaterEase.Show(View(0.5f, (5, 5, 5, 0.5f, 0f)), null)).Water[i]);
    }

    private static BrickView View(float voxelSize, params (int X, int Y, int Z, float Water, float Solid)[] voxels)
    {
        var view = new BrickView(0, 0, 0, voxelSize);
        foreach ((int x, int y, int z, float water, float solid) in voxels)
        {
            view.Water[Brick.Index(x, y, z)] = water;
            view.Solid[Brick.Index(x, y, z)] = solid;
        }

        return view;
    }
}
