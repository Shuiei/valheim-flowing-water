using System;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The falls, splashes and spray of the water the graphics card moves (<see cref="SimGrid"/>), as particles on the card
/// (the mod's FlowingWaterSpray compute shader): fast water throws droplets, water running over a lip leaves it as
/// sheets that fall and arc at its own speed, a sheet landing fast throws up a splash. Only the look: no water is taken
/// from the sheet for them, nor sent anywhere. Needs only Unity, so the Unity project's preview runs it too.
/// </summary>
internal sealed class SprayParticles : IDisposable
{
    /// <summary>Particles at most (the oldest give way to new ones).</summary>
    public const int Count = 32768;

    // A particle's floats: place and age, velocity and life, size and kind.
    private const int Stride = 12 * sizeof(float);

    // Rapids: water faster than this throws droplets (m/s), so many a second for each m/s more over each square metre.
    private const float SprayFrom = 3.5f;

    private const float SprayRate = 4f;

    // Lips: ground this much lower than the ground beside it is a fall (m); so many sheets a second for each cubic metre
    // a second going over it.
    private const float LipDrop = 0.6f;

    private const float SheetRate = 60f;

    // Falls (the server's water falling from high): so many sheets a second for each square metre they cover.
    private const float FallRate = 30f;

    // Splashes: a sheet landing faster than this (m/s) throws up so many droplets.
    private const float SplashFrom = 3f;

    private const int SplashDroplets = 3;

    private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
    private static readonly int NextId = Shader.PropertyToID("_Next");
    private static readonly int CountId = Shader.PropertyToID("_Count");
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
    private static readonly int SprayRateId = Shader.PropertyToID("_SprayRate");
    private static readonly int LipDropId = Shader.PropertyToID("_LipDrop");
    private static readonly int SheetRateId = Shader.PropertyToID("_SheetRate");
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

    /// <summary>Particles moved by <paramref name="shader"/> and drawn with <paramref name="material"/> (copied: it holds the buffer).</summary>
    public SprayParticles(ComputeShader shader, Material material)
    {
        _shader = shader;
        Material = new Material(material) { name = material.name + " particles" };
        _emit = shader.FindKernel("EmitFromWater");
        _move = shader.FindKernel("Move");
        _particles = new ComputeBuffer(Count, Stride);
        _particles.SetData(new float[Count * 12]);
        _next = new ComputeBuffer(1, sizeof(uint));
        _next.SetData(new uint[1]);
        Material.SetBuffer(ParticlesId, _particles);
    }

    /// <summary>What they are drawn with: <see cref="Count"/> times six points (two triangles each), the particles' own buffer.</summary>
    public Material Material { get; }

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
        _shader.SetInt(NId, grid.Cells);
        _shader.SetFloat(DxId, grid.CellSize);
        _shader.SetVector(OriginId, origin);
        _shader.SetFloat(DtId, seconds);
        _shader.SetFloat(GravityId, SimGrid.Gravity);
        _shader.SetInt(SeedId, (int)_seed);
        _shader.SetFloat(SprayFromId, SprayFrom);
        _shader.SetFloat(SprayRateId, SprayRate);
        _shader.SetFloat(LipDropId, LipDrop);
        _shader.SetFloat(SheetRateId, SheetRate);
        _shader.SetFloat(SplashFromId, SplashFrom);
        _shader.SetInt(SplashDropletsId, SplashDroplets);
        _shader.SetInt(PerVoxelId, grid.PerVoxel);
        _shader.SetFloat(FallRateId, FallRate);
        _shader.SetTexture(_emit, LayersId, grid.Layers);
        foreach (int kernel in new[] { _emit, _move })
        {
            _shader.SetBuffer(kernel, ParticlesId, _particles);
            _shader.SetBuffer(kernel, NextId, _next);
            _shader.SetTexture(kernel, HId, grid.Depth);
            _shader.SetTexture(kernel, BedId, grid.Bed);
            _shader.SetTexture(kernel, UId, grid.SpeedEast);
            _shader.SetTexture(kernel, VId, grid.SpeedNorth);
        }

        _shader.Dispatch(_emit, (grid.Cells + 7) / 8, (grid.Cells + 7) / 8, 1);
        _shader.Dispatch(_move, Count / 64, 1, 1);
    }

    /// <summary>How many are alive, and how many of them are sheets (read back from the card: for previews and tests).</summary>
    public int Alive(out int sheets)
    {
        float[] data = new float[Count * 12];
        _particles.GetData(data);
        int alive = 0;
        sheets = 0;
        for (int k = 0; k < Count; k++)
        {
            if (data[(k * 12) + 3] < data[(k * 12) + 7])
            {
                alive++;
                sheets += Math.Abs(data[(k * 12) + 9] - 1f) < 0.5f ? 1 : 0;
            }
        }

        return alive;
    }

    /// <summary>Draws them this frame, for every camera, within these bounds (world).</summary>
    public void Draw(Bounds bounds) => Graphics.DrawProcedural(Material, bounds, MeshTopology.Triangles, Count * 6);

    public void Dispose()
    {
        _particles.Release();
        _next.Release();
        UnityEngine.Object.Destroy(Material);
    }
}
