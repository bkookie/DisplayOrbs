using System.Threading.Tasks;
using DisplayOrbs.DisplayOrbsCode.Orbs;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;

namespace DisplayOrbs.DisplayOrbsCode.Patches;

[HarmonyPatch(typeof(OrbCmd), nameof(OrbCmd.AddSlots))]
public static class OrbAddCapacityPatch
{
    [HarmonyPrefix]
    private static bool OrbAddCapacity(Player player, int amount, ref Task __result)
    {
        if (DisplayOrbManager.PrepareToAddSlots(player, amount))
            return true;

        __result = Task.CompletedTask;
        return false;
    }
}

[HarmonyPatch(typeof(OrbCmd), nameof(OrbCmd.RemoveSlots))]
public static class OrbRemoveCapacityPatch
{
    [HarmonyPrefix]
    private static bool OrbRemoveCapacity(Player player, int amount)
    {
        return DisplayOrbManager.PrepareToRemoveSlots(player, amount);
    }
}

[HarmonyPatch(typeof(OrbCmd), nameof(OrbCmd.Channel), [typeof(PlayerChoiceContext), typeof(OrbModel), typeof(Player)])]
public static class OrbChannelPatch
{
    [HarmonyPrefix]
    private static void OrbChannel(OrbModel orb, Player player)
    {
        if (orb is not DisplayOrbModel)
        {
            DisplayOrbManager.PrepareToChannel(player);
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

////[HarmonyPatch(typeof(OrbCmd), nameof(OrbCmd.Channel), [typeof(PlayerChoiceContext), typeof(OrbModel), typeof(Player)])]
//[HarmonyPatch("<Channel>d__3", "MoveNext")]
//public static class ChannelPatch
//{
//    [HarmonyTranspiler]
//    private static IEnumerable<CodeInstruction> Channel(IEnumerable<CodeInstruction> instructions)
//    {
//        List<CodeInstruction> codes = [.. instructions];

//        MethodInfo referenceMethod = AccessTools.PropertyGetter(typeof(OrbQueue), nameof(OrbQueue.Capacity));
//        bool foundFirst = false; // Looking for second instance of OrbQueue.get_Capacity()

//        for (int i = 0; i < codes.Count; i++)
//        {
//            if (codes[i].Calls(referenceMethod))
//            {
//                if (!foundFirst)
//                {
//                    foundFirst = true;
//                }
//                else
//                {
//                    // Replace "if (orbQueue.Orbs.Count >= orbQueue.Capacity)" with "if (orbQueue.Orbs.Count >= 10)", since orbQueue.Capacity may be over 10
//                    codes.RemoveAt(i--);
//                    codes.RemoveAt(i);
//                    codes.Insert(i, new CodeInstruction(OpCodes.Ldc_I4, DisplayOrbManager.DefaultMaxOrbCapacity));
//                    break;
//                }
//            }
//        }

//        return codes;
//    }
//}
