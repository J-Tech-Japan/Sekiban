using Customer.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;

namespace Customer.Domain.Aggregates.RecentActivities.Commands;

public record RecentActivity : ICommand<RecentActivities.RecentActivity>
{
    public Guid GetAggregateId()
    {
        return Guid.NewGuid();
    }

    public class Handler : ICommandHandlerBase<RecentActivities.RecentActivity, RecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;

        public Handler(ISekibanDateProducer sekibanDateProducer)
        {
            _sekibanDateProducer = sekibanDateProducer;
        }

        public async IAsyncEnumerable<IEventPayload<RecentActivities.RecentActivity>> HandleCommandAsync(
            Func<AggregateState<RecentActivities.RecentActivity>> getAggregateState,
            RecentActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentActivityCreated(new RecentActivityRecord("First Event Created",
                _sekibanDateProducer.UtcNow));
        }
    }
}
