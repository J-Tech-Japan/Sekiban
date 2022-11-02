using Customer.Domain.Aggregates.RecentActivities.Events;
using Sekiban.Core.Aggregate;
using Sekiban.Core.Command;
using Sekiban.Core.Event;
using Sekiban.Core.Shared;
namespace Customer.Domain.Aggregates.RecentActivities.Commands;

public record CreateRecentActivity : ICreateCommand<RecentActivity>
{
    public Guid GetAggregateId() => Guid.NewGuid();
    public class Handler : CreateCommandHandlerBase<RecentActivity, CreateRecentActivity>
    {
        private readonly ISekibanDateProducer _sekibanDateProducer;
        public Handler(ISekibanDateProducer sekibanDateProducer) => _sekibanDateProducer = sekibanDateProducer;

        protected override async IAsyncEnumerable<IApplicableEvent<RecentActivity>> ExecCreateCommandAsync(
            Func<AggregateState<RecentActivity>> getAggregateState,
            CreateRecentActivity command)
        {
            await Task.CompletedTask;
            yield return new RecentActivityCreated(new RecentActivityRecord("First Event Created", _sekibanDateProducer.UtcNow));
        }
    }
}
