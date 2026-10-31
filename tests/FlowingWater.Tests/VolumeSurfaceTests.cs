using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The pictures the volume water's march writes (src/Client/VolumeSurface.cs, made only in the game), read from the
/// source: the surface's distances, found and smoothed, in full floats (its slope is the difference between neighbours a
/// texel apart: in half floats flat water seen along it was terraced in stripes), the normal's flow in half floats; and
/// every draw over the whole screen (the volume and particle water's surface, the froth) bounded about the camera and
/// given the scene's reflection there just before it is drawn (SceneReflection), so it is lit by the probes the bricks'
/// water about the camera is.
/// </summary>
public class VolumeSurfaceTests
{
    [Theory]
    [InlineData("found", "ARGBFloat")]
    [InlineData("found smoothed", "ARGBFloat")]
    [InlineData("normal", "ARGBHalf")]
    public void EachPictureIsMadeInItsFormat(string picture, string format)
    {
        string source = File.ReadAllText(Repository.File("src/Client/VolumeSurface.cs"));

        Match made = Regex.Match(source, $@"ParticleSurface\.Make\(""{Regex.Escape(picture)}"", width, height, 0, RenderTextureFormat\.(\w+),", RegexOptions.None, ShaderSource.Patience);
        Assert.True(made.Success, $"no picture {picture}");
        Assert.Equal(format, made.Groups[1].Value);
    }

    [Theory]
    [InlineData("src/Client/VolumeSurface.cs", 1)]
    [InlineData("src/Client/ParticleSurface.cs", 2)]
    public void EachFullScreenDrawIsBoundedAboutTheCameraAndGivenTheReflectionThere(string file, int draws)
    {
        string source = File.ReadAllText(Repository.File(file));

        MatchCollection found = Regex.Matches(source, @"SceneReflection\.Into\((\w+(?:\.\w+)?), camera\.transform\.position\);\s*Graphics\.DrawProcedural(?:Indirect)?\([^;]*\(camera\)[^;]*, camera, (\w+(?:\.\w+)?), ShadowCastingMode\.Off", RegexOptions.None, ShaderSource.Patience);
        Assert.Equal(draws, found.Count);
        Assert.All(found, m => Assert.Equal(m.Groups[1].Value, m.Groups[2].Value));
        Assert.Equal(draws, Regex.Count(source, @"Graphics\.DrawProcedural", RegexOptions.None, ShaderSource.Patience));
        Assert.Contains("internal static Bounds Around(Camera camera) => new(camera.transform.position, EverywhereSize);", File.ReadAllText(Repository.File("src/Client/ParticleSurface.cs")), StringComparison.Ordinal);
    }
}
