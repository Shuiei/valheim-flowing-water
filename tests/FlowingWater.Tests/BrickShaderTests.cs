using System;
using System.Globalization;
using System.Text.RegularExpressions;
using FlowingWater.Volume;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// Where the bricks' surface (FlowingWater.shader) draws, read from its source: between columns the CPU's surface joins
/// (at most VoxelSurface.MaxStep rows apart, counted by their tops' rows as the shell counts them: where it does not, the
/// shell draws a side and water on rock so steep is the falls'), never rising more than one row more a run (rows that far
/// apart are never joined), and not where the volume water near the camera has filled its square
/// (FlowingWaterSimMask.cginc), the strip a move took in left to the bricks till it is poured.
/// </summary>
public class BrickShaderTests
{
    [Fact]
    public void TheSurfaceIsCutBetweenColumnsTheShellCountsUnjoinedAndOnlyThere()
    {
        string source = ShaderSource.Read("FlowingWater.shader");

        Assert.Equal(VoxelSurface.MaxStep, Define(source, "JOIN_ROWS"));
        Assert.Equal(VoxelSurface.MaxStep + 1, Define(source, "STEEP_GONE"));
        Assert.Contains("o.rise = max(abs(east), abs(north)) / max(_VoxelSize, 0.001);", ShaderSource.Function(source, "void vert("), StringComparison.Ordinal);
        Assert.Contains("i.rise > STEEP_GONE || Unjoined(i.cell))", ShaderSource.Function(source, "void surf("), StringComparison.Ordinal);
        Assert.Contains("tex2Dlod(_FlowNext, float4(GridUv(corner), 0.0, 0.0)).w", ShaderSource.Function(source, "float4 ColumnAt("), StringComparison.Ordinal);
        Assert.Contains("abs(a.w - b.w) > JOIN_ROWS + 0.5", ShaderSource.Function(source, "bool Apart("), StringComparison.Ordinal);
        Assert.Contains("(b.z <= 0.0 && c.z <= 0.0 && Apart(a, d)) || (a.z <= 0.0 && d.z <= 0.0 && Apart(b, c))", ShaderSource.Function(source, "bool Unjoined("), StringComparison.Ordinal);
    }

    [Fact]
    public void EachColumnsTopRowIsInTheCurrentsSpareChannel()
    {
        // The shader's rows (above) are SurfaceGrid.Row, the voxel each top is in, not rebuilt from the level laid flat.
        string pack = System.IO.File.ReadAllText(Repository.File("src/Client/GpuSurface.cs"));

        Assert.Contains("_pixels[(k * 4) + 3] = grid.Row[k];", pack, StringComparison.Ordinal);
    }

    // A #define's number in a shader's source.
    private static float Define(string source, string name)
    {
        Match found = Regex.Match(source, $@"#define {name} ([0-9.]+)", RegexOptions.None, ShaderSource.Patience);
        Assert.True(found.Success, $"no {name}");
        return float.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture);
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
