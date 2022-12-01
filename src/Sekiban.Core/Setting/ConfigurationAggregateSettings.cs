using Microsoft.Extensions.Configuration;

namespace Sekiban.Core.Setting;

public class ConfigurationAggregateSettings : AggregateSettings
{
    private const string SekibanSection = "Sekiban";
    private const string AggregatesSection = "Aggregates";

    public ConfigurationAggregateSettings(IConfiguration configuration, ISekibanContext sekibanContext)
    {
        var section = configuration?.GetSection(SekibanSection);
        if (!string.IsNullOrWhiteSpace(sekibanContext.SettingGroupIdentifier))
            section = section?.GetSection(sekibanContext.SettingGroupIdentifier);
        section = section?.GetSection(AggregatesSection);
        var useHybridDefault = section?.GetValue<bool?>("UseHybridDefault") ?? false;
        var takeSnapshotDefault = section?.GetValue<bool?>("TakeSnapshotDefault") ?? false;
        var snapshotFrequencyDefault = section?.GetValue<int?>("SnapshotFrequencyDefault") ?? 80;
        var snapshotOffsetDefault = section?.GetValue<int?>("SnapshotOffsetDefault") ?? 15;
        var useUpdateMarker = section?.GetValue<bool?>("UseUpdateMarker") ?? false;
        var exceptions = section?.GetSection("SingleAggregateExceptions").Get<List<AggregateSetting>>() ??
                         new List<AggregateSetting>();
        Helper = new AggregateSettingHelper(
            takeSnapshotDefault,
            useHybridDefault,
            snapshotFrequencyDefault,
            snapshotOffsetDefault,
            useUpdateMarker,
            exceptions);
    }
}
