using FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Command;
using Sekiban.Core.Events;
using Sekiban.Core.Shared;
namespace FeatureCheck.Domain.Aggregates.RecentInMemoryActivities.Commands;

public record CreateRecentInMemoryActivity : ICommand<RecentInMemoryActivity>
{
    public Guid GetAggregateId() => Guid.NewGuid();

    public class Handler : ICommandHandler<RecentInMemoryActivity, CreateRecentInMemoryActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        public IEnumerable<IEventPayloadApplicableTo<RecentInMemoryActivity>> HandleCommand(
            CreateRecentInMemoryActivity command,
            ICommandContext<RecentInMemoryActivity> context)
        {
            yield return new RecentInMemoryActivityCreated(new RecentInMemoryActivityRecord("First Event Created", _sekibanDateProducer.UtcNow));
        }
    }
}
