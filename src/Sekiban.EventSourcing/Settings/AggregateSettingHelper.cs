namespace Sekiban.EventSourcing.Settings;

public class AggregateSettingHelper
{
    public bool UseHybridDefault { get; }
    public bool TakeSnapshotDefault { get; }
    public int SnapshotFrequencyDefault { get; }
    public int SnapshotOffsetDefault { get; }
    public bool UseUpdateMarker { get; }
    public IEnumerable<SingleAggregateSetting> Exceptions { get; } = new List<SingleAggregateSetting>();

    public AggregateSettingHelper()
    {
        UseHybridDefault = false;
        TakeSnapshotDefault = true;
        SnapshotFrequencyDefault = 80;
        SnapshotOffsetDefault = 15;
        UseUpdateMarker = false;
    }

    public AggregateSettingHelper(
        bool takeSnapshotDefault,
        bool useHybridDefault,
        int snapshotFrequencyDefault,
        int snapshotOffsetDefault,
        bool useUpdateMarker,
        IEnumerable<SingleAggregateSetting> exceptions)
    {
        TakeSnapshotDefault = takeSnapshotDefault;
        UseHybridDefault = useHybridDefault;
        SnapshotFrequencyDefault = snapshotFrequencyDefault;
        SnapshotOffsetDefault = snapshotOffsetDefault;
        UseUpdateMarker = useUpdateMarker;
        Exceptions = exceptions;
    }
}
