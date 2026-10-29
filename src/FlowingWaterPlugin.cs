using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
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

    // The settings file, read again when it is written (no restart needed).
    private FileWatch _settingsFile;

    internal static ManualLogSource Log { get; private set; }

    internal static Settings Settings { get; private set; }

    /// <summary>The water's server while this game is one (alone, hosting, or dedicated) with a world open; else null.</summary>
    internal static VoxelServer Server { get; private set; }

    /// <summary>Set once the server has read its world's objects (ZNet.LoadWorld), cleared when the world closes.</summary>
    internal static bool WorldLoaded { get; set; }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Awake()
    {
        Log = Logger;
        Settings = new Settings(Config);
        _settingsFile = new FileWatch(() => File.GetLastWriteTimeUtc(Config.ConfigFilePath));
        _harmony = new Harmony(PluginId);
        _harmony.PatchAll(typeof(FlowingWaterPlugin).Assembly);
        ZNet.WorldSaveStarted += OnWorldSave;
        Settings.CommandsForEveryone.SettingChanged += (_, _) => Commands.TellEveryone();
        Logger.LogInfo($"{Name} {BuildInfo.Version} loaded");
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void Update()
    {
        if (_settingsFile.Changed(Time.realtimeSinceStartup))
        {
            ReadSettingsAgain();
        }

        // The source and drain panel, before the water: a fault in it turns it off and leaves the water's frame be.
        Client.MarkerPanel.Tick();
        if (ZNet.instance == null || ZDOMan.instance == null)
        {
            WorldLoaded = false;
            if (_server != null)
            {
                Logger.LogInfo("World closed: water simulation stopped");
                _server.Close();
                _server = null;
                Server = null;
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

            _server = new VoxelServer(Path.Combine(Path.GetDirectoryName(Config.ConfigFilePath), $"{PluginId}.{ZNet.instance.GetWorldName()}.markers"));
            Server = _server;
            Logger.LogInfo("World open: water simulation started");
        }

        _server.Update(Time.deltaTime);
    }

    // After every brick's frame: the water near the camera moved and drawn on the graphics card, and heard.
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    [SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Unity calls it on the plugin's object.")]
    private void LateUpdate()
    {
        Client.GpuWater.Tick();
        Client.WaterSound.Tick();
    }

    // The settings as the file has them now: what reads a setting reads it as it goes (each frame, each scan, each
    // placing), and what must act at once on a change (the shader, the commands' rights) listens for it.
    private void ReadSettingsAgain()
    {
        if (!File.Exists(Config.ConfigFilePath))
        {
            // Told once: a file that is gone reads as written at one and the same time each second.
            if (!_settingsFile.Again)
            {
                Logger.LogWarning($"Settings file {Config.ConfigFilePath} is gone: the settings stay as they are until it is written again");
            }

            return;
        }

        // Not saved while read: BepInEx saves on each setting changed, which, half way through the file, would write it
        // over without the entries it has not read yet that no setting holds.
        Dictionary<string, string> before = SettingValues();
        bool save = Config.SaveOnConfigSet;
        Config.SaveOnConfigSet = false;
        try
        {
            Config.Reload();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Held by an editor, or not readable now: tried again at the next look, told once.
            if (!_settingsFile.Again)
            {
                Logger.LogWarning($"Settings file {Config.ConfigFilePath} could not be read, tried again each second: {e.Message}");
            }

            return;
        }
        finally
        {
            Config.SaveOnConfigSet = save;
        }

        _settingsFile.Read();
        List<string> changed = FileWatch.Changes(before, SettingValues());
        if (changed.Count > 0)
        {
            Logger.LogInfo($"Settings read again from {Path.GetFileName(Config.ConfigFilePath)}: {string.Join(", ", changed)}");
        }
    }

    private Dictionary<string, string> SettingValues() =>
        Config.ToDictionary(s => $"{s.Key.Section}.{s.Key.Key}", s => s.Value.GetSerializedValue());

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Unity calls it by name.")]
    private void OnDestroy()
    {
        ZNet.WorldSaveStarted -= OnWorldSave;
        _harmony?.UnpatchSelf();
    }

    // The water's latest state goes into the world before it is saved (it is otherwise sent every SendSeconds).
    // The game runs this inside its save with nothing to catch: a failure here would stop the world being saved at all.
    // So it is logged in full and the save goes on, with the water as last sent; this and the panel's (FaultGate) are the
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
