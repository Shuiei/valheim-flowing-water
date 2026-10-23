using System;
using System.Globalization;
using FlowingWater.Client;
using FlowingWater.Server;
using UnityEngine;

namespace FlowingWater;

/// <summary>The flowingwater console command: place and remove sources and drains, inspect the water.</summary>
internal static class Commands
{
    private const string Usage = "flowingwater source [m3/s] | drain [m3/s, 0 = all] [radius m] | remove | info | debug | solids | flat | lift [m] | mat custom|still <property> [values]";

    // How the server tells each player its CommandsForEveryone.
    private const string EveryoneRpc = "FlowingWater_CommandsForEveryone";

    private static bool _registered;

    // Who may use the command: the server's CommandsForEveryone, as it told this player's game.
    private static readonly CommandRights Rights = new();

    public static void Register()
    {
        if (_registered)
        {
            return;
        }

        _registered = true;
        _ = new Terminal.ConsoleCommand("flowingwater", Usage, Run);
    }

    /// <summary>
    /// A peer's PeerInfo handled (it joined): the server tells the player's game its CommandsForEveryone; the player's
    /// game, joining, listens for it.
    /// </summary>
    public static void Joined(ZNet net, ZRpc rpc)
    {
        if (!net.IsServer())
        {
            Rights.Joining();
            rpc.Register<bool>(EveryoneRpc, (_, everyone) =>
            {
                Rights.Told(everyone);
                HoePieces.Sync();
            });
            return;
        }

        CommandRights.Tell(net.GetPeers().FindAll(p => p.m_rpc == rpc), p => p.IsReady(), TellOne);
    }

    /// <summary>
    /// Why this game's player may not place, change or remove sources and drains with the hoe; null when they may (as
    /// for the command, where FlowingWater is); outside a world, refused.
    /// </summary>
    public static string MarkerRefusal(CommandRights.MarkerUse use) =>
        Rights.MarkerRefusal(ZNet.instance != null && ZNet.instance.IsServer(), ZNet.instance != null && ZNet.instance.LocalPlayerIsAdminOrHost(), use);

    /// <summary>Whether FlowingWater is where this game plays (alone, hosting, or on a server running it).</summary>
    public static bool ModHere => Rights.ModHere(ZNet.instance != null && ZNet.instance.IsServer());

    /// <summary>The server's CommandsForEveryone changed: every player's game is told.</summary>
    public static void TellEveryone()
    {
        if (ZNet.instance == null || !ZNet.instance.IsServer())
        {
            return;
        }

        CommandRights.Tell(ZNet.instance.GetPeers(), p => p.IsReady(), TellOne);
    }

    private static void TellOne(ZNetPeer peer) => peer.m_rpc.Invoke(EveryoneRpc, FlowingWaterPlugin.Settings.CommandsForEveryone.Value);

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
        Player player = Player.m_localPlayer;
        if (player == null || ZNet.instance == null)
        {
            terminal.AddString("flowingwater: needs a player in the world");
            return;
        }

        if (Rights.Refusal(ZNet.instance.LocalPlayerIsAdminOrHost()) is { } refusal)
        {
            terminal.AddString(refusal);
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
                terminal.AddString("flowingwater: " + VoxelWater.Describe(player.transform.position));
                break;
            case "debug":
                terminal.AddString("flowingwater: " + WaterBrick.DebugInfo(player.transform.position));
                break;
            case "solids":
                foreach (string line in SolidsView.Around(player.transform.position, (float)TileData.ZoneMetres / TileData.SizeFor(FlowingWaterPlugin.Settings.CellSize.Value)))
                {
                    terminal.AddString(line);
                    FlowingWaterPlugin.Log.LogInfo(line);
                }

                break;
            case "flat":
                WaterBrick.Flat = !WaterBrick.Flat;
                terminal.AddString("flowingwater: " + (WaterBrick.Flat ? "a plain colour (blue)" : "the water's material"));
                break;
            case "mat":
                terminal.AddString("flowingwater: " + Tune(args));
                break;
            case "lift":
                if (Number(terminal, args, 2, 0f, true, "the lift (m)", out float lift))
                {
                    WaterBrick.Lift = lift;
                    terminal.AddString($"flowingwater: water drawn {WaterBrick.Lift:0.##} m above its level");
                }

                break;
            default:
                terminal.AddString(Usage);
                break;
        }
    }

    // flowingwater mat custom|still <property> [value | r g b [a]]
    private static string Tune(Terminal.ConsoleEventArgs args)
    {
        if (args.Length < 4)
        {
            return "mat custom|still <property> [value | r g b [a]]; <property> alone shows it, \"list\" names them all";
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

    // A source (radius 0) or a drain, under the player.
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

        Vector3 position = player.transform.position;
        if (ZoneSystem.instance.GetGroundHeight(position, out float ground))
        {
            position.y = ground;
        }

        var placed = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        Marker.Write(placed.GetComponent<ZNetView>().GetZDO(), drain, rate, radius);

        terminal.AddString($"flowingwater: {kind} placed at {position.x:0},{position.y:0},{position.z:0}, {rate:0.##} m³/s"
            + (rate > MarkerRules.MaxRate ? $" (the server holds it to {MarkerRules.MaxRate:0} m³/s)" : string.Empty)
            + (drain ? (rate <= 0f ? " (takes everything)" : string.Empty) + $" within {radius:0.##} m" : string.Empty));
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
