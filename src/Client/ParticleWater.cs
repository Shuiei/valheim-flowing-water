using System;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// Water as particles on the graphics card (the mod's FlowingWaterParticles compute shader), a prototype: position based
/// fluids over a hashed grid, each particle a cube of water <see cref="Spacing"/> on a side, kept out of the ground the
/// card's water knows (<see cref="SimGrid.Bed"/>), the square's walls and a sphere. Nothing is sent anywhere. Needs only
/// Unity, so the Unity project's preview runs it (ParticlePreview) before the game does.
/// </summary>
internal sealed class ParticleWater : IDisposable
{
    /// <summary>A particle's water: a cube this long on a side (m), at rest; a litre.</summary>
    public const float Spacing = 0.1f;

    /// <summary>The smoothing radius (m): how far a particle feels its neighbours. Twice the spacing: about 30 of them.</summary>
    public const float Radius = 2f * Spacing;

    // A particle's floats: place, the step's guess and its pull, velocity and density.
    private const int Stride = 12 * sizeof(float);

    // The hashed grid's buckets (a power of two) and the buckets a scan block holds (the compute shader's SCAN).
    private const int Buckets = 1 << 18;

    private const int Scan = 1024;

    private const int Threads = 256;

    // Macklin and Müller's artificial pressure is measured against the kernel this share of the radius away.
    private const float CorrDistance = 0.2f;

    // The smoothing kernels' constants (Müller et al., 2003): poly6's 315 / (64 pi h^9), spiky's gradient's 45 / (pi h^6).
    private static readonly float Poly6K = 315f / (64f * Mathf.PI * Mathf.Pow(Radius, 9f));

    private static readonly float SpikyK = 45f / (Mathf.PI * Mathf.Pow(Radius, 6f));

    private static readonly int ParticlesId = Shader.PropertyToID("_Particles");
    private static readonly int SortedId = Shader.PropertyToID("_Sorted");
    private static readonly int CountId = Shader.PropertyToID("_Count");
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

    private readonly ComputeShader _shader;

    private readonly int _clearCounts;
    private readonly int _predict;
    private readonly int _scanBlocks;
    private readonly int _scanSums;
    private readonly int _addSums;
    private readonly int _scatter;
    private readonly int _lambda;
    private readonly int _displace;
    private readonly int _apply;
    private readonly int _finish;

    // The particles, and the other buffer they are sorted and finished into (swapped each time).
    private readonly ComputeBuffer[] _particles = new ComputeBuffer[2];

    private readonly ComputeBuffer _counts;
    private readonly ComputeBuffer _starts;
    private readonly ComputeBuffer _blockSums;
    private readonly ComputeBuffer _hashes;
    private readonly ComputeBuffer _slots;
    private readonly ComputeBuffer _delta;

    // The sums over a lattice of particles at their spacing: of the smoothing kernel (water at rest), and of the squared
    // kernel gradients (what the relaxation is a share of).
    private readonly float _restWeight;

    private readonly float _restGradients;

    private int _current;

    private Texture _bed;

    private int _cells;

    private float _cellSize;

    private Vector2 _origin;

    private float _top;

    private Vector4 _sphere = new(0f, 0f, 0f, -1f);

    /// <summary>
    /// Room for <paramref name="capacity"/> particles, moved by <paramref name="shader"/> and drawn with
    /// <paramref name="material"/>.
    /// </summary>
    public ParticleWater(ComputeShader shader, Material material, int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "no room for particles");
        }

        _shader = shader;
        Material = material;
        Capacity = capacity;
        _clearCounts = shader.FindKernel("ClearCounts");
        _predict = shader.FindKernel("Predict");
        _scanBlocks = shader.FindKernel("ScanBlocks");
        _scanSums = shader.FindKernel("ScanSums");
        _addSums = shader.FindKernel("AddSums");
        _scatter = shader.FindKernel("Scatter");
        _lambda = shader.FindKernel("Lambda");
        _displace = shader.FindKernel("Displace");
        _apply = shader.FindKernel("Apply");
        _finish = shader.FindKernel("Finish");
        for (int k = 0; k < 2; k++)
        {
            _particles[k] = new ComputeBuffer(capacity, Stride);
        }

        _counts = new ComputeBuffer(Buckets, sizeof(uint));
        _starts = new ComputeBuffer(Buckets, sizeof(uint));
        _blockSums = new ComputeBuffer(Buckets / Scan, sizeof(uint));
        _hashes = new ComputeBuffer(capacity, sizeof(uint));
        _slots = new ComputeBuffer(capacity, sizeof(uint));
        _delta = new ComputeBuffer(capacity, 4 * sizeof(float));
        (_restWeight, _restGradients) = Lattice();
        Properties.SetBuffer(ParticlesId, _particles[0]);
    }

    /// <summary>Particles at most.</summary>
    public int Capacity { get; }

    /// <summary>Particles now.</summary>
    public int Count { get; private set; }

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

    /// <summary>The fastest any particle goes (m/s): a speed from a mended guess is held there, not thrown on.</summary>
    public float MaxSpeed { get; set; } = 25f;

    /// <summary>Ground higher than a particle by more than this (m) is a wall to it, not a step it rides up.</summary>
    public float StepUp { get; set; } = 0.35f;

    /// <summary>Steps the last <see cref="Step"/> took.</summary>
    public int Substeps { get; private set; }

    /// <summary>What they are drawn with: <see cref="Count"/> times six points (two triangles each), with <see cref="Properties"/>.</summary>
    public Material Material { get; }

    /// <summary>The particles' own buffer, for the material.</summary>
    public MaterialPropertyBlock Properties { get; } = new();

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

    /// <summary>A sphere the water meets, at <paramref name="centre"/> (world) of this radius (m); none at a radius of 0 or less.</summary>
    public void SetSphere(Vector3 centre, float radius) => _sphere = new Vector4(centre.x, centre.y, centre.z, radius);

    /// <summary>
    /// A block of still water from <paramref name="min"/> to <paramref name="max"/> (world), a lattice of particles at
    /// their spacing, each nudged by a hair so no two rows fall alike; as many as there is room for.
    /// </summary>
    public int Add(Vector3 min, Vector3 max)
    {
        int nx = Mathf.Max(0, Mathf.FloorToInt((max.x - min.x) / Spacing + 1e-4f));
        int ny = Mathf.Max(0, Mathf.FloorToInt((max.y - min.y) / Spacing + 1e-4f));
        int nz = Mathf.Max(0, Mathf.FloorToInt((max.z - min.z) / Spacing + 1e-4f));
        int count = Math.Min(nx * ny * nz, Capacity - Count);
        if (count <= 0)
        {
            return 0;
        }

        float[] data = new float[count * 12];
        var random = new System.Random(Count + 1);
        int n = 0;
        for (int k = 0; k < count; k++)
        {
            int i = k % nx, j = (k / nx) % ny, l = k / (nx * ny);
            float x = min.x + ((i + 0.5f) * Spacing) + ((float)(random.NextDouble() - 0.5) * 0.02f * Spacing);
            float y = min.y + ((j + 0.5f) * Spacing) + ((float)(random.NextDouble() - 0.5) * 0.02f * Spacing);
            float z = min.z + ((l + 0.5f) * Spacing) + ((float)(random.NextDouble() - 0.5) * 0.02f * Spacing);
            data[n++] = x;
            data[n++] = y;
            data[n++] = z;
            data[n++] = 1f;
            data[n++] = x;
            data[n++] = y;
            data[n++] = z;
            data[n++] = 0f;
            n += 4;
        }

        _particles[_current].SetData(data, 0, Count * 12, count * 12);
        Count += count;
        return count;
    }

    /// <summary>No water: every particle gone.</summary>
    public void Clear()
    {
        Count = 0;
        Properties.SetBuffer(ParticlesId, _particles[_current]);
    }

    /// <summary>Moves the water over this much time, in steps of <see cref="Substep"/> at most.</summary>
    public void Step(float seconds)
    {
        if (seconds <= 0f || Count == 0)
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
        Common(dt);
        for (int k = 0; k < steps; k++)
        {
            Advance();
        }

        Substeps = steps;
        Properties.SetBuffer(ParticlesId, _particles[_current]);
    }

    /// <summary>
    /// Every particle's twelve floats (place and 1, the step's guess and its pull, velocity and density), read back from
    /// the card: for previews and tests.
    /// </summary>
    public float[] Read()
    {
        float[] data = new float[Count * 12];
        if (Count > 0)
        {
            _particles[_current].GetData(data, 0, 0, Count * 12);
        }

        return data;
    }

    /// <summary>Draws them this frame, for every camera, within these bounds (world).</summary>
    public void Draw(Bounds bounds)
    {
        if (Count > 0)
        {
            Graphics.DrawProcedural(Material, bounds, MeshTopology.Triangles, Count * 6, 1, null, Properties);
        }
    }

    public void Dispose()
    {
        _particles[0].Release();
        _particles[1].Release();
        _counts.Release();
        _starts.Release();
        _blockSums.Release();
        _hashes.Release();
        _slots.Release();
        _delta.Release();
    }

    // The smoothing kernels' sums over a lattice of particles at their spacing round one of them (itself counted in the
    // weight): the weight of water at rest, and the squared gradients a particle at rest feels (the compute shader's own
    // kernels, as it sums them).
    private static (float Weight, float Gradients) Lattice()
    {
        float h2 = Radius * Radius;
        float poly = Poly6K;
        float spiky = SpikyK;
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
                    weight += poly * a * a * a;
                    if (d2 > 0f)
                    {
                        float d = Mathf.Sqrt(d2);
                        float b = Radius - d;
                        Vector3 g = -spiky * b * b * (r / d);
                        sum += g;
                        gradients += g.sqrMagnitude;
                    }
                }
            }
        }

        gradients = (gradients + sum.sqrMagnitude) / (weight * weight);
        return (weight, gradients);
    }

    // What every kernel reads.
    private void Common(float dt)
    {
        float h2 = Radius * Radius;
        float corrDistance = CorrDistance * Radius;
        float a = h2 - (corrDistance * corrDistance);
        _shader.SetInt(CountId, Count);
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
        _shader.SetFloat(Poly6KId, Poly6K);
        _shader.SetFloat(SpikyKId, SpikyK);
        _shader.SetFloat(RestWeightId, _restWeight);
        _shader.SetFloat(RestNumberId, 1f / (Spacing * Spacing * Spacing) / _restWeight);
        _shader.SetFloat(RelaxationId, Relaxation * _restGradients);
        _shader.SetFloat(CorrKId, Tension);
        _shader.SetFloat(CorrWId, Poly6K * a * a * a);
        _shader.SetFloat(ViscosityId, Viscosity);
        _shader.SetFloat(MaxSpeedId, MaxSpeed);
        _shader.SetFloat(DtId, dt);
        _shader.SetFloat(GravityId, SimGrid.Gravity);
        _shader.SetVector(SphereId, _sphere);
        foreach (int kernel in new[] { _lambda, _displace, _apply })
        {
            _shader.SetTexture(kernel, BedId, _bed);
        }
    }

    // One step: the particles guessed on and sorted into their buckets, pulled back to rest so many rounds, finished.
    private void Advance()
    {
        _shader.SetBuffer(_clearCounts, CountsId, _counts);
        _shader.Dispatch(_clearCounts, Buckets / Scan, 1, 1);

        int from = _current, to = 1 - _current;
        _shader.SetBuffer(_predict, ParticlesId, _particles[from]);
        _shader.SetBuffer(_predict, CountsId, _counts);
        _shader.SetBuffer(_predict, HashesId, _hashes);
        _shader.SetBuffer(_predict, SlotsId, _slots);
        DispatchParticles(_predict);

        _shader.SetBuffer(_scanBlocks, CountsId, _counts);
        _shader.SetBuffer(_scanBlocks, StartsId, _starts);
        _shader.SetBuffer(_scanBlocks, BlockSumsId, _blockSums);
        _shader.Dispatch(_scanBlocks, Buckets / Scan, 1, 1);
        _shader.SetBuffer(_scanSums, BlockSumsId, _blockSums);
        _shader.Dispatch(_scanSums, 1, 1, 1);
        _shader.SetBuffer(_addSums, StartsId, _starts);
        _shader.SetBuffer(_addSums, BlockSumsId, _blockSums);
        _shader.Dispatch(_addSums, Buckets / Scan, 1, 1);

        _shader.SetBuffer(_scatter, ParticlesId, _particles[from]);
        _shader.SetBuffer(_scatter, SortedId, _particles[to]);
        _shader.SetBuffer(_scatter, StartsId, _starts);
        _shader.SetBuffer(_scatter, HashesId, _hashes);
        _shader.SetBuffer(_scatter, SlotsId, _slots);
        DispatchParticles(_scatter);
        (from, to) = (to, from);

        foreach (int kernel in new[] { _lambda, _displace, _apply, _finish })
        {
            _shader.SetBuffer(kernel, ParticlesId, _particles[from]);
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

    private void DispatchParticles(int kernel) => _shader.Dispatch(kernel, (Count + Threads - 1) / Threads, 1, 1);
}
