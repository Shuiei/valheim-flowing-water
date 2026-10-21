namespace FlowingWater.Server;

/// <summary>A zone's water on the server: the data kept in its ZDO plus the flow between its cells.</summary>
internal sealed class ServerTile
{
    public ServerTile(Vector2s zone, ZDO zdo, int size)
    {
        Data = new TileData(zone, size);
        Zdo = zdo;
        Flux = new float[Directions.Count][];
        ClearFlux();
    }

    public TileData Data { get; }

    public ZDO Zdo { get; set; }

    /// <summary>Water leaving each cell towards each of its eight neighbours, cubic metres per second (a "pipe" each, with inertia).</summary>
    public float[][] Flux { get; }

    /// <summary>Cells whose pipes carried water last step (their flows must be stopped once dry).</summary>
    public bool[] Flowing { get; private set; }

    /// <summary>Cells water flows into this step, wet or not (dry cells without it are skipped).</summary>
    public bool[] Active { get; private set; }

    /// <summary>Depths changed since the last send.</summary>
    public bool Dirty { get; set; }

    /// <summary>A source fed the tile this step.</summary>
    public bool HasSource { get; set; }

    /// <summary>Seconds the tile has been dry with no source.</summary>
    public float EmptyFor { get; set; }

    /// <summary>The terrain version the heights were copied from; -1 before the first copy.</summary>
    public int TerrainVersion { get; set; } = -1;

    /// <summary>Cubic metres in the tile after the last step.</summary>
    public float Water { get; set; }

    /// <summary>Puts the water on a grid of this many cells per side; the flows start again from rest.</summary>
    public void Resize(int size)
    {
        if (size == Data.Size)
        {
            return;
        }

        Data.Resample(size);
        ClearFlux();
        TerrainVersion = -1;
        Dirty = true;
    }

    private void ClearFlux()
    {
        for (int i = 0; i < Flux.Length; i++)
        {
            Flux[i] = new float[Data.Cells];
        }

        Flowing = new bool[Data.Cells];
        Active = new bool[Data.Cells];
    }
}
