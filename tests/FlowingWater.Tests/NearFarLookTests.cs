using System;
using System.Text.RegularExpressions;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The volume water near the camera (FlowingWaterVolumeSurface.shader) looks as the bricks' water beyond it
/// (FlowingWater.shader on FlowingWaterLook.cginc), read from their sources, so the two are one water where they meet at
/// the square's side: lit with Unity's standard lighting, alpha premultiplied, as the bricks' surface shader is; the same
/// body colours; shallow water as glossy as each other (one include); and at the side the near water fades out whole,
/// its mirrored sky too.
/// </summary>
public class NearFarLookTests
{
    [Fact]
    public void TheNearWaterIsLitWithTheStandardLightingPremultipliedAsTheBricksWaterIs()
    {
        string near = ShaderSource.Read("FlowingWaterVolumeSurface.shader");

        Assert.Contains("#pragma surface surf Standard alpha:premul", ShaderSource.Read("FlowingWater.shader"), StringComparison.Ordinal);
        Assert.Contains("#define _ALPHAPREMULTIPLY_ON 1", near, StringComparison.Ordinal);
        Assert.Contains("#include \"UnityPBSLighting.cginc\"", near, StringComparison.Ordinal);
        Assert.Contains("Blend One OneMinusSrcAlpha", near, StringComparison.Ordinal);
        string frag = ShaderSource.Function(near, "fixed4 frag(");
        Assert.Contains("LightingStandard_GI(so, lighting, gi);", frag, StringComparison.Ordinal);
        Assert.Contains("LightingStandard(so, toCamera, gi);", frag, StringComparison.Ordinal);
    }

    [Fact]
    public void BothTakeTheirBodyFromTheShallowAndTheDeepColours()
    {
        Assert.Matches(new Regex(@"lerp\(_ColorBottomShallow\.rgb, _ColorBottom\.rgb, ", RegexOptions.None, ShaderSource.Patience), ShaderSource.Function(ShaderSource.Read("FlowingWaterLook.cginc"), "void Look("));
        Assert.Matches(new Regex(@"float3 body = \(1\.0 - through\) \* lerp\(_ColorBottomShallow\.rgb, _ColorBottom\.rgb, ", RegexOptions.None, ShaderSource.Patience), ShaderSource.Function(ShaderSource.Read("FlowingWaterVolumeSurface.shader"), "fixed4 frag("));
    }

    [Theory]
    [InlineData("FlowingWaterLook.cginc")]
    [InlineData("FlowingWaterVolumeSurface.shader")]
    public void BothMakeShallowWaterAsGlossyFromOneInclude(string shader)
    {
        string source = ShaderSource.Read(shader);

        Assert.Contains("#include \"FlowingWaterShallow.cginc\"", source, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"lerp\(SHALLOW_GLOSS, 1\.0, saturate\(\w+ / SHALLOW_ABSORB\)\)", RegexOptions.None, ShaderSource.Patience), source);
        Assert.DoesNotContain("#define SHALLOW_GLOSS", source, StringComparison.Ordinal);
    }

    [Fact]
    public void AtTheSquaresSideTheNearWaterFadesOutWhole()
    {
        string frag = ShaderSource.Function(ShaderSource.Read("FlowingWaterVolumeSurface.shader"), "fixed4 frag(");

        Assert.Matches(new Regex(@"return colour \* found\.w;\s*}$", RegexOptions.None, ShaderSource.Patience), frag);
    }
}
