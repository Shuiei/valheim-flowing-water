using System;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The falls, splashes and spray of the water the graphics card moves (<see cref="SimGrid"/>), as particles on the card
/// (the mod's FlowingWaterSpray compute shader): fast water throws droplets, water running over a lip leaves it as
/// sheets that fall and arc at its own speed carrying it (the card's water thrown, <see cref="SimGrid.Airborne"/>), and
/// add it to the water where they land; a sheet landing fast throws up a splash. Nothing is sent anywhere. Needs only
/// Unity, so the Unity project's preview runs it too.
/// </summary>
internal sealed class SprayParticles : IDisposable
{
    /// <summary>Particles at most (the oldest give way to new ones).</summary>
    public const int Count = 32768;

    // Of them, places kept for the sheets carrying the card's water (a ring of their own: a droplet never takes the place
    // of one still in the air, and one that gives way lands its water where it is).
    private const int Carriers = 8192;

    // A particle's floats: place and age, velocity and life, size and kind.
    private const int Stride = 12 * sizeof(float);

    // Rapids: water at least this deep (m) and faster than this (m/s) throws droplets, so many a second for each m/s more
    // over each square metre (a thin sheet running fast down a slope throws none).
    private const float SprayDepth = 0.1f;

    private const float SprayFrom = 4.5f;

    private const float SprayRate = 1.5f;

    // Lips: a sheet of falling water for each so much water thrown over one (m³).
    private const float SheetVolume = 1f / 60f;

    // Falls (the server's water falling from high): so many sheets a second for each square metre they cover.
    private const float FallRate = 30f;

    // Splashes: a sheet landing faster than this (m/s) throws up so many droplets.
    private const float SplashFrom = 3f;

    private const int SplashDroplets = 3;

    private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
    private static readonly int NextId = Shader.PropertyToID("_Next");
    private static readonly int CountId = Shader.PropertyToID("_Count");
    private static readonly int CarriersId = Shader.PropertyToID("_Carriers");
    private static readonly int HId = Shader.PropertyToID("_H");
    private static readonly int BedId = Shader.PropertyToID("_Bed");
    private static readonly int UId = Shader.PropertyToID("_U");
    private static readonly int VId = Shader.PropertyToID("_V");
    private static readonly int NId = Shader.PropertyToID("_N");
    private static readonly int DxId = Shader.PropertyToID("_Dx");
    private static readonly int OriginId = Shader.PropertyToID("_Origin");
    private static readonly int DtId = Shader.PropertyToID("_Dt");
    private static readonly int GravityId = Shader.PropertyToID("_Gravity");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int SprayFromId = Shader.PropertyToID("_SprayFrom");
    private static readonly int SprayDepthId = Shader.PropertyToID("_SprayDepth");
    private static readonly int SprayRateId = Shader.PropertyToID("_SprayRate");
    private static readonly int LipDropId = Shader.PropertyToID("_LipDrop");
    private static readonly int SheetVolumeId = Shader.PropertyToID("_SheetVolume");
    private static readonly int AirborneId = Shader.PropertyToID("_Airborne");
    private static readonly int LandedId = Shader.PropertyToID("_Landed");
    private static readonly int LandedXId = Shader.PropertyToID("_LandedX");
    private static readonly int LandedZId = Shader.PropertyToID("_LandedZ");
    private static readonly int SplashFromId = Shader.PropertyToID("_SplashFrom");
    private static readonly int SplashDropletsId = Shader.PropertyToID("_SplashDroplets");
    private static readonly int LayersId = Shader.PropertyToID("_Layers");
    private static readonly int PerVoxelId = Shader.PropertyToID("_PerVoxel");
    private static readonly int FallRateId = Shader.PropertyToID("_FallRate");

    private readonly ComputeShader _shader;

    private readonly int _emit;

    private readonly int _move;

    private readonly ComputeBuffer _particles;

    private readonly ComputeBuffer _next;

    private uint _seed;

    /// <summary>Particles moved by <paramref name="shader"/> and drawn with <paramref name="material"/>.</summary>
    public SprayParticles(ComputeShader shader, Material material)
    {
        _shader = shader;
        Material = material;
        _emit = shader.FindKernel("EmitFromWater");
        _move = shader.FindKernel("Move");
        _particles = new ComputeBuffer(Count, Stride);
        _particles.SetData(new float[Count * 12]);
        _next = new ComputeBuffer(2, sizeof(uint));
        _next.SetData(new uint[2]);
        Properties.SetBuffer(ParticlesId, _particles);
    }

    /// <summary>
    /// What they are drawn with: <see cref="Count"/> times six points (two triangles each), with <see cref="Properties"/>.
    /// The mod's spray material itself (not a copy), so the sea's look taken onto it later shows on them too.
    /// </summary>
    public Material Material { get; }

    /// <summary>The particles' own buffer, for the material.</summary>
    public MaterialPropertyBlock Properties { get; } = new();

    /// <summary>
    /// Throws new particles from the card's water (its square's south-west corner at world x, z <paramref name="origin"/>)
    /// and moves them all over this much time.
    /// </summary>
    public void Step(SimGrid grid, Vector2 origin, float seconds)
    {
        if (seconds <= 0f)
        {
            return;
        }

        _seed++;
        _shader.SetInt(CountId, Count);
        _shader.SetInt(CarriersId, Carriers);
        _shader.SetInt(NId, grid.Cells);
        _shader.SetFloat(DxId, grid.CellSize);
        _shader.SetVector(OriginId, origin);
        _shader.SetFloat(DtId, seconds);
        _shader.SetFloat(GravityId, SimGrid.Gravity);
        _shader.SetInt(SeedId, (int)_seed);
        _shader.SetFloat(SprayFromId, SprayFrom);
        _shader.SetFloat(SprayDepthId, SprayDepth);
        _shader.SetFloat(SprayRateId, SprayRate);
        _shader.SetFloat(LipDropId, SimGrid.LipDrop);
        _shader.SetFloat(SheetVolumeId, SheetVolume);
        _shader.SetFloat(SplashFromId, SplashFrom);
        _shader.SetInt(SplashDropletsId, SplashDroplets);
        _shader.SetInt(PerVoxelId, grid.PerVoxel);
        _shader.SetFloat(FallRateId, FallRate);
        _shader.SetTexture(_emit, LayersId, grid.Layers);
        _shader.SetTexture(_emit, AirborneId, grid.Airborne);
        Bind(_emit, grid);
        Bind(_move, grid);

        _shader.Dispatch(_emit, (grid.Cells + 7) / 8, (grid.Cells + 7) / 8, 1);
        _shader.Dispatch(_move, Count / 64, 1, 1);
    }

    /// <summary>
    /// Every particle's twelve floats (place and age, velocity and life, size, kind and water carried, one unused), read
    /// back from the card: for previews and tests.
    /// </summary>
    public float[] Read()
    {
        float[] data = new float[Count * 12];
        _particles.GetData(data);
        return data;
    }

    /// <summary>
    /// How many are alive, how many of them are sheets, and the water they carry (m³; read back from the card: for
    /// previews and tests).
    /// </summary>
    public int Alive(out int sheets, out float water)
    {
        float[] data = new float[Count * 12];
        _particles.GetData(data);
        int alive = 0;
        sheets = 0;
        water = 0f;
        for (int k = 0; k < Count; k++)
        {
            if (data[(k * 12) + 3] < data[(k * 12) + 7])
            {
                alive++;
                sheets += Math.Abs(data[(k * 12) + 9] - 1f) < 0.5f ? 1 : 0;
                water += data[(k * 12) + 10];
            }
        }

        return alive;
    }

    /// <summary>Draws them this frame, for every camera, within these bounds (world).</summary>
    public void Draw(Bounds bounds) => Graphics.DrawProcedural(Material, bounds, MeshTopology.Triangles, Count * 6, 1, null, Properties);

    // What both kernels read: the particles, the rings' next places, the card's water, where water lands (a carrier giving
    // way to a new one lands its water).
    private void Bind(int kernel, SimGrid grid)
    {
        _shader.SetBuffer(kernel, ParticlesId, _particles);
        _shader.SetBuffer(kernel, NextId, _next);
        _shader.SetTexture(kernel, HId, grid.Depth);
        _shader.SetTexture(kernel, BedId, grid.Bed);
        _shader.SetTexture(kernel, UId, grid.SpeedEast);
        _shader.SetTexture(kernel, VId, grid.SpeedNorth);
        _shader.SetTexture(kernel, LandedId, grid.Landed);
        _shader.SetTexture(kernel, LandedXId, grid.LandedEast);
        _shader.SetTexture(kernel, LandedZId, grid.LandedNorth);
    }

    public void Dispose()
    {
        _particles.Release();
        _next.Release();
    }
}
