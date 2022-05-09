using Microsoft.Extensions.Configuration;
namespace Sekiban.EventSourcing.Settings;

public class AggregateSettings : IAggregateSettings
{
    private readonly IConfiguration _configuration;
    private readonly AggregateSettingHelper _helper;

    public AggregateSettings(IConfiguration configuration, AggregateSettingHelper? helper)
    {
        _configuration = configuration;
        if (helper != null)
        {
            _helper = helper;
        }
    }
    public bool ShouldTakeSnapshotForType(Type originalType) =>
        throw new NotImplementedException();
    public bool CanUseHybrid(Type originalType) =>
        throw new NotImplementedException();
    public int SnapshotFrequencyForType(Type originalType) =>
        throw new NotImplementedException();
    public int SnapshotOffsetForType(Type originalType) =>
        throw new NotImplementedException();
}
