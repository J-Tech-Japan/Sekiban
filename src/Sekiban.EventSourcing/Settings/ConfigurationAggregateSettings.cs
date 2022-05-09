using Microsoft.Extensions.Configuration;
namespace Sekiban.EventSourcing.Settings;

public class ConfigurationAggregateSettings : AggregateSettings
{
    private readonly IConfiguration _configuration;

    public ConfigurationAggregateSettings(IConfiguration configuration)
    {
        _configuration = configuration;
        var useHybridDefault = _configuration.GetValue<bool?>("useHybridDefault") ?? false;
        var takeSnapshotDefault = _configuration.GetValue<bool?>("takeSnapshotDefault") ?? false;
        var snapshotFrequencyDefault = _configuration.GetValue<int?>("snapshotFrequencyDefault") ?? 80;
        var snapshotOffsetDefault = _configuration.GetValue<int?>("snapshotOffsetDefault") ?? 15;
        var exceptions = _configuration.GetSection("SingleAggregateExceptions").Get<List<SingleAggregateSetting>>();
        Helper = new AggregateSettingHelper(takeSnapshotDefault, useHybridDefault, snapshotFrequencyDefault, snapshotOffsetDefault, exceptions);
    }
}
