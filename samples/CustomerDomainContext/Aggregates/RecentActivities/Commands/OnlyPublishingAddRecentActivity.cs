using CustomerDomainContext.Aggregates.RecentActivities.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Shared;
namespace CustomerDomainContext.Aggregates.RecentActivities.Commands;

public record OnlyPublishingAddRecentActivity(Guid RecentActivityId, string Activity) : ChangeAggregateCommandBase<RecentActivity>,
    IOnlyPublishingCommand
{
    public OnlyPublishingAddRecentActivity() : this(Guid.Empty, string.Empty)
    {
    }
    public override Guid GetAggregateId()
    {
        return RecentActivityId;
    }
}
public class OnlyPublishingAddRecentActivityHandler : EventPublishOnlyChangeAggregateCommandHandlerBase<RecentActivity,
    OnlyPublishingAddRecentActivity>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;

    public OnlyPublishingAddRecentActivityHandler(ISekibanDateProducer sekibanDateProducer)
    {
        _sekibanDateProducer = sekibanDateProducer;
    }
    protected override async Task ExecCommandAsync(Guid aggregateId, OnlyPublishingAddRecentActivity command)
    {
        SaveEvent(new RecentActivityAdded(new RecentActivityRecord(command.Activity, _sekibanDateProducer.UtcNow)));
        await Task.CompletedTask;
    }
}
