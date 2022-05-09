namespace Sekiban.EventSourcing.Settings;

public class AggregateSettingHelper
{
    public bool UseHybridDefault { get; }
    public bool TakeSnapshotDefault { get; }
    public int SnapshotFrequencyDefault { get; }
    public int SnapshotOffsetDefault { get; }

    public IEnumerable<SingleAggregateSetting> SingleAggregateSettings { get; }
    public IEnumerable<SingleAggregateProjectionSetting> SingleAggregateProjectionSettings { get; }

    public AggregateSettingHelper(
        bool takeSnapshotDefault,
        bool useHybridDefault,
        int snapshotFrequencyDefault,
        int snapshotOffsetDefault,
        IEnumerable<SingleAggregateSetting> singleAggregateSettings,
        IEnumerable<SingleAggregateProjectionSetting> singleAggregateProjectionSettings)
    {
        TakeSnapshotDefault = takeSnapshotDefault;
        UseHybridDefault = useHybridDefault;
        SnapshotFrequencyDefault = snapshotFrequencyDefault;
        SnapshotOffsetDefault = snapshotOffsetDefault;
        SingleAggregateSettings = singleAggregateSettings;
        SingleAggregateProjectionSettings = singleAggregateProjectionSettings;
    }
}
