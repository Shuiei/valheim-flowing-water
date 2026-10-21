namespace FlowingWater.Server;

/// <summary>A zone's water on the server: the data kept in its ZDO plus the step's working arrays.</summary>
internal sealed class ServerTile
{
    public ServerTile(Vector2s zone, ZDO zdo)
    {
        Data = new TileData(zone);
        Zdo = zdo;
    }

    public TileData Data { get; }

    public ZDO Zdo { get; set; }

    /// <summary>Depth change per cell gathered during a step, applied at its end.</summary>
    public float[] Delta { get; } = new float[TileData.Cells];

    /// <summary>Water that left each cell during the step, as a vector (cubic metres east and north).</summary>
    public float[] FluxX { get; } = new float[TileData.Cells];

    public float[] FluxZ { get; } = new float[TileData.Cells];

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
}
