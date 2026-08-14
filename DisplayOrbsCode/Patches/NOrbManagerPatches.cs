using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Orbs;

namespace DisplayOrbs.DisplayOrbsCode.Patches;

[HarmonyPatch(typeof(NOrbManager), nameof(NOrbManager.UpdateControllerNavigation))]
public static class UpdateControllerNavigationPatch
{
    [HarmonyPrefix]
    private static bool Prefix(NOrbManager __instance)
    {
        // Adding or evoking DisplayOrbs seems to happen off the main thread, which defers scene tree initialization of the node.
        // So we will abandon navigation if any NOrbs are not in the scene tree yet
        return __instance._orbs.All(nOrb => nOrb.IsInsideTree());
    }
}

//    [HarmonyTranspiler]
//    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator il)
//    {
//        // Adding or evoking DisplayOrbs seems to happen off the main thread, which defers scene tree initialization of the node.
//        // So we will abandon navigation if any NOrbs are not in the scene tree yet

//        // Why I wrote this whole transpiler when I could just use prefix???

//        FieldInfo nOrbMan_orbsField = AccessTools.Field(typeof(NOrbManager), nameof(NOrbManager._orbs));
//        MethodInfo list_nOrb_getIndexMethod = AccessTools.IndexerGetter(typeof(List<NOrb>), [typeof(int)]);
//        MethodInfo nOrb_isInsideTreeMethod = AccessTools.Method(typeof(NOrb), nameof(NOrb.IsInsideTree));
//        MethodInfo list_nOrb_getCountProperty = AccessTools.PropertyGetter(typeof(List<NOrb>), nameof(List<NOrb>.Count));

//        Label conditionLabel = il.DefineLabel();
//        Label insideLoopLabel = il.DefineLabel();
//        Label incrementLabel = il.DefineLabel();

//        List<CodeInstruction> codes = [.. instructions];

//        int i = 0; // Start inserting at the very beginning

//        //     for (int i = 0; i < _orbs.Count; i++)
//        //          ^^^^^^^^^
//        codes.Insert(i++, new CodeInstruction(OpCodes.Ldc_I4_0));
//        codes.Insert(i++, new CodeInstruction(OpCodes.Stloc_0));
//        codes.Insert(i++, new CodeInstruction(OpCodes.Br_S, conditionLabel)); // --> conditionLabel

//        //         if (!_orbs[i].IsInsideTree())
//        codes.Insert(i++, new CodeInstruction(OpCodes.Ldarg_0)); // insideLoopLabel
//        codes[i - 1].labels.Add(insideLoopLabel);
//        codes.Insert(i++, new CodeInstruction(OpCodes.Ldfld, nOrbMan_orbsField));
//        codes.Insert(i++, new CodeInstruction(OpCodes.Ldloc_0));
//        codes.Insert(i++, new CodeInstruction(OpCodes.Callvirt, list_nOrb_getIndexMethod));
//        codes.Insert(i++, new CodeInstruction(OpCodes.Callvirt, nOrb_isInsideTreeMethod));
//        codes.Insert(i++, new CodeInstruction(OpCodes.Brtrue, incrementLabel)); // --> incrementLabel

//        //             return;
//        codes.Insert(i++, new CodeInstruction(OpCodes.Ret));

//        //     for (int i = 0; i < _orbs.Count; i++)
//        //                                      ^^^
//        codes.Insert(i++, new CodeInstruction(OpCodes.Ldloc_0)); // incrementLabel
//        codes[i - 1].labels.Add(incrementLabel);
//        codes.Insert(i++, new CodeInstruction(OpCodes.Ldc_I4_1));
//        codes.Insert(i++, new CodeInstruction(OpCodes.Add));
//        codes.Insert(i++, new CodeInstruction(OpCodes.Stloc_0));

//        //     for (int i = 0; i < _orbs.Count; i++)
//        //                     ^^^^^^^^^^^^^^^
//        codes.Insert(i++, new CodeInstruction(OpCodes.Ldloc_0)); // conditionLabel
//        codes[i - 1].labels.Add(conditionLabel);
//        codes.Insert(i++, new CodeInstruction(OpCodes.Ldarg_0));
//        codes.Insert(i++, new CodeInstruction(OpCodes.Ldfld, nOrbMan_orbsField));
//        codes.Insert(i++, new CodeInstruction(OpCodes.Callvirt, list_nOrb_getCountProperty));
//        codes.Insert(i++, new CodeInstruction(OpCodes.Blt_S, insideLoopLabel)); // --> insideLoopLabel

//        return codes;
//    }
//}

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