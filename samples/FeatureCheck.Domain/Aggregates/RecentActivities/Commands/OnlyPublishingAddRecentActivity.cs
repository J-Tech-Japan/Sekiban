using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Commands;

public record OnlyPublishingAddRecentActivity(Guid RecentActivityId, string Activity) :
    IOnlyPublishingCommand<RecentActivity>,
    IOnlyPublishingCommandCommon
{
    public OnlyPublishingAddRecentActivity() : this(Guid.Empty, string.Empty)
    {
    }

    public int ReferenceVersion { get; init; }

    public Guid GetAggregateId() => RecentActivityId;

    public class Handler : IOnlyPublishingCommandHandler<RecentActivity,
        OnlyPublishingAddRecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        public async IAsyncEnumerable<IEventPayload<RecentActivity>> HandleCommandAsync(
            Guid aggregateId,
            OnlyPublishingAddRecentActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentActivityAdded(
                new RecentActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
    }
}
