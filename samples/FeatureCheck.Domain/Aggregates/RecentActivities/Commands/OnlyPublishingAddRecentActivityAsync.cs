using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Commands;

public record OnlyPublishingAddRecentActivityAsync(Guid RecentActivityId, string Activity) : IOnlyPublishingCommand<RecentActivity>
{

    public OnlyPublishingAddRecentActivityAsync() : this(Guid.Empty, string.Empty)
    {
    }

    public Guid GetAggregateId() => RecentActivityId;

    public class Handler : IOnlyPublishingCommandHandlerAsync<RecentActivity, OnlyPublishingAddRecentActivityAsync>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        public async IAsyncEnumerable<IEventPayloadApplicableTo<RecentActivity>> HandleCommandAsync(
            Guid aggregateId,
            OnlyPublishingAddRecentActivityAsync command)
        {
            await Task.CompletedTask;
            yield return new RecentActivityAdded(new RecentActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
    }
}