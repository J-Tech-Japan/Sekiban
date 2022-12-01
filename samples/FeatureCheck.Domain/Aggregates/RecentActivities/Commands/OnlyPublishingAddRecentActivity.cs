using Customer.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;

namespace Customer.Domain.Aggregates.RecentActivities.Commands;

public record OnlyPublishingAddRecentActivity(Guid RecentActivityId, string Activity) :
    IOnlyPublishingCommand<RecentActivities.RecentActivity>,
    IOnlyPublishingCommandCommon
{
    public OnlyPublishingAddRecentActivity() : this(Guid.Empty, string.Empty)
    {
    }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId()
    {
        return RecentActivityId;
    }

    public class Handler : IOnlyPublishingCommandHandlerBase<RecentActivities.RecentActivity,
        OnlyPublishingAddRecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer)
        {
            _sekibanDateProducer = sekibanDateProducer;
        }

        public async IAsyncEnumerable<IEventPayload<RecentActivities.RecentActivity>> HandleCommandAsync(
            Guid aggregateId, OnlyPublishingAddRecentActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentActivityAdded(
                new RecentActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
    }
}
