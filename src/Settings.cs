using BepInEx.Configuration;
using FlowingWater.Client;
using FlowingWater.Server;

namespace FlowingWater;

/// <summary>The plugin's settings (BepInEx/config/Tie.FlowingWater.cfg).</summary>
internal sealed class Settings
{
    public Settings(ConfigFile config)
    {
        Enabled = config.Bind("General", "Enabled", true, "Simulate (server) and show (client) flowing water.");
        CellSize = config.Bind("Server", "CellSize", 1f, new ConfigDescription("Side of the water's cubic voxels, in metres. Smaller voxels give finer streams, shores and falls. Moving water costs the server about 3 times as much at 0.5 as at 1, and about 15 times at 0.25 (for small builds only); still water stays cheap at any size. Changing it moves the water already there to the new grid.", new AcceptableValueList<float>(TileData.CellSizes)));
        Erosion = config.Bind("Server", "Erosion", false, "Running water wears the ground away where it runs fast and lays it down where it slows, so streams slowly cut their own beds. The ground within 5 m of anything players built, and of the game's own places, is left alone. Turning it off stops it; what it did stays. Paused while the water moves to 3D voxels: it comes back in a later version.");
        ErosionSpeed = config.Bind("Server", "ErosionSpeed", 1f, new ConfigDescription("How fast erosion goes (1 = millimetres a minute in a fast stream); higher to watch it happen, for testing. Paused with Erosion: it comes back in a later version.", new AcceptableValueRange<float>(0.1f, 1000f)));
        SendSeconds = config.Bind("Server", "SendSeconds", 1f, new ConfigDescription("How often a block of water whose change would show is sent to the players near it, in seconds (a change too small to see waits until it adds up, ten sends at most).", new AcceptableValueRange<float>(0.25f, 10f)));
        StepBudget = config.Bind("Server", "StepBudget", 8f, new ConfigDescription("Most milliseconds of each server frame the water may take, on average (a step of the water is taken a part at a time, so a frame may take a part more now and then). Water needing more slows down, sources and drains with it, instead of slowing the server (and the game for its players).", new AcceptableValueRange<float>(1f, 50f)));
        MaxBricks = config.Bind("Server", "MaxBricks", 1024, new ConfigDescription("Most blocks of 16 by 16 by 16 voxels that may hold water at once (each about 130 KB of server memory while its water moves, 37 KB once it is still). Water stops spreading beyond.", new AcceptableValueRange<int>(16, 16384)));
        EmptyTileSeconds = config.Bind("Server", "EmptyTileSeconds", 30f, new ConfigDescription("A block of voxels dry for this long is forgotten (made again when water comes back).", new AcceptableValueRange<float>(5f, 3600f)));
        MarkerScanSeconds = config.Bind("Server", "MarkerScanSeconds", 3f, new ConfigDescription("Seconds from the end of one look for new or removed sources and drains to the next (a look over a big world itself takes a few seconds, a little each frame).", new AcceptableValueRange<float>(1f, 60f)));
        FixedSources = config.Bind("Server", "FixedSources", string.Empty, "Sources that exist without a placed marker, for servers and tests: world x,z and cubic metres per second, like \"120,-340,2; 125,-338,1.5\".");
        FixedDrains = config.Bind("Server", "FixedDrains", string.Empty, "Drains that exist without a placed marker: world x,z and cubic metres per second (0 takes all), like \"180,-400,0\".");
        DebugSeconds = config.Bind("Server", "DebugSeconds", 0f, new ConfigDescription("Every this many seconds the server logs each zone with water as a map of depths (0 = off). For testing.", new AcceptableValueRange<float>(0f, 600f)));
        DefaultSourceRate = config.Bind("Markers", "DefaultSourceRate", 2f, new ConfigDescription("Cubic metres per second a source gives when placed without a rate (above 0, up to the most a source may give).", new AcceptableValueRange<float>(MinSourceRate, MarkerRules.MaxRate)));
        DefaultDrainRate = config.Bind("Markers", "DefaultDrainRate", 0f, new ConfigDescription("Cubic metres per second a drain takes when placed without a rate; 0 takes everything that reaches it.", new AcceptableValueRange<float>(0f, MarkerRules.MaxRate)));
        DefaultDrainRadius = config.Bind("Markers", "DefaultDrainRadius", 1f, new ConfigDescription("How far around it a drain takes water, in metres, when placed without a radius.", new AcceptableValueRange<float>(MarkerValues.MinRadius, MarkerRules.MaxDrainRadius)));
        CommandsForEveryone = config.Bind("Commands", "CommandsForEveryone", true, "Whether players who are not admins may use the flowingwater console command, and place, change or remove sources and drains with the hoe. On a server, the server's setting counts (it tells each player, and simulates only the sources and drains placed by a player who may); the host and the server's admins always may.");
        CustomShader = config.Bind("Client", "CustomShader", true, "Draw the water with the mod's own shader: the look of the game's sea (its textures, colours and light), with ripples that run with the current and foam on fast water. Off: the game's own sea material, still.");
        GpuWater = config.Bind("Client", "GpuWater", true, "Move the water near you on the graphics card, between the server's changes, as water moves: a surge runs out over the ground and up the walls, a pool sloshes, waves cross and come back. The server still says how much water is where. Off (or on a card without compute shaders): the water is drawn as the server sends it, easing from one change to the next.");
        GpuWaterRange = config.Bind("Client", "GpuWaterRange", 96f, new ConfigDescription("Side of the square round you, in metres, whose water the graphics card moves (beyond it the water is drawn as the server sends it).", new AcceptableValueRange<float>(32f, 256f)));
        GpuWaterCell = config.Bind("Client", "GpuWaterCell", 0.25f, new ConfigDescription("Side of the graphics card's cells, in metres (no larger than the server's voxels): smaller cells give finer waves and fronts and cost the card more (four times for half the side).", new AcceptableValueList<float>(0.125f, 0.25f, 0.5f, 1f)));
        CurrentStrength = config.Bind("Client", "CurrentStrength", 1f, new ConfigDescription("How strongly the current carries swimmers (1 = at the water's own speed) and pushes floating things.", new AcceptableValueRange<float>(0f, 5f)));
        FlowVisualMinSpeed = config.Bind("Client", "FlowVisualMinSpeed", 0.15f, new ConfigDescription("Unused for now (it was the column water's): water faster than this, in metres per second, was drawn as a running stream instead of still water.", new AcceptableValueRange<float>(0.01f, 5f)));
        Spray = config.Bind("Client", "Spray", true, "With the water moved on the graphics card (Client.GpuWater), its falls, splashes and spray as particles on the card: water running over a lip falls and arcs, a fall landing splashes, fast water throws droplets.");
        ParticleCapacity = config.Bind("Client", "ParticleCapacity", 65536, new ConfigDescription("Particles of the particle water at most: no more water is made near you once they are all in use, until some goes. More costs the graphics card more; a change takes effect when the water near you is made again (moving far, or a world opening).", new AcceptableValueRange<int>(4096, 262144)));
        ParticleWater = config.Bind("Client", "ParticleWater", false, "A prototype: the water near you is particles of water on the graphics card instead of a sheet, made from the server's water and held to it, moved as water moves (it falls, breaks up, splashes and pools, and is pushed aside by you, creatures and ships). The sheet and its spray are not drawn near you then. The console's \"flowingwater particles\" pours some whatever this is.");
        ParticleSize = config.Bind("Client", "ParticleSize", 0.25f, new ConfigDescription("Side of a particle of the particle water, metres: 0.25 is water as it looks; 0.5 is coarse (balls of water) and an eighth of the particles for the same water, for a slow graphics card. Takes effect when the water near you is made again.", new AcceptableValueList<float>(0.25f, 0.5f)));
        VolumeWater = config.Bind("Client", "VolumeWater", false, "The water near you as one body of fluid on the graphics card (a prototype, in place of the sheet and of Client.ParticleWater): a 3D fluid over a grid round you, its mass its own, poured in from the server's water once and at its edge, moving as water moves (it pools and levels, bursts out of a broken box, falls, flows round what stands in it), with sources pouring into it and drains taking from it. Needs a graphics card with room: the grid costs memory and time by its range and height.");
        VolumeRange = config.Bind("Client", "VolumeRange", 32f, new ConfigDescription("Side of the square round you the volume water fills, metres (the card's square is this at most then); more costs the card more, by the square, and a wide square of small cells is cut down in height to what the card runs at once (128 m of 0.5 m cells leaves 15.5 m; 96 m, 28 m; 64 m, 48 m).", new AcceptableValueRange<float>(16f, 128f)));
        VolumeHeight = config.Bind("Client", "VolumeHeight", 32f, new ConfigDescription("Height of the volume water's grid, metres, about you (seven tenths of it above you): water above or below it is the server's, and comes into the grid as it falls in. The grid is cut to what the card runs at once (a 32 m square of 0.25 m cells allows 31.75 m).", new AcceptableValueRange<float>(8f, 64f)));
        VolumeCapacity = config.Bind("Client", "VolumeCapacity", 1 << 20, new ConfigDescription("Particles of the volume water at most (eight to a cell of 0.25 m: a million is 2000 m³); more costs the card memory.", new AcceptableValueRange<int>(65536, 1 << 22)));
        VolumeFroth = config.Bind("Client", "VolumeFroth", 1f, new ConfigDescription("How much spray, foam and bubbles the volume water throws where it is violent (a fall's foot, a jet into a pool, water striking a wall or the ground, a fast front): 0 none, 1 as tuned, more a wilder water. Particles of their own, a quarter of VolumeCapacity at most.", new AcceptableValueRange<float>(0f, 4f)));
        WaterVolume = config.Bind("Client", "WaterVolume", 0.7f, new ConfigDescription("How loud running water is (0 = silent), within the game's sound-effects volume.", new AcceptableValueRange<float>(0f, 1f)));
        FlowTextureSpeed = config.Bind("Client", "FlowTextureSpeed", 0.5f, new ConfigDescription("Unused for now (it was the column water's): how fast the stream texture ran.", new AcceptableValueRange<float>(0f, 5f)));
    }

    /// <summary>The least a source placed without a rate may give, cubic metres a second: a source's rate is above 0.</summary>
    public const float MinSourceRate = 0.001f;

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

    public ConfigEntry<bool> ParticleWater { get; }

    public ConfigEntry<float> ParticleSize { get; }

    public ConfigEntry<int> ParticleCapacity { get; }

    public ConfigEntry<bool> VolumeWater { get; }

    public ConfigEntry<float> VolumeRange { get; }

    public ConfigEntry<float> VolumeHeight { get; }

    public ConfigEntry<int> VolumeCapacity { get; }

    public ConfigEntry<float> VolumeFroth { get; }

    public ConfigEntry<float> WaterVolume { get; }
}
