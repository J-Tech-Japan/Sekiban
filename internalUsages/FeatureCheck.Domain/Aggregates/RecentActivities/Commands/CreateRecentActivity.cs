using FeatureCheck.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentActivities.Commands;

public record CreateRecentActivity : ICommand<RecentActivity>
{
    public class Handler : ICommandHandler<RecentActivity, CreateRecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        public IEnumerable<IEventPayloadApplicableTo<RecentActivity>> HandleCommand(
            CreateRecentActivity command,
            ICommandContext<RecentActivity> context)
        {
            yield return new RecentActivityCreated(
                new RecentActivityRecord("First Event Created", _sekibanDateProducer.UtcNow));
        }
        public Guid SpecifyAggregateId(CreateRecentActivity command) => Guid.NewGuid();
    }
}
