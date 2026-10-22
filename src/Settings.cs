using BepInEx.Configuration;

namespace FlowingWater;

/// <summary>The plugin's settings (BepInEx/config/Tie.FlowingWater.cfg).</summary>
internal sealed class Settings
{
    public Settings(ConfigFile config)
    {
        Enabled = config.Bind("General", "Enabled", true, "Simulate (server) and show (client) flowing water.");
        CellSize = config.Bind("Server", "CellSize", 1f, new ConfigDescription("Size of the simulation's square cells, in metres. Smaller cells give narrower streams and finer shores, at a cost: 0.5 is about 6 times the server work and 4 times the data sent to players of 1, 0.25 about 32 and 16 times (for a few small streams only). Changing it moves the water already there to the new grid.", new AcceptableValueList<float>(TileData.CellSizes)));
        StepsPerSecond = config.Bind("Server", "StepsPerSecond", 15, new ConfigDescription("Simulation steps per second with one-metre cells; smaller cells step proportionally more often (30 for 0.5, 60 for 0.25).", new AcceptableValueRange<int>(2, 30)));
        Friction = config.Bind("Server", "Friction", 0.5f, new ConfigDescription("How fast a flow loses its momentum, per second, besides the bed's drag. Lower keeps pools sloshing longer; higher settles them sooner.", new AcceptableValueRange<float>(0f, 5f)));
        Erosion = config.Bind("Server", "Erosion", false, "Running water wears the ground away where it runs fast and lays it down where it slows, so streams slowly cut their own beds. The ground within 5 m of anything players built, and of the game's own places, is left alone. Turning it off stops it; what it did stays.");
        ErosionSpeed = config.Bind("Server", "ErosionSpeed", 1f, new ConfigDescription("How fast erosion goes (1 = millimetres a minute in a fast stream); higher to watch it happen, for testing.", new AcceptableValueRange<float>(0.1f, 1000f)));
        BedRoughness = config.Bind("Server", "BedRoughness", 0.04f, new ConfigDescription("How rough the ground is under running water (Manning's n): the drag that slows shallow water much more than deep, so a stream gathers into a deeper, slower channel instead of a fast film. 0.03 smooth channel, 0.04 natural stream, 0.08 grass; 0 = no drag.", new AcceptableValueRange<float>(0f, 0.2f)));
        SendSeconds = config.Bind("Server", "SendSeconds", 1f, new ConfigDescription("How often a changed water tile is sent to the players near it, in seconds.", new AcceptableValueRange<float>(0.25f, 10f)));
        MaxTiles = config.Bind("Server", "MaxTiles", 256, new ConfigDescription("Most zones that may hold water at once (each is 64 by 64 metres). Water stops spreading beyond.", new AcceptableValueRange<int>(1, 4096)));
        MaxDepth = config.Bind("Server", "MaxDepth", 40f, new ConfigDescription("Deepest a cell may fill, in metres; a basin without an outlet stops rising there.", new AcceptableValueRange<float>(1f, 300f)));
        EmptyTileSeconds = config.Bind("Server", "EmptyTileSeconds", 30f, new ConfigDescription("A zone dry for this long, with no source in it, forgets its water tile.", new AcceptableValueRange<float>(5f, 3600f)));
        MarkerScanSeconds = config.Bind("Server", "MarkerScanSeconds", 3f, new ConfigDescription("How often the server looks for new or removed sources and drains, in seconds.", new AcceptableValueRange<float>(1f, 60f)));
        FixedSources = config.Bind("Server", "FixedSources", string.Empty, "Sources that exist without a placed marker, for servers and tests: world x,z and cubic metres per second, like \"120,-340,2; 125,-338,1.5\".");
        FixedDrains = config.Bind("Server", "FixedDrains", string.Empty, "Drains that exist without a placed marker: world x,z and cubic metres per second (0 takes all), like \"180,-400,0\".");
        DebugSeconds = config.Bind("Server", "DebugSeconds", 0f, new ConfigDescription("Every this many seconds the server logs each water tile as a map of depths (0 = off). For testing.", new AcceptableValueRange<float>(0f, 600f)));
        DefaultSourceRate = config.Bind("Markers", "DefaultSourceRate", 2f, "Cubic metres per second a source gives when placed without a rate.");
        DefaultDrainRate = config.Bind("Markers", "DefaultDrainRate", 0f, "Cubic metres per second a drain takes when placed without a rate; 0 takes everything that reaches it.");
        DefaultDrainRadius = config.Bind("Markers", "DefaultDrainRadius", 1f, new ConfigDescription("How far around it a drain takes water, in metres, when placed without a radius.", new AcceptableValueRange<float>(0.25f, 16f)));
        CommandsForEveryone = config.Bind("Commands", "CommandsForEveryone", true, "Whether the flowingwater console command works without cheats on.");
        CustomShader = config.Bind("Client", "CustomShader", true, "Draw the water with the mod's own shader (ripples that run with the current, colour by depth, foam, a shore that fades out). Off: the game's own sea and stream materials.");
        CurrentStrength = config.Bind("Client", "CurrentStrength", 1f, new ConfigDescription("How strongly the current carries swimmers (1 = at the water's own speed) and pushes floating things.", new AcceptableValueRange<float>(0f, 5f)));
        FlowVisualMinSpeed = config.Bind("Client", "FlowVisualMinSpeed", 0.15f, new ConfigDescription("Water faster than this, in metres per second, is drawn as a running stream instead of still water.", new AcceptableValueRange<float>(0.01f, 5f)));
        Spray = config.Bind("Client", "Spray", true, "Spray where the water falls or runs steeply (rapids, falls).");
        WaterVolume = config.Bind("Client", "WaterVolume", 0.7f, new ConfigDescription("How loud running water is (0 = silent), within the game's sound-effects volume.", new AcceptableValueRange<float>(0f, 1f)));
        FlowTextureSpeed = config.Bind("Client", "FlowTextureSpeed", 0.5f, new ConfigDescription("How fast the stream texture runs.", new AcceptableValueRange<float>(0f, 5f)));
    }

    public ConfigEntry<bool> Enabled { get; }

    public ConfigEntry<float> CellSize { get; }

    public ConfigEntry<int> StepsPerSecond { get; }

    public ConfigEntry<float> Friction { get; }

    public ConfigEntry<float> BedRoughness { get; }

    public ConfigEntry<bool> Erosion { get; }

    public ConfigEntry<float> ErosionSpeed { get; }

    public ConfigEntry<float> SendSeconds { get; }

    public ConfigEntry<int> MaxTiles { get; }

    public ConfigEntry<float> MaxDepth { get; }

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

    public ConfigEntry<float> CurrentStrength { get; }

    public ConfigEntry<float> FlowVisualMinSpeed { get; }

    public ConfigEntry<float> FlowTextureSpeed { get; }

    public ConfigEntry<bool> Spray { get; }

    public ConfigEntry<float> WaterVolume { get; }
}
