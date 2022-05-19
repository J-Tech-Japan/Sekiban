using Microsoft.Extensions.Configuration;
namespace Sekiban.EventSourcing.Settings;

public class ConfigurationAggregateSettings : AggregateSettings
{
    private const string SekibanSection = "Sekiban";
    private const string AggregatesSection = "Aggregates";

    public ConfigurationAggregateSettings(IConfiguration configuration, ISekibanContext sekibanContext)
    {
        var section = configuration?.GetSection(SekibanSection);
        if (!string.IsNullOrWhiteSpace(sekibanContext.SettingGroupIdentifier))
        {
            section = section?.GetSection(sekibanContext.SettingGroupIdentifier);
        }
        section = section?.GetSection(AggregatesSection);
        var useHybridDefault = section?.GetValue<bool?>("useHybridDefault") ?? false;
        var takeSnapshotDefault = section?.GetValue<bool?>("takeSnapshotDefault") ?? false;
        var snapshotFrequencyDefault = section?.GetValue<int?>("snapshotFrequencyDefault") ?? 80;
        var snapshotOffsetDefault = section?.GetValue<int?>("snapshotOffsetDefault") ?? 15;
        var exceptions = section?.GetSection("SingleAggregateExceptions").Get<List<SingleAggregateSetting>>() ?? new List<SingleAggregateSetting>();
        Helper = new AggregateSettingHelper(takeSnapshotDefault, useHybridDefault, snapshotFrequencyDefault, snapshotOffsetDefault, exceptions);
    }
}
