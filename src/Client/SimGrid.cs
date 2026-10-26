using System;
using UnityEngine;

namespace FlowingWater.Client;

/// <summary>
/// The water near the camera as the graphics card moves it (the mod's FlowingWaterSim compute shader): shallow water
/// over a square of cells finer than the server's voxels, a depth over the ground in each, a speed through each face.
/// The server's water (<see cref="SetTarget"/>) says how much water is where; between its changes the card moves it
/// as water moves, pulled back toward it a little each frame. Needs only Unity (the game is not looked at), so the
/// Unity project's preview runs it too.
/// </summary>
internal sealed class SimGrid : IDisposable
{
    /// <summary>Metres a second squared.</summary>
    public const float Gravity = 9.81f;

    /// <summary>A texel of <see cref="Target"/>'s top (red) with no water.</summary>
    public const float DryTop = -10000f;

    /// <summary>The ground of a cell whose ground is not known: so high no water goes there.</summary>
    public const float Wall = 10000f;

    /// <summary>
    /// Ground this much lower than the ground beside it (and its water too), metres, is past a lip: water running over it
    /// falls, thrown while <see cref="Throws"/>.
    /// </summary>
    public const float LipDrop = 0.6f;

    /// <summary>The units of <see cref="Landed"/> in a cubic metre (the compute shaders' LAND_SCALE).</summary>
    public const float LandScale = 1000000f;

    // A step is kept stable for water this deep running this fast (m, m/s): a wave crosses at most half a cell. Deeper
    // water carries its flow as if this deep (its waves a little slower), so the step never needs to be shorter.
    private const float DeepestStable = 8f;

    private const float FastestStable = 6f;

    // The longest frame stepped whole, seconds: a longer one (a hitch) is stepped as this long, the water a moment slower.
    private const float LongestFrame = 1f / 15f;

    // A second, how much of the difference between its water's level and the server's each voxel column makes up, and how
    // much of its water goes where the server has none (slower: a surge running ahead of the server keeps on a moment).
    private const float Relax = 0.8f;

    private const float Drain = 0.4f;

    // Cells along the square's edge holding the server's water as it is (the water beyond is not moved here).
    private const int BorderCells = 6;

    private const int Group = 8;

    private static readonly int HId = Shader.PropertyToID("_H");
    private static readonly int HNextId = Shader.PropertyToID("_HNext");
    private static readonly int BedId = Shader.PropertyToID("_Bed");
    private static readonly int ScaleId = Shader.PropertyToID("_Scale");
    private static readonly int UId = Shader.PropertyToID("_U");
    private static readonly int VId = Shader.PropertyToID("_V");
    private static readonly int UFromId = Shader.PropertyToID("_UFrom");
    private static readonly int VFromId = Shader.PropertyToID("_VFrom");
    private static readonly int QXId = Shader.PropertyToID("_QX");
    private static readonly int QYId = Shader.PropertyToID("_QY");
    private static readonly int PullId = Shader.PropertyToID("_Pull");
    private static readonly int TargetId = Shader.PropertyToID("_Target");
    private static readonly int LayersId = Shader.PropertyToID("_Layers");
    private static readonly int BedRowsId = Shader.PropertyToID("_BedRows");
    private static readonly int BedRowId = Shader.PropertyToID("_BedRow");
    private static readonly int BedRowCountId = Shader.PropertyToID("_BedRowCount");
    private static readonly int NId = Shader.PropertyToID("_N");
    private static readonly int PerVoxelId = Shader.PropertyToID("_PerVoxel");
    private static readonly int DxId = Shader.PropertyToID("_Dx");
    private static readonly int DtId = Shader.PropertyToID("_Dt");
    private static readonly int GravityId = Shader.PropertyToID("_Gravity");
    private static readonly int FrictionId = Shader.PropertyToID("_Friction");
    private static readonly int MaxSpeedId = Shader.PropertyToID("_MaxSpeed");
    private static readonly int MaxDepthId = Shader.PropertyToID("_MaxDepth");
    private static readonly int SeaLevelId = Shader.PropertyToID("_SeaLevel");
    private static readonly int RelaxId = Shader.PropertyToID("_Relax");
    private static readonly int DrainId = Shader.PropertyToID("_Drain");
    private static readonly int BorderId = Shader.PropertyToID("_Border");
    private static readonly int AirborneId = Shader.PropertyToID("_Airborne");
    private static readonly int LandedId = Shader.PropertyToID("_Landed");
    private static readonly int ShiftFromId = Shader.PropertyToID("_ShiftFrom");
    private static readonly int ShiftToId = Shader.PropertyToID("_ShiftTo");
    private static readonly int ShiftId = Shader.PropertyToID("_Shift");
    private static readonly int OutsideId = Shader.PropertyToID("_Outside");
    private static readonly int LandedXId = Shader.PropertyToID("_LandedX");
    private static readonly int LandedZId = Shader.PropertyToID("_LandedZ");
    private static readonly int ThrowId = Shader.PropertyToID("_Throw");
    private static readonly int LipDropId = Shader.PropertyToID("_LipDrop");
    private static readonly int DrawnId = Shader.PropertyToID("_Drawn");

    private readonly ComputeShader _shader;

    private readonly int _seed;
    private readonly int _fluxes;
    private readonly int _momentum;
    private readonly int _outflow;
    private readonly int _depth;
    private readonly int _reduce;
    private readonly int _relax;
    private readonly int _writeBed;
    private readonly int _landSpeeds;
    private readonly int _land;
    private readonly int _clear;
    private readonly int _shiftCells;
    private readonly int _shape;

    // Depth in two (a step reads one and writes the other), the speeds in two pairs (likewise), what crosses the faces
    // at a step's start, the ground, each cell's share of its outflow, each voxel column's pull.
    private readonly RenderTexture[] _h = new RenderTexture[2];
    private readonly RenderTexture[] _u = new RenderTexture[2];
    private readonly RenderTexture[] _v = new RenderTexture[2];
    private readonly RenderTexture _qx;
    private readonly RenderTexture _qy;
    private readonly RenderTexture _scale;
    private readonly RenderTexture _pull;
    private readonly float[] _targetPixels;
    private readonly float[] _noLayers;

    private ComputeBuffer _bedRows;

    private int _current;

    private int _speeds;

    /// <summary>A square of <paramref name="cells"/> cells a side, each <paramref name="cellSize"/> metres, <paramref name="perVoxel"/> to a voxel's side.</summary>
    public SimGrid(ComputeShader shader, int cells, float cellSize, int perVoxel)
    {
        if (cells % (perVoxel * Group) != 0)
        {
            throw new ArgumentException($"{cells} cells is not a whole number of {Group}-voxel groups of {perVoxel} cells", nameof(cells));
        }

        _shader = shader;
        Cells = cells;
        CellSize = cellSize;
        PerVoxel = perVoxel;
        _seed = shader.FindKernel("Seed");
        _fluxes = shader.FindKernel("Fluxes");
        _momentum = shader.FindKernel("Momentum");
        _outflow = shader.FindKernel("Outflow");
        _depth = shader.FindKernel("Depth");
        _reduce = shader.FindKernel("Reduce");
        _relax = shader.FindKernel("Relax");
        _writeBed = shader.FindKernel("WriteBed");
        _landSpeeds = shader.FindKernel("LandSpeeds");
        _land = shader.FindKernel("Land");
        for (int k = 0; k < 2; k++)
        {
            _h[k] = MakeCells("depth " + k, cells);
            _u[k] = MakeCells("speed east " + k, cells);
            _v[k] = MakeCells("speed north " + k, cells);
        }

        _qx = MakeCells("flow east", cells);
        _qy = MakeCells("flow north", cells);
        Bed = MakeCells("ground", cells);
        _scale = MakeCells("outflow share", cells);
        _pull = MakeCells("pull", Columns, RenderTextureFormat.ARGBFloat);
        Airborne = MakeCells("airborne", cells);
        Landed = MakeCells("landed", cells, RenderTextureFormat.RInt);
        LandedEast = MakeCells("landed east", cells, RenderTextureFormat.RInt);
        LandedNorth = MakeCells("landed north", cells, RenderTextureFormat.RInt);
        _clear = shader.FindKernel("Clear");
        _shiftCells = shader.FindKernel("ShiftCells");
        _shape = shader.FindKernel("Shape");
        Drawn = MakeCells("drawn", cells);
        ClearThrown();
        Target = new Texture2D(Columns, Columns, TextureFormat.RGBAFloat, false, true)
        {
            name = "FlowingWater sim target",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        _targetPixels = new float[Columns * Columns * 4];
        for (int k = 0; k < Columns * Columns; k++)
        {
            _targetPixels[k * 4] = DryTop;
        }

        Target.SetPixelData(_targetPixels, 0);
        Target.Apply(false);
        Layers = new Texture2D(Columns, Columns, TextureFormat.RGBAFloat, false, true)
        {
            name = "FlowingWater sim layers",
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
        };
        _noLayers = new float[Columns * Columns * 4];
        for (int k = 0; k < _noLayers.Length; k++)
        {
            _noLayers[k] = (k & 3) == 3 ? 0f : DryTop;
        }

        Layers.SetPixelData(_noLayers, 0);
        Layers.Apply(false);
        SeaLevel = float.NegativeInfinity;

        // No ground is known yet: every cell is a wall until its own is written.
        float[] walls = new float[cells * cells];
        for (int k = 0; k < walls.Length; k++)
        {
            walls[k] = Wall;
        }

        WriteBed(0, walls, cells);
    }

    /// <summary>
    /// The water as it is drawn, metres over each cell's ground, as <see cref="Shape"/> last made it: its level smoothed
    /// over the cells round it, the small bumps of a sheet's ground covered.
    /// </summary>
    public RenderTexture Drawn { get; }

    /// <summary>Cells along a side.</summary>
    public int Cells { get; }

    /// <summary>Metres along a cell's side.</summary>
    public float CellSize { get; }

    /// <summary>Cells along a voxel's side.</summary>
    public int PerVoxel { get; }

    /// <summary>Voxel columns along a side.</summary>
    public int Columns => Cells / PerVoxel;

    /// <summary>Water on ground under this level (world height) is the sea's: none is kept there.</summary>
    public float SeaLevel { get; set; }

    /// <summary>The depth in each cell now, metres.</summary>
    public RenderTexture Depth => _h[_current];

    /// <summary>The ground under each cell, world height.</summary>
    public RenderTexture Bed { get; }

    /// <summary>The speed through each cell's east face, m/s.</summary>
    public RenderTexture SpeedEast => _u[_speeds];

    /// <summary>The speed through each cell's north face, m/s.</summary>
    public RenderTexture SpeedNorth => _v[_speeds];

    /// <summary>
    /// The server's water, a texel per voxel column: its resting top (world height, <see cref="DryTop"/> for none),
    /// whether the card draws it there, its depth (metres), the faces pieces close (1 east, 2 north) plus half its foam.
    /// </summary>
    public Texture2D Target { get; }

    /// <summary>
    /// The server's water's ledges and falls, a texel per voxel column: where a ledge crosses it, the top of the water
    /// at its foot (<see cref="DryTop"/> for none) and the height under which a cell's ground is at its foot (DryTop: no
    /// ledge); the top of water falling through the air over it (DryTop for none); nothing.
    /// </summary>
    public Texture2D Layers { get; }

    /// <summary>Water thrown over a lip, by the cell it left (m³), until the spray throws it (and takes it from here).</summary>
    public RenderTexture Airborne { get; }

    /// <summary>
    /// Water landed by the cell it fell in (m³ times <see cref="LandScale"/>, whole numbers), added to the cells at the next
    /// <see cref="Step"/>.
    /// </summary>
    public RenderTexture Landed { get; }

    /// <summary>The momentum of the water landed east and north, by the cell (m³ times m/s, times <see cref="LandScale"/>).</summary>
    public RenderTexture LandedEast { get; }

    /// <inheritdoc cref="LandedEast"/>
    public RenderTexture LandedNorth { get; }

    /// <summary>
    /// Whether water running over a lip is thrown (<see cref="Airborne"/>, for the spray to carry and land), not run into
    /// the cell past it: while the spray throws falls.
    /// </summary>
    public bool Throws { get; set; }

    /// <summary>The ground's roughness (Manning's: 0.03 a natural bed, as the server's; 0 none, for previews).</summary>
    public float Roughness { get; set; } = 0.03f;

    /// <summary>
    /// Whether the water is pulled toward the server's after each <see cref="Step"/> (else it moves on its own, closed in by
    /// the square's edge: for previews and tests).
    /// </summary>
    public bool Pulled { get; set; } = true;

    /// <summary>The substeps the last <see cref="Step"/> took.</summary>
    public int Substeps { get; private set; }

    /// <summary>
    /// Sets the server's water (four floats a voxel column, as <see cref="Target"/>'s texels, and as many of its ledges
    /// and falls, as <see cref="Layers"/>'; null: none), for the next steps.
    /// </summary>
    public void SetTarget(float[] texels, float[] layers = null)
    {
        if (texels.Length != _targetPixels.Length)
        {
            throw new ArgumentException($"{texels.Length} floats for {Columns * Columns} columns", nameof(texels));
        }

        if (layers != null && layers.Length != _noLayers.Length)
        {
            throw new ArgumentException($"{layers.Length} floats for {Columns * Columns} columns", nameof(layers));
        }

        Target.SetPixelData(texels, 0);
        Target.Apply(false);
        Layers.SetPixelData(layers ?? _noLayers, 0);
        Layers.Apply(false);
    }

    /// <summary>
    /// Writes the ground of <paramref name="rows"/> rows of cells from row <paramref name="row"/>, from the square's
    /// ground (world heights, a row after another). A cell found a wall loses its water; one whose ground is found where
    /// none was known takes the server's water there, as it is.
    /// </summary>
    public void WriteBed(int row, float[] ground, int rows)
    {
        if (rows <= 0)
        {
            return;
        }

        int count = rows * Cells;
        if (_bedRows == null || _bedRows.count < count)
        {
            _bedRows?.Release();
            _bedRows = new ComputeBuffer(Math.Max(count, Cells * 8), sizeof(float));
        }

        _bedRows.SetData(ground, row * Cells, 0, count);
        _shader.SetBuffer(_writeBed, BedRowsId, _bedRows);
        _shader.SetTexture(_writeBed, BedId, Bed);
        _shader.SetTexture(_writeBed, HId, _h[_current]);
        _shader.SetTexture(_writeBed, TargetId, Target);
        _shader.SetTexture(_writeBed, LayersId, Layers);
        _shader.SetInt(BedRowId, row);
        _shader.SetInt(BedRowCountId, rows);
        Common();
        _shader.Dispatch(_writeBed, (Cells + 63) / 64, rows, 1);
    }

    /// <summary>Sets every cell's depth (metres, a row after another), still: for previews and tests.</summary>
    public void SetDepth(float[] depth)
    {
        var texture = new Texture2D(Cells, Cells, TextureFormat.RFloat, false, true);
        texture.SetPixelData(depth, 0);
        texture.Apply(false);
        Graphics.CopyTexture(texture, _h[_current]);
        Free(texture);
    }

    /// <summary>The water as the server has it, still (where the card had none, or the square moved).</summary>
    public void Seed()
    {
        Bind(_seed, HId, _h[_current]);
        Bind(_seed, UId, _u[_speeds]);
        Bind(_seed, VId, _v[_speeds]);
        Bind(_seed, BedId, Bed);
        _shader.SetTexture(_seed, TargetId, Target);
        _shader.SetTexture(_seed, LayersId, Layers);
        Common();
        Dispatch(_seed, Cells);
    }

    /// <summary>Moves the water over this much time (in as many short steps as it takes), then pulls it toward the server's.</summary>
    public void Step(float seconds)
    {
        seconds = Math.Min(seconds, LongestFrame);
        if (seconds <= 0f)
        {
            Substeps = 0;
            return;
        }

        float longest = 0.5f * CellSize / ((float)Math.Sqrt(Gravity * DeepestStable) + FastestStable);
        int steps = Math.Max(1, (int)Math.Ceiling(seconds / longest));
        float dt = seconds / steps;
        float fastest = 0.5f * CellSize / dt;
        Common();
        _shader.SetFloat(MaxSpeedId, fastest);
        Bind(_landSpeeds, HId, _h[_current]);
        Bind(_landSpeeds, UId, _u[_speeds]);
        Bind(_landSpeeds, VId, _v[_speeds]);
        BindLanded(_landSpeeds);
        Dispatch(_landSpeeds, Cells);
        Bind(_land, HId, _h[_current]);
        BindLanded(_land);
        Dispatch(_land, Cells);
        _shader.SetFloat(DtId, dt);
        _shader.SetFloat(MaxDepthId, fastest * fastest / Gravity);
        for (int k = 0; k < steps; k++)
        {
            Advance();
        }

        Substeps = steps;
        if (!Pulled)
        {
            return;
        }

        _shader.SetFloat(DtId, seconds);
        Bind(_reduce, HId, _h[_current]);
        Bind(_reduce, BedId, Bed);
        Bind(_reduce, PullId, _pull);
        _shader.SetTexture(_reduce, TargetId, Target);
        _shader.SetTexture(_reduce, LayersId, Layers);
        Dispatch(_reduce, Columns);
        Bind(_relax, HId, _h[_current]);
        Bind(_relax, BedId, Bed);
        Bind(_relax, PullId, _pull);
        _shader.SetTexture(_relax, TargetId, Target);
        _shader.SetTexture(_relax, LayersId, Layers);
        Dispatch(_relax, Cells);
    }

    /// <summary>
    /// The water as the square moved by <paramref name="dx"/>, <paramref name="dz"/> cells sees it (the camera went on):
    /// each cell's depth, speeds and ground taken from its place plus the move, so the water goes on as it was; the cells
    /// new to the square dry, their ground not known (each takes the server's water as its ground is written). The water
    /// in the air is let go (a frame's: the server's pulls the card's back to it). The server's water is set again after.
    /// </summary>
    public void Shift(int dx, int dz)
    {
        Common();
        _shader.SetInts(ShiftId, dx, dz);
        ShiftInto(_h[_current], _h[1 - _current], 0f);
        _current = 1 - _current;
        ShiftInto(_u[_speeds], _u[1 - _speeds], 0f);
        ShiftInto(_v[_speeds], _v[1 - _speeds], 0f);
        _speeds = 1 - _speeds;
        ShiftInto(Bed, _scale, Wall);
        Graphics.CopyTexture(_scale, Bed);
        ClearThrown();
    }

    /// <summary>Shapes the water as it is to be drawn (<see cref="Drawn"/>) from the water as it is now.</summary>
    public void Shape()
    {
        Bind(_shape, HId, _h[_current]);
        Bind(_shape, BedId, Bed);
        Bind(_shape, DrawnId, Drawn);
        Common();
        Dispatch(_shape, Cells);
    }

    /// <summary>No water in the air nor landed (the spray that would carry or land it is gone).</summary>
    public void ClearThrown()
    {
        Bind(_clear, AirborneId, Airborne);
        BindLanded(_clear);
        Common();
        Dispatch(_clear, Cells);
    }

    public void Dispose()
    {
        foreach (RenderTexture texture in new[] { _h[0], _h[1], _u[0], _u[1], _v[0], _v[1], _qx, _qy, Bed, _scale, _pull, Airborne, Landed, LandedEast, LandedNorth, Drawn })
        {
            texture.Release();
            Free(texture);
        }

        Free(Target);
        Free(Layers);
        _bedRows?.Release();
    }

    // One step: what crosses the faces, the speeds moved (into the other pair), the shares, the depths.
    private void Advance()
    {
        Bind(_fluxes, HId, _h[_current]);
        Bind(_fluxes, BedId, Bed);
        Bind(_fluxes, UFromId, _u[_speeds]);
        Bind(_fluxes, VFromId, _v[_speeds]);
        Bind(_fluxes, QXId, _qx);
        Bind(_fluxes, QYId, _qy);
        Dispatch(_fluxes, Cells);

        Bind(_momentum, HId, _h[_current]);
        Bind(_momentum, BedId, Bed);
        Bind(_momentum, UFromId, _u[_speeds]);
        Bind(_momentum, VFromId, _v[_speeds]);
        Bind(_momentum, UId, _u[1 - _speeds]);
        Bind(_momentum, VId, _v[1 - _speeds]);
        Bind(_momentum, QXId, _qx);
        Bind(_momentum, QYId, _qy);
        _shader.SetTexture(_momentum, TargetId, Target);
        Dispatch(_momentum, Cells);
        _speeds = 1 - _speeds;

        Bind(_outflow, UId, _u[_speeds]);
        Bind(_outflow, VId, _v[_speeds]);
        Bind(_outflow, ScaleId, _scale);
        Dispatch(_outflow, Cells);

        Bind(_depth, HId, _h[_current]);
        Bind(_depth, HNextId, _h[1 - _current]);
        Bind(_depth, BedId, Bed);
        Bind(_depth, UId, _u[_speeds]);
        Bind(_depth, VId, _v[_speeds]);
        Bind(_depth, ScaleId, _scale);
        Bind(_depth, AirborneId, Airborne);

        // Which columns the card draws: water runs over a lip, thrown, only from those (where the bricks draw the water,
        // they draw its falls).
        _shader.SetTexture(_depth, TargetId, Target);
        Dispatch(_depth, Cells);
        _current = 1 - _current;
    }

    // What every kernel reads.
    private void Common()
    {
        _shader.SetInt(NId, Cells);
        _shader.SetInt(PerVoxelId, PerVoxel);
        _shader.SetFloat(DxId, CellSize);
        _shader.SetFloat(GravityId, Gravity);
        _shader.SetFloat(FrictionId, Gravity * Roughness * Roughness);
        _shader.SetFloat(SeaLevelId, float.IsNegativeInfinity(SeaLevel) ? -1e9f : SeaLevel);
        _shader.SetFloat(RelaxId, Relax);
        _shader.SetFloat(DrainId, Drain);
        _shader.SetInt(BorderId, BorderCells);
        _shader.SetInt(ThrowId, Throws ? 1 : 0);
        _shader.SetFloat(LipDropId, LipDrop);
    }

    private void Bind(int kernel, int id, RenderTexture texture) => _shader.SetTexture(kernel, id, texture);

    private void ShiftInto(RenderTexture from, RenderTexture to, float outside)
    {
        Bind(_shiftCells, ShiftFromId, from);
        Bind(_shiftCells, ShiftToId, to);
        _shader.SetFloat(OutsideId, outside);
        Dispatch(_shiftCells, Cells);
    }

    private void BindLanded(int kernel)
    {
        Bind(kernel, LandedId, Landed);
        Bind(kernel, LandedXId, LandedEast);
        Bind(kernel, LandedZId, LandedNorth);
    }

    // Gone: in the game as Unity frees things, in the editor's preview (not playing) at once, as it must there.
    private static void Free(UnityEngine.Object thing)
    {
        if (Application.isPlaying)
        {
            UnityEngine.Object.Destroy(thing);
        }
        else
        {
            UnityEngine.Object.DestroyImmediate(thing);
        }
    }

    private void Dispatch(int kernel, int size) => _shader.Dispatch(kernel, (size + Group - 1) / Group, (size + Group - 1) / Group, 1);

    private static RenderTexture MakeCells(string name, int side, RenderTextureFormat format = RenderTextureFormat.RFloat)
    {
        var texture = new RenderTexture(side, side, 0, format, RenderTextureReadWrite.Linear)
        {
            name = "FlowingWater sim " + name,
            enableRandomWrite = true,
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,
        };
        texture.Create();
        return texture;
    }
}
