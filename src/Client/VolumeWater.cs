using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The water near the camera as one body of fluid on the graphics card (the mod's FlowingWaterVolume compute shader):
/// particles carrying the water, each a cube half a cell on a side, over a grid of <see cref="CellSize"/> cells on
/// which a pressure is solved each step so that no cell makes or loses water (FLIP, Zhu and Bridson 2005). Its mass
/// is its own: nothing inside the square is corrected toward the server's water; the server's water is poured in
/// once (<see cref="SeedFromServer"/>) and at the square's edge band, and water leaving the square is the server's.
/// Solid to it: the ground the card's water knows (<see cref="SetGround"/>), what is built (<see cref="SetSolids"/>)
/// and the bodies in the water (<see cref="SetBodies"/>). Sources pour into it and drains take from it
/// (<see cref="SetSprings"/>). Drawn by <see cref="ParticleSurface"/> as the particle prototype is. Needs only Unity,
/// so the Unity project's preview runs it too (VolumePreview).
/// </summary>
internal sealed class VolumeWater : IParticleCloud, IDisposable
{
    /// <summary>Bodies in the water at most.</summary>
    public const int MostBodies = 32;

    /// <summary>Sources and drains at most.</summary>
    public const int MostSprings = 16;

    /// <summary>Particles a cell holds at rest (two a side).</summary>
    public const int Rest = 8;

    private const int Stride = 12 * sizeof(float);

    private const int Threads = 256;

    // A source's water comes out at about this speed (m/s): its reach grows with its rate instead (a big spring boils up
    // over a wide patch); about half its sphere's faces are open to the water (the rest cut to the grid or against
    // the ground), and Regulate scales the speed the rest of the way to the rate.

    private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
    private static readonly int ParticlesInId = Shader.PropertyToID("_ParticlesIn");
    private static readonly int SortedId = Shader.PropertyToID("_Sorted");
    private static readonly int AliveId = Shader.PropertyToID("_Alive");
    private static readonly int AliveInId = Shader.PropertyToID("_AliveIn");
    private static readonly int ArgsId = Shader.PropertyToID("_Args");
    private static readonly int CapacityId = Shader.PropertyToID("_Capacity");
    private static readonly int NxId = Shader.PropertyToID("_Nx");
    private static readonly int NyId = Shader.PropertyToID("_Ny");
    private static readonly int NzId = Shader.PropertyToID("_Nz");
    private static readonly int HId = Shader.PropertyToID("_H");
    private static readonly int OriginId = Shader.PropertyToID("_Origin");
    private static readonly int DtId = Shader.PropertyToID("_Dt");
    private static readonly int GravityId = Shader.PropertyToID("_Gravity");
    private static readonly int FlipId = Shader.PropertyToID("_Flip");
    private static readonly int MaxSpeedId = Shader.PropertyToID("_MaxSpeed");
    private static readonly int DriftId = Shader.PropertyToID("_Drift");
    private static readonly int OpenSidesId = Shader.PropertyToID("_OpenSides");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int ParityId = Shader.PropertyToID("_Parity");
    private static readonly int TypeId = Shader.PropertyToID("_Type");
    private static readonly int TypeInId = Shader.PropertyToID("_TypeIn");
    private static readonly int CountId = Shader.PropertyToID("_Count");
    private static readonly int CountInId = Shader.PropertyToID("_CountIn");
    private static readonly int DivId = Shader.PropertyToID("_Div");
    private static readonly int DivInId = Shader.PropertyToID("_DivIn");
    private static readonly int PId = Shader.PropertyToID("_P");
    private static readonly int PInId = Shader.PropertyToID("_PIn");
    private static readonly int ActiveOutId = Shader.PropertyToID("_ActiveOut");
    private static readonly int ActiveId = Shader.PropertyToID("_Active");
    private static readonly int ActiveArgsId = Shader.PropertyToID("_ActiveArgs");
    private static readonly int ActiveArgsInId = Shader.PropertyToID("_ActiveArgsIn");
    private static readonly int MomId = Shader.PropertyToID("_Mom");
    private static readonly int MomInId = Shader.PropertyToID("_MomIn");
    private static readonly int WgtId = Shader.PropertyToID("_Wgt");
    private static readonly int WgtInId = Shader.PropertyToID("_WgtIn");
    private static readonly int VelId = Shader.PropertyToID("_Vel");
    private static readonly int VelInId = Shader.PropertyToID("_VelIn");
    private static readonly int Vel0Id = Shader.PropertyToID("_Vel0");
    private static readonly int Vel0InId = Shader.PropertyToID("_Vel0In");
    private static readonly int MaskId = Shader.PropertyToID("_Mask");
    private static readonly int Mask2Id = Shader.PropertyToID("_Mask2");
    private static readonly int BedId = Shader.PropertyToID("_Bed");
    private static readonly int BedCellsId = Shader.PropertyToID("_BedCells");
    private static readonly int BedDxId = Shader.PropertyToID("_BedDx");
    private static readonly int BedOriginId = Shader.PropertyToID("_BedOrigin");
    private static readonly int SolidsOnId = Shader.PropertyToID("_SolidsOn");
    private static readonly int SolidsId = Shader.PropertyToID("_Solids");
    private static readonly int SolidColumnsId = Shader.PropertyToID("_SolidColumns");
    private static readonly int SolidRowsId = Shader.PropertyToID("_SolidRows");
    private static readonly int VoxelSizeId = Shader.PropertyToID("_VoxelSize");
    private static readonly int SolidOriginId = Shader.PropertyToID("_SolidOrigin");
    private static readonly int SolidY0Id = Shader.PropertyToID("_SolidY0");
    private static readonly int BodiesId = Shader.PropertyToID("_Bodies");
    private static readonly int BodyCountId = Shader.PropertyToID("_BodyCount");
    private static readonly int SpringsId = Shader.PropertyToID("_Springs");
    private static readonly int SpringCountId = Shader.PropertyToID("_SpringCount");
    private static readonly int TakenId = Shader.PropertyToID("_Taken");
    private static readonly int GivenId = Shader.PropertyToID("_Given");
    private static readonly int GivenInId = Shader.PropertyToID("_GivenIn");
    private static readonly int ScaleId = Shader.PropertyToID("_Scale");
    private static readonly int ScaleInId = Shader.PropertyToID("_ScaleIn");
    private static readonly int TargetId = Shader.PropertyToID("_Target");
    private static readonly int PerVoxelId = Shader.PropertyToID("_PerVoxel");
    private static readonly int SeedBandId = Shader.PropertyToID("_SeedBand");
    private static readonly int SeedShareId = Shader.PropertyToID("_SeedShare");
    private static readonly int PourOnId = Shader.PropertyToID("_PourOn");
    private static readonly int PourMinId = Shader.PropertyToID("_PourMin");
    private static readonly int PourMaxId = Shader.PropertyToID("_PourMax");
    private static readonly int SizeId = Shader.PropertyToID("_Size");
    private static readonly int FrothId = Shader.PropertyToID("_Froth");
    private static readonly int FrothAliveId = Shader.PropertyToID("_FrothAlive");
    private static readonly int FrothCapacityId = Shader.PropertyToID("_FrothCapacity");
    private static readonly int FrothRateId = Shader.PropertyToID("_FrothRate");
    private static readonly int FrothCountId = Shader.PropertyToID("_FrothCount");
    private static readonly int FrothCountInId = Shader.PropertyToID("_FrothCountIn");

    // No particles: the counters and the drawing's arguments.
    private static readonly uint[] NoAlive = new uint[3];

    private static readonly uint[] NoArgs = Counts(0u, 1u, 0u, 0u);

    private readonly ComputeShader _shader;

    private readonly int _clearCells;
    private readonly int _clearFaces;
    private readonly int _scatter;
    private readonly int _gather;
    private readonly int _collect;
    private readonly int _arguments;
    private readonly int _divergence;
    private readonly int _relax;
    private readonly int _project;
    private readonly int _extend;
    private readonly int _move;
    private readonly int _emit;
    private readonly int _regulate;
    private readonly int _compact;
    private readonly int _settle;
    private readonly int _seed;
    private readonly int _frothClear;
    private readonly int _frothMove;

    private readonly ComputeBuffer[] _particles = new ComputeBuffer[2];

    private readonly ComputeBuffer[] _froth = new ComputeBuffer[2];

    private readonly ComputeBuffer _frothAlive;

    private readonly ComputeBuffer _frothCount;

    private readonly ComputeBuffer _alive;
    private readonly ComputeBuffer _type;
    private readonly ComputeBuffer _count;
    private readonly ComputeBuffer _div;
    private readonly ComputeBuffer _pressure;
    private readonly ComputeBuffer _active;
    private readonly ComputeBuffer _activeArgs;
    private readonly ComputeBuffer _mom;
    private readonly ComputeBuffer _wgt;
    private readonly ComputeBuffer _vel;
    private readonly ComputeBuffer _vel0;
    private readonly ComputeBuffer _mask;
    private readonly ComputeBuffer _mask2;
    private readonly ComputeBuffer _bodies;
    private readonly ComputeBuffer _springs;
    private readonly ComputeBuffer _taken;

    private readonly ComputeBuffer _given;

    private readonly ComputeBuffer _scale;

    private readonly Texture2D _noTarget;

    private readonly Texture3D _noSolids;

    private readonly int _cells;

    private readonly int _faces;

    private readonly List<Spring> _springList = new(MostSprings);

    private readonly Counter _counted;

    private readonly Counter _frothCounted;

    private int _current;

    private int _frothCurrent;

    private uint _seedCounter;

    private Texture _bed;

    private int _bedCells;

    private float _bedDx;

    private Vector2 _bedOrigin;

    private Texture _solids;

    private int _solidColumns;

    private int _solidRows;

    private float _voxelSize;

    private Vector2 _solidOrigin;

    private float _solidY0;

    private int _bodyCount;

    private Texture _target;

    private int _perVoxel = 1;

    /// <summary>
    /// Room for <paramref name="capacity"/> particles over a grid of <paramref name="cellsX"/> by <paramref name="cellsY"/>
    /// (up) by <paramref name="cellsZ"/> cells of <paramref name="cellSize"/> metres, from <see cref="Origin"/>; the
    /// froth (a quarter as many particles) drawn with <paramref name="froth"/>, or none made (null).
    /// </summary>
    public VolumeWater(ComputeShader shader, Material material, Material froth, int capacity, int cellsX, int cellsY, int cellsZ, float cellSize)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "no room for particles");
        }

        if (cellsX <= 0 || cellsY <= 0 || cellsZ <= 0 || cellSize <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(cellsX), "the grid must have cells of a size");
        }

        _shader = shader;
        Material = material;
        Capacity = capacity;
        CellsX = cellsX;
        CellsY = cellsY;
        CellsZ = cellsZ;
        CellSize = cellSize;
        _cells = cellsX * cellsY * cellsZ;
        _faces = (cellsX + 1) * (cellsY + 1) * (cellsZ + 1);
        _clearCells = shader.FindKernel("ClearCells");
        _clearFaces = shader.FindKernel("ClearFaces");
        _scatter = shader.FindKernel("Scatter");
        _gather = shader.FindKernel("Gather");
        _collect = shader.FindKernel("Collect");
        _arguments = shader.FindKernel("Arguments");
        _divergence = shader.FindKernel("Divergence");
        _relax = shader.FindKernel("Relax");
        _project = shader.FindKernel("Project");
        _extend = shader.FindKernel("Extend");
        _move = shader.FindKernel("Move");
        _emit = shader.FindKernel("Emit");
        _regulate = shader.FindKernel("Regulate");
        _compact = shader.FindKernel("Compact");
        _settle = shader.FindKernel("Settle");
        _seed = shader.FindKernel("Seed");
        _frothClear = shader.FindKernel("FrothClear");
        _frothMove = shader.FindKernel("FrothMove");
        FrothCapacity = froth != null ? Math.Max(1024, capacity / 4) : 1;
        for (int k = 0; k < 2; k++)
        {
            _particles[k] = new ComputeBuffer(capacity, Stride);
            _froth[k] = new ComputeBuffer(FrothCapacity, Stride);
        }

        _alive = new ComputeBuffer(3, sizeof(uint));
        _frothAlive = new ComputeBuffer(3, sizeof(uint));
        _frothCount = new ComputeBuffer(_cells, sizeof(int));
        _frothCount.SetData(new int[_cells]);
        Arguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
        Froth = new Cloud(froth, new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments), Spacing);
        _counted = new Counter(_alive, capacity);
        _frothCounted = new Counter(_frothAlive, FrothCapacity);
        _type = new ComputeBuffer(_cells, sizeof(int));
        _count = new ComputeBuffer(_cells, sizeof(int));
        _div = new ComputeBuffer(_cells, sizeof(float));
        // Kept from step to step (the relaxation starts from the last step's), so it must start from nothing: a new buffer
        // holds whatever the card's memory last held, which 80 sweeps would not wash out of a big pool.
        _pressure = new ComputeBuffer(_cells, sizeof(float));
        _pressure.SetData(new float[_cells]);
        _active = new ComputeBuffer(_cells, sizeof(uint), ComputeBufferType.Append);
        _activeArgs = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
        _mom = new ComputeBuffer(3 * _faces, sizeof(int));
        _wgt = new ComputeBuffer(3 * _faces, sizeof(int));
        _vel = new ComputeBuffer(3 * _faces, sizeof(float));
        _vel0 = new ComputeBuffer(3 * _faces, sizeof(float));
        _mask = new ComputeBuffer(3 * _faces, sizeof(int));
        _mask2 = new ComputeBuffer(3 * _faces, sizeof(int));

        _bodies = new ComputeBuffer(MostBodies, 16 * sizeof(float));
        _springs = new ComputeBuffer(MostSprings, 8 * sizeof(float));
        _taken = new ComputeBuffer(MostSprings, sizeof(uint));
        _given = new ComputeBuffer(MostSprings, sizeof(uint));
        _scale = new ComputeBuffer(MostSprings, sizeof(float));
        _scale.SetData(new float[MostSprings]);
        _noTarget = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true) { name = "FlowingWater volume no target" };
        _noTarget.SetPixel(0, 0, new Color(-10000f, 0f, 0f, 0f));
        _noTarget.Apply(false);
        _noSolids = new Texture3D(1, 1, 1, TextureFormat.R8, false) { name = "FlowingWater volume no solids" };
        _noSolids.SetPixelData(new byte[1], 0);
        _noSolids.Apply(false);
        Clear();
        Properties.SetBuffer(ParticlesId, _particles[0]);
        Properties.SetFloat(SizeId, 0.8f * Spacing);
        Froth.Properties.SetBuffer(ParticlesId, _froth[0]);
    }

    /// <summary>A cell's side (m).</summary>
    public float CellSize { get; }

    /// <summary>Cells east, up and north.</summary>
    public int CellsX { get; }

    /// <inheritdoc cref="CellsX"/>
    public int CellsY { get; }

    /// <inheritdoc cref="CellsX"/>
    public int CellsZ { get; }

    /// <summary>The grid's lowest south-west corner (world).</summary>
    public Vector3 Origin { get; set; }

    /// <summary>A particle's water: a cube this long on a side (m), half a cell.</summary>
    public float Spacing => 0.5f * CellSize;

    /// <summary>A particle's water (m³).</summary>
    public float Litre => Spacing * Spacing * Spacing;

    /// <summary>Particles at most.</summary>
    public int Capacity { get; }

    /// <summary>Particles now, read back from the card (a wait: for the console, previews and tests).</summary>
    public int Count => _counted.Now;

    /// <summary>Particles as the card last answered (no wait).</summary>
    public int LastCount => _counted.Last;

    /// <summary>Froth particles at most (a quarter of the water's; 1 with no froth material).</summary>
    public int FrothCapacity { get; }

    /// <summary>Froth particles now, read back from the card (a wait).</summary>
    public int FrothCount => _frothCounted.Now;

    /// <summary>
    /// How much froth the water's violence makes (0: none; 1: as tuned): spray, foam and bubbles, particles of their own
    /// that carry no water, drawn over it.
    /// </summary>
    public float FrothRate { get; set; } = 1f;

    /// <summary>The froth as drawn: its own buffer, arguments and material (none to draw without the froth material).</summary>
    public Cloud Froth { get; }

    /// <summary>The indirect drawing arguments: six points a particle.</summary>
    public ComputeBuffer Arguments { get; }

    /// <summary>What they are drawn with.</summary>
    public Material Material { get; }

    /// <summary>The particles' own buffer and size, for the material.</summary>
    public MaterialPropertyBlock Properties { get; } = new();

    /// <summary>A frame is moved in steps this long (s) at most.</summary>
    public float Substep { get; set; } = 1f / 60f;

    /// <summary>Sweeps of the pressure's relaxation a step (each over both colours).</summary>
    public int Iterations { get; set; } = 80;

    /// <summary>How much of a particle's new velocity is its own plus the grid's change (FLIP), the rest the grid's (PIC).</summary>
    public float Flip { get; set; } = 0.95f;

    /// <summary>The share of a cell's excess or lack of particles mended by the pressure each step.</summary>
    public float Drift { get; set; } = 0.1f;

    /// <summary>The fastest any particle goes (m/s).</summary>
    public float MaxSpeed { get; set; } = 30f;

    /// <summary>Whether water may leave through the square's sides (the world beyond is the server's) or they are walls.</summary>
    public bool OpenSides { get; set; } = true;

    /// <summary>Cells along the square's sides the server's water is poured into each step (0: none).</summary>
    public int EdgeBand { get; set; }

    /// <summary>Steps the last <see cref="Step"/> took.</summary>
    public int Substeps { get; private set; }

    /// <summary>The ground: a world height per cell of the card's water, <paramref name="cells"/> a side of <paramref name="dx"/> metres from <paramref name="origin"/> (world x, z).</summary>
    public void SetGround(Texture bed, int cells, float dx, Vector2 origin)
    {
        _bed = bed;
        _bedCells = cells;
        _bedDx = dx;
        _bedOrigin = origin;
    }

    /// <summary>What is built (<see cref="ParticleSolids"/>): solid voxels over the square from <paramref name="origin"/> (world x, z), <paramref name="rows"/> up from <paramref name="baseY"/>; or none (null).</summary>
    public void SetSolids(Texture solids, int columns, int rows, float voxelSize, Vector2 origin, float baseY)
    {
        _solids = solids;
        _solidColumns = columns;
        _solidRows = rows;
        _voxelSize = voxelSize;
        _solidOrigin = origin;
        _solidY0 = baseY;
    }

    /// <summary>The bodies in the water this frame, <see cref="MostBodies"/> at most: solid to it, as the ground is.</summary>
    public void SetBodies(List<ParticleWater.Body> bodies)
    {
        _bodyCount = Math.Min(bodies.Count, MostBodies);
        if (_bodyCount > 0)
        {
            _bodies.SetData(bodies, 0, 0, _bodyCount);
        }
    }

    /// <summary>The sources and drains in the square, <see cref="MostSprings"/> at most.</summary>
    public void SetSprings(List<Spring> springs)
    {
        _springList.Clear();
        for (int k = 0; k < springs.Count && k < MostSprings; k++)
        {
            _springList.Add(springs[k]);
        }
    }

    /// <summary>The server's water (<see cref="SimGrid.Target"/>, a texel a voxel column of <paramref name="perVoxel"/> cells of the ground's), for <see cref="SeedFromServer"/> and the edge band; or none (null).</summary>
    public void SetServer(Texture target, int perVoxel)
    {
        _target = target;
        _perVoxel = Math.Max(1, perVoxel);
    }

    /// <summary>
    /// The server's water poured as particles wherever the cells hold none (a share of what each lacks): once when the
    /// square is made, at the edge band each step on its own. Needs the ground.
    /// </summary>
    public void SeedFromServer(float share = 1f, int band = 0)
    {
        if (_target == null)
        {
            return;
        }

        Common(Substep);
        Prepare();
        _shader.SetInt(PourOnId, 0);
        _shader.SetInt(SeedBandId, band);
        _shader.SetFloat(SeedShareId, share);
        DispatchCells(_seed);
        Finish();
    }

    /// <summary>A block of still water from <paramref name="min"/> to <paramref name="max"/> (world), where the cells hold none (a wait for the card: how many were made).</summary>
    public int Add(Vector3 min, Vector3 max)
    {
        int before = Count;
        Common(Substep);
        Prepare();
        _shader.SetInt(PourOnId, 1);
        _shader.SetInt(SeedBandId, 0);
        _shader.SetFloat(SeedShareId, 1f);
        _shader.SetVector(PourMinId, min);
        _shader.SetVector(PourMaxId, max);
        DispatchCells(_seed);
        Finish();
        return Count - before;
    }

    /// <summary>No water: every particle gone, the froth with it.</summary>
    public void Clear()
    {
        _alive.SetData(NoAlive);
        Arguments.SetData(NoArgs);
        _frothAlive.SetData(NoAlive);
        Froth.Arguments.SetData(NoArgs);
        _counted.Last = 0;
        _frothCounted.Last = 0;
    }

    /// <summary>Moves the water over this much time, in steps of <see cref="Substep"/> at most.</summary>
    public void Step(float seconds)
    {
        if (seconds <= 0f)
        {
            Substeps = 0;
            return;
        }

        if (_bed == null)
        {
            throw new InvalidOperationException("the volume water has no ground: SetGround first");
        }

        int steps = Mathf.Max(1, Mathf.CeilToInt(seconds / Substep));
        float dt = seconds / steps;
        for (int k = 0; k < steps; k++)
        {
            Common(dt);
            Prepare();
            if (EdgeBand > 0 && _target != null)
            {
                _shader.SetInt(PourOnId, 0);
                _shader.SetInt(SeedBandId, EdgeBand);
                _shader.SetFloat(SeedShareId, Mathf.Min(1f, 0.8f * dt));
                DispatchCells(_seed);
            }

            Solve();
            Finish();
            if (Froth.Material != null)
            {
                MoveFroth();
            }
        }

        Substeps = steps;
        _counted.Poll();
        _frothCounted.Poll();
        Froth.LastCount = _frothCounted.Last;
    }

    /// <summary>Every particle's twelve floats, read back from the card: for previews and tests.</summary>
    public float[] Read()
    {
        int count = Count;
        float[] data = new float[count * 12];
        if (count > 0)
        {
            _particles[_current].GetData(data, 0, 0, count * 12);
        }

        return data;
    }

    /// <summary>Every froth particle's twelve floats (its place; its life left, born with, own number and kind; its velocity), read back from the card: for previews and tests.</summary>
    public float[] ReadFroth()
    {
        int count = FrothCount;
        float[] data = new float[count * 12];
        if (count > 0)
        {
            _froth[_frothCurrent].GetData(data, 0, 0, count * 12);
        }

        return data;
    }

    /// <summary>Each source's speed scale as Regulate last left it, and the particles it gave in the last step: for previews and tests (a wait for the card).</summary>
    public (float[] Scales, uint[] Given) ReadSources()
    {
        float[] scales = new float[MostSprings];
        uint[] given = new uint[MostSprings];
        _scale.GetData(scales);
        _given.GetData(given);
        return (scales, given);
    }

    /// <summary>Whether a particle read back (its fourth float) is still there.</summary>
    public static bool Alive(float keyBits) => BitConverter.ToInt32(BitConverter.GetBytes(keyBits), 0) != -1;

    public void Dispose()
    {
        _particles[0].Release();
        _particles[1].Release();
        _froth[0].Release();
        _froth[1].Release();
        _alive.Release();
        _frothAlive.Release();
        _frothCount.Release();
        Arguments.Release();
        Froth.Arguments.Release();
        _type.Release();
        _count.Release();
        _div.Release();
        _pressure.Release();
        _active.Release();
        _activeArgs.Release();
        _mom.Release();
        _wgt.Release();
        _vel.Release();
        _vel0.Release();
        _mask.Release();
        _mask2.Release();

        _bodies.Release();
        _springs.Release();
        _taken.Release();
        _given.Release();
        _scale.Release();
        Free(_noTarget);
        Free(_noSolids);
    }

    /// <summary>A water source or drain in the square.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Spring : IEquatable<Spring>
    {
        /// <summary>The centre (world) and the reach (m): a source fills the cells within it, a drain empties them.</summary>
        public Vector4 Ring;

        /// <summary>A source's speed out of it (m/s), the rate (m³/s), 1 for a drain, and a drain's particles a step (set each step).</summary>
        public Vector4 Flow;

        /// <summary>
        /// A source at <paramref name="centre"/> giving <paramref name="rate"/> m³/s: its water comes out of a sphere
        /// round it at about three metres a second, the sphere as big as the rate needs (0.4 m at least).
        /// </summary>
        public static Spring Source(Vector3 centre, float rate)
        {
            return new Spring { Ring = new Vector4(centre.x, centre.y, centre.z, Reach(rate)), Flow = new Vector4(SpringShape.Outflow(rate), rate, 0f, 0f) };
        }

        /// <summary>A source whose marker stands at <paramref name="foot"/>: its sphere sits on the ground there.</summary>
        public static Spring SourceOn(Vector3 foot, float rate) => Source(foot + (Vector3.up * Reach(rate)), rate);

        private static float Reach(float rate) => SpringShape.Reach(rate);

        /// <summary>A drain at <paramref name="centre"/> taking <paramref name="rate"/> m³/s (0 or less: all) within <paramref name="radius"/>.</summary>
        public static Spring Drain(Vector3 centre, float radius, float rate) => new()
        {
            Ring = new Vector4(centre.x, centre.y, centre.z, SpringShape.DrainReach(radius)),
            Flow = new Vector4(0f, rate <= 0f ? float.MaxValue : rate, 1f, 0f),
        };

        public static bool operator ==(Spring a, Spring b) => a.Equals(b);

        public static bool operator !=(Spring a, Spring b) => !a.Equals(b);

        public readonly bool Equals(Spring other) => Ring == other.Ring && Flow == other.Flow;

        public override readonly bool Equals(object obj) => obj is Spring other && Equals(other);

        public override readonly int GetHashCode() => (Ring.GetHashCode() * 397) ^ Flow.GetHashCode();
    }

    private static uint[] Counts(params uint[] values) => values;

    private static void Free(UnityEngine.Object thing)
    {
        if (thing == null)
        {
            return;
        }

        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(thing);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(thing);
        }
    }

    // What every kernel reads this step.
    private void Common(float dt)
    {
        _seedCounter++;
        _shader.SetInt(CapacityId, Capacity);
        _shader.SetInt(NxId, CellsX);
        _shader.SetInt(NyId, CellsY);
        _shader.SetInt(NzId, CellsZ);
        _shader.SetFloat(HId, CellSize);
        _shader.SetVector(OriginId, Origin);
        _shader.SetFloat(DtId, dt);
        _shader.SetFloat(GravityId, SimGrid.Gravity);
        _shader.SetFloat(FlipId, Flip);
        _shader.SetFloat(MaxSpeedId, MaxSpeed);
        _shader.SetFloat(DriftId, Drift);
        _shader.SetInt(OpenSidesId, OpenSides ? 1 : 0);
        _shader.SetInt(SeedId, (int)_seedCounter);
        _shader.SetInt(FrothCapacityId, FrothCapacity);
        _shader.SetFloat(FrothRateId, Froth.Material != null ? Math.Max(0f, FrothRate) : 0f);
        _shader.SetInt(BedCellsId, _bedCells);
        _shader.SetFloat(BedDxId, _bedDx);
        _shader.SetVector(BedOriginId, _bedOrigin);
        _shader.SetInt(SolidsOnId, _solids != null ? 1 : 0);
        _shader.SetInt(SolidColumnsId, _solidColumns);
        _shader.SetInt(SolidRowsId, _solidRows);
        _shader.SetFloat(VoxelSizeId, Math.Max(_voxelSize, 0.001f));
        _shader.SetVector(SolidOriginId, _solidOrigin);
        _shader.SetFloat(SolidY0Id, _solidY0);
        _shader.SetInt(BodyCountId, _bodyCount);
        _shader.SetInt(PerVoxelId, _perVoxel);

        // A drain's particles this step, from its rate (the solver sees _SpringCount of them; a step with none binds a
        // buffer it does not read).
        var springs = new Spring[Math.Max(1, _springList.Count)];
        for (int k = 0; k < _springList.Count; k++)
        {
            Spring s = _springList[k];
            s.Flow.w = s.Flow.z > 0.5f ? Mathf.Min(1e9f, s.Flow.y * dt / Litre) : 0f;
            springs[k] = s;
        }

        _springs.SetData(springs);
        _shader.SetInt(SpringCountId, _springList.Count);
        _taken.SetData(new uint[MostSprings]);
        _given.SetData(new uint[MostSprings]);

        // (A kernel reads the solids for the faces thin pieces close too: unbound, Unity would skip the kernel.)
        foreach (int kernel in new[] { _clearCells, _gather, _relax, _project, _move, _emit, _seed, _frothMove })
        {
            _shader.SetTexture(kernel, BedId, _bed);
            _shader.SetTexture(kernel, SolidsId, _solids ?? _noSolids);
            _shader.SetBuffer(kernel, BodiesId, _bodies);
        }

        // (The server's water beyond an open side is a wall to the solver: every kernel that reads a cell's type reads
        // it.)
        foreach (int kernel in new[] { _gather, _divergence, _relax, _project, _seed })
        {
            _shader.SetTexture(kernel, TargetId, _target ?? _noTarget);
        }

        // Each kernel writes what it must and reads the rest (the game's graphics let a kernel write eight buffers).
        foreach (int kernel in new[] { _clearCells, _scatter })
        {
            _shader.SetBuffer(kernel, TypeId, _type);
            _shader.SetBuffer(kernel, CountId, _count);
        }

        foreach (int kernel in new[] { _gather, _collect, _divergence, _relax, _project, _move, _emit, _seed })
        {
            _shader.SetBuffer(kernel, TypeInId, _type);
        }

        foreach (int kernel in new[] { _divergence, _move, _emit, _seed })
        {
            _shader.SetBuffer(kernel, CountInId, _count);
        }

        _shader.SetBuffer(_clearCells, DivId, _div);
        _shader.SetBuffer(_divergence, DivId, _div);
        _shader.SetBuffer(_relax, DivInId, _div);
        _shader.SetBuffer(_relax, PId, _pressure);
        _shader.SetBuffer(_project, PInId, _pressure);

        foreach (int kernel in new[] { _move, _emit, _seed })
        {
            _shader.SetBuffer(kernel, ParticlesId, _particles[_current]);
        }

        foreach (int kernel in new[] { _scatter, _compact })
        {
            _shader.SetBuffer(kernel, ParticlesInId, _particles[_current]);
        }

        foreach (int kernel in new[] { _emit, _seed, _compact, _settle })
        {
            _shader.SetBuffer(kernel, AliveId, _alive);
        }

        foreach (int kernel in new[] { _scatter, _move })
        {
            _shader.SetBuffer(kernel, AliveInId, _alive);
        }

        _shader.SetBuffer(_clearFaces, MomId, _mom);
        _shader.SetBuffer(_clearFaces, WgtId, _wgt);
        _shader.SetBuffer(_scatter, MomId, _mom);
        _shader.SetBuffer(_scatter, WgtId, _wgt);
        _shader.SetBuffer(_gather, MomInId, _mom);
        _shader.SetBuffer(_gather, WgtInId, _wgt);
        foreach (int kernel in new[] { _clearFaces, _gather, _project, _extend })
        {
            _shader.SetBuffer(kernel, VelId, _vel);
            _shader.SetBuffer(kernel, MaskId, _mask);
        }

        foreach (int kernel in new[] { _clearFaces, _gather, _extend })
        {
            _shader.SetBuffer(kernel, Vel0Id, _vel0);
        }

        _shader.SetBuffer(_extend, Mask2Id, _mask2);
        foreach (int kernel in new[] { _divergence, _move })
        {
            _shader.SetBuffer(kernel, VelInId, _vel);
        }

        _shader.SetBuffer(_move, Vel0InId, _vel0);

        _shader.SetBuffer(_collect, ActiveOutId, _active);
        _shader.SetBuffer(_arguments, ActiveArgsId, _activeArgs);
        foreach (int kernel in new[] { _divergence, _relax })
        {
            _shader.SetBuffer(kernel, ActiveId, _active);
            _shader.SetBuffer(kernel, ActiveArgsInId, _activeArgs);
        }

        foreach (int kernel in new[] { _clearCells, _gather, _move, _emit, _regulate })
        {
            _shader.SetBuffer(kernel, SpringsId, _springs);
        }

        foreach (int kernel in new[] { _gather, _emit })
        {
            _shader.SetBuffer(kernel, ScaleInId, _scale);
        }

        _shader.SetBuffer(_regulate, ScaleId, _scale);
        _shader.SetBuffer(_emit, GivenId, _given);
        _shader.SetBuffer(_regulate, GivenInId, _given);
        _shader.SetBuffer(_move, TakenId, _taken);
        _shader.SetBuffer(_move, FrothId, _froth[_frothCurrent]);
        _shader.SetBuffer(_move, FrothAliveId, _frothAlive);
        _shader.SetBuffer(_move, FrothCountInId, _frothCount);
        _shader.SetBuffer(_compact, SortedId, _particles[1 - _current]);
    }

    // The cells typed and the particles counted onto them (what pouring and the step both start from).
    private void Prepare()
    {
        DispatchCells(_clearCells);
        DispatchFaces(_clearFaces);
        DispatchParticles(_scatter);
    }

    // One step's pressure and motion.
    private void Solve()
    {
        DispatchFaces(_gather);
        _active.SetCounterValue(0);
        DispatchCells(_collect);
        ComputeBuffer.CopyCount(_active, _activeArgs, 3 * sizeof(uint));
        _shader.Dispatch(_arguments, 1, 1, 1);
        _shader.DispatchIndirect(_divergence, _activeArgs, 0);
        for (int k = 0; k < Iterations; k++)
        {
            _shader.SetInt(ParityId, 0);
            _shader.DispatchIndirect(_relax, _activeArgs, 0);
            _shader.SetInt(ParityId, 1);
            _shader.DispatchIndirect(_relax, _activeArgs, 0);
        }

        DispatchFaces(_project);
        for (int pass = 0; pass < 2; pass++)
        {
            _shader.SetBuffer(_extend, MaskId, pass == 0 ? _mask : _mask2);
            _shader.SetBuffer(_extend, Mask2Id, pass == 0 ? _mask2 : _mask);
            DispatchFaces(_extend);
        }

        DispatchParticles(_move);
        if (_springList.Count > 0)
        {
            DispatchCells(_emit);
            _shader.Dispatch(_regulate, 1, 1, 1);
        }
    }

    // The particles still there into the other buffer, and how many.
    private void Finish()
    {
        DispatchParticles(_compact);
        _shader.SetBuffer(_settle, ArgsId, Arguments);
        _shader.Dispatch(_settle, 1, 1, 1);
        _current = 1 - _current;
        Properties.SetBuffer(ParticlesId, _particles[_current]);
    }

    // The froth moved by its kinds, compacted and counted, with the water's kernels bound to its own buffers (the
    // next step's Common binds the water's again).
    private void MoveFroth()
    {
        _shader.SetInt(CapacityId, FrothCapacity);
        _shader.SetBuffer(_frothClear, FrothCountId, _frothCount);
        DispatchCells(_frothClear);
        _shader.SetBuffer(_frothMove, FrothCountId, _frothCount);
        _shader.SetBuffer(_frothMove, ParticlesId, _froth[_frothCurrent]);
        _shader.SetBuffer(_frothMove, AliveInId, _frothAlive);
        _shader.SetBuffer(_frothMove, CountInId, _count);
        _shader.SetBuffer(_frothMove, VelInId, _vel);
        int groups = (FrothCapacity + Threads - 1) / Threads;
        _shader.Dispatch(_frothMove, groups, 1, 1);
        _shader.SetBuffer(_compact, ParticlesInId, _froth[_frothCurrent]);
        _shader.SetBuffer(_compact, SortedId, _froth[1 - _frothCurrent]);
        _shader.SetBuffer(_compact, AliveId, _frothAlive);
        _shader.Dispatch(_compact, groups, 1, 1);
        _shader.SetBuffer(_settle, AliveId, _frothAlive);
        _shader.SetBuffer(_settle, ArgsId, Froth.Arguments);
        _shader.Dispatch(_settle, 1, 1, 1);
        _frothCurrent = 1 - _frothCurrent;
        Froth.Properties.SetBuffer(ParticlesId, _froth[_frothCurrent]);
    }

    private void DispatchCells(int kernel) => _shader.Dispatch(kernel, (_cells + Threads - 1) / Threads, 1, 1);

    private void DispatchFaces(int kernel) => _shader.Dispatch(kernel, (_faces + Threads - 1) / Threads, 1, 1);

    private void DispatchParticles(int kernel) => _shader.Dispatch(kernel, (Capacity + Threads - 1) / Threads, 1, 1);

    /// <summary>A set of particles as drawn: the froth's (<see cref="IParticleCloud"/> for <see cref="ParticleSurface"/>).</summary>
    public sealed class Cloud : IParticleCloud
    {
        internal Cloud(Material material, ComputeBuffer arguments, float spacing)
        {
            Material = material;
            Arguments = arguments;
            Spacing = spacing;
        }

        /// <inheritdoc/>
        public ComputeBuffer Arguments { get; }

        /// <summary>The froth's material; null when the bundle had none (no froth is made then).</summary>
        public Material Material { get; }

        /// <inheritdoc/>
        public MaterialPropertyBlock Properties { get; } = new();

        /// <summary>Froth particles as the card last answered (no wait).</summary>
        public int LastCount { get; internal set; }

        /// <inheritdoc/>
        public float Spacing { get; }
    }

    // A buffer's first count, read back from the card without a wait (asked each poll, taken when it comes; the editor,
    // not playing, waits), or with one.
    private sealed class Counter
    {
        private readonly ComputeBuffer _buffer;

        private readonly int _most;

        private UnityEngine.Rendering.AsyncGPUReadbackRequest _request;

        private bool _asked;

        public Counter(ComputeBuffer buffer, int most)
        {
            _buffer = buffer;
            _most = most;
        }

        public int Last { get; set; }

        public int Now
        {
            get
            {
                uint[] counts = new uint[3];
                _buffer.GetData(counts);
                return (int)Math.Min(counts[0], (uint)_most);
            }
        }

        public void Poll()
        {
            if (_asked && !Application.isPlaying)
            {
                _request.WaitForCompletion();
            }

            if (_asked && _request.done)
            {
                _asked = false;
                if (!_request.hasError)
                {
                    Last = (int)Math.Min(_request.GetData<uint>()[0], (uint)_most);
                }
            }

            if (!_asked)
            {
                _request = UnityEngine.Rendering.AsyncGPUReadback.Request(_buffer);
                _asked = true;
            }
        }
    }
}
