using System;
using System.Collections.Generic;
using System.IO;
using FlowingWater.Volume;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// Every placed source's jet in the air within the camera's draw distance (its far clip, as the game's settings set it),
/// drawn as curtains of falling water (the falls' material, FlowingWaterFall.shader) along the arc its water flies on,
/// from its outlet till it meets the ground, a piece or water (<see cref="JetFlight"/>). A source the game has not
/// loaded, too far off, is drawn from what the server says of its jet (<see cref="Heard"/>): where it stands, and where
/// each strip lands as the server flies it, the ground under it not being loaded either. The server pours a jet's water
/// where it lands, and the volume water near the camera flies it only within its grid, too thin there to draw as more
/// than a few drops: each jet is drawn whole, wherever the camera is. Nothing is sent anywhere.
/// </summary>
internal static class JetArcs
{
    // Strips drawn at most (a 10 m outlet is ten), the floats of each (FlowingWaterFall.shader's Curtain), and the rows of
    // each (its ROWS).
    private const int MostStrips = 1024;

    private const int Floats = 16;

    private const int Rows = 20;

    // The jets' arcs are followed again this often (s): a source placed, turned or changed, or the ground under its jet.
    private const float FollowSeconds = 0.25f;

    /// <summary>A jet leaves its outlet this far over its marker's foot (m), as the server pours it (VoxelServer's PourPoint).</summary>
    internal const float OutletLift = 0.3f;

    // What stops a jet: the ground and what is built or stands on it (not characters, nor what lets things through).
    private static readonly string[] SolidLayers = ["Default", "static_solid", "Default_small", "piece", "terrain", "vehicle"];

    private static readonly int CurtainsId = Shader.PropertyToID("_Curtains");
    private static readonly int MostCurtainsId = Shader.PropertyToID("_MostCurtains");
    private static readonly int FaceCameraId = Shader.PropertyToID("_FaceCamera");

    private static readonly FaultGate Gate = new(
        "The jets in the air could not be drawn; they are not drawn until a world opens",
        FlowingWaterPlugin.Log.LogError,
        Stop);

    private static readonly List<(Vector3 At, float Rate, bool Drain, float Radius, SourceShape Shape)> Markers = [];

    private static readonly List<JetFlight.Strip> Cut = [];

    // The strips followed last, with how long each flies, whether it lands and its open ends.
    private static readonly List<(JetFlight.Strip Strip, float Flight, bool Lands, int Open)> Strips = [];

    private static readonly RaycastHit[] Hits = new RaycastHit[8];

    // The jets as the server last told them (a game playing on a server; one hosting reads its own server's).
    private static List<ServerJet> _heard = [];

    // Of the strips followed last, how many are the server's (sources not loaded).
    private static int _fromServer;

    private static readonly float[] Data = new float[MostStrips * Floats];

    private static readonly MaterialPropertyBlock Properties = new();

    private static ComputeBuffer _buffer;

    private static float _followed = float.NegativeInfinity;

    private static int _mask;

    /// <summary>A frame: the jets followed again now and then, and drawn.</summary>
    public static void Tick() => Gate.Run(TickOn);

    /// <summary>A world opens: off after a fault in the last one, they have a fresh try.</summary>
    public static void WorldOpened()
    {
        Gate.Reset();
        Stop();
        _heard = [];
    }

    /// <summary>
    /// The server told its jets (<see cref="JetNews"/>): they are followed again at once. Bytes that are not its jets are
    /// logged, and the jets heard before kept.
    /// </summary>
    public static void Heard(byte[] data)
    {
        try
        {
            _heard = JetNews.Read(data);
            _followed = float.NegativeInfinity;
        }
        catch (InvalidDataException e)
        {
            FlowingWaterPlugin.Log.LogError($"The server's jets could not be read; the far ones are drawn as last told: {e}");
        }
    }

    /// <summary>The jets drawn, for the console ("flowingwater jets"): each strip's start, how long it flies and where it ends.</summary>
    public static IEnumerable<string> Describe()
    {
        if (!Gate.On)
        {
            yield return "flowingwater: the jets in the air are off after a fault (see the log)";
            yield break;
        }

        yield return FormattableString.Invariant($"flowingwater: {Strips.Count} strips of jets in the air drawn, {_fromServer} of them as the server flies them (their sources not loaded here); {(FlowingWaterPlugin.Server?.Jets ?? _heard).Count} jets in the server's word");
        foreach ((JetFlight.Strip strip, float flight, bool lands, _) in Strips)
        {
            Vec3 end = strip.At(flight);
            yield return FormattableString.Invariant($"  from {strip.Start.X:0.0},{strip.Start.Y:0.0},{strip.Start.Z:0.0} at {strip.Velocity.X:0.0},{strip.Velocity.Y:0.0},{strip.Velocity.Z:0.0} m/s, {strip.Width:0.##} m wide: {flight:0.00} s to {end.X:0.0},{end.Y:0.0},{end.Z:0.0} ({(lands ? "lands" : "still falling")})");
        }
    }

    private static void TickOn()
    {
        Camera camera = GameCamera.instance != null ? GameCamera.instance.GetComponent<Camera>() : Camera.main;
        Settings settings = FlowingWaterPlugin.Settings;
        if (ZNet.instance == null || ZNet.instance.IsDedicated() || !settings.Enabled.Value || !settings.CustomShader.Value || camera == null
            || !Materials.Spray(out _, out _) || Materials.Falls is not { } material)
        {
            Stop();
            return;
        }

        if (Time.time - _followed >= FollowSeconds)
        {
            _followed = Time.time;
            Follow(camera.transform.position, camera.farClipPlane);
            if (Strips.Count > 0)
            {
                _buffer ??= new ComputeBuffer(MostStrips, Floats * sizeof(float));
                _buffer.SetData(Data, 0, 0, Strips.Count * Floats);
            }
        }

        if (Strips.Count == 0)
        {
            return;
        }

        Properties.SetBuffer(CurtainsId, _buffer);
        Properties.SetInt(MostCurtainsId, Strips.Count);
        Properties.SetFloat(FaceCameraId, 1f);
        float reach = camera.farClipPlane;
        Graphics.DrawProcedural(material, new Bounds(camera.transform.position, Vector3.one * (2f * reach)), MeshTopology.Triangles, (Rows - 1) * 6, Strips.Count, null, Properties);
    }

    // Each flying source's strips within the draw distance, followed down till they meet something.
    private static void Follow(Vector3 camera, float reach)
    {
        if (_mask == 0)
        {
            _mask = LayerMask.GetMask(SolidLayers);
        }

        Strips.Clear();
        Marker.Markers(Markers);
        float voxel = VoxelWater.Field.VoxelSize > 0f ? VoxelWater.Field.VoxelSize : 1f;
        foreach ((Vector3 at, float rate, bool drain, _, SourceShape shape) in Markers)
        {
            if (drain || !shape.Flies || (at - camera).sqrMagnitude > reach * reach)
            {
                continue;
            }

            JetFlight.Strips(new Vec3(at.x, at.y + OutletLift, at.z), shape, rate, voxel, Cut);
            for (int k = 0; k < Cut.Count && Strips.Count < MostStrips; k++)
            {
                float flight = JetFlight.Flight(Cut[k], Meets, out bool lands);
                // Open at the outlet's ends: the first strip's left, the last one's right (none beside them).
                int open = (k == 0 ? 1 : 0) + (k == Cut.Count - 1 ? 2 : 0);
                Add(Cut[k], flight, lands, open);
            }
        }

        _fromServer = Strips.Count;
        FromServer(camera, reach, voxel);
        _fromServer = Strips.Count - _fromServer;
    }

    // The server's jets whose sources are not loaded here, within the draw distance: each strip flies to where the
    // server lands it (a strip pouring at its outlet is not drawn). Its places are the server's: where the voxels' sizes
    // differ, each strip takes the landing of the place at its share across the outlet.
    private static void FromServer(Vector3 camera, float reach, float voxel)
    {
        foreach (ServerJet jet in FlowingWaterPlugin.Server?.Jets ?? _heard)
        {
            var foot = new Vector3(jet.Foot.X, jet.Foot.Y, jet.Foot.Z);
            if (jet.Landings.Length == 0 || (foot - camera).sqrMagnitude > reach * reach || Loaded(foot))
            {
                continue;
            }

            JetFlight.Strips(new Vec3(foot.x, foot.y + OutletLift, foot.z), jet.Shape, jet.Rate, voxel, Cut);
            for (int k = 0; k < Cut.Count && Strips.Count < MostStrips; k++)
            {
                if (jet.Landings[k * jet.Landings.Length / Cut.Count] is { } landing)
                {
                    Add(Cut[k], JetFlight.FlightTo(Cut[k], landing), true, (k == 0 ? 1 : 0) + (k == Cut.Count - 1 ? 2 : 0));
                }
            }
        }
    }

    // Whether a source stands at a foot among the markers loaded here.
    private static bool Loaded(Vector3 foot)
    {
        foreach ((Vector3 at, _, bool drain, _, _) in Markers)
        {
            if (!drain && (at - foot).sqrMagnitude < 0.01f)
            {
                return true;
            }
        }

        return false;
    }

    // A strip drawn, if it flies at all.
    private static void Add(JetFlight.Strip strip, float flight, bool lands, int open)
    {
        if (flight > 0f)
        {
            Write(Strips.Count, strip, flight, lands, open);
            Strips.Add((strip, flight, lands, open));
        }
    }

    // The share of the way from one place to the next where it first meets the ground, a piece, the sea or the
    // flowing water's top; null where it meets nothing. A source's own marker is passed through.
    private static float? Meets(Vec3 from, Vec3 to)
    {
        var a = new Vector3(from.X, from.Y, from.Z);
        var b = new Vector3(to.X, to.Y, to.Z);
        Vector3 way = b - a;
        float length = way.magnitude;
        float? share = null;
        if (length > 1e-4f)
        {
            int count = Physics.RaycastNonAlloc(a, way / length, Hits, length, _mask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                if (Hits[i].collider.GetComponentInParent<Marker>() == null)
                {
                    share = Math.Min(share ?? 1f, Hits[i].distance / length);
                }
            }
        }

        float sea = ZoneSystem.instance != null ? ZoneSystem.instance.m_waterLevel : float.NegativeInfinity;
        float top = Math.Max(sea, VoxelWater.SurfaceAt(b));
        if (b.y < top && a.y > b.y)
        {
            share = Math.Min(share ?? 1f, Mathf.Clamp01((a.y - top) / (a.y - b.y)));
        }

        return share;
    }

    // A strip as FlowingWaterFall.shader's Curtain: start and flight; velocity and the water across it; across it (to its
    // left, along the ground), its width and open ends; the water's depth (none), whether it lands, how fast its sides
    // move out, and nothing more.
    private static void Write(int k, JetFlight.Strip strip, float flight, bool lands, int open)
    {
        int i = k * Floats;
        float flat = (float)Math.Sqrt((strip.Velocity.X * strip.Velocity.X) + (strip.Velocity.Z * strip.Velocity.Z));
        float leftX = flat > 1e-4f ? -strip.Velocity.Z / flat : 1f, leftZ = flat > 1e-4f ? strip.Velocity.X / flat : 0f;
        Data[i] = strip.Start.X;
        Data[i + 1] = strip.Start.Y;
        Data[i + 2] = strip.Start.Z;
        Data[i + 3] = flight;
        Data[i + 4] = strip.Velocity.X;
        Data[i + 5] = strip.Velocity.Y;
        Data[i + 6] = strip.Velocity.Z;
        Data[i + 7] = strip.Flow;
        Data[i + 8] = leftX;
        Data[i + 9] = leftZ;
        Data[i + 10] = strip.Width;
        Data[i + 11] = open;
        Data[i + 12] = 0f;
        Data[i + 13] = lands ? 1f : 0f;
        Data[i + 14] = strip.Widen;
        Data[i + 15] = 0f;
    }

    private static void Stop()
    {
        Strips.Clear();
        _followed = float.NegativeInfinity;
        _buffer?.Release();
        _buffer = null;
    }
}
