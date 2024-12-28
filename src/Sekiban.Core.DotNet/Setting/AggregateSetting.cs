namespace Sekiban.Core.Setting;

/// <summary>
///     Aggregate Setting.
/// </summary>
public class AggregateSetting
{
    /// <summary>
    ///     Aggregate Class Name this is used to match the aggregate class name.
    /// </summary>
    public string AggregateClassName { get; init; } = string.Empty;
    /// <summary>
    ///     Setting if this aggregate makes snapshots.
    /// </summary>
    public bool? MakeSnapshots { get; init; }
    /// <summary>
    ///     How often snapshot will be taken.
    /// </summary>
    public int? SnapshotFrequency { get; init; }
    /// <summary>
    ///     Snapshot offset (When events reach snapshot point, it still is unstable, so we need to wait for a while before
    ///     taking snapshot)
    /// </summary>
    public int? SnapshotOffset { get; init; }
    /// <summary>
    ///     Uses Update marker. Update marker works only with single server environment or multiple servers sending
    ///     updates to
    ///     the projection server.
    /// </summary>
    public bool? UseUpdateMarker { get; set; }
    public AggregateSetting()
    {
    }

    public AggregateSetting(
        string aggregateClassName,
        bool? makeSnapshots,
        bool? useUpdateMarker = null,
        int? snapshotFrequency = null,
        int? snapshotOffset = null)
    {
        AggregateClassName = aggregateClassName;
        MakeSnapshots = makeSnapshots;
        SnapshotFrequency = snapshotFrequency;
        SnapshotOffset = snapshotOffset;
        UseUpdateMarker = useUpdateMarker;
    }
}
