using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Commands;

public record CreateRecentActivity : ICommand<RecentActivity>
{
    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<RecentActivity, CreateRecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        public IEnumerable<IEventPayloadApplicableTo<RecentActivity>> HandleCommand(
            Func<AggregateState<RecentActivity>> getAggregateState,
            CreateRecentActivity command)
        {
            yield return new RecentActivityCreated(new RecentActivityRecord("First Event Created", _sekibanDateProducer.UtcNow));
        }
    }
}
