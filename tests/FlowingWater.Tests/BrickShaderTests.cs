using System;
using System.Globalization;
using System.Text.RegularExpressions;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// Where the bricks' surface (FlowingWater.shader) draws, read from its source: not where it stands steeper than the
/// CPU's surface joins two columns (VoxelSurface.MaxStep rows a column, along either axis, so a slope dropping that much
/// both ways is still drawn: the shell draws no side there, VoxelShellTests'
/// ASheetDownADiagonalSlopeThreeVoxelsAColumnBothWaysShowsNoSide; its length, 4.2, was cut, and nothing drew it), and not where the volume water near the camera has
/// filled its square (FlowingWaterSimMask.cginc), the strip a move took in left to the bricks till it is poured.
/// </summary>
public class BrickShaderTests
{
    [Fact]
    public void TheSurfaceIsCutWhereItRisesMoreThanTheCpuSurfaceJoinsAlongEitherAxis()
    {
        string source = ShaderSource.Read("FlowingWater.shader");

        Match steep = Regex.Match(source, @"#define STEEP_GONE ([0-9.]+)", RegexOptions.None, ShaderSource.Patience);
        Assert.True(steep.Success, "no STEEP_GONE");
        Assert.Equal(VoxelSurface.MaxStep, float.Parse(steep.Groups[1].Value, CultureInfo.InvariantCulture));
        Assert.Contains("o.rise = max(abs(east), abs(north)) / max(_VoxelSize, 0.001);", ShaderSource.Function(source, "void vert("), StringComparison.Ordinal);
        Assert.Matches(new Regex(@"i\.rise > STEEP_GONE\)", RegexOptions.None, ShaderSource.Patience), ShaderSource.Function(source, "void surf("));
    }

    [Fact]
    public void TheVolumeDrawsOnlyWhatItHasFilledTheBricksTheRest()
    {
        string draws = ShaderSource.Function(ShaderSource.Read("FlowingWaterSimMask.cginc"), "bool SimDraws(");

        Assert.Contains("float2 edge = min(world.xz - _FWSimFilled.xy, _FWSimFilled.zw - world.xz);", draws, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"_FWSimWhole > 0\.5 && min\(edge\.x, edge\.y\) > _FWSimEdge", RegexOptions.None, ShaderSource.Patience), draws);
        Assert.Matches(new Regex(@"return tex2Dlod\(_FWSimTarget, [^;]+\)\.y > 0\.5;\s*}$", RegexOptions.None, ShaderSource.Patience), draws);
    }
}
