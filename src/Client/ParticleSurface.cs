using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlowingWater.Client;

/// <summary>
/// The particle water (<see cref="ParticleWater"/>) drawn as water: a screen-space surface over the particles. A
/// command buffer on the camera, before the scene's transparent things, makes two textures with the mod's
/// FlowingWaterParticles shader (the nearest particle's distance at each pixel, smoothed into one surface; the water
/// the view goes through there), and the FlowingWaterParticleSurface shader then draws the water over the scene with
/// the transparent things, after the sheet of water near the camera, grabbing what is behind as the sheet does. Needs
/// only Unity, so the Unity project's preview draws it too.
/// </summary>
internal sealed class ParticleSurface : IDisposable
{
    private const CameraEvent When = CameraEvent.BeforeForwardAlpha;

    private const int DepthPass = 0;
    private const int ThicknessPass = 1;
    private const int BlurPass = 2;
    private const int SpheresPass = 3;

    private static readonly int ParticleDepthId = Shader.PropertyToID("_ParticleDepth");
    private static readonly int ParticleThicknessId = Shader.PropertyToID("_ParticleThickness");
    private static readonly int BlurDirectionId = Shader.PropertyToID("_BlurDirection");
    private static readonly int SunDirId = Shader.PropertyToID("_FWSunDir");
    private static readonly int SunColourId = Shader.PropertyToID("_FWSunColour");

    private static readonly Bounds Everywhere = new(Vector3.zero, new Vector3(100000f, 100000f, 100000f));

    private readonly CommandBuffer _commands = new() { name = "FlowingWater particle water" };

    // The textures, the camera's size (made again when it changes): the distances (with a depth buffer, the nearest
    // kept), the distances smoothed one way, the thickness.
    private RenderTexture _depth;

    private RenderTexture _smoothed;

    private RenderTexture _thickness;

    private Camera _camera;

    private Light _sun;

    private int _sunLookedFor = -1;

    /// <summary>Draws the particles as opaque spheres instead of water (the console's "flowingwater particles spheres").</summary>
    public static bool Spheres { get; set; }

    /// <summary>
    /// The water as this camera will draw it next: the command buffer filled for this frame's particles (as many as the
    /// card says there are), on the camera (moved from the last one if it changed), and the surface drawn over the scene
    /// with <paramref name="surface"/> (the mod's particle surface material).
    /// </summary>
    public void Render(Camera camera, ParticleWater water, Material surface)
    {
        if (_camera != camera)
        {
            Detach();
            _camera = camera;
            _camera.AddCommandBuffer(When, _commands);
        }

        _commands.Clear();
        Sun();
        if (Spheres)
        {
            _commands.DrawProceduralIndirect(Matrix4x4.identity, water.Material, SpheresPass, MeshTopology.Triangles, water.Arguments, 0, water.Properties);
            return;
        }

        Textures(camera.pixelWidth, camera.pixelHeight);
        _commands.SetRenderTarget(_depth);
        _commands.ClearRenderTarget(true, true, Color.clear);
        _commands.DrawProceduralIndirect(Matrix4x4.identity, water.Material, DepthPass, MeshTopology.Triangles, water.Arguments, 0, water.Properties);

        _commands.SetRenderTarget(_thickness);
        _commands.ClearRenderTarget(false, true, Color.clear);
        _commands.DrawProceduralIndirect(Matrix4x4.identity, water.Material, ThicknessPass, MeshTopology.Triangles, water.Arguments, 0, water.Properties);

        _commands.SetGlobalVector(BlurDirectionId, new Vector4(1f, 0f, 0f, 0f));
        _commands.Blit(_depth, _smoothed, water.Material, BlurPass);
        _commands.SetGlobalVector(BlurDirectionId, new Vector4(0f, 1f, 0f, 0f));
        _commands.Blit(_smoothed, _depth, water.Material, BlurPass);
        _commands.SetGlobalTexture(ParticleDepthId, _depth);
        _commands.SetGlobalTexture(ParticleThicknessId, _thickness);

        // Back to the camera's own target for what it draws next.
        _commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        Graphics.DrawProcedural(surface, Everywhere, MeshTopology.Triangles, 3, 1, camera, null, ShadowCastingMode.Off, false, 0);
    }

    /// <summary>Off the camera it was on.</summary>
    public void Detach()
    {
        // Unity's own null test: a camera gone with its world is null to it, and left alone.
        if (_camera == null)
        {
            return;
        }

        _camera.RemoveCommandBuffer(When, _commands);
        _camera = null;
    }

    public void Dispose()
    {
        Detach();
        _commands.Release();
        Free(ref _depth);
        Free(ref _smoothed);
        Free(ref _thickness);
    }

    // The textures at the camera's size.
    private void Textures(int width, int height)
    {
        if (_depth != null && _depth.width == width && _depth.height == height)
        {
            return;
        }

        Free(ref _depth);
        Free(ref _smoothed);
        Free(ref _thickness);
        _depth = Make("distance", width, height, 24, RenderTextureFormat.RFloat, FilterMode.Point);
        _smoothed = Make("distance smoothed", width, height, 0, RenderTextureFormat.RFloat, FilterMode.Point);
        _thickness = Make("thickness", width, height, 0, RenderTextureFormat.ARGBHalf, FilterMode.Bilinear);
    }

    private static RenderTexture Make(string name, int width, int height, int depthBits, RenderTextureFormat format, FilterMode filter)
    {
        var texture = new RenderTexture(width, height, depthBits, format, RenderTextureReadWrite.Linear)
        {
            name = "FlowingWater particle " + name,
            filterMode = filter,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.Create();
        return texture;
    }

    // Gone: in the game as Unity frees things, in the editor's preview (not playing) at once, as it must there.
    private static void Free(ref RenderTexture texture)
    {
        if (texture == null)
        {
            return;
        }

        texture.Release();
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(texture);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(texture);
        }

        texture = null;
    }

    // The scene's sun, for the shaders (a command buffer's draw gets no light of its own): the one the render settings
    // name, else the first directional light, looked for once a second while none is found; none, straight down, white.
    private void Sun()
    {
        Light sun = Shining(_sun) ? _sun : Shining(RenderSettings.sun) ? RenderSettings.sun : null;
        if (sun == null && Time.frameCount - _sunLookedFor >= 60)
        {
            _sunLookedFor = Time.frameCount;
            foreach (Light light in UnityEngine.Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.type == LightType.Directional && Shining(light))
                {
                    sun = light;
                    break;
                }
            }
        }

        _sun = sun;
        Vector3 direction = sun != null ? -sun.transform.forward : Vector3.up;
        Color colour = sun != null ? sun.color * sun.intensity : Color.white;
        Shader.SetGlobalVector(SunDirId, direction);
        Shader.SetGlobalColor(SunColourId, colour);
    }

    private static bool Shining(Light light) => light != null && light.isActiveAndEnabled;
}
