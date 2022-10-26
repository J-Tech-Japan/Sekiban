namespace Sekiban.Core.Setting;

public class SingleAggregateSetting
{
    public SingleAggregateSetting() { }

    public SingleAggregateSetting(
        string aggregateClassName,
        bool? makeSnapshots,
        bool? useHybrid,
        bool? useUpdateMarker = null,
        int? snapshotFrequency = null,
        int? snapshotOffset = null)
    {
        AggregateClassName = aggregateClassName;
        UseHybrid = useHybrid;
        MakeSnapshots = makeSnapshots;
        SnapshotFrequency = snapshotFrequency;
        SnapshotOffset = snapshotOffset;
        UseUpdateMarker = useUpdateMarker;
    }
    public string AggregateClassName { get; init; } = string.Empty;
    public bool? MakeSnapshots { get; init; }
    public bool? UseHybrid { get; init; }
    public int? SnapshotFrequency { get; init; }
    public int? SnapshotOffset { get; init; }
    public bool? UseUpdateMarker { get; set; }
}
