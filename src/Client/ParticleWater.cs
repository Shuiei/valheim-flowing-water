using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// Water as particles on the graphics card (the mod's FlowingWaterParticles compute shader), a prototype: position based
/// fluids over a hashed grid, each particle a cube of water <see cref="Spacing"/> on a side, kept out of the ground the
/// card's water knows (<see cref="SimGrid.Bed"/>), what players build, the square's walls and the bodies in the water.
/// Given the server's water by the voxel column (<see cref="SetOwner"/>), the particles are the water near the camera:
/// made where the server has water and the particles have less, let go where they have more, so the server keeps the
/// say on how much water is where while how it moves is the particles' own. The particles' count lives on the card
/// (<see cref="Count"/> reads it back). Nothing is sent anywhere. Needs only Unity, so the Unity project's preview runs
/// it (ParticlePreview) too.
/// </summary>
internal sealed class ParticleWater : IDisposable
{
    /// <summary>Bodies in the water at most (<see cref="SetBodies"/>).</summary>
    public const int MostBodies = 32;

    /// <summary>Water sources the particles come out of at most (<see cref="SetSpouts"/>).</summary>
    public const int MostSpouts = 16;

    // A particle's floats: place and its cell's key, the step's guess and its pull, velocity and density.
    private const int Stride = 12 * sizeof(float);

    // The hashed grid's buckets (a power of two) and the buckets a scan block holds (the compute shader's SCAN).
    private const int Buckets = 1 << 18;

    private const int Scan = 1024;

    private const int Threads = 256;

    // Macklin and Müller's artificial pressure is measured against the kernel this share of the radius away.
    private const float CorrDistance = 0.2f;

    private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
    private static readonly int SortedId = Shader.PropertyToID("_Sorted");
    private static readonly int AliveId = Shader.PropertyToID("_Alive");
    private static readonly int ArgsId = Shader.PropertyToID("_Args");
    private static readonly int CapacityId = Shader.PropertyToID("_Capacity");
    private static readonly int CountsId = Shader.PropertyToID("_Counts");
    private static readonly int StartsId = Shader.PropertyToID("_Starts");
    private static readonly int BlockSumsId = Shader.PropertyToID("_BlockSums");
    private static readonly int HashesId = Shader.PropertyToID("_Hashes");
    private static readonly int SlotsId = Shader.PropertyToID("_Slots");
    private static readonly int BucketsId = Shader.PropertyToID("_Buckets");
    private static readonly int BlocksId = Shader.PropertyToID("_Blocks");
    private static readonly int DeltaId = Shader.PropertyToID("_Delta");
    private static readonly int BedId = Shader.PropertyToID("_Bed");
    private static readonly int NId = Shader.PropertyToID("_N");
    private static readonly int DxId = Shader.PropertyToID("_Dx");
    private static readonly int OriginId = Shader.PropertyToID("_Origin");
    private static readonly int TopId = Shader.PropertyToID("_Top");
    private static readonly int RadiusId = Shader.PropertyToID("_Radius");
    private static readonly int StepUpId = Shader.PropertyToID("_StepUp");
    private static readonly int HId = Shader.PropertyToID("_H");
    private static readonly int H2Id = Shader.PropertyToID("_H2");
    private static readonly int Poly6KId = Shader.PropertyToID("_Poly6K");
    private static readonly int SpikyKId = Shader.PropertyToID("_SpikyK");
    private static readonly int RestWeightId = Shader.PropertyToID("_RestWeight");
    private static readonly int RestNumberId = Shader.PropertyToID("_RestNumber");
    private static readonly int RelaxationId = Shader.PropertyToID("_Relaxation");
    private static readonly int CorrKId = Shader.PropertyToID("_CorrK");
    private static readonly int CorrWId = Shader.PropertyToID("_CorrW");
    private static readonly int ViscosityId = Shader.PropertyToID("_Viscosity");
    private static readonly int MaxSpeedId = Shader.PropertyToID("_MaxSpeed");
    private static readonly int DtId = Shader.PropertyToID("_Dt");
    private static readonly int GravityId = Shader.PropertyToID("_Gravity");
    private static readonly int SphereId = Shader.PropertyToID("_Sphere");
    private static readonly int BodiesId = Shader.PropertyToID("_Bodies");
    private static readonly int BodyCountId = Shader.PropertyToID("_BodyCount");
    private static readonly int SpoutsId = Shader.PropertyToID("_Spouts");
    private static readonly int SpoutCountId = Shader.PropertyToID("_SpoutCount");
    private static readonly int FrameId = Shader.PropertyToID("_Frame");
    private static readonly int OwnId = Shader.PropertyToID("_Own");
    private static readonly int TargetId = Shader.PropertyToID("_Target");
    private static readonly int PerVoxelId = Shader.PropertyToID("_PerVoxel");
    private static readonly int ColumnCountId = Shader.PropertyToID("_ColumnCount");
    private static readonly int ColumnTopId = Shader.PropertyToID("_ColumnTop");
    private static readonly int ColumnLossId = Shader.PropertyToID("_ColumnLoss");
    private static readonly int PullId = Shader.PropertyToID("_Pull");
    private static readonly int BorderId = Shader.PropertyToID("_Border");
    private static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");
    private static readonly int LitreId = Shader.PropertyToID("_Litre");
    private static readonly int SeedId = Shader.PropertyToID("_Seed");
    private static readonly int SolidsOnId = Shader.PropertyToID("_SolidsOn");
    private static readonly int SolidsId = Shader.PropertyToID("_Solids");
    private static readonly int SolidColumnsId = Shader.PropertyToID("_SolidColumns");
    private static readonly int SolidRowsId = Shader.PropertyToID("_SolidRows");
    private static readonly int VoxelSizeId = Shader.PropertyToID("_VoxelSize");
    private static readonly int SolidY0Id = Shader.PropertyToID("_SolidY0");
    private static readonly int SizeId = Shader.PropertyToID("_Size");

    private readonly ComputeShader _shader;

    private readonly int _clearColumns;
    private readonly int _countColumns;
    private readonly int _fill;
    private readonly int _spout;
    private readonly int _clearCounts;
    private readonly int _predict;
    private readonly int _scanBlocks;
    private readonly int _scanSums;
    private readonly int _addSums;
    private readonly int _scatter;
    private readonly int _settle;
    private readonly int _lambda;
    private readonly int _displace;
    private readonly int _apply;
    private readonly int _finish;

    // The particles, and the other buffer they are sorted and finished into (swapped each time).
    private readonly ComputeBuffer[] _particles = new ComputeBuffer[2];

    private readonly ComputeBuffer _alive;
    private readonly ComputeBuffer _counts;
    private readonly ComputeBuffer _starts;
    private readonly ComputeBuffer _blockSums;
    private readonly ComputeBuffer _hashes;
    private readonly ComputeBuffer _slots;
    private readonly ComputeBuffer _delta;
    private readonly ComputeBuffer _bodies;

    private readonly ComputeBuffer _spouts;

    // What the server's texture and the solids are bound as when there are none: nothing is read there.
    private readonly Texture2D _noTarget;

    private readonly Texture3D _noSolids;

    // The smoothing kernels' constants (Müller et al., 2003): poly6's 315 / (64 pi h^9), spiky's gradient's 45 / (pi h^6);
    // the sums over a lattice of particles at their spacing: of the smoothing kernel (water at rest), and of the squared
    // kernel gradients (what the relaxation is a share of).
    private readonly float _poly6K;

    private readonly float _spikyK;

    private readonly float _restWeight;

    private readonly float _restGradients;

    // The particles' count asked of the card now and then, so a frame need not wait for it (LastCount).
    private UnityEngine.Rendering.AsyncGPUReadbackRequest _counting;

    private bool _countingNow;

    private int _current;

    private int _bodyCount;

    private int _spoutCount;

    private uint _seed;

    private Texture _bed;

    private int _cells;

    private float _cellSize;

    private Vector2 _origin;

    private float _top;

    private Vector4 _sphere = new(0f, 0f, 0f, -1f);

    private Texture _target;

    private int _perVoxel = 1;

    private int _border;

    private float _seaLevel = float.NegativeInfinity;

    // The columns' particle counts, highest particles and loss shares, a texel a voxel column (made at the square's columns).
    private RenderTexture _columnCount;

    private RenderTexture _columnTop;

    private RenderTexture _columnLoss;

    private Texture _solids;

    private int _solidColumns;

    private int _solidRows;

    private float _voxelSize;

    private float _solidY0;

    /// <summary>
    /// Room for <paramref name="capacity"/> particles of water <paramref name="spacing"/> metres on a side (0.1 for a
    /// litre; 0.25 or 0.5 for the water near the camera), moved by <paramref name="shader"/> and drawn with
    /// <paramref name="material"/>.
    /// </summary>
    public ParticleWater(ComputeShader shader, Material material, int capacity, float spacing = 0.1f)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "no room for particles");
        }

        if (spacing <= 0f)
        {
            throw new ArgumentOutOfRangeException(nameof(spacing), spacing, "a particle must have a size");
        }

        _shader = shader;
        Material = material;
        Capacity = capacity;
        Spacing = spacing;
        Radius = 2f * spacing;
        _poly6K = 315f / (64f * Mathf.PI * Mathf.Pow(Radius, 9f));
        _spikyK = 45f / (Mathf.PI * Mathf.Pow(Radius, 6f));
        _clearColumns = shader.FindKernel("ClearColumns");
        _countColumns = shader.FindKernel("CountColumns");
        _fill = shader.FindKernel("Fill");
        _spout = shader.FindKernel("Spout");
        _clearCounts = shader.FindKernel("ClearCounts");
        _predict = shader.FindKernel("Predict");
        _scanBlocks = shader.FindKernel("ScanBlocks");
        _scanSums = shader.FindKernel("ScanSums");
        _addSums = shader.FindKernel("AddSums");
        _scatter = shader.FindKernel("Scatter");
        _settle = shader.FindKernel("Settle");
        _lambda = shader.FindKernel("Lambda");
        _displace = shader.FindKernel("Displace");
        _apply = shader.FindKernel("Apply");
        _finish = shader.FindKernel("Finish");
        for (int k = 0; k < 2; k++)
        {
            _particles[k] = new ComputeBuffer(capacity, Stride);
        }

        _alive = new ComputeBuffer(2, sizeof(uint));
        Arguments = new ComputeBuffer(4, sizeof(uint), ComputeBufferType.IndirectArguments);
        _counts = new ComputeBuffer(Buckets, sizeof(uint));
        _starts = new ComputeBuffer(Buckets, sizeof(uint));
        _blockSums = new ComputeBuffer(Buckets / Scan, sizeof(uint));
        _hashes = new ComputeBuffer(capacity, sizeof(uint));
        _slots = new ComputeBuffer(capacity, sizeof(uint));
        _delta = new ComputeBuffer(capacity, 4 * sizeof(float));
        _bodies = new ComputeBuffer(MostBodies, 16 * sizeof(float));
        _spouts = new ComputeBuffer(MostSpouts, 8 * sizeof(float));
        _noTarget = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true) { name = "FlowingWater particles no target" };
        _noTarget.SetPixel(0, 0, new Color(-10000f, 0f, 0f, 0f));
        _noTarget.Apply(false);
        _noSolids = new Texture3D(1, 1, 1, TextureFormat.R8, false) { name = "FlowingWater particles no solids" };
        _noSolids.SetPixelData(new byte[1], 0);
        _noSolids.Apply(false);
        (_restWeight, _restGradients) = Lattice();
        Clear();
        Properties.SetBuffer(ParticlesId, _particles[0]);
        Properties.SetFloat(SizeId, 0.8f * spacing);
    }

    /// <summary>A particle's water: a cube this long on a side (m), at rest.</summary>
    public float Spacing { get; }

    /// <summary>The smoothing radius (m): how far a particle feels its neighbours. Twice the spacing: about 30 of them.</summary>
    public float Radius { get; }

    /// <summary>A particle's water (m³).</summary>
    public float Litre => Spacing * Spacing * Spacing;

    /// <summary>Particles at most.</summary>
    public int Capacity { get; }

    /// <summary>
    /// Particles now, read back from the card (a wait for it: for the console, previews and tests; drawing goes by
    /// <see cref="Arguments"/>).
    /// </summary>
    public int Count
    {
        get
        {
            uint[] alive = new uint[2];
            _alive.GetData(alive);
            return (int)Math.Min(alive[0], (uint)Capacity);
        }
    }

    /// <summary>
    /// Particles as the card last answered (asked each <see cref="Step"/>, answered a frame or two later; set at once
    /// by <see cref="Add"/> and <see cref="Clear"/>): no wait, for a frame to know whether there is anything to draw.
    /// </summary>
    public int LastCount { get; private set; }

    /// <summary>The indirect drawing arguments: six points a particle, as many particles as there are (kept on the card).</summary>
    public ComputeBuffer Arguments { get; }

    /// <summary>A frame is moved in steps this long (s) at most.</summary>
    public float Substep { get; set; } = 1f / 60f;

    /// <summary>
    /// Rounds of pulls a step takes to bring the water back to rest. A round carries the crowding one neighbour on, so
    /// a deep pool needs several to stand its own weight: with 8, a pool 1 m deep is pressed down 1 %.
    /// </summary>
    public int Iterations { get; set; } = 8;

    /// <summary>
    /// The relaxation of the pulls, as a share of the squared gradients a particle at rest feels: higher is softer
    /// (compressible, stable), lower stiffer. Too low and the rounds overshoot each other (every particle and its
    /// neighbours all mend the same crowding at once) and the water flies apart.
    /// </summary>
    public float Relaxation { get; set; } = 1f;

    /// <summary>
    /// The artificial pressure keeping particles apart before they come so close that the pulls throw them (Macklin and
    /// Müller's, m²): a pull of its own added to every pair's, steep at short range. Small: it pushes the surface of water
    /// at rest up too, and more than gravity brings back in a step would make it boil.
    /// </summary>
    public float Tension { get; set; } = 0.001f;

    /// <summary>XSPH: how much of its neighbours' speed each particle takes each step.</summary>
    public float Viscosity { get; set; } = 0.01f;

    /// <summary>Ground higher than a particle by more than this (m) is a wall to it, not a step it rides up.</summary>
    public float StepUp { get; set; } = 0.35f;

    /// <summary>
    /// The fastest any particle goes (m/s): a speed from a mended guess is held there, not thrown on. A drop falls no
    /// faster than about 9 m/s in air; so a particle thrown by a pull (set in water at rest, caught between two
    /// bodies) flies a few metres, not thirty.
    /// </summary>
    public float MaxSpeed { get; set; } = 12f;

    /// <summary>
    /// With an owner (<see cref="SetOwner"/>), the share of what a column lacks or has too much of that one second
    /// mends (the sheet's own rate): the water comes and goes smoothly, not as a lump a frame.
    /// </summary>
    public float Pull { get; set; } = 0.8f;

    /// <summary>Steps the last <see cref="Step"/> took.</summary>
    public int Substeps { get; private set; }

    /// <summary>What they are drawn with (<see cref="Arguments"/> says how many points), with <see cref="Properties"/>.</summary>
    public Material Material { get; }

    /// <summary>The particles' own buffer and size, for the material.</summary>
    public MaterialPropertyBlock Properties { get; } = new();

    /// <summary>Whether the particles are the water near the camera, held to the server's (<see cref="SetOwner"/>).</summary>
    public bool Owned => _target != null;

    /// <summary>
    /// The ground the water is kept out of: a world height per cell (a wall over <see cref="SimGrid.Wall"/>, as the
    /// card's water has it), <paramref name="cells"/> a side of <paramref name="cellSize"/> metres from the square's
    /// south-west corner <paramref name="origin"/> (world x, z); the square's walls hold the water in, and nothing goes
    /// over <paramref name="top"/> (world height).
    /// </summary>
    public void SetGround(Texture bed, int cells, float cellSize, Vector2 origin, float top)
    {
        _bed = bed;
        _cells = cells;
        _cellSize = cellSize;
        _origin = origin;
        _top = top;
    }

    /// <summary>
    /// The server's water the particles are held to (<see cref="SimGrid.Target"/>: a texel a voxel column of the
    /// ground's square, <paramref name="perVoxel"/> cells to a column's side), with the cells along the square's edge
    /// the bricks draw instead (<paramref name="border"/>) and the sea's level (a particle under it is the sea's); or
    /// none (null): the particles are then only what was poured.
    /// </summary>
    public void SetOwner(Texture target, int perVoxel, int border, float seaLevel)
    {
        _target = target;
        _perVoxel = Math.Max(1, perVoxel);
        _border = border;
        _seaLevel = seaLevel;
    }

    /// <summary>A sphere the water meets, at <paramref name="centre"/> (world) of this radius (m); none at a radius of 0 or less.</summary>
    public void SetSphere(Vector3 centre, float radius) => _sphere = new Vector4(centre.x, centre.y, centre.z, radius);

    /// <summary>
    /// What players built, as <see cref="ParticleSolids"/> holds it: solid voxels over the square (from the same corner
    /// as the ground's), <paramref name="columns"/> a side of <paramref name="voxelSize"/> metres, <paramref name="rows"/>
    /// rows up from world height <paramref name="baseY"/>; or none (null).
    /// </summary>
    public void SetSolids(Texture solids, int columns, int rows, float voxelSize, float baseY)
    {
        _solids = solids;
        _solidColumns = columns;
        _solidRows = rows;
        _voxelSize = voxelSize;
        _solidY0 = baseY;
    }

    /// <summary>
    /// The bodies in the water this frame (ships' hulls, players, creatures), <see cref="MostBodies"/> at most: a
    /// particle keeps a radius out of each, so a body moving on shoves the water ahead and aside as far as it moved.
    /// </summary>
    public void SetBodies(List<Body> bodies)
    {
        _bodyCount = Math.Min(bodies.Count, MostBodies);
        if (_bodyCount > 0)
        {
            _bodies.SetData(bodies, 0, 0, _bodyCount);
        }
    }

    /// <summary>
    /// The water sources in the square this frame (<see cref="Spout.At"/>), <see cref="MostSpouts"/> at most: each
    /// frame's water of each comes out all round its rim as particles, outward.
    /// </summary>
    public void SetSpouts(List<Spout> spouts)
    {
        _spoutCount = Math.Min(spouts.Count, MostSpouts);
        if (_spoutCount > 0)
        {
            _spouts.SetData(spouts, 0, 0, _spoutCount);
        }
    }

    /// <summary>
    /// A block of still water from <paramref name="min"/> to <paramref name="max"/> (world), a lattice of particles at
    /// their spacing, each nudged by a hair (a fixed sequence: the same block is the same each time) so no two rows fall
    /// alike; as many as there is room for (a wait for the card, which says how many there are).
    /// </summary>
    public int Add(Vector3 min, Vector3 max)
    {
        int nx = Mathf.Max(0, Mathf.FloorToInt((max.x - min.x) / Spacing + 1e-4f));
        int ny = Mathf.Max(0, Mathf.FloorToInt((max.y - min.y) / Spacing + 1e-4f));
        int nz = Mathf.Max(0, Mathf.FloorToInt((max.z - min.z) / Spacing + 1e-4f));
        int before = Count;
        int count = Math.Min(nx * ny * nz, Capacity - before);
        if (count <= 0)
        {
            return 0;
        }

        float[] data = new float[count * 12];
        int n = 0;
        for (int k = 0; k < count; k++)
        {
            int i = k % nx, j = (k / nx) % ny, l = k / (nx * ny);
            int step = before + k;
            float x = min.x + ((i + 0.5f) * Spacing) + (Nudge(step, 0.618034f) * 0.02f * Spacing);
            float y = min.y + ((j + 0.5f) * Spacing) + (Nudge(step, 0.754878f) * 0.02f * Spacing);
            float z = min.z + ((l + 0.5f) * Spacing) + (Nudge(step, 0.569840f) * 0.02f * Spacing);
            data[n++] = x;
            data[n++] = y;
            data[n++] = z;
            data[n++] = 1f;
            data[n++] = x;
            data[n++] = y;
            data[n++] = z;
            data[n++] = 0f;
            n += 3;
            data[n++] = 1f;
        }

        _particles[_current].SetData(data, 0, before * 12, count * 12);
        SetCount(before + count);
        return count;
    }

    /// <summary>No water: every particle gone.</summary>
    public void Clear() => SetCount(0);

    /// <summary>
    /// Moves the water over this much time, in steps of <see cref="Substep"/> at most; with an owner
    /// (<see cref="SetOwner"/>), first brings each column's particles toward the server's water there.
    /// </summary>
    public void Step(float seconds)
    {
        if (seconds <= 0f)
        {
            Substeps = 0;
            return;
        }

        if (_bed == null)
        {
            throw new InvalidOperationException("the particle water has no ground: SetGround first");
        }

        int steps = Mathf.Max(1, Mathf.CeilToInt(seconds / Substep));
        float dt = seconds / steps;
        _seed++;
        Common(dt, seconds);
        if (_target != null)
        {
            Own();
        }

        if (_spoutCount > 0)
        {
            _shader.SetBuffer(_spout, ParticlesId, _particles[_current]);
            _shader.SetBuffer(_spout, AliveId, _alive);
            _shader.Dispatch(_spout, _spoutCount, 1, 1);
        }

        for (int k = 0; k < steps; k++)
        {
            Advance();
        }

        Substeps = steps;
        Properties.SetBuffer(ParticlesId, _particles[_current]);

        // The count, asked once the last asking is answered (in the editor's preview, not playing, no frame goes by to
        // answer it: waited for).
        if (_countingNow && !Application.isPlaying)
        {
            _counting.WaitForCompletion();
        }

        if (_countingNow && _counting.done)
        {
            _countingNow = false;
            if (!_counting.hasError)
            {
                LastCount = (int)Math.Min(_counting.GetData<uint>()[0], (uint)Capacity);
            }
        }

        if (!_countingNow)
        {
            _counting = UnityEngine.Rendering.AsyncGPUReadback.Request(_alive);
            _countingNow = true;
        }
    }

    /// <summary>
    /// Every particle's twelve floats (place and its cell's key, the step's guess and its pull, velocity and density),
    /// read back from the card: for previews and tests. <see cref="Alive"/> tells a particle still there.
    /// </summary>
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

    /// <summary>Whether a particle read back (<see cref="Read"/>, its fourth float) is still there (not gone this step).</summary>
    public static bool Alive(float keyBits) => BitConverter.ToInt32(BitConverter.GetBytes(keyBits), 0) != -1;

    public void Dispose()
    {
        _particles[0].Release();
        _particles[1].Release();
        _alive.Release();
        Arguments.Release();
        _counts.Release();
        _starts.Release();
        _blockSums.Release();
        _hashes.Release();
        _slots.Release();
        _delta.Release();
        _bodies.Release();
        _spouts.Release();
        Free(_noTarget);
        Free(_noSolids);
        FreeColumns();
    }

    /// <summary>A box in the water: its centre, its half extents along its own axes, and two of those axes; or a sphere.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Body : IEquatable<Body>
    {
        /// <summary>The centre (world), and the kind: 1 a box, 2 a sphere.</summary>
        public Vector4 Centre;

        /// <summary>The half extents along the body's axes (m); a sphere's radius in x.</summary>
        public Vector4 Half;

        /// <summary>The body's own x axis (world, unit).</summary>
        public Vector4 AxisX;

        /// <summary>The body's own y axis (world, unit); z is across the two.</summary>
        public Vector4 AxisY;

        /// <summary>A box at <paramref name="centre"/>, <paramref name="half"/> extents along its axes, turned by <paramref name="rotation"/>.</summary>
        public static Body Box(Vector3 centre, Vector3 half, Quaternion rotation)
        {
            Vector3 x = rotation * Vector3.right, y = rotation * Vector3.up;
            return new Body
            {
                Centre = new Vector4(centre.x, centre.y, centre.z, 1f),
                Half = new Vector4(half.x, half.y, half.z, 0f),
                AxisX = new Vector4(x.x, x.y, x.z, 0f),
                AxisY = new Vector4(y.x, y.y, y.z, 0f),
            };
        }

        /// <summary>A sphere at <paramref name="centre"/> of this radius.</summary>
        public static Body Sphere(Vector3 centre, float radius) => new()
        {
            Centre = new Vector4(centre.x, centre.y, centre.z, 2f),
            Half = new Vector4(radius, radius, radius, 0f),
            AxisX = new Vector4(1f, 0f, 0f, 0f),
            AxisY = new Vector4(0f, 1f, 0f, 0f),
        };

        public static bool operator ==(Body a, Body b) => a.Equals(b);

        public static bool operator !=(Body a, Body b) => !a.Equals(b);

        public readonly bool Equals(Body other) => Centre == other.Centre && Half == other.Half && AxisX == other.AxisX && AxisY == other.AxisY;

        public override readonly bool Equals(object obj) => obj is Body other && Equals(other);

        public override readonly int GetHashCode()
        {
            int hash = Centre.GetHashCode();
            hash = (hash * 397) ^ Half.GetHashCode();
            hash = (hash * 397) ^ AxisX.GetHashCode();
            return (hash * 397) ^ AxisY.GetHashCode();
        }
    }

    /// <summary>A water source the particles come out of: its rim, and the speed and rate of its water.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Spout : IEquatable<Spout>
    {
        /// <summary>The rim's centre (world) and radius (m).</summary>
        public Vector4 Ring;

        /// <summary>The speed out of the rim (m/s), the rate (m³/s); nothing.</summary>
        public Vector4 Flow;

        /// <summary>
        /// A source whose marker is a sphere of <paramref name="radius"/> metres at <paramref name="centre"/>, giving
        /// <paramref name="rate"/> m³/s: its water comes out of a rim a particle's radius out from the sphere, as fast
        /// as the rate pushes it through the sphere's side (a metre a second at least, eight at most).
        /// </summary>
        public static Spout At(Vector3 centre, float radius, float rate, float particleRadius) => new()
        {
            Ring = new Vector4(centre.x, centre.y, centre.z, radius + particleRadius),
            Flow = new Vector4(Mathf.Clamp(rate / (2f * Mathf.PI * radius * radius), 1f, 8f), rate, 0f, 0f),
        };

        public static bool operator ==(Spout a, Spout b) => a.Equals(b);

        public static bool operator !=(Spout a, Spout b) => !a.Equals(b);

        public readonly bool Equals(Spout other) => Ring == other.Ring && Flow == other.Flow;

        public override readonly bool Equals(object obj) => obj is Spout other && Equals(other);

        public override readonly int GetHashCode() => (Ring.GetHashCode() * 397) ^ Flow.GetHashCode();
    }

    // A nudge in -0.5 to 0.5 for the step of a sequence, spread as an irrational's multiples are (no two steps alike).
    private static float Nudge(int step, float spread) => Mathf.Repeat(step * spread, 1f) - 0.5f;

    // Gone: in the game as Unity frees things, in the editor's preview (not playing) at once, as it must there.
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

    private static RenderTexture MakeColumns(string name, int columns, RenderTextureFormat format)
    {
        var texture = new RenderTexture(columns, columns, 0, format, RenderTextureReadWrite.Linear)
        {
            name = "FlowingWater particles " + name,
            enableRandomWrite = true,
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.Create();
        return texture;
    }

    // The smoothing kernels' sums over a lattice of particles at their spacing round one of them (itself counted in the
    // weight): the weight of water at rest, and the squared gradients a particle at rest feels (the compute shader's own
    // kernels, as it sums them).
    private (float Weight, float Gradients) Lattice()
    {
        float h2 = Radius * Radius;
        int reach = Mathf.CeilToInt(Radius / Spacing);
        float weight = 0f, gradients = 0f;
        Vector3 sum = Vector3.zero;
        for (int i = -reach; i <= reach; i++)
        {
            for (int j = -reach; j <= reach; j++)
            {
                for (int k = -reach; k <= reach; k++)
                {
                    var r = new Vector3(i, j, k) * Spacing;
                    float d2 = r.sqrMagnitude;
                    if (d2 >= h2)
                    {
                        continue;
                    }

                    float a = h2 - d2;
                    weight += _poly6K * a * a * a;
                    if (d2 > 0f)
                    {
                        float d = Mathf.Sqrt(d2);
                        float b = Radius - d;
                        Vector3 g = -_spikyK * b * b * (r / d);
                        sum += g;
                        gradients += g.sqrMagnitude;
                    }
                }
            }
        }

        gradients = (gradients + sum.sqrMagnitude) / (weight * weight);
        return (weight, gradients);
    }

    // So many particles: the first so many of the buffer (and the drawing's points).
    private void SetCount(int count)
    {
        _alive.SetData(new[] { (uint)count, 0u });
        Arguments.SetData(new[] { (uint)count * 6u, 1u, 0u, 0u });
        LastCount = count;
    }

    // The columns' textures at the square's columns (made again at another size).
    private void Columns()
    {
        int columns = Math.Max(1, _cells / _perVoxel);
        if (_columnCount != null && _columnCount.width == columns)
        {
            return;
        }

        FreeColumns();
        _columnCount = MakeColumns("column count", columns, RenderTextureFormat.RInt);
        _columnTop = MakeColumns("column top", columns, RenderTextureFormat.RInt);
        _columnLoss = MakeColumns("column loss", columns, RenderTextureFormat.RFloat);
    }

    private void FreeColumns()
    {
        FreeTexture(ref _columnCount);
        FreeTexture(ref _columnTop);
        FreeTexture(ref _columnLoss);
    }

    private static void FreeTexture(ref RenderTexture texture)
    {
        if (texture == null)
        {
            return;
        }

        texture.Release();
        Free(texture);
        texture = null;
    }

    // What every kernel reads (the pull's share for a frame of so many seconds).
    private void Common(float dt, float frame)
    {
        float h2 = Radius * Radius;
        float corrDistance = CorrDistance * Radius;
        float a = h2 - (corrDistance * corrDistance);
        _shader.SetInt(CapacityId, Capacity);
        _shader.SetInt(BucketsId, Buckets);
        _shader.SetInt(BlocksId, Buckets / Scan);
        _shader.SetInt(NId, _cells);
        _shader.SetFloat(DxId, _cellSize);
        _shader.SetVector(OriginId, _origin);
        _shader.SetFloat(TopId, _top);
        _shader.SetFloat(RadiusId, 0.5f * Spacing);
        _shader.SetFloat(StepUpId, StepUp);
        _shader.SetFloat(HId, Radius);
        _shader.SetFloat(H2Id, h2);
        _shader.SetFloat(Poly6KId, _poly6K);
        _shader.SetFloat(SpikyKId, _spikyK);
        _shader.SetFloat(RestWeightId, _restWeight);
        _shader.SetFloat(RestNumberId, 1f / Litre / _restWeight);
        _shader.SetFloat(RelaxationId, Relaxation * _restGradients);
        _shader.SetFloat(CorrKId, Tension);
        _shader.SetFloat(CorrWId, _poly6K * a * a * a);
        _shader.SetFloat(ViscosityId, Viscosity);
        _shader.SetFloat(MaxSpeedId, MaxSpeed);
        _shader.SetFloat(DtId, dt);
        _shader.SetFloat(GravityId, SimGrid.Gravity);
        _shader.SetVector(SphereId, _sphere);
        _shader.SetInt(BodyCountId, _bodyCount);
        _shader.SetBuffer(_apply, BodiesId, _bodies);
        _shader.SetInt(SpoutCountId, _spoutCount);
        _shader.SetFloat(FrameId, frame);
        _shader.SetBuffer(_spout, SpoutsId, _spouts);
        _shader.SetInt(OwnId, _target != null ? 1 : 0);
        _shader.SetInt(PerVoxelId, _perVoxel);
        _shader.SetInt(BorderId, _border);
        _shader.SetFloat(PullId, Mathf.Min(1f, Pull * frame));
        _shader.SetFloat(SeaLevelId, float.IsNegativeInfinity(_seaLevel) ? -1e9f : _seaLevel);
        _shader.SetFloat(LitreId, Litre);
        _shader.SetInt(SeedId, (int)_seed);
        _shader.SetInt(SolidsOnId, _solids != null ? 1 : 0);
        _shader.SetInt(SolidColumnsId, _solidColumns);
        _shader.SetInt(SolidRowsId, _solidRows);
        _shader.SetFloat(VoxelSizeId, Math.Max(_voxelSize, 0.001f));
        _shader.SetFloat(SolidY0Id, _solidY0);
        _shader.SetTexture(_apply, SolidsId, _solids ?? _noSolids);
        foreach (int kernel in new[] { _fill, _spout, _lambda, _displace, _apply, _finish })
        {
            _shader.SetTexture(kernel, BedId, _bed);
        }

        Columns();
        _shader.SetTexture(_fill, TargetId, _target ?? _noTarget);
        foreach (int kernel in new[] { _clearColumns, _countColumns, _fill, _spout, _finish })
        {
            _shader.SetTexture(kernel, ColumnCountId, _columnCount);
            _shader.SetTexture(kernel, ColumnTopId, _columnTop);
            _shader.SetTexture(kernel, ColumnLossId, _columnLoss);
        }
    }

    // Each column's particles brought toward the server's water there: counted, the lack filled, the excess marked.
    private void Own()
    {
        int columns = Math.Max(1, _cells / _perVoxel);
        int groups = (columns + 7) / 8;
        _shader.Dispatch(_clearColumns, groups, groups, 1);
        _shader.SetBuffer(_countColumns, ParticlesId, _particles[_current]);
        _shader.SetBuffer(_countColumns, AliveId, _alive);
        DispatchParticles(_countColumns);
        _shader.SetBuffer(_fill, ParticlesId, _particles[_current]);
        _shader.SetBuffer(_fill, AliveId, _alive);
        _shader.Dispatch(_fill, groups, groups, 1);
    }

    // One step: the particles guessed on and sorted into their buckets (the gone ones left out), pulled back to rest
    // so many rounds, finished.
    private void Advance()
    {
        _shader.SetBuffer(_clearCounts, CountsId, _counts);
        _shader.Dispatch(_clearCounts, Buckets / Scan, 1, 1);

        int from = _current, to = 1 - _current;
        _shader.SetBuffer(_predict, ParticlesId, _particles[from]);
        _shader.SetBuffer(_predict, AliveId, _alive);
        _shader.SetBuffer(_predict, CountsId, _counts);
        _shader.SetBuffer(_predict, HashesId, _hashes);
        _shader.SetBuffer(_predict, SlotsId, _slots);
        DispatchParticles(_predict);

        _shader.SetBuffer(_scanBlocks, CountsId, _counts);
        _shader.SetBuffer(_scanBlocks, StartsId, _starts);
        _shader.SetBuffer(_scanBlocks, BlockSumsId, _blockSums);
        _shader.Dispatch(_scanBlocks, Buckets / Scan, 1, 1);
        _shader.SetBuffer(_scanSums, BlockSumsId, _blockSums);
        _shader.SetBuffer(_scanSums, AliveId, _alive);
        _shader.Dispatch(_scanSums, 1, 1, 1);
        _shader.SetBuffer(_addSums, StartsId, _starts);
        _shader.SetBuffer(_addSums, BlockSumsId, _blockSums);
        _shader.Dispatch(_addSums, Buckets / Scan, 1, 1);

        _shader.SetBuffer(_scatter, ParticlesId, _particles[from]);
        _shader.SetBuffer(_scatter, SortedId, _particles[to]);
        _shader.SetBuffer(_scatter, AliveId, _alive);
        _shader.SetBuffer(_scatter, StartsId, _starts);
        _shader.SetBuffer(_scatter, HashesId, _hashes);
        _shader.SetBuffer(_scatter, SlotsId, _slots);
        DispatchParticles(_scatter);
        (from, to) = (to, from);

        _shader.SetBuffer(_settle, AliveId, _alive);
        _shader.SetBuffer(_settle, ArgsId, Arguments);
        _shader.Dispatch(_settle, 1, 1, 1);

        foreach (int kernel in new[] { _lambda, _displace, _apply, _finish })
        {
            _shader.SetBuffer(kernel, ParticlesId, _particles[from]);
            _shader.SetBuffer(kernel, AliveId, _alive);
            _shader.SetBuffer(kernel, CountsId, _counts);
            _shader.SetBuffer(kernel, StartsId, _starts);
        }

        _shader.SetBuffer(_displace, DeltaId, _delta);
        _shader.SetBuffer(_apply, DeltaId, _delta);
        for (int k = 0; k < Iterations; k++)
        {
            DispatchParticles(_lambda);
            DispatchParticles(_displace);
            DispatchParticles(_apply);
        }

        _shader.SetBuffer(_finish, SortedId, _particles[to]);
        DispatchParticles(_finish);
        _current = to;
    }

    private void DispatchParticles(int kernel) => _shader.Dispatch(kernel, (Capacity + Threads - 1) / Threads, 1, 1);
}
