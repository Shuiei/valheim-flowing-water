using System;
using System.Globalization;
using FlowingWater.Client;
using FlowingWater.Server;
using UnityEngine;

namespace FlowingWater;

/// <summary>The flowingwater console command: place and remove sources and drains, inspect the water.</summary>
internal static class Commands
{
    private const string Usage = "flowingwater source [m3/s] | drain [m3/s, 0 = all] | remove | info | debug | flat | lift [m]";

    private static bool _registered;

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        _ = new Terminal.ConsoleCommand("flowingwater", Usage, Run);
    }

    // An exception out of a console command reaches Unity's event system, whose logging of it has
    // crashed the game outright; it is logged here instead, with its message on the console.
    private static void Run(Terminal.ConsoleEventArgs args)
    {
        try
        {
            RunUnguarded(args);
        }
        catch (Exception e)
        {
            FlowingWaterPlugin.Log.LogError($"flowingwater {args.ArgsAll}: {e}");
            args.Context.AddString($"flowingwater: failed, {e.GetType().Name}: {e.Message} (see BepInEx/LogOutput.log)");
        }
    }

    private static void RunUnguarded(Terminal.ConsoleEventArgs args)
    {
        Terminal terminal = args.Context;
        if (!FlowingWaterPlugin.Settings.CommandsForEveryone.Value && !terminal.IsCheatsEnabled())
        {
            terminal.AddString("flowingwater: cheats must be on (or CommandsForEveryone in the config)");
            return;
        }

        Player player = Player.m_localPlayer;
        if (player == null)
        {
            terminal.AddString("flowingwater: needs a player in the world");
            return;
        }

        string what = args.Length > 1 ? args.Args[1].ToLowerInvariant() : "info";
        switch (what)
        {
            case "source":
                Place(terminal, player, Prefabs.SourceName, Rate(args, FlowingWaterPlugin.Settings.DefaultSourceRate.Value), "source");
                break;
            case "drain":
                Place(terminal, player, Prefabs.DrainName, Rate(args, FlowingWaterPlugin.Settings.DefaultDrainRate.Value), "drain");
                break;
            case "remove":
                Remove(terminal, player);
                break;
            case "info":
                terminal.AddString("flowingwater: " + WaterTile.Describe(player.transform.position));
                break;
            case "debug":
                terminal.AddString("flowingwater: " + WaterTile.DebugInfo(player.transform.position));
                break;
            case "flat":
                WaterTile.Flat = !WaterTile.Flat;
                terminal.AddString("flowingwater: " + (WaterTile.Flat ? "plain colours (blue still, green stream)" : "water materials"));
                break;
            case "lift":
                WaterTile.Lift = Rate(args, 0f);
                terminal.AddString($"flowingwater: water drawn {WaterTile.Lift:0.##} m above its level");
                break;
            default:
                terminal.AddString(Usage);
                break;
        }
    }

    private static float Rate(Terminal.ConsoleEventArgs args, float fallback)
    {
        return args.Length > 2 && float.TryParse(args.Args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float rate) && rate >= 0f ? rate : fallback;
    }

    private static void Place(Terminal terminal, Player player, string prefabName, float rate, string kind)
    {
        GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
        if (prefab == null)
        {
            terminal.AddString($"flowingwater: prefab {prefabName} missing (is the mod on the server too?)");
            return;
        }

        Vector3 position = player.transform.position;
        if (ZoneSystem.instance.GetGroundHeight(position, out float ground))
        {
            position.y = ground;
        }

        var placed = Object.Instantiate(prefab, position, Quaternion.identity);
        placed.GetComponent<ZNetView>().GetZDO().Set(WaterServer.RateHash, rate);
        terminal.AddString($"flowingwater: {kind} placed at {position.x:0},{position.y:0},{position.z:0}, {rate:0.##} m³/s" + (rate <= 0f && kind == "drain" ? " (takes everything)" : string.Empty));
    }

    private static void Remove(Terminal terminal, Player player)
    {
        var marker = Marker.Nearest(player.transform.position, 5f);
        if (marker == null)
        {
            terminal.AddString("flowingwater: no source or drain within 5 m");
            return;
        }

        string kind = marker.IsDrain ? "drain" : "source";
        marker.Remove();
        terminal.AddString($"flowingwater: {kind} removed");
    }
}
