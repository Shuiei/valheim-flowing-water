using System;
using System.Collections.Generic;

namespace FlowingWater.Server;

/// <summary>
/// Which placed sources and drains the server simulates, without the game: a player's game places a marker whether its
/// player may or not (its own check is only a courtesy to the player; a game with the setting changed skips it), so
/// the server checks who placed each one, by what the game wrote into the marker about its placer. That is the game's
/// word: one changed to write a false placer gets past this, as past any check the game's world allows; this keeps
/// honest games to the server's setting. One is allowed when the server's CommandsForEveryone is on, or its placer is on the server and
/// may place (the host, an admin); one whose placer is not on the server now, or not written in it (placed with an
/// older version's console), waits, ignored, until they are; one whose placer may not is refused, ignored. What the
/// server allowed once stays allowed: the allowed markers are kept in a file of the server's (<see cref="Lines"/>),
/// not in the markers themselves, where a player's game could write it.
/// </summary>
internal sealed class MarkerRights
{
    /// <summary>What the server does with a marker.</summary>
    public enum Verdict
    {
        /// <summary>Simulated.</summary>
        Allowed,

        /// <summary>Ignored until its placer is on the server to be checked.</summary>
        Waiting,

        /// <summary>Ignored: its placer may not place one.</summary>
        Refused,
    }

    private readonly Action<string> _warn;

    private readonly HashSet<string> _allowed = [];

    // The markers told of as waiting or refused, each once.
    private readonly HashSet<string> _told = [];

    /// <summary>Rights that tell what they ignore.</summary>
    /// <param name="warn">Logs a warning.</param>
    public MarkerRights(Action<string> warn) => _warn = warn;

    /// <summary>Whether the allowed markers changed since <see cref="Lines"/> was last read (the file is to be written again).</summary>
    public bool Changed { get; private set; }

    /// <summary>The allowed markers as kept in the file, one an id, and forgets that they changed.</summary>
    public IEnumerable<string> Lines
    {
        get
        {
            Changed = false;
            return [.. _allowed];
        }
    }

    /// <summary>The allowed markers as read from the file.</summary>
    public void Read(IEnumerable<string> lines)
    {
        foreach (string line in lines)
        {
            string id = line.Trim();
            if (id.Length > 0)
            {
                _allowed.Add(id);
            }
        }
    }

    /// <summary>
    /// What the server does with a marker, and remembers it when allowed.
    /// </summary>
    /// <param name="id">The marker's object, as written in the file.</param>
    /// <param name="everyone">The server's CommandsForEveryone.</param>
    /// <param name="creator">Who placed it: the game's player id of the character, 0 when not written in it.</param>
    /// <param name="creatorMayPlace">Whether its placer may place one (the host, an admin), or null when they are not on the server now.</param>
    /// <param name="where">What and where it is, for the warning.</param>
    public Verdict Decide(string id, bool everyone, long creator, bool? creatorMayPlace, Func<string> where)
    {
        if (_allowed.Contains(id))
        {
            return Verdict.Allowed;
        }

        Verdict verdict = everyone || creatorMayPlace == true ? Verdict.Allowed
            : creatorMayPlace == false ? Verdict.Refused
            : Verdict.Waiting;
        if (verdict == Verdict.Allowed)
        {
            _allowed.Add(id);
            Changed = true;
            _told.Remove(id);
        }
        else if (_told.Add(id))
        {
            _warn(verdict == Verdict.Refused
                ? $"{where()}: placed by a player who may not place one (CommandsForEveryone is off); ignored"
                : creator == 0
                    ? $"{where()}: placed with an older version's console, by no one the server can check; ignored while CommandsForEveryone is off (turn it on for a moment to allow every marker in the world, or place it again)"
                    : $"{where()}: its placer is not on the server to be checked, or did not place it from this game of theirs (CommandsForEveryone is off); ignored until they are, or it is placed again");
        }

        return verdict;
    }

    /// <summary>Forgets the allowed markers not among these (gone from the world); those told of too.</summary>
    public void Keep(HashSet<string> ids)
    {
        Changed |= _allowed.RemoveWhere(id => !ids.Contains(id)) > 0;
        _told.RemoveWhere(id => !ids.Contains(id));
    }
}
