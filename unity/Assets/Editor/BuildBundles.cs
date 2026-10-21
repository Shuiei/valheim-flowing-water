using System;
using System.IO;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

// Builds the water asset bundles the plugin embeds, one per game platform (the shader is compiled
// for each platform's graphics APIs). Batch mode:
//   Unity -batchmode -quit -projectPath unity -executeMethod BuildBundles.Build -logFile -
// Output: ../assets/bundles/flowingwater.windows and flowingwater.linux (beside this project).
public static class BuildBundles
{
    public const string ShaderPath = "Assets/FlowingWater/Shaders/FlowingWater.shader";

    public const string NormalPath = "Assets/FlowingWater/Textures/water_normal.png";

    public const string FoamPath = "Assets/FlowingWater/Textures/water_foam.png";

    public const string MaterialPath = "Assets/FlowingWater/FlowingWater.mat";

    public const string BundleName = "flowingwater";

    public static void Build()
    {
        try
        {
            Prepare();
            string output = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "..", "assets", "bundles"));
            Directory.CreateDirectory(output);
            BuildFor(BuildTarget.StandaloneWindows64, output, "windows", new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.Direct3D12, GraphicsDeviceType.Vulkan });
            BuildFor(BuildTarget.StandaloneLinux64, output, "linux", new[] { GraphicsDeviceType.Vulkan, GraphicsDeviceType.OpenGLCore });
            Debug.Log($"FlowingWater: bundles written to {output}");
        }
        catch (Exception e)
        {
            Debug.LogError($"FlowingWater: bundle build failed: {e}");
            EditorApplication.Exit(1);
        }
    }

    // The textures' import settings, the material, and shader variants kept for every fog mode
    // (with no scene in the build, the default stripping would drop the fog the game uses).
    public static Material Prepare()
    {
        AssetDatabase.Refresh();
        Import(NormalPath, TextureImporterType.NormalMap, true);
        Import(FoamPath, TextureImporterType.SingleChannel, false);

        var shader = AssetDatabase.LoadAssetAtPath<Shader>(ShaderPath);
        if (shader == null)
        {
            throw new InvalidOperationException($"{ShaderPath} did not import");
        }

        foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
        {
            Debug.Log($"FlowingWater shader {message.severity}: {message.message} (line {message.line}, {message.platform})");
            if (message.severity == ShaderCompilerMessageSeverity.Error)
            {
                throw new InvalidOperationException($"shader error: {message.message} at line {message.line}");
            }
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, MaterialPath);
        }

        material.shader = shader;
        material.name = "FlowingWater";
        material.SetTexture("_NormalMap", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(FoamPath));
        EditorUtility.SetDirty(material);

        var settings = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
        settings.FindProperty("m_FogStripping").intValue = 1;
        settings.FindProperty("m_FogKeepLinear").boolValue = true;
        settings.FindProperty("m_FogKeepExp").boolValue = true;
        settings.FindProperty("m_FogKeepExp2").boolValue = true;
        settings.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();
        return material;
    }

    private static void Import(string path, TextureImporterType type, bool colour)
    {
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null)
        {
            throw new InvalidOperationException($"{path} did not import");
        }

        importer.textureType = type;
        importer.sRGBTexture = colour && type != TextureImporterType.NormalMap;
        importer.wrapMode = TextureWrapMode.Repeat;
        importer.mipmapEnabled = true;
        importer.anisoLevel = 4;
        importer.maxTextureSize = 512;
        importer.SaveAndReimport();
    }

    private static void BuildFor(BuildTarget target, string output, string suffix, GraphicsDeviceType[] apis)
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(target, false);
        PlayerSettings.SetGraphicsAPIs(target, apis);
        string folder = Path.Combine(output, "build-" + suffix);
        Directory.CreateDirectory(folder);
        var build = new AssetBundleBuild
        {
            assetBundleName = BundleName,
            assetNames = new[] { MaterialPath, ShaderPath, NormalPath, FoamPath },
        };
        AssetBundleManifest manifest = BuildPipeline.BuildAssetBundles(folder, new[] { build }, BuildAssetBundleOptions.ChunkBasedCompression | BuildAssetBundleOptions.StrictMode, target);
        if (manifest == null)
        {
            throw new InvalidOperationException($"building for {target} failed");
        }

        string bundle = Path.Combine(output, BundleName + "." + suffix);
        File.Copy(Path.Combine(folder, BundleName), bundle, true);
        Debug.Log($"FlowingWater: {target} bundle {new FileInfo(bundle).Length} bytes");
    }
}
