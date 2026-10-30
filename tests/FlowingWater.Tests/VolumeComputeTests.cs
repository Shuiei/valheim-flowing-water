using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The volume water's compute shader (FlowingWaterVolume.compute), read from its source, and what VolumeWater binds for
/// it: every kernel that reads the ground, itself or through what it calls, has the ground bound (unbound, Unity skips
/// the kernel: the pressure's divergence was skipped once the cells under the grid read the ground); under the grid a
/// cell is solid only where something is; water out of the top is let go; a facing source's slot stands on the ground.
/// </summary>
public class VolumeComputeTests
{
    private static readonly string Source = ShaderSource.Read("FlowingWaterVolume.compute");

    [Fact]
    public void EveryKernelReadingTheGroundHasItBound()
    {
        string water = System.IO.File.ReadAllText(Repository.File("src/Client/VolumeWater.cs"));
        HashSet<string> bound = BoundWithGround(water);
        List<string> kernels = [.. Regex.Matches(Source, @"#pragma kernel (\w+)", RegexOptions.None, ShaderSource.Patience).Select(m => m.Groups[1].Value)];
        HashSet<string> reading = ReadingTheGround();

        Assert.Contains("Divergence", reading);
        Assert.All(kernels.Where(reading.Contains), k => Assert.True(bound.Contains("_" + char.ToLowerInvariant(k[0]) + k[1..]), $"{k} reads the ground unbound"));
    }

    [Fact]
    public void UnderTheGridACellIsSolidOnlyWhereSomethingIs()
    {
        string typeAt = ShaderSource.Function(Source, "int TypeAt(int3 c)");

        Assert.Contains("return SolidAt(CentreOf(c)) ? SOLID : WetBeyond(c) ? BEYOND : AIR;", typeAt);
    }

    [Fact]
    public void WaterOutOfTheTopIsLetGoWithOpenSides()
    {
        string move = ShaderSource.Function(Source, "void Move(uint3 id : SV_DispatchThreadID)");

        int top = move.IndexOf("if (to.y >= hi.y && _OpenSides != 0)", System.StringComparison.Ordinal);
        Assert.True(top >= 0, "no letting go out of the top");
        Assert.True(top < move.IndexOf("to.y = min(to.y, hi.y - eps);", System.StringComparison.Ordinal), "held under the top first");
    }

    [Fact]
    public void AFacingSourcesSlotStandsOnTheGroundUnderEachCell()
    {
        string inSource = ShaderSource.Function(Source, "bool InSource(Spring spring, int s, float3 place)");

        Assert.Contains("o.z -= max(0.0, GroundAt(place.xz) - spring.ring.y);", inSource);
    }

    // The kernels VolumeWater gives the ground: those in the list it binds _Bed for in a loop, and any it binds one by one.
    private static HashSet<string> BoundWithGround(string water)
    {
        var bound = new HashSet<string>();
        Match loop = Regex.Match(water, @"foreach \(int kernel in new\[\] \{([^}]*)\}\)\s*\{\s*_shader\.SetTexture\(kernel, BedId", RegexOptions.None, ShaderSource.Patience);
        Assert.True(loop.Success, "no loop binding the ground");
        bound.UnionWith(loop.Groups[1].Value.Split(',').Select(k => k.Trim()));
        bound.UnionWith(Regex.Matches(water, @"_shader\.SetTexture\((_\w+), BedId", RegexOptions.None, ShaderSource.Patience).Select(m => m.Groups[1].Value));
        return bound;
    }

    // The functions that read the ground (_Bed), themselves or through the functions they call.
    private static HashSet<string> ReadingTheGround()
    {
        var bodies = new Dictionary<string, string>();
        foreach (Match m in Regex.Matches(Source, @"^(?:\w+) (\w+)\([^)]*\)\s*$", RegexOptions.Multiline, ShaderSource.Patience))
        {
            bodies[m.Groups[1].Value] = ShaderSource.Function(Source, m.Value.Trim());
        }

        var reading = new HashSet<string>(bodies.Where(b => b.Value.Contains("_Bed[")).Select(b => b.Key));
        bool grew = true;
        while (grew)
        {
            grew = false;
            foreach ((string name, string body) in bodies)
            {
                if (!reading.Contains(name) && reading.Any(r => Regex.IsMatch(body, @"\b" + r + @"\(", RegexOptions.None, ShaderSource.Patience)))
                {
                    reading.Add(name);
                    grew = true;
                }
            }
        }

        return reading;
    }
}
