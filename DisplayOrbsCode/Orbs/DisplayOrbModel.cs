using BaseLib.Abstracts;
using MegaCrit.Sts2.Core.Models;

namespace DisplayOrbs.DisplayOrbsCode.Orbs;

public abstract class DisplayOrbModel : CustomOrbModel
{
    public override int ModifyOrbPassiveTriggerCounts(OrbModel orb, int triggerCount)
    {
        // Prevent game 'hanging' at end of turn while all DisplayOrbs try to trigger their passive.
        if (orb == this)
            return 0;

        return triggerCount;
    }
}
