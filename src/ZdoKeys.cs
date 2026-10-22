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

    /// <summary>A zone's ground change from erosion, in its FlowingWater_Ground object (ErosionGround).</summary>
    public static readonly int Ground = "FW_Ground".GetStableHashCode();

    /// <summary>A brick's water, in its FlowingWater_Brick object (BrickData).</summary>
    public static readonly int Brick = "FW_Brick".GetStableHashCode();
}
