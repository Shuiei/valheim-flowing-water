namespace FlowingWater.Server;

/// <summary>A zone's water on the server: the data kept in its ZDO plus the flow between its cells.</summary>
internal sealed class ServerTile
{
    public ServerTile(Vector2s zone, ZDO zdo)
    {
        Data = new TileData(zone);
        Zdo = zdo;
        Flux = new float[Directions.Count][];
        for (int i = 0; i < Flux.Length; i++)
        {
            Flux[i] = new float[TileData.Cells];
        }
    }

    public TileData Data { get; }

    public ZDO Zdo { get; set; }

    /// <summary>Water leaving each cell towards each of its eight neighbours, cubic metres per second (a "pipe" each, with inertia).</summary>
    public float[][] Flux { get; }

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
