using MegaCrit.Sts2.Core.Models;

namespace DisplayOrbs.DisplayOrbsCode.Orbs;

/// <summary>
/// Interface for any <see cref="AbstractModel"/> that wants to use Display Orbs.
/// </summary>
/// <typeparam name="T">The type of <see cref="DisplayOrbModel"/> to be generated.</typeparam>
public interface IDisplayOrbGenerator<T> : IDisplayOrbGenerator where T : DisplayOrbModel
{

}

/// <summary>
/// Non-generic interface for any <see cref="AbstractModel"/> that wants to use Display Orbs.
/// </summary>
/// <remarks>WARNING: The generic version of this <see langword="interface"/> interface should be used instead.</remarks>
public interface IDisplayOrbGenerator
{
    /// <summary>
    /// Whether this should get deregistered. Checked after refreshing orbs.
    /// </summary>
    public bool ShouldDeregister { get; }

    /// <summary>
    /// Used by <see cref="DisplayOrbManager"/> when refreshing, to channel or evoke as necessary to maintain this many orbs. If there are not enough available orb slots, as many as possible will be channelled.
    /// </summary>
    public int PreferredNumberOfOrbs { get; }
}
