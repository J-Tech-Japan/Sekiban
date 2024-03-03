using Microsoft.Extensions.Configuration;
namespace Sekiban.Core.Setting;

/// <summary>
///     Aggregate Setting Helper.
///     Default setting can be set by this class
/// </summary>
public class AggregateSettingHelper
{
    private const string AggregatesSection = "Aggregates";
    public bool UseHybridDefault { get; }
    public bool TakeSnapshotDefault { get; }
    public int SnapshotFrequencyDefault { get; }
    public int SnapshotOffsetDefault { get; }
    public bool UseUpdateMarker { get; }
    public IEnumerable<AggregateSetting> Exceptions { get; } = Enumerable.Empty<AggregateSetting>();
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
        IEnumerable<AggregateSetting> exceptions)
    {
        TakeSnapshotDefault = takeSnapshotDefault;
        UseHybridDefault = useHybridDefault;
        SnapshotFrequencyDefault = snapshotFrequencyDefault;
        SnapshotOffsetDefault = snapshotOffsetDefault;
        UseUpdateMarker = useUpdateMarker;
        Exceptions = exceptions;
    }
    public static AggregateSettingHelper FromConfigurationSection(IConfigurationSection section)
    {
        section = section.GetSection(AggregatesSection);
        var useHybridDefault = section.GetValue<bool?>("UseHybridDefault") ?? false;
        var takeSnapshotDefault = section.GetValue<bool?>("TakeSnapshotDefault") ?? true;
        var snapshotFrequencyDefault = section.GetValue<int?>("SnapshotFrequencyDefault") ?? 80;
        var snapshotOffsetDefault = section.GetValue<int?>("SnapshotOffsetDefault") ?? 15;
        var useUpdateMarker = section.GetValue<bool?>("UseUpdateMarker") ?? false;
        var exceptions = section.GetSection("SingleAggregateExceptions").Get<List<AggregateSetting>>() ?? [];
        return new AggregateSettingHelper(
            takeSnapshotDefault,
            useHybridDefault,
            snapshotFrequencyDefault,
            snapshotOffsetDefault,
            useUpdateMarker,
            exceptions);
    }
}
