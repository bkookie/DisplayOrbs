using DisplayOrbs.DisplayOrbsCode.Orbs;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Models;

namespace DisplayOrbs.DisplayOrbsCode.Patches;

[HarmonyPatch(typeof(Hook), nameof(Hook.AfterOrbChanneled))]
public static class AfterOrbChannelledPatch
{
    [HarmonyPrefix]
    private static void AfterOrbChannelled(Player player, OrbModel orb)
    {
        if (orb is not DisplayOrbModel)
        {
            DisplayOrbManager.PushRealOrbToFront(player, orb);
        }
    }
}