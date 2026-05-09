using DisplayOrbs.DisplayOrbsCode.Orbs;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;

namespace DisplayOrbs.DisplayOrbsCode.Patches;

[HarmonyPatch(typeof(OrbCmd), nameof(OrbCmd.AddSlots))]
public static class OrbAddCapacityPatch
{
    [HarmonyPrefix]
    private static void OrbAddCapacity(Player player, ref int amount)
    {
        amount = DisplayOrbManager.PrepareToAddSlots(player, amount);
    }
}

[HarmonyPatch(typeof(OrbCmd), nameof(OrbCmd.RemoveSlots))]
public static class OrbRemoveCapacityPatch
{
    [HarmonyPrefix]
    private static void OrbRemoveCapacity(Player player, ref int amount, out bool __state)
    {
        amount = DisplayOrbManager.PrepareToRemoveSlots(player, amount, out __state);
    }

    [HarmonyPostfix]
    private static void OrbRemoveCapacityFinalize(Player player, bool __state)
    {
        if (__state)
        {
            DisplayOrbManager.RefreshAllOrbs(player);
        }
    }
}

[HarmonyPatch(typeof(OrbCmd), nameof(OrbCmd.EvokeNext))]
public static class OrbEvokeNextPatch
{
    [HarmonyPrefix]
    private static void OrbEvokeNext(Player player, bool dequeue, out bool __state)
    {
        DisplayOrbManager.PrepareToEvokeNext(player, out __state);
        __state &= dequeue;
    }

    [HarmonyPostfix]
    private static void OrbEvokeNextFinalize(Player player, bool __state)
    {
        if (__state)
        {
            OrbCmd.RemoveSlots(player, 1);
            DisplayOrbManager.RefreshAllOrbs(player);
        }
    }
}

[HarmonyPatch(typeof(OrbCmd), nameof(OrbCmd.EvokeLast))]
public static class OrbEvokeLastPatch
{
    [HarmonyPrefix]
    private static void OrbEvokeLast(Player player, out bool __state)
    {
        DisplayOrbManager.PrepareToEvokeLast(player, out __state);
    }

    [HarmonyPostfix]
    private static void OrbEvokeLastFinalize(Player player, bool __state)
    {
        if (__state)
        {
            OrbCmd.RemoveSlots(player, 1);
            DisplayOrbManager.RefreshAllOrbs(player);
        }
    }
}