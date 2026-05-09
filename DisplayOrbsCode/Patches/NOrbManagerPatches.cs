//using System.Reflection;
//using System.Reflection.Emit;
//using HarmonyLib;
//using MegaCrit.Sts2.Core.Nodes.Orbs;

//[HarmonyPatch(typeof(NOrbManager), nameof(NOrbManager.ReplaceOrb))]
//public static class ReplaceOrbPatch
//{
//    [HarmonyTranspiler]
//    private static IEnumerable<CodeInstruction> ReplaceOrb(IEnumerable<CodeInstruction> instructions)
//    {
//        // Modify NOrb.ReplaceOrb to only replace the first instance of an orb, rather than all instances.

//        MethodInfo referenceMethod = AccessTools.Method(typeof(NOrb), nameof(NOrb.ReplaceOrb));

//        List<CodeInstruction> codes = [.. instructions];

//        for (int i = 0; i < codes.Count; i++)
//        {
//            if (codes[i].Calls(referenceMethod))
//            {
//                codes.Insert(++i, new CodeInstruction(OpCodes.Ret));
//            }
//        }

//        return codes;
//    }
//}