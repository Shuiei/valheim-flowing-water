using System;
using System.Text.RegularExpressions;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The waves as the two water shaders draw them (the card's near the camera, the blocks' beyond), read from their
/// sources: both raise the same long waves (FlowingWaterWaves.cginc's LongWaves) at the world place, and nothing else,
/// so they meet at the square's edge; the fine waves are only the shared look's, lit near and far; the blocks churn on
/// their crests and never where water slows (they do not know the water ahead), the card on both. What the functions
/// give is the preview's check (unity/Assets/Editor/SimPreview.cs, Waves).
/// </summary>
public class WaveShadersTests
{
    private static readonly TimeSpan Patience = ShaderSource.Patience;

    [Theory]
    [InlineData("FlowingWater.shader")]
    [InlineData("FlowingWaterSim.shader")]
    public void BothRaiseTheSameLongWavesAtTheWorldPlaceAndNothingElse(string shader)
    {
        string vertex = ShaderSource.Function(ShaderSource.Read(shader), "void vert(");

        Assert.Single(Regex.Matches(vertex, @"\bLongWaves\(", RegexOptions.None, Patience));
        Assert.Matches(new Regex(@"float2 world = mul\(unity_ObjectToWorld, v\.vertex\)\.xz;[\s\S]*LongWaves\(world,", RegexOptions.None, Patience), vertex);
        Assert.Matches(new Regex(@"v\.vertex\.y \+= wave\.x;", RegexOptions.None, Patience), vertex);
        Assert.Matches(new Regex(@"\+ wave\.yz;", RegexOptions.None, Patience), vertex);
        Assert.DoesNotMatch(new Regex(@"\b(Chop|WaveHeight|WaveNoise|FineWaves)\(", RegexOptions.None, Patience), vertex);
    }

    [Theory]
    [InlineData("FlowingWater.shader")]
    [InlineData("FlowingWaterSim.shader")]
    public void BothLightTheFineWavesThroughTheSharedLook(string shader)
    {
        string source = ShaderSource.Read(shader);

        Assert.Contains("#include \"FlowingWaterLook.cginc\"", source, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"\bLook\(", RegexOptions.None, Patience), ShaderSource.Function(source, "void surf("));
        Assert.Matches(new Regex(@"n = FineWaves\(n, i\.world, i\.current, depth\);", RegexOptions.None, Patience), ShaderSource.Function(ShaderSource.Read("FlowingWaterLook.cginc"), "void Look("));
    }

    [Fact]
    public void TheBlocksChurnOnlyOnCrestsAndTheCardWhereItSlowsAndWhereFallsLandToo()
    {
        Assert.Matches(new Regex(@"Churn\(crest, [^;]+, 0\.0\)\)?;", RegexOptions.None, Patience), ShaderSource.Function(ShaderSource.Read("FlowingWater.shader"), "void vert("));
        string card = ShaderSource.Function(ShaderSource.Read("FlowingWaterSim.shader"), "void vert(");
        Assert.Matches(new Regex(@"max\(Churn\(crest, [^;]+, slowing\), At\(_SimChurn, p\)\);", RegexOptions.None, Patience), card);
    }
}
