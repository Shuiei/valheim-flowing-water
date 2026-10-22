using System;
using System.Globalization;
using FlowingWater.Client;
using FlowingWater.Server;
using UnityEngine;

namespace FlowingWater;

/// <summary>The flowingwater console command: place and remove sources and drains, inspect the water.</summary>
internal static class Commands
{
    private const string Usage = "flowingwater source [m3/s] | drain [m3/s, 0 = all] [radius m] | remove | info | debug | flat | lift [m] | mat custom|still|stream <property> [values]";

    // How far above the feet the ray under them starts, and how far below them it still finds a floor.
    private const float FeetLift = 0.5f;

    private const float FloorReach = 5f;

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
                if (Number(terminal, args, 2, FlowingWaterPlugin.Settings.DefaultSourceRate.Value, false, "a source's rate (m³/s)", out float give))
                {
                    Place(terminal, player, Prefabs.SourceName, give, 0f);
                }

                break;
            case "drain":
                if (Number(terminal, args, 2, FlowingWaterPlugin.Settings.DefaultDrainRate.Value, true, "a drain's rate (m³/s, 0 = all)", out float take)
                    && Number(terminal, args, 3, FlowingWaterPlugin.Settings.DefaultDrainRadius.Value, false, "a drain's radius (m)", out float radius))
                {
                    Place(terminal, player, Prefabs.DrainName, take, radius);
                }

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
            case "mat":
                terminal.AddString("flowingwater: " + Tune(args));
                break;
            case "lift":
                if (Number(terminal, args, 2, 0f, true, "the lift (m)", out float lift))
                {
                    WaterTile.Lift = lift;
                    terminal.AddString($"flowingwater: water drawn {WaterTile.Lift:0.##} m above its level");
                }

                break;
            default:
                terminal.AddString(Usage);
                break;
        }
    }

    // flowingwater mat still|stream <property> [value | r g b [a]]
    private static string Tune(Terminal.ConsoleEventArgs args)
    {
        if (args.Length < 4)
        {
            return "mat custom|still|stream <property> [value | r g b [a]]; <property> alone shows it, \"list\" names them all";
        }

        string kind = args.Args[2].ToLowerInvariant();
        string property = args.Args[3];
        float[] values = new float[args.Length - 4];
        for (int i = 0; i < values.Length; i++)
        {
            if (!float.TryParse(args.Args[4 + i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
            {
                return $"{args.Args[4 + i]} is not a number";
            }
        }

        return Materials.Tune(kind, property, values);
    }

    // The number at that place after the command, or the fallback when there is none. A word that
    // is not a number, or a number out of range (below 0, or 0 when zero is not allowed), is refused
    // with the reason on the console, and the command does nothing.
    private static bool Number(Terminal terminal, Terminal.ConsoleEventArgs args, int place, float fallback, bool zeroAllowed, string what, out float value)
    {
        value = fallback;
        if (args.Length <= place)
        {
            return true;
        }

        string text = args.Args[place];
        if (float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !float.IsInfinity(value) && (zeroAllowed ? value >= 0f : value > 0f))
        {
            return true;
        }

        terminal.AddString($"flowingwater: {what} must be a number {(zeroAllowed ? "of 0 or more" : "above 0")}, not \"{text}\"; nothing done");
        return false;
    }

    // A source (radius 0) or a drain, on what the player stands on.
    private static void Place(Terminal terminal, Player player, string prefabName, float rate, float radius)
    {
        bool drain = radius > 0f;
        string kind = drain ? "drain" : "source";
        GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
        if (prefab == null)
        {
            terminal.AddString($"flowingwater: prefab {prefabName} missing (is the mod on the server too?)");
            return;
        }

        Vector3 position = UnderFeet(player.transform.position);
        var placed = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        ZDO zdo = placed.GetComponent<ZNetView>().GetZDO();
        zdo.Set(WaterServer.RateHash, rate);
        if (drain)
        {
            zdo.Set(WaterServer.RadiusHash, radius);
        }

        terminal.AddString($"flowingwater: {kind} placed at {position.x:0},{position.y:0},{position.z:0}, {rate:0.##} m³/s"
            + (drain ? (rate <= 0f ? " (takes everything)" : string.Empty) + $" within {radius:0.##} m" : string.Empty));
    }

    // The surface right under the feet: a floor or anything built counts, not only the terrain, as the
    // water is to rest on what players build. The ray starts a little above the feet, so that a floor
    // level with them is not missed, and reaches a few metres down, so that a jump still finds it. A
    // player higher than that (flying in debug mode) gets the terrain below, as before.
    private static Vector3 UnderFeet(Vector3 feet)
    {
        int solid = LayerMask.GetMask("Default", "static_solid", "Default_small", "piece", "terrain", "vehicle");
        if (Physics.Raycast(feet + (Vector3.up * FeetLift), Vector3.down, out RaycastHit hit, FeetLift + FloorReach, solid, QueryTriggerInteraction.Ignore))
        {
            return hit.point;
        }

        if (ZoneSystem.instance.GetGroundHeight(feet, out float ground))
        {
            feet.y = ground;
        }

        return feet;
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
