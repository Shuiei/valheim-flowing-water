using System;
using System.IO;
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
    private const string Shaders = "unity/Assets/FlowingWater/Shaders/";

    // The longest a match may take (the files are the repository's own, short).
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(1);

    [Theory]
    [InlineData("FlowingWater.shader")]
    [InlineData("FlowingWaterSim.shader")]
    public void BothRaiseTheSameLongWavesAtTheWorldPlaceAndNothingElse(string shader)
    {
        string vertex = Function(Read(shader), "void vert(");

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
        string source = Read(shader);

        Assert.Contains("#include \"FlowingWaterLook.cginc\"", source, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"\bLook\(", RegexOptions.None, Patience), Function(source, "void surf("));
        Assert.Matches(new Regex(@"n = FineWaves\(n, i\.world, i\.current, depth\);", RegexOptions.None, Patience), Function(Read("FlowingWaterLook.cginc"), "void Look("));
    }

    [Fact]
    public void TheBlocksChurnOnlyOnCrestsAndTheCardWhereItSlowsAndWhereFallsLandToo()
    {
        Assert.Matches(new Regex(@"Churn\(crest, [^;]+, 0\.0\)\)?;", RegexOptions.None, Patience), Function(Read("FlowingWater.shader"), "void vert("));
        string card = Function(Read("FlowingWaterSim.shader"), "void vert(");
        Assert.Matches(new Regex(@"max\(Churn\(crest, [^;]+, slowing\), At\(_SimChurn, p\)\);", RegexOptions.None, Patience), card);
    }

    private static string Read(string file) => File.ReadAllText(Repository.File(Shaders + file));

    // A function's body, from its signature to its closing brace (its braces balanced).
    private static string Function(string source, string signature)
    {
        int start = source.IndexOf(signature, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no {signature}");
        int open = source.IndexOf('{', start), depth = 0;
        for (int at = open; at < source.Length; at++)
        {
            depth += source[at] == '{' ? 1 : source[at] == '}' ? -1 : 0;
            if (depth == 0)
            {
                return source[start..(at + 1)];
            }
        }

        throw new InvalidDataException($"{signature} has no closing brace");
    }
}
