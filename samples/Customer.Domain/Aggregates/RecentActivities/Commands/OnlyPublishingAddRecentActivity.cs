using Customer.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentActivities.Commands;

public record OnlyPublishingAddRecentActivity(Guid RecentActivityId, string Activity) : ChangeCommandBase<RecentActivity>,
    IOnlyPublishingCommand
{
    public OnlyPublishingAddRecentActivity() : this(Guid.Empty, string.Empty)
    {
    }
    public override Guid GetAggregateId() => RecentActivityId;
}
public class OnlyPublishingAddRecentActivityHandler : EventPublishOnlyChangeCommandHandlerBase<RecentActivity,
    OnlyPublishingAddRecentActivity>
{
    private readonly ISekibanDateProducer _sekibanDateProducer;

    public OnlyPublishingAddRecentActivityHandler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;
    protected override async IAsyncEnumerable<IChangedEvent<RecentActivity>> ExecCommandAsync(
        Guid aggregateId,
        OnlyPublishingAddRecentActivity command)
    {
        await Task.CompletedTask;
        yield return new RecentActivityAdded(new RecentActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
    }
}
