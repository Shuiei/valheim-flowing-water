using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Rendering;

namespace FlowingWater.Client;

/// <summary>
/// The source and drain in the hoe's pieces, and the piece that removes them: added to the hoe's piece table when the
/// game's item list wakes up (the table is the hoe prefab's own, kept from one world to the next: added once), with
/// icons drawn here; offered only to a player who may place them, as the game offers a piece it has enabled. Placed
/// sources and drains show only while one of these is chosen in the hoe in hand (<see cref="Shown"/>); the hoe, which
/// the game lets remove nothing, removes only them, and only then.
/// </summary>
internal static class HoePieces
{
    private const string HoeName = "Hoe";

    private const int IconSize = 64;

    // The game's own listings of the pieces a player knows and their tool offers (null if the game has them no more).
    private static readonly MethodInfo UpdateKnownRecipes = AccessTools.Method(typeof(Player), "UpdateKnownRecipesList");

    private static readonly MethodInfo UpdateAvailablePieces = AccessTools.Method(typeof(Player), "UpdateAvailablePiecesList");

    // The pieces and recipes the player knows, kept in their character: where the source and drain are forgotten.
    private static readonly FieldInfo KnownRecipes = AccessTools.Field(typeof(Player), "m_knownRecipes");

    // The pieces of the tool in the player's hand: whether it is the hoe (null if the game has it no more).
    private static readonly FieldInfo BuildPieces = AccessTools.Field(typeof(Player), "m_buildPieces");

    private static NextPlacement _next;

    // The hoe's piece table, once found, and whether the game let it remove pieces before the remover came.
    private static PieceTable _table;

    private static bool _tableRemoved;

    private static int _shownFrame = -1;

    private static bool _shown;

    private static bool _removing;

    /// <summary>
    /// Whether placed sources and drains show, this frame: the local player holds the hoe with one of its water pieces
    /// chosen (else they are hidden, and nothing aims at them); always without the hoe's remover.
    /// </summary>
    public static bool Shown
    {
        get
        {
            Look();
            return _shown || !HoeRemovePatch.Applied || BuildPieces == null;
        }
    }

    /// <summary>Whether the hoe's remover is chosen, this frame: the source or drain aimed at is marked to go.</summary>
    public static bool Removing
    {
        get
        {
            Look();
            return _removing;
        }
    }

    /// <summary>The values the next source or drain placed takes, kept for the session (from the settings at first).</summary>
    public static NextPlacement Next => _next ??= new NextPlacement(
        FlowingWaterPlugin.Settings.DefaultSourceRate.Value,
        FlowingWaterPlugin.Settings.DefaultDrainRate.Value,
        FlowingWaterPlugin.Settings.DefaultDrainRadius.Value);

    /// <summary>
    /// Adds the source, drain and remover to the hoe's pieces, in the category of its own (once; nothing on a game that
    /// draws nothing, a dedicated server, where no one holds a hoe), and lets the hoe remove: what the game asks of a
    /// tool before its remove kind of piece does anything (<see cref="MayRemove"/> keeps it to sources and drains).
    /// </summary>
    public static void AddTo(ObjectDB items)
    {
        // The main menu's ObjectDB holds no items yet: the world's, filled, comes when a world opens.
        if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null || items.m_items.Count == 0)
        {
            return;
        }

        GameObject hoe = items.GetItemPrefab(HoeName);
        PieceTable table = hoe?.GetComponent<ItemDrop>()?.m_itemData.m_shared.m_buildPieces;
        if (table == null)
        {
            FlowingWaterPlugin.Log.LogError($"The game's {HoeName} or its pieces not found: sources and drains cannot be placed with it (the flowingwater command still can)");
            return;
        }

        Piece.PieceCategory category = Piece.PieceCategory.Misc;
        foreach (GameObject other in table.m_pieces)
        {
            if (other != null && other.GetComponent<Piece>() is { } piece && !Prefabs.HoeTools.Contains(other))
            {
                category = piece.m_category;
                break;
            }
        }

        // The hoe's remove kept to sources and drains needs its patch: without it, no remover (the patch's error names
        // the method missing), and placed ones show at all times for the hammer's remove, as before the remover.
        bool removes = HoeRemovePatch.Applied && BuildPieces != null;
        if (BuildPieces == null)
        {
            FlowingWaterPlugin.Log.LogError("Player.m_buildPieces not found: the hoe does not remove sources and drains (the hammer still does), and they show at all times");
        }

        foreach (GameObject tool in Prefabs.HoeTools)
        {
            if (tool == Prefabs.Remover && !removes)
            {
                continue;
            }

            Piece piece = tool.GetComponent<Piece>();
            piece.m_category = category;
            if (piece.m_icon == null)
            {
                piece.m_icon = tool.GetComponent<Marker>() is { } marker
                    ? Icon(marker.IsDrain ? "drain" : "source", marker.IsDrain ? Whirl : Drop)
                    : Icon("remover", Crossed);
            }

            if (!table.m_pieces.Contains(tool))
            {
                table.m_pieces.Add(tool);
            }
        }

        if (_table != table)
        {
            _table = table;
            _tableRemoved = table.m_canRemovePieces;
            table.m_canRemovePieces |= removes;
        }
    }

    /// <summary>
    /// Whether the game's remove may go on for this player, at the piece the game found aimed at: anything with a tool
    /// that removes of its own; with the hoe, a source or drain while they show, nothing else.
    /// </summary>
    public static bool MayRemove(Player player, Piece aimed)
    {
        if (_table == null || _tableRemoved || Tool(player) != _table)
        {
            return true;
        }

        return player == Player.m_localPlayer && Shown && aimed != null && aimed.GetComponent<Marker>() != null;
    }

    private static PieceTable Tool(Player player) => BuildPieces?.GetValue(player) as PieceTable;

    // Once a frame: whether the hoe in hand has one of its water pieces chosen.
    private static void Look()
    {
        if (_shownFrame == Time.frameCount)
        {
            return;
        }

        _shownFrame = Time.frameCount;
        _shown = false;
        _removing = false;
        Player player = Player.m_localPlayer;
        if (_table == null || player == null || !player.InPlaceMode() || Tool(player) != _table)
        {
            return;
        }

        Piece selected = player.GetSelectedPiece();
        if (selected == null)
        {
            return;
        }

        _removing = selected.gameObject == Prefabs.Remover;
        _shown = _removing || selected.GetComponent<Marker>() != null;
    }

    /// <summary>
    /// Offers the source, drain and remover to this game's player when they may place them, else not: what the game reads
    /// when it lists the pieces a tool offers and those the player knows.
    /// </summary>
    public static void Offer()
    {
        bool may = Commands.MarkerRefusal(CommandRights.MarkerUse.Place) == null;
        foreach (GameObject tool in Prefabs.HoeTools)
        {
            tool.GetComponent<Piece>().m_enabled = may;
        }
    }

    /// <summary>
    /// The local player's source and drain made to match where they play: learned where FlowingWater is (the game
    /// teaches a tool's new pieces only when the inventory changes after a character is loaded, so a character older
    /// than them would not learn them until then), forgotten on a server without it (they would do nothing there).
    /// </summary>
    public static void Sync()
    {
        Player player = Player.m_localPlayer;
        if (player != null && !Commands.ModHere)
        {
            if (KnownRecipes?.GetValue(player) is HashSet<string> known)
            {
                MarkerKnowledge.Sync(modHere: false, known, Prefabs.HoeTools.Select(t => t.GetComponent<Piece>().m_name));
            }
            else
            {
                FlowingWaterPlugin.Log.LogError("Player.m_knownRecipes not found: a character keeps the source and drain learned on a server without FlowingWater (the hoe still does not offer them)");
            }
        }

        Refresh();
    }

    /// <summary>
    /// What the player may do changed: the pieces they know and their tool offers are listed again (else only when the
    /// game lists them next, as when a tool is taken in hand).
    /// </summary>
    public static void Refresh()
    {
        Offer();
        Player player = Player.m_localPlayer;
        if (player == null)
        {
            return;
        }

        if (UpdateKnownRecipes == null || UpdateAvailablePieces == null)
        {
            FlowingWaterPlugin.Log.LogError("Player.UpdateKnownRecipesList or UpdateAvailablePiecesList not found: the hoe offers the source and drain as the player's rights change only when a tool is next taken in hand");
            return;
        }

        UpdateKnownRecipes.Invoke(player, []);
        UpdateAvailablePieces.Invoke(player, []);
    }

    // A source: a drop of water. A drain: a dark whirl taking it in. The remover: a drop crossed out. Drawn here, no file.
    private static Sprite Icon(string kind, System.Func<float, float, Color32> paint)
    {
        var texture = new Texture2D(IconSize, IconSize, TextureFormat.RGBA32, false) { name = $"FlowingWater {kind} icon" };
        var pixels = new Color32[IconSize * IconSize];
        for (int y = 0; y < IconSize; y++)
        {
            for (int x = 0; x < IconSize; x++)
            {
                // Centred, -1..1, y up.
                float u = ((x + 0.5f) / IconSize * 2f) - 1f;
                float v = ((y + 0.5f) / IconSize * 2f) - 1f;
                pixels[(y * IconSize) + x] = paint(u, v);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return Sprite.Create(texture, new Rect(0f, 0f, IconSize, IconSize), new Vector2(0.5f, 0.5f));
    }

    // A drop: a round bottom drawn to a point at the top, light on its upper left.
    private static Color32 Drop(float u, float v)
    {
        const float Radius = 0.55f;
        float cy = -0.25f;
        float r = Mathf.Sqrt((u * u) + ((v - cy) * (v - cy)));
        bool inside = r < Radius;
        if (!inside && v > cy && v < 0.9f)
        {
            // Above the round part, the sides narrow straight to the tip.
            float half = Radius * (0.9f - v) / (0.9f - cy);
            inside = Mathf.Abs(u) < half;
        }

        if (!inside)
        {
            return new Color32(0, 0, 0, 0);
        }

        float light = Mathf.Clamp01(1f - (Vector2.Distance(new Vector2(u, v), new Vector2(-0.2f, -0.05f)) / 0.9f));
        var colour = Color.Lerp(new Color(0.12f, 0.42f, 0.85f), new Color(0.7f, 0.9f, 1f), light * light);
        return colour;
    }

    // A drop under a red cross.
    private static Color32 Crossed(float u, float v)
    {
        const float Half = 0.09f;
        bool cross = Mathf.Abs(u) < 0.8f && Mathf.Abs(v) < 0.8f && (Mathf.Abs(u - v) < Half * 1.41f || Mathf.Abs(u + v) < Half * 1.41f);
        return cross ? new Color32(220, 40, 30, 255) : Drop(u, v);
    }

    // A whirl: a dark round pool, rings turning in to a black middle.
    private static Color32 Whirl(float u, float v)
    {
        float r = Mathf.Sqrt((u * u) + (v * v));
        if (r > 0.85f)
        {
            return new Color32(0, 0, 0, 0);
        }

        float turn = Mathf.Atan2(v, u) + (r * 9f);
        float ring = 0.5f + (0.5f * Mathf.Sin(turn * 2f));
        var colour = Color.Lerp(new Color(0.1f, 0.25f, 0.45f), new Color(0.45f, 0.7f, 0.9f), ring * Mathf.Clamp01(r * 1.6f));
        return Color.Lerp(Color.black, colour, Mathf.Clamp01(r * 3f));
    }
}
