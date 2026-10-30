using System;
using System.IO;
using System.Text.RegularExpressions;
using Xunit;

namespace FlowingWater.Tests;

/// <summary>
/// The bundle build's preparation (unity/Assets/Editor/BuildBundles.cs, run only in Unity), read from its source: every
/// shader and every compute shader of the water imported again, each type looked for on its own (a change to an include
/// reached only the shaders including it themselves), the build failing if one finds none; and the shader folder
/// holding both types, so neither search comes back empty.
/// </summary>
public class BundlePrepareTests
{
    private const string Shaders = "unity/Assets/FlowingWater/Shaders";

    [Fact]
    public void EachShaderTypeIsImportedAgainOnItsOwnAndNoneFoundFails()
    {
        string prepare = ShaderSource.Function(File.ReadAllText(Repository.File("unity/Assets/Editor/BuildBundles.cs")), "public static Material Prepare(");

        Assert.Contains("foreach (string type in new[] { \"t:Shader\", \"t:ComputeShader\" })", prepare, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"AssetDatabase\.FindAssets\(type, new\[\] \{ ""Assets/FlowingWater/Shaders"" \}\)", RegexOptions.None, ShaderSource.Patience), prepare);
        Assert.Matches(new Regex(@"if \(found\.Length == 0\)\s*\{\s*throw new InvalidOperationException", RegexOptions.None, ShaderSource.Patience), prepare);
        Assert.Contains("AssetDatabase.ImportAsset(AssetDatabase.GUIDToAssetPath(guid), ImportAssetOptions.ForceUpdate);", prepare, StringComparison.Ordinal);
        Assert.True(prepare.IndexOf("ForceUpdate", StringComparison.Ordinal) < prepare.IndexOf("MakeMaterial(", StringComparison.Ordinal), "the shaders are imported again before the materials are made");
    }

    [Theory]
    [InlineData(".shader")]
    [InlineData(".compute")]
    public void TheShaderFolderHoldsBothTypes(string extension)
    {
        Assert.Contains(Directory.GetFiles(Repository.File(Shaders)), f => f.EndsWith(extension, StringComparison.Ordinal));
    }
}
