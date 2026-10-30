namespace FlowingWater;

/// <summary>The keys of what the plugin keeps in its objects' ZDOs, shared by the server and the players' games.</summary>
internal static class ZdoKeys
{
    /// <summary>A zone's water tile (the column water before the voxels): TileData's bytes.</summary>
    public static readonly int Data = "FW_Data".GetStableHashCode();

    /// <summary>A source's or drain's rate, cubic metres a second.</summary>
    public static readonly int Rate = "FW_Rate".GetStableHashCode();

    /// <summary>A drain's reach, metres.</summary>
    public static readonly int Radius = "FW_Radius".GetStableHashCode();

    /// <summary>A source's shape (MarkerShape): 1 for one facing its piece's turn, none or 0 for one out of every side.</summary>
    public static readonly int Shape = "FW_Shape".GetStableHashCode();

    /// <summary>A source's outlet's width, metres (0: a point).</summary>
    public static readonly int Width = "FW_Width".GetStableHashCode();

    /// <summary>How far a source's water fans out, degrees from one side to the other.</summary>
    public static readonly int Spread = "FW_Spread".GetStableHashCode();

    /// <summary>How far up a source's water is thrown, degrees over the ground.</summary>
    public static readonly int Throw = "FW_Throw".GetStableHashCode();

    /// <summary>A zone's ground change from erosion, in its FlowingWater_Ground object (ErosionGround).</summary>
    public static readonly int Ground = "FW_Ground".GetStableHashCode();

    /// <summary>A brick's water, in its FlowingWater_Brick object (BrickData).</summary>
    public static readonly int Brick = "FW_Brick".GetStableHashCode();
}
