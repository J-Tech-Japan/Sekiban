using Customer.Domain.Aggregates.RecentInMemoryActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentInMemoryActivities.Commands;

public record RecentInMemoryActivity : ICommandBase<RecentInMemoryActivities.RecentInMemoryActivity>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public class Handler : CreateCommandHandlerBase<RecentInMemoryActivities.RecentInMemoryActivity, RecentInMemoryActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;
        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        protected override async IAsyncEnumerable<IApplicableEvent<RecentInMemoryActivities.RecentInMemoryActivity>> ExecCreateCommandAsync(
            Func<AggregateState<RecentInMemoryActivities.RecentInMemoryActivity>> getAggregateState,
            RecentInMemoryActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentInMemoryActivityCreated(new RecentInMemoryActivityRecord("First Event Created", _sekibanDateProducer.UtcNow));
        }
    }
}
