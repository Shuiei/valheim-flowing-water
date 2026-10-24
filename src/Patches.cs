using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using FlowingWater.Client;
using HarmonyLib;
using UnityEngine;

namespace FlowingWater;

/// <summary>
/// A world opens: the plugin's prefabs go into its prefab list before it is indexed, and the source and drain panel, off
/// after a fault in the last world, has a fresh try.
/// </summary>
[HarmonyPatch(typeof(ZNetScene), "Awake")]
internal static class PrefabPatch
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Prefix(ZNetScene __instance)
    {
        Prefabs.Register(__instance);
        MarkerPanel.WorldOpened();
    }
}

/// <summary>
/// Tells the plugin the server's world objects are in, so the simulation finds the saved tiles.
/// ServerLoadWorld runs for every world a server or host opens: it reads a chunked save, or an old
/// save or none at all (a world just created), which LoadWorld alone would miss.
/// </summary>
[HarmonyPatch(typeof(ZNet), "ServerLoadWorld")]
internal static class WorldLoadedPatch
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Postfix()
    {
        FlowingWaterPlugin.WorldLoaded = true;
        FlowingWaterPlugin.Log.LogInfo("World loaded");
    }
}

/// <summary>A zone's ground, once the game has applied its own terrain changes to it, takes erosion's change too.</summary>
[HarmonyPatch(typeof(Heightmap), "ApplyModifiers")]
internal static class GroundChangePatch
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Postfix(Heightmap __instance)
    {
        GroundChanges.ApplyTo(__instance);
    }
}

/// <summary>Registers the console command once the game's own are in.</summary>
[HarmonyPatch(typeof(Terminal), "InitTerminal")]
internal static class CommandPatch
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Postfix()
    {
        Commands.Register();
    }
}

/// <summary>A player joins a server: the server tells the player's game its CommandsForEveryone.</summary>
[HarmonyPatch(typeof(ZNet), "RPC_PeerInfo")]
internal static class CommandRightsPatch
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Postfix(ZNet __instance, ZRpc rpc)
    {
        Commands.Joined(__instance, rpc);
    }
}

/// <summary>The game's point-in-water checks (fishing, AI, building, effects) also see flowing water.</summary>
[HarmonyPatch(typeof(Floating), nameof(Floating.GetLiquidLevel))]
internal static class LiquidLevelPatch
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Postfix(Vector3 p, LiquidType type, ref float __result)
    {
        if (type is LiquidType.Water or LiquidType.All)
        {
            __result = Mathf.Max(__result, VoxelWater.SurfaceAt(p));
        }
    }
}

/// <inheritdoc cref="LiquidLevelPatch"/>
[HarmonyPatch(typeof(Floating), nameof(Floating.GetWaterLevel))]
internal static class WaterLevelPatch
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Postfix(Vector3 p, ref float __result)
    {
        __result = Mathf.Max(__result, VoxelWater.SurfaceAt(p));
    }
}

/// <inheritdoc cref="LiquidLevelPatch"/>
[HarmonyPatch(typeof(Floating), nameof(Floating.IsUnderWater))]
internal static class UnderWaterPatch
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Postfix(Vector3 p, ref bool __result)
    {
        if (!__result)
        {
            __result = VoxelWater.SurfaceAt(p) > p.y;
        }
    }
}

/// <summary>
/// The water level a character, item or fish is told by any water volume is never below the
/// flowing water over it: the zone's sea volume and the tile's trigger both report, and the higher
/// surface is the one that counts.
/// </summary>
[HarmonyPatch]
internal static class SetLiquidLevelPatch
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static IEnumerable<MethodBase> TargetMethods()
    {
        foreach (Type type in (Type[])[typeof(Character), typeof(Floating), typeof(Fish)])
        {
            MethodInfo method = AccessTools.Method(type, "SetLiquidLevel", [typeof(float), typeof(LiquidType), typeof(Component)]);
            if (method != null)
            {
                yield return method;
            }
        }
    }

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Prefix(Component __instance, ref float level, LiquidType type)
    {
        // Water volumes also report to objects already destroyed, when a world closes.
        if (type == LiquidType.Water && __instance != null)
        {
            level = Mathf.Max(level, VoxelWater.SurfaceAt(__instance.transform.position));
        }
    }
}

/// <summary>A swimming character drifts with the current (its own game simulates it: the owner).</summary>
[HarmonyPatch(typeof(Character), "UpdateMotion")]
internal static class CurrentPatch
{
    private static readonly AccessTools.FieldRef<Character, Rigidbody> Body = AccessTools.FieldRefAccess<Character, Rigidbody>("m_body");

    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Postfix(Character __instance)
    {
        if (!__instance.IsSwimming() || !__instance.IsOwner() || !VoxelWater.CurrentAt(__instance.transform.position, out Vector3 current))
        {
            return;
        }

        Rigidbody body = Body(__instance);
        if (body != null)
        {
            body.linearVelocity += current * FlowingWaterPlugin.Settings.CurrentStrength.Value;
        }
    }
}

/// <summary>The game's methods a patch for the sources and drains changes (<see cref="PatchTargets"/>), by name.</summary>
internal static class GameMethods
{
    /// <summary>The methods, each the only one of its name in its type, looked up with Harmony's AccessTools.</summary>
    public static PatchTargets For(string patch, params (Type Type, string Name)[] methods) =>
        new(patch, FlowingWaterPlugin.Log.LogError, [.. methods.Select(m => ($"{m.Type.Name}.{m.Name}", (Func<MethodBase>)(() => AccessTools.Method(m.Type, m.Name))))]);

    /// <summary>
    /// For the patches that refuse a source or drain to a player who may not place or remove one: the player told why in
    /// the centre of the screen, and the game's method skipped with false as its result.
    /// </summary>
    /// <returns>Whether the game's method runs.</returns>
    public static bool MarkerAllowed(Player player, Piece piece, CommandRights.MarkerUse use, ref bool result)
    {
        if (piece == null || piece.GetComponent<Marker>() == null || Commands.MarkerRefusal(use) is not { } refusal)
        {
            return true;
        }

        player.Message(MessageHud.MessageType.Center, refusal);
        result = false;
        return false;
    }
}

/// <summary>
/// The game's item list woke up (a world's, or the main menu's copy of it): the source and drain go into the hoe's
/// pieces, once.
/// </summary>
[HarmonyPatch]
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls them by name.")]
internal static class HoePiecesPatch
{
    private static readonly PatchTargets Targets = GameMethods.For(nameof(HoePiecesPatch), (typeof(ObjectDB), "Awake"), (typeof(ObjectDB), nameof(ObjectDB.CopyOtherDB)));

    private static bool Prepare(MethodBase original) => Targets.Prepare(original);

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Found;

    private static void Postfix(ObjectDB __instance)
    {
        HoePieces.AddTo(__instance);
    }
}

/// <summary>
/// The local player spawned, their character loaded: they learn the source and drain where FlowingWater is, and forget
/// them on a server without it (the game itself teaches a tool's new pieces only when the inventory next changes).
/// </summary>
[HarmonyPatch]
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls them by name.")]
internal static class LearnPiecesPatch
{
    private static readonly PatchTargets Targets = GameMethods.For(nameof(LearnPiecesPatch), (typeof(Player), nameof(Player.OnSpawned)));

    private static bool Prepare(MethodBase original) => Targets.Prepare(original);

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Found;

    private static void Postfix(Player __instance)
    {
        if (__instance == Player.m_localPlayer)
        {
            HoePieces.Sync();
        }
    }
}

/// <summary>
/// The source and drain are offered (listed in the hoe, learned as pieces) only to a player who may place them: the
/// game reads whether a piece is enabled each time it lists them.
/// </summary>
[HarmonyPatch]
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls them by name.")]
internal static class OfferPiecesPatch
{
    private static readonly PatchTargets Targets = GameMethods.For(nameof(OfferPiecesPatch), (typeof(PieceTable), nameof(PieceTable.UpdateAvailable)), (typeof(Player), "UpdateKnownRecipesList"));

    private static bool Prepare(MethodBase original) => Targets.Prepare(original);

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Found;

    private static void Prefix()
    {
        HoePieces.Offer();
    }
}

/// <summary>
/// A player who may not place a source or drain is refused with the game's centre message, whatever offered it (with
/// building costs off, the game lists every piece, enabled or not).
/// </summary>
[HarmonyPatch]
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls them by name.")]
internal static class PlaceMarkerPatch
{
    private static readonly PatchTargets Targets = GameMethods.For(nameof(PlaceMarkerPatch), (typeof(Player), nameof(Player.TryPlacePiece)));

    private static bool Prepare(MethodBase original) => Targets.Prepare(original);

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Found;

    private static bool Prefix(Player __instance, Piece piece, ref bool __result) =>
        GameMethods.MarkerAllowed(__instance, piece, CommandRights.MarkerUse.Place, ref __result);
}

/// <summary>
/// A player who may not remove a source or drain is refused with the game's centre message, at the check the game
/// makes after its own (wards, no-build places) and before it removes the piece.
/// </summary>
[HarmonyPatch]
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls them by name.")]
internal static class RemoveMarkerPatch
{
    private static readonly PatchTargets Targets = GameMethods.For(nameof(RemoveMarkerPatch), (typeof(Player), "CheckCanRemovePiece"));

    private static bool Prepare(MethodBase original) => Targets.Prepare(original);

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Found;

    private static bool Prefix(Player __instance, Piece piece, ref bool __result) =>
        GameMethods.MarkerAllowed(__instance, piece, CommandRights.MarkerUse.Remove, ref __result);
}

/// <summary>
/// While the source and drain panel takes the keyboard, the game takes it for one of its own text boxes: the player
/// does not move, the camera does not turn, the menu, chat and map do not open, and the mouse is free.
/// </summary>
[HarmonyPatch]
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls them by name.")]
internal static class PanelInputPatch
{
    private static readonly PatchTargets Targets = GameMethods.For(nameof(PanelInputPatch), (typeof(TextInput), nameof(TextInput.IsVisible)));

    private static bool Prepare(MethodBase original) => Targets.Prepare(original);

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Found;

    private static void Postfix(ref bool __result)
    {
        __result |= MarkerPanel.TakesInput;
    }
}
