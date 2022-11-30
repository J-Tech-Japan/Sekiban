using Customer.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Commands;

public record CreateRecentInMemoryActivity : ICommandBase<RecentInMemoryActivities.RecentInMemoryActivity>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public class Handler : ICommandHandlerBase<RecentInMemoryActivities.RecentInMemoryActivity, CreateRecentInMemoryActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;
        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        public async IAsyncEnumerable<IEventPayload<RecentInMemoryActivities.RecentInMemoryActivity>> HandleCommandAsync(
            Func<AggregateState<RecentInMemoryActivities.RecentInMemoryActivity>> getAggregateState,
            CreateRecentInMemoryActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentInMemoryActivityCreated(new RecentInMemoryActivityRecord("First Event Created", _sekibanDateProducer.UtcNow));
        }
    }
}
