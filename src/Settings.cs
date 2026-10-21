using BepInEx.Configuration;

namespace FlowingWater;

/// <summary>The plugin's settings (BepInEx/config/Tie.FlowingWater.cfg).</summary>
internal sealed class Settings
{
    public Settings(ConfigFile config)
    {
        Enabled = config.Bind("General", "Enabled", true, "Simulate (server) and show (client) flowing water.");
        StepsPerSecond = config.Bind("Server", "StepsPerSecond", 15, new ConfigDescription("Simulation steps per second.", new AcceptableValueRange<int>(2, 30)));
        Friction = config.Bind("Server", "Friction", 0.5f, new ConfigDescription("How fast a flow loses its momentum, per second. Lower keeps streams rushing and pools sloshing longer; higher settles them sooner.", new AcceptableValueRange<float>(0f, 5f)));
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
        CommandsForEveryone = config.Bind("Commands", "CommandsForEveryone", true, "Whether the flowingwater console command works without cheats on.");
        CurrentStrength = config.Bind("Client", "CurrentStrength", 1f, new ConfigDescription("How strongly the current carries swimmers (1 = at the water's own speed) and pushes floating things.", new AcceptableValueRange<float>(0f, 5f)));
        FlowVisualMinSpeed = config.Bind("Client", "FlowVisualMinSpeed", 0.15f, new ConfigDescription("Water faster than this, in metres per second, is drawn as a running stream instead of still water.", new AcceptableValueRange<float>(0.01f, 5f)));
        FlowTextureSpeed = config.Bind("Client", "FlowTextureSpeed", 0.5f, new ConfigDescription("How fast the stream texture runs.", new AcceptableValueRange<float>(0f, 5f)));
    }

    public ConfigEntry<bool> Enabled { get; }

    public ConfigEntry<int> StepsPerSecond { get; }

    public ConfigEntry<float> Friction { get; }

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

    public ConfigEntry<bool> CommandsForEveryone { get; }

    public ConfigEntry<float> CurrentStrength { get; }

    public ConfigEntry<float> FlowVisualMinSpeed { get; }

    public ConfigEntry<float> FlowTextureSpeed { get; }
}
