using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Orbs;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace DisplayOrbs.DisplayOrbsCode.Orbs;

public static class DisplayOrbManager
{
    public const int DefaultMaxOrbCapacity = 10;

    private static readonly Logger logger = new Logger(MainFile.ModId, LogType.Generic);

    static DisplayOrbManager()
    {
        // Mods should use CombatManager.Instance.CombatStarted to call SetMaxDisplayOrbSlots()
        RunManager.Instance.RunStarted += _ => Reset();
        CombatManager.Instance.CombatEnded += _ => Reset();
    }

    private static readonly Dictionary<Player, int> MaxDisplayOrbSlots = [];
    private static readonly Dictionary<(Player player, IDisplayOrbGenerator orbGenerator), MethodInfo> OrbGenerators = [];
    //private static readonly OrderedTaskQueue taskQueue = new OrderedTaskQueue();

    private static void Reset()
    {
        MaxDisplayOrbSlots.Clear();
        OrbGenerators.Clear();
    }

    /// <summary>
    /// Set the maximun number of DisplayOrb slots for a player (min 10). Does not affect real orbs.
    /// </summary>
    /// <remarks>Must be applied each combat.</remarks>
    /// <param name="player">The player to set for.</param>
    /// <param name="maxOrbSlots">The maximun number of orb slots that can be active.</param>
    public static void SetMaxDisplayOrbSlots(Player player, int maxOrbSlots)
    {
        MaxDisplayOrbSlots[player] = Math.Max(10, maxOrbSlots);
    }

    private static int MaxDisplayOrbSlotsByPlayer(Player player)
    {
        if (MaxDisplayOrbSlots.TryGetValue(player, out int value))
        {
            return value;
        }
        else
        {
            return DefaultMaxOrbCapacity;
        }
    }

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

        bool needRefresh = true;

        try
        {
            while (needRefresh) // If any orbs were removed, check all generators again, as there is more room to channel them now.
            {
                needRefresh = false;

                foreach (var kvp in OrbGenerators.ToArray())
                {
                    IDisplayOrbGenerator orbGen = kvp.Key.orbGenerator;

                    if (kvp.Key.player == player)
                    {
                        //_ = taskQueue.EnqueueAsync(() => (Task)kvp.Value.Invoke(null, [choiceContext, kvp.Key.player, orbGen])!); // Calls the generic RefreshOrbs<T>
                        needRefresh = (bool)kvp.Value.Invoke(null, [choiceContext, kvp.Key.player, orbGen])!; // Calls the generic RefreshOrbs<T>
                    }

                    if (orbGen.ShouldDeregister)
                    {
                        OrbGenerators.Remove(kvp.Key);
                    }
                }
            }
        }
        finally
        {
            CheckOrbQueuesAreInSync(player);
        }
    }

    private static bool RefreshOrbs<T>(PlayerChoiceContext? choiceContext, Player? player, IDisplayOrbGenerator<T> orbGenerator) where T : DisplayOrbModel
    {
        OrbQueue? orbQueue = player?.PlayerCombatState?.OrbQueue;

        if (player == null || orbQueue == null)
            return false;

        int currentNumOrbs = orbQueue.Orbs.Count(orb => orb is T);
        int preferredNumOrbs = orbGenerator.PreferredNumberOfOrbs;

        bool orbEvoked = false;

        // Add or remove orbs to match power amount
        if (currentNumOrbs < preferredNumOrbs)
        {
            choiceContext ??= new BlockingPlayerChoiceContext();

            for (int i = currentNumOrbs; i < preferredNumOrbs && i < MaxDisplayOrbSlotsByPlayer(player); i++) // No point to channel more than 10 orbs
            {
                ChannelDisplayOrb<T>(choiceContext, player);
            }
        }
        else if (currentNumOrbs > preferredNumOrbs)
        {
            for (int i = 0; i < currentNumOrbs - preferredNumOrbs; i++)
            {
                orbEvoked |= EvokeDisplayOrb<T>(player, removeCapacity: true);
            }
        }

        return orbEvoked;
    }

    private static void CheckOrbQueuesAreInSync(Player player)
    {
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;
        NOrbManager? nOrbMan = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager;

        if (orbQueue != null && nOrbMan != null)
        {
            List<OrbModel> orbs = [.. orbQueue._orbs];
            List<OrbModel> orbs2 = [.. nOrbMan._orbs.Select(nOrb => nOrb.Model)];

            if (!Enumerable.SequenceEqual(orbs, orbs2))
            {
                logger.Error("Orb queues are out of sync.");
            }
        }
    }

    /// <summary>
    /// Channels a DisplayOrb. Does not process hooks or combat history.
    /// </summary>
    /// <typeparam name="T">The type of the orb.</typeparam>
    /// <param name="player">The player who is channeling the orb.</param>
    public static void ChannelDisplayOrb<T>(PlayerChoiceContext choiceContext, Player player) where T : DisplayOrbModel
    {
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;
        NOrbManager? nOrbMan = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager;

        if (CombatManager.Instance.IsOverOrEnding || orbQueue == null || nOrbMan == null || orbQueue.Capacity >= MaxDisplayOrbSlotsByPlayer(player))
            return;

        OrbModel newOrb = ModelDb.Orb<T>().ToMutable();
        newOrb.Owner = player;

        AddOrbSlots(orbQueue, nOrbMan, 1);

        orbQueue._orbs.Add(newOrb);
        nOrbMan.AddOrbAnim();

        if (newOrb.ChannelSfx != "")
        {
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
    internal static void PushRealOrbToFront(Player player, OrbModel orbToMove)
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
    /// <returns><see langword="true"/> if a real orb is in the last slot.</returns>
    internal static bool PushRealOrbToBack(Player player)
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
                // Find the last real orb. If it's already last in queue, no action necessary
                if (i == orbQueue.Orbs.Count - 1)
                {
                    return true;
                }
                else
                {
                    orbToMove = orbQueue.Orbs[i];
                    moveFromIndex = i;
                }
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

    private static void AddOrbSlots(Player player, int amount)
    {
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;
        NOrbManager? nOrbMan = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager;

        if (orbQueue != null && nOrbMan != null)
        {
            AddOrbSlots(orbQueue, nOrbMan, amount);
        }
    }

    private static void AddOrbSlots(OrbQueue orbQueue, NOrbManager nOrbMan, int amount)
    {
        if (amount > 0)
        {
            orbQueue.AddCapacity(amount);
            nOrbMan.AddSlotAnim(amount);
        }
    }

    private static void RemoveOrbSlots(Player player, int amount)
    {
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;
        NOrbManager? nOrbMan = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager;

        if (orbQueue != null && nOrbMan != null)
        {
            RemoveOrbSlots(orbQueue, nOrbMan, amount);
        }
    }

    private static void RemoveOrbSlots(OrbQueue orbQueue, NOrbManager nOrbMan, int amount)
    {
        if (amount > 0)
        {
            orbQueue.RemoveCapacity(amount);
            nOrbMan.RemoveSlotAnim(amount);
        }
    }

    /// <summary>
    /// Remove DisplayOrbs to make room for real orbs if necessary, then manually adds the orb slots (avoiding OrbCmd.AddSlots)
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if OrbCmd.AddSlots should still be executed, otherwise <see langword="false"/> to skip it.
    /// </returns>
    internal static bool PrepareToAddSlots(Player player, int amount)
    {
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;

        if (orbQueue == null)
            return true;

        // Even adding 0 slots through OrbCmd.AddSlots may result in it actually removing slots isntead, when you are over the default orb limit
        // So dont let OrbCmd try add any slots.

        int numDisplayOrbs = orbQueue.Orbs.Count(orb => orb is DisplayOrbModel);
        int realOrbCapacity = orbQueue.Capacity - numDisplayOrbs;


        int overCap = orbQueue.Capacity + amount - MaxDisplayOrbSlotsByPlayer(player);
        int overDefaultCap = realOrbCapacity + amount - DefaultMaxOrbCapacity;
        int availableDefaultCap = DefaultMaxOrbCapacity - realOrbCapacity;

        if (availableDefaultCap > 0)
        {
            int numDisplayOrbsRemoved = 0;

            for (int i = orbQueue.Orbs.Count - 1; i >= 0 && overCap > 0 && availableDefaultCap > 0; i--, overCap--, availableDefaultCap--)
            {
                if (orbQueue.Orbs[i] is not DisplayOrbModel)
                    break;

                if (EvokeDisplayOrb<DisplayOrbModel>(player, removeCapacity: false))
                    numDisplayOrbsRemoved++;
            }

            int slotsToAdd = Math.Min(availableDefaultCap, amount - numDisplayOrbsRemoved);
            AddOrbSlots(player, slotsToAdd);
        }

        return false;
    }

    /// <summary>
    /// Moves real orbs to the back if necessary, so they will be removed along with the removed slots, then manually removes the orb slots (avoiding OrbCmd.RemoveSlots)
    /// </summary>
    /// <returns>
    /// <see langword="true"/> if OrbCmd.RemoveSlots should still be executed, otherwise <see langword="false"/> to skip it.
    /// </returns>
    internal static bool PrepareToRemoveSlots(Player player, int amount)
    {
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;
        NOrbManager? nOrbMan = NCombatRoom.Instance?.GetCreatureNode(player.Creature)?.OrbManager;

        if (orbQueue == null || nOrbMan == null)
            return true;

        int numEmptySlotsToRemove = Math.Min(amount, orbQueue.Capacity - orbQueue.Orbs.Count);
        int numRemainingSlotsToRemove = amount - numEmptySlotsToRemove;

        RemoveOrbSlots(player, numEmptySlotsToRemove); // Remove any empty slots first

        for (int i = 0; i < numRemainingSlotsToRemove; i++)
        {
            if (PushRealOrbToBack(player))
            {
                RemoveOrbSlots(orbQueue, nOrbMan, 1);
            }
            else
            {
                break; // No more real orbs (or empty slots), removing slots has not further effect
            }
        }

        RefreshAllOrbs(player);

        return false;
    }

    /// <summary>
    /// Adds the first real orb slot, if there are none.
    /// </summary>
    /// <param name="player">The player who is channelling.</param>
    internal static void PrepareToChannel(Player player)
    {
        OrbQueue? orbQueue = player.PlayerCombatState?.OrbQueue;

        if (orbQueue != null && orbQueue.Orbs.Count == orbQueue.Capacity && orbQueue.Orbs.All(o => o is DisplayOrbModel))
        {
            // When all orb slots are full and are DisplayOrbs, this is same as having no orbs slots at all, and need to add the first orb slot
            AddOrbSlots(player, 1);
        }
    }

    /// <summary>
    /// Checks what kind of orb will be evoked (either a Display orb or a real orb).
    /// </summary>
    /// <param name="player">The player who is performing the evoke.</param>
    /// <param name="removeSlotRequired">Set to <see langword="true"/> if the evoked orb will be a DisplayOrb, such that it's slot should also be removed.</param>
    internal static void PrepareToEvokeNext(Player player, out bool removeSlotRequired)
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
    internal static void PrepareToEvokeLast(Player player, out bool removeSlotRequired)
    {
        // If no real orbs are there, a DisplayOrb will get evoked (but will refresh immediately)
        removeSlotRequired = !PushRealOrbToBack(player);
    }
}