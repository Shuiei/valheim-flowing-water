using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlowingWater.Client;

/// <summary>
/// The volume water (<see cref="VolumeWater"/>) drawn as water: its own renderer. A command buffer on the camera,
/// before the scene's transparent things, marches a ray for each pixel of a half-size picture through the water's
/// density (the mod's FlowingWaterVolumeMarch shader) and keeps where the surface is, how much water the view goes
/// through, the foam there and the surface's normal; the FlowingWaterVolumeSurface shader then draws the water over the
/// scene with the transparent things, after the sheet of water near the camera, grabbing what is behind as the sheet
/// does; the froth's sprites go over it. Needs only Unity, so the Unity project's preview draws it too.
/// </summary>
internal sealed class VolumeSurface : IDisposable
{
    private const CameraEvent When = CameraEvent.BeforeForwardAlpha;

    private static readonly int WaterId = Shader.PropertyToID("_VolumeWater");
    private static readonly int NormalId = Shader.PropertyToID("_VolumeNormal");
    private static readonly int FineId = Shader.PropertyToID("_Fine");
    private static readonly int CoarseId = Shader.PropertyToID("_Coarse");
    private static readonly int FoamId = Shader.PropertyToID("_Foam");
    private static readonly int OriginId = Shader.PropertyToID("_VolumeOrigin");
    private static readonly int SizeId = Shader.PropertyToID("_VolumeSize");
    private static readonly int CellsId = Shader.PropertyToID("_VolumeCells");
    private static readonly int CellId = Shader.PropertyToID("_VolumeCell");

    private static readonly Bounds Everywhere = new(Vector3.zero, new Vector3(100000f, 100000f, 100000f));

    private readonly CommandBuffer _commands = new() { name = "FlowingWater volume water" };

    private readonly RenderTargetIdentifier[] _targets = new RenderTargetIdentifier[2];

    // The pictures the march writes, the camera's size over Scale (made again when it changes).
    private RenderTexture _water;

    private RenderTexture _normal;

    private Camera _camera;

    /// <summary>
    /// The size of the march's pictures as a share of the screen's, each way: half (the default) costs a quarter of
    /// the full size's march, and the surface's edge steps by two pixels; 1 is the screen's own.
    /// </summary>
    public static float Scale { get; set; } = 0.5f;

    /// <summary>
    /// The water as this camera will draw it next: the march run for this frame's water on the camera (moved from the
    /// last one if it changed), the surface drawn over the scene with <paramref name="surface"/>, the froth's sprites
    /// over it when there are any.
    /// </summary>
    public void Render(Camera camera, VolumeWater water, Material march, Material surface, IParticleCloud froth)
    {
        if (_camera != camera)
        {
            Detach();
            _camera = camera;
            _camera.AddCommandBuffer(When, _commands);
        }

        _commands.Clear();
        if (water.LastCount == 0)
        {
            // Nothing to draw, as the card last said (a frame or two behind: a fall's first drops wait that long).
            return;
        }

        ParticleSurface.Sun();
        march.SetTexture(FineId, water.Fine);
        march.SetTexture(CoarseId, water.Coarse);
        march.SetTexture(FoamId, water.Foam);
        march.SetVector(OriginId, water.Origin);
        march.SetVector(SizeId, water.Size);
        march.SetVector(CellsId, new Vector3(water.CellsX, water.CellsY, water.CellsZ));
        march.SetFloat(CellId, water.CellSize);

        Pictures(Mathf.Max(1, Mathf.RoundToInt(camera.pixelWidth * Scale)), Mathf.Max(1, Mathf.RoundToInt(camera.pixelHeight * Scale)));
        _targets[0] = _water;
        _targets[1] = _normal;
        _commands.SetRenderTarget(_targets, _water);
        _commands.ClearRenderTarget(false, true, Color.clear);
        _commands.DrawProcedural(Matrix4x4.identity, march, 0, MeshTopology.Triangles, 3, 1);
        _commands.SetGlobalTexture(WaterId, _water);
        _commands.SetGlobalTexture(NormalId, _normal);

        // Back to the camera's own target for what it draws next.
        _commands.SetRenderTarget(BuiltinRenderTextureType.CameraTarget);
        Graphics.DrawProcedural(surface, Everywhere, MeshTopology.Triangles, 3, 1, camera, null, ShadowCastingMode.Off, false, 0);
        ParticleSurface.Froth(camera, froth);
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
        ParticleSurface.Free(ref _water);
        ParticleSurface.Free(ref _normal);
    }

    // The pictures at the camera's size.
    private void Pictures(int width, int height)
    {
        if (_water != null && _water.width == width && _water.height == height)
        {
            return;
        }

        ParticleSurface.Free(ref _water);
        ParticleSurface.Free(ref _normal);
        _water = ParticleSurface.Make("found", width, height, 0, RenderTextureFormat.ARGBHalf, FilterMode.Point);
        _normal = ParticleSurface.Make("normal", width, height, 0, RenderTextureFormat.ARGBHalf, FilterMode.Point);
    }
}
