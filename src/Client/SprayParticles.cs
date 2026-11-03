using System;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The falls, splashes and spray of the water the graphics card moves (<see cref="SimGrid"/>), as particles on the card
/// (the mod's FlowingWaterSpray compute shader): fast water throws droplets, water running over a lip leaves it as
/// sheets that fall and arc at its own speed carrying it (the card's water thrown, <see cref="SimGrid.Airborne"/>), and
/// add it to the water where they land; a sheet landing fast throws up a splash. A lip's water is drawn as one curtain
/// falling along the arc it is thrown on to where it lands (<see cref="Curtain"/>), its sheets faint, the curtain's
/// mist. With <see cref="Froth"/> on, the droplets are sorted into spray, foam and bubbles by where they are against the
/// water (Ihmsen et al. 2012's diffuse particles), and water slamming into slower water traps air. Nothing is sent
/// anywhere. Needs only Unity, so the Unity project's preview runs it too.
/// </summary>
internal sealed class SprayParticles : IDisposable
{
    /// <summary>Particles at most (the oldest give way to new ones, each in its own ring).</summary>
    public const int Count = 49152;

    // Of them, places kept for the sheets carrying the card's water (a ring of their own: a droplet never takes the place
    // of one still in the air, and one that gives way lands its water where it is).
    private const int Carriers = 8192;

    // And places kept for the froth thrown as foam or bubbles (a ring of their own: a fall's plunge or trapped air never
    // takes the place of spray, splashes or falling sheets, only of older froth).
    private const int FrothRoom = 16384;

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

    // Froth: trapped air (FlowingWaterTrapped.cginc, the card's foam's rule too) lets so many particles go a second over
    // each square metre where the water traps all it will. A sheet plunging fast into a pool drives so many bubbles down.
    private const float TrappedRate = 60f;

    private const int PlungeBubbles = 4;

    // The crowd count's hashed cells (FlowingWaterSpray.compute's CROWD_CELLS).
    private const int CrowdCells = 65536;

    // Curtains at most a frame (faces of cells over a lip: a kilometre of lip), the rows of each (FlowingWaterFall.shader's
    // ROWS), and a curtain's floats.
    private const int MostCurtains = 4096;

    private const int CurtainRows = 20;

    private const int CurtainStride = 16 * sizeof(float);

    private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
    private static readonly int NextId = Shader.PropertyToID("_Next");
    private static readonly int CountId = Shader.PropertyToID("_Count");
    private static readonly int CarriersId = Shader.PropertyToID("_Carriers");
    private static readonly int FrothRoomId = Shader.PropertyToID("_FrothRoom");
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
    private static readonly int CurtainsId = Shader.PropertyToID("_Curtains");
    private static readonly int MostCurtainsId = Shader.PropertyToID("_MostCurtains");
    private static readonly int TargetId = Shader.PropertyToID("_Target");
    private static readonly int BorderId = Shader.PropertyToID("_Border");
    private static readonly int FrothId = Shader.PropertyToID("_Froth");
    private static readonly int TrappedRateId = Shader.PropertyToID("_TrappedRate");
    private static readonly int PlungeBubblesId = Shader.PropertyToID("_PlungeBubbles");
    private static readonly int CrowdId = Shader.PropertyToID("_Crowd");
    private static readonly int FoamAddedId = Shader.PropertyToID("_FoamAdded");
    private static readonly int FrothCrowdId = Shader.PropertyToID("_FrothCrowd");
    private static readonly int WaterDepthId = Shader.PropertyToID("_SprayWaterDepth");
    private static readonly int WaterRippleId = Shader.PropertyToID("_SprayWaterRipple");
    private static readonly int WaterOriginId = Shader.PropertyToID("_SprayWaterOrigin");
    private static readonly int WaterCellId = Shader.PropertyToID("_SprayWaterCell");
    private static readonly int WaterCellsId = Shader.PropertyToID("_SprayWaterCells");

    private readonly ComputeShader _shader;

    private readonly int _emit;

    private readonly int _move;

    private readonly int _curtains;

    private readonly int _crowdClear;

    private readonly ComputeBuffer _particles;

    private readonly ComputeBuffer _next;

    // Spray, foam and bubbles counted in hashed cells each step (the shader draws spray in a crowd as mist).
    private readonly ComputeBuffer _crowd;

    // The froth counted apart in the same cells (foam and bubbles: a cell full of it takes no more).
    private readonly ComputeBuffer _frothCrowd;

    // Whether the froth was on at the last step (turned off, one more step runs over its ring, ending what is left).
    private bool _frothWasOn;

    // The curtains this frame (their arguments drawn with: CurtainArguments).
    private readonly ComputeBuffer _curtainList;

    private uint _seed;

    /// <summary>Particles moved by <paramref name="shader"/> and drawn with <paramref name="material"/>.</summary>
    public SprayParticles(ComputeShader shader, Material material)
    {
        _shader = shader;
        Material = material;
        _emit = shader.FindKernel("EmitFromWater");
        _move = shader.FindKernel("Move");
        _curtains = shader.FindKernel("Curtains");
        _crowdClear = shader.FindKernel("CrowdClear");
        _particles = new ComputeBuffer(Count, Stride);
        _particles.SetData(new float[Count * 12]);
        _next = new ComputeBuffer(3, sizeof(uint));
        _next.SetData(new uint[3]);
        Properties.SetBuffer(ParticlesId, _particles);
        _crowd = new ComputeBuffer(CrowdCells, sizeof(uint));
        _crowd.SetData(new uint[CrowdCells]);
        Properties.SetBuffer(CrowdId, _crowd);
        _frothCrowd = new ComputeBuffer(CrowdCells, sizeof(uint));
        _frothCrowd.SetData(new uint[CrowdCells]);
        _curtainList = new ComputeBuffer(MostCurtains, CurtainStride, ComputeBufferType.Counter);
        CurtainArguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
        CurtainArguments.SetData(new uint[] { (CurtainRows - 1) * 6, 0, 0, 0 });
        CurtainProperties.SetBuffer(CurtainsId, _curtainList);
        CurtainProperties.SetInt(MostCurtainsId, MostCurtains);
    }

    /// <summary>
    /// What they are drawn with: <see cref="Count"/> times six points (two triangles each), with <see cref="Properties"/>.
    /// The mod's spray material itself (not a copy), so the sea's look taken onto it later shows on them too.
    /// </summary>
    public Material Material { get; }

    /// <summary>The particles' own buffer and crowd count, for the material.</summary>
    public MaterialPropertyBlock Properties { get; } = new();

    /// <summary>
    /// Whether the droplets are sorted into spray, foam and bubbles and the water traps air (true), or droplets end
    /// where they land (false).
    /// </summary>
    public bool Froth { get; set; } = true;

    /// <summary>
    /// The particles stepped and drawn: all of them, or with the froth off (once what was left of it has ended) all but
    /// the froth's own ring, the last places, which then hold nothing.
    /// </summary>
    public int Stepped => Froth || _frothWasOn ? Count : Count - FrothRoom;

    /// <summary>
    /// What the curtains are drawn with (the mod's falls material, FlowingWaterFall.shader), none where it has none:
    /// <see cref="CurtainArguments"/> gives how many and their points, <see cref="CurtainProperties"/> their buffer.
    /// </summary>
    public Material Curtain { get; set; }

    /// <summary>The curtains' buffer, for their material.</summary>
    public MaterialPropertyBlock CurtainProperties { get; } = new();

    /// <summary>The curtains' indirect drawing arguments: points a curtain, curtains this frame (counted on the card).</summary>
    public ComputeBuffer CurtainArguments { get; }

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
        _shader.SetInt(FrothRoomId, FrothRoom);
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
        _shader.SetInt(FrothId, Froth ? 1 : 0);
        _shader.SetFloat(TrappedRateId, TrappedRate);
        _shader.SetInt(PlungeBubblesId, PlungeBubbles);
        Properties.SetInt(FrothId, Froth ? 1 : 0);
        _shader.SetTexture(_emit, LayersId, grid.Layers);
        _shader.SetTexture(_emit, AirborneId, grid.Airborne);
        Bind(_emit, grid);
        Bind(_move, grid);
        _shader.SetBuffer(_crowdClear, CrowdId, _crowd);
        _shader.SetBuffer(_crowdClear, FrothCrowdId, _frothCrowd);

        // The water's froth is thrown where last step's crowd leaves room, then counted again as it moves (with the froth
        // off, nothing is counted: no crowd to clear, and its ring stepped only the step it was turned off).
        _shader.Dispatch(_emit, (grid.Cells + 7) / 8, (grid.Cells + 7) / 8, 1);
        if (Froth)
        {
            _shader.Dispatch(_crowdClear, CrowdCells / 64, 1, 1);
        }

        _shader.Dispatch(_move, Stepped / 64, 1, 1);
        _frothWasOn = Froth;

        // What the shader raises foam on: the water's depth, its cells and where they start (the long waves stand on it).
        Properties.SetTexture(WaterDepthId, grid.Depth);
        Properties.SetTexture(WaterRippleId, grid.Ripple);
        Properties.SetVector(WaterOriginId, origin);
        Properties.SetFloat(WaterCellId, grid.CellSize);
        Properties.SetInt(WaterCellsId, grid.Cells);

        // The curtains of the water as it is now (after the particles took what they carry from it).
        _curtainList.SetCounterValue(0);
        _shader.SetInt(MostCurtainsId, MostCurtains);
        _shader.SetInt(BorderId, SimGrid.BorderCells);
        _shader.SetBuffer(_curtains, CurtainsId, _curtainList);
        _shader.SetTexture(_curtains, TargetId, grid.Target);
        _shader.SetTexture(_curtains, HId, grid.Depth);
        _shader.SetTexture(_curtains, BedId, grid.Bed);
        _shader.SetTexture(_curtains, UId, grid.SpeedEast);
        _shader.SetTexture(_curtains, VId, grid.SpeedNorth);
        _shader.Dispatch(_curtains, (grid.Cells + 7) / 8, (grid.Cells + 7) / 8, 1);
        ComputeBuffer.CopyCount(_curtainList, CurtainArguments, sizeof(uint));
    }

    /// <summary>
    /// The curtains of the last step, sixteen floats each (FlowingWaterSpray.compute's Curtain: start and fall time,
    /// velocity and flow, along the lip with width and open ends, depth and whether it lands), read back from the card:
    /// for previews and tests.
    /// </summary>
    public float[] ReadCurtains()
    {
        uint[] arguments = new uint[4];
        CurtainArguments.GetData(arguments);
        float[] data = new float[Math.Min((int)arguments[1], MostCurtains) * 16];
        _curtainList.GetData(data, 0, 0, data.Length);
        return data;
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

    /// <summary>
    /// How many are alive of each kind (droplets, sheets, splashes, foam, bubbles: FlowingWaterSpray.compute's kinds), read
    /// back from the card: for previews and tests.
    /// </summary>
    public int[] AliveByKind()
    {
        float[] data = Read();
        int[] kinds = new int[5];
        for (int k = 0; k < Count; k++)
        {
            int kind = (int)Math.Round(data[(k * 12) + 9]);
            if (data[(k * 12) + 3] < data[(k * 12) + 7] && kind >= 0 && kind < kinds.Length)
            {
                kinds[kind]++;
            }
        }

        return kinds;
    }

    /// <summary>Draws them this frame, for every camera, within these bounds (world), and the curtains if they have a material.</summary>
    public void Draw(Bounds bounds)
    {
        Graphics.DrawProcedural(Material, bounds, MeshTopology.Triangles, Stepped * 6, 1, null, Properties);
        if (Curtain != null)
        {
            Graphics.DrawProceduralIndirect(Curtain, bounds, MeshTopology.Triangles, CurtainArguments, 0, null, CurtainProperties);
        }
    }

    // What both kernels read: the particles, the rings' next places, the card's water, where water lands (a carrier giving
    // way to a new one lands its water).
    private void Bind(int kernel, SimGrid grid)
    {
        _shader.SetBuffer(kernel, ParticlesId, _particles);
        _shader.SetBuffer(kernel, NextId, _next);
        _shader.SetBuffer(kernel, CrowdId, _crowd);
        _shader.SetBuffer(kernel, FrothCrowdId, _frothCrowd);
        _shader.SetTexture(kernel, HId, grid.Depth);
        _shader.SetTexture(kernel, BedId, grid.Bed);
        _shader.SetTexture(kernel, UId, grid.SpeedEast);
        _shader.SetTexture(kernel, VId, grid.SpeedNorth);
        _shader.SetTexture(kernel, LandedId, grid.Landed);
        _shader.SetTexture(kernel, LandedXId, grid.LandedEast);
        _shader.SetTexture(kernel, LandedZId, grid.LandedNorth);
        _shader.SetTexture(kernel, FoamAddedId, grid.FoamAdded);
    }

    public void Dispose()
    {
        _particles.Release();
        _next.Release();
        _crowd.Release();
        _frothCrowd.Release();
        _curtainList.Release();
        CurtainArguments.Release();
    }
}
