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
        ResetToShaderDefaults(material, shader);
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

    // A material asset keeps the values it was first made with; when the shader's defaults change
    // later, the built material would still carry the old ones. Every value is set back to the
    // shader's own default, so the shader file is the one place the look is defined (the textures
    // are set from their files).
    private static void ResetToShaderDefaults(Material material, Shader shader)
    {
        for (int i = 0; i < shader.GetPropertyCount(); i++)
        {
            string name = shader.GetPropertyName(i);
            switch (shader.GetPropertyType(i))
            {
                case ShaderPropertyType.Color:
                    material.SetColor(name, (Color)shader.GetPropertyDefaultVectorValue(i));
                    break;
                case ShaderPropertyType.Vector:
                    material.SetVector(name, shader.GetPropertyDefaultVectorValue(i));
                    break;
                case ShaderPropertyType.Float:
                case ShaderPropertyType.Range:
                    material.SetFloat(name, shader.GetPropertyDefaultFloatValue(i));
                    break;
                case ShaderPropertyType.Texture:
                    // Set from their files after (Prepare): a texture's default is only a name ("bump", "white").
                    break;
                default:
                    // A kind of property this does not know how to reset (an integer) would keep the material's old
                    // value unseen: the build stops instead, to be taught it.
                    throw new InvalidOperationException($"{ShaderPath}: property {name} is a {shader.GetPropertyType(i)}, which the build cannot reset to its default");
            }
        }
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
