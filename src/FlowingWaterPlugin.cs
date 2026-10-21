using System.Diagnostics.CodeAnalysis;
using BepInEx;
using BepInEx.Logging;
using FlowingWater.Server;
using HarmonyLib;
using UnityEngine;

namespace FlowingWater;

/// <summary>
/// Flowing water for Valheim: water springs from sources, runs downhill over the terrain, pools in
/// hollows, spills on, and vanishes in drains or the sea. The server simulates every river whole,
/// players or not nearby; clients draw the water, swim in it and are carried by its current.
/// </summary>
[BepInPlugin(PluginId, Name, BuildInfo.Version)]
[SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "Unity owns its lifetime; OnDestroy unpatches.")]
public sealed class FlowingWaterPlugin : BaseUnityPlugin
{
    /// <summary>The plugin's BepInEx id, also the Harmony id and the config file's name.</summary>
    public const string PluginId = "Tie.FlowingWater";

    /// <summary>The plugin's display name.</summary>
    public const string Name = "FlowingWater";

    private Harmony _harmony;

    private WaterServer _server;

    internal static ManualLogSource Log { get; private set; }

    internal static Settings Settings { get; private set; }

    /// <summary>Set once the server has read its world's objects (ZNet.LoadWorld), cleared when the world closes.</summary>
    internal static bool WorldLoaded { get; set; }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Awake()
    {
        Log = Logger;
        Settings = new Settings(Config);
        _harmony = new Harmony(PluginId);
        _harmony.PatchAll(typeof(FlowingWaterPlugin).Assembly);
        Logger.LogInfo($"{Name} {BuildInfo.Version} loaded");
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Update()
    {
        if (ZNet.instance == null || ZDOMan.instance == null)
        {
            WorldLoaded = false;
            if (_server != null)
            {
                Logger.LogInfo("World closed: water simulation stopped");
                _server = null;
            }

            return;
        }

        if (!Settings.Enabled.Value || !ZNet.instance.IsServer())
        {
            return;
        }

        if (_server == null)
        {
            // The server reads its world's objects in ZNet.Start, after the first frames: wait for it.
            if (!WorldLoaded || WorldGenerator.instance == null || ZoneSystem.instance == null)
            {
                return;
            }

            _server = new WaterServer();
            Logger.LogInfo("World open: water simulation started");
        }

        _server.Update(Time.deltaTime);
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        _harmony?.UnpatchSelf();
    }
}
