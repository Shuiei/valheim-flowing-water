using System;
using System.Globalization;
using FlowingWater.Client;
using FlowingWater.Server;
using UnityEngine;

namespace FlowingWater;

/// <summary>The flowingwater console command: place and remove sources and drains, inspect the water.</summary>
internal static class Commands
{
    private const string Usage = "flowingwater source [m3/s] | drain [m3/s, 0 = all] [radius m] | remove | wipe | info | debug | sound | solids | gpu [colours|columns] | particles [m3|clear|spheres|full|look N] | flat | lift [m] | mat custom|card|falls|spray|still <property> [values]";

    // How the server tells each player its CommandsForEveryone.
    private const string EveryoneRpc = "FlowingWater_CommandsForEveryone";

    // How a player's game asks the server to wipe its water, and how the server answers (a line for the console).
    private const string WipeRpc = "FlowingWater_Wipe";

    private const string WipedRpc = "FlowingWater_Wiped";

    private static bool _registered;

    // Who may use the command: the server's CommandsForEveryone, as it told this player's game.
    private static readonly CommandRights Rights = new();

    // The server telling this game its CommandsForEveryone tells it FlowingWater is there: the hoe's source and drain
    // are learned (or offered as the rights now say) at once.
    static Commands() => Rights.Told += HoePieces.Sync;

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
                Rights.Tell(everyone);
            });
            rpc.Register<string>(WipedRpc, (_, answer) =>
            {
                ReplyOn(Console.instance).Say(answer);
            });
            return;
        }

        rpc.Register(WipeRpc, WipeAsked);
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

    // A player's game asked the server to wipe its water: done for one of the server's admins, refused to anyone else
    // (whatever CommandsForEveryone says: it takes every player's water), and answered either way.
    private static void WipeAsked(ZRpc rpc)
    {
        ZNetPeer peer = ZNet.instance.GetPeers().Find(p => p.m_rpc == rpc);
        string answer = peer == null || !peer.IsReady() ? "flowingwater: the server does not know you yet; try again"
            : !ZNet.instance.IsAdmin(peer.m_socket.GetHostName()) ? "flowingwater: only the server's admins may wipe the water"
            : Wipe();
        rpc.Invoke(WipedRpc, answer);
    }

    // The server's water wiped: what to tell the one who asked.
    private static string Wipe()
    {
        if (FlowingWaterPlugin.Server == null)
        {
            return "flowingwater: the water is not running on the server (Enabled is off, or the world is still opening)";
        }

        (float water, int markers) = FlowingWaterPlugin.Server.Wipe();
        return FormattableString.Invariant($"flowingwater: wiped {water:0.#} m³ of water and removed {markers} sources and drains");
    }

    // An exception out of a console command reaches Unity's event system, whose logging of it has
    // crashed the game outright; it is logged here instead, with its message on the console.
    private static void Run(Terminal.ConsoleEventArgs args)
    {
        CommandReply terminal = ReplyOn(args.Context);
        try
        {
            RunUnguarded(terminal, args);
        }
        catch (Exception e)
        {
            terminal.Failed(args.ArgsAll, e);
        }
    }

    // What a command says on a console (none: only the log), logged with General.Debug as it is when said.
    private static CommandReply ReplyOn(Terminal console) =>
        new(
            line => console?.AddString(line),
            FlowingWaterPlugin.Log.LogInfo,
            FlowingWaterPlugin.Log.LogError,
            () => FlowingWaterPlugin.Settings.Debug.Value);

    private static void RunUnguarded(CommandReply terminal, Terminal.ConsoleEventArgs args)
    {
        terminal.Asked(args.ArgsAll);
        Player player = Player.m_localPlayer;
        if (player == null || ZNet.instance == null)
        {
            terminal.Say("flowingwater: needs a player in the world");
            return;
        }

        if (Rights.Refusal(ZNet.instance.LocalPlayerIsAdminOrHost()) is { } refusal)
        {
            terminal.Say(refusal);
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
            case "wipe":
                WipeAll(terminal);
                break;
            case "info":
                terminal.Say("flowingwater: " + VoxelWater.Describe(player.transform.position));
                break;
            case "sound":
                terminal.SayLogged("flowingwater: " + WaterSound.Describe());
                break;
            case "debug":
                terminal.Say("flowingwater: " + BrickMeasures.DebugInfo(player.transform.position));
                break;
            case "solids":
                foreach (string line in SolidsView.Around(player.transform.position, FlowingWaterPlugin.Settings.CellSize.Value, FlowingWaterPlugin.Server?.Solids))
                {
                    terminal.SayLogged(line);
                }

                break;
            case "gpu":
                if (args.Length > 2 && args.Args[2].Equals("colours", StringComparison.OrdinalIgnoreCase))
                {
                    GpuWater.DebugColours = !GpuWater.DebugColours;
                    terminal.Say("flowingwater: " + (GpuWater.DebugColours ? "the card's water blue, the bricks' surface red, their sides and falls yellow" : "the water's own colours"));
                }
                else if (args.Length > 2 && args.Args[2].Equals("columns", StringComparison.OrdinalIgnoreCase))
                {
                    foreach (string line in GpuWater.Columns(player.transform.position))
                    {
                        terminal.SayLogged(line);
                    }
                }
                else
                {
                    terminal.Say("flowingwater: " + GpuWater.Describe());
                }

                break;
            case "particles":
                if (args.Length > 2 && args.Args[2].Equals("clear", StringComparison.OrdinalIgnoreCase))
                {
                    terminal.Say("flowingwater: " + GpuWater.ClearParticles());
                }
                else if (args.Length > 2 && args.Args[2].Equals("spheres", StringComparison.OrdinalIgnoreCase))
                {
                    ParticleSurface.Spheres = !ParticleSurface.Spheres;
                    terminal.Say("flowingwater: the particle water drawn as " + (ParticleSurface.Spheres ? "spheres" : "water"));
                }
                else if (args.Length > 3 && args.Args[2].Equals("look", StringComparison.OrdinalIgnoreCase) && int.TryParse(args.Args[3], out int look))
                {
                    // The volume water's drawing taken apart, for testing: 0 as it is, 1 the density alone (no sheet on
                    // the ground), 2 the sheet alone, 3 the water seen through as grey, 4 the surface's distance in
                    // bands of a metre, 5 the surface's normal as a colour, 6 no smoothing of the surface.
                    VolumeSurface.Look = look;
                    terminal.Say("flowingwater: the volume water drawn as look " + look);
                }
                else if (args.Length > 2 && args.Args[2].Equals("full", StringComparison.OrdinalIgnoreCase))
                {
                    ParticleSurface.Scale = ParticleSurface.Scale < 1f ? 1f : 0.5f;
                    terminal.Say("flowingwater: the particle water's surface drawn at " + (ParticleSurface.Scale < 1f ? "half the screen's size" : "the screen's full size"));
                }
                else if (Number(terminal, args, 2, 2f, false, "the water poured (m³)", out float poured))
                {
                    terminal.Say("flowingwater: " + GpuWater.Pour(player.transform.position, poured));
                }

                break;
            case "flat":
                WaterBrick.Flat = !WaterBrick.Flat;
                terminal.Say("flowingwater: " + (WaterBrick.Flat ? "a plain colour (blue)" : "the water's material"));
                break;
            case "mat":
                terminal.Say("flowingwater: " + Tune(args));
                break;
            case "lift":
                if (Number(terminal, args, 2, 0f, true, "the lift (m)", out float lift))
                {
                    WaterBrick.Lift = lift;
                    terminal.Say(FormattableString.Invariant($"flowingwater: water drawn {WaterBrick.Lift:0.##} m above its level"));
                }

                break;
            default:
                terminal.Say(Usage);
                break;
        }
    }

    // flowingwater mat custom|card|falls|spray|still <property> [value | r g b [a]]
    private static string Tune(Terminal.ConsoleEventArgs args)
    {
        if (args.Length < 4)
        {
            return "mat custom|card|falls|spray|still <property> [value | r g b [a]]; <property> alone shows it, \"list\" names them all";
        }

        string kind = args.Args[2].ToLowerInvariant();
        string property = args.Args[3];
        float[] values = new float[args.Length - 4];
        for (int i = 0; i < values.Length; i++)
        {
            if (!Parse(args.Args[4 + i], out values[i]))
            {
                return $"{args.Args[4 + i]} is not a number";
            }
        }

        return Materials.Tune(kind, property, values);
    }

    // The number at that place after the command, or the fallback (a setting's) when there is none. A word that is
    // not a number, or a number out of range (below 0, or 0 when zero is not allowed), is refused with the reason on
    // the console, and the command does nothing; so is a fallback out of range (a setting written by hand).
    private static bool Number(CommandReply terminal, Terminal.ConsoleEventArgs args, int place, float fallback, bool zeroAllowed, string what, out float value)
    {
        bool given = args.Length > place;
        string text = given ? args.Args[place] : null;
        value = fallback;
        if ((!given || Parse(text, out value)) && !float.IsNaN(value) && !float.IsInfinity(value) && (zeroAllowed ? value >= 0f : value > 0f))
        {
            return true;
        }

        terminal.Say(given
            ? $"flowingwater: {what} must be a number {(zeroAllowed ? "of 0 or more" : "above 0")}, not \"{text}\"; nothing done"
            : FormattableString.Invariant($"flowingwater: {what} must be a number {(zeroAllowed ? "of 0 or more" : "above 0")}: the settings' default is {fallback}; give one, or fix the setting"));
        return false;
    }

    // A number as written on the console, whatever the game's language (a point for the decimals).
    private static bool Parse(string text, out float value) => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    // A source (radius 0) or a drain, under the player.
    private static void Place(CommandReply terminal, Player player, string prefabName, float rate, float radius)
    {
        bool drain = radius > 0f;
        string kind = drain ? "drain" : "source";
        GameObject prefab = ZNetScene.instance.GetPrefab(prefabName);
        if (prefab == null)
        {
            terminal.Say($"flowingwater: prefab {prefabName} missing (is the mod on the server too?)");
            return;
        }

        Vector3 position = player.transform.position;
        if (ZoneSystem.instance.GetGroundHeight(position, out float ground))
        {
            position.y = ground;
        }

        var placed = UnityEngine.Object.Instantiate(prefab, position, Quaternion.identity);
        ZDO zdo = placed.GetComponent<ZNetView>().GetZDO();
        Marker.Write(zdo, drain, rate, radius);

        // Placed by this player, as the game writes of a piece the hoe places: the server simulates a marker only once
        // it knows its placer may place one.
        zdo.Set(ZDOVars.s_creator, player.GetPlayerID());

        terminal.Say(FormattableString.Invariant($"flowingwater: {kind} placed at {position.x:0},{position.y:0},{position.z:0}, {rate:0.##} m³/s")
            + (rate > MarkerRules.MaxRate ? FormattableString.Invariant($" (the server holds it to {MarkerRules.MaxRate:0} m³/s)") : string.Empty)
            + (drain ? (rate <= 0f ? " (takes everything)" : string.Empty) + FormattableString.Invariant($" within {radius:0.##} m") : string.Empty));
    }

    // All the mod's water and every placed source and drain gone: done here where this game is the server (alone or
    // hosting: the host may), else asked of the server, which answers when it is done.
    private static void WipeAll(CommandReply terminal)
    {
        if (ZNet.instance.IsServer())
        {
            terminal.Say(Wipe());
            return;
        }

        if (!ZNet.instance.LocalPlayerIsAdminOrHost())
        {
            terminal.Say("flowingwater: only the server's admins may wipe the water");
            return;
        }

        ZNetPeer server = ZNet.instance.GetServerPeer();
        if (server == null)
        {
            terminal.Say("flowingwater: not connected to a server");
            return;
        }

        server.m_rpc.Invoke(WipeRpc);
        terminal.Say("flowingwater: asked the server to wipe its water");
    }

    // How far from the player the command's remove looks for a source or drain, metres.
    private const float RemoveReach = 5f;

    private static void Remove(CommandReply terminal, Player player)
    {
        var marker = Marker.Nearest(player.transform.position, RemoveReach);
        if (marker == null)
        {
            terminal.Say(FormattableString.Invariant($"flowingwater: no source or drain within {RemoveReach:0} m"));
            return;
        }

        string kind = marker.IsDrain ? "drain" : "source";
        marker.Remove();
        terminal.Say($"flowingwater: {kind} removed");
    }
}
