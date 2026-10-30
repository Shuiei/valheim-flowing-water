using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The reflection of the scene about the camera, for what is drawn over the whole screen (the volume and particle
/// water's surface, the froth): not a renderer, it is given no reflection probe and mirrored the plain sky (Unity's
/// default), where the bricks' water about it mirrors the game's own probes there (the trees, the clouds, the light of the
/// hour). The probe whose box holds the camera, the widest of them (the game's two take turns, the next growing in).
/// </summary>
internal static class SceneReflection
{
    // Seconds between two looks for the scene's probes.
    private const float LookEvery = 2f;

    private static readonly int CubeId = Shader.PropertyToID("unity_SpecCube0");

    private static readonly int CubeHdrId = Shader.PropertyToID("unity_SpecCube0_HDR");

    // Its box (w of the lower corner: its share against the second probe's, which the draw is given none of: unset, the
    // lighting took all of the second, the plain sky) and its middle.
    private static readonly int BoxMinId = Shader.PropertyToID("unity_SpecCube0_BoxMin");

    private static readonly int BoxMaxId = Shader.PropertyToID("unity_SpecCube0_BoxMax");

    private static readonly int ProbePositionId = Shader.PropertyToID("unity_SpecCube0_ProbePosition");

    // The scene's probes, looked for at the first draw (_lookedAt starts long ago) and again now and then. No collection
    // expression: the Unity project compiles this file too (tools/sim-preview.sh), in C# 9.
    private static ReflectionProbe[] _probes;

    private static float _lookedAt = float.NegativeInfinity;

    /// <summary>The probe about a place set into the properties a draw is given (ProbePick); none there, Unity's default.</summary>
    public static void Into(MaterialPropertyBlock properties, Vector3 at)
    {
        if (Time.time - _lookedAt > LookEvery)
        {
            _probes = Object.FindObjectsByType<ReflectionProbe>(FindObjectsSortMode.None);
            _lookedAt = Time.time;
        }

        // Unity's own null test: a probe gone with its world is null to it.
        int pick = ProbePick.Widest(_probes.Length, k => _probes[k] != null && _probes[k].isActiveAndEnabled && _probes[k].texture != null && _probes[k].bounds.Contains(at), k => _probes[k].size.x * _probes[k].size.z);

        // None about the place: Unity's default, as the draw would have with no properties (a block given a probe once
        // keeps it, and would mirror that probe's scene wherever the camera went).
        Texture cube = pick < 0 ? ReflectionProbe.defaultTexture : _probes[pick].texture;
        Vector4 hdr = pick < 0 ? ReflectionProbe.defaultTextureHDRDecodeValues : _probes[pick].textureHDRDecodeValues;
        Bounds box = pick < 0 ? new Bounds(at, Vector3.zero) : _probes[pick].bounds;
        Vector3 middle = pick < 0 ? at : _probes[pick].transform.position;
        properties.SetTexture(CubeId, cube);
        properties.SetVector(CubeHdrId, hdr);
        properties.SetVector(BoxMinId, new Vector4(box.min.x, box.min.y, box.min.z, 1f));
        properties.SetVector(BoxMaxId, new Vector4(box.max.x, box.max.y, box.max.z, 1f));
        properties.SetVector(ProbePositionId, new Vector4(middle.x, middle.y, middle.z, pick >= 0 && _probes[pick].boxProjection ? 1f : 0f));
    }
}
