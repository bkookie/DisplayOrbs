using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models;

namespace DisplayOrbs.DisplayOrbsCode.Orbs;

/// <summary>
/// Base class for all Display Orbs
/// </summary>
public abstract class DisplayOrbModel : CustomOrbModel
{
    /// <returns><c>0</c> for itself (prevents ever triggering it's own passive).</returns>
    /// <inheritdoc cref="AbstractModel.ModifyOrbPassiveTriggerCounts(OrbModel, int)"/>
    public override int ModifyOrbPassiveTriggerCounts(OrbModel orb, int triggerCount)
    {
        // Prevent game 'hanging' at end of turn while all DisplayOrbs try to trigger their passive.
        if (orb == this)
            return 0;

        return triggerCount;
    }
}
