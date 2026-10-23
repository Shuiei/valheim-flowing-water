using System;
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

    private VoxelServer _server;

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
        ZNet.WorldSaveStarted += OnWorldSave;
        Settings.CommandsForEveryone.SettingChanged += (_, _) => Commands.TellEveryone();
        Logger.LogInfo($"{Name} {BuildInfo.Version} loaded");
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Update()
    {
        TickPanel();
        if (ZNet.instance == null || ZDOMan.instance == null)
        {
            WorldLoaded = false;
            if (_server != null)
            {
                Logger.LogInfo("World closed: water simulation stopped");
                _server.Close();
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

            _server = new VoxelServer();
            Logger.LogInfo("World open: water simulation started");
        }

        _server.Update(Time.deltaTime);
    }

    // The source and drain panel, before the water. A fault in it would otherwise skip the water's update after it, every
    // frame: it is logged once in full, and the panel is put away and left off until the game restarts.
    private void TickPanel()
    {
        if (Client.MarkerPanel.Off)
        {
            return;
        }

        try
        {
            Client.MarkerPanel.Tick();
        }
        catch (Exception e)
        {
            Logger.LogError($"The source and drain panel failed; it is off until the game restarts, the water goes on: {e}");
            Client.MarkerPanel.TurnOff();
        }
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        ZNet.WorldSaveStarted -= OnWorldSave;
        _harmony?.UnpatchSelf();
    }

    // The water's latest state goes into the world before it is saved (it is otherwise sent every SendSeconds).
    // The game runs this inside its save with nothing to catch: a failure here would stop the world being saved at all.
    // So it is logged in full and the save goes on, with the water as last sent; this and the panel's (TickPanel) are the
    // only places where going on past an error is right.
    private void OnWorldSave()
    {
        try
        {
            _server?.Publish();
        }
        catch (Exception e)
        {
            Logger.LogError($"Water: could not send the water before the world was saved; it is saved as last sent: {e}");
        }
    }
}
