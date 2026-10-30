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
        Assert.Contains("return ceil((column.x / max(_VoxelSize, 0.001)) - 0.001) - 1.0;", ShaderSource.Function(source, "float RowOf("), StringComparison.Ordinal);
        Assert.Contains("abs(RowOf(a) - RowOf(b)) > JOIN_ROWS + 0.5", ShaderSource.Function(source, "bool Apart("), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10.9f, 8.7f, false)]
    [InlineData(10.99f, 8.05f, false)]
    [InlineData(10.5f, 9.5f, false)]
    [InlineData(10.99f, 8.0f, true)]
    [InlineData(11f, 7.5f, true)]
    public void TwoColumnsAreCutApartByTheirRowsNotTheirLevels(float high, float low, bool cut)
    {
        // The shader's rule (above) on two columns' levels (voxels over the brick's floor): rows 10 and 8 are joined
        // however far their levels (a rise of 2.2 or 2.9 was cut, and the shell drew no side: nothing drew it); a level
        // at a voxel's very top is that voxel's; and no joined pair rises more than STEEP_GONE.
        static int Row(float level) => (int)Math.Ceiling(level - 0.001f) - 1;
        bool apart = Math.Abs(Row(high) - Row(low)) > VoxelSurface.MaxStep;

        Assert.Equal(cut, apart);
        Assert.True(apart || high - low <= VoxelSurface.MaxStep + 1, $"a joined pair rises {high - low}");
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
