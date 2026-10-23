using System;
using System.Collections.Generic;

namespace FlowingWater;

/// <summary>
/// Who may use the flowingwater command, and place, change or remove sources and drains with the hoe, without the
/// game: the host (or a game alone) and the server's admins always; other players as the server's CommandsForEveryone
/// says, not their own. The server tells each player's game its value when the player joins and again when it
/// changes; a game joining a server has heard nothing from it yet.
/// </summary>
internal sealed class CommandRights
{
    /// <summary>What a player does to a source or drain with the hoe (and the hammer).</summary>
    public enum MarkerUse
    {
        /// <summary>Places one.</summary>
        Place,

        /// <summary>Changes a placed one's rate or radius.</summary>
        Change,

        /// <summary>Removes one.</summary>
        Remove,
    }

    /// <summary>The server's CommandsForEveryone as it told this game; null until it has.</summary>
    public bool? ServerSaysEveryone { get; private set; }

    /// <summary>This game joins a server: what the last one said no longer counts.</summary>
    public void Joining() => ServerSaysEveryone = null;

    /// <summary>
    /// Whether FlowingWater is where this game plays: in a game alone or hosted (it is here), or on a server that told
    /// its CommandsForEveryone (it is there). A server that said nothing has none (or one too old to tell): a source or
    /// drain placed there would do nothing, so the hoe neither offers them nor keeps them learned.
    /// </summary>
    /// <param name="isServer">Whether this game is the server (alone or hosting).</param>
    public bool ModHere(bool isServer) => isServer || ServerSaysEveryone != null;

    /// <summary>The server told its CommandsForEveryone.</summary>
    /// <param name="everyone">Its value.</param>
    public void Told(bool everyone) => ServerSaysEveryone = everyone;

    /// <summary>Why this game's player may not use the command; null when they may.</summary>
    /// <param name="adminOrHost">Whether the player is the host, alone, or one of the server's admins.</param>
    public string Refusal(bool adminOrHost) => adminOrHost || ServerSaysEveryone == true
        ? null
        : ServerSaysEveryone == null
            ? "flowingwater: the server has not said whether everyone may use it (is the mod on the server?); its admins may"
            : "flowingwater: only the server's admins may use it (CommandsForEveryone is off on the server)";

    /// <summary>
    /// Why this game's player may not place, change or remove sources and drains (in the game's centre message); null
    /// when they may: where FlowingWater is (<see cref="ModHere"/>), the same players as may use the command; on a
    /// server without it, no one.
    /// </summary>
    /// <param name="isServer">Whether this game is the server (alone or hosting).</param>
    /// <param name="adminOrHost">Whether the player is the host, alone, or one of the server's admins.</param>
    /// <param name="use">What they do.</param>
    public string MarkerRefusal(bool isServer, bool adminOrHost, MarkerUse use)
    {
        string verb = use switch
        {
            MarkerUse.Place => "place",
            MarkerUse.Change => "change",
            _ => "remove",
        };
        if (!ModHere(isServer))
        {
            return $"FlowingWater is not on this server: no one may {verb} sources and drains here";
        }

        return adminOrHost || ServerSaysEveryone != false
            ? null
            : $"Only the server's admins may {verb} sources and drains (CommandsForEveryone is off on the server)";
    }

    /// <summary>
    /// The server tells these players' games its CommandsForEveryone: each one ready (a game refused, by a wrong
    /// password or a full server, is not), once.
    /// </summary>
    /// <typeparam name="TPeer">A player's connection.</typeparam>
    /// <param name="peers">Those to tell.</param>
    /// <param name="ready">Whether one has joined.</param>
    /// <param name="tell">Tells one.</param>
    public static void Tell<TPeer>(IEnumerable<TPeer> peers, Func<TPeer, bool> ready, Action<TPeer> tell)
    {
        foreach (TPeer peer in peers)
        {
            if (ready(peer))
            {
                tell(peer);
            }
        }
    }
}
