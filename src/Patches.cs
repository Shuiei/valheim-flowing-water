using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using FlowingWater.Client;
using HarmonyLib;
using UnityEngine;

namespace FlowingWater;

/// <summary>Adds the plugin's prefabs to each world's prefab list before it is indexed.</summary>
[HarmonyPatch(typeof(ZNetScene), "Awake")]
internal static class PrefabPatch
{
    [SuppressMessage("CodeQuality", "IDE0051:Remove unused private members", Justification = "Harmony calls it by name.")]
    private static void Prefix(ZNetScene __instance)
    {
        Prefabs.Register(__instance);
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
