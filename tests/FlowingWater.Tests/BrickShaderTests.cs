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
/// apart are never joined), and not where the water near the camera draws: the sheet, or the volume water in what it has
/// filled of its box (FlowingWaterSimMask.cginc), the strip a move took in left to the sheet till it is poured.
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

    // A property's default in a shader's source.
    private static float Default(string source, string name)
    {
        Match found = Regex.Match(source, $@"{name} \(""[^""]*"", (?:Float|Range\([^)]*\))\) = ([0-9.]+)", RegexOptions.None, ShaderSource.Patience);
        Assert.True(found.Success, $"no {name}");
        return float.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    // A #define's number in a shader's source.
    private static float Define(string source, string name)
    {
        Match found = Regex.Match(source, $@"#define {name} ([0-9.]+)", RegexOptions.None, ShaderSource.Patience);
        Assert.True(found.Success, $"no {name}");
        return float.Parse(found.Groups[1].Value, CultureInfo.InvariantCulture);
    }

    [Fact]
    public void TheVolumeDrawsOnlyWhatItHasFilledOfItsBoxTheSheetTheRestOfItsSquare()
    {
        string mask = ShaderSource.Read("FlowingWaterSimMask.cginc");
        string volume = ShaderSource.Function(mask, "bool VolumeDraws(");
        string card = ShaderSource.Function(mask, "bool CardDraws(");

        Assert.Contains("_FWSimWhole < 0.5", volume, StringComparison.Ordinal);
        Assert.Contains("float2 edge = min(world.xz - _FWSimFilled.xy, _FWSimFilled.zw - world.xz);", volume, StringComparison.Ordinal);
        Assert.Contains("return min(edge.x, edge.y) > _FWSimEdge && world.y >= _FWSimBottom + _FWSimEdge && world.y <= _FWSimTop - _FWSimEdge;", volume, StringComparison.Ordinal);
        // Outside the volume's box, or above or below its grid, the sheet draws as the texels say (not the bricks).
        Assert.Contains("if (_FWSimOn < 0.5 || VolumeDraws(world))", card, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"return tex2Dlod\(_FWSimTarget, [^;]+\)\.y > 0\.5;\s*}$", RegexOptions.None, ShaderSource.Patience), card);
        Assert.Contains("return VolumeDraws(world) || CardDraws(world);", ShaderSource.Function(mask, "bool SimDraws("), StringComparison.Ordinal);
        Assert.Contains("!CardDraws(i.worldPos)", ShaderSource.Function(ShaderSource.Read("FlowingWaterSim.shader"), "void surf("), StringComparison.Ordinal);
    }

    [Fact]
    public void TheShellFadesWhiteWaterOutTowardItsOpenEdgesAndBetweenItsStreaks()
    {
        // The shell's faces carry their distances to the edges open to the air (VoxelShell.Edges) as their fourth uv.
        string shell = ShaderSource.Read("FlowingWaterShell.shader");
        string surf = ShaderSource.Function(shell, "void surf(");

        Assert.Contains("o.edges = v.texcoord3;", ShaderSource.Function(shell, "void vert("), StringComparison.Ordinal);
        Assert.Contains("float edge = min(min(i.edges.x, i.edges.y), i.edges.w) / max(_Fringe, 0.001);", surf, StringComparison.Ordinal);
        Assert.Contains("float fringe = saturate(min(edge, i.edges.z / max(_TopFringe, 0.001)) * lerp(0.5, 1.5, streaky));", surf, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"o\.Alpha = [^;]*lerp\(_WhiteGaps, 1\.0, [^;]*\* fringe;", RegexOptions.None, ShaderSource.Patience), surf);
        // A film or a fall at its widest keeps its middle whole: the fringe from each open end is under half its width.
        Assert.True(Default(shell, "_Fringe") < VoxelShell.WidestFall * 0.5f, "the fringe of an open end is narrower than half the widest fall");
        Assert.Contains("_mesh.SetUVs(3, Fours(shell.Edges));", System.IO.File.ReadAllText(Repository.File("src/Client/ShellRenderer.cs")), StringComparison.Ordinal);
    }

    [Fact]
    public void TheSheetsSprayAndFallsLeaveTheVolumesBoxToItButNotAJet()
    {
        Assert.Contains("VolumeDraws(p.placeAge.xyz)", ShaderSource.Function(ShaderSource.Read("FlowingWaterSpray.shader"), "v2f vert("), StringComparison.Ordinal);
        Assert.Contains("if (_FaceCamera < 0.5 && VolumeDraws(i.world))", ShaderSource.Function(ShaderSource.Read("FlowingWaterFall.shader"), "fixed4 frag("), StringComparison.Ordinal);
    }
}
