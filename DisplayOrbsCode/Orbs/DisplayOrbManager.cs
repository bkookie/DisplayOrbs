using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace DisplayOrbs.DisplayOrbsCode.Orbs;

public static class DisplayOrbManager
{
    public const int MaxOrbCapacity = 10;

    static DisplayOrbManager()
    {
        RunManager.Instance.RunStarted += _ => OrbGenerators.Clear();
        RunManager.Instance.RoomExited += OrbGenerators.Clear;
    }

    private static readonly Dictionary<(Player player, IDisplayOrbGenerator orbGenerator), MethodInfo> OrbGenerators = [];
    private static readonly OrderedTaskQueue  taskQueue = new OrderedTaskQueue();

    /// <summary>
    /// Registers an <see cref="IDisplayOrbGenerator{T}"/> for the supplied <paramref name="player"/>, so it's DisplayOrbs can be refreshed automatically.
    /// </summary>
    /// <typeparam name="T">The type of DisplayOrb</typeparam>
    /// <param name="player">The player that the <paramref name="orbGenerator"/> belongs to.</param>
    /// <param name="orbGenerator">The OrbGenerator.</param>
    public static void Register<T>(Player player, IDisplayOrbGenerator<T> orbGenerator) where T : DisplayOrbModel
    {
        (Player, IDisplayOrbGenerator) tuple = (player, orbGenerator);

        if (OrbGenerators.ContainsKey(tuple))
            return;

        // Seems like an awkward approach, but allows putting all the logic in the manager class
        // Need to use reflection, because we dont know the generic type of the OrbGenerator

        Type type = orbGenerator.GetType();
        Type? interfaceType = type.GetInterfaces().FirstOrDefault(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IDisplayOrbGenerator<>));
        Type? genericArgument = interfaceType?.GetGenericArguments()[0];

        if (genericArgument == null)
            throw new InvalidOperationException($"{nameof(orbGenerator)} must be of generic type {typeof(IDisplayOrbGenerator<>)}");

        MethodInfo? method = AccessTools.Method(typeof(DisplayOrbManager), nameof(RefreshOrbs));
        MethodInfo? genericMethod = method?.MakeGenericMethod(genericArgument);

        if (genericMethod == null)
            throw new MissingMethodException(nameof(DisplayOrbManager), nameof(RefreshOrbs));

        OrbGenerators.Add(tuple, genericMethod);
    }

    /// <summary>
    /// Removes an <see cref="IDisplayOrbGenerator{T}"/> from the rgistser, so it no longer tries to automatically refresh.
    /// </summary>
    /// <param name="player">The player that the <paramref name="orbGenerator"/> belongs to.</param>
    /// <param name="orbGenerator">The OrbGenerator.</param>
    public static void Deregister(Player player, IDisplayOrbGenerator orbGenerator)
    {
        OrbGenerators.Remove((player, orbGenerator));
    }

    /// <inheritdoc cref="RefreshAllOrbs(PlayerChoiceContext?, Player)"/>
    public static void RefreshAllOrbs(Player player)
    {
        RefreshAllOrbs(null, player);
    }

    /// <summary>
    /// Refreshes all registered DisplayOrbs for the supplied <paramref name="player"/>.
    /// </summary>
    /// <param name="player">The player to refresh DisplayOrbs for.</param>
    public static void RefreshAllOrbs(PlayerChoiceContext? choiceContext, Player player)
    {
        if (CombatManager.Instance.IsOverOrEnding)
            return;

        foreach (var kvp in OrbGenerators.ToArray())
        {
            IDisplayOrbGenerator orbGen = kvp.Key.orbGenerator;

            if (kvp.Key.player == player)
            {
                _ = taskQueue.EnqueueAsync(() => (Task)kvp.Value.Invoke(null, [choiceContext, kvp.Key.player, orbGen])!); // Calls the generic TryChannelPreferredNumberOfDisplayOrbs<T>
            }

            if (orbGen.ShouldDeregister)
            {
                OrbGenerators.Remove(kvp.Key);
            }
        }
    }

    private static async Task RefreshOrbs<T>(PlayerChoiceContext? choiceContext, Player? player, IDisplayOrbGenerator<T> orbGenerator) where T : DisplayOrbModel
    {
        OrbQueue? orbQueue = player?.PlayerCombatState?.OrbQueue;

        if (player == null || orbQueue == null)
            return;

        int currentNumOrbs = orbQueue.Orbs.Count(orb => orb is T);
        int preferredNumOrbs = orbGenerator.PreferredNumberOfOrbs;

        // Add or remove orbs to match power amount
        if (currentNumOrbs < preferredNumOrbs)
        {
            choiceContext ??= new BlockingPlayerChoiceContext();

            for (int i = currentNumOrbs; i < preferredNumOrbs && i < MaxOrbCapacity; i++) // No point to channel more than 10 orbs
            {
                await ChannelDisplayOrb<T>(choiceContext, player);
            }
        }
        else if (currentNumOrbs > preferredNumOrbs)
        {
            for (int i = 0; i < currentNumOrbs - preferredNumOrbs; i++)
            {
                EvokeDisplayOrb<T>(player, removeCapacity: true);
            }
        }
    }

    /// <summary>
    /// Channels a DisplayOrb. Does not process hooks or combat history.
    /// </summary>
    /// <typeparam name="T">The type of the orb.</typeparam>
    /// <param name="player">The player who is channeling the orb.</param>
    public static async Task ChannelDisplayOrb<T>(PlayerChoiceContext choiceContext, Player player) where T : DisplayOrbModel
    {
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;
        NOrbManager? nOrbMan = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager;

        if (CombatManager.Instance.IsOverOrEnding || orbQueue == null || nOrbMan == null || orbQueue.Capacity >= MaxOrbCapacity)
            return;

        OrbModel newOrb = ModelDb.Orb<T>().ToMutable();
        newOrb.Owner = player;

        await OrbCmd.AddSlots(player, 1);

        if (await orbQueue.TryEnqueue(newOrb) && newOrb.ChannelSfx != "")
        {
            nOrbMan.AddOrbAnim();
            newOrb.PlayChannelSfx();
        }
    }

    /// <summary>
    /// Evokes a DisplayOrb. Does not process hooks.
    /// </summary>
    /// <typeparam name="T">The type of the orb.</typeparam>
    /// <param name="player">The player who's orb you are trying to evoke.</param>
    /// <param name="removeCapacity">If <see langword="true"/>, also removes an orb slot.</param>
    /// <returns>Returns <see langword="true"/> if an orb was evoked.</returns>
    public static bool EvokeDisplayOrb<T>(Player player, bool removeCapacity) where T : DisplayOrbModel
    {
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;
        NOrbManager? nOrbMan = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager;

        if (CombatManager.Instance.IsOverOrEnding || orbQueue == null || nOrbMan == null)
            return false;

        int removedIndex = orbQueue.Orbs.FirstIndex(orb => orb is T);

        if (removedIndex >= 0)
        {
            OrbModel removedOrb = orbQueue._orbs[removedIndex];

            orbQueue.Remove(removedOrb);
            nOrbMan.EvokeOrbAnim(removedOrb);

            removedOrb.RemoveInternal();

            if (removeCapacity)
            {
                orbQueue.RemoveCapacity(1);
                nOrbMan.RemoveSlotAnim(1); // Must be performed after orbQueue.RemoveCapacity
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Moves a real orb that was just channeled to be placed in front of all DisplayOrbs.
    /// </summary>
    /// <param name="player">The player who's orb you are trying to move.</param>
    /// <param name="orbToMove">The orb to move.</param>
    public static void PushRealOrbToFront(Player player, OrbModel orbToMove)
    {
        if (CombatManager.Instance.IsOverOrEnding)
            return;

        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;
        NOrbManager? nOrbMan = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager;

        if (orbToMove is not DisplayOrbModel && orbQueue != null && nOrbMan != null)
        {
            int moveFromIndex = orbQueue.Orbs.IndexOf(orbToMove);
            int moveToIndex = orbQueue.Orbs.FirstIndex(orb => orb is DisplayOrbModel);

            if (moveToIndex >= 0 && moveFromIndex > moveToIndex)
            {
                // Shuffle OrbModels and NOrbs

                List<OrbModel> orbs = orbQueue._orbs;
                List<NOrb> nOrbs = nOrbMan._orbs;
                NOrb nOrbToMove = nOrbs[moveFromIndex];

                for (int i = moveFromIndex; i > moveToIndex; i--)
                {
                    orbs[i] = orbs[i - 1];
                    nOrbs[i] = nOrbs[i - 1];
                }
                orbs[moveToIndex] = orbToMove;
                nOrbs[moveToIndex] = nOrbToMove;

                nOrbMan.TweenLayout();
                nOrbMan.UpdateControllerNavigation();
            }
        }
    }

    /// <summary>
    /// Moves the back-most real orb all the way to the back, so it can be removed along with a slot.
    /// </summary>
    /// <param name="player">The player who's orb you are trying to move.</param>
    /// <returns><see langword="true"/> if an orb was moved.</returns>
    public static bool PushRealOrbToBack(Player player)
    {
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;
        NOrbManager? nOrbMan = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager;

        if (CombatManager.Instance.IsOverOrEnding || orbQueue == null || nOrbMan == null)
            return false;

        OrbModel? orbToMove = null;
        OrbModel? orbToDisplace = orbQueue.Orbs.LastOrDefault(orb => orb is DisplayOrbModel);

        if (orbToDisplace == null)
            return false;

        int moveFromIndex = -1;
        int moveToIndex = orbQueue.Orbs.IndexOf(orbToDisplace);

        for (int i = orbQueue.Orbs.Count - 1; i >= 0; i--)
        {
            if (orbQueue.Orbs[i] is not DisplayOrbModel)
            {
                // Find the last real orb
                orbToMove = orbQueue.Orbs[i];
                moveFromIndex = i;
                break;
            }
        }

        if (orbToMove == null)
            return false;

        // Shuffle OrbModels and NOrbs

        List<OrbModel> orbs = orbQueue._orbs;
        List<NOrb> nOrbs = nOrbMan._orbs;
        NOrb nOrbToMove = nOrbs[moveFromIndex];

        for (int i = moveFromIndex; i < moveToIndex; i++)
        {
            orbs[i] = orbs[i + 1];
            nOrbs[i] = nOrbs[i + 1];
        }
        orbs[moveToIndex] = orbToMove;
        nOrbs[moveToIndex] = nOrbToMove;

        nOrbMan.TweenLayout();
        nOrbMan.UpdateControllerNavigation();

        return true;
    }

    /// <summary>
    /// Remove DisplayOrbs to make room for real orbs.
    /// </summary>
    /// <returns>
    /// The number of slots that are still required to be added, after removing any DisplayOrbs.
    /// </returns>
    public static int PrepareToAddSlots(Player player, int amount)
    {
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;

        if (orbQueue == null)
            return amount;

        int numDisplayOrbsRemoved = 0;

        int i = orbQueue.Orbs.Count - 1;
        int overCap = orbQueue.Capacity + amount - MaxOrbCapacity;
        for (; i >= 0 && overCap > 0; i--, overCap--)
        {
            if (orbQueue.Orbs[i] is not DisplayOrbModel)
                break;

            if (EvokeDisplayOrb<DisplayOrbModel>(player, removeCapacity: false))
                numDisplayOrbsRemoved++;
        }

        return amount - numDisplayOrbsRemoved;
    }

    /// <summary>
    /// Moves real orbs to the back if necessary, so they will be removed along with the removed slots.
    /// </summary>
    /// <returns>
    /// The number of modified number of slots that should be removed, considering that DisplayOrbs and their slots should not be removed.
    /// </returns>
    public static int PrepareToRemoveSlots(Player player, int amount, out bool refreshNeeded)
    {
        refreshNeeded = false;
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;

        if (orbQueue == null)
            return amount;

        if (orbQueue.Capacity - orbQueue.Orbs.Count >= amount)
        {
            refreshNeeded = true;
            return amount;
        }

        for (int i = 0; i < amount; i++)
        {
            refreshNeeded |= PushRealOrbToBack(player);
        }

        return Math.Min(amount, orbQueue.Orbs.Count(orb => orb is not DisplayOrbModel));
    }

    /// <summary>
    /// Checks what kind of orb will be evoked (either a Display orb or a real orb).
    /// </summary>
    /// <param name="player">The player who is performing the evoke.</param>
    /// <param name="removeSlotRequired">Set to <see langword="true"/> if the evoked orb will be a DisplayOrb, such that it's slot should also be removed.</param>
    public static void PrepareToEvokeNext(Player player, out bool removeSlotRequired)
    {
        // If no real orbs are there, a DisplayOrb will get evoked (but will refresh immediately)
        IReadOnlyList<OrbModel>? orbs = player.PlayerCombatState?.OrbQueue.Orbs;
        removeSlotRequired = orbs?.Count > 0 && orbs[0] is DisplayOrbModel;
    }

    /// <summary>
    /// Moves the last real orb to the back if necessary, so it can be evoked.
    /// </summary>
    /// <remarks>The are currently no hooks to prevent an orb from evoking, so this may evoke a DisplayOrb.</remarks>
    /// <param name="player">The player who is performing the evoke.</param>
    /// <param name="removeSlotRequired">Set to <see langword="true"/> if the evoked orb will be a DisplayOrb, such that it's slot should also be removed.</param>
    public static void PrepareToEvokeLast(Player player, out bool removeSlotRequired)
    {
        // If no real orbs are there, a DisplayOrb will get evoked (but will refresh immediately)
        removeSlotRequired = !PushRealOrbToBack(player);
    }
}