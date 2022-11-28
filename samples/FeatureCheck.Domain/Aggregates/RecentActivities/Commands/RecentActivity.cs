using Customer.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentActivities.Commands;

public record RecentActivity : ICommandBase<RecentActivities.RecentActivity>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public class Handler : CreateCommandHandlerBase<RecentActivities.RecentActivity, RecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;
        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        protected override async IAsyncEnumerable<IApplicableEvent<RecentActivities.RecentActivity>> ExecCreateCommandAsync(
            Func<AggregateState<RecentActivities.RecentActivity>> getAggregateState,
            RecentActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentActivityCreated(new RecentActivityRecord("First Event Created", _sekibanDateProducer.UtcNow));
        }
    }
}
