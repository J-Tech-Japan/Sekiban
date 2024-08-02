using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Commands;

public record OnlyPublishingAddRecentActivity(Guid RecentActivityId, string Activity)
    : ICommandWithoutLoadingAggregate<RecentActivity>
{

    public int ReferenceVersion { get; init; }
    public OnlyPublishingAddRecentActivity() : this(Guid.Empty, string.Empty)
    {
    }

    public Guid GetAggregateId() => RecentActivityId;

    public class Handler : ICommandWithoutLoadingAggregateHandler<RecentActivity, OnlyPublishingAddRecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        public IEnumerable<IEventPayloadApplicableTo<RecentActivity>> HandleCommand(
            Guid aggregateId,
            OnlyPublishingAddRecentActivity command)
        {
            yield return new RecentActivityAdded(
                new RecentActivityRecord(command.Activity, _sekibanDateProducer.UtcNow));
        }
    }
}
