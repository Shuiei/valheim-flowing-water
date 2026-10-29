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

    public const string ShellShaderPath = "Assets/FlowingWater/Shaders/FlowingWaterShell.shader";

    public const string ShellMaterialPath = "Assets/FlowingWater/FlowingWaterShell.mat";

    public const string SimShaderPath = "Assets/FlowingWater/Shaders/FlowingWaterSim.shader";

    public const string SimMaterialPath = "Assets/FlowingWater/FlowingWaterSim.mat";

    public const string SimComputePath = "Assets/FlowingWater/Shaders/FlowingWaterSim.compute";

    public const string SprayShaderPath = "Assets/FlowingWater/Shaders/FlowingWaterSpray.shader";

    public const string SprayMaterialPath = "Assets/FlowingWater/FlowingWaterSpray.mat";

    public const string SprayComputePath = "Assets/FlowingWater/Shaders/FlowingWaterSpray.compute";

    public const string FallShaderPath = "Assets/FlowingWater/Shaders/FlowingWaterFall.shader";

    public const string FallMaterialPath = "Assets/FlowingWater/FlowingWaterFall.mat";

    // The particle water, a prototype (the console pours some, for testing).
    public const string ParticlesShaderPath = "Assets/FlowingWater/Shaders/FlowingWaterParticles.shader";

    public const string ParticlesMaterialPath = "Assets/FlowingWater/FlowingWaterParticles.mat";

    public const string ParticlesComputePath = "Assets/FlowingWater/Shaders/FlowingWaterParticles.compute";

    public const string ParticleSurfaceShaderPath = "Assets/FlowingWater/Shaders/FlowingWaterParticleSurface.shader";

    public const string ParticleSurfaceMaterialPath = "Assets/FlowingWater/FlowingWaterParticleSurface.mat";

    // The volume water (the water near the camera as one body of fluid), drawn with the particles' materials.
    public const string VolumeComputePath = "Assets/FlowingWater/Shaders/FlowingWaterVolume.compute";

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

    // The textures' import settings, the materials (the surface's, returned, and the shell's), and shader variants kept
    // for every fog mode (with no scene in the build, the default stripping would drop the fog the game uses).
    public static Material Prepare()
    {
        AssetDatabase.Refresh();
        Import(NormalPath, TextureImporterType.NormalMap, true);
        Import(FoamPath, TextureImporterType.SingleChannel, false);

        Material material = MakeMaterial(ShaderPath, MaterialPath, "FlowingWater");
        material.SetTexture("_RandomFoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(FoamPath));
        MakeMaterial(ShellShaderPath, ShellMaterialPath, "FlowingWaterShell");
        Material sim = MakeMaterial(SimShaderPath, SimMaterialPath, "FlowingWaterSim");
        sim.SetTexture("_RandomFoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(FoamPath));
        CheckCompute(SimComputePath, "Seed", "Fluxes", "Momentum", "Outflow", "Depth", "Reduce", "Relax", "WriteBed", "LandSpeeds", "Land", "Clear", "ShiftCells", "Shape");
        MakeMaterial(SprayShaderPath, SprayMaterialPath, "FlowingWaterSpray");
        CheckCompute(SprayComputePath, "EmitFromWater", "Move", "Curtains");
        MakeMaterial(FallShaderPath, FallMaterialPath, "FlowingWaterFall");
        MakeMaterial(ParticlesShaderPath, ParticlesMaterialPath, "FlowingWaterParticles");
        MakeMaterial(ParticleSurfaceShaderPath, ParticleSurfaceMaterialPath, "FlowingWaterParticleSurface");
        CheckCompute(ParticlesComputePath, "ClearColumns", "CountColumns", "Fill", "Spout", "ClearCounts", "Predict", "ScanBlocks", "ScanSums", "AddSums", "Scatter", "Settle", "Lambda", "Displace", "Apply", "Finish");
        CheckCompute(VolumeComputePath, "ClearCells", "ClearFaces", "Scatter", "Gather", "Collect", "Arguments", "Divergence", "Relax", "Project", "Extend", "Move", "Emit", "Regulate", "Compact", "Settle", "Seed");

        var settings = new SerializedObject(GraphicsSettings.GetGraphicsSettings());
        settings.FindProperty("m_FogStripping").intValue = 1;
        settings.FindProperty("m_FogKeepLinear").boolValue = true;
        settings.FindProperty("m_FogKeepExp").boolValue = true;
        settings.FindProperty("m_FogKeepExp2").boolValue = true;
        settings.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();
        return material;
    }

    // A material of the shader, made or found at its path, its values the shader's defaults, its textures set.
    private static Material MakeMaterial(string shaderPath, string materialPath, string name)
    {
        var shader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        if (shader == null)
        {
            throw new InvalidOperationException($"{shaderPath} did not import");
        }

        foreach (ShaderMessage message in ShaderUtil.GetShaderMessages(shader))
        {
            Debug.Log($"FlowingWater shader {name} {message.severity}: {message.message} (line {message.line}, {message.platform})");
            if (message.severity == ShaderCompilerMessageSeverity.Error)
            {
                throw new InvalidOperationException($"{shaderPath} error: {message.message} at line {message.line}");
            }
        }

        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        material.shader = shader;
        material.name = name;
        ResetToShaderDefaults(material, shader);
        material.SetTexture("_Normal", AssetDatabase.LoadAssetAtPath<Texture2D>(NormalPath));
        material.SetTexture("_FoamTex", AssetDatabase.LoadAssetAtPath<Texture2D>(FoamPath));
        EditorUtility.SetDirty(material);
        return material;
    }

    // A compute shader of the water near the camera, imported and compiled without errors, with these kernels.
    private static void CheckCompute(string path, params string[] kernels)
    {
        var compute = AssetDatabase.LoadAssetAtPath<ComputeShader>(path);
        if (compute == null)
        {
            throw new InvalidOperationException($"{path} did not import");
        }

        foreach (ShaderMessage message in ShaderUtil.GetComputeShaderMessages(compute))
        {
            Debug.Log($"FlowingWater compute {message.severity}: {message.message} (line {message.line}, {message.platform})");
            if (message.severity == ShaderCompilerMessageSeverity.Error)
            {
                throw new InvalidOperationException($"{path} error: {message.message} at line {message.line}");
            }
        }

        // A kernel that did not compile is not always told above: each must be there and run here.
        foreach (string kernel in kernels)
        {
            if (!compute.HasKernel(kernel) || !compute.IsSupported(compute.FindKernel(kernel)))
            {
                throw new InvalidOperationException($"{path}: kernel {kernel} did not compile");
            }
        }
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
                    throw new InvalidOperationException($"{shader.name}: property {name} is a {shader.GetPropertyType(i)}, which the build cannot reset to its default");
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
        if (type == TextureImporterType.SingleChannel)
        {
            // Kept in red, where the shaders read it (as in the sea's own textures); Unity's default is alpha.
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;
            importer.SetTextureSettings(settings);
        }

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
            assetNames = new[] { MaterialPath, ShaderPath, ShellMaterialPath, ShellShaderPath, SimMaterialPath, SimShaderPath, SimComputePath, SprayMaterialPath, SprayShaderPath, SprayComputePath, FallMaterialPath, FallShaderPath, ParticlesMaterialPath, ParticlesShaderPath, ParticlesComputePath, ParticleSurfaceMaterialPath, ParticleSurfaceShaderPath, VolumeComputePath, NormalPath, FoamPath },
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
