namespace Sekiban.EventSourcing.Settings;

public class AggregateSettingHelper
{
    public bool UseHybridDefault { get; }
    public bool TakeSnapshotDefault { get; }
    public int SnapshotFrequencyDefault { get; }
    public int SnapshotOffsetDefault { get; }

    public IEnumerable<SingleAggregateSetting> Exceptions { get; } = new List<SingleAggregateSetting>();

    public AggregateSettingHelper()
    {
        UseHybridDefault = false;
        TakeSnapshotDefault = true;
        SnapshotFrequencyDefault = 80;
        SnapshotOffsetDefault = 15;
    }

    public AggregateSettingHelper(
        bool takeSnapshotDefault,
        bool useHybridDefault,
        int snapshotFrequencyDefault,
        int snapshotOffsetDefault,
        IEnumerable<SingleAggregateSetting> exceptions)
    {
        TakeSnapshotDefault = takeSnapshotDefault;
        UseHybridDefault = useHybridDefault;
        SnapshotFrequencyDefault = snapshotFrequencyDefault;
        SnapshotOffsetDefault = snapshotOffsetDefault;
        Exceptions = exceptions;
    }
}
