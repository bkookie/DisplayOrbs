namespace DisplayOrbs.DisplayOrbsCode.Orbs;

public interface IDisplayOrbGenerator<T> : IDisplayOrbGenerator where T : DisplayOrbModel
{

}

public interface IDisplayOrbGenerator
{
    /// <summary>
    /// Does not require any special logic (Just keep as auto-property). Used by <see cref="DisplayOrbManager"/>.
    /// </summary>
    public bool IsRefreshing { get; set; }

    /// <summary>
    /// Whether this should get deregistered. Checked after refreshing orbs.
    /// </summary>
    public bool ShouldDeregister { get; }

    /// <summary>
    /// Used by <see cref="DisplayOrbManager"/> when refreshing, to channel or evoke as necessary to maintain this many orbs. If there are not enough available orb slots, as many as possible will be channelled.
    /// </summary>
    public int PreferredNumberOfOrbs { get; }
}
