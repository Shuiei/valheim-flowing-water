using BepInEx.Configuration;

namespace FlowingWater;

/// <summary>The plugin's settings (BepInEx/config/Tie.FlowingWater.cfg).</summary>
internal sealed class Settings
{
    public Settings(ConfigFile config)
    {
        Enabled = config.Bind("General", "Enabled", true, "Simulate (server) and show (client) flowing water.");
        CellSize = config.Bind("Server", "CellSize", 1f, new ConfigDescription("Side of the water's cubic voxels, in metres. Smaller voxels give finer streams, shores and falls. Moving water costs the server about 3 times as much at 0.5 as at 1, and about 15 times at 0.25 (for small builds only); still water stays cheap at any size. Changing it moves the water already there to the new grid.", new AcceptableValueList<float>(TileData.CellSizes)));
        Erosion = config.Bind("Server", "Erosion", false, "Running water wears the ground away where it runs fast and lays it down where it slows, so streams slowly cut their own beds. The ground within 5 m of anything players built, and of the game's own places, is left alone. Turning it off stops it; what it did stays. Paused while the water moves to 3D voxels: it comes back in a later version.");
        ErosionSpeed = config.Bind("Server", "ErosionSpeed", 1f, new ConfigDescription("How fast erosion goes (1 = millimetres a minute in a fast stream); higher to watch it happen, for testing.", new AcceptableValueRange<float>(0.1f, 1000f)));
        SendSeconds = config.Bind("Server", "SendSeconds", 1f, new ConfigDescription("How often a block of water whose change would show is sent to the players near it, in seconds (a change too small to see waits until it adds up, ten sends at most).", new AcceptableValueRange<float>(0.25f, 10f)));
        StepBudget = config.Bind("Server", "StepBudget", 8f, new ConfigDescription("Most milliseconds of each server frame the water may take, on average (a step of the water is taken a part at a time, so a frame may take a part more now and then). Water needing more slows down, sources and drains with it, instead of slowing the server (and the game for its players).", new AcceptableValueRange<float>(1f, 50f)));
        MaxBricks = config.Bind("Server", "MaxBricks", 1024, new ConfigDescription("Most blocks of 16 by 16 by 16 voxels that may hold water at once (each about 130 KB of server memory while its water moves, 37 KB once it is still). Water stops spreading beyond.", new AcceptableValueRange<int>(16, 16384)));
        EmptyTileSeconds = config.Bind("Server", "EmptyTileSeconds", 30f, new ConfigDescription("A block of voxels dry for this long is forgotten (made again when water comes back).", new AcceptableValueRange<float>(5f, 3600f)));
        MarkerScanSeconds = config.Bind("Server", "MarkerScanSeconds", 3f, new ConfigDescription("Seconds from the end of one look for new or removed sources and drains to the next (a look over a big world itself takes a few seconds, a little each frame).", new AcceptableValueRange<float>(1f, 60f)));
        FixedSources = config.Bind("Server", "FixedSources", string.Empty, "Sources that exist without a placed marker, for servers and tests: world x,z and cubic metres per second, like \"120,-340,2; 125,-338,1.5\".");
        FixedDrains = config.Bind("Server", "FixedDrains", string.Empty, "Drains that exist without a placed marker: world x,z and cubic metres per second (0 takes all), like \"180,-400,0\".");
        DebugSeconds = config.Bind("Server", "DebugSeconds", 0f, new ConfigDescription("Every this many seconds the server logs each zone with water as a map of depths (0 = off). For testing.", new AcceptableValueRange<float>(0f, 600f)));
        DefaultSourceRate = config.Bind("Markers", "DefaultSourceRate", 2f, "Cubic metres per second a source gives when placed without a rate.");
        DefaultDrainRate = config.Bind("Markers", "DefaultDrainRate", 0f, "Cubic metres per second a drain takes when placed without a rate; 0 takes everything that reaches it.");
        DefaultDrainRadius = config.Bind("Markers", "DefaultDrainRadius", 1f, new ConfigDescription("How far around it a drain takes water, in metres, when placed without a radius.", new AcceptableValueRange<float>(0.25f, 16f)));
        CommandsForEveryone = config.Bind("Commands", "CommandsForEveryone", true, "Whether players who are not admins may use the flowingwater console command. On a server, the server's setting counts (it tells each player); the host and the server's admins always may.");
        CustomShader = config.Bind("Client", "CustomShader", true, "Draw the water with the mod's own shader: the look of the game's sea (its textures, colours and light), with ripples that run with the current and foam on fast water. Off: the game's own sea material, still.");
        GpuWater = config.Bind("Client", "GpuWater", true, "Move the water near you on the graphics card, between the server's changes, as water moves: a surge runs out over the ground and up the walls, a pool sloshes, waves cross and come back. The server still says how much water is where. Off (or on a card without compute shaders): the water is drawn as the server sends it, easing from one change to the next.");
        GpuWaterRange = config.Bind("Client", "GpuWaterRange", 96f, new ConfigDescription("Side of the square round you, in metres, whose water the graphics card moves (beyond it the water is drawn as the server sends it).", new AcceptableValueRange<float>(32f, 256f)));
        GpuWaterCell = config.Bind("Client", "GpuWaterCell", 0.25f, new ConfigDescription("Side of the graphics card's cells, in metres (no larger than the server's voxels): smaller cells give finer waves and fronts and cost the card more (four times for half the side).", new AcceptableValueList<float>(0.125f, 0.25f, 0.5f, 1f)));
        CurrentStrength = config.Bind("Client", "CurrentStrength", 1f, new ConfigDescription("How strongly the current carries swimmers (1 = at the water's own speed) and pushes floating things.", new AcceptableValueRange<float>(0f, 5f)));
        FlowVisualMinSpeed = config.Bind("Client", "FlowVisualMinSpeed", 0.15f, new ConfigDescription("Water faster than this, in metres per second, is drawn as a running stream instead of still water.", new AcceptableValueRange<float>(0.01f, 5f)));
        Spray = config.Bind("Client", "Spray", true, "Spray where the water falls or runs steeply (rapids, falls).");
        WaterVolume = config.Bind("Client", "WaterVolume", 0.7f, new ConfigDescription("How loud running water is (0 = silent), within the game's sound-effects volume.", new AcceptableValueRange<float>(0f, 1f)));
        FlowTextureSpeed = config.Bind("Client", "FlowTextureSpeed", 0.5f, new ConfigDescription("How fast the stream texture runs.", new AcceptableValueRange<float>(0f, 5f)));
    }

    public ConfigEntry<bool> Enabled { get; }

    public ConfigEntry<float> CellSize { get; }

    public ConfigEntry<bool> Erosion { get; }

    public ConfigEntry<float> ErosionSpeed { get; }

    public ConfigEntry<float> SendSeconds { get; }

    public ConfigEntry<float> StepBudget { get; }

    public ConfigEntry<int> MaxBricks { get; }

    public ConfigEntry<float> EmptyTileSeconds { get; }

    public ConfigEntry<float> MarkerScanSeconds { get; }

    public ConfigEntry<string> FixedSources { get; }

    public ConfigEntry<string> FixedDrains { get; }

    public ConfigEntry<float> DebugSeconds { get; }

    public ConfigEntry<float> DefaultSourceRate { get; }

    public ConfigEntry<float> DefaultDrainRate { get; }

    public ConfigEntry<float> DefaultDrainRadius { get; }

    public ConfigEntry<bool> CommandsForEveryone { get; }

    public ConfigEntry<bool> CustomShader { get; }

    public ConfigEntry<bool> GpuWater { get; }

    public ConfigEntry<float> GpuWaterRange { get; }

    public ConfigEntry<float> GpuWaterCell { get; }

    public ConfigEntry<float> CurrentStrength { get; }

    public ConfigEntry<float> FlowVisualMinSpeed { get; }

    public ConfigEntry<float> FlowTextureSpeed { get; }

    public ConfigEntry<bool> Spray { get; }

    public ConfigEntry<float> WaterVolume { get; }
}
