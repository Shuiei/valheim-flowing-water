using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using FlowingWater.Client;
using HarmonyLib;
using UnityEngine;

namespace FlowingWater;

/// <summary>
/// A world opens: the plugin's prefabs go into its prefab list before it is indexed, and the source and drain panel and
/// the water moved on the graphics card, off after a fault in the last world, have a fresh try.
/// </summary>
[HarmonyPatch(typeof(ZNetScene), "Awake")]
internal static class PrefabPatch
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Prefix(ZNetScene __instance)
    {
        Prefabs.Register(__instance);
        MarkerPanel.WorldOpened();
        Materials.WorldOpened();
        WaterBrick.WorldOpened();
        GpuWater.WorldOpened();
        JetArcs.WorldOpened();
        WaterSound.WorldOpened();
        ShipCurrent.WorldOpened();
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
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls them by name.")]
internal static class SetLiquidLevelPatch
{
    private static readonly Type[] Parameters = [typeof(float), typeof(LiquidType), typeof(Component)];

    private static readonly PatchTargets Targets = GameMethods.For(nameof(SetLiquidLevelPatch), (typeof(Character), "SetLiquidLevel", Parameters), (typeof(Floating), "SetLiquidLevel", Parameters), (typeof(Fish), "SetLiquidLevel", Parameters));

    private static bool Prepare(MethodBase original) => Targets.Prepare(original);

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Found;

    private static void Prefix(Component __instance, ref float level, LiquidType type)
    {
        // Water volumes also report to objects already destroyed, when a world closes.
        if (type == LiquidType.Water && __instance != null)
        {
            level = Mathf.Max(level, VoxelWater.SurfaceAt(__instance.transform.position));
        }
    }
}

/// <summary>
/// The current adds to a character's own speed in the water (its own game simulates it: the owner): a swimmer's all of
/// it; a wader's across the ground, a share growing with the depth, all of it once the water would take them off their
/// feet (<see cref="CurrentCarry.WadeShare"/>), so running down a stream is faster than on the ground and walking up it
/// slower, and fast water sweeps them along (standing still in it, they piled it up on themselves). The game makes a
/// character's speed each step from the last step's, so the current added at a step is taken off again before the
/// next (else it built on itself: a wader ran 200 m in four seconds). Left out, with an error, when
/// the game's body field is not found: it is looked up as the patch is prepared, not at the first character to move,
/// where a failure would have been thrown at every one of them.
/// </summary>
[HarmonyPatch]
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls them by name.")]
internal static class CurrentPatch
{
    private static readonly PatchTargets Targets = GameMethods.For(nameof(CurrentPatch), (typeof(Character), "UpdateMotion"));

    // The current added to each character's speed at its last step, taken off before its next.
    private static readonly ConditionalWeakTable<Character, StrongBox<Vector3>> Added = new();

    private static AccessTools.FieldRef<Character, Rigidbody> _body;

    private static bool Prepare(MethodBase original)
    {
        if (original != null)
        {
            return true;
        }

        if (AccessTools.Field(typeof(Character), "m_body") is not { } field || field.FieldType != typeof(Rigidbody))
        {
            FlowingWaterPlugin.Log.LogError("The game's Character.m_body was not found (a game update?): CurrentPatch is left out, the current carries no swimmer");
            return false;
        }

        _body = AccessTools.FieldRefAccess<Character, Rigidbody>(field);
        return Targets.Prepare(original);
    }

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Found;

    private static void Prefix(Character __instance)
    {
        if (Added.TryGetValue(__instance, out StrongBox<Vector3> added))
        {
            Rigidbody body = _body(__instance);
            if (body != null)
            {
                body.linearVelocity -= added.Value;
            }

            Added.Remove(__instance);
        }
    }

    private static void Postfix(Character __instance)
    {
        if (!__instance.IsOwner())
        {
            return;
        }

        Vector3 position = __instance.transform.position;
        bool swimming = __instance.IsSwimming();
        float depth = swimming ? 0f : VoxelWater.SurfaceAt(position) - position.y;
        if ((!swimming && !(depth > 0f)) || !VoxelWater.CurrentAt(position, out Vector3 current))
        {
            return;
        }

        Rigidbody body = _body(__instance);
        if (body == null)
        {
            return;
        }

        float strength = FlowingWaterPlugin.Settings.CurrentStrength.Value;
        Vector3 add = swimming ? current * strength : new Vector3(current.x, 0f, current.z) * (strength * CurrentCarry.WaderShare(depth, new Vector2(current.x, current.z).magnitude));
        if (add != Vector3.zero)
        {
            body.linearVelocity += add;
            Added.Add(__instance, new StrongBox<Vector3>(add));
        }
    }
}

/// <summary>The game's methods a patch for the sources and drains changes (<see cref="PatchTargets"/>), by name.</summary>
internal static class GameMethods
{
    /// <summary>
    /// The methods, each the only one of its name in its type, looked up with Harmony's AccessTools (one given a twin
    /// by a game update counts as not found: <see cref="PatchTargets"/>).
    /// </summary>
    public static PatchTargets For(string patch, params (Type Type, string Name)[] methods) =>
        For(patch, [.. methods.Select(m => (m.Type, m.Name, (Type[])null))]);

    /// <summary>The methods, each by its parameter types too (null: the only one of its name), looked up with Harmony's AccessTools.</summary>
    public static PatchTargets For(string patch, params (Type Type, string Name, Type[] Parameters)[] methods) =>
        new(patch, FlowingWaterPlugin.Log.LogError, [.. methods.Select(m => ($"{m.Type.Name}.{m.Name}", (Func<MethodBase>)(() => AccessTools.Method(m.Type, m.Name, m.Parameters))))]);

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
/// A click with the hoe's remover places nothing: it runs the game's remove at what the player aims at instead (which
/// <see cref="HoeRemovePatch"/> keeps to a source or drain), its result the click's.
/// </summary>
[HarmonyPatch]
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls them by name.")]
internal static class RemoverClickPatch
{
    private static readonly PatchTargets Targets = GameMethods.For(nameof(RemoverClickPatch), (typeof(Player), nameof(Player.TryPlacePiece)));

    /// <summary>Whether it was applied: without it, the hoe has no remover (a click with it would place it).</summary>
    public static bool Applied => Targets.Found.Any();

    private static bool Prepare(MethodBase original) => Targets.Prepare(original);

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Found;

    private static bool Prefix(Player __instance, Piece piece, ref bool __result)
    {
        if (piece == null || piece.gameObject != Prefabs.Remover)
        {
            return true;
        }

        __result = HoePieces.RemoveAimed(__instance);
        return false;
    }
}

/// <summary>
/// The hoe removes nothing of the game's: its remove (its remover's click) goes on only at a source or drain aimed at
/// while they show, and does nothing else.
/// </summary>
[HarmonyPatch]
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls them by name.")]
internal static class HoeRemovePatch
{
    private static readonly PatchTargets Targets = GameMethods.For(nameof(HoeRemovePatch), (typeof(Player), "RemovePiece"));

    /// <summary>Whether it was applied: without it, the hoe is not let remove (it would remove anything).</summary>
    public static bool Applied => Targets.Found.Any();

    private static bool Prepare(MethodBase original) => Targets.Prepare(original);

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Found;

    private static bool Prefix(Player __instance, ref bool __result)
    {
        if (HoePieces.MayRemove(__instance, __instance.GetHoveringPiece()))
        {
            return true;
        }

        __result = false;
        return false;
    }
}

/// <summary>
/// A ship (any piece placed on water) can be placed on the flowing water: where the camera's ray meets its surface
/// before what the game's own ray met (the ground under it, or nothing), the game is told it met water there, as it is
/// at the sea's surface. Within the game's reach for the piece; the sea's own surface is found by the game.
/// </summary>
[HarmonyPatch]
[SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls them by name.")]
internal static class ShipPlacePatch
{
    // The game's ray reaches this far, metres.
    private const float GameRay = 50f;

    private static readonly PatchTargets Targets = GameMethods.For(nameof(ShipPlacePatch), (typeof(Player), "PieceRayTest"));

    private static bool Prepare(MethodBase original) => Targets.Prepare(original);

    private static IEnumerable<MethodBase> TargetMethods() => Targets.Found;

    private static void Postfix(Player __instance, bool water, ref bool __result, ref Vector3 point, ref Vector3 normal, ref Piece piece, ref Heightmap heightmap, ref Collider waterSurface)
    {
        if (!water || GameCamera.instance == null)
        {
            return;
        }

        Transform camera = GameCamera.instance.transform;
        float within = __result ? Vector3.Distance(camera.position, point) : GameRay;
        if (!VoxelWater.RayToSurface(camera.position, camera.forward, within, out Vector3 hit))
        {
            return;
        }

        float reach = __instance.m_maxPlaceDistance + (__instance.GetSelectedPiece() is { } selected ? selected.m_extraPlacementDistance : 0f);
        if (Vector3.Distance(__instance.m_eye.position, hit) >= reach)
        {
            return;
        }

        __result = true;
        point = hit;
        normal = Vector3.up;
        piece = null;
        heightmap = null;
        waterSurface = VoxelWater.StandIn;
    }
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
